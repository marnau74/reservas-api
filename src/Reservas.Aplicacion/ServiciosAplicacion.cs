using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Reservas.Aplicacion.Agenda;
using Reservas.Aplicacion.Correos;
using Reservas.Aplicacion.Disponibilidad;
using Reservas.Aplicacion.Local;
using Reservas.Aplicacion.Mantenimiento;
using Reservas.Aplicacion.Negocios;
using Reservas.Aplicacion.Personal;
using Reservas.Aplicacion.ReservasPublicas;

namespace Reservas.Aplicacion;

public static class ServiciosAplicacion
{
    /// <summary>Registra los casos de uso. <paramref name="sesion"/> fija la duración de las sesiones (por defecto, 15 minutos y 14 días).</summary>
    public static IServiceCollection AddAplicacion(
        this IServiceCollection servicios,
        OpcionesSesion? sesion = null,
        OpcionesCorreo? correo = null)
    {
        ArgumentNullException.ThrowIfNull(servicios);

        servicios.TryAddSingleton(correo ?? new OpcionesCorreo());
        servicios.AddScoped<ServicioDisponibilidad>();

        // Público
        servicios.AddScoped<ConsultarNegocio>();
        servicios.AddScoped<ConsultarDisponibilidad>();
        servicios.AddScoped<CrearReserva>();
        servicios.AddScoped<ConsultarReserva>();
        servicios.AddScoped<ConfirmarReserva>();
        servicios.AddScoped<CancelarReserva>();
        servicios.AddScoped<BorrarDatosCliente>();

        // Personal
        servicios.TryAddSingleton(sesion ?? new OpcionesSesion());
        servicios.AddScoped<IniciarSesion>();
        servicios.AddScoped<RenovarSesion>();
        servicios.AddScoped<CerrarSesion>();
        servicios.AddScoped<ServicioUsuarios>();
        servicios.AddScoped<ServicioLocal>();
        servicios.AddScoped<GestionReservasPersonal>();

        // Correos y tareas programadas
        servicios.AddScoped<ProcesarCorreos>();
        servicios.AddScoped<MantenimientoProgramado>();

        return servicios;
    }
}
