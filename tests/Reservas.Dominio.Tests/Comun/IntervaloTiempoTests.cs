using Reservas.Dominio.Comun;

using Shouldly;

namespace Reservas.Dominio.Tests.Comun;

public class IntervaloTiempoTests
{
    private static readonly DateTimeOffset Base = Escenario.Utc(2026, 10, 3, 19);

    private static IntervaloTiempo Tramo(int desdeMinutos, int hastaMinutos) =>
        new(Base.AddMinutes(desdeMinutos), Base.AddMinutes(hastaMinutos));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(30, 10)]
    public void El_fin_debe_ser_posterior_al_inicio(int desdeMinutos, int hastaMinutos)
    {
        Should.Throw<ArgumentException>(() => Tramo(desdeMinutos, hastaMinutos));
    }

    [Theory]
    [InlineData(-30, 60, true)]    // empieza antes y acaba dentro
    [InlineData(10, 20, true)]     // contenido
    [InlineData(-10, 100, true)]   // lo contiene
    [InlineData(89, 120, true)]    // se pisan un minuto
    [InlineData(90, 180, false)]   // empieza justo cuando acaba: no chocan
    [InlineData(-90, 0, false)]    // acaba justo cuando empieza: no chocan
    [InlineData(200, 260, false)]  // separados
    public void Dos_tramos_solo_se_solapan_si_comparten_tiempo(int desdeMinutos, int hastaMinutos, bool esperado)
    {
        var reserva = Tramo(0, 90);

        reserva.Solapa(Tramo(desdeMinutos, hastaMinutos)).ShouldBe(esperado);
        Tramo(desdeMinutos, hastaMinutos).Solapa(reserva).ShouldBe(esperado);
    }

    [Fact]
    public void Los_extremos_se_normalizan_a_utc()
    {
        var conOffset = new DateTimeOffset(2026, 10, 3, 21, 0, 0, TimeSpan.FromHours(2));

        var tramo = new IntervaloTiempo(conOffset, conOffset.AddMinutes(90));

        tramo.Inicio.Offset.ShouldBe(TimeSpan.Zero);
        tramo.Inicio.ShouldBe(Escenario.Utc(2026, 10, 3, 19));
    }

    [Fact]
    public void La_duracion_es_la_diferencia_entre_fin_e_inicio()
    {
        Tramo(0, 90).Duracion.ShouldBe(TimeSpan.FromMinutes(90));
    }
}
