using System.Text.Json.Serialization;

namespace Reservas.Api.Contratos;

// --- Sesión ---------------------------------------------------------------------------------

public sealed record LoginDto(string? Email, string? Contrasena);

public sealed record RefrescoDto(string? TokenRefresco);

public sealed record UsuarioRespuesta(Guid Id, string Email, string Nombre, string Rol, bool Activo);

/// <param name="TokenAcceso">Se envía en cada petición: <c>Authorization: Bearer …</c>. Dura pocos minutos.</param>
/// <param name="TokenRefresco">Sirve una sola vez para pedir una sesión nueva en <c>/auth/refresh</c>.</param>
public sealed record SesionRespuesta(
    string TokenAcceso,
    DateTimeOffset AccesoExpiraEn,
    string TokenRefresco,
    DateTimeOffset RefrescoExpiraEn,
    UsuarioRespuesta Usuario);

// --- Usuarios -------------------------------------------------------------------------------

/// <param name="Rol">«personal» o «encargado».</param>
public sealed record CrearUsuarioDto(string? Email, string? Nombre, string? Rol, string? Contrasena);

// --- Agenda y reservas ----------------------------------------------------------------------

public sealed record ConsultaAgendaDto(string? Fecha);

public sealed record ClienteGestionRespuesta(string Nombre, string Email, string? Telefono);

/// <param name="Estado">pendiente, confirmada, sentada, completada, cancelada o noPresentada.</param>
/// <param name="Hora">Hora local del negocio (HH:mm).</param>
/// <param name="MesaIds">Mesas que ocupa; sus nombres están en <c>/gestion/mesas</c>.</param>
public sealed record ReservaGestionRespuesta(
    Guid Id,
    string Estado,
    string Fecha,
    string Hora,
    DateTimeOffset Inicio,
    DateTimeOffset Fin,
    int Comensales,
    ClienteGestionRespuesta Cliente,
    IReadOnlyList<Guid> MesaIds);

public sealed record AgendaRespuesta(string Fecha, IReadOnlyList<ReservaGestionRespuesta> Reservas);

// --- Local ----------------------------------------------------------------------------------

public sealed record CrearSalaDto(string? Nombre);

public sealed record SalaRespuesta(Guid Id, string Nombre);

public sealed record CrearMesaDto(Guid? SalaId, string? Nombre, int? CapacidadMinima, int? CapacidadMaxima, bool? EsCombinable);

public sealed record MesaRespuesta(Guid Id, Guid SalaId, string Nombre, int CapacidadMinima, int CapacidadMaxima, bool EsCombinable);

/// <param name="Dia">lunes, martes, miercoles, jueves, viernes, sabado o domingo.</param>
/// <param name="Turno">comida o cena.</param>
/// <param name="Inicio">Primera hora a la que se puede sentar un grupo (HH:mm).</param>
/// <param name="Fin">Última hora a la que se puede sentar un grupo (HH:mm).</param>
public sealed record CrearHorarioDto(string? Dia, string? Turno, string? Inicio, string? Fin, int? IntervaloMinutos);

public sealed record HorarioRespuesta(Guid Id, string Dia, string Turno, string Inicio, string Fin, int IntervaloMinutos);

/// <param name="Turno">comida o cena; sin turno, cierra todo el día.</param>
public sealed record CrearCierreDto(string? Fecha, string? Turno, string? Motivo);

public sealed record CierreRespuesta(
    Guid Id,
    string Fecha,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Turno,
    string Motivo);
