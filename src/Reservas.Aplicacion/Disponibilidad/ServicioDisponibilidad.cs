using Reservas.Aplicacion.Abstracciones;
using Reservas.Dominio.Comun;
using Reservas.Dominio.Disponibilidad;
using Reservas.Dominio.Gestion;
using Reservas.Dominio.Negocios;

namespace Reservas.Aplicacion.Disponibilidad;

/// <summary>
/// Reúne los datos de un negocio (local, horarios, cierres y mesas ya ocupadas) y aplica el
/// cálculo de disponibilidad del dominio. Lo comparten la consulta de disponibilidad y la
/// creación de reservas, para que las dos vean exactamente lo mismo.
/// </summary>
public sealed class ServicioDisponibilidad(IRepositorioNegocios negocios, IRepositorioReservas reservas)
{
    public async Task<Resultado<IReadOnlyList<Franja>>> CalcularAsync(
        Negocio negocio,
        DateOnly fecha,
        int comensales,
        OrigenReserva origen,
        DateTimeOffset ahora,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(negocio);

        var local = await negocios.ObtenerConfiguracionAsync(negocio.Id, cancellationToken);

        // Ventana amplia alrededor del día: es mejor traer alguna ocupación de más que
        // perder una por un cambio de hora o una reserva que cruza la medianoche.
        var mediaNoche = new DateTimeOffset(fecha.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var ventana = new IntervaloTiempo(mediaNoche.AddDays(-1), mediaNoche.AddDays(2));
        var ocupaciones = await reservas.ObtenerOcupacionesAsync(negocio.Id, ventana, cancellationToken);

        var datos = new DatosDisponibilidad(negocio, local.Mesas, local.Horarios, local.Cierres, ocupaciones);

        return CalculadoraDisponibilidad.Calcular(datos, fecha, comensales, origen, ahora);
    }
}
