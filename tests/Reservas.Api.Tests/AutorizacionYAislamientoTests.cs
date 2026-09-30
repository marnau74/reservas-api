using System.Net;

using Microsoft.EntityFrameworkCore;

using Reservas.Api.Contratos;
using Reservas.Dominio.Gestion;

using Shouldly;

namespace Reservas.Api.Tests;

/// <summary>Qué puede hacer cada rol: sin sesión, personal, encargado y propietario.</summary>
public class AutorizacionTests(ApiConPersonal api) : PruebaPersonal(api), IClassFixture<ApiConPersonal>
{
    private static readonly string Fecha = "2026-09-20";

    // Un endpoint de cada nivel de la parte privada.
    public static TheoryData<string> RutasDePersonal => new() { $"/api/v1/gestion/agenda?fecha={Fecha}" };

    public static TheoryData<string> RutasDeEncargado => new()
    {
        "/api/v1/gestion/salas", "/api/v1/gestion/mesas", "/api/v1/gestion/horarios", "/api/v1/gestion/cierres",
    };

    public static TheoryData<string> RutasDePropietario => new() { "/api/v1/gestion/usuarios" };

    [Theory]
    [MemberData(nameof(RutasDePersonal))]
    [MemberData(nameof(RutasDeEncargado))]
    [MemberData(nameof(RutasDePropietario))]
    public async Task Sin_sesion_toda_la_parte_privada_responde_401(string ruta)
    {
        using var respuesta = await ObtenerAsync(Api.CrearCliente(), ruta);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(RutasDePersonal))]
    public async Task El_personal_ve_la_agenda(string ruta)
    {
        using var respuesta = await ObtenerAsync(await ClienteDeAsync(ApiConPersonal.PersonalA), ruta);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [MemberData(nameof(RutasDeEncargado))]
    [MemberData(nameof(RutasDePropietario))]
    public async Task El_personal_no_puede_configurar_el_local_ni_gestionar_usuarios(string ruta)
    {
        using var respuesta = await ObtenerAsync(await ClienteDeAsync(ApiConPersonal.PersonalA), ruta);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [MemberData(nameof(RutasDePersonal))]
    [MemberData(nameof(RutasDeEncargado))]
    public async Task El_encargado_puede_lo_del_personal_y_configurar_el_local(string ruta)
    {
        using var respuesta = await ObtenerAsync(await ClienteDeAsync(ApiConPersonal.EncargadoA), ruta);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [MemberData(nameof(RutasDePropietario))]
    public async Task El_encargado_no_puede_gestionar_usuarios(string ruta)
    {
        using var respuesta = await ObtenerAsync(await ClienteDeAsync(ApiConPersonal.EncargadoA), ruta);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [MemberData(nameof(RutasDePersonal))]
    [MemberData(nameof(RutasDeEncargado))]
    [MemberData(nameof(RutasDePropietario))]
    public async Task El_propietario_puede_todo(string ruta)
    {
        using var respuesta = await ObtenerAsync(await ClienteDeAsync(ApiConPersonal.PropietarioA), ruta);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task El_personal_tampoco_puede_crear_ni_borrar_en_la_configuracion_del_local()
    {
        var cliente = await ClienteDeAsync(ApiConPersonal.PersonalA);

        using var crear = await PostAsync(cliente, "/api/v1/gestion/salas", new CrearSalaDto("Terraza"));
        using var borrar = await BorrarAsync(cliente, $"/api/v1/gestion/mesas/{Guid.NewGuid()}");
        using var alta = await PostAsync(cliente, "/api/v1/gestion/usuarios", new CrearUsuarioDto(NuevoEmail(), "Nuevo", "personal", "Contrasena-Nueva-1"));

        crear.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        borrar.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        alta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task La_parte_publica_sigue_funcionando_sin_sesion_y_con_la_sesion_de_otro_negocio()
    {
        var fecha = Api.SiguienteFecha();
        var ruta = $"/api/v1/negocios/{ApiConBaseDeDatos.NegocioUnaMesa}/disponibilidad?fecha={Formatos.DeFecha(fecha)}&comensales=2";

        using var anonima = await ObtenerAsync(Api.CrearCliente(), ruta);

        // Una persona de A consulta la disponibilidad pública de B: la sesión no la restringe.
        using var conSesionDeA = await ObtenerAsync(await ClienteDeAsync(ApiConPersonal.PersonalA), ruta);

        anonima.StatusCode.ShouldBe(HttpStatusCode.OK);
        conSesionDeA.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await LeerAsync<DisponibilidadRespuesta>(conSesionDeA)).Franjas.Count.ShouldBe(12);
    }
}

/// <summary>Un negocio no ve, no modifica ni sabe que existe nada del otro.</summary>
public class AislamientoTests(ApiConPersonal api) : PruebaPersonal(api), IClassFixture<ApiConPersonal>
{
    private async Task<Guid> IdDeMesaDeAsync(string slug)
    {
        await using var db = Api.NuevoContexto();
        var negocio = await db.Negocios.SingleAsync(n => n.Slug == slug, Cancelacion);

        return await db.Mesas.IgnoreQueryFilters()
            .Where(m => EF.Property<Guid>(m, "NegocioId") == negocio.Id)
            .Select(m => m.Id)
            .FirstAsync(Cancelacion);
    }

    [Fact]
    public async Task Cada_negocio_lista_solo_sus_propias_mesas_salas_y_usuarios()
    {
        var a = await ClienteDeAsync(ApiConPersonal.PropietarioA);
        var b = await ClienteDeAsync(ApiConPersonal.PropietarioB);

        (await LeerAsync<MesaRespuesta[]>(await ObtenerAsync(a, "/api/v1/gestion/mesas"))).Length.ShouldBe(2);
        (await LeerAsync<MesaRespuesta[]>(await ObtenerAsync(b, "/api/v1/gestion/mesas"))).Length.ShouldBe(1);

        var usuariosA = await LeerAsync<UsuarioRespuesta[]>(await ObtenerAsync(a, "/api/v1/gestion/usuarios"));
        var usuariosB = await LeerAsync<UsuarioRespuesta[]>(await ObtenerAsync(b, "/api/v1/gestion/usuarios"));
        usuariosA.Select(u => u.Email).ShouldBe([ApiConPersonal.PropietarioA, ApiConPersonal.EncargadoA, ApiConPersonal.PersonalA], ignoreOrder: true);
        usuariosB.Select(u => u.Email).ShouldBe([ApiConPersonal.PropietarioB, ApiConPersonal.EncargadoB, ApiConPersonal.PersonalB], ignoreOrder: true);

        var salasA = await LeerAsync<SalaRespuesta[]>(await ObtenerAsync(a, "/api/v1/gestion/salas"));
        var salasB = await LeerAsync<SalaRespuesta[]>(await ObtenerAsync(b, "/api/v1/gestion/salas"));
        salasA.Select(s => s.Id).ShouldNotContain(salasB[0].Id);
    }

    [Fact]
    public async Task Un_encargado_no_puede_borrar_una_mesa_de_otro_negocio_y_recibe_404()
    {
        var mesaDeB = await IdDeMesaDeAsync(ApiConBaseDeDatos.NegocioUnaMesa);
        var encargadoA = await ClienteDeAsync(ApiConPersonal.EncargadoA);

        using var respuesta = await BorrarAsync(encargadoA, $"/api/v1/gestion/mesas/{mesaDeB}");

        // 404, no 403: no se revela ni que esa mesa exista.
        respuesta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await LeerProblemaAsync(respuesta)).Code.ShouldBe("mesa.no_encontrada");

        await using var db = Api.NuevoContexto();
        (await db.Mesas.IgnoreQueryFilters().AnyAsync(m => m.Id == mesaDeB, Cancelacion)).ShouldBeTrue("la mesa de B sigue existiendo");
    }

    [Fact]
    public async Task Un_encargado_no_puede_crear_una_mesa_en_una_sala_de_otro_negocio()
    {
        var salasB = await LeerAsync<SalaRespuesta[]>(await ObtenerAsync(await ClienteDeAsync(ApiConPersonal.EncargadoB), "/api/v1/gestion/salas"));
        var encargadoA = await ClienteDeAsync(ApiConPersonal.EncargadoA);

        using var respuesta = await PostAsync(encargadoA, "/api/v1/gestion/mesas", new CrearMesaDto(salasB[0].Id, "Mesa intrusa", 1, 4, false));

        respuesta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await LeerProblemaAsync(respuesta)).Code.ShouldBe("sala.no_encontrada");
    }

    [Fact]
    public async Task Un_encargado_no_puede_borrar_horarios_ni_cierres_de_otro_negocio()
    {
        var encargadoB = await ClienteDeAsync(ApiConPersonal.EncargadoB);
        using var cierre = await PostAsync(encargadoB, "/api/v1/gestion/cierres", new CrearCierreDto("2026-12-25", null, "Navidad"));
        var cierreDeB = await LeerAsync<CierreRespuesta>(cierre);
        var horarioDeB = (await LeerAsync<HorarioRespuesta[]>(await ObtenerAsync(encargadoB, "/api/v1/gestion/horarios")))[0];

        var encargadoA = await ClienteDeAsync(ApiConPersonal.EncargadoA);
        using var borrarCierre = await BorrarAsync(encargadoA, $"/api/v1/gestion/cierres/{cierreDeB.Id}");
        using var borrarHorario = await BorrarAsync(encargadoA, $"/api/v1/gestion/horarios/{horarioDeB.Id}");

        borrarCierre.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        borrarHorario.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await LeerAsync<CierreRespuesta[]>(await ObtenerAsync(encargadoB, "/api/v1/gestion/cierres"))).ShouldContain(c => c.Id == cierreDeB.Id);
    }

    [Fact]
    public async Task Un_propietario_no_puede_desactivar_a_un_usuario_de_otro_negocio()
    {
        var usuariosB = await LeerAsync<UsuarioRespuesta[]>(await ObtenerAsync(await ClienteDeAsync(ApiConPersonal.PropietarioB), "/api/v1/gestion/usuarios"));
        var victima = usuariosB.Single(u => u.Email == ApiConPersonal.PersonalB);

        using var respuesta = await BorrarAsync(await ClienteDeAsync(ApiConPersonal.PropietarioA), $"/api/v1/gestion/usuarios/{victima.Id}");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await LeerProblemaAsync(respuesta)).Code.ShouldBe("usuario.no_encontrado");

        // Sigue pudiendo entrar.
        using var entrada = await LoginAsync(ApiConPersonal.PersonalB);
        entrada.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task El_personal_solo_ve_y_toca_las_reservas_de_su_negocio()
    {
        var fecha = Api.SiguienteFecha();
        var personalB = await ClienteDeAsync(ApiConPersonal.PersonalB);
        var reservaDeB = await ApuntarReservaAsync(personalB, fecha);

        var personalA = await ClienteDeAsync(ApiConPersonal.PersonalA);
        var agendaA = await LeerAsync<AgendaRespuesta>(await ObtenerAsync(personalA, $"/api/v1/gestion/agenda?fecha={Formatos.DeFecha(fecha)}"));
        var agendaB = await LeerAsync<AgendaRespuesta>(await ObtenerAsync(personalB, $"/api/v1/gestion/agenda?fecha={Formatos.DeFecha(fecha)}"));

        agendaA.Reservas.ShouldBeEmpty();
        agendaB.Reservas.Select(r => r.Id).ShouldBe([reservaDeB.Id]);

        // Y no puede cancelarla, sentarla, completarla ni marcarla como no presentada.
        foreach (var accion in new[] { "cancelar", "llegada", "completar", "no-presentada" })
        {
            using var intento = await PostAsync(personalA, $"/api/v1/gestion/reservas/{reservaDeB.Id}/{accion}");
            intento.StatusCode.ShouldBe(HttpStatusCode.NotFound, accion);
            (await LeerProblemaAsync(intento)).Code.ShouldBe("reserva.no_encontrada");
        }

        var despues = await LeerAsync<AgendaRespuesta>(await ObtenerAsync(personalB, $"/api/v1/gestion/agenda?fecha={Formatos.DeFecha(fecha)}"));
        despues.Reservas.Single().Estado.ShouldBe("confirmada");
    }

    [Fact]
    public async Task El_negocio_lo_decide_la_sesion_y_no_un_parametro_de_la_peticion()
    {
        var fecha = Api.SiguienteFecha();
        await ApuntarReservaAsync(await ClienteDeAsync(ApiConPersonal.PersonalB), fecha);
        var idDeB = (await LeerAsync<UsuarioRespuesta[]>(await ObtenerAsync(await ClienteDeAsync(ApiConPersonal.PropietarioB), "/api/v1/gestion/usuarios")))[0].Id;

        var personalA = await ClienteDeAsync(ApiConPersonal.PersonalA);
        using var respuesta = await ObtenerAsync(personalA, $"/api/v1/gestion/agenda?fecha={Formatos.DeFecha(fecha)}&negocioId={idDeB}&slug={ApiConBaseDeDatos.NegocioUnaMesa}");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await LeerAsync<AgendaRespuesta>(respuesta)).Reservas.ShouldBeEmpty();
    }

    [Fact]
    public async Task Las_reservas_por_internet_de_un_negocio_no_aparecen_en_la_agenda_del_otro()
    {
        var fecha = Api.SiguienteFecha();
        using var publica = await ReservarAsync(ApiConBaseDeDatos.NegocioDosMesas, fecha);
        publica.StatusCode.ShouldBe(HttpStatusCode.Created);

        var agendaA = await LeerAsync<AgendaRespuesta>(await ObtenerAsync(await ClienteDeAsync(ApiConPersonal.PersonalA), $"/api/v1/gestion/agenda?fecha={Formatos.DeFecha(fecha)}"));
        var agendaB = await LeerAsync<AgendaRespuesta>(await ObtenerAsync(await ClienteDeAsync(ApiConPersonal.PersonalB), $"/api/v1/gestion/agenda?fecha={Formatos.DeFecha(fecha)}"));

        agendaA.Reservas.Count.ShouldBe(1);
        agendaA.Reservas[0].Estado.ShouldBe(EstadoReserva.Pendiente.ToString().ToLowerInvariant());
        agendaB.Reservas.ShouldBeEmpty();
    }
}
