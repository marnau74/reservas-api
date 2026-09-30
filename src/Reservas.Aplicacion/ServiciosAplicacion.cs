using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Reservas.Aplicacion.Agenda;
using Reservas.Aplicacion.Disponibilidad;
using Reservas.Aplicacion.Local;
using Reservas.Aplicacion.Negocios;
using Reservas.Aplicacion.Personal;
using Reservas.Aplicacion.ReservasPublicas;

namespace Reservas.Aplicacion;

public static class ServiciosAplicacion
{
    /// <summary>Registra los casos de uso. <paramref name="sesion"/> fija la duración de las sesiones (por defecto, 15 minutos y 14 días).</summary>
    public static IServiceCollection AddAplicacion(this IServiceCollection servicios, OpcionesSesion? sesion = null)
    {
        ArgumentNullException.ThrowIfNull(servicios);

        servicios.AddScoped<ServicioDisponibilidad>();

        // Público
        servicios.AddScoped<ConsultarNegocio>();
        servicios.AddScoped<ConsultarDisponibilidad>();
        servicios.AddScoped<CrearReserva>();
        servicios.AddScoped<ConsultarReserva>();
        servicios.AddScoped<ConfirmarReserva>();
        servicios.AddScoped<CancelarReserva>();

        // Personal
        servicios.TryAddSingleton(sesion ?? new OpcionesSesion());
        servicios.AddScoped<IniciarSesion>();
        servicios.AddScoped<RenovarSesion>();
        servicios.AddScoped<CerrarSesion>();
        servicios.AddScoped<ServicioUsuarios>();
        servicios.AddScoped<ServicioLocal>();
        servicios.AddScoped<GestionReservasPersonal>();

        return servicios;
    }
}
