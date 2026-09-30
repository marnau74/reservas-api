using System.Net;
using System.Text.RegularExpressions;

using Microsoft.EntityFrameworkCore;

using Reservas.Api.Contratos;
using Reservas.Dominio.Correos;

using Shouldly;

namespace Reservas.Api.Tests;

/// <summary>Como la API real: el código de gestión no se devuelve, solo llega por correo.</summary>
public sealed class ApiFlujoPorCorreo(Reservas.Tests.Comunes.ServidorPostgres servidor) : ApiConPersonal(servidor)
{
    protected override IReadOnlyDictionary<string, string?> Ajustes =>
        new Dictionary<string, string?>(base.Ajustes) { ["Publico:MostrarCodigoGestion"] = "false" };
}

public partial class CorreosApiTests(ApiFlujoPorCorreo api) : PruebaPersonal(api), IClassFixture<ApiFlujoPorCorreo>
{
    [GeneratedRegex(@"/reservas/([A-Za-z0-9_-]{22})")]
    private static partial Regex PatronCodigo();

    private static string CodigoDe(Reservas.Aplicacion.Abstracciones.MensajeCorreo correo) =>
        PatronCodigo().Match(correo.Cuerpo).Groups[1].Value;

    private async Task<int> ContarCorreosAsync()
    {
        await using var db = Api.NuevoContexto();
        return await db.CorreosPendientes.CountAsync(Cancelacion);
    }

    [Fact]
    public async Task El_recorrido_del_cliente_solo_con_lo_que_le_llega_por_correo()
    {
        var fecha = Api.SiguienteFecha();
        var email = NuevoEmail();

        // 1. Reserva por internet: la respuesta NO trae el código ni la ubicación que lo contiene.
        using var creada = await ReservarAsync(ApiConBaseDeDatos.NegocioDosMesas, fecha, email: email);
        creada.StatusCode.ShouldBe(HttpStatusCode.Created);
        creada.Headers.Location.ShouldBeNull();
        (await LeerReservaAsync(creada)).CodigoGestion.ShouldBeNull();

        // 2. El correo con su enlace sale de la bandeja.
        (await ProcesarCorreosAsync()).Enviados.ShouldBe(1);
        var solicitud = Api.Enviador.Enviados.Last(m => m.Destinatario == email);
        solicitud.Asunto.ShouldStartWith("Confirma tu reserva");
        var codigo = CodigoDe(solicitud);
        codigo.Length.ShouldBe(22);

        // 3. Con ese código consulta y confirma.
        using var consulta = await GestionarAsync(codigo);
        (await LeerReservaAsync(consulta)).Estado.ShouldBe("pendiente");

        using var confirmada = await GestionarAsync(codigo, "confirmar");
        confirmada.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await ProcesarCorreosAsync()).Enviados.ShouldBe(1);
        Api.Enviador.Enviados.Last(m => m.Destinatario == email).Asunto.ShouldStartWith("Reserva confirmada");

        // 4. Y cancela: otro correo.
        using var cancelada = await GestionarAsync(codigo, "cancelar");
        cancelada.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await ProcesarCorreosAsync()).Enviados.ShouldBe(1);
        Api.Enviador.Enviados.Last(m => m.Destinatario == email).Asunto.ShouldStartWith("Reserva cancelada");
    }

    [Fact]
    public async Task El_correo_de_una_reserva_por_internet_tiene_la_hora_local_y_el_enlace_de_gestion()
    {
        var fecha = Api.SiguienteFecha();
        var email = NuevoEmail();
        using var creada = await ReservarAsync(ApiConBaseDeDatos.NegocioDosMesas, fecha, "21:30", 2, email: email);
        creada.StatusCode.ShouldBe(HttpStatusCode.Created);

        await ProcesarCorreosAsync();

        var cuerpo = Api.Enviador.Enviados.Last(m => m.Destinatario == email).Cuerpo;
        cuerpo.ShouldContain("21:30");
        cuerpo.ShouldContain("2 personas");
        cuerpo.ShouldContain("http://localhost:3000/reservas/");
    }

    [Fact]
    public async Task Si_el_servidor_de_correo_esta_caido_la_reserva_se_hace_y_el_correo_sale_cuando_vuelve()
    {
        var fecha = Api.SiguienteFecha();
        var email = NuevoEmail();
        Api.Enviador.Caido = true;

        using var creada = await ReservarAsync(ApiConBaseDeDatos.NegocioDosMesas, fecha, email: email);
        creada.StatusCode.ShouldBe(HttpStatusCode.Created, "que falle el correo no impide reservar");

        (await ProcesarCorreosAsync()).Fallidos.ShouldBe(1);
        Api.Enviador.Enviados.ShouldNotContain(m => m.Destinatario == email);

        // El servidor vuelve; el correo se reintenta pasado el tiempo de espera.
        Api.Enviador.Caido = false;
        (await ProcesarCorreosAsync()).Enviados.ShouldBe(0, "todavía no toca reintentar");
        Api.Reloj.Advance(TimeSpan.FromMinutes(1));

        (await ProcesarCorreosAsync()).Enviados.ShouldBe(1);
        Api.Enviador.Enviados.ShouldContain(m => m.Destinatario == email);
    }

    [Fact]
    public async Task Con_veinte_peticiones_simultaneas_hay_exactamente_un_correo_por_cada_reserva_creada()
    {
        var fecha = Api.SiguienteFecha();
        var antes = await ContarCorreosAsync();

        var respuestas = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => ReservarAsync(ApiConBaseDeDatos.NegocioUnaMesa, fecha)));

        var creadas = respuestas.Count(r => r.StatusCode == HttpStatusCode.Created);
        creadas.ShouldBe(1, "solo hay una mesa");
        foreach (var respuesta in respuestas)
        {
            respuesta.Dispose();
        }

        // Ni una reserva sin su correo, ni correos de las reservas que perdieron la carrera.
        (await ContarCorreosAsync()).ShouldBe(antes + creadas);
    }

    [Fact]
    public async Task El_personal_que_apunta_o_cancela_una_reserva_genera_los_correos_al_cliente()
    {
        var personal = await ClienteDeAsync(ApiConPersonal.PersonalA);
        var fecha = Api.SiguienteFecha();
        var solicitud = new SolicitudReservaDto(Formatos.DeFecha(fecha), "21:00", 2, new ClienteDto("Luis Gómez", "luis@example.com", null));

        using var apuntada = await EnviarAsync(personal, HttpMethod.Post, "/api/v1/gestion/reservas", solicitud, Guid.NewGuid().ToString("N"));
        var reserva = await LeerAsync<ReservaGestionRespuesta>(apuntada);

        using var cancelada = await PostAsync(personal, $"/api/v1/gestion/reservas/{reserva.Id}/cancelar");
        cancelada.StatusCode.ShouldBe(HttpStatusCode.OK);

        await ProcesarCorreosAsync();
        var asuntos = Api.Enviador.Enviados.Where(m => m.Destinatario == "luis@example.com").Select(m => m.Asunto).ToList();
        asuntos.Count.ShouldBe(2);
        asuntos.ShouldContain(a => a.StartsWith("Reserva confirmada", StringComparison.Ordinal));
        asuntos.ShouldContain(a => a.StartsWith("Reserva cancelada", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Sentar_o_completar_una_reserva_no_manda_correos()
    {
        var personal = await ClienteDeAsync(ApiConPersonal.PersonalA);
        var reserva = await ApuntarReservaAsync(personal, Api.SiguienteFecha());
        await ProcesarCorreosAsync();
        var antes = Api.Enviador.Enviados.Count;

        (await PostAsync(personal, $"/api/v1/gestion/reservas/{reserva.Id}/llegada")).Dispose();
        (await PostAsync(personal, $"/api/v1/gestion/reservas/{reserva.Id}/completar")).Dispose();

        (await ProcesarCorreosAsync()).Enviados.ShouldBe(0);
        Api.Enviador.Enviados.Count.ShouldBe(antes);
    }
}

/// <summary>Derecho de supresión: el cliente pide que se borren sus datos.</summary>
public class SupresionDeDatosTests(ApiConPersonal api) : PruebaPersonal(api), IClassFixture<ApiConPersonal>
{
    private async Task<string> ReservarYCodigoAsync(string email)
    {
        using var creada = await ReservarAsync(ApiConBaseDeDatos.NegocioDosMesas, Api.SiguienteFecha(), email: email);
        return (await LeerReservaAsync(creada)).CodigoGestion!;
    }

    [Fact]
    public async Task Con_la_reserva_activa_no_se_pueden_borrar_los_datos_y_una_vez_cancelada_si()
    {
        var email = NuevoEmail();
        var codigo = await ReservarYCodigoAsync(email);

        using var activa = await GestionarAsync(codigo, metodo: HttpMethod.Delete);
        activa.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await LeerProblemaAsync(activa)).Code.ShouldBe("reserva.activa");

        (await GestionarAsync(codigo, "cancelar")).Dispose();

        using var borrada = await GestionarAsync(codigo, metodo: HttpMethod.Delete);
        borrada.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // La reserva sigue existiendo, pero sin datos personales.
        using var consulta = await GestionarAsync(codigo);
        var texto = await consulta.Content.ReadAsStringAsync(Cancelacion);
        texto.ShouldNotContain(email);
        texto.ShouldNotContain("Ana Pérez");
        (await LeerReservaAsync(consulta)).Estado.ShouldBe("cancelada");

        await using var db = Api.NuevoContexto();
        (await db.Reservas.AnyAsync(r => r.Cliente.Email == email, Cancelacion)).ShouldBeFalse("el correo ya no está en la base de datos");
    }

    [Fact]
    public async Task Pedirlo_dos_veces_no_es_un_error_y_un_codigo_desconocido_es_un_404()
    {
        var codigo = await ReservarYCodigoAsync(NuevoEmail());
        (await GestionarAsync(codigo, "cancelar")).Dispose();

        using var primera = await GestionarAsync(codigo, metodo: HttpMethod.Delete);
        using var segunda = await GestionarAsync(codigo, metodo: HttpMethod.Delete);
        using var desconocido = await GestionarAsync("codigo-que-no-existe", metodo: HttpMethod.Delete);

        primera.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        segunda.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        desconocido.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task El_personal_ve_la_reserva_anonimizada_en_su_agenda()
    {
        var fecha = Api.SiguienteFecha();
        using var creada = await ReservarAsync(ApiConBaseDeDatos.NegocioDosMesas, fecha, email: NuevoEmail());
        var codigo = (await LeerReservaAsync(creada)).CodigoGestion!;
        (await GestionarAsync(codigo, "cancelar")).Dispose();
        (await GestionarAsync(codigo, metodo: HttpMethod.Delete)).Dispose();

        var personal = await ClienteDeAsync(ApiConPersonal.PersonalA);
        var agenda = await LeerAsync<AgendaRespuesta>(await ObtenerAsync(personal, $"/api/v1/gestion/agenda?fecha={Formatos.DeFecha(fecha)}"));

        var reserva = agenda.Reservas.Single();
        reserva.Cliente.Nombre.ShouldBe("Cliente anonimizado");
        reserva.Cliente.Telefono.ShouldBeNull();
    }
}

/// <summary>Las tareas programadas con los servicios reales de la API y un reloj que avanza el test.</summary>
public class TareasProgramadasApiTests(ApiConPersonal api) : PruebaPersonal(api), IClassFixture<ApiConPersonal>
{
    [Fact]
    public async Task Una_reserva_sin_confirmar_caduca_a_los_30_minutos_y_su_mesa_se_libera()
    {
        var fecha = Api.SiguienteFecha();
        using var creada = await ReservarAsync(ApiConBaseDeDatos.NegocioUnaMesa, fecha, "21:00");
        var codigo = (await LeerReservaAsync(creada)).CodigoGestion!;
        (await HorasDisponiblesAsync(ApiConBaseDeDatos.NegocioUnaMesa, fecha)).ShouldNotContain("21:00");

        Api.Reloj.Advance(TimeSpan.FromMinutes(30));
        (await MantenimientoAsync((m, ct) => m.CaducarPendientesAsync(ct))).ShouldBe(1);

        (await HorasDisponiblesAsync(ApiConBaseDeDatos.NegocioUnaMesa, fecha)).ShouldContain("21:00");
        using var consulta = await GestionarAsync(codigo);
        (await LeerReservaAsync(consulta)).Estado.ShouldBe("cancelada");

        using var confirmar = await GestionarAsync(codigo, "confirmar");
        confirmar.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task El_recordatorio_llega_una_sola_vez_el_dia_antes_de_una_reserva_confirmada()
    {
        var fecha = Api.SiguienteFecha();
        var personal = await ClienteDeAsync(ApiConPersonal.PersonalA);
        var reserva = await ApuntarReservaAsync(personal, fecha, "21:00");
        await ProcesarCorreosAsync();
        var antes = Api.Enviador.Enviados.Count;

        Api.Reloj.SetUtcNow(reserva.Inicio - TimeSpan.FromHours(20));
        (await MantenimientoAsync((m, ct) => m.ProgramarRecordatoriosAsync(ct))).ShouldBe(1);
        (await MantenimientoAsync((m, ct) => m.ProgramarRecordatoriosAsync(ct))).ShouldBe(0);

        (await ProcesarCorreosAsync()).Enviados.ShouldBe(1);
        Api.Enviador.Enviados.Count.ShouldBe(antes + 1);
        Api.Enviador.Enviados[^1].Asunto.ShouldStartWith("Recordatorio");
        Api.Enviador.Enviados[^1].Cuerpo.ShouldContain("21:00");
    }
}

/// <summary>Los servicios en segundo plano de verdad, arrancados con la API y movidos por el reloj de prueba.</summary>
public sealed class ApiConTareasActivas(Reservas.Tests.Comunes.ServidorPostgres servidor) : ApiConPersonal(servidor)
{
    protected override IReadOnlyDictionary<string, string?> Ajustes =>
        new Dictionary<string, string?>(base.Ajustes) { ["Tareas:Activas"] = "true" };
}

public class ServiciosEnSegundoPlanoTests(ApiConTareasActivas api) : PruebaPersonal(api), IClassFixture<ApiConTareasActivas>
{
    [Fact]
    public async Task La_api_envia_sola_los_correos_pendientes_y_caduca_las_reservas_sin_confirmar()
    {
        var fecha = Api.SiguienteFecha();
        var email = NuevoEmail();
        using var creada = await ReservarAsync(ApiConBaseDeDatos.NegocioUnaMesa, fecha, "21:00", email: email);
        var codigo = (await LeerReservaAsync(creada)).CodigoGestion!;

        // Nadie llama a nada: el correo sale porque el servicio en segundo plano lo hace al pasar el tiempo.
        await EsperarAsync(() => Api.Enviador.Enviados.Any(m => m.Destinatario == email), avance: TimeSpan.FromSeconds(10));

        // Y pasada la media hora, la reserva sin confirmar caduca sola.
        await EsperarAsync(async () =>
        {
            using var consulta = await GestionarAsync(codigo);
            return (await LeerReservaAsync(consulta)).Estado == "cancelada";
        }, avance: TimeSpan.FromMinutes(1));
    }

    private async Task EsperarAsync(Func<bool> condicion, TimeSpan avance) =>
        await EsperarAsync(() => Task.FromResult(condicion()), avance);

    private async Task EsperarAsync(Func<Task<bool>> condicion, TimeSpan avance)
    {
        for (var intento = 0; intento < 80; intento++)
        {
            if (await condicion())
            {
                return;
            }

            Api.Reloj.Advance(avance);
            await Task.Delay(150, Cancelacion);
        }

        (await condicion()).ShouldBeTrue("el servicio en segundo plano no hizo su trabajo a tiempo");
    }
}
