using Reservas.Dominio.Negocios;

using Shouldly;

namespace Reservas.Dominio.Tests.Negocios;

public class NegocioTests
{
    [Fact]
    public void Un_negocio_valido_se_crea_con_sus_datos()
    {
        var resultado = Negocio.Crear("bar-la-plaza", "  Bar La Plaza (demo)  ", "Europe/Madrid", PoliticasReserva.PorDefecto);

        resultado.EsExito.ShouldBeTrue();
        resultado.Valor.Slug.ShouldBe("bar-la-plaza");
        resultado.Valor.Nombre.ShouldBe("Bar La Plaza (demo)");
        resultado.Valor.Zona.Id.ShouldBe("Europe/Madrid");
        resultado.Valor.Id.ShouldNotBe(Guid.Empty);
    }

    [Theory]
    [InlineData("ab")]                                          // demasiado corto
    [InlineData("Bar-La-Plaza")]                                // mayúsculas
    [InlineData("bar la plaza")]                                // espacios
    [InlineData("-bar")]                                        // empieza por guion
    [InlineData("bar-")]                                        // acaba en guion
    [InlineData("bar--plaza")]                                  // guiones seguidos
    [InlineData("bar_plaza")]                                   // guion bajo
    [InlineData("bar-de-la-plaza-mayor-de-la-ciudad-vieja-1")]  // más de 40 caracteres
    public void Un_slug_invalido_se_rechaza(string slug)
    {
        var resultado = Negocio.Crear(slug, "Bar", "Europe/Madrid", PoliticasReserva.PorDefecto);

        resultado.Error.Codigo.ShouldBe("negocio.slug_invalido");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void El_nombre_es_obligatorio(string nombre)
    {
        var resultado = Negocio.Crear("bar-la-plaza", nombre, "Europe/Madrid", PoliticasReserva.PorDefecto);

        resultado.Error.Codigo.ShouldBe("negocio.nombre_invalido");
    }

    [Fact]
    public void El_nombre_no_puede_superar_los_cien_caracteres()
    {
        var resultado = Negocio.Crear("bar-la-plaza", new string('a', 101), "Europe/Madrid", PoliticasReserva.PorDefecto);

        resultado.Error.Codigo.ShouldBe("negocio.nombre_invalido");
    }

    [Fact]
    public void Una_zona_horaria_inexistente_se_rechaza()
    {
        var resultado = Negocio.Crear("bar-la-plaza", "Bar", "Marte/Olimpo", PoliticasReserva.PorDefecto);

        resultado.Error.Codigo.ShouldBe("negocio.zona_horaria_invalida");
    }
}
