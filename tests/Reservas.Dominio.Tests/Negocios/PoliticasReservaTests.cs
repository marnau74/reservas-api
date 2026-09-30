using Reservas.Dominio.Negocios;

using Shouldly;

namespace Reservas.Dominio.Tests.Negocios;

public class PoliticasReservaTests
{
    private static readonly TimeSpan Hora = TimeSpan.FromHours(1);
    private static readonly TimeSpan NoventaMinutos = TimeSpan.FromMinutes(90);

    [Fact]
    public void Las_politicas_por_defecto_son_razonables()
    {
        var politicas = PoliticasReserva.PorDefecto;

        politicas.AntelacionMinima.ShouldBe(Hora);
        politicas.DiasMaximosAntelacion.ShouldBe(60);
        politicas.MaxComensalesOnline.ShouldBe(10);
        politicas.DuracionEstandar.ShouldBe(NoventaMinutos);
    }

    [Fact]
    public void Unas_politicas_dentro_de_rango_se_aceptan()
    {
        var resultado = PoliticasReserva.Crear(TimeSpan.Zero, 30, 8, TimeSpan.FromHours(2));

        resultado.EsExito.ShouldBeTrue();
        resultado.Valor.MaxComensalesOnline.ShouldBe(8);
    }

    [Fact]
    public void La_antelacion_no_puede_ser_negativa()
    {
        PoliticasReserva.Crear(TimeSpan.FromMinutes(-1), 30, 8, NoventaMinutos).EsFallo.ShouldBeTrue();
    }

    [Fact]
    public void La_antelacion_no_puede_superar_una_semana()
    {
        PoliticasReserva.Crear(TimeSpan.FromDays(8), 30, 8, NoventaMinutos).EsFallo.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(366)]
    public void Los_dias_de_antelacion_tienen_limites(int dias)
    {
        PoliticasReserva.Crear(Hora, dias, 8, NoventaMinutos).EsFallo.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public void Los_comensales_online_tienen_limites(int comensales)
    {
        PoliticasReserva.Crear(Hora, 30, comensales, NoventaMinutos).EsFallo.ShouldBeTrue();
    }

    [Theory]
    [InlineData(10)]
    [InlineData(361)]
    public void La_duracion_de_una_reserva_tiene_limites(int minutos)
    {
        PoliticasReserva.Crear(Hora, 30, 8, TimeSpan.FromMinutes(minutos)).EsFallo.ShouldBeTrue();
    }
}
