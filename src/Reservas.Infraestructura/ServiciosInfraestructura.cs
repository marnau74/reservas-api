using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Reservas.Aplicacion.Abstracciones;
using Reservas.Infraestructura.Correo;
using Reservas.Infraestructura.Persistencia;

namespace Reservas.Infraestructura;

public static class ServiciosInfraestructura
{
    /// <summary>
    /// Registra las implementaciones de los puertos de la aplicación. El <see cref="ReservasDbContext"/>
    /// lo registra el host (la API, con la integración de Aspire). El envío de correo se configura con
    /// la sección <c>Correo</c>: sin servidor, los correos solo se anotan en el registro.
    /// </summary>
    public static IServiceCollection AddInfraestructura(this IServiceCollection servicios, IConfiguration configuracion)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(configuracion);

        servicios.AddScoped<IRepositorioReservas, RepositorioReservas>();
        servicios.AddScoped<IRepositorioNegocios, RepositorioNegocios>();
        servicios.AddScoped<IAlmacenIdempotencia, AlmacenIdempotencia>();
        servicios.AddScoped<IRepositorioUsuarios, RepositorioUsuarios>();
        servicios.AddScoped<IRepositorioTokensRefresco, RepositorioTokensRefresco>();
        servicios.AddScoped<IRepositorioLocal, RepositorioLocal>();
        servicios.AddScoped<IBandejaCorreos, BandejaCorreos>();
        servicios.AddScoped<IRepositorioMantenimiento, RepositorioMantenimiento>();
        servicios.AddSingleton<IHasherContrasenas, HasherContrasenas>();

        var smtp = configuracion.GetSection(OpcionesSmtp.Seccion).Get<OpcionesSmtp>() ?? new OpcionesSmtp();

        if (string.IsNullOrWhiteSpace(smtp.Servidor))
        {
            servicios.AddSingleton<IEnviadorCorreo, EnviadorCorreoRegistro>();
        }
        else
        {
            servicios.AddSingleton(smtp);
            servicios.AddSingleton<IEnviadorCorreo, EnviadorSmtp>();
        }

        return servicios;
    }
}
