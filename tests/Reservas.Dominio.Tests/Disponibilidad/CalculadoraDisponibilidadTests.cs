using Reservas.Dominio.Comun;
using Reservas.Dominio.Disponibilidad;
using Reservas.Dominio.Gestion;
using Reservas.Dominio.Locales;
using Reservas.Dominio.Negocios;

using Shouldly;

namespace Reservas.Dominio.Tests.Disponibilidad;

public class CalculadoraDisponibilidadTests
{
    // Sábado 3 de octubre de 2026 (horario de verano: Madrid = UTC+2), reservando un mes antes.
    private static readonly DateOnly Sabado = new(2026, 10, 3);
    private static readonly DateTimeOffset UnMesAntes = Escenario.Utc(2026, 9, 1, 10);

    private static IReadOnlyList<Franja> Calcular(
        DatosDisponibilidad datos,
        DateOnly? fecha = null,
        int comensales = 2,
        OrigenReserva origen = OrigenReserva.Publica,
        DateTimeOffset? ahora = null) =>
        CalculadoraDisponibilidad.Calcular(datos, fecha ?? Sabado, comensales, origen, ahora ?? UnMesAntes).Valor;

    private static string[] Horas(IEnumerable<Franja> franjas) =>
        [.. franjas.Select(f => f.HoraLocal.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture))];

    // --- Franjas ----------------------------------------------------------------------

    [Fact]
    public void Ofrece_las_franjas_de_cada_turno_en_orden()
    {
        var franjas = Calcular(Escenario.Datos());

        Horas(franjas).ShouldBe(
        [
            "13:00", "13:30", "14:00", "14:30", "15:00", "15:30",
            "20:00", "20:30", "21:00", "21:30", "22:00", "22:30",
        ]);
        franjas.Where(f => f.HoraLocal.Hour < 18).ShouldAllBe(f => f.Turno == Turno.Comida);
        franjas.Where(f => f.HoraLocal.Hour >= 18).ShouldAllBe(f => f.Turno == Turno.Cena);
    }

    [Fact]
    public void Cada_franja_dura_lo_que_dicen_las_politicas_y_lleva_su_mesa()
    {
        var franja = Calcular(Escenario.Datos())[0];

        // 13:00 hora de verano son las 11:00 UTC; la reserva estándar dura 90 minutos.
        franja.Intervalo.Inicio.ShouldBe(Escenario.Utc(2026, 10, 3, 11));
        franja.Intervalo.Duracion.ShouldBe(TimeSpan.FromMinutes(90));
        franja.MesaIds.Count.ShouldBe(1);
    }

    [Fact]
    public void Un_dia_sin_horario_no_tiene_franjas()
    {
        var soloLunes = new[] { Escenario.CrearHorario(DayOfWeek.Monday, Turno.Comida, "13:00", "15:30", 30) };

        Calcular(Escenario.Datos(horarios: soloLunes)).ShouldBeEmpty();
    }

    [Fact]
    public void Los_horarios_que_se_solapan_no_duplican_franjas()
    {
        var horarios = new[]
        {
            Escenario.CrearHorario(DayOfWeek.Saturday, Turno.Cena, "20:00", "21:00", 30),
            Escenario.CrearHorario(DayOfWeek.Saturday, Turno.Cena, "20:30", "22:00", 30),
        };

        Horas(Calcular(Escenario.Datos(horarios: horarios))).ShouldBe(["20:00", "20:30", "21:00", "21:30", "22:00"]);
    }

    // --- Cierres ----------------------------------------------------------------------

    [Fact]
    public void Un_cierre_de_todo_el_dia_quita_todas_las_franjas()
    {
        var cierres = new[] { new Cierre(Sabado, null, "Vacaciones") };

        Calcular(Escenario.Datos(cierres: cierres)).ShouldBeEmpty();
    }

    [Fact]
    public void Un_cierre_de_un_turno_solo_quita_ese_turno()
    {
        var cierres = new[] { new Cierre(Sabado, Turno.Cena, "Evento privado") };

        var franjas = Calcular(Escenario.Datos(cierres: cierres));

        franjas.Count.ShouldBe(6);
        franjas.ShouldAllBe(f => f.Turno == Turno.Comida);
    }

    [Fact]
    public void Un_cierre_de_otro_dia_no_afecta()
    {
        var cierres = new[] { new Cierre(Sabado.AddDays(1), null, "Vacaciones") };

        Calcular(Escenario.Datos(cierres: cierres)).Count.ShouldBe(12);
    }

    // --- Ocupación --------------------------------------------------------------------

    [Fact]
    public void Con_una_mesa_ocupada_se_asigna_otra_libre()
    {
        var mesas = new[] { Escenario.CrearMesa(1, 2), Escenario.CrearMesa(1, 2) };
        var comida = new IntervaloTiempo(Escenario.Utc(2026, 10, 3, 11), Escenario.Utc(2026, 10, 3, 12, 30));
        var ocupaciones = new[] { new OcupacionMesa(mesas[0].Id, comida) };

        var primera = Calcular(Escenario.Datos(mesas, ocupaciones: ocupaciones))[0];

        primera.HoraLocal.ShouldBe(new TimeOnly(13, 0));
        primera.MesaIds.ShouldBe([mesas[1].Id]);
    }

    [Fact]
    public void Con_todas_las_mesas_ocupadas_desaparecen_las_franjas_que_se_pisan()
    {
        var mesas = new[] { Escenario.CrearMesa(1, 2), Escenario.CrearMesa(1, 4) };
        // De 13:00 a 14:30 (hora local): 11:00 a 12:30 UTC.
        var comida = new IntervaloTiempo(Escenario.Utc(2026, 10, 3, 11), Escenario.Utc(2026, 10, 3, 12, 30));
        var ocupaciones = mesas.Select(m => new OcupacionMesa(m.Id, comida)).ToArray();

        var franjas = Calcular(Escenario.Datos(mesas, ocupaciones: ocupaciones));

        // Las de 13:00, 13:30 y 14:00 se pisan con esa ocupación; desde las 14:30 ya hay hueco.
        Horas(franjas.Where(f => f.Turno == Turno.Comida)).ShouldBe(["14:30", "15:00", "15:30"]);
        franjas.Count(f => f.Turno == Turno.Cena).ShouldBe(6);
    }

    [Fact]
    public void Un_grupo_grande_combina_mesas_en_cada_franja()
    {
        var mesas = new[] { Escenario.CrearMesa(1, 4, combinable: true), Escenario.CrearMesa(1, 4, combinable: true) };

        var franjas = Calcular(Escenario.Datos(mesas), comensales: 7);

        franjas.Count.ShouldBe(12);
        franjas.ShouldAllBe(f => f.MesaIds.Count == 2);
    }

    [Fact]
    public void Un_grupo_que_no_cabe_no_tiene_franjas_pero_no_es_un_error()
    {
        var resultado = CalculadoraDisponibilidad.Calcular(Escenario.Datos(), Sabado, 9, OrigenReserva.Publica, UnMesAntes);

        resultado.EsExito.ShouldBeTrue();
        resultado.Valor.ShouldBeEmpty();
    }

    // --- Políticas del negocio ----------------------------------------------------------

    [Fact]
    public void El_publico_no_puede_reservar_con_menos_antelacion_que_la_minima()
    {
        // Son las 13:00 locales (11:00 UTC) y hace falta una hora de antelación: desde las 14:00.
        var ahora = Escenario.Utc(2026, 10, 3, 11);

        var franjas = Calcular(Escenario.Datos(), ahora: ahora);

        Horas(franjas.Where(f => f.Turno == Turno.Comida)).ShouldBe(["14:00", "14:30", "15:00", "15:30"]);
        franjas.Count(f => f.Turno == Turno.Cena).ShouldBe(6);
    }

    [Fact]
    public void El_personal_puede_reservar_sin_limite_de_antelacion()
    {
        var ahora = Escenario.Utc(2026, 10, 3, 11);

        Calcular(Escenario.Datos(), origen: OrigenReserva.Personal, ahora: ahora).Count.ShouldBe(12);
    }

    [Fact]
    public void El_publico_no_puede_reservar_con_mas_dias_de_antelacion_que_el_maximo()
    {
        var ahora = Escenario.Utc(2026, 10, 3, 10);
        var datos = Escenario.Datos();

        CalculadoraDisponibilidad.Calcular(datos, new DateOnly(2026, 12, 2), 2, OrigenReserva.Publica, ahora).EsExito.ShouldBeTrue();

        var resultado = CalculadoraDisponibilidad.Calcular(datos, new DateOnly(2026, 12, 3), 2, OrigenReserva.Publica, ahora);
        resultado.Error.Codigo.ShouldBe("reserva.demasiado_lejos");
    }

    [Fact]
    public void El_personal_puede_reservar_con_mucha_antelacion()
    {
        var resultado = CalculadoraDisponibilidad.Calcular(
            Escenario.Datos(), new DateOnly(2027, 6, 1), 2, OrigenReserva.Personal, Escenario.Utc(2026, 10, 3, 10));

        resultado.EsExito.ShouldBeTrue();
    }

    [Fact]
    public void El_publico_no_puede_reservar_mas_comensales_que_el_maximo_online()
    {
        var resultado = CalculadoraDisponibilidad.Calcular(Escenario.Datos(), Sabado, 11, OrigenReserva.Publica, UnMesAntes);

        resultado.Error.Codigo.ShouldBe("reserva.demasiados_comensales_online");
    }

    [Fact]
    public void El_personal_si_puede_reservar_grupos_grandes()
    {
        CalculadoraDisponibilidad.Calcular(Escenario.Datos(), Sabado, 11, OrigenReserva.Personal, UnMesAntes)
            .EsExito.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void Los_comensales_deben_ser_positivos(int comensales)
    {
        var resultado = CalculadoraDisponibilidad.Calcular(Escenario.Datos(), Sabado, comensales, OrigenReserva.Personal, UnMesAntes);

        resultado.Error.Codigo.ShouldBe("reserva.comensales_invalidos");
    }

    // --- Cambios de hora ------------------------------------------------------------------

    [Fact]
    public void El_dia_que_se_adelanta_el_reloj_no_se_ofrecen_las_horas_que_no_existen()
    {
        // 29 de marzo de 2026: a las 02:00 los relojes pasan a las 03:00.
        var domingo = new DateOnly(2026, 3, 29);
        var horarios = new[] { Escenario.CrearHorario(DayOfWeek.Sunday, Turno.Cena, "01:00", "03:00", 30) };

        var franjas = Calcular(Escenario.Datos(horarios: horarios), domingo, ahora: Escenario.Utc(2026, 3, 1));

        Horas(franjas).ShouldBe(["01:00", "01:30", "03:00"]);
        franjas[2].Intervalo.Inicio.ShouldBe(Escenario.Utc(2026, 3, 29, 1, 0)); // 03:00 de verano
    }

    [Fact]
    public void El_dia_que_se_atrasa_el_reloj_cada_hora_repetida_se_ofrece_una_sola_vez()
    {
        // 25 de octubre de 2026: a las 03:00 los relojes vuelven a las 02:00.
        var domingo = new DateOnly(2026, 10, 25);
        var horarios = new[] { Escenario.CrearHorario(DayOfWeek.Sunday, Turno.Cena, "01:00", "03:00", 30) };

        var franjas = Calcular(Escenario.Datos(horarios: horarios), domingo, ahora: Escenario.Utc(2026, 10, 1));

        Horas(franjas).ShouldBe(["01:00", "01:30", "02:00", "02:30", "03:00"]);
        franjas[3].Intervalo.Inicio.ShouldBe(Escenario.Utc(2026, 10, 25, 0, 30)); // primera vez que son las 02:30
        franjas[4].Intervalo.Inicio.ShouldBe(Escenario.Utc(2026, 10, 25, 2, 0));  // 03:00 de invierno
    }

    [Fact]
    public void Una_reserva_dura_lo_mismo_aunque_en_medio_se_cambie_la_hora()
    {
        var dosHoras = PoliticasReserva.Crear(TimeSpan.FromHours(1), 60, 10, TimeSpan.FromHours(2)).Valor;
        var horarios = new[] { Escenario.CrearHorario(DayOfWeek.Sunday, Turno.Cena, "01:30", "01:30", 30) };
        var datos = Escenario.Datos(horarios: horarios, politicas: dosHoras);
        var zona = datos.Negocio.Zona;

        var franja = Calcular(datos, new DateOnly(2026, 3, 29), ahora: Escenario.Utc(2026, 3, 1)).Single();

        // Empieza a las 01:30 y, con el salto de las 02:00 a las 03:00, dos horas reales
        // después son las 04:30 en el reloj, no las 03:30.
        franja.Intervalo.Duracion.ShouldBe(TimeSpan.FromHours(2));
        zona.ALocal(franja.Intervalo.Fin).TimeOfDay.ShouldBe(new TimeSpan(4, 30, 0));
    }
}
