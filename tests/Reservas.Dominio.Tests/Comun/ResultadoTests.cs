using Reservas.Dominio.Comun;

using Shouldly;

namespace Reservas.Dominio.Tests.Comun;

public class ResultadoTests
{
    private static readonly ErrorDominio MesaOcupada = new("reserva.mesa_ocupada", "La mesa ya está reservada a esa hora.");

    [Fact]
    public void Un_exito_no_lleva_error()
    {
        var resultado = Resultado.Exito();

        resultado.EsExito.ShouldBeTrue();
        resultado.Error.ShouldBe(ErrorDominio.Ninguno);
    }

    [Fact]
    public void Un_fallo_lleva_su_error()
    {
        var resultado = Resultado.Fallo(MesaOcupada);

        resultado.EsFallo.ShouldBeTrue();
        resultado.Error.Codigo.ShouldBe("reserva.mesa_ocupada");
    }

    [Fact]
    public void Un_exito_con_valor_lo_devuelve()
    {
        Resultado.Exito(42).Valor.ShouldBe(42);
    }

    [Fact]
    public void Pedir_el_valor_de_un_fallo_es_un_error_de_programacion()
    {
        var resultado = Resultado.Fallo<int>(MesaOcupada);

        Should.Throw<InvalidOperationException>(() => resultado.Valor)
            .Message.ShouldContain("reserva.mesa_ocupada");
    }

    [Fact]
    public void Un_fallo_sin_error_no_se_puede_crear()
    {
        Should.Throw<ArgumentException>(() => Resultado.Fallo(ErrorDominio.Ninguno));
    }
}
