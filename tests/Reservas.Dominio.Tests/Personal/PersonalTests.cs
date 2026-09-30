using Reservas.Dominio.Personal;

using Shouldly;

namespace Reservas.Dominio.Tests.Personal;

public class UsuarioTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 9, 30, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Negocio = Guid.NewGuid();

    private static Usuario Nuevo(Rol rol = Rol.Personal) =>
        Usuario.Crear(Negocio, "Ana@Demo.Example ", " Ana ", rol, "hash", Ahora).Valor;

    [Fact]
    public void Un_usuario_se_crea_con_el_correo_normalizado_y_activo()
    {
        var usuario = Nuevo();

        usuario.Email.ShouldBe("ana@demo.example");
        usuario.Nombre.ShouldBe("Ana");
        usuario.Activo.ShouldBeTrue();
        usuario.NegocioId.ShouldBe(Negocio);
        usuario.PuedeIniciarSesion(Ahora).ShouldBeTrue();
    }

    [Theory]
    [InlineData("", "Ana", "hash")]
    [InlineData("no-es-un-correo", "Ana", "hash")]
    [InlineData("ana@demo.example", "", "hash")]
    [InlineData("ana@demo.example", "Ana", "")]
    public void Un_usuario_con_datos_invalidos_no_se_crea(string email, string nombre, string hash)
    {
        Usuario.Crear(Negocio, email, nombre, Rol.Personal, hash, Ahora).Error.Codigo.ShouldBe("usuario.invalido");
    }

    [Fact]
    public void Un_usuario_sin_negocio_o_con_rol_desconocido_no_se_crea()
    {
        Usuario.Crear(Guid.Empty, "ana@demo.example", "Ana", Rol.Personal, "hash", Ahora).EsFallo.ShouldBeTrue();
        Usuario.Crear(Negocio, "ana@demo.example", "Ana", (Rol)99, "hash", Ahora).EsFallo.ShouldBeTrue();
    }

    [Fact]
    public void Cinco_fallos_seguidos_bloquean_la_cuenta_un_rato()
    {
        var usuario = Nuevo();

        for (var i = 0; i < Usuario.MaximoIntentosFallidos - 1; i++)
        {
            usuario.RegistrarFallo(Ahora);
            usuario.EstaBloqueado(Ahora).ShouldBeFalse();
        }

        usuario.RegistrarFallo(Ahora);

        usuario.EstaBloqueado(Ahora).ShouldBeTrue();
        usuario.PuedeIniciarSesion(Ahora).ShouldBeFalse();
        usuario.EstaBloqueado(Ahora + Usuario.DuracionBloqueo - TimeSpan.FromSeconds(1)).ShouldBeTrue();
        usuario.EstaBloqueado(Ahora + Usuario.DuracionBloqueo).ShouldBeFalse();
    }

    [Fact]
    public void Tras_el_bloqueo_el_contador_de_fallos_empieza_de_cero()
    {
        var usuario = Nuevo();
        for (var i = 0; i < Usuario.MaximoIntentosFallidos; i++)
        {
            usuario.RegistrarFallo(Ahora);
        }

        usuario.IntentosFallidos.ShouldBe(0);
    }

    [Fact]
    public void Una_entrada_correcta_borra_los_fallos_anteriores()
    {
        var usuario = Nuevo();
        usuario.RegistrarFallo(Ahora);
        usuario.RegistrarFallo(Ahora);

        usuario.RegistrarAcceso();

        usuario.IntentosFallidos.ShouldBe(0);
        usuario.BloqueadoHasta.ShouldBeNull();
    }

    [Fact]
    public void Un_usuario_desactivado_no_puede_entrar_y_no_se_desactiva_dos_veces()
    {
        var usuario = Nuevo();

        usuario.Desactivar().EsExito.ShouldBeTrue();

        usuario.Activo.ShouldBeFalse();
        usuario.PuedeIniciarSesion(Ahora).ShouldBeFalse();
        usuario.Desactivar().Error.Codigo.ShouldBe("usuario.ya_desactivado");
    }
}

public class RolTests
{
    [Theory]
    [InlineData(Rol.Personal, Rol.Personal, true)]
    [InlineData(Rol.Personal, Rol.Encargado, false)]
    [InlineData(Rol.Personal, Rol.Propietario, false)]
    [InlineData(Rol.Encargado, Rol.Personal, true)]
    [InlineData(Rol.Encargado, Rol.Encargado, true)]
    [InlineData(Rol.Encargado, Rol.Propietario, false)]
    [InlineData(Rol.Propietario, Rol.Personal, true)]
    [InlineData(Rol.Propietario, Rol.Encargado, true)]
    [InlineData(Rol.Propietario, Rol.Propietario, true)]
    public void Cada_rol_incluye_lo_de_los_inferiores(Rol rol, Rol minimo, bool esperado)
    {
        rol.Cubre(minimo).ShouldBe(esperado);
    }

    [Fact]
    public void Un_rol_desconocido_no_cubre_nada()
    {
        ((Rol)0).Cubre(Rol.Personal).ShouldBeFalse();
        ((Rol)99).Cubre(Rol.Personal).ShouldBeFalse();
    }
}

public class TokenRefrescoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 9, 30, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Un_token_esta_activo_hasta_que_caduca()
    {
        var token = TokenRefresco.Crear(Guid.NewGuid(), "hash", Ahora, TimeSpan.FromDays(14));

        token.EstaActivo(Ahora).ShouldBeTrue();
        token.EstaActivo(Ahora.AddDays(14).AddSeconds(-1)).ShouldBeTrue();
        token.EstaActivo(Ahora.AddDays(14)).ShouldBeFalse();
    }

    [Fact]
    public void Un_token_revocado_ya_no_esta_activo_y_conserva_el_primer_momento_de_revocacion()
    {
        var token = TokenRefresco.Crear(Guid.NewGuid(), "hash", Ahora, TimeSpan.FromDays(14));

        token.Revocar(Ahora.AddHours(1));
        token.Revocar(Ahora.AddHours(5));

        token.EstaRevocado.ShouldBeTrue();
        token.EstaActivo(Ahora.AddHours(2)).ShouldBeFalse();
        token.RevocadoEn.ShouldBe(Ahora.AddHours(1));
    }
}
