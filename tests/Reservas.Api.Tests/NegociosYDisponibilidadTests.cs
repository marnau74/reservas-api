using System.Net;
using System.Net.Http.Json;

using Reservas.Api.Contratos;

using Shouldly;

namespace Reservas.Api.Tests;

public class NegociosYDisponibilidadTests(ApiConBaseDeDatos api) : PruebaApi(api), IClassFixture<ApiConBaseDeDatos>
{
    // --- Negocio ---------------------------------------------------------------------------

    [Fact]
    public async Task Un_negocio_se_consulta_por_su_identificador_publico()
    {
        using var respuesta = await Api.CrearCliente().GetAsync(new Uri("/api/v1/negocios/bar-la-plaza", UriKind.Relative), Cancelacion);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
        var negocio = await respuesta.Content.ReadFromJsonAsync<NegocioRespuesta>(Cancelacion);
        negocio.ShouldBe(new NegocioRespuesta("bar-la-plaza", "Negocio bar-la-plaza (demo)", "Europe/Madrid", 10, 60));
    }

    [Fact]
    public async Task Un_negocio_inexistente_es_un_404_con_problem_details()
    {
        using var respuesta = await Api.CrearCliente().GetAsync(new Uri("/api/v1/negocios/no-existe", UriKind.Relative), Cancelacion);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        respuesta.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var problema = await LeerProblemaAsync(respuesta);
        problema.Code.ShouldBe("negocio.no_encontrado");
        problema.Type.ShouldBe("urn:reservas:error:negocio.no_encontrado");
        problema.Status.ShouldBe(404);
        problema.Title.ShouldBe("No encontrado");
        problema.Detail.ShouldNotBeNullOrWhiteSpace();
    }

    // --- Disponibilidad ----------------------------------------------------------------------

    [Fact]
    public async Task La_disponibilidad_ofrece_las_franjas_de_comida_y_cena_en_hora_local()
    {
        // Sábado 3 de octubre de 2026: horario de verano (UTC+2).
        using var respuesta = await DisponibilidadAsync(ApiConBaseDeDatos.NegocioDosMesas, new DateOnly(2026, 10, 3));

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
        var disponibilidad = (await respuesta.Content.ReadFromJsonAsync<DisponibilidadRespuesta>(Cancelacion))!;

        disponibilidad.Negocio.ShouldBe("bar-la-plaza");
        disponibilidad.Fecha.ShouldBe("2026-10-03");
        disponibilidad.Comensales.ShouldBe(2);
        disponibilidad.Franjas.Select(f => f.Hora).ShouldBe(
        [
            "13:00", "13:30", "14:00", "14:30", "15:00", "15:30",
            "20:00", "20:30", "21:00", "21:30", "22:00", "22:30",
        ]);
        disponibilidad.Franjas.Take(6).ShouldAllBe(f => f.Turno == "comida");
        disponibilidad.Franjas.Skip(6).ShouldAllBe(f => f.Turno == "cena");
        disponibilidad.Franjas[0].Inicio.ShouldBe(new DateTimeOffset(2026, 10, 3, 11, 0, 0, TimeSpan.Zero)); // 13:00 local
    }

    [Fact]
    public async Task La_disponibilidad_no_revela_las_mesas_del_local()
    {
        using var respuesta = await DisponibilidadAsync(ApiConBaseDeDatos.NegocioDosMesas, Api.SiguienteFecha());

        var texto = await respuesta.Content.ReadAsStringAsync(Cancelacion);

        texto.ShouldNotContain("mesa", Case.Insensitive);
    }

    [Fact]
    public async Task Sin_parametros_es_un_422_que_dice_que_campos_faltan()
    {
        using var respuesta = await Api.CrearCliente().GetAsync(
            new Uri("/api/v1/negocios/bar-la-plaza/disponibilidad", UriKind.Relative), Cancelacion);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        respuesta.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var problema = await LeerProblemaAsync(respuesta);
        problema.Code.ShouldBe("validacion.invalida");
        problema.Errors!.Keys.ShouldBe(["fecha", "comensales"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("03/10/2026")]  // ambigua: 3 de octubre o 10 de marzo según el país
    [InlineData("2026-13-01")]
    [InlineData("mañana")]
    public async Task Una_fecha_que_no_es_aaaa_mm_dd_se_rechaza(string fecha)
    {
        using var respuesta = await Api.CrearCliente().GetAsync(
            new Uri($"/api/v1/negocios/bar-la-plaza/disponibilidad?fecha={Uri.EscapeDataString(fecha)}&comensales=2", UriKind.Relative), Cancelacion);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await LeerProblemaAsync(respuesta)).Errors!.ShouldContainKey("fecha");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("dos")]
    [InlineData("2.5")]
    public async Task Un_numero_de_comensales_que_no_es_un_entero_positivo_se_rechaza(string comensales)
    {
        using var respuesta = await Api.CrearCliente().GetAsync(
            new Uri($"/api/v1/negocios/bar-la-plaza/disponibilidad?fecha=2026-10-03&comensales={Uri.EscapeDataString(comensales)}", UriKind.Relative), Cancelacion);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await LeerProblemaAsync(respuesta)).Errors!.ShouldContainKey("comensales");
    }

    [Fact]
    public async Task Un_grupo_mayor_que_el_maximo_online_es_un_422_con_su_codigo_de_negocio()
    {
        using var respuesta = await DisponibilidadAsync(ApiConBaseDeDatos.NegocioDosMesas, new DateOnly(2026, 10, 3), comensales: 11);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await LeerProblemaAsync(respuesta)).Code.ShouldBe("reserva.demasiados_comensales_online");
    }

    [Fact]
    public async Task Una_fecha_mas_alla_del_plazo_de_reserva_es_un_422()
    {
        using var respuesta = await DisponibilidadAsync(ApiConBaseDeDatos.NegocioDosMesas, new DateOnly(2027, 1, 1));

        respuesta.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await LeerProblemaAsync(respuesta)).Code.ShouldBe("reserva.demasiado_lejos");
    }

    [Fact]
    public async Task La_disponibilidad_de_un_negocio_inexistente_es_un_404()
    {
        using var respuesta = await DisponibilidadAsync("no-existe", new DateOnly(2026, 10, 3));

        respuesta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Una_reserva_quita_de_la_disponibilidad_las_horas_que_se_pisan_con_ella()
    {
        var fecha = Api.SiguienteFecha();
        (await ReservarAsync(ApiConBaseDeDatos.NegocioUnaMesa, fecha, "21:00")).StatusCode.ShouldBe(HttpStatusCode.Created);

        var horas = await HorasDisponiblesAsync(ApiConBaseDeDatos.NegocioUnaMesa, fecha);

        // La reserva ocupa de 21:00 a 22:30: se pisan las franjas de 20:00 a 22:00, y la de 22:30
        // empieza justo cuando termina, así que sigue libre.
        horas.ShouldNotContain("20:00");
        horas.ShouldNotContain("21:00");
        horas.ShouldNotContain("22:00");
        horas.ShouldContain("22:30");
        horas.Take(6).ShouldBe(["13:00", "13:30", "14:00", "14:30", "15:00", "15:30"]);
    }
}
