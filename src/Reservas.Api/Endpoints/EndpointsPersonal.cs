using System.Security.Claims;

using FluentValidation;

using Reservas.Api.Contratos;
using Reservas.Api.Errores;
using Reservas.Api.Idempotencia;
using Reservas.Api.Limites;
using Reservas.Api.Seguridad;
using Reservas.Aplicacion;
using Reservas.Aplicacion.Agenda;
using Reservas.Aplicacion.Local;
using Reservas.Aplicacion.Personal;
using Reservas.Dominio.Comun;
using Reservas.Dominio.Locales;
using Reservas.Dominio.Personal;

namespace Reservas.Api.Endpoints;

/// <summary>
/// La parte privada de la API: la sesión del personal y lo que este hace con su negocio. El negocio
/// sale siempre de la sesión, nunca de la URL ni del cuerpo: no hay ningún parámetro con el que
/// pedir los datos de otro.
/// </summary>
public static class EndpointsPersonal
{
    public static IEndpointRouteBuilder MapEndpointsPersonal(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);

        MapAutenticacion(rutas.MapGroup("/api/v1/auth").WithTags("Sesión"));

        var gestion = rutas.MapGroup(Politicas.RutaGestion).RequireAuthorization(Politicas.Personal);

        MapAgenda(gestion.MapGroup(string.Empty).WithTags("Agenda"));
        MapLocal(gestion.MapGroup(string.Empty).RequireAuthorization(Politicas.Encargado).WithTags("Local"));
        MapUsuarios(gestion.MapGroup("/usuarios").RequireAuthorization(Politicas.Propietario).WithTags("Usuarios"));

        return rutas;
    }

    // --- Sesión ---------------------------------------------------------------------------------

    private static void MapAutenticacion(RouteGroupBuilder grupo)
    {
        grupo.MapPost("/login", IniciarSesionAsync)
            .RequireRateLimiting(LimitesPeticiones.Autenticacion)
            .WithName("IniciarSesion")
            .WithSummary("Inicia sesión con correo y contraseña.")
            .Produces<SesionRespuesta>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        grupo.MapPost("/refresh", RenovarSesionAsync)
            .RequireRateLimiting(LimitesPeticiones.Autenticacion)
            .WithName("RenovarSesion")
            .WithSummary("Cambia un token de renovación por una sesión nueva.")
            .WithDescription("Cada token de renovación sirve una sola vez. Si se presenta uno ya usado, se cierran todas las sesiones de esa persona.")
            .Produces<SesionRespuesta>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        grupo.MapPost("/logout", CerrarSesionAsync)
            .RequireRateLimiting(LimitesPeticiones.Autenticacion)
            .WithName("CerrarSesion")
            .WithSummary("Cierra la sesión revocando su token de renovación.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> IniciarSesionAsync(
        LoginDto peticion,
        IValidator<LoginDto> validador,
        IniciarSesion casoDeUso,
        CancellationToken cancellationToken)
    {
        var validacion = await validador.ValidateAsync(peticion, cancellationToken);
        if (!validacion.IsValid)
        {
            return ProblemasApi.Validacion(validacion);
        }

        return RespuestaSesion(await casoDeUso.EjecutarAsync(peticion.Email!, peticion.Contrasena!, cancellationToken));
    }

    private static async Task<IResult> RenovarSesionAsync(
        RefrescoDto peticion,
        IValidator<RefrescoDto> validador,
        RenovarSesion casoDeUso,
        CancellationToken cancellationToken)
    {
        var validacion = await validador.ValidateAsync(peticion, cancellationToken);
        if (!validacion.IsValid)
        {
            return ProblemasApi.Validacion(validacion);
        }

        return RespuestaSesion(await casoDeUso.EjecutarAsync(peticion.TokenRefresco!, cancellationToken));
    }

    private static async Task<IResult> CerrarSesionAsync(
        RefrescoDto peticion,
        IValidator<RefrescoDto> validador,
        CerrarSesion casoDeUso,
        CancellationToken cancellationToken)
    {
        var validacion = await validador.ValidateAsync(peticion, cancellationToken);
        if (!validacion.IsValid)
        {
            return ProblemasApi.Validacion(validacion);
        }

        await casoDeUso.EjecutarAsync(peticion.TokenRefresco!, cancellationToken);

        return Results.NoContent();
    }

    private static IResult RespuestaSesion(Resultado<SesionIniciada> resultado)
    {
        if (resultado.EsFallo)
        {
            return ProblemasApi.Desde(resultado.Error);
        }

        var sesion = resultado.Valor;

        return Results.Ok(new SesionRespuesta(
            sesion.Acceso.Token,
            sesion.Acceso.ExpiraEn,
            sesion.TokenRefresco,
            sesion.RefrescoExpiraEn,
            MapeoGestion.AUsuario(sesion.Usuario)));
    }

    // --- Agenda y reservas (personal) -----------------------------------------------------------

    private static void MapAgenda(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/agenda", ObtenerAgendaAsync)
            .RequireRateLimiting(LimitesPeticiones.Lectura)
            .WithName("ObtenerAgenda")
            .WithSummary("Reservas de un día del negocio, por hora.")
            .Produces<AgendaRespuesta>()
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        grupo.MapPost("/reservas", CrearReservaAsync)
            .RequireRateLimiting(LimitesPeticiones.Escritura)
            .WithMetadata(new RequiereIdempotencia())
            .WithName("CrearReservaPersonal")
            .WithSummary("Apunta una reserva hecha por teléfono o en persona.")
            .WithDescription("Nace confirmada y no le afectan los límites de las reservas por internet. Exige `Idempotency-Key`.")
            .Produces<ReservaGestionRespuesta>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        var reserva = grupo.MapGroup("/reservas/{id:guid}");

        Transicion(reserva, "/cancelar", "CancelarReservaPersonal", "Cancela una reserva y libera su mesa.", (g, id, ct) => g.CancelarAsync(id.Negocio, id.Reserva, ct));
        Transicion(reserva, "/llegada", "SentarReserva", "El grupo ha llegado y se sienta.", (g, id, ct) => g.SentarAsync(id.Negocio, id.Reserva, ct));
        Transicion(reserva, "/completar", "CompletarReserva", "El grupo ha terminado: la mesa queda libre.", (g, id, ct) => g.CompletarAsync(id.Negocio, id.Reserva, ct));
        Transicion(reserva, "/no-presentada", "MarcarNoPresentada", "El grupo no ha aparecido pasado el margen de cortesía.", (g, id, ct) => g.MarcarNoPresentadaAsync(id.Negocio, id.Reserva, ct));
    }

    private readonly record struct Identificadores(Guid Negocio, Guid Reserva);

    private static void Transicion(
        RouteGroupBuilder grupo,
        string ruta,
        string nombre,
        string resumen,
        Func<GestionReservasPersonal, Identificadores, CancellationToken, Task<Resultado<Reservas.Aplicacion.ReservasPublicas.ReservaConNegocio>>> accion)
    {
        grupo.MapPost(
                ruta,
                async (Guid id, ClaimsPrincipal usuario, GestionReservasPersonal gestion, CancellationToken cancellationToken) =>
                {
                    var sesion = SesionActual.Leer(usuario);
                    if (sesion is null)
                    {
                        return SesionInvalida();
                    }

                    var resultado = await accion(gestion, new Identificadores(sesion.NegocioId, id), cancellationToken);

                    return resultado.EsFallo ? ProblemasApi.Desde(resultado.Error) : Results.Ok(MapeoGestion.AReserva(resultado.Valor));
                })
            .RequireRateLimiting(LimitesPeticiones.Escritura)
            .WithName(nombre)
            .WithSummary(resumen)
            .Produces<ReservaGestionRespuesta>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> ObtenerAgendaAsync(
        [AsParameters] ConsultaAgendaDto consulta,
        ClaimsPrincipal usuario,
        IValidator<ConsultaAgendaDto> validador,
        GestionReservasPersonal gestion,
        CancellationToken cancellationToken)
    {
        var validacion = await validador.ValidateAsync(consulta, cancellationToken);
        if (!validacion.IsValid)
        {
            return ProblemasApi.Validacion(validacion);
        }

        var sesion = SesionActual.Leer(usuario);
        if (sesion is null)
        {
            return SesionInvalida();
        }

        _ = Formatos.TryParseFecha(consulta.Fecha, out var fecha);
        var resultado = await gestion.ObtenerAgendaAsync(sesion.NegocioId, fecha, cancellationToken);

        return resultado.EsFallo ? ProblemasApi.Desde(resultado.Error) : Results.Ok(MapeoGestion.AAgenda(resultado.Valor));
    }

    private static async Task<IResult> CrearReservaAsync(
        SolicitudReservaDto solicitud,
        ClaimsPrincipal usuario,
        IValidator<SolicitudReservaDto> validador,
        GestionReservasPersonal gestion,
        HttpContext contexto,
        CancellationToken cancellationToken)
    {
        var validacion = await validador.ValidateAsync(solicitud, cancellationToken);
        if (!validacion.IsValid)
        {
            return ProblemasApi.Validacion(validacion);
        }

        var sesion = SesionActual.Leer(usuario);
        if (sesion is null)
        {
            return SesionInvalida();
        }

        _ = Formatos.TryParseFecha(solicitud.Fecha, out var fecha);
        _ = Formatos.TryParseHora(solicitud.Hora, out var hora);

        var resultado = await gestion.CrearAsync(
            sesion.NegocioId,
            new SolicitudReservaPersonal(
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

        // La respuesta lleva los datos del cliente: su copia para reintentos se borra con ellos.
        contexto.Items[IdempotenciaMiddleware.ClaveReserva] = resultado.Valor.Reserva.Id;

        return Results.Json(MapeoGestion.AReserva(resultado.Valor), statusCode: StatusCodes.Status201Created);
    }

    // --- Local (encargado) ----------------------------------------------------------------------

    private static void MapLocal(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/salas", (ClaimsPrincipal usuario, ServicioLocal local, CancellationToken ct) =>
                ConSesion(usuario, async sesion => Results.Ok((await local.ListarSalasAsync(sesion.NegocioId, ct)).Select(MapeoGestion.ASala))))
            .RequireRateLimiting(LimitesPeticiones.Lectura)
            .WithName("ListarSalas").WithSummary("Salas del negocio.").Produces<SalaRespuesta[]>();

        grupo.MapPost("/salas", CrearSalaAsync)
            .RequireRateLimiting(LimitesPeticiones.Escritura)
            .WithName("CrearSala").WithSummary("Crea una sala.")
            .Produces<SalaRespuesta>(StatusCodes.Status201Created).ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        grupo.MapDelete("/salas/{id:guid}", (Guid id, ClaimsPrincipal usuario, ServicioLocal local, CancellationToken ct) =>
                ConSesion(usuario, async sesion => SinContenido(await local.EliminarSalaAsync(sesion.NegocioId, id, ct))))
            .RequireRateLimiting(LimitesPeticiones.Escritura)
            .WithName("EliminarSala").WithSummary("Borra una sala sin mesas.")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        grupo.MapGet("/mesas", (ClaimsPrincipal usuario, ServicioLocal local, CancellationToken ct) =>
                ConSesion(usuario, async sesion => Results.Ok((await local.ListarMesasAsync(sesion.NegocioId, ct)).Select(MapeoGestion.AMesa))))
            .RequireRateLimiting(LimitesPeticiones.Lectura)
            .WithName("ListarMesas").WithSummary("Mesas del negocio.").Produces<MesaRespuesta[]>();

        grupo.MapPost("/mesas", CrearMesaAsync)
            .RequireRateLimiting(LimitesPeticiones.Escritura)
            .WithName("CrearMesa").WithSummary("Crea una mesa en una sala.")
            .Produces<MesaRespuesta>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        grupo.MapDelete("/mesas/{id:guid}", (Guid id, ClaimsPrincipal usuario, ServicioLocal local, CancellationToken ct) =>
                ConSesion(usuario, async sesion => SinContenido(await local.EliminarMesaAsync(sesion.NegocioId, id, ct))))
            .RequireRateLimiting(LimitesPeticiones.Escritura)
            .WithName("EliminarMesa").WithSummary("Borra una mesa que nunca ha tenido reservas.")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        grupo.MapGet("/horarios", (ClaimsPrincipal usuario, ServicioLocal local, CancellationToken ct) =>
                ConSesion(usuario, async sesion => Results.Ok((await local.ListarHorariosAsync(sesion.NegocioId, ct)).Select(MapeoGestion.AHorario))))
            .RequireRateLimiting(LimitesPeticiones.Lectura)
            .WithName("ListarHorarios").WithSummary("Horarios de reserva del negocio.").Produces<HorarioRespuesta[]>();

        grupo.MapPost("/horarios", CrearHorarioAsync)
            .RequireRateLimiting(LimitesPeticiones.Escritura)
            .WithName("CrearHorario").WithSummary("Añade un horario de reserva.")
            .Produces<HorarioRespuesta>(StatusCodes.Status201Created).ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        grupo.MapDelete("/horarios/{id:guid}", (Guid id, ClaimsPrincipal usuario, ServicioLocal local, CancellationToken ct) =>
                ConSesion(usuario, async sesion => SinContenido(await local.EliminarHorarioAsync(sesion.NegocioId, id, ct))))
            .RequireRateLimiting(LimitesPeticiones.Escritura)
            .WithName("EliminarHorario").WithSummary("Borra un horario.")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);

        grupo.MapGet("/cierres", (ClaimsPrincipal usuario, ServicioLocal local, CancellationToken ct) =>
                ConSesion(usuario, async sesion => Results.Ok((await local.ListarCierresAsync(sesion.NegocioId, ct)).Select(MapeoGestion.ACierre))))
            .RequireRateLimiting(LimitesPeticiones.Lectura)
            .WithName("ListarCierres").WithSummary("Días de cierre del negocio.").Produces<CierreRespuesta[]>();

        grupo.MapPost("/cierres", CrearCierreAsync)
            .RequireRateLimiting(LimitesPeticiones.Escritura)
            .WithName("CrearCierre").WithSummary("Añade un día (o un turno) de cierre.")
            .Produces<CierreRespuesta>(StatusCodes.Status201Created).ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        grupo.MapDelete("/cierres/{id:guid}", (Guid id, ClaimsPrincipal usuario, ServicioLocal local, CancellationToken ct) =>
                ConSesion(usuario, async sesion => SinContenido(await local.EliminarCierreAsync(sesion.NegocioId, id, ct))))
            .RequireRateLimiting(LimitesPeticiones.Escritura)
            .WithName("EliminarCierre").WithSummary("Borra un cierre.")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static Task<IResult> CrearSalaAsync(
        CrearSalaDto peticion, ClaimsPrincipal usuario, IValidator<CrearSalaDto> validador, ServicioLocal local, CancellationToken ct) =>
        ConValidacion(peticion, validador, () => ConSesion(usuario, async sesion =>
        {
            var resultado = await local.CrearSalaAsync(sesion.NegocioId, peticion.Nombre!, ct);
            return Creado(resultado, sala => MapeoGestion.ASala(sala));
        }), ct);

    private static Task<IResult> CrearMesaAsync(
        CrearMesaDto peticion, ClaimsPrincipal usuario, IValidator<CrearMesaDto> validador, ServicioLocal local, CancellationToken ct) =>
        ConValidacion(peticion, validador, () => ConSesion(usuario, async sesion =>
        {
            var resultado = await local.CrearMesaAsync(
                sesion.NegocioId,
                peticion.SalaId!.Value,
                peticion.Nombre!,
                peticion.CapacidadMinima!.Value,
                peticion.CapacidadMaxima!.Value,
                peticion.EsCombinable!.Value,
                ct);

            return Creado(resultado, mesa => MapeoGestion.AMesa(mesa));
        }), ct);

    private static Task<IResult> CrearHorarioAsync(
        CrearHorarioDto peticion, ClaimsPrincipal usuario, IValidator<CrearHorarioDto> validador, ServicioLocal local, CancellationToken ct) =>
        ConValidacion(peticion, validador, () => ConSesion(usuario, async sesion =>
        {
            _ = MapeoGestion.TryParseDia(peticion.Dia, out var dia);
            _ = MapeoGestion.TryParseTurno(peticion.Turno, out var turno);
            _ = Formatos.TryParseHora(peticion.Inicio, out var inicio);
            _ = Formatos.TryParseHora(peticion.Fin, out var fin);

            var resultado = await local.CrearHorarioAsync(sesion.NegocioId, dia, turno, inicio, fin, peticion.IntervaloMinutos!.Value, ct);
            return Creado(resultado, horario => MapeoGestion.AHorario(horario));
        }), ct);

    private static Task<IResult> CrearCierreAsync(
        CrearCierreDto peticion, ClaimsPrincipal usuario, IValidator<CrearCierreDto> validador, ServicioLocal local, CancellationToken ct) =>
        ConValidacion(peticion, validador, () => ConSesion(usuario, async sesion =>
        {
            _ = Formatos.TryParseFecha(peticion.Fecha, out var fecha);
            Turno? turno = MapeoGestion.TryParseTurno(peticion.Turno, out var t) ? t : null;

            var resultado = await local.CrearCierreAsync(sesion.NegocioId, fecha, turno, peticion.Motivo!, ct);
            return Creado(resultado, cierre => MapeoGestion.ACierre(cierre));
        }), ct);

    // --- Usuarios (propietario) -----------------------------------------------------------------

    private static void MapUsuarios(RouteGroupBuilder grupo)
    {
        grupo.MapGet(string.Empty, async (ClaimsPrincipal usuario, ServicioUsuarios servicio, CancellationToken ct) =>
                await ConSesion(usuario, async sesion => Results.Ok((await servicio.ListarAsync(sesion, ct)).Select(MapeoGestion.AUsuario))))
            .RequireRateLimiting(LimitesPeticiones.Lectura)
            .WithName("ListarUsuarios").WithSummary("Personas que trabajan en el negocio.").Produces<UsuarioRespuesta[]>();

        grupo.MapPost(string.Empty, CrearUsuarioAsync)
            .RequireRateLimiting(LimitesPeticiones.Escritura)
            .WithName("CrearUsuario").WithSummary("Da de alta a una persona del personal o un encargado.")
            .Produces<UsuarioRespuesta>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        grupo.MapDelete("/{id:guid}", (Guid id, ClaimsPrincipal usuario, ServicioUsuarios servicio, CancellationToken ct) =>
                ConSesion(usuario, async sesion => SinContenido(await servicio.DesactivarAsync(sesion, id, ct))))
            .RequireRateLimiting(LimitesPeticiones.Escritura)
            .WithName("DesactivarUsuario").WithSummary("Desactiva a una persona y cierra sus sesiones.")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static Task<IResult> CrearUsuarioAsync(
        CrearUsuarioDto peticion, ClaimsPrincipal usuario, IValidator<CrearUsuarioDto> validador, ServicioUsuarios servicio, CancellationToken ct) =>
        ConValidacion(peticion, validador, () => ConSesion(usuario, async sesion =>
        {
            _ = MapeoGestion.TryParseRol(peticion.Rol, out var rol);
            var resultado = await servicio.CrearAsync(sesion, peticion.Email!, peticion.Nombre!, rol, peticion.Contrasena!, ct);

            return Creado(resultado, nuevo => MapeoGestion.AUsuario(nuevo));
        }), ct);

    // --- Utilidades -----------------------------------------------------------------------------

    private static async Task<IResult> ConValidacion<T>(T peticion, IValidator<T> validador, Func<Task<IResult>> siguiente, CancellationToken ct)
    {
        var validacion = await validador.ValidateAsync(peticion, ct);

        return validacion.IsValid ? await siguiente() : ProblemasApi.Validacion(validacion);
    }

    private static async Task<IResult> ConSesion(ClaimsPrincipal usuario, Func<SesionUsuario, Task<IResult>> accion)
    {
        var sesion = SesionActual.Leer(usuario);

        return sesion is null ? SesionInvalida() : await accion(sesion);
    }

    private static IResult SesionInvalida() =>
        ProblemasApi.Crear(StatusCodes.Status401Unauthorized, "auth.token_invalido", "La sesión no es válida o ha caducado. Inicia sesión de nuevo.");

    private static IResult SinContenido(Resultado resultado) =>
        resultado.EsFallo ? ProblemasApi.Desde(resultado.Error) : Results.NoContent();

    private static IResult Creado<TValor, TRespuesta>(Resultado<TValor> resultado, Func<TValor, TRespuesta> mapear) =>
        resultado.EsFallo
            ? ProblemasApi.Desde(resultado.Error)
            : Results.Json(mapear(resultado.Valor), statusCode: StatusCodes.Status201Created);
}
