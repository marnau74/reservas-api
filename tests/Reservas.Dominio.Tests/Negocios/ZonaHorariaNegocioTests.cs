using Reservas.Dominio.Negocios;

using Shouldly;

namespace Reservas.Dominio.Tests.Negocios;

/// <summary>
/// Los cambios de hora de España en 2026: el 29 de marzo a las 02:00 los relojes pasan a las
/// 03:00 (esa hora se salta) y el 25 de octubre a las 03:00 vuelven a las 02:00 (esa hora se
/// repite). Los dos son domingo.
/// </summary>
public class ZonaHorariaNegocioTests
{
    private static readonly ZonaHorariaNegocio Madrid = ZonaHorariaNegocio.Crear("Europe/Madrid").Valor;

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Marte/Olimpo")]
    public void Una_zona_que_no_existe_es_un_error_de_negocio(string id)
    {
        var resultado = ZonaHorariaNegocio.Crear(id);

        resultado.EsFallo.ShouldBeTrue();
        resultado.Error.Codigo.ShouldBe("negocio.zona_horaria_invalida");
    }

    [Fact]
    public void En_invierno_madrid_va_una_hora_por_delante_de_utc()
    {
        var instante = Madrid.AUtc(new DateOnly(2026, 1, 15), new TimeOnly(21, 0));

        instante.ShouldBe(Escenario.Utc(2026, 1, 15, 20));
    }

    [Fact]
    public void En_verano_madrid_va_dos_horas_por_delante_de_utc()
    {
        var instante = Madrid.AUtc(new DateOnly(2026, 7, 15), new TimeOnly(21, 0));

        instante.ShouldBe(Escenario.Utc(2026, 7, 15, 19));
    }

    [Fact]
    public void Una_hora_que_no_existe_al_adelantar_el_reloj_no_se_puede_convertir()
    {
        Madrid.AUtc(new DateOnly(2026, 3, 29), new TimeOnly(2, 30)).ShouldBeNull();
    }

    [Fact]
    public void Las_horas_a_ambos_lados_del_salto_de_primavera_si_existen()
    {
        // 01:59 es todavía invierno (UTC+1); 03:00 ya es verano (UTC+2): son el mismo minuto.
        Madrid.AUtc(new DateOnly(2026, 3, 29), new TimeOnly(1, 59)).ShouldBe(Escenario.Utc(2026, 3, 29, 0, 59));
        Madrid.AUtc(new DateOnly(2026, 3, 29), new TimeOnly(3, 0)).ShouldBe(Escenario.Utc(2026, 3, 29, 1, 0));
    }

    [Fact]
    public void Una_hora_que_se_repite_al_atrasar_el_reloj_se_interpreta_como_la_primera()
    {
        // 02:30 ocurre a las 00:30 UTC (verano) y otra vez a las 01:30 UTC (invierno).
        Madrid.AUtc(new DateOnly(2026, 10, 25), new TimeOnly(2, 30)).ShouldBe(Escenario.Utc(2026, 10, 25, 0, 30));
    }

    [Fact]
    public void Despues_de_la_hora_repetida_el_reloj_ya_es_de_invierno()
    {
        Madrid.AUtc(new DateOnly(2026, 10, 25), new TimeOnly(3, 0)).ShouldBe(Escenario.Utc(2026, 10, 25, 2, 0));
    }

    [Fact]
    public void Dos_instantes_distintos_pueden_marcar_la_misma_hora_local()
    {
        var primera = Madrid.ALocal(Escenario.Utc(2026, 10, 25, 0, 30));
        var segunda = Madrid.ALocal(Escenario.Utc(2026, 10, 25, 1, 30));

        primera.TimeOfDay.ShouldBe(new TimeSpan(2, 30, 0));
        segunda.TimeOfDay.ShouldBe(new TimeSpan(2, 30, 0));
    }

    [Fact]
    public void La_fecha_local_puede_ser_distinta_de_la_fecha_utc()
    {
        // 22:30 UTC del 15 de julio son las 00:30 del 16 en Madrid.
        Madrid.FechaLocal(Escenario.Utc(2026, 7, 15, 22, 30)).ShouldBe(new DateOnly(2026, 7, 16));
    }
}
