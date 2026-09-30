using FluentValidation.Results;

using Reservas.Dominio.Comun;
using Reservas.Dominio.Gestion;

namespace Reservas.Api.Errores;

/// <summary>
/// Traduce los errores de negocio a respuestas <c>application/problem+json</c> (RFC 9457). El
/// código del error de negocio, estable, va en la extensión <c>code</c>: los clientes de la API
/// deben basarse en él y no en el texto, que puede cambiar.
/// </summary>
public static class ProblemasApi
{
    private static readonly Dictionary<string, int> Estados = new()
    {
        // No autenticado
        ["auth.credenciales_invalidas"] = StatusCodes.Status401Unauthorized,
        ["auth.token_invalido"] = StatusCodes.Status401Unauthorized,

        // No encontrado
        ["negocio.no_encontrado"] = StatusCodes.Status404NotFound,
        ["reserva.no_encontrada"] = StatusCodes.Status404NotFound,
        ["usuario.no_encontrado"] = StatusCodes.Status404NotFound,
        ["sala.no_encontrada"] = StatusCodes.Status404NotFound,
        ["mesa.no_encontrada"] = StatusCodes.Status404NotFound,
        ["horario.no_encontrado"] = StatusCodes.Status404NotFound,
        ["cierre.no_encontrado"] = StatusCodes.Status404NotFound,

        // Conflicto: la petición era válida, pero el estado actual no la permite
        ["reserva.franja_no_disponible"] = StatusCodes.Status409Conflict,
        ["usuario.email_en_uso"] = StatusCodes.Status409Conflict,
        ["usuario.protegido"] = StatusCodes.Status409Conflict,
        ["reserva.activa"] = StatusCodes.Status409Conflict,
        ["usuario.ya_desactivado"] = StatusCodes.Status409Conflict,
        ["sala.con_mesas"] = StatusCodes.Status409Conflict,
        ["mesa.con_reservas"] = StatusCodes.Status409Conflict,
        [ErroresReserva.MesaOcupada.Codigo] = StatusCodes.Status409Conflict,
        [ErroresReserva.ConflictoConcurrencia.Codigo] = StatusCodes.Status409Conflict,
        [ErroresReserva.TransicionInvalida.Codigo] = StatusCodes.Status409Conflict,
        [ErroresReserva.Caducada.Codigo] = StatusCodes.Status409Conflict,
        [ErroresReserva.AunNoEsHora.Codigo] = StatusCodes.Status409Conflict,
        [ErroresReserva.NoCaducaAun.Codigo] = StatusCodes.Status409Conflict,
    };

    /// <summary>Cualquier otro error de negocio (datos que incumplen una regla) es un 422.</summary>
    public static int EstadoHttp(ErrorDominio error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return Estados.GetValueOrDefault(error.Codigo, StatusCodes.Status422UnprocessableEntity);
    }

    public static IResult Desde(ErrorDominio error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return Crear(EstadoHttp(error), error.Codigo, error.Mensaje);
    }

    public static IResult Crear(int estadoHttp, string codigo, string detalle, IDictionary<string, object?>? extra = null)
    {
        var extensiones = new Dictionary<string, object?> { ["code"] = codigo };
        if (extra is not null)
        {
            foreach (var (clave, valor) in extra)
            {
                extensiones[clave] = valor;
            }
        }

        return Results.Problem(
            title: Titulo(estadoHttp),
            detail: detalle,
            statusCode: estadoHttp,
            type: $"urn:reservas:error:{codigo}",
            extensions: extensiones);
    }

    /// <summary>Errores de validación de la petición, agrupados por campo (en camelCase, como el JSON).</summary>
    public static IResult Validacion(ValidationResult resultado)
    {
        ArgumentNullException.ThrowIfNull(resultado);

        var errores = resultado.Errors
            .GroupBy(error => CamelCase(error.PropertyName))
            .ToDictionary(grupo => grupo.Key, grupo => grupo.Select(error => error.ErrorMessage).ToArray());

        return Results.ValidationProblem(
            errores,
            title: Titulo(StatusCodes.Status422UnprocessableEntity),
            detail: "Hay campos con valores no válidos.",
            statusCode: StatusCodes.Status422UnprocessableEntity,
            type: "urn:reservas:error:validacion.invalida",
            extensions: new Dictionary<string, object?> { ["code"] = "validacion.invalida" });
    }

    private static string Titulo(int estadoHttp) => estadoHttp switch
    {
        StatusCodes.Status400BadRequest => "Petición incorrecta",
        StatusCodes.Status401Unauthorized => "No autenticado",
        StatusCodes.Status404NotFound => "No encontrado",
        StatusCodes.Status409Conflict => "Conflicto",
        StatusCodes.Status422UnprocessableEntity => "Petición no procesable",
        StatusCodes.Status429TooManyRequests => "Demasiadas peticiones",
        _ => "Error",
    };

    /// <summary><c>Cliente.Nombre</c> → <c>cliente.nombre</c>.</summary>
    private static string CamelCase(string propiedad) =>
        string.Join('.', propiedad.Split('.').Select(parte => parte.Length == 0 ? parte : char.ToLowerInvariant(parte[0]) + parte[1..]));
}
