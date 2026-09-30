using Reservas.Dominio.Gestion;

using Shouldly;

namespace Reservas.Dominio.Tests.Gestion;

public class DatosClienteTests
{
    [Fact]
    public void Un_cliente_valido_se_crea_sin_espacios_sobrantes()
    {
        var resultado = DatosCliente.Crear("  Ana Pérez ", " ana@example.com ", " 600 000 000 ");

        resultado.EsExito.ShouldBeTrue();
        resultado.Valor.Nombre.ShouldBe("Ana Pérez");
        resultado.Valor.Email.ShouldBe("ana@example.com");
        resultado.Valor.Telefono.ShouldBe("600 000 000");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void El_telefono_es_opcional(string? telefono)
    {
        var resultado = DatosCliente.Crear("Ana", "ana@example.com", telefono);

        resultado.EsExito.ShouldBeTrue();
        resultado.Valor.Telefono.ShouldBeNull();
    }

    [Theory]
    [InlineData("", "ana@example.com")]
    [InlineData("   ", "ana@example.com")]
    [InlineData("Ana", "")]
    [InlineData("Ana", "no-es-un-correo")]
    [InlineData("Ana", "ana@")]
    [InlineData("Ana", "ana perez@example.com")]
    [InlineData("Ana", "Ana Pérez <ana@example.com>")] // con nombre delante: no es solo la dirección
    public void Un_cliente_invalido_se_rechaza(string nombre, string email)
    {
        var resultado = DatosCliente.Crear(nombre, email);

        resultado.Error.Codigo.ShouldBe("reserva.cliente_invalido");
    }

    [Fact]
    public void El_nombre_no_puede_superar_los_cien_caracteres()
    {
        DatosCliente.Crear(new string('a', 101), "ana@example.com").EsFallo.ShouldBeTrue();
    }

    [Fact]
    public void El_telefono_no_puede_superar_los_treinta_caracteres()
    {
        DatosCliente.Crear("Ana", "ana@example.com", new string('6', 31)).EsFallo.ShouldBeTrue();
    }
}
