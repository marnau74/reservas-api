using System.Net;

using Shouldly;

namespace Reservas.Api.Tests;

public class GestionDeReservaTests(ApiConBaseDeDatos api) : PruebaApi(api), IClassFixture<ApiConBaseDeDatos>
{
    private async Task<string> ReservarYObtenerCodigoAsync(string slug, DateOnly fecha, string hora = "21:00")
    {
        using var respuesta = await ReservarAsync(slug, fecha, hora);
        respuesta.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await LeerReservaAsync(respuesta)).CodigoGestion!;
    }

    [Fact]
    public async Task El_recorrido_completo_de_una_reserva()
    {
        var codigo = await ReservarYObtenerCodigoAsync(ApiConBaseDeDatos.NegocioDosMesas, Api.SiguienteFecha());

        // Consultar: pendiente, y la respuesta ya no repite el código secreto.
        using var consulta = await GestionarAsync(codigo);
        consulta.StatusCode.ShouldBe(HttpStatusCode.OK);
        var consultada = await LeerReservaAsync(consulta);
        consultada.Estado.ShouldBe("pendiente");
        consultada.CodigoGestion.ShouldBeNull();
        consultada.CaducaEn.ShouldNotBeNull();

        // Confirmar: pasa a confirmada y deja de tener plazo de caducidad.
        using var confirmacion = await GestionarAsync(codigo, "confirmar");
        confirmacion.StatusCode.ShouldBe(HttpStatusCode.OK);
        var confirmada = await LeerReservaAsync(confirmacion);
        confirmada.Estado.ShouldBe("confirmada");
        confirmada.CaducaEn.ShouldBeNull();

        // Cancelar.
        using var cancelacion = await GestionarAsync(codigo, "cancelar");
        cancelacion.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await LeerReservaAsync(cancelacion)).Estado.ShouldBe("cancelada");

        // Ya cancelada: no se puede cancelar ni confirmar de nuevo.
        using var otraCancelacion = await GestionarAsync(codigo, "cancelar");
        otraCancelacion.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await LeerProblemaAsync(otraCancelacion)).Code.ShouldBe("reserva.transicion_invalida");

        using var otraConfirmacion = await GestionarAsync(codigo, "confirmar");
        otraConfirmacion.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // Y la consulta refleja el estado final.
        using var final = await GestionarAsync(codigo);
        (await LeerReservaAsync(final)).Estado.ShouldBe("cancelada");
    }

    [Fact]
    public async Task Un_codigo_desconocido_es_un_404_en_las_tres_operaciones()
    {
        foreach (var accion in new string?[] { null, "confirmar", "cancelar" })
        {
            using var respuesta = await GestionarAsync("codigo-que-no-existe-0000", accion);

            respuesta.StatusCode.ShouldBe(HttpStatusCode.NotFound, $"acción: {accion ?? "consultar"}");
            (await LeerProblemaAsync(respuesta)).Code.ShouldBe("reserva.no_encontrada");
        }
    }

    [Fact]
    public async Task Una_reserva_pendiente_no_se_puede_confirmar_pasado_el_plazo()
    {
        var codigo = await ReservarYObtenerCodigoAsync(ApiConBaseDeDatos.NegocioDosMesas, Api.SiguienteFecha());

        Api.Reloj.Advance(TimeSpan.FromMinutes(31));

        using var respuesta = await GestionarAsync(codigo, "confirmar");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await LeerProblemaAsync(respuesta)).Code.ShouldBe("reserva.caducada");

        // Sigue pendiente: el plazo vencido no la modifica.
        using var consulta = await GestionarAsync(codigo);
        (await LeerReservaAsync(consulta)).Estado.ShouldBe("pendiente");
    }

    [Fact]
    public async Task Cancelar_libera_la_hora_para_que_otra_persona_reserve()
    {
        var fecha = Api.SiguienteFecha();
        var codigo = await ReservarYObtenerCodigoAsync(ApiConBaseDeDatos.NegocioUnaMesa, fecha);

        // Con una sola mesa, esa hora ya no se puede reservar.
        using var ocupada = await ReservarAsync(ApiConBaseDeDatos.NegocioUnaMesa, fecha, "21:00");
        ocupada.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        using var cancelacion = await GestionarAsync(codigo, "cancelar");
        cancelacion.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var libre = await ReservarAsync(ApiConBaseDeDatos.NegocioUnaMesa, fecha, "21:00");
        libre.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Confirmar_no_libera_la_mesa()
    {
        var fecha = Api.SiguienteFecha();
        var codigo = await ReservarYObtenerCodigoAsync(ApiConBaseDeDatos.NegocioUnaMesa, fecha);

        (await GestionarAsync(codigo, "confirmar")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await HorasDisponiblesAsync(ApiConBaseDeDatos.NegocioUnaMesa, fecha)).ShouldNotContain("21:00");
    }

    [Fact]
    public async Task La_hora_de_la_reserva_se_muestra_en_la_zona_horaria_del_negocio()
    {
        // Una fecha con horario de invierno (UTC+1) y otra con horario de verano (UTC+2): la hora
        // local (21:00) es la misma, el instante UTC no.
        var verano = await ReservarYObtenerCodigoAsync(ApiConBaseDeDatos.NegocioDosMesas, new DateOnly(2026, 9, 20));
        var invierno = await ReservarYObtenerCodigoAsync(ApiConBaseDeDatos.NegocioDosMesas, new DateOnly(2026, 10, 30));

        var deVerano = await LeerReservaAsync(await GestionarAsync(verano));
        var deInvierno = await LeerReservaAsync(await GestionarAsync(invierno));

        deVerano.Hora.ShouldBe("21:00");
        deInvierno.Hora.ShouldBe("21:00");
        deVerano.Inicio.ShouldBe(new DateTimeOffset(2026, 9, 20, 19, 0, 0, TimeSpan.Zero));
        deInvierno.Inicio.ShouldBe(new DateTimeOffset(2026, 10, 30, 20, 0, 0, TimeSpan.Zero));
    }
}
