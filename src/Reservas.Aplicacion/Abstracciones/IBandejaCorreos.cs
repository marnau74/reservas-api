using Reservas.Dominio.Correos;

namespace Reservas.Aplicacion.Abstracciones;

/// <summary>La cola de correos pendientes (la «bandeja de salida»).</summary>
public interface IBandejaCorreos
{
    /// <summary>
    /// Toma hasta <paramref name="maximo"/> correos que ya toca enviar y los reserva durante
    /// <paramref name="reserva"/> para que otro proceso no los envíe a la vez. Si el proceso muere
    /// a mitad, pasado ese tiempo vuelven a estar disponibles. Devuelve los correos con seguimiento:
    /// se modifican y se guardan con <see cref="GuardarAsync"/>.
    /// </summary>
    Task<IReadOnlyList<CorreoPendiente>> ReclamarAsync(DateTimeOffset ahora, TimeSpan reserva, int maximo, CancellationToken cancellationToken);

    Task GuardarAsync(CancellationToken cancellationToken);
}
