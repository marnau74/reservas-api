using System.Text.Json.Serialization;

namespace Reservas.Api.Contratos;

// --- Peticiones ----------------------------------------------------------------------------
// Todos los campos son opcionales en el contrato JSON para poder decir «falta el campo X» con un
// error de validación claro en lugar de un fallo genérico de deserialización.

/// <summary>Datos para reservar. <c>fecha</c> es AAAA-MM-DD y <c>hora</c> es HH:mm, en hora local del negocio.</summary>
public sealed record SolicitudReservaDto(string? Fecha, string? Hora, int? Comensales, ClienteDto? Cliente);

public sealed record ClienteDto(string? Nombre, string? Email, string? Telefono);

/// <summary>Parámetros de la consulta de disponibilidad: <c>fecha</c> (AAAA-MM-DD) y <c>comensales</c>.</summary>
public sealed record ConsultaDisponibilidadDto(string? Fecha, string? Comensales);

// --- Respuestas ----------------------------------------------------------------------------

public sealed record NegocioRespuesta(
    string Slug,
    string Nombre,
    string ZonaHoraria,
    int MaxComensalesOnline,
    int DiasMaximosAntelacion);

/// <param name="Hora">Hora local del negocio (HH:mm), la que ve el cliente.</param>
/// <param name="Turno">«comida» o «cena».</param>
/// <param name="Inicio">El mismo instante en UTC.</param>
public sealed record FranjaRespuesta(string Hora, string Turno, DateTimeOffset Inicio);

public sealed record DisponibilidadRespuesta(string Negocio, string Fecha, int Comensales, IReadOnlyList<FranjaRespuesta> Franjas);

public sealed record ClienteRespuesta(string Nombre, string Email);

/// <param name="CodigoGestion">
/// Código secreto del enlace de gestión. Solo se incluye al crear la reserva y mientras el correo
/// de confirmación no esté activo (ver <c>Publico:MostrarCodigoGestion</c>).
/// </param>
/// <param name="Estado">pendiente, confirmada, sentada, completada, cancelada o noPresentada.</param>
/// <param name="CaducaEn">Hasta cuándo se puede confirmar una reserva pendiente.</param>
public sealed record ReservaRespuesta(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CodigoGestion,
    string Estado,
    string Negocio,
    string Fecha,
    string Hora,
    DateTimeOffset Inicio,
    DateTimeOffset Fin,
    int Comensales,
    ClienteRespuesta Cliente,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? CaducaEn);
