using System.Net;
using System.Net.Http.Json;
using System.Text;

using Reservas.Api.Contratos;

using Shouldly;

namespace Reservas.Api.Tests;

public class ReservarTests(ApiConBaseDeDatos api) : PruebaApi(api), IClassFixture<ApiConBaseDeDatos>
{
    [Fact]
    public async Task Reservar_devuelve_201_con_la_reserva_pendiente_y_su_ubicacion()
    {
        var fecha = Api.SiguienteFecha();
        var email = NuevoEmail();

        using var respuesta = await ReservarAsync(ApiConBaseDeDatos.NegocioDosMesas, fecha, "21:00", 3, email: email);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Created);
        respuesta.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");

        var reserva = await LeerReservaAsync(respuesta);
        reserva.Estado.ShouldBe("pendiente");
        reserva.Negocio.ShouldBe("bar-la-plaza");
        reserva.Fecha.ShouldBe(Formatos.DeFecha(fecha));
        reserva.Hora.ShouldBe("21:00");
        reserva.Comensales.ShouldBe(3);
        reserva.Cliente.ShouldBe(new ClienteRespuesta("Ana Pérez", email));
        reserva.Fin.ShouldBe(reserva.Inicio.AddMinutes(90));
        reserva.CaducaEn.ShouldBe(Api.Reloj.GetUtcNow().AddMinutes(30));

        reserva.CodigoGestion.ShouldNotBeNull();
        reserva.CodigoGestion.Length.ShouldBe(22);
        respuesta.Headers.Location!.OriginalString.ShouldBe($"/api/v1/reservas/gestion/{reserva.CodigoGestion}");
    }

    [Fact]
    public async Task La_respuesta_no_incluye_datos_internos_como_las_mesas()
    {
        using var respuesta = await ReservarAsync(ApiConBaseDeDatos.NegocioDosMesas, Api.SiguienteFecha());

        var texto = await respuesta.Content.ReadAsStringAsync(Cancelacion);

        texto.ShouldNotContain("mesa", Case.Insensitive);
        texto.ShouldNotContain("negocioId", Case.Insensitive);
    }

    [Fact]
    public async Task Sin_idempotency_key_es_un_400()
    {
        using var respuesta = await EnviarReservaAsync(
            ApiConBaseDeDatos.NegocioDosMesas, JsonContent.Create(new SolicitudReservaDto("2026-10-03", "21:00", 2, new ClienteDto("Ana", NuevoEmail(), null))), clave: null);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await LeerProblemaAsync(respuesta)).Code.ShouldBe("idempotencia.clave_requerida");
    }

    [Theory]
    [InlineData("corta")]
    [InlineData("clave con espacios y más símbolos!")]
    public async Task Una_clave_de_idempotencia_con_formato_invalido_es_un_400(string clave)
    {
        using var respuesta = await ReservarAsync(ApiConBaseDeDatos.NegocioDosMesas, Api.SiguienteFecha(), clave: clave);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await LeerProblemaAsync(respuesta)).Code.ShouldBe("idempotencia.clave_invalida");
    }

    [Fact]
    public async Task Un_cuerpo_vacio_lista_todos_los_campos_obligatorios()
    {
        using var respuesta = await EnviarReservaAsync(
            ApiConBaseDeDatos.NegocioDosMesas, new StringContent("{}", Encoding.UTF8, "application/json"), Guid.NewGuid().ToString("N"));

        respuesta.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problema = await LeerProblemaAsync(respuesta);
        problema.Code.ShouldBe("validacion.invalida");
        problema.Errors!.Keys.ShouldBe(["fecha", "hora", "comensales", "cliente"], ignoreOrder: true);
    }

    [Fact]
    public async Task Los_campos_con_valores_invalidos_se_reportan_juntos_con_su_ruta()
    {
        var solicitud = new SolicitudReservaDto("2026-10-03", "9pm", 0, new ClienteDto("", "no-es-un-correo", new string('6', 31)));

        using var respuesta = await EnviarReservaAsync(
            ApiConBaseDeDatos.NegocioDosMesas, JsonContent.Create(solicitud), Guid.NewGuid().ToString("N"));

        respuesta.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var errores = (await LeerProblemaAsync(respuesta)).Errors!;
        errores.Keys.ShouldBe(["hora", "comensales", "cliente.nombre", "cliente.email", "cliente.telefono"], ignoreOrder: true);
        errores["hora"].ShouldContain("La hora debe tener el formato HH:mm.");
    }

    [Fact]
    public async Task Un_json_mal_formado_es_un_400()
    {
        using var respuesta = await EnviarReservaAsync(
            ApiConBaseDeDatos.NegocioDosMesas, new StringContent("{esto no es json", Encoding.UTF8, "application/json"), Guid.NewGuid().ToString("N"));

        respuesta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Una_hora_que_el_negocio_no_ofrece_es_un_409_de_franja_no_disponible()
    {
        using var respuesta = await ReservarAsync(ApiConBaseDeDatos.NegocioDosMesas, Api.SiguienteFecha(), "21:15");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await LeerProblemaAsync(respuesta)).Code.ShouldBe("reserva.franja_no_disponible");
    }

    [Fact]
    public async Task Reservar_en_un_negocio_inexistente_es_un_404()
    {
        using var respuesta = await ReservarAsync("no-existe", Api.SiguienteFecha());

        respuesta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await LeerProblemaAsync(respuesta)).Code.ShouldBe("negocio.no_encontrado");
    }

    [Fact]
    public async Task Un_grupo_mayor_que_el_maximo_online_es_un_422_de_negocio()
    {
        using var respuesta = await ReservarAsync(ApiConBaseDeDatos.NegocioDosMesas, Api.SiguienteFecha(), comensales: 11);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await LeerProblemaAsync(respuesta)).Code.ShouldBe("reserva.demasiados_comensales_online");
    }

    [Fact]
    public async Task Con_dos_mesas_caben_dos_reservas_a_la_misma_hora_y_la_tercera_no()
    {
        var fecha = Api.SiguienteFecha();

        (await ReservarAsync(ApiConBaseDeDatos.NegocioDosMesas, fecha, "21:00")).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await ReservarAsync(ApiConBaseDeDatos.NegocioDosMesas, fecha, "21:00")).StatusCode.ShouldBe(HttpStatusCode.Created);

        using var tercera = await ReservarAsync(ApiConBaseDeDatos.NegocioDosMesas, fecha, "21:00");

        tercera.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await LeerProblemaAsync(tercera)).Code.ShouldBe("reserva.franja_no_disponible");
    }

    [Fact]
    public async Task Una_reserva_guarda_todos_sus_datos_en_la_base_de_datos()
    {
        var fecha = Api.SiguienteFecha();
        var email = NuevoEmail();

        using var respuesta = await ReservarAsync(ApiConBaseDeDatos.NegocioUnaMesa, fecha, "13:30", 4, email: email);
        var reserva = await LeerReservaAsync(respuesta);

        await using var db = Api.NuevoContexto();
        var guardada = db.Reservas.Single(r => r.Cliente.Email == email);
        guardada.CodigoGestion.ShouldBe(reserva.CodigoGestion);
        guardada.Comensales.ShouldBe(4);
        guardada.Cliente.Telefono.ShouldBe("600 000 000");
        guardada.CreadaEn.ShouldBe(Api.Reloj.GetUtcNow());
    }
}
