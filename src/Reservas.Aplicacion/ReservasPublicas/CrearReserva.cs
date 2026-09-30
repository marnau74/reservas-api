using Reservas.Aplicacion.Abstracciones;
using Reservas.Aplicacion.Disponibilidad;
using Reservas.Dominio.Comun;
using Reservas.Dominio.Gestion;

namespace Reservas.Aplicacion.ReservasPublicas;

/// <param name="Slug">Identificador público del negocio.</param>
/// <param name="Fecha">Día local de la reserva.</param>
/// <param name="Hora">Hora local a la que se quiere reservar (una de las que ofrece la disponibilidad).</param>
/// <param name="Comensales">Tamaño del grupo.</param>
/// <param name="Nombre">Nombre del cliente.</param>
/// <param name="Email">Correo del cliente.</param>
/// <param name="Telefono">Teléfono del cliente, opcional.</param>
public sealed record SolicitudCrearReserva(
    string Slug,
    DateOnly Fecha,
    TimeOnly Hora,
    int Comensales,
    string Nombre,
    string Email,
    string? Telefono);

/// <summary>
/// Crea una reserva del público en la franja pedida. Si mientras tanto otra petición se queda con
/// la mesa elegida, vuelve a calcular la disponibilidad y prueba con otra mesa libre a esa hora
/// antes de rendirse: dos peticiones simultáneas para la misma hora no deberían fallar si hay
/// mesa para las dos.
/// </summary>
public sealed class CrearReserva(
    IRepositorioNegocios negocios,
    ServicioDisponibilidad disponibilidad,
    IRepositorioReservas reservas,
    TimeProvider reloj)
{
    private const int MaximoIntentos = 3;

    public async Task<Resultado<ReservaConNegocio>> EjecutarAsync(SolicitudCrearReserva solicitud, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(solicitud);

        var negocio = await negocios.ObtenerPorSlugAsync(solicitud.Slug, cancellationToken);
        if (negocio is null)
        {
            return Resultado.Fallo<ReservaConNegocio>(ErroresAplicacion.NegocioNoEncontrado);
        }

        var cliente = DatosCliente.Crear(solicitud.Nombre, solicitud.Email, solicitud.Telefono);
        if (cliente.EsFallo)
        {
            return Resultado.Fallo<ReservaConNegocio>(cliente.Error);
        }

        for (var intento = 1; intento <= MaximoIntentos; intento++)
        {
            var ahora = reloj.GetUtcNow();
            var franjas = await disponibilidad.CalcularAsync(
                negocio, solicitud.Fecha, solicitud.Comensales, OrigenReserva.Publica, ahora, cancellationToken);

            if (franjas.EsFallo)
            {
                return Resultado.Fallo<ReservaConNegocio>(franjas.Error);
            }

            var franja = franjas.Valor.FirstOrDefault(f => f.HoraLocal == solicitud.Hora);
            if (franja is null)
            {
                return Resultado.Fallo<ReservaConNegocio>(ErroresAplicacion.FranjaNoDisponible);
            }

            var reserva = Reserva.Crear(
                negocio.Id, franja.Intervalo, solicitud.Comensales, cliente.Valor, franja.MesaIds, OrigenReserva.Publica, ahora);

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
