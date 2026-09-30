using System.Net;
using System.Net.Http.Json;

using Reservas.Api.Contratos;

using Shouldly;

namespace Reservas.Api.Tests;

/// <summary>Lo que hace el personal con las reservas: agenda, reservas por teléfono y estados.</summary>
public class GestionPersonalTests(ApiConPersonal api) : PruebaPersonal(api), IClassFixture<ApiConPersonal>
{
    private static string RutaAgenda(DateOnly fecha) => $"/api/v1/gestion/agenda?fecha={Formatos.DeFecha(fecha)}";

    [Fact]
    public async Task Una_reserva_apuntada_por_el_personal_nace_confirmada_y_aparece_en_la_agenda_con_la_hora_local()
    {
        var fecha = Api.SiguienteFecha();
        var personal = await ClienteDeAsync(ApiConPersonal.PersonalA);

        var reserva = await ApuntarReservaAsync(personal, fecha, "21:00", 3);

        reserva.Estado.ShouldBe("confirmada");
        reserva.Fecha.ShouldBe(Formatos.DeFecha(fecha));
        reserva.Hora.ShouldBe("21:00");
        reserva.Comensales.ShouldBe(3);
        reserva.Cliente.Telefono.ShouldBe("611 222 333");
        reserva.MesaIds.Count.ShouldBe(1);

        var agenda = await LeerAsync<AgendaRespuesta>(await ObtenerAsync(personal, RutaAgenda(fecha)));
        agenda.Fecha.ShouldBe(Formatos.DeFecha(fecha));
        var enAgenda = agenda.Reservas.Single();
        enAgenda.Id.ShouldBe(reserva.Id);
        enAgenda.Estado.ShouldBe("confirmada");
        enAgenda.Hora.ShouldBe("21:00");
        enAgenda.MesaIds.ShouldBe(reserva.MesaIds);
    }

    [Fact]
    public async Task La_agenda_de_un_dia_esta_ordenada_por_hora_y_no_mezcla_otros_dias()
    {
        var fecha = Api.SiguienteFecha();
        var otroDia = Api.SiguienteFecha();
        var personal = await ClienteDeAsync(ApiConPersonal.PersonalA);

        var tarde = await ApuntarReservaAsync(personal, fecha, "22:00");
        var temprano = await ApuntarReservaAsync(personal, fecha, "13:30");
        await ApuntarReservaAsync(personal, otroDia, "20:00");

        var agenda = await LeerAsync<AgendaRespuesta>(await ObtenerAsync(personal, RutaAgenda(fecha)));

        agenda.Reservas.Select(r => r.Id).ShouldBe([temprano.Id, tarde.Id]);
    }

    [Fact]
    public async Task Una_reserva_de_internet_aparece_pendiente_y_el_personal_puede_cancelarla()
    {
        var fecha = Api.SiguienteFecha();
        using var publica = await ReservarAsync(ApiConBaseDeDatos.NegocioDosMesas, fecha, "20:30");
        var personal = await ClienteDeAsync(ApiConPersonal.PersonalA);

        var agenda = await LeerAsync<AgendaRespuesta>(await ObtenerAsync(personal, RutaAgenda(fecha)));
        var reserva = agenda.Reservas.Single();
        reserva.Estado.ShouldBe("pendiente");

        using var cancelacion = await PostAsync(personal, $"/api/v1/gestion/reservas/{reserva.Id}/cancelar");

        cancelacion.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await LeerAsync<ReservaGestionRespuesta>(cancelacion)).Estado.ShouldBe("cancelada");
    }

    [Fact]
    public async Task Una_reserva_recorre_llegada_y_completar_y_una_cancelada_libera_la_mesa()
    {
        var fecha = Api.SiguienteFecha();
        var personal = await ClienteDeAsync(ApiConPersonal.PersonalB);

        // «una-mesa» solo tiene una mesa: mientras haya una reserva a las 21:00, esa hora no se ofrece.
        var reserva = await ApuntarReservaAsync(personal, fecha, "21:00");
        (await HorasDisponiblesAsync(ApiConBaseDeDatos.NegocioUnaMesa, fecha)).ShouldNotContain("21:00");

        using var sentada = await PostAsync(personal, $"/api/v1/gestion/reservas/{reserva.Id}/llegada");
        sentada.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await LeerAsync<ReservaGestionRespuesta>(sentada)).Estado.ShouldBe("sentada");
        (await HorasDisponiblesAsync(ApiConBaseDeDatos.NegocioUnaMesa, fecha)).ShouldNotContain("21:00");

        using var completada = await PostAsync(personal, $"/api/v1/gestion/reservas/{reserva.Id}/completar");
        completada.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await LeerAsync<ReservaGestionRespuesta>(completada)).Estado.ShouldBe("completada");
        (await HorasDisponiblesAsync(ApiConBaseDeDatos.NegocioUnaMesa, fecha)).ShouldContain("21:00", "una reserva completada libera la mesa");
    }

    [Fact]
    public async Task Cancelar_una_reserva_apuntada_libera_su_mesa_para_el_publico()
    {
        var fecha = Api.SiguienteFecha();
        var personal = await ClienteDeAsync(ApiConPersonal.PersonalB);
        var reserva = await ApuntarReservaAsync(personal, fecha, "21:00");

        using var cancelacion = await PostAsync(personal, $"/api/v1/gestion/reservas/{reserva.Id}/cancelar");

        cancelacion.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await HorasDisponiblesAsync(ApiConBaseDeDatos.NegocioUnaMesa, fecha)).ShouldContain("21:00");
    }

    [Fact]
    public async Task Las_transiciones_que_no_corresponden_al_estado_son_un_409()
    {
        var fecha = Api.SiguienteFecha();
        var personal = await ClienteDeAsync(ApiConPersonal.PersonalA);
        var reserva = await ApuntarReservaAsync(personal, fecha);

        // Confirmada: no se puede completar sin haber sentado al grupo.
        using var completar = await PostAsync(personal, $"/api/v1/gestion/reservas/{reserva.Id}/completar");
        completar.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await LeerProblemaAsync(completar)).Code.ShouldBe("reserva.transicion_invalida");

        using var llegada = await PostAsync(personal, $"/api/v1/gestion/reservas/{reserva.Id}/llegada");
        llegada.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Sentada: ya no se puede sentar otra vez ni cancelar.
        using var otraLlegada = await PostAsync(personal, $"/api/v1/gestion/reservas/{reserva.Id}/llegada");
        using var cancelar = await PostAsync(personal, $"/api/v1/gestion/reservas/{reserva.Id}/cancelar");

        otraLlegada.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        cancelar.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task No_se_puede_dar_por_no_presentado_a_un_grupo_antes_de_que_pase_el_margen()
    {
        var fecha = Api.SiguienteFecha();
        var personal = await ClienteDeAsync(ApiConPersonal.PersonalA);
        var reserva = await ApuntarReservaAsync(personal, fecha);

        using var respuesta = await PostAsync(personal, $"/api/v1/gestion/reservas/{reserva.Id}/no-presentada");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await LeerProblemaAsync(respuesta)).Code.ShouldBe("reserva.aun_no_es_hora");
    }

    [Fact]
    public async Task Una_reserva_inexistente_es_un_404_en_todas_las_operaciones()
    {
        var personal = await ClienteDeAsync(ApiConPersonal.PersonalA);

        foreach (var accion in new[] { "cancelar", "llegada", "completar", "no-presentada" })
        {
            using var respuesta = await PostAsync(personal, $"/api/v1/gestion/reservas/{Guid.NewGuid()}/{accion}");
            respuesta.StatusCode.ShouldBe(HttpStatusCode.NotFound, accion);
        }
    }

    [Fact]
    public async Task Apuntar_una_reserva_exige_la_clave_de_idempotencia_y_un_reintento_no_la_duplica()
    {
        var fecha = Api.SiguienteFecha();
        var personal = await ClienteDeAsync(ApiConPersonal.PersonalA);
        var solicitud = new SolicitudReservaDto(Formatos.DeFecha(fecha), "21:00", 2, new ClienteDto("Marta Ruiz", "marta@example.com", null));

        using var sinClave = await EnviarAsync(personal, HttpMethod.Post, "/api/v1/gestion/reservas", solicitud);
        sinClave.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var primera = await EnviarAsync(personal, HttpMethod.Post, "/api/v1/gestion/reservas", solicitud, "clave-de-personal-0001-" + fecha.DayNumber);
        using var repetida = await EnviarAsync(personal, HttpMethod.Post, "/api/v1/gestion/reservas", solicitud, "clave-de-personal-0001-" + fecha.DayNumber);

        primera.StatusCode.ShouldBe(HttpStatusCode.Created);
        repetida.StatusCode.ShouldBe(HttpStatusCode.Created);
        repetida.Headers.GetValues("Idempotency-Replayed").ShouldBe(["true"]);
        (await LeerAsync<ReservaGestionRespuesta>(repetida)).Id.ShouldBe((await LeerAsync<ReservaGestionRespuesta>(primera)).Id);

        var agenda = await LeerAsync<AgendaRespuesta>(await ObtenerAsync(personal, RutaAgenda(fecha)));
        agenda.Reservas.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Otra_persona_no_puede_reutilizar_la_clave_de_idempotencia_para_leer_la_respuesta_guardada()
    {
        var fecha = Api.SiguienteFecha();
        var solicitud = new SolicitudReservaDto(Formatos.DeFecha(fecha), "21:00", 2, new ClienteDto("Marta Ruiz", "marta@example.com", null));
        var clave = "clave-compartida-" + fecha.DayNumber;

        using var suya = await EnviarAsync(await ClienteDeAsync(ApiConPersonal.PersonalA), HttpMethod.Post, "/api/v1/gestion/reservas", solicitud, clave);
        using var ajena = await EnviarAsync(await ClienteDeAsync(ApiConPersonal.PersonalB), HttpMethod.Post, "/api/v1/gestion/reservas", solicitud, clave);

        suya.StatusCode.ShouldBe(HttpStatusCode.Created);
        ajena.StatusCode.ShouldBe((HttpStatusCode)422);
        (await LeerProblemaAsync(ajena)).Code.ShouldBe("idempotencia.clave_reutilizada");
    }

    [Fact]
    public async Task Sin_hueco_a_esa_hora_apuntar_una_reserva_es_un_409()
    {
        var fecha = Api.SiguienteFecha();
        var personal = await ClienteDeAsync(ApiConPersonal.PersonalB);
        await ApuntarReservaAsync(personal, fecha, "21:00");
        var solicitud = new SolicitudReservaDto(Formatos.DeFecha(fecha), "21:00", 2, new ClienteDto("Otra Persona", NuevoEmail(), null));

        using var respuesta = await EnviarAsync(personal, HttpMethod.Post, "/api/v1/gestion/reservas", solicitud, Guid.NewGuid().ToString("N"));

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await LeerProblemaAsync(respuesta)).Code.ShouldBe("reserva.franja_no_disponible");
    }

    [Fact]
    public async Task Los_datos_mal_formados_son_un_error_de_validacion()
    {
        var personal = await ClienteDeAsync(ApiConPersonal.PersonalA);

        using var agenda = await ObtenerAsync(personal, "/api/v1/gestion/agenda?fecha=10/03/2026");
        using var reserva = await EnviarAsync(personal, HttpMethod.Post, "/api/v1/gestion/reservas", new SolicitudReservaDto(null, "9pm", 0, null), "clave-invalida-0001");

        agenda.StatusCode.ShouldBe((HttpStatusCode)422);
        reserva.StatusCode.ShouldBe((HttpStatusCode)422);
        (await LeerProblemaAsync(reserva)).Errors!.Keys.ShouldBe(["fecha", "hora", "comensales", "cliente"], ignoreOrder: true);
    }

    [Fact]
    public async Task Un_cuerpo_que_no_es_json_es_un_400_tambien_en_la_parte_privada()
    {
        var personal = await ClienteDeAsync(ApiConPersonal.PersonalA);
        using var peticion = new HttpRequestMessage(HttpMethod.Post, "/api/v1/gestion/reservas") { Content = new StringContent("{no es json", System.Text.Encoding.UTF8, "application/json") };
        peticion.Headers.Add("Idempotency-Key", "clave-json-roto-0001");

        using var respuesta = await personal.SendAsync(peticion, Cancelacion);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Las_respuestas_de_la_parte_privada_nunca_incluyen_el_codigo_de_gestion_del_cliente()
    {
        var personal = await ClienteDeAsync(ApiConPersonal.PersonalA);
        var fecha = Api.SiguienteFecha();
        using var publica = await ReservarAsync(ApiConBaseDeDatos.NegocioDosMesas, fecha);
        var codigo = (await LeerReservaAsync(publica)).CodigoGestion!;

        using var agenda = await ObtenerAsync(personal, RutaAgenda(fecha));

        (await agenda.Content.ReadAsStringAsync(Cancelacion)).ShouldNotContain(codigo);
    }
}
