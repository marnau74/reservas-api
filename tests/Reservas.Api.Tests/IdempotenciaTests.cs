using System.Net;
using System.Net.Http.Json;

using Microsoft.EntityFrameworkCore;

using Reservas.Api.Contratos;

using Shouldly;

namespace Reservas.Api.Tests;

public class IdempotenciaTests(ApiConBaseDeDatos api) : PruebaApi(api), IClassFixture<ApiConBaseDeDatos>
{
    private async Task<int> ContarReservasAsync(string email)
    {
        await using var db = Api.NuevoContexto();
        return await db.Reservas.CountAsync(r => r.Cliente.Email == email, Cancelacion);
    }

    [Fact]
    public async Task Repetir_la_peticion_con_la_misma_clave_devuelve_la_misma_respuesta_sin_crear_otra_reserva()
    {
        var fecha = Api.SiguienteFecha();
        var email = NuevoEmail();
        const string clave = "clave-repetida-0001";

        using var primera = await ReservarAsync(ApiConBaseDeDatos.NegocioDosMesas, fecha, email: email, clave: clave);
        using var segunda = await ReservarAsync(ApiConBaseDeDatos.NegocioDosMesas, fecha, email: email, clave: clave);

        primera.StatusCode.ShouldBe(HttpStatusCode.Created);
        segunda.StatusCode.ShouldBe(HttpStatusCode.Created);

        // La segunda es una repetición: mismo cuerpo, misma ubicación y así lo indica una cabecera.
        segunda.Headers.GetValues("Idempotency-Replayed").ShouldBe(["true"]);
        primera.Headers.Contains("Idempotency-Replayed").ShouldBeFalse();
        (await segunda.Content.ReadAsStringAsync(Cancelacion)).ShouldBe(await primera.Content.ReadAsStringAsync(Cancelacion));
        segunda.Headers.Location.ShouldBe(primera.Headers.Location);

        (await ContarReservasAsync(email)).ShouldBe(1);
    }

    [Fact]
    public async Task Tambien_se_repite_una_respuesta_de_error()
    {
        var fecha = Api.SiguienteFecha();
        var email = NuevoEmail();
        const string clave = "clave-de-un-error-0001";

        using var primera = await ReservarAsync(ApiConBaseDeDatos.NegocioDosMesas, fecha, "21:15", email: email, clave: clave);
        using var segunda = await ReservarAsync(ApiConBaseDeDatos.NegocioDosMesas, fecha, "21:15", email: email, clave: clave);

        primera.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        segunda.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        segunda.Headers.GetValues("Idempotency-Replayed").ShouldBe(["true"]);
        segunda.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Reutilizar_la_clave_para_otra_peticion_es_un_422_y_no_crea_nada()
    {
        var fecha = Api.SiguienteFecha();
        var email = NuevoEmail();
        const string clave = "clave-reutilizada-0001";

        (await ReservarAsync(ApiConBaseDeDatos.NegocioDosMesas, fecha, "21:00", email: email, clave: clave)).StatusCode.ShouldBe(HttpStatusCode.Created);

        // Misma clave, otra hora: no es la misma operación.
        using var otra = await ReservarAsync(ApiConBaseDeDatos.NegocioDosMesas, fecha, "22:00", email: email, clave: clave);

        otra.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await LeerProblemaAsync(otra)).Code.ShouldBe("idempotencia.clave_reutilizada");
        (await ContarReservasAsync(email)).ShouldBe(1);
    }

    [Fact]
    public async Task La_clave_tambien_va_ligada_al_negocio_de_la_peticion()
    {
        var fecha = Api.SiguienteFecha();
        var email = NuevoEmail();
        const string clave = "clave-de-otro-negocio-01";

        (await ReservarAsync(ApiConBaseDeDatos.NegocioDosMesas, fecha, email: email, clave: clave)).StatusCode.ShouldBe(HttpStatusCode.Created);

        using var otroNegocio = await ReservarAsync(ApiConBaseDeDatos.NegocioUnaMesa, fecha, email: email, clave: clave);

        otroNegocio.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await LeerProblemaAsync(otroNegocio)).Code.ShouldBe("idempotencia.clave_reutilizada");
    }

    [Fact]
    public async Task Claves_distintas_son_operaciones_distintas_aunque_los_datos_sean_iguales()
    {
        var fecha = Api.SiguienteFecha();
        var email = NuevoEmail();

        (await ReservarAsync(ApiConBaseDeDatos.NegocioDosMesas, fecha, email: email)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await ReservarAsync(ApiConBaseDeDatos.NegocioDosMesas, fecha, email: email)).StatusCode.ShouldBe(HttpStatusCode.Created);

        (await ContarReservasAsync(email)).ShouldBe(2);
    }

    [Fact]
    public async Task Una_peticion_rechazada_por_validacion_queda_ligada_a_su_clave()
    {
        // Es el comportamiento habitual de las APIs con idempotencia: para corregir el cuerpo hay
        // que usar una clave nueva, no reenviar la misma con otro contenido.
        const string clave = "clave-tras-un-error-01";
        var fecha = Api.SiguienteFecha();
        var invalida = new SolicitudReservaDto(Formatos.DeFecha(fecha), "9pm", 2, new ClienteDto("Ana", NuevoEmail(), null));

        using var rechazada = await EnviarReservaAsync(ApiConBaseDeDatos.NegocioDosMesas, JsonContent.Create(invalida), clave);
        using var corregida = await ReservarAsync(ApiConBaseDeDatos.NegocioDosMesas, fecha, "21:00", clave: clave);

        rechazada.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        corregida.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await LeerProblemaAsync(corregida)).Code.ShouldBe("idempotencia.clave_reutilizada");
    }

    [Fact]
    public async Task Diez_peticiones_simultaneas_con_la_misma_clave_crean_una_sola_reserva()
    {
        var fecha = Api.SiguienteFecha();
        var email = NuevoEmail();
        const string clave = "clave-simultanea-0001";
        var salida = new TaskCompletionSource();

        var peticiones = Enumerable.Range(0, 10).Select(async _ =>
        {
            await salida.Task;
            return await ReservarAsync(ApiConBaseDeDatos.NegocioDosMesas, fecha, email: email, clave: clave);
        }).ToArray();

        salida.SetResult();
        var respuestas = await Task.WhenAll(peticiones);

        // Unas obtienen la reserva (la original y sus repeticiones) y otras llegan mientras se
        // procesa y reciben «en curso»: pero solo se crea una.
        respuestas.ShouldAllBe(r => r.StatusCode == HttpStatusCode.Created || r.StatusCode == HttpStatusCode.Conflict);
        var creadas = respuestas.Where(r => r.StatusCode == HttpStatusCode.Created).ToList();
        creadas.Count(r => !r.Headers.Contains("Idempotency-Replayed")).ShouldBe(1);

        var codigos = new HashSet<string?>();
        foreach (var creada in creadas)
        {
            codigos.Add((await LeerReservaAsync(creada)).CodigoGestion);
        }

        codigos.Count.ShouldBe(1);

        foreach (var respuesta in respuestas.Where(r => r.StatusCode == HttpStatusCode.Conflict))
        {
            (await LeerProblemaAsync(respuesta)).Code.ShouldBe("idempotencia.en_curso");
        }

        (await ContarReservasAsync(email)).ShouldBe(1);
    }

    [Fact]
    public async Task Las_consultas_y_la_gestion_no_piden_clave_de_idempotencia()
    {
        using var respuesta = await Api.CrearCliente().GetAsync(new Uri("/api/v1/negocios/bar-la-plaza", UriKind.Relative), Cancelacion);
        using var gestion = await GestionarAsync("codigo-que-no-existe-0000", "cancelar");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
        gestion.StatusCode.ShouldBe(HttpStatusCode.NotFound); // no un 400 por falta de clave
    }
}
