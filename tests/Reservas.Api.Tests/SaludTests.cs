using System.Net;

using Shouldly;

namespace Reservas.Api.Tests;

public class SaludTests(ApiConBaseDeDatos api) : IClassFixture<ApiConBaseDeDatos>
{
    [Theory]
    [InlineData("/health")]
    [InlineData("/alive")]
    public async Task Los_health_checks_responden(string ruta)
    {
        using var cliente = api.CrearCliente();

        var respuesta = await cliente.GetAsync(new Uri(ruta, UriKind.Relative), TestContext.Current.CancellationToken);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task El_health_check_incluye_la_base_de_datos()
    {
        using var cliente = api.CrearCliente();

        var respuesta = await cliente.GetStringAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        respuesta.ShouldBe("Healthy");
    }

    [Fact]
    public async Task Una_ruta_inexistente_devuelve_problem_details()
    {
        using var cliente = api.CrearCliente();

        var respuesta = await cliente.GetAsync(new Uri("/no-existe", UriKind.Relative), TestContext.Current.CancellationToken);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        respuesta.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }
}
