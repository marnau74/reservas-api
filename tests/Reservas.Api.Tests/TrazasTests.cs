using System.Collections.Concurrent;
using System.Diagnostics;

using Microsoft.AspNetCore.Http;

using Reservas.Api.Seguridad;

using Shouldly;

namespace Reservas.Api.Tests;

/// <summary>El código de gestión es un secreto que va en la ruta: no puede acabar en las trazas que se envían fuera.</summary>
public class TrazasTests(ApiConBaseDeDatos api) : PruebaApi(api), IClassFixture<ApiConBaseDeDatos>
{
    [Theory]
    [InlineData("/api/v1/reservas/gestion/AbCdEfGhIjKlMnOpQrStUv", "/api/v1/reservas/gestion/{codigo}")]
    [InlineData("/api/v1/reservas/gestion/AbCdEfGhIjKlMnOpQrStUv/cancelar", "/api/v1/reservas/gestion/{codigo}/cancelar")]
    [InlineData("/API/V1/RESERVAS/GESTION/x/confirmar", "/api/v1/reservas/gestion/{codigo}/confirmar")]
    public void El_codigo_de_la_ruta_se_sustituye(string ruta, string oculta)
    {
        TrazasSinSecretos.Ocultar(new PathString(ruta)).ShouldBe(oculta);
    }

    [Theory]
    [InlineData("/api/v1/negocios/bar-la-plaza")]
    [InlineData("/api/v1/reservas/gestion/")]
    [InlineData("/health")]
    public void Una_ruta_sin_codigo_no_se_toca(string ruta)
    {
        TrazasSinSecretos.Ocultar(new PathString(ruta)).ShouldBeNull();
    }

    [Fact]
    public async Task La_traza_de_una_peticion_de_gestion_no_lleva_el_codigo()
    {
        const string Codigo = "CodigoSecretoDePrueba01";
        var rutas = new ConcurrentBag<string>();

        using var escucha = new ActivityListener
        {
            ShouldListenTo = fuente => fuente.Name == "Microsoft.AspNetCore",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = actividad =>
            {
                foreach (var etiqueta in actividad.TagObjects)
                {
                    if (etiqueta.Value is string valor)
                    {
                        rutas.Add(valor);
                    }
                }

                rutas.Add(actividad.DisplayName);
            },
        };
        ActivitySource.AddActivityListener(escucha);

        using var respuesta = await Api.CrearCliente().GetAsync($"/api/v1/reservas/gestion/{Codigo}", Cancelacion);

        // La traza se cierra justo después de enviar la respuesta: se espera un momento a que llegue.
        var limite = DateTime.UtcNow.AddSeconds(5);
        while (!rutas.Contains("/api/v1/reservas/gestion/{codigo}") && DateTime.UtcNow < limite)
        {
            await Task.Delay(20, Cancelacion);
        }

        rutas.ShouldContain("/api/v1/reservas/gestion/{codigo}", "url.path con el código sustituido");
        rutas.ShouldNotContain(valor => valor.Contains(Codigo, StringComparison.Ordinal));
    }
}
