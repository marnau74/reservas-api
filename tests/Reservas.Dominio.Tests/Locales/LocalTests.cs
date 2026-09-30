using Reservas.Dominio.Locales;

using Shouldly;

namespace Reservas.Dominio.Tests.Locales;

public class MesaTests
{
    [Fact]
    public void Una_mesa_valida_se_crea()
    {
        var resultado = Mesa.Crear(Escenario.SalaPrincipal, "  Mesa 4  ", 2, 4, esCombinable: true);

        resultado.EsExito.ShouldBeTrue();
        resultado.Valor.Nombre.ShouldBe("Mesa 4");
        resultado.Valor.EsCombinable.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Mesa", 0, 4)]   // mínima cero
    [InlineData("Mesa", 5, 4)]   // mínima mayor que la máxima
    [InlineData("Mesa", 1, 31)]  // máxima excesiva
    [InlineData("", 1, 4)]       // sin nombre
    [InlineData("  ", 1, 4)]
    public void Una_mesa_invalida_se_rechaza(string nombre, int minima, int maxima)
    {
        var resultado = Mesa.Crear(Escenario.SalaPrincipal, nombre, minima, maxima, esCombinable: false);

        resultado.Error.Codigo.ShouldBe("local.mesa_invalida");
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(4, true)]
    [InlineData(5, false)]
    public void Una_mesa_admite_solo_grupos_dentro_de_su_rango(int comensales, bool esperado)
    {
        Escenario.CrearMesa(2, 4).Admite(comensales).ShouldBe(esperado);
    }
}

public class HorarioTests
{
    [Fact]
    public void Las_horas_ofrecidas_van_de_la_primera_a_la_ultima_admision()
    {
        var horario = Escenario.CrearHorario(DayOfWeek.Saturday, Turno.Comida, "13:00", "15:30", 30);

        horario.HorasOfrecidas().ShouldBe(
        [
            new TimeOnly(13, 0), new TimeOnly(13, 30), new TimeOnly(14, 0),
            new TimeOnly(14, 30), new TimeOnly(15, 0), new TimeOnly(15, 30),
        ]);
    }

    [Fact]
    public void Si_el_intervalo_no_llega_a_la_ultima_hora_no_se_pasa_de_ella()
    {
        var horario = Escenario.CrearHorario(DayOfWeek.Saturday, Turno.Cena, "20:00", "21:50", 45);

        horario.HorasOfrecidas().ShouldBe([new TimeOnly(20, 0), new TimeOnly(20, 45), new TimeOnly(21, 30)]);
    }

    [Fact]
    public void Si_abre_y_cierra_a_la_misma_hora_solo_hay_una_franja()
    {
        var horario = Escenario.CrearHorario(DayOfWeek.Saturday, Turno.Cena, "21:00", "21:00", 15);

        horario.HorasOfrecidas().ShouldBe([new TimeOnly(21, 0)]);
    }

    [Theory]
    [InlineData("15:00", "13:00", 30)]  // cierra antes de abrir
    [InlineData("13:00", "15:00", 4)]   // intervalo demasiado corto
    [InlineData("13:00", "15:00", 121)] // intervalo demasiado largo
    public void Un_horario_invalido_se_rechaza(string desde, string hasta, int intervalo)
    {
        var resultado = Horario.Crear(DayOfWeek.Friday, Turno.Comida, Escenario.Hora(desde), Escenario.Hora(hasta), intervalo);

        resultado.Error.Codigo.ShouldBe("local.horario_invalido");
    }
}

public class CierreTests
{
    private static readonly DateOnly Nochebuena = new(2026, 12, 24);

    [Fact]
    public void Un_cierre_sin_turno_cubre_todo_el_dia()
    {
        var cierre = new Cierre(Nochebuena, null, "Festivo");

        cierre.Cubre(Nochebuena, Turno.Comida).ShouldBeTrue();
        cierre.Cubre(Nochebuena, Turno.Cena).ShouldBeTrue();
    }

    [Fact]
    public void Un_cierre_de_un_turno_no_afecta_al_otro()
    {
        var cierre = new Cierre(Nochebuena, Turno.Cena, "Cena familiar");

        cierre.Cubre(Nochebuena, Turno.Cena).ShouldBeTrue();
        cierre.Cubre(Nochebuena, Turno.Comida).ShouldBeFalse();
    }

    [Fact]
    public void Un_cierre_solo_afecta_a_su_fecha()
    {
        new Cierre(Nochebuena, null, "Festivo").Cubre(Nochebuena.AddDays(1), Turno.Comida).ShouldBeFalse();
    }
}
