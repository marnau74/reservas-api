using Microsoft.Extensions.DependencyInjection;

using Reservas.Aplicacion.Disponibilidad;
using Reservas.Aplicacion.Negocios;
using Reservas.Aplicacion.ReservasPublicas;

namespace Reservas.Aplicacion;

public static class ServiciosAplicacion
{
    /// <summary>Registra los casos de uso. Necesitan que el host registre las implementaciones de los puertos y un <see cref="TimeProvider"/>.</summary>
    public static IServiceCollection AddAplicacion(this IServiceCollection servicios)
    {
        ArgumentNullException.ThrowIfNull(servicios);

        servicios.AddScoped<ServicioDisponibilidad>();
        servicios.AddScoped<ConsultarNegocio>();
        servicios.AddScoped<ConsultarDisponibilidad>();
        servicios.AddScoped<CrearReserva>();
        servicios.AddScoped<ConsultarReserva>();
        servicios.AddScoped<ConfirmarReserva>();
        servicios.AddScoped<CancelarReserva>();

        return servicios;
    }
}
