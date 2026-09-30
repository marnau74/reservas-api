using FluentValidation;

using Microsoft.Extensions.Options;

using Reservas.Api.Contratos;
using Reservas.Api.Errores;
using Reservas.Api.Idempotencia;
using Reservas.Api.Limites;
using Reservas.Aplicacion.Disponibilidad;
using Reservas.Aplicacion.Negocios;
using Reservas.Aplicacion.ReservasPublicas;
using Reservas.Dominio.Comun;

namespace Reservas.Api.Endpoints;

/// <summary>
/// API pública: la que usa el cliente final (web del negocio) para ver disponibilidad, reservar
/// y gestionar su reserva con el código del enlace. No requiere sesión.
/// </summary>
public static class EndpointsPublicos
{
    private const string RutaGestion = "/api/v1/reservas/gestion";

    public static IEndpointRouteBuilder MapEndpointsPublicos(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);

        var api = rutas.MapGroup("/api/v1").WithTags("Público");

        api.MapGet("/negocios/{slug}", ObtenerNegocioAsync)
            .RequireRateLimiting(LimitesPeticiones.Lectura)
            .WithName("ObtenerNegocio")
            .WithSummary("Datos públicos de un negocio.")
            .Produces<NegocioRespuesta>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        api.MapGet("/negocios/{slug}/disponibilidad", ConsultarDisponibilidadAsync)
            .RequireRateLimiting(LimitesPeticiones.Lectura)
            .WithName("ConsultarDisponibilidad")
            .WithSummary("Horas a las que se puede reservar un día para un grupo.")
            .WithDescription("`fecha` es AAAA-MM-DD, en el día local del negocio. Cada franja lleva su hora local (HH:mm).")
            .Produces<DisponibilidadRespuesta>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        api.MapPost("/negocios/{slug}/reservas", CrearReservaAsync)
            .RequireRateLimiting(LimitesPeticiones.Escritura)
            .WithMetadata(new RequiereIdempotencia())
            .WithName("CrearReserva")
            .WithSummary("Crea una reserva pendiente de confirmación.")
            .WithDescription(
                "Exige la cabecera `Idempotency-Key`: una clave única por operación. Si la petición se repite con la misma clave, " +
                "se devuelve la respuesta original (cabecera `Idempotency-Replayed: true`) y no se crea otra reserva.")
            .Produces<ReservaRespuesta>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        var gestion = api.MapGroup("/reservas/gestion/{codigo}");

        gestion.MapGet(string.Empty, ConsultarReservaAsync)
            .RequireRateLimiting(LimitesPeticiones.Lectura)
            .WithName("ConsultarReserva")
            .WithSummary("Consulta una reserva con el código de su enlace.")
            .Produces<ReservaRespuesta>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        gestion.MapPost("/confirmar", ConfirmarReservaAsync)
            .RequireRateLimiting(LimitesPeticiones.Escritura)
            .WithName("ConfirmarReserva")
            .WithSummary("Confirma una reserva pendiente.")
            .Produces<ReservaRespuesta>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        gestion.MapPost("/cancelar", CancelarReservaAsync)
            .RequireRateLimiting(LimitesPeticiones.Escritura)
            .WithName("CancelarReserva")
            .WithSummary("Cancela una reserva pendiente o confirmada y libera su mesa.")
            .Produces<ReservaRespuesta>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return rutas;
    }

    private static async Task<IResult> ObtenerNegocioAsync(string slug, ConsultarNegocio consulta, CancellationToken cancellationToken)
    {
        var resultado = await consulta.EjecutarAsync(slug, cancellationToken);

        return resultado.EsFallo ? ProblemasApi.Desde(resultado.Error) : Results.Ok(Mapeo.ANegocio(resultado.Valor));
    }

    private static async Task<IResult> ConsultarDisponibilidadAsync(
        string slug,
        [AsParameters] ConsultaDisponibilidadDto consulta,
        IValidator<ConsultaDisponibilidadDto> validador,
        ConsultarDisponibilidad casoDeUso,
        CancellationToken cancellationToken)
    {
        var validacion = await validador.ValidateAsync(consulta, cancellationToken);
        if (!validacion.IsValid)
        {
            return ProblemasApi.Validacion(validacion);
        }

        // El validador ya ha comprobado que ambos valores son correctos.
        _ = Formatos.TryParseFecha(consulta.Fecha, out var fecha);
        _ = Formatos.TryParseComensales(consulta.Comensales, out var comensales);

        var resultado = await casoDeUso.EjecutarAsync(new SolicitudDisponibilidad(slug, fecha, comensales), cancellationToken);

        return resultado.EsFallo ? ProblemasApi.Desde(resultado.Error) : Results.Ok(Mapeo.ADisponibilidad(resultado.Valor));
    }

    private static async Task<IResult> CrearReservaAsync(
        string slug,
        SolicitudReservaDto solicitud,
        IValidator<SolicitudReservaDto> validador,
        CrearReserva casoDeUso,
        IOptions<OpcionesPublicas> opciones,
        CancellationToken cancellationToken)
    {
        var validacion = await validador.ValidateAsync(solicitud, cancellationToken);
        if (!validacion.IsValid)
        {
            return ProblemasApi.Validacion(validacion);
        }

        _ = Formatos.TryParseFecha(solicitud.Fecha, out var fecha);
        _ = Formatos.TryParseHora(solicitud.Hora, out var hora);

        var resultado = await casoDeUso.EjecutarAsync(
            new SolicitudCrearReserva(
                slug,
                fecha,
                hora,
                solicitud.Comensales!.Value,
                solicitud.Cliente!.Nombre!,
                solicitud.Cliente.Email!,
                solicitud.Cliente.Telefono),
            cancellationToken);

        if (resultado.EsFallo)
        {
            return ProblemasApi.Desde(resultado.Error);
        }

        var mostrarCodigo = opciones.Value.MostrarCodigoGestion;
        var respuesta = Mapeo.AReserva(resultado.Valor, mostrarCodigo);

        // La ubicación contiene el código secreto: solo se da si también se da en el cuerpo.
        return mostrarCodigo
            ? Results.Created($"{RutaGestion}/{resultado.Valor.Reserva.CodigoGestion}", respuesta)
            : Results.Json(respuesta, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> ConsultarReservaAsync(string codigo, ConsultarReserva casoDeUso, CancellationToken cancellationToken) =>
        Responder(await casoDeUso.EjecutarAsync(codigo, cancellationToken));

    private static async Task<IResult> ConfirmarReservaAsync(string codigo, ConfirmarReserva casoDeUso, CancellationToken cancellationToken) =>
        Responder(await casoDeUso.EjecutarAsync(codigo, cancellationToken));

    private static async Task<IResult> CancelarReservaAsync(string codigo, CancelarReserva casoDeUso, CancellationToken cancellationToken) =>
        Responder(await casoDeUso.EjecutarAsync(codigo, cancellationToken));

    /// <summary>Respuesta de la gestión de una reserva ya existente: nunca repite el código secreto.</summary>
    private static IResult Responder(Resultado<ReservaConNegocio> resultado) =>
        resultado.EsFallo ? ProblemasApi.Desde(resultado.Error) : Results.Ok(Mapeo.AReserva(resultado.Valor, mostrarCodigoGestion: false));
}
