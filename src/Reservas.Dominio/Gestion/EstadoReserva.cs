namespace Reservas.Dominio.Gestion;

/// <summary>
/// Ciclo de vida de una reserva:
/// <code>
/// Pendiente ─► Confirmada ─► Sentada ─► Completada
///     │            ├────────► NoPresentada
///     └────────────┴────────► Cancelada
/// </code>
/// </summary>
public enum EstadoReserva
{
    /// <summary>Hecha por el público, a la espera de que el cliente la confirme.</summary>
    Pendiente = 1,

    Confirmada = 2,

    /// <summary>El grupo ha llegado y está en la mesa.</summary>
    Sentada = 3,

    Completada = 4,

    Cancelada = 5,

    /// <summary>El grupo no ha aparecido pasado el margen de cortesía.</summary>
    NoPresentada = 6,
}
