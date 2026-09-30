using Reservas.Dominio.Comun;
using Reservas.Dominio.Gestion;

using Shouldly;

namespace Reservas.Dominio.Tests.Gestion;

public class ReservaTests
{
    private static readonly DateTimeOffset Ahora = Escenario.Utc(2026, 10, 1, 10);
    private static readonly IntervaloTiempo Cena = new(Escenario.Utc(2026, 10, 3, 19), Escenario.Utc(2026, 10, 3, 20, 30));
    private static readonly DatosCliente Cliente = DatosCliente.Crear("Ana Pérez", "ana@example.com").Valor;
    private static readonly Guid Mesa1 = Guid.NewGuid();
    private static readonly Guid Mesa2 = Guid.NewGuid();

    private static Reserva Nueva(OrigenReserva origen = OrigenReserva.Publica, params Guid[] mesas) =>
        Reserva.Crear(Guid.NewGuid(), Cena, 4, Cliente, mesas.Length > 0 ? mesas : [Mesa1], origen, Ahora).Valor;

    /// <summary>Lleva una reserva nueva hasta el estado pedido usando solo transiciones válidas.</summary>
    private static Reserva EnEstado(EstadoReserva estado)
    {
        var reserva = Nueva();

        switch (estado)
        {
            case EstadoReserva.Confirmada:
                reserva.Confirmar(Ahora);
                break;
            case EstadoReserva.Sentada:
                reserva.Confirmar(Ahora);
                reserva.Sentar();
                break;
            case EstadoReserva.Completada:
                reserva.Confirmar(Ahora);
                reserva.Sentar();
                reserva.Completar();
                break;
            case EstadoReserva.Cancelada:
                reserva.Cancelar();
                break;
            case EstadoReserva.NoPresentada:
                reserva.Confirmar(Ahora);
                reserva.MarcarNoPresentada(Cena.Inicio + Reserva.MargenNoPresentada);
                break;
            case EstadoReserva.Pendiente:
            default:
                break;
        }

        reserva.Estado.ShouldBe(estado, "el helper debe dejar la reserva en el estado pedido");
        return reserva;
    }

    // --- Creación -----------------------------------------------------------------

    [Fact]
    public void Una_reserva_del_publico_nace_pendiente()
    {
        Nueva(OrigenReserva.Publica).Estado.ShouldBe(EstadoReserva.Pendiente);
    }

    [Fact]
    public void Una_reserva_del_personal_nace_confirmada()
    {
        Nueva(OrigenReserva.Personal).Estado.ShouldBe(EstadoReserva.Confirmada);
    }

    [Fact]
    public void Una_reserva_guarda_sus_datos()
    {
        var negocio = Guid.NewGuid();

        var reserva = Reserva.Crear(negocio, Cena, 6, Cliente, [Mesa1, Mesa2], OrigenReserva.Publica, Ahora).Valor;

        reserva.NegocioId.ShouldBe(negocio);
        reserva.Intervalo.ShouldBe(Cena);
        reserva.Comensales.ShouldBe(6);
        reserva.Cliente.ShouldBe(Cliente);
        reserva.MesaIds.ShouldBe([Mesa1, Mesa2]);
        reserva.CreadaEn.ShouldBe(Ahora);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Los_comensales_deben_ser_positivos(int comensales)
    {
        var resultado = Reserva.Crear(Guid.NewGuid(), Cena, comensales, Cliente, [Mesa1], OrigenReserva.Publica, Ahora);

        resultado.Error.Codigo.ShouldBe("reserva.comensales_invalidos");
    }

    [Fact]
    public void Una_reserva_necesita_al_menos_una_mesa()
    {
        var resultado = Reserva.Crear(Guid.NewGuid(), Cena, 2, Cliente, [], OrigenReserva.Publica, Ahora);

        resultado.Error.Codigo.ShouldBe("reserva.sin_mesa");
    }

    [Fact]
    public void Una_mesa_repetida_solo_cuenta_una_vez()
    {
        Nueva(OrigenReserva.Publica, Mesa1, Mesa1).MesaIds.ShouldBe([Mesa1]);
    }

    [Fact]
    public void El_codigo_de_gestion_es_largo_impredecible_y_seguro_para_una_url()
    {
        var codigos = Enumerable.Range(0, 50).Select(_ => Nueva().CodigoGestion).ToList();

        codigos.Distinct().Count().ShouldBe(50);
        codigos.ShouldAllBe(c => c.Length == 22);
        codigos.SelectMany(c => c).ShouldAllBe(letra => char.IsAsciiLetterOrDigit(letra) || letra == '-' || letra == '_');
    }

    [Fact]
    public void Una_reserva_pendiente_caduca_a_los_treinta_minutos()
    {
        Nueva().CaducaEn.ShouldBe(Ahora.AddMinutes(30));
    }

    // --- Máquina de estados ---------------------------------------------------------

    [Fact]
    public void Confirmar_solo_es_posible_desde_pendiente()
    {
        foreach (var estado in Enum.GetValues<EstadoReserva>())
        {
            var resultado = EnEstado(estado).Confirmar(Ahora);

            resultado.EsExito.ShouldBe(estado == EstadoReserva.Pendiente, $"Confirmar desde {estado}");
        }
    }

    [Fact]
    public void Cancelar_solo_es_posible_desde_pendiente_o_confirmada()
    {
        foreach (var estado in Enum.GetValues<EstadoReserva>())
        {
            var resultado = EnEstado(estado).Cancelar();

            resultado.EsExito.ShouldBe(estado is EstadoReserva.Pendiente or EstadoReserva.Confirmada, $"Cancelar desde {estado}");
        }
    }

    [Fact]
    public void Sentar_solo_es_posible_desde_confirmada()
    {
        foreach (var estado in Enum.GetValues<EstadoReserva>())
        {
            var resultado = EnEstado(estado).Sentar();

            resultado.EsExito.ShouldBe(estado == EstadoReserva.Confirmada, $"Sentar desde {estado}");
        }
    }

    [Fact]
    public void Completar_solo_es_posible_desde_sentada()
    {
        foreach (var estado in Enum.GetValues<EstadoReserva>())
        {
            var resultado = EnEstado(estado).Completar();

            resultado.EsExito.ShouldBe(estado == EstadoReserva.Sentada, $"Completar desde {estado}");
        }
    }

    [Fact]
    public void Marcar_no_presentada_solo_es_posible_desde_confirmada()
    {
        var pasadoElMargen = Cena.Inicio + Reserva.MargenNoPresentada;

        foreach (var estado in Enum.GetValues<EstadoReserva>())
        {
            var resultado = EnEstado(estado).MarcarNoPresentada(pasadoElMargen);

            resultado.EsExito.ShouldBe(estado == EstadoReserva.Confirmada, $"NoPresentada desde {estado}");
        }
    }

    [Fact]
    public void Caducar_solo_es_posible_desde_pendiente()
    {
        var pasadoElPlazo = Ahora + Reserva.TiempoParaConfirmar;

        foreach (var estado in Enum.GetValues<EstadoReserva>())
        {
            var resultado = EnEstado(estado).Caducar(pasadoElPlazo);

            resultado.EsExito.ShouldBe(estado == EstadoReserva.Pendiente, $"Caducar desde {estado}");
        }
    }

    [Fact]
    public void Una_transicion_invalida_no_cambia_el_estado_y_devuelve_su_error()
    {
        var reserva = EnEstado(EstadoReserva.Completada);

        var resultado = reserva.Cancelar();

        resultado.Error.Codigo.ShouldBe("reserva.transicion_invalida");
        reserva.Estado.ShouldBe(EstadoReserva.Completada);
    }

    [Fact]
    public void El_recorrido_normal_de_una_reserva_llega_a_completada()
    {
        var reserva = Nueva();

        reserva.Confirmar(Ahora).EsExito.ShouldBeTrue();
        reserva.Sentar().EsExito.ShouldBeTrue();
        reserva.Completar().EsExito.ShouldBeTrue();

        reserva.Estado.ShouldBe(EstadoReserva.Completada);
    }

    // --- Tiempo ---------------------------------------------------------------------

    [Fact]
    public void No_se_puede_confirmar_una_reserva_pendiente_pasado_el_plazo()
    {
        var reserva = Nueva();

        var resultado = reserva.Confirmar(reserva.CaducaEn);

        resultado.Error.Codigo.ShouldBe("reserva.caducada");
        reserva.Estado.ShouldBe(EstadoReserva.Pendiente);
    }

    [Fact]
    public void Se_puede_confirmar_hasta_el_ultimo_instante_del_plazo()
    {
        Nueva().Confirmar(Ahora.AddMinutes(29).AddSeconds(59)).EsExito.ShouldBeTrue();
    }

    [Fact]
    public void Una_reserva_pendiente_no_caduca_antes_de_tiempo()
    {
        var reserva = Nueva();

        var resultado = reserva.Caducar(reserva.CaducaEn.AddTicks(-1));

        resultado.Error.Codigo.ShouldBe("reserva.no_caduca_aun");
        reserva.Estado.ShouldBe(EstadoReserva.Pendiente);
    }

    [Fact]
    public void Una_reserva_pendiente_caduca_al_cumplirse_el_plazo_y_libera_sus_mesas()
    {
        var reserva = Nueva();

        reserva.Caducar(reserva.CaducaEn).EsExito.ShouldBeTrue();

        reserva.Estado.ShouldBe(EstadoReserva.Cancelada);
        reserva.OcupaMesas.ShouldBeFalse();
    }

    [Fact]
    public void No_se_da_por_no_presentado_a_un_grupo_antes_del_margen_de_cortesia()
    {
        var reserva = EnEstado(EstadoReserva.Confirmada);

        var resultado = reserva.MarcarNoPresentada(Cena.Inicio + Reserva.MargenNoPresentada - TimeSpan.FromTicks(1));

        resultado.Error.Codigo.ShouldBe("reserva.aun_no_es_hora");
        reserva.Estado.ShouldBe(EstadoReserva.Confirmada);
    }

    [Fact]
    public void Se_da_por_no_presentado_al_cumplirse_el_margen_de_cortesia()
    {
        var reserva = EnEstado(EstadoReserva.Confirmada);

        reserva.MarcarNoPresentada(Cena.Inicio + Reserva.MargenNoPresentada).EsExito.ShouldBeTrue();

        reserva.Estado.ShouldBe(EstadoReserva.NoPresentada);
    }

    // --- Ocupación de mesas -----------------------------------------------------------

    [Theory]
    [InlineData(EstadoReserva.Pendiente, true)]
    [InlineData(EstadoReserva.Confirmada, true)]
    [InlineData(EstadoReserva.Sentada, true)]
    [InlineData(EstadoReserva.Completada, false)]
    [InlineData(EstadoReserva.Cancelada, false)]
    [InlineData(EstadoReserva.NoPresentada, false)]
    public void Solo_las_reservas_activas_ocupan_mesa(EstadoReserva estado, bool ocupa)
    {
        EnEstado(estado).OcupaMesas.ShouldBe(ocupa);
    }
}
