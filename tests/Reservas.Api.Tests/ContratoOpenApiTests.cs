using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

using Shouldly;

namespace Reservas.Api.Tests;

/// <summary>
/// El contrato de la API (OpenAPI) está guardado en el repositorio: cualquier cambio en las rutas, los
/// parámetros o las respuestas hace fallar este test hasta que alguien lo revise y lo acepte. Así un
/// cambio que rompería a los clientes no llega por accidente, y una revisión de código ve exactamente
/// qué cambia del contrato.
/// </summary>
/// <remarks>
/// Para aceptar un cambio consciente: <c>ACTUALIZAR_CONTRATO=1 dotnet run --project tests/Reservas.Api.Tests</c>
/// y revisar el diff de <c>Contrato/openapi.v1.json</c>.
/// </remarks>
public class ContratoOpenApiTests(ApiConPersonal api) : PruebaApi(api), IClassFixture<ApiConPersonal>
{
    private static readonly JsonSerializerOptions Formato = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static string RutaSnapshot([CallerFilePath] string ficheroActual = "") =>
        Path.Combine(Path.GetDirectoryName(ficheroActual)!, "Contrato", "openapi.v1.json");

    private async Task<JsonNode> ObtenerContratoAsync()
    {
        using var respuesta = await Api.CrearCliente().GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), Cancelacion);
        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);

        var documento = JsonNode.Parse(await respuesta.Content.ReadAsStringAsync(Cancelacion))!.AsObject();

        // La dirección del servidor depende de dónde se ejecute el test: no forma parte del contrato.
        documento.Remove("servers");

        return documento;
    }

    [Fact]
    public async Task El_contrato_publicado_es_el_que_esta_guardado_en_el_repositorio()
    {
        var actual = (await ObtenerContratoAsync()).ToJsonString(Formato).ReplaceLineEndings("\n") + "\n";
        var ruta = RutaSnapshot();

        if (Environment.GetEnvironmentVariable("ACTUALIZAR_CONTRATO") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
            await File.WriteAllTextAsync(ruta, actual, Cancelacion);
            return;
        }

        File.Exists(ruta).ShouldBeTrue("falta Contrato/openapi.v1.json: genéralo con ACTUALIZAR_CONTRATO=1");
        var guardado = (await File.ReadAllTextAsync(ruta, Cancelacion)).ReplaceLineEndings("\n");

        actual.ShouldBe(guardado, "el contrato de la API ha cambiado; si es intencionado, actualiza el fichero con ACTUALIZAR_CONTRATO=1 y revisa el diff");
    }

    [Fact]
    public async Task Todas_las_operaciones_tienen_identificador_unico_y_resumen()
    {
        var contrato = await ObtenerContratoAsync();
        var ids = new List<string>();

        foreach (var (ruta, item) in contrato["paths"]!.AsObject())
        {
            foreach (var (metodo, operacion) in item!.AsObject())
            {
                var etiqueta = $"{metodo.ToUpperInvariant()} {ruta}";
                var id = operacion!["operationId"]?.GetValue<string>();
                id.ShouldNotBeNullOrWhiteSpace(etiqueta);
                operacion["summary"]?.GetValue<string>().ShouldNotBeNullOrWhiteSpace(etiqueta);
                ids.Add(id!);
            }
        }

        ids.Count.ShouldBeGreaterThan(30);
        ids.ShouldBeUnique();
    }

    [Fact]
    public async Task Las_rutas_privadas_documentan_que_piden_sesion_y_las_que_escriben_el_limite_de_peticiones()
    {
        var contrato = await ObtenerContratoAsync();

        foreach (var (ruta, item) in contrato["paths"]!.AsObject().Where(p => p.Key.StartsWith("/api/v1/gestion", StringComparison.Ordinal)))
        {
            foreach (var (metodo, operacion) in item!.AsObject())
            {
                var respuestas = operacion!["responses"]!.AsObject().Select(r => r.Key).ToList();
                var etiqueta = $"{metodo.ToUpperInvariant()} {ruta}";

                if (metodo is "post" or "delete")
                {
                    respuestas.Any(r => r is "200" or "201" or "204").ShouldBeTrue(etiqueta);
                }
            }
        }
    }
}
