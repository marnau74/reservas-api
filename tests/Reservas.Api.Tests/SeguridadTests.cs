using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

namespace Reservas.Api.Tests;

/// <summary>
/// Redes de seguridad sobre cómo está montada la API, para que un endpoint nuevo no se olvide de
/// pedir sesión: no dependen de que alguien acuerde escribir el test de ese endpoint concreto.
/// </summary>
public class SeguridadTests(ApiConPersonal api) : PruebaPersonal(api), IClassFixture<ApiConPersonal>
{
    private IReadOnlyList<RouteEndpoint> Endpoints() =>
    [
        .. Api.Servicios.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is { } ruta && ruta.StartsWith("/api/", StringComparison.Ordinal)),
    ];

    private static string Ruta(RouteEndpoint endpoint) =>
        $"{string.Join(",", endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods)} {endpoint.RoutePattern.RawText}";

    [Fact]
    public void Todos_los_endpoints_de_la_parte_privada_exigen_estar_autenticados()
    {
        var privados = Endpoints().Where(e => e.RoutePattern.RawText!.StartsWith("/api/v1/gestion", StringComparison.Ordinal)).ToList();

        privados.Count.ShouldBeGreaterThan(15, "se están inspeccionando los endpoints de la gestión");
        privados.Where(e => e.Metadata.GetOrderedMetadata<IAuthorizeData>().Count == 0)
            .Select(Ruta)
            .ShouldBeEmpty("estos endpoints de /gestion no exigen sesión");

        privados.Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(Ruta)
            .ShouldBeEmpty("ningún endpoint de /gestion admite acceso anónimo");
    }

    [Fact]
    public void Solo_la_parte_publica_y_el_inicio_de_sesion_son_accesibles_sin_sesion()
    {
        var abiertos = Endpoints()
            .Where(e => e.Metadata.GetOrderedMetadata<IAuthorizeData>().Count == 0)
            .Select(e => e.RoutePattern.RawText!)
            .ToList();

        abiertos.ShouldAllBe(ruta =>
            ruta.StartsWith("/api/v1/negocios/", StringComparison.Ordinal)
            || ruta.StartsWith("/api/v1/reservas/gestion/", StringComparison.Ordinal)
            || ruta.StartsWith("/api/v1/auth/", StringComparison.Ordinal),
            "una ruta nueva sin protección debe ser una decisión consciente y figurar aquí");
    }

    [Fact]
    public void Los_endpoints_de_configuracion_y_de_usuarios_piden_el_rol_de_encargado_y_los_de_usuarios_el_de_propietario()
    {
        var protegidos = Endpoints().Where(e =>
        {
            var ruta = e.RoutePattern.RawText!;
            return ruta.StartsWith("/api/v1/gestion/usuarios", StringComparison.Ordinal)
                || ruta.StartsWith("/api/v1/gestion/salas", StringComparison.Ordinal)
                || ruta.StartsWith("/api/v1/gestion/mesas", StringComparison.Ordinal)
                || ruta.StartsWith("/api/v1/gestion/horarios", StringComparison.Ordinal)
                || ruta.StartsWith("/api/v1/gestion/cierres", StringComparison.Ordinal);
        }).ToList();

        protegidos.Count.ShouldBe(15);

        foreach (var endpoint in protegidos)
        {
            var politicas = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).ToList();
            var esDeUsuarios = endpoint.RoutePattern.RawText!.StartsWith("/api/v1/gestion/usuarios", StringComparison.Ordinal);

            politicas.ShouldContain(esDeUsuarios ? "propietario" : "encargado", Ruta(endpoint));
        }
    }

    [Fact]
    public void Todos_los_endpoints_tienen_limite_de_peticiones()
    {
        // También los que leen: una lectura de la parte privada con una sesión robada no debe poder repetirse sin freno.
        var sinLimite = Endpoints()
            .Where(e => e.Metadata.GetMetadata<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>() is null)
            .Select(Ruta)
            .ToList();

        sinLimite.ShouldBeEmpty("estos endpoints no tienen límite de peticiones");
    }
}
