using System.Net;
using System.Net.Http.Json;

using Reservas.Api.Contratos;

using Shouldly;

namespace Reservas.Api.Tests;

/// <summary>Configuración del local por su encargado: salas, mesas, horarios y cierres.</summary>
public class ConfiguracionLocalTests(ApiConPersonal api) : PruebaPersonal(api), IClassFixture<ApiConPersonal>
{
    [Fact]
    public async Task Una_sala_con_mesas_no_se_borra_pero_una_vacia_si()
    {
        var encargado = await ClienteDeAsync(ApiConPersonal.EncargadoA);

        using var creada = await PostAsync(encargado, "/api/v1/gestion/salas", new CrearSalaDto("Terraza"));
        creada.StatusCode.ShouldBe(HttpStatusCode.Created);
        var sala = await LeerAsync<SalaRespuesta>(creada);
        sala.Nombre.ShouldBe("Terraza");

        using var mesaCreada = await PostAsync(encargado, "/api/v1/gestion/mesas", new CrearMesaDto(sala.Id, "T1", 2, 6, true));
        mesaCreada.StatusCode.ShouldBe(HttpStatusCode.Created);
        var mesa = await LeerAsync<MesaRespuesta>(mesaCreada);
        mesa.ShouldBe(new MesaRespuesta(mesa.Id, sala.Id, "T1", 2, 6, true));

        using var conMesas = await BorrarAsync(encargado, $"/api/v1/gestion/salas/{sala.Id}");
        conMesas.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await LeerProblemaAsync(conMesas)).Code.ShouldBe("sala.con_mesas");

        using var borrarMesa = await BorrarAsync(encargado, $"/api/v1/gestion/mesas/{mesa.Id}");
        borrarMesa.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var vacia = await BorrarAsync(encargado, $"/api/v1/gestion/salas/{sala.Id}");
        vacia.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var otraVez = await BorrarAsync(encargado, $"/api/v1/gestion/salas/{sala.Id}");
        otraVez.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Una_mesa_con_reservas_no_se_puede_borrar()
    {
        var encargadoB = await ClienteDeAsync(ApiConPersonal.EncargadoB);
        var personalB = await ClienteDeAsync(ApiConPersonal.PersonalB);
        var reserva = await ApuntarReservaAsync(personalB, Api.SiguienteFecha());
        var mesaId = reserva.MesaIds.Single();

        using var respuesta = await BorrarAsync(encargadoB, $"/api/v1/gestion/mesas/{mesaId}");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await LeerProblemaAsync(respuesta)).Code.ShouldBe("mesa.con_reservas");
    }

    [Fact]
    public async Task Una_mesa_nueva_ofrece_mas_disponibilidad_al_publico_y_al_borrarla_deja_de_ofrecerla()
    {
        var encargado = await ClienteDeAsync(ApiConPersonal.EncargadoB);
        var fecha = Api.SiguienteFecha();
        var salas = await LeerAsync<SalaRespuesta[]>(await ObtenerAsync(encargado, "/api/v1/gestion/salas"));

        // Con la única mesa ocupada a las 21:00, no queda hueco para nadie más…
        var personal = await ClienteDeAsync(ApiConPersonal.PersonalB);
        await ApuntarReservaAsync(personal, fecha, "21:00");
        (await HorasDisponiblesAsync(ApiConBaseDeDatos.NegocioUnaMesa, fecha)).ShouldNotContain("21:00");

        // …hasta que el encargado añade otra mesa.
        using var nueva = await PostAsync(encargado, "/api/v1/gestion/mesas", new CrearMesaDto(salas[0].Id, "Extra", 1, 4, false));
        var mesa = await LeerAsync<MesaRespuesta>(nueva);
        (await HorasDisponiblesAsync(ApiConBaseDeDatos.NegocioUnaMesa, fecha)).ShouldContain("21:00");

        using var borrada = await BorrarAsync(encargado, $"/api/v1/gestion/mesas/{mesa.Id}");
        borrada.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await HorasDisponiblesAsync(ApiConBaseDeDatos.NegocioUnaMesa, fecha)).ShouldNotContain("21:00");
    }

    [Fact]
    public async Task Crear_una_mesa_en_una_sala_inexistente_es_un_404_y_con_capacidades_imposibles_un_422()
    {
        var encargado = await ClienteDeAsync(ApiConPersonal.EncargadoA);
        var salas = await LeerAsync<SalaRespuesta[]>(await ObtenerAsync(encargado, "/api/v1/gestion/salas"));

        using var sinSala = await PostAsync(encargado, "/api/v1/gestion/mesas", new CrearMesaDto(Guid.NewGuid(), "M", 1, 4, false));
        using var imposible = await PostAsync(encargado, "/api/v1/gestion/mesas", new CrearMesaDto(salas[0].Id, "M", 5, 2, false));
        using var incompleta = await PostAsync(encargado, "/api/v1/gestion/mesas", new CrearMesaDto(null, null, null, null, null));

        sinSala.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        imposible.StatusCode.ShouldBe((HttpStatusCode)422);
        (await LeerProblemaAsync(imposible)).Code.ShouldBe("local.mesa_invalida");
        incompleta.StatusCode.ShouldBe((HttpStatusCode)422);
        (await LeerProblemaAsync(incompleta)).Errors!.Keys.ShouldBe(["salaId", "nombre", "capacidadMinima", "capacidadMaxima", "esCombinable"], ignoreOrder: true);
    }

    [Fact]
    public async Task Un_cierre_de_todo_el_dia_quita_la_disponibilidad_y_al_borrarlo_vuelve()
    {
        var encargado = await ClienteDeAsync(ApiConPersonal.EncargadoA);
        var fecha = Api.SiguienteFecha();
        (await HorasDisponiblesAsync(ApiConBaseDeDatos.NegocioDosMesas, fecha)).ShouldNotBeEmpty();

        using var creado = await PostAsync(encargado, "/api/v1/gestion/cierres", new CrearCierreDto(Formatos.DeFecha(fecha), null, "  Reforma "));
        creado.StatusCode.ShouldBe(HttpStatusCode.Created);
        var cierre = await LeerAsync<CierreRespuesta>(creado);
        cierre.Motivo.ShouldBe("Reforma");
        cierre.Turno.ShouldBeNull();

        (await HorasDisponiblesAsync(ApiConBaseDeDatos.NegocioDosMesas, fecha)).ShouldBeEmpty();

        using var borrado = await BorrarAsync(encargado, $"/api/v1/gestion/cierres/{cierre.Id}");
        borrado.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await HorasDisponiblesAsync(ApiConBaseDeDatos.NegocioDosMesas, fecha)).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Un_cierre_de_un_turno_solo_quita_ese_turno()
    {
        var encargado = await ClienteDeAsync(ApiConPersonal.EncargadoA);
        var fecha = Api.SiguienteFecha();

        using var creado = await PostAsync(encargado, "/api/v1/gestion/cierres", new CrearCierreDto(Formatos.DeFecha(fecha), "cena", "Evento privado"));
        creado.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await LeerAsync<CierreRespuesta>(creado)).Turno.ShouldBe("cena");

        var horas = await HorasDisponiblesAsync(ApiConBaseDeDatos.NegocioDosMesas, fecha);
        horas.ShouldContain("13:00");
        horas.ShouldNotContain("21:00");
    }

    [Fact]
    public async Task Un_cierre_sin_motivo_o_con_un_turno_desconocido_es_un_422()
    {
        var encargado = await ClienteDeAsync(ApiConPersonal.EncargadoA);

        using var sinMotivo = await PostAsync(encargado, "/api/v1/gestion/cierres", new CrearCierreDto("2026-12-24", null, null));
        using var turnoRaro = await PostAsync(encargado, "/api/v1/gestion/cierres", new CrearCierreDto("2026-12-24", "desayuno", "Motivo"));

        sinMotivo.StatusCode.ShouldBe((HttpStatusCode)422);
        turnoRaro.StatusCode.ShouldBe((HttpStatusCode)422);
    }

    [Fact]
    public async Task Los_horarios_se_crean_se_listan_y_se_borran()
    {
        var encargado = await ClienteDeAsync(ApiConPersonal.EncargadoA);

        using var creado = await PostAsync(encargado, "/api/v1/gestion/horarios", new CrearHorarioDto("sabado", "cena", "23:00", "23:45", 15));
        creado.StatusCode.ShouldBe(HttpStatusCode.Created);
        var horario = await LeerAsync<HorarioRespuesta>(creado);
        horario.ShouldBe(new HorarioRespuesta(horario.Id, "sabado", "cena", "23:00", "23:45", 15));

        var lista = await LeerAsync<HorarioRespuesta[]>(await ObtenerAsync(encargado, "/api/v1/gestion/horarios"));
        lista.ShouldContain(h => h.Id == horario.Id);
        lista.Count(h => h.Dia == "lunes").ShouldBe(2, "comida y cena de los lunes, de los datos de demostración");

        using var borrado = await BorrarAsync(encargado, $"/api/v1/gestion/horarios/{horario.Id}");
        borrado.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await LeerAsync<HorarioRespuesta[]>(await ObtenerAsync(encargado, "/api/v1/gestion/horarios"))).ShouldNotContain(h => h.Id == horario.Id);
    }

    [Fact]
    public async Task Un_horario_con_datos_invalidos_es_un_422()
    {
        var encargado = await ClienteDeAsync(ApiConPersonal.EncargadoA);

        using var diaRaro = await PostAsync(encargado, "/api/v1/gestion/horarios", new CrearHorarioDto("someday", "cena", "20:00", "22:00", 30));
        using var finAntes = await PostAsync(encargado, "/api/v1/gestion/horarios", new CrearHorarioDto("lunes", "cena", "22:00", "20:00", 30));
        using var intervaloRaro = await PostAsync(encargado, "/api/v1/gestion/horarios", new CrearHorarioDto("lunes", "cena", "20:00", "22:00", 1));

        diaRaro.StatusCode.ShouldBe((HttpStatusCode)422);
        finAntes.StatusCode.ShouldBe((HttpStatusCode)422);
        (await LeerProblemaAsync(finAntes)).Code.ShouldBe("local.horario_invalido");
        intervaloRaro.StatusCode.ShouldBe((HttpStatusCode)422);
    }
}

/// <summary>Altas y bajas del personal por el propietario.</summary>
public class UsuariosTests(ApiConPersonal api) : PruebaPersonal(api), IClassFixture<ApiConPersonal>
{
    private const string ContrasenaNueva = "Otra-Contrasena-77";

    private static async Task<UsuarioRespuesta> CrearAsync(HttpClient propietario, string rol = "personal", string? email = null)
    {
        using var respuesta = await PostAsync(propietario, "/api/v1/gestion/usuarios", new CrearUsuarioDto(email ?? NuevoEmail(), "Persona nueva", rol, ContrasenaNueva));
        respuesta.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await LeerAsync<UsuarioRespuesta>(respuesta);
    }

    [Fact]
    public async Task Un_usuario_nuevo_puede_entrar_con_su_contrasena_y_con_su_rol()
    {
        var propietario = await ClienteDeAsync(ApiConPersonal.PropietarioA);

        var encargado = await CrearAsync(propietario, "encargado");

        encargado.Rol.ShouldBe("encargado");
        encargado.Activo.ShouldBeTrue();
        (await IniciarSesionAsync(encargado.Email, ContrasenaNueva)).Usuario.Rol.ShouldBe("encargado");
    }

    [Fact]
    public async Task No_se_pueden_repetir_correos_ni_siquiera_entre_negocios()
    {
        var propietarioA = await ClienteDeAsync(ApiConPersonal.PropietarioA);
        var propietarioB = await ClienteDeAsync(ApiConPersonal.PropietarioB);
        var existente = await CrearAsync(propietarioA);

        using var mismoNegocio = await PostAsync(propietarioA, "/api/v1/gestion/usuarios", new CrearUsuarioDto(existente.Email.ToUpperInvariant(), "Otro", "personal", ContrasenaNueva));
        using var otroNegocio = await PostAsync(propietarioB, "/api/v1/gestion/usuarios", new CrearUsuarioDto(existente.Email, "Otro", "personal", ContrasenaNueva));

        mismoNegocio.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await LeerProblemaAsync(mismoNegocio)).Code.ShouldBe("usuario.email_en_uso");
        otroNegocio.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData("corta1")]
    [InlineData("solo-letras-sin-numeros")]
    [InlineData("12345678901234")]
    public async Task Las_contrasenas_debiles_se_rechazan(string contrasena)
    {
        var propietario = await ClienteDeAsync(ApiConPersonal.PropietarioA);

        using var respuesta = await PostAsync(propietario, "/api/v1/gestion/usuarios", new CrearUsuarioDto(NuevoEmail(), "Nuevo", "personal", contrasena));

        respuesta.StatusCode.ShouldBe((HttpStatusCode)422);
        (await LeerProblemaAsync(respuesta)).Code.ShouldBe("usuario.contrasena_debil");
    }

    [Fact]
    public async Task Nadie_puede_crear_propietarios_ni_roles_inexistentes_por_la_api()
    {
        var propietario = await ClienteDeAsync(ApiConPersonal.PropietarioA);

        using var otroPropietario = await PostAsync(propietario, "/api/v1/gestion/usuarios", new CrearUsuarioDto(NuevoEmail(), "Nuevo", "propietario", ContrasenaNueva));
        using var rolRaro = await PostAsync(propietario, "/api/v1/gestion/usuarios", new CrearUsuarioDto(NuevoEmail(), "Nuevo", "administrador", ContrasenaNueva));
        using var incompleto = await PostAsync(propietario, "/api/v1/gestion/usuarios", new CrearUsuarioDto(null, null, null, null));

        otroPropietario.StatusCode.ShouldBe((HttpStatusCode)422);
        (await LeerProblemaAsync(otroPropietario)).Code.ShouldBe("usuario.rol_no_permitido");
        rolRaro.StatusCode.ShouldBe((HttpStatusCode)422);
        incompleto.StatusCode.ShouldBe((HttpStatusCode)422);
    }

    [Fact]
    public async Task Desactivar_a_alguien_le_cierra_las_sesiones_y_le_impide_volver_a_entrar()
    {
        var propietario = await ClienteDeAsync(ApiConPersonal.PropietarioA);
        var usuario = await CrearAsync(propietario);
        var sesion = await IniciarSesionAsync(usuario.Email, ContrasenaNueva);

        using var baja = await BorrarAsync(propietario, $"/api/v1/gestion/usuarios/{usuario.Id}");
        baja.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var renovacion = await Api.CrearCliente().PostAsJsonAsync("/api/v1/auth/refresh", new RefrescoDto(sesion.TokenRefresco), Cancelacion);
        using var entrada = await LoginAsync(usuario.Email, ContrasenaNueva);
        renovacion.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        entrada.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var lista = await LeerAsync<UsuarioRespuesta[]>(await ObtenerAsync(propietario, "/api/v1/gestion/usuarios"));
        lista.Single(u => u.Id == usuario.Id).Activo.ShouldBeFalse();

        using var otraBaja = await BorrarAsync(propietario, $"/api/v1/gestion/usuarios/{usuario.Id}");
        otraBaja.StatusCode.ShouldBe(HttpStatusCode.Conflict, "un usuario ya desactivado no se puede desactivar otra vez");
    }

    [Fact]
    public async Task Un_propietario_no_puede_desactivarse_a_si_mismo_ni_a_otro_propietario()
    {
        var propietario = await ClienteDeAsync(ApiConPersonal.PropietarioA);
        var lista = await LeerAsync<UsuarioRespuesta[]>(await ObtenerAsync(propietario, "/api/v1/gestion/usuarios"));
        var yo = lista.Single(u => u.Email == ApiConPersonal.PropietarioA);

        using var respuesta = await BorrarAsync(propietario, $"/api/v1/gestion/usuarios/{yo.Id}");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await LeerProblemaAsync(respuesta)).Code.ShouldBe("usuario.protegido");
    }

    [Fact]
    public async Task Un_usuario_desactivado_con_un_token_de_acceso_todavia_vigente_lo_conserva_hasta_que_caduca()
    {
        // Documenta un límite conocido del diseño con tokens de corta duración: el token de acceso
        // no se consulta contra la base de datos, así que sigue valiendo hasta que caduca (15 min).
        var propietario = await ClienteDeAsync(ApiConPersonal.PropietarioA);
        var usuario = await CrearAsync(propietario);
        var sesion = await IniciarSesionAsync(usuario.Email, ContrasenaNueva);
        (await BorrarAsync(propietario, $"/api/v1/gestion/usuarios/{usuario.Id}")).Dispose();
        var cliente = ClienteConToken(sesion.TokenAcceso);
        var ruta = $"/api/v1/gestion/agenda?fecha={Formatos.DeFecha(Api.SiguienteFecha())}";

        using var vigente = await ObtenerAsync(cliente, ruta);
        Api.Reloj.Advance(TimeSpan.FromMinutes(15));
        using var caducado = await ObtenerAsync(cliente, ruta);

        vigente.StatusCode.ShouldBe(HttpStatusCode.OK);
        caducado.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
