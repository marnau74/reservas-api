using System.Net;

using Microsoft.AspNetCore.Mvc.Testing;

using Shouldly;

namespace Reservas.Api.Tests;

public class SaludTests(WebApplicationFactory<Program> fabrica) : IClassFixture<WebApplicationFactory<Program>>
{
    [Theory]
    [InlineData("/health")]
    [InlineData("/alive")]
    public async Task Los_health_checks_responden(string ruta)
    {
        using var cliente = fabrica.CreateClient();

        var respuesta = await cliente.GetAsync(new Uri(ruta, UriKind.Relative), TestContext.Current.CancellationToken);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Una_ruta_inexistente_devuelve_problem_details()
    {
        using var cliente = fabrica.CreateClient();

        var respuesta = await cliente.GetAsync(new Uri("/no-existe", UriKind.Relative), TestContext.Current.CancellationToken);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        respuesta.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }
}
