using System.Globalization;
using System.Net;

using Shouldly;

namespace Reservas.Api.Tests;

/// <summary>API con un límite de solo 3 escrituras por minuto, para poder alcanzarlo en un test.</summary>
public sealed class ApiConLimiteBajo(Reservas.Tests.Comunes.ServidorPostgres servidor) : ApiConBaseDeDatos(servidor)
{
    protected override IReadOnlyDictionary<string, string?> Ajustes { get; } = new Dictionary<string, string?>
    {
        ["Desarrollo:SembrarDatosDemo"] = "false",
        ["Limites:Lectura:Permisos"] = "100000",
        ["Limites:Lectura:VentanaSegundos"] = "60",
        ["Limites:Escritura:Permisos"] = "3",
        ["Limites:Escritura:VentanaSegundos"] = "60",
    };
}

/// <summary>API que no devuelve el código de gestión (como cuando el correo de confirmación esté activo).</summary>
public sealed class ApiSinCodigoEnRespuesta(Reservas.Tests.Comunes.ServidorPostgres servidor) : ApiConBaseDeDatos(servidor)
{
    protected override IReadOnlyDictionary<string, string?> Ajustes =>
        new Dictionary<string, string?>(base.Ajustes) { ["Publico:MostrarCodigoGestion"] = "false" };
}

public class LimitesTests(ApiConLimiteBajo api) : PruebaApi(api), IClassFixture<ApiConLimiteBajo>
{
    [Fact]
    public async Task La_cuarta_escritura_en_la_ventana_es_un_429_con_retry_after()
    {
        for (var i = 0; i < 3; i++)
        {
            using var permitida = await ReservarAsync(ApiConBaseDeDatos.NegocioDosMesas, Api.SiguienteFecha());
            permitida.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);
        }

        using var rechazada = await ReservarAsync(ApiConBaseDeDatos.NegocioDosMesas, Api.SiguienteFecha());

        rechazada.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        rechazada.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        (await LeerProblemaAsync(rechazada)).Code.ShouldBe("limite.excedido");

        var espera = int.Parse(rechazada.Headers.GetValues("Retry-After").Single(), CultureInfo.InvariantCulture);
        espera.ShouldBeInRange(1, 60);

        // Las lecturas tienen su propio límite y siguen funcionando.
        using var lectura = await Api.CrearCliente().GetAsync(new Uri("/api/v1/negocios/bar-la-plaza", UriKind.Relative), Cancelacion);
        lectura.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}

public class OpcionesPublicasTests(ApiSinCodigoEnRespuesta api) : PruebaApi(api), IClassFixture<ApiSinCodigoEnRespuesta>
{
    [Fact]
    public async Task Con_el_codigo_desactivado_la_respuesta_no_lo_incluye_ni_en_la_ubicacion()
    {
        using var respuesta = await ReservarAsync(ApiConBaseDeDatos.NegocioDosMesas, Api.SiguienteFecha());

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Created);
        respuesta.Headers.Location.ShouldBeNull();

        var texto = await respuesta.Content.ReadAsStringAsync(Cancelacion);
        texto.ShouldNotContain("codigoGestion");
        (await LeerReservaAsync(respuesta)).CodigoGestion.ShouldBeNull();
    }
}

/// <summary>API con la siembra de desarrollo activada y sin datos previos: como al ejecutar el entorno local.</summary>
public sealed class ApiConDatosDeDemostracion(Reservas.Tests.Comunes.ServidorPostgres servidor) : ApiConBaseDeDatos(servidor)
{
    protected override IReadOnlyDictionary<string, string?> Ajustes =>
        new Dictionary<string, string?>(base.Ajustes) { ["Desarrollo:SembrarDatosDemo"] = "true" };

    protected override Task SembrarAsync(Reservas.Infraestructura.Persistencia.ReservasDbContext db) => Task.CompletedTask;
}

public class DatosDeDemostracionTests(ApiConDatosDeDemostracion api) : PruebaApi(api), IClassFixture<ApiConDatosDeDemostracion>
{
    [Fact]
    public async Task En_desarrollo_la_api_arranca_con_un_negocio_de_demostracion_listo_para_probar()
    {
        var horas = await HorasDisponiblesAsync("bar-la-plaza", Api.SiguienteFecha());

        horas.Length.ShouldBe(12);

        using var reserva = await ReservarAsync("bar-la-plaza", Api.SiguienteFecha());
        reserva.StatusCode.ShouldBe(HttpStatusCode.Created);
    }
}
