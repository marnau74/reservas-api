using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;

using Reservas.Api.Errores;
using Reservas.Api.Idempotencia;
using Reservas.Aplicacion;
using Reservas.Dominio.Comun;
using Reservas.Dominio.Gestion;

using Shouldly;

namespace Reservas.Api.Tests;

public partial class ContratoTests(ApiConBaseDeDatos api) : PruebaApi(api), IClassFixture<ApiConBaseDeDatos>
{
    private static readonly string[] Conflictos =
    [
        "reserva.franja_no_disponible", "reserva.mesa_ocupada", "reserva.conflicto_concurrencia",
        "reserva.transicion_invalida", "reserva.caducada", "reserva.aun_no_es_hora", "reserva.no_caduca_aun",
        "usuario.email_en_uso", "usuario.protegido", "usuario.ya_desactivado", "sala.con_mesas", "mesa.con_reservas",
    ];

    private static readonly string[] NoEncontrados =
    [
        "negocio.no_encontrado", "reserva.no_encontrada", "usuario.no_encontrado", "sala.no_encontrada",
        "mesa.no_encontrada", "horario.no_encontrado", "cierre.no_encontrado",
    ];

    private static readonly string[] NoAutenticados = ["auth.credenciales_invalidas", "auth.token_invalido"];

    /// <summary>Todos los errores de negocio declarados en el dominio, la aplicación y la API.</summary>
    private static List<ErrorDominio> TodosLosErrores() =>
    [
        .. new[] { typeof(ErroresReserva).Assembly, typeof(ErroresAplicacion).Assembly, typeof(ErroresIdempotencia).Assembly }
            .SelectMany(ensamblado => ensamblado.GetTypes())
            .Where(tipo => tipo is { IsAbstract: true, IsSealed: true } && tipo.Name.StartsWith("Errores", StringComparison.Ordinal))
            .SelectMany(tipo => tipo.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(campo => campo.FieldType == typeof(ErrorDominio))
            .Select(campo => (ErrorDominio)campo.GetValue(null)!),
    ];

    [Fact]
    public void Todos_los_codigos_de_error_son_unicos_y_tienen_el_formato_esperado()
    {
        var errores = TodosLosErrores();

        errores.Count.ShouldBeGreaterThan(20);
        errores.Select(e => e.Codigo).ShouldBeUnique();
        errores.ShouldAllBe(e => FormatoCodigo().IsMatch(e.Codigo), "los códigos son «ambito.descripcion_en_minusculas»");
        errores.ShouldAllBe(e => e.Mensaje.Length > 10, "todo error explica qué ha pasado");
    }

    [Fact]
    public void Cada_error_de_negocio_se_traduce_al_estado_http_correcto()
    {
        var negocio = TodosLosErrores().Where(e => !e.Codigo.StartsWith("idempotencia.", StringComparison.Ordinal)).ToList();

        foreach (var error in negocio)
        {
            var esperado = NoAutenticados.Contains(error.Codigo) ? 401 : NoEncontrados.Contains(error.Codigo) ? 404 : Conflictos.Contains(error.Codigo) ? 409 : 422;

            ProblemasApi.EstadoHttp(error).ShouldBe(esperado, error.Codigo);
        }

        // Los listados de arriba no se han quedado desfasados respecto a los errores reales.
        Conflictos.Concat(NoEncontrados).Concat(NoAutenticados).ShouldBeSubsetOf(negocio.Select(e => e.Codigo));
    }

    [Fact]
    public async Task El_documento_openapi_describe_todas_las_operaciones_publicas()
    {
        using var respuesta = await Api.CrearCliente().GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), Cancelacion);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
        var documento = await LeerJsonAsync(respuesta);
        var rutas = documento.GetProperty("paths");

        var esperadas = new Dictionary<string, string[]>
        {
            ["/api/v1/negocios/{slug}"] = ["get"],
            ["/api/v1/negocios/{slug}/disponibilidad"] = ["get"],
            ["/api/v1/negocios/{slug}/reservas"] = ["post"],
            ["/api/v1/reservas/gestion/{codigo}"] = ["get"],
            ["/api/v1/reservas/gestion/{codigo}/confirmar"] = ["post"],
            ["/api/v1/reservas/gestion/{codigo}/cancelar"] = ["post"],
        };

        foreach (var (ruta, metodos) in esperadas)
        {
            foreach (var metodo in metodos)
            {
                var operacion = rutas.GetProperty(ruta).GetProperty(metodo);
                operacion.GetProperty("operationId").GetString().ShouldNotBeNullOrWhiteSpace($"{metodo} {ruta}");
                operacion.GetProperty("summary").GetString().ShouldNotBeNullOrWhiteSpace($"{metodo} {ruta}");
            }
        }

        rutas.GetProperty("/api/v1/negocios/{slug}/reservas").GetProperty("post").GetProperty("responses")
            .TryGetProperty("409", out _).ShouldBeTrue("crear una reserva documenta el conflicto");
    }

    [GeneratedRegex(@"^[a-z]+\.[a-z_]+$", RegexOptions.CultureInvariant)]
    private static partial Regex FormatoCodigo();
}
