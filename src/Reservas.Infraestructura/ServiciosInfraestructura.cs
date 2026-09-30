using Microsoft.Extensions.DependencyInjection;

using Reservas.Aplicacion.Abstracciones;
using Reservas.Infraestructura.Persistencia;

namespace Reservas.Infraestructura;

public static class ServiciosInfraestructura
{
    /// <summary>
    /// Registra las implementaciones de los puertos de la aplicación. El <see cref="ReservasDbContext"/>
    /// lo registra el host (la API, con la integración de Aspire).
    /// </summary>
    public static IServiceCollection AddInfraestructura(this IServiceCollection servicios)
    {
        ArgumentNullException.ThrowIfNull(servicios);

        servicios.AddScoped<IRepositorioReservas, RepositorioReservas>();
        servicios.AddScoped<IRepositorioNegocios, RepositorioNegocios>();
        servicios.AddScoped<IAlmacenIdempotencia, AlmacenIdempotencia>();

        return servicios;
    }
}
