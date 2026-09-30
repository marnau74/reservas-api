using System.Text;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using Reservas.Api.Idempotencia;
using Reservas.Aplicacion.Abstracciones;

using Shouldly;

namespace Reservas.Api.Tests;

/// <summary>
/// El middleware de idempotencia sin base de datos, con un almacén falso. Sirve para probar lo
/// que con un servidor real es difícil provocar, como un error 500 o una excepción a mitad de la
/// petición.
/// </summary>
public class IdempotenciaMiddlewareTests
{
    private const string CuerpoPeticion = "{\"fecha\":\"2026-10-03\"}";

    private static readonly FakeTimeProvider Reloj = new(new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero));

    // Las respuestas de error (ProblemDetails) necesitan estos servicios, que un contexto HTTP suelto no trae.
    private static readonly ServiceProvider Servicios = new ServiceCollection().AddLogging().AddOptions().BuildServiceProvider();

    private sealed class AlmacenFalso(EstadoAdquisicion estado = EstadoAdquisicion.Adquirida, RespuestaGuardada? guardada = null) : IAlmacenIdempotencia
    {
        public int Adquisiciones { get; private set; }

        public int Completadas { get; private set; }

        public int Liberaciones { get; private set; }

        public string? Clave { get; private set; }

        public string? Huella { get; private set; }

        public RespuestaGuardada? Guardada { get; private set; }

        public Task<ResultadoAdquisicion> AdquirirAsync(string clave, string huellaPeticion, DateTimeOffset ahora, CancellationToken cancellationToken)
        {
            Adquisiciones++;
            Clave = clave;
            Huella = huellaPeticion;
            return Task.FromResult(new ResultadoAdquisicion(estado, guardada));
        }

        public Task CompletarAsync(string clave, RespuestaGuardada respuesta, DateTimeOffset ahora, CancellationToken cancellationToken)
        {
            Completadas++;
            Guardada = respuesta;
            return Task.CompletedTask;
        }

        public Task LiberarAsync(string clave, CancellationToken cancellationToken)
        {
            Liberaciones++;
            return Task.CompletedTask;
        }
    }

    private static DefaultHttpContext Contexto(
        string cuerpo = CuerpoPeticion,
        string? clave = "clave-valida-0001",
        bool idempotente = true,
        string ruta = "/api/v1/negocios/bar/reservas")
    {
        var contexto = new DefaultHttpContext { RequestServices = Servicios };
        contexto.Request.Method = "POST";
        contexto.Request.Path = ruta;
        contexto.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(cuerpo));
        contexto.Response.Body = new MemoryStream();

        if (clave is not null)
        {
            contexto.Request.Headers[IdempotenciaMiddleware.Cabecera] = clave;
        }

        if (idempotente)
        {
            contexto.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new RequiereIdempotencia()), "prueba"));
        }

        return contexto;
    }

    private static async Task<string> LeerCuerpoAsync(HttpContext contexto)
    {
        contexto.Response.Body.Position = 0;
        return await new StreamReader(contexto.Response.Body).ReadToEndAsync(TestContext.Current.CancellationToken);
    }

    private static Task Ejecutar(IdempotenciaMiddleware middleware, HttpContext contexto, AlmacenFalso almacen) =>
        middleware.InvokeAsync(contexto, almacen, Reloj);

    private static IdempotenciaMiddleware Con(Func<HttpContext, Task> siguiente) => new(contextoSiguiente => siguiente(contextoSiguiente));

    // --- Endpoints que no son idempotentes -------------------------------------------------

    [Fact]
    public async Task Un_endpoint_sin_la_marca_pasa_sin_tocar_el_almacen()
    {
        var almacen = new AlmacenFalso();
        var llamado = false;
        var contexto = Contexto(idempotente: false, clave: null);

        await Ejecutar(Con(_ => { llamado = true; return Task.CompletedTask; }), contexto, almacen);

        llamado.ShouldBeTrue();
        almacen.Adquisiciones.ShouldBe(0);
    }

    // --- Validación de la cabecera -------------------------------------------------------------

    [Fact]
    public async Task Sin_la_cabecera_responde_400_y_no_llega_al_endpoint()
    {
        var almacen = new AlmacenFalso();
        var llamado = false;
        var contexto = Contexto(clave: null);

        await Ejecutar(Con(_ => { llamado = true; return Task.CompletedTask; }), contexto, almacen);

        contexto.Response.StatusCode.ShouldBe(400);
        (await LeerCuerpoAsync(contexto)).ShouldContain("idempotencia.clave_requerida");
        llamado.ShouldBeFalse();
        almacen.Adquisiciones.ShouldBe(0);
    }

    [Theory]
    [InlineData("corta")]
    [InlineData("tiene espacios en la clave")]
    [InlineData("caracteres-raros-ñ-€-0001")]
    public async Task Una_clave_con_formato_invalido_responde_400(string clave)
    {
        var almacen = new AlmacenFalso();
        var contexto = Contexto(clave: clave);

        await Ejecutar(Con(_ => Task.CompletedTask), contexto, almacen);

        contexto.Response.StatusCode.ShouldBe(400);
        (await LeerCuerpoAsync(contexto)).ShouldContain("idempotencia.clave_invalida");
        almacen.Adquisiciones.ShouldBe(0);
    }

    // --- Primera vez: se procesa y se guarda -----------------------------------------------------

    [Fact]
    public async Task La_primera_vez_procesa_la_peticion_y_guarda_su_respuesta()
    {
        var almacen = new AlmacenFalso();
        var contexto = Contexto();

        await Ejecutar(
            Con(async c =>
            {
                c.Response.StatusCode = 201;
                c.Response.ContentType = "application/json";
                c.Response.Headers.Location = "/api/v1/reservas/gestion/abc";
                await c.Response.WriteAsync("{\"ok\":true}", TestContext.Current.CancellationToken);
            }),
            contexto,
            almacen);

        // El cliente recibe la respuesta...
        contexto.Response.StatusCode.ShouldBe(201);
        (await LeerCuerpoAsync(contexto)).ShouldBe("{\"ok\":true}");

        // ...y queda guardada para poder repetirla.
        almacen.Completadas.ShouldBe(1);
        almacen.Liberaciones.ShouldBe(0);
        almacen.Guardada.ShouldBe(new RespuestaGuardada(201, "application/json", "{\"ok\":true}", "/api/v1/reservas/gestion/abc"));
    }

    [Fact]
    public async Task El_endpoint_puede_leer_el_cuerpo_aunque_el_middleware_lo_haya_leido_antes()
    {
        var almacen = new AlmacenFalso();
        string? leido = null;

        await Ejecutar(
            Con(async c => leido = await new StreamReader(c.Request.Body).ReadToEndAsync(TestContext.Current.CancellationToken)),
            Contexto(),
            almacen);

        leido.ShouldBe(CuerpoPeticion);
    }

    [Fact]
    public async Task Un_error_4xx_tambien_se_guarda()
    {
        var almacen = new AlmacenFalso();

        await Ejecutar(Con(c => { c.Response.StatusCode = 409; return Task.CompletedTask; }), Contexto(), almacen);

        almacen.Completadas.ShouldBe(1);
        almacen.Guardada!.EstadoHttp.ShouldBe(409);
    }

    // --- Fallos del servidor: la clave se libera para que el cliente pueda reintentar -------------

    [Fact]
    public async Task Un_error_5xx_libera_la_clave_en_lugar_de_guardarlo_y_el_cliente_lo_recibe()
    {
        var almacen = new AlmacenFalso();
        var contexto = Contexto();

        await Ejecutar(
            Con(async c =>
            {
                c.Response.StatusCode = 503;
                await c.Response.WriteAsync("no disponible", TestContext.Current.CancellationToken);
            }),
            contexto,
            almacen);

        almacen.Liberaciones.ShouldBe(1);
        almacen.Completadas.ShouldBe(0);
        contexto.Response.StatusCode.ShouldBe(503);
        (await LeerCuerpoAsync(contexto)).ShouldBe("no disponible");
    }

    [Fact]
    public async Task Una_excepcion_libera_la_clave_y_sigue_propagandose()
    {
        var almacen = new AlmacenFalso();
        var contexto = Contexto();

        await Should.ThrowAsync<InvalidOperationException>(
            () => Ejecutar(Con(_ => throw new InvalidOperationException("fallo del servidor")), contexto, almacen));

        almacen.Liberaciones.ShouldBe(1);
        almacen.Completadas.ShouldBe(0);
    }

    // --- Repeticiones y conflictos ---------------------------------------------------------------

    [Fact]
    public async Task Una_peticion_repetida_devuelve_la_respuesta_guardada_sin_ejecutar_el_endpoint()
    {
        var guardada = new RespuestaGuardada(201, "application/json", "{\"ok\":true}", "/api/v1/reservas/gestion/abc");
        var almacen = new AlmacenFalso(EstadoAdquisicion.Repetida, guardada);
        var llamado = false;
        var contexto = Contexto();

        await Ejecutar(Con(_ => { llamado = true; return Task.CompletedTask; }), contexto, almacen);

        llamado.ShouldBeFalse();
        contexto.Response.StatusCode.ShouldBe(201);
        contexto.Response.ContentType.ShouldBe("application/json");
        contexto.Response.Headers.Location.ToString().ShouldBe("/api/v1/reservas/gestion/abc");
        contexto.Response.Headers[IdempotenciaMiddleware.CabeceraRepetida].ToString().ShouldBe("true");
        (await LeerCuerpoAsync(contexto)).ShouldBe("{\"ok\":true}");
        almacen.Completadas.ShouldBe(0);
    }

    [Fact]
    public async Task Una_peticion_en_curso_responde_409()
    {
        var almacen = new AlmacenFalso(EstadoAdquisicion.EnCurso);
        var llamado = false;
        var contexto = Contexto();

        await Ejecutar(Con(_ => { llamado = true; return Task.CompletedTask; }), contexto, almacen);

        llamado.ShouldBeFalse();
        contexto.Response.StatusCode.ShouldBe(409);
        (await LeerCuerpoAsync(contexto)).ShouldContain("idempotencia.en_curso");
    }

    [Fact]
    public async Task Una_clave_reutilizada_para_otra_peticion_responde_422()
    {
        var almacen = new AlmacenFalso(EstadoAdquisicion.HuellaDistinta);
        var contexto = Contexto();

        await Ejecutar(Con(_ => Task.CompletedTask), contexto, almacen);

        contexto.Response.StatusCode.ShouldBe(422);
        (await LeerCuerpoAsync(contexto)).ShouldContain("idempotencia.clave_reutilizada");
    }

    // --- La huella identifica «esta misma petición» -----------------------------------------------

    [Fact]
    public async Task La_huella_es_igual_para_la_misma_peticion_y_distinta_si_cambia_algo()
    {
        static async Task<string?> HuellaDe(string cuerpo, string ruta)
        {
            var almacen = new AlmacenFalso();
            await Ejecutar(Con(_ => Task.CompletedTask), Contexto(cuerpo: cuerpo, ruta: ruta), almacen);
            return almacen.Huella;
        }

        var original = await HuellaDe(CuerpoPeticion, "/api/v1/negocios/bar/reservas");

        (await HuellaDe(CuerpoPeticion, "/api/v1/negocios/bar/reservas")).ShouldBe(original);
        (await HuellaDe(CuerpoPeticion.Replace("10-03", "10-04", StringComparison.Ordinal), "/api/v1/negocios/bar/reservas")).ShouldNotBe(original);
        (await HuellaDe(CuerpoPeticion, "/api/v1/negocios/otro/reservas")).ShouldNotBe(original);
        original.ShouldNotBeNull();
        original.Length.ShouldBe(64); // SHA-256 en hexadecimal
    }
}
