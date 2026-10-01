using OpenTelemetry.Instrumentation.AspNetCore;

namespace Reservas.Api.Seguridad;

/// <summary>
/// El código de gestión de una reserva es un secreto (quien lo tiene puede cancelarla o borrar los datos del cliente) y
/// viaja en la ruta: <c>/api/v1/reservas/gestion/{codigo}</c>. Las trazas guardan la ruta de cada petición
/// (<c>url.path</c>) y se envían a un colector externo, así que en ellas se sustituye el código por <c>{codigo}</c>.
/// </summary>
/// <remarks>
/// Los registros de ASP.NET Core no escriben la ruta de cada petición (el nivel de <c>Microsoft.AspNetCore</c> es
/// <c>Warning</c>). Lo que queda fuera del alcance de la API son los registros del proxy que tenga delante.
/// </remarks>
public static class TrazasSinSecretos
{
    private const string PrefijoGestion = "/api/v1/reservas/gestion/";
    private const string Sustituto = "{codigo}";

    /// <summary>La ruta con el código de gestión sustituido, o <c>null</c> si no lleva ninguno.</summary>
    public static string? Ocultar(PathString ruta)
    {
        var texto = ruta.Value;

        if (texto is null || !texto.StartsWith(PrefijoGestion, StringComparison.OrdinalIgnoreCase) || texto.Length == PrefijoGestion.Length)
        {
            return null;
        }

        var resto = texto[PrefijoGestion.Length..];
        var barra = resto.IndexOf('/', StringComparison.Ordinal);

        return PrefijoGestion + Sustituto + (barra < 0 ? string.Empty : resto[barra..]);
    }

    public static IServiceCollection AddTrazasSinSecretos(this IServiceCollection servicios) =>
        servicios.Configure<AspNetCoreTraceInstrumentationOptions>(opciones =>
        {
            var anterior = opciones.EnrichWithHttpRequest;

            opciones.EnrichWithHttpRequest = (actividad, peticion) =>
            {
                anterior?.Invoke(actividad, peticion);

                if (Ocultar(peticion.Path) is { } ruta)
                {
                    actividad.SetTag("url.path", ruta);
                }
            };
        });
}
