using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

using Reservas.Api.Contratos;
using Reservas.Infraestructura.Persistencia;
using Reservas.Tests.Comunes;

using Shouldly;

namespace Reservas.Api.Tests;

/// <summary>La API arrancada como en producción: sin el entorno de desarrollo y solo con lo que se configure.</summary>
public class ApiEnProduccion(ServidorPostgres servidor) : ApiConBaseDeDatos(servidor)
{
    public const string ContrasenaDemo = "Demo-De-Produccion-42";
    public const string OrigenPermitido = "https://mi-bar.example";

    protected override string Entorno => "Production";

    protected override IReadOnlyDictionary<string, string?> Ajustes => new Dictionary<string, string?>(base.Ajustes)
    {
        ["Jwt:Clave"] = "una-clave-de-produccion-larga-y-secreta-para-los-tests",
        ["BaseDeDatos:MigrarAlArrancar"] = "true",
        ["Demo:Sembrar"] = "true",
        ["Demo:Contrasena"] = ContrasenaDemo,
        ["Cors:Origenes:0"] = OrigenPermitido,
    };

    // Los datos los siembra la propia API al arrancar (Demo:Sembrar).
    protected override Task SembrarAsync(ReservasDbContext db) => Task.CompletedTask;
}

public class ProduccionTests(ApiEnProduccion api) : PruebaPersonal(api), IClassFixture<ApiEnProduccion>
{
    [Fact]
    public async Task Las_comprobaciones_de_salud_responden_en_produccion_sin_dar_detalles()
    {
        var cliente = Api.CrearCliente();

        using var salud = await cliente.GetAsync(new Uri("/health", UriKind.Relative), Cancelacion);
        using var viva = await cliente.GetAsync(new Uri("/alive", UriKind.Relative), Cancelacion);

        salud.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await salud.Content.ReadAsStringAsync(Cancelacion)).ShouldBe("Healthy", "solo el estado, nada de versiones ni cadenas de conexión");
        viva.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task El_contrato_openapi_no_se_publica_en_produccion()
    {
        using var respuesta = await Api.CrearCliente().GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), Cancelacion);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Todas_las_respuestas_llevan_cabeceras_de_seguridad_y_las_de_la_api_no_se_guardan_en_cache()
    {
        using var respuesta = await Api.CrearCliente().GetAsync(new Uri("/api/v1/negocios/bar-la-plaza", UriKind.Relative), Cancelacion);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
        respuesta.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        respuesta.Headers.GetValues("X-Frame-Options").ShouldBe(["DENY"]);
        respuesta.Headers.GetValues("Referrer-Policy").ShouldBe(["no-referrer"]);
        respuesta.Headers.GetValues("Content-Security-Policy").Single().ShouldContain("default-src 'none'");
        respuesta.Headers.CacheControl!.NoStore.ShouldBeTrue();
    }

    [Fact]
    public async Task Los_errores_tambien_llevan_las_cabeceras_de_seguridad()
    {
        using var respuesta = await Api.CrearCliente().GetAsync(new Uri("/api/v1/gestion/agenda?fecha=2026-10-03", UriKind.Relative), Cancelacion);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        respuesta.Headers.Contains("X-Content-Type-Options").ShouldBeTrue();
    }

    [Fact]
    public async Task Solo_el_origen_configurado_puede_llamar_a_la_api_desde_un_navegador()
    {
        var cliente = Api.CrearCliente();

        using var permitido = await PreflightAsync(cliente, ApiEnProduccion.OrigenPermitido);
        using var ajeno = await PreflightAsync(cliente, "https://otra-web.example");

        permitido.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe([ApiEnProduccion.OrigenPermitido]);
        permitido.Headers.GetValues("Access-Control-Allow-Headers").Single().ShouldContain("Idempotency-Key", Case.Insensitive);
        ajeno.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse("un origen no configurado no recibe permiso");
    }

    [Fact]
    public async Task Una_peticion_normal_desde_el_origen_permitido_recibe_la_cabecera_cors()
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Get, "/api/v1/negocios/bar-la-plaza");
        peticion.Headers.Add("Origin", ApiEnProduccion.OrigenPermitido);

        using var respuesta = await Api.CrearCliente().SendAsync(peticion, Cancelacion);

        respuesta.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe([ApiEnProduccion.OrigenPermitido]);
    }

    [Fact]
    public async Task La_base_de_datos_se_migra_y_se_siembra_la_demo_con_la_contrasena_configurada_y_no_con_la_publicada()
    {
        using var conConfigurada = await LoginAsync("personal@demo.example", ApiEnProduccion.ContrasenaDemo);
        using var conPublicada = await LoginAsync("personal@demo.example", "Demo-Reservas-2026");

        conConfigurada.StatusCode.ShouldBe(HttpStatusCode.OK);
        conPublicada.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, "la contraseña de desarrollo no vale en un despliegue");
    }

    [Fact]
    public async Task La_agenda_de_la_demo_trae_reservas_de_ejemplo_ficticias_de_los_proximos_dias()
    {
        var personal = ClienteConToken((await IniciarSesionAsync("personal@demo.example", ApiEnProduccion.ContrasenaDemo)).TokenAcceso);
        var manana = Formatos.DeFecha(DateOnly.FromDateTime(Api.Reloj.GetUtcNow().UtcDateTime).AddDays(1));

        var agenda = await LeerAsync<AgendaRespuesta>(await ObtenerAsync(personal, $"/api/v1/gestion/agenda?fecha={manana}"));

        agenda.Reservas.Count.ShouldBe(2);
        agenda.Reservas.Select(r => r.Hora).ShouldBe(["13:30", "21:00"]);
        agenda.Reservas.ShouldAllBe(r => r.Cliente.Nombre.StartsWith("Cliente demo", StringComparison.Ordinal) && r.Cliente.Email.EndsWith("@example.com", StringComparison.Ordinal));
        agenda.Reservas.ShouldAllBe(r => r.Estado == "confirmada");
    }

    [Fact]
    public async Task Sembrar_dos_veces_no_duplica_ni_las_cuentas_ni_las_reservas()
    {
        await using var db = Api.NuevoContexto();

        await SembradorDemo.SembrarSiHaceFaltaAsync(db, ApiEnProduccion.ContrasenaDemo);
        await SembradorDemo.SembrarReservasDeEjemploAsync(db, Api.Reloj);

        (await db.Usuarios.CountAsync(Cancelacion)).ShouldBe(3);
        (await db.Reservas.CountAsync(Cancelacion)).ShouldBe(6);
        (await db.Negocios.CountAsync(Cancelacion)).ShouldBe(1);
    }

    private static async Task<HttpResponseMessage> PreflightAsync(HttpClient cliente, string origen)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Options, "/api/v1/negocios/bar-la-plaza/reservas");
        peticion.Headers.Add("Origin", origen);
        peticion.Headers.Add("Access-Control-Request-Method", "POST");
        peticion.Headers.Add("Access-Control-Request-Headers", "content-type,idempotency-key");

        return await cliente.SendAsync(peticion, Cancelacion);
    }
}

/// <summary>Cosas que en producción deben impedir que la API arranque mal configurada, en lugar de arrancar insegura.</summary>
public class ArranqueSeguroTests
{
    private static WebApplicationFactory<Program> ApiConAjustes(params (string Clave, string Valor)[] ajustes) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseEnvironment("Production");
            host.UseSetting("ConnectionStrings:reservas", "Host=localhost;Database=no_se_usa");

            foreach (var (clave, valor) in ajustes)
            {
                host.UseSetting(clave, valor);
            }
        });

    [Fact]
    public void Sin_clave_para_firmar_los_tokens_la_api_no_arranca_en_produccion()
    {
        using var fabrica = ApiConAjustes(("Tareas:Activas", "false"));

        var error = Should.Throw<InvalidOperationException>(() => fabrica.CreateClient());

        error.Message.ShouldContain("Jwt:Clave");
    }

    [Fact]
    public void Una_clave_corta_tampoco_vale()
    {
        using var fabrica = ApiConAjustes(("Jwt:Clave", "corta"), ("Tareas:Activas", "false"));

        Should.Throw<InvalidOperationException>(() => fabrica.CreateClient()).Message.ShouldContain("Jwt:Clave");
    }

    [Fact]
    public void Sembrar_la_demo_fuera_de_desarrollo_exige_configurar_una_contrasena()
    {
        using var fabrica = ApiConAjustes(
            ("Jwt:Clave", "una-clave-de-produccion-larga-y-secreta-para-los-tests"),
            ("Tareas:Activas", "false"),
            ("Demo:Sembrar", "true"));

        Should.Throw<InvalidOperationException>(() => fabrica.CreateClient()).Message.ShouldContain("Demo:Contrasena");
    }

    [Fact]
    public void Una_contrasena_de_demo_debil_no_se_acepta_fuera_de_desarrollo()
    {
        using var fabrica = ApiConAjustes(
            ("Jwt:Clave", "una-clave-de-produccion-larga-y-secreta-para-los-tests"),
            ("Tareas:Activas", "false"),
            ("Demo:Sembrar", "true"),
            ("Demo:Contrasena", "1234"));

        Should.Throw<InvalidOperationException>(() => fabrica.CreateClient()).Message.ShouldContain("Demo:Contrasena");
    }

    [Fact]
    public void Un_origen_cors_mal_escrito_impide_arrancar()
    {
        using var fabrica = ApiConAjustes(
            ("Jwt:Clave", "una-clave-de-produccion-larga-y-secreta-para-los-tests"),
            ("Tareas:Activas", "false"),
            ("Cors:Origenes:0", "https://mi-bar.example/"));

        Should.Throw<InvalidOperationException>(() => fabrica.CreateClient()).Message.ShouldContain("Cors:Origenes");
    }

    [Fact]
    public void Un_enlace_de_gestion_sin_la_marca_del_codigo_impide_arrancar()
    {
        using var fabrica = ApiConAjustes(
            ("Jwt:Clave", "una-clave-de-produccion-larga-y-secreta-para-los-tests"),
            ("Tareas:Activas", "false"),
            ("Correo:UrlGestion", "https://mi-bar.example/reservas"));

        Should.Throw<InvalidOperationException>(() => fabrica.CreateClient()).Message.ShouldContain("{codigo}");
    }
}
