using Reservas.Aplicacion.Abstracciones;
using Reservas.Aplicacion.Disponibilidad;
using Reservas.Dominio.Comun;
using Reservas.Dominio.Gestion;
using Reservas.Dominio.Negocios;

namespace Reservas.Aplicacion.ReservasPublicas;

/// <summary>
/// Crea una reserva en la franja pedida. Lo comparten el público (que espera confirmación) y el
/// personal (cuya reserva nace confirmada). Si mientras tanto otra petición se queda con la mesa
/// elegida, vuelve a calcular la disponibilidad y prueba con otra mesa libre a esa hora antes de
/// rendirse: dos peticiones simultáneas para la misma hora no deberían fallar si hay mesa para las dos.
/// </summary>
internal sealed class CreadorReservas(ServicioDisponibilidad disponibilidad, IRepositorioReservas reservas, TimeProvider reloj)
{
    private const int MaximoIntentos = 3;

    public async Task<Resultado<ReservaConNegocio>> CrearAsync(
        Negocio negocio,
        DateOnly fecha,
        TimeOnly hora,
        int comensales,
        DatosCliente cliente,
        OrigenReserva origen,
        CancellationToken cancellationToken)
    {
        for (var intento = 1; intento <= MaximoIntentos; intento++)
        {
            var ahora = reloj.GetUtcNow();
            var franjas = await disponibilidad.CalcularAsync(negocio, fecha, comensales, origen, ahora, cancellationToken);

            if (franjas.EsFallo)
            {
                return Resultado.Fallo<ReservaConNegocio>(franjas.Error);
            }

            var franja = franjas.Valor.FirstOrDefault(f => f.HoraLocal == hora);
            if (franja is null)
            {
                return Resultado.Fallo<ReservaConNegocio>(ErroresAplicacion.FranjaNoDisponible);
            }

            var reserva = Reserva.Crear(negocio.Id, franja.Intervalo, comensales, cliente, franja.MesaIds, origen, ahora);

            if (reserva.EsFallo)
            {
                return Resultado.Fallo<ReservaConNegocio>(reserva.Error);
            }

            var guardada = await reservas.AgregarAsync(reserva.Valor, cancellationToken);

            if (guardada.EsExito)
            {
                return Resultado.Exito(new ReservaConNegocio(reserva.Valor, negocio));
            }

            if (guardada.Error != ErroresReserva.MesaOcupada)
            {
                return Resultado.Fallo<ReservaConNegocio>(guardada.Error);
            }

            // Otra petición se quedó con la mesa entre el cálculo y el guardado: se recalcula.
        }

        return Resultado.Fallo<ReservaConNegocio>(ErroresAplicacion.FranjaNoDisponible);
    }
}
