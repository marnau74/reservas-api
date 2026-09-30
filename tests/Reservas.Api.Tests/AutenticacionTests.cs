using System.Net;
using System.Net.Http.Json;

using Microsoft.EntityFrameworkCore;

using Reservas.Api.Contratos;
using Reservas.Dominio.Personal;
using Reservas.Infraestructura.Persistencia;

using Shouldly;

namespace Reservas.Api.Tests;

public class AutenticacionTests(ApiConPersonal api) : PruebaPersonal(api), IClassFixture<ApiConPersonal>
{
    private Task<Usuario> NuevoUsuarioAsync(Rol rol = Rol.Personal, string? contrasena = null)
    {
        return CrearAsync();

        async Task<Usuario> CrearAsync()
        {
            await using var db = Api.NuevoContexto();
            var negocio = await db.Negocios.SingleAsync(n => n.Slug == ApiConBaseDeDatos.NegocioDosMesas, Cancelacion);

            return await SembradorDemo.CrearUsuarioAsync(
                db, negocio.Id, NuevoEmail(), "Usuario de prueba", rol, contrasena ?? ApiConPersonal.Contrasena, Api.Reloj.GetUtcNow());
        }
    }

    [Fact]
    public async Task Con_las_credenciales_correctas_se_recibe_una_sesion()
    {
        using var respuesta = await LoginAsync(ApiConPersonal.EncargadoA);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
        var sesion = await LeerAsync<SesionRespuesta>(respuesta);
        sesion.TokenAcceso.ShouldNotBeNullOrWhiteSpace();
        sesion.TokenRefresco.ShouldNotBeNullOrWhiteSpace();
        sesion.AccesoExpiraEn.ShouldBe(Api.Reloj.GetUtcNow().AddMinutes(15));
        sesion.RefrescoExpiraEn.ShouldBe(Api.Reloj.GetUtcNow().AddDays(14));
        sesion.Usuario.Email.ShouldBe(ApiConPersonal.EncargadoA);
        sesion.Usuario.Rol.ShouldBe("encargado");
    }

    [Fact]
    public async Task El_correo_no_distingue_mayusculas_ni_espacios()
    {
        using var respuesta = await LoginAsync("  Encargado.A@Example.COM ");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ni_la_respuesta_ni_la_base_de_datos_contienen_la_contrasena_ni_el_token_en_claro()
    {
        var sesion = await IniciarSesionAsync(ApiConPersonal.PersonalA);

        await using var db = Api.NuevoContexto();
        var usuario = await db.Usuarios.IgnoreQueryFilters().SingleAsync(u => u.Email == ApiConPersonal.PersonalA, Cancelacion);
        usuario.HashContrasena.ShouldNotContain(ApiConPersonal.Contrasena);

        var tokens = await db.TokensRefresco.Select(t => t.HashToken).ToListAsync(Cancelacion);
        tokens.ShouldNotContain(sesion.TokenRefresco);
        tokens.ShouldAllBe(t => t.Length == 64);
    }

    [Fact]
    public async Task Un_correo_desconocido_y_una_contrasena_incorrecta_reciben_exactamente_el_mismo_error()
    {
        using var desconocido = await LoginAsync("nadie@example.com");
        using var incorrecta = await LoginAsync(ApiConPersonal.PersonalA, "Otra-contrasena-9");

        desconocido.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        incorrecta.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var a = await LeerProblemaAsync(desconocido);
        var b = await LeerProblemaAsync(incorrecta);
        a.Code.ShouldBe("auth.credenciales_invalidas");
        b.ShouldBe(a);
    }

    [Fact]
    public async Task Una_peticion_sin_correo_o_sin_contrasena_es_un_error_de_validacion()
    {
        using var respuesta = await Api.CrearCliente().PostAsJsonAsync("/api/v1/auth/login", new LoginDto(null, null), Cancelacion);

        respuesta.StatusCode.ShouldBe((HttpStatusCode)422);
        var problema = await LeerProblemaAsync(respuesta);
        problema.Errors!.Keys.ShouldBe(["email", "contrasena"], ignoreOrder: true);
    }

    [Fact]
    public async Task Cinco_contrasenas_incorrectas_bloquean_la_cuenta_incluso_para_la_contrasena_correcta_hasta_que_pasa_el_tiempo()
    {
        var usuario = await NuevoUsuarioAsync();

        for (var i = 0; i < Usuario.MaximoIntentosFallidos; i++)
        {
            using var fallo = await LoginAsync(usuario.Email, "Incorrecta-123456");
            fallo.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        // Bloqueada: la contraseña buena tampoco entra, y el error no dice que esté bloqueada.
        using var bloqueada = await LoginAsync(usuario.Email);
        bloqueada.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await LeerProblemaAsync(bloqueada)).Code.ShouldBe("auth.credenciales_invalidas");

        Api.Reloj.Advance(Usuario.DuracionBloqueo);

        using var desbloqueada = await LoginAsync(usuario.Email);
        desbloqueada.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Una_entrada_correcta_borra_los_fallos_y_no_se_acumulan_entre_sesiones()
    {
        var usuario = await NuevoUsuarioAsync();

        for (var ronda = 0; ronda < 3; ronda++)
        {
            for (var i = 0; i < Usuario.MaximoIntentosFallidos - 1; i++)
            {
                using var fallo = await LoginAsync(usuario.Email, "Incorrecta-123456");
                fallo.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            }

            using var acierto = await LoginAsync(usuario.Email);
            acierto.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Renovar_la_sesion_entrega_tokens_nuevos_y_el_anterior_deja_de_servir()
    {
        var primera = await IniciarSesionAsync(ApiConPersonal.PersonalA);

        using var renovada = await Api.CrearCliente().PostAsJsonAsync("/api/v1/auth/refresh", new RefrescoDto(primera.TokenRefresco), Cancelacion);

        renovada.StatusCode.ShouldBe(HttpStatusCode.OK);
        var segunda = await LeerAsync<SesionRespuesta>(renovada);
        segunda.TokenRefresco.ShouldNotBe(primera.TokenRefresco);

        using var conElNuevo = await ObtenerAsync(ClienteConToken(segunda.TokenAcceso), $"/api/v1/gestion/agenda?fecha={Formatos.DeFecha(Api.SiguienteFecha())}");
        conElNuevo.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var reutilizado = await Api.CrearCliente().PostAsJsonAsync("/api/v1/auth/refresh", new RefrescoDto(primera.TokenRefresco), Cancelacion);
        reutilizado.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await LeerProblemaAsync(reutilizado)).Code.ShouldBe("auth.token_invalido");
    }

    [Fact]
    public async Task Reutilizar_un_token_ya_gastado_cierra_tambien_las_sesiones_que_salieron_de_el()
    {
        var primera = await IniciarSesionAsync(ApiConPersonal.PersonalA);
        using var renovada = await Api.CrearCliente().PostAsJsonAsync("/api/v1/auth/refresh", new RefrescoDto(primera.TokenRefresco), Cancelacion);
        var segunda = await LeerAsync<SesionRespuesta>(renovada);

        // Alguien presenta el token viejo (robado, o un cliente que se equivoca)…
        using var intruso = await Api.CrearCliente().PostAsJsonAsync("/api/v1/auth/refresh", new RefrescoDto(primera.TokenRefresco), Cancelacion);
        intruso.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // …y la sesión legítima también se cierra: hay que volver a entrar.
        using var legitimo = await Api.CrearCliente().PostAsJsonAsync("/api/v1/auth/refresh", new RefrescoDto(segunda.TokenRefresco), Cancelacion);
        legitimo.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Diez_renovaciones_simultaneas_con_el_mismo_token_solo_dan_una_sesion()
    {
        var sesion = await IniciarSesionAsync(ApiConPersonal.PersonalA);

        var respuestas = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            Api.CrearCliente().PostAsJsonAsync("/api/v1/auth/refresh", new RefrescoDto(sesion.TokenRefresco), Cancelacion)));

        respuestas.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        respuestas.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK || r.StatusCode == HttpStatusCode.Unauthorized);

        foreach (var respuesta in respuestas)
        {
            respuesta.Dispose();
        }
    }

    [Fact]
    public async Task Un_token_de_renovacion_caduca()
    {
        var sesion = await IniciarSesionAsync(ApiConPersonal.PersonalA);

        Api.Reloj.Advance(TimeSpan.FromDays(14));

        using var respuesta = await Api.CrearCliente().PostAsJsonAsync("/api/v1/auth/refresh", new RefrescoDto(sesion.TokenRefresco), Cancelacion);
        respuesta.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Un_token_de_renovacion_inventado_no_sirve()
    {
        using var respuesta = await Api.CrearCliente().PostAsJsonAsync("/api/v1/auth/refresh", new RefrescoDto("token-inventado"), Cancelacion);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Cerrar_sesion_revoca_el_token_de_renovacion_y_se_puede_repetir()
    {
        var sesion = await IniciarSesionAsync(ApiConPersonal.PersonalA);
        var cliente = Api.CrearCliente();

        using var cierre = await cliente.PostAsJsonAsync("/api/v1/auth/logout", new RefrescoDto(sesion.TokenRefresco), Cancelacion);
        using var otroCierre = await cliente.PostAsJsonAsync("/api/v1/auth/logout", new RefrescoDto(sesion.TokenRefresco), Cancelacion);
        using var renovacion = await cliente.PostAsJsonAsync("/api/v1/auth/refresh", new RefrescoDto(sesion.TokenRefresco), Cancelacion);

        cierre.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        otroCierre.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        renovacion.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task El_token_de_acceso_caduca_a_los_quince_minutos()
    {
        var cliente = await ClienteDeAsync(ApiConPersonal.PersonalA);
        var ruta = $"/api/v1/gestion/agenda?fecha={Formatos.DeFecha(Api.SiguienteFecha())}";

        Api.Reloj.Advance(TimeSpan.FromMinutes(15) - TimeSpan.FromSeconds(1));
        using var aTiempo = await ObtenerAsync(cliente, ruta);

        Api.Reloj.Advance(TimeSpan.FromSeconds(1));
        using var caducado = await ObtenerAsync(cliente, ruta);

        aTiempo.StatusCode.ShouldBe(HttpStatusCode.OK);
        caducado.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Un_token_manipulado_o_de_otra_clave_no_se_acepta()
    {
        var sesion = await IniciarSesionAsync(ApiConPersonal.PersonalA);
        var ruta = $"/api/v1/gestion/agenda?fecha={Formatos.DeFecha(Api.SiguienteFecha())}";

        // Cambiar un carácter de la firma la invalida.
        var ultimo = sesion.TokenAcceso[^1];
        var manipulado = sesion.TokenAcceso[..^1] + (ultimo == 'A' ? 'B' : 'A');

        using var conFirmaRota = await ObtenerAsync(ClienteConToken(manipulado), ruta);
        using var sinFirma = await ObtenerAsync(ClienteConToken(sesion.TokenAcceso.Split('.')[0] + "." + sesion.TokenAcceso.Split('.')[1] + "."), ruta);
        using var basura = await ObtenerAsync(ClienteConToken("no.es.un-token"), ruta);

        conFirmaRota.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        sinFirma.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        basura.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Un_usuario_desactivado_no_puede_renovar_su_sesion()
    {
        var usuario = await NuevoUsuarioAsync();
        var sesion = await IniciarSesionAsync(usuario.Email);

        await using (var db = Api.NuevoContexto())
        {
            var guardado = await db.Usuarios.IgnoreQueryFilters().SingleAsync(u => u.Id == usuario.Id, Cancelacion);
            guardado.Desactivar();
            await db.SaveChangesAsync(Cancelacion);
        }

        using var renovacion = await Api.CrearCliente().PostAsJsonAsync("/api/v1/auth/refresh", new RefrescoDto(sesion.TokenRefresco), Cancelacion);
        using var entrada = await LoginAsync(usuario.Email);

        renovacion.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        entrada.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
