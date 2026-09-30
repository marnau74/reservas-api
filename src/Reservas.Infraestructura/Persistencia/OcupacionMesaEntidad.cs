using NpgsqlTypes;

namespace Reservas.Infraestructura.Persistencia;

/// <summary>
/// Una fila por cada mesa de cada reserva: <c>(reserva, mesa, periodo, activa)</c>. Existe para
/// que PostgreSQL pueda impedir, con una restricción de exclusión, que dos reservas activas
/// ocupen la misma mesa a la vez. Es un detalle de infraestructura: el dominio solo conoce la
/// reserva, y el repositorio mantiene estas filas dentro de la misma transacción.
/// </summary>
public sealed class OcupacionMesaEntidad
{
    public OcupacionMesaEntidad(Guid reservaId, Guid mesaId, Guid negocioId, NpgsqlRange<DateTime> periodo, bool activa)
    {
        ReservaId = reservaId;
        MesaId = mesaId;
        NegocioId = negocioId;
        Periodo = periodo;
        Activa = activa;
    }

    public Guid ReservaId { get; }

    public Guid MesaId { get; }

    public Guid NegocioId { get; }

    public NpgsqlRange<DateTime> Periodo { get; }

    /// <summary>Falsa cuando la reserva se cancela o termina: libera la mesa sin borrar el histórico.</summary>
    public bool Activa { get; set; }
}
