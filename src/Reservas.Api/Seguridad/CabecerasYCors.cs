namespace Reservas.Api.Seguridad;

/// <summary>
/// Defensas del navegador y de los intermediarios: qué páginas web pueden llamar a la API (CORS) y
/// cabeceras que evitan usos indebidos de las respuestas.
/// </summary>
public static class CabecerasYCors
{
    public const string PoliticaCors = "web-del-negocio";

    /// <summary>
    /// CORS restringido a los orígenes de la sección <c>Cors:Origenes</c>. Sin ninguno configurado,
    /// ninguna otra web puede llamar a la API desde un navegador: no hay «*» por defecto.
    /// </summary>
    public static IServiceCollection AddCorsRestringido(this IServiceCollection servicios, IConfiguration configuracion)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(configuracion);

        var origenes = configuracion.GetSection("Cors:Origenes").Get<string[]>() ?? [];

        foreach (var origen in origenes)
        {
            if (!Uri.TryCreate(origen, UriKind.Absolute, out var uri) || uri.PathAndQuery != "/" || origen.EndsWith('/'))
            {
                throw new InvalidOperationException($"«Cors:Origenes» debe contener orígenes como https://mi-bar.example, sin ruta ni barra final: «{origen}».");
            }
        }

        if (origenes.Length > 0)
        {
            servicios.AddCors(opciones => opciones.AddPolicy(PoliticaCors, politica => politica
                .WithOrigins(origenes)
                .WithMethods("GET", "POST", "DELETE")
                .WithHeaders("Content-Type", "Authorization", "Idempotency-Key")
                .WithExposedHeaders("Idempotency-Replayed", "Retry-After")
                .SetPreflightMaxAge(TimeSpan.FromHours(1))));
        }

        return servicios;
    }

    /// <summary>Cabeceras de seguridad en todas las respuestas. La API solo devuelve JSON: no hay nada que un navegador deba interpretar.</summary>
    public static IApplicationBuilder UseCabecerasDeSeguridad(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use((contexto, siguiente) =>
        {
            contexto.Response.OnStarting(() =>
            {
                var cabeceras = contexto.Response.Headers;
                cabeceras["X-Content-Type-Options"] = "nosniff";
                cabeceras["Referrer-Policy"] = "no-referrer";
                cabeceras["X-Frame-Options"] = "DENY";
                cabeceras["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";

                // Las respuestas de la API llevan datos de personas (o un código secreto): que ninguna
                // caché intermedia las guarde.
                if (contexto.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
                {
                    cabeceras.CacheControl = "no-store";
                }

                return Task.CompletedTask;
            });

            return siguiente(contexto);
        });
    }
}
