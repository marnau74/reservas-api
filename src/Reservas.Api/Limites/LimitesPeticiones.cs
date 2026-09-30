using System.Globalization;
using System.Threading.RateLimiting;

using Microsoft.AspNetCore.RateLimiting;

using Reservas.Api.Errores;

namespace Reservas.Api.Limites;

/// <summary>Cuántas peticiones deja pasar una política por dirección IP en cada ventana de tiempo.</summary>
public sealed record OpcionesLimite(int Permisos, int VentanaSegundos);

/// <summary>
/// Límite de peticiones por dirección IP, para que una sola procedencia no pueda saturar la API
/// ni recorrer códigos de reserva a fuerza bruta. Las lecturas admiten más peticiones que las
/// escrituras. Los límites se configuran en la sección <c>Limites</c> de la configuración.
/// </summary>
public static class LimitesPeticiones
{
    public const string Lectura = "lectura";
    public const string Escritura = "escritura";

    private static readonly OpcionesLimite LecturaPorDefecto = new(Permisos: 120, VentanaSegundos: 60);
    private static readonly OpcionesLimite EscrituraPorDefecto = new(Permisos: 20, VentanaSegundos: 60);

    public static IServiceCollection AddLimitesPeticiones(this IServiceCollection servicios, IConfiguration configuracion)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(configuracion);

        var lectura = configuracion.GetSection("Limites:Lectura").Get<OpcionesLimite>() ?? LecturaPorDefecto;
        var escritura = configuracion.GetSection("Limites:Escritura").Get<OpcionesLimite>() ?? EscrituraPorDefecto;

        servicios.AddRateLimiter(opciones =>
        {
            opciones.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            opciones.AddPolicy(Lectura, contexto => Particion(contexto, Lectura, lectura));
            opciones.AddPolicy(Escritura, contexto => Particion(contexto, Escritura, escritura));
            opciones.OnRejected = RechazarAsync;
        });

        return servicios;
    }

    private static RateLimitPartition<string> Particion(HttpContext contexto, string politica, OpcionesLimite opciones)
    {
        // Cada dirección IP tiene su propio contador dentro de cada política.
        var ip = contexto.Connection.RemoteIpAddress?.ToString() ?? "desconocida";

        return RateLimitPartition.GetFixedWindowLimiter(
            $"{politica}:{ip}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = opciones.Permisos,
                Window = TimeSpan.FromSeconds(opciones.VentanaSegundos),
                QueueLimit = 0,
            });
    }

    private static async ValueTask RechazarAsync(OnRejectedContext contexto, CancellationToken cancellationToken)
    {
        if (contexto.Lease.TryGetMetadata(MetadataName.RetryAfter, out var espera))
        {
            contexto.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(espera.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        await ProblemasApi.Crear(
                StatusCodes.Status429TooManyRequests,
                "limite.excedido",
                "Has hecho demasiadas peticiones. Espera unos segundos antes de volver a intentarlo.")
            .ExecuteAsync(contexto.HttpContext);
    }
}
