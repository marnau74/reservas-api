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

/// <summary>Crea una reserva del público en la franja pedida; queda pendiente de que el cliente la confirme.</summary>
public sealed class CrearReserva(
    IRepositorioNegocios negocios,
    ServicioDisponibilidad disponibilidad,
    IRepositorioReservas reservas,
    TimeProvider reloj)
{
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

        return await new CreadorReservas(disponibilidad, reservas, reloj).CrearAsync(
            negocio, solicitud.Fecha, solicitud.Hora, solicitud.Comensales, cliente.Valor, OrigenReserva.Publica, cancellationToken);
    }
}
