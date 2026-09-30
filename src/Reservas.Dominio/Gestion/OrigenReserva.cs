namespace Reservas.Dominio.Gestion;

/// <summary>Quién hace la reserva, porque cambian las reglas que se le aplican.</summary>
public enum OrigenReserva
{
    /// <summary>El cliente, desde la web: sujeta a las políticas del negocio y con confirmación.</summary>
    Publica = 1,

    /// <summary>El personal del local (teléfono, en persona): sin límites de antelación y ya confirmada.</summary>
    Personal = 2,
}
