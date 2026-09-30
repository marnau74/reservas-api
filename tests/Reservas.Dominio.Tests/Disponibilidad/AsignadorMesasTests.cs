using Reservas.Dominio.Comun;
using Reservas.Dominio.Disponibilidad;
using Reservas.Dominio.Locales;

using Shouldly;

namespace Reservas.Dominio.Tests.Disponibilidad;

public class AsignadorMesasTests
{
    private static readonly IntervaloTiempo Cena = new(Escenario.Utc(2026, 10, 3, 19), Escenario.Utc(2026, 10, 3, 20, 30));

    private static IReadOnlyList<Guid> Asignar(int comensales, Mesa[] mesas, params OcupacionMesa[] ocupaciones) =>
        AsignadorMesas.Asignar(comensales, mesas, ocupaciones, Cena);

    [Fact]
    public void Elige_la_mesa_mas_ajustada_al_grupo()
    {
        var de2 = Escenario.CrearMesa(1, 2);
        var de4 = Escenario.CrearMesa(1, 4);
        var de6 = Escenario.CrearMesa(1, 6);

        Asignar(3, [de6, de2, de4]).ShouldBe([de4.Id]);
    }

    [Fact]
    public void Una_pareja_no_ocupa_una_mesa_grande_si_hay_una_pequena()
    {
        var de2 = Escenario.CrearMesa(1, 2);
        var de8 = Escenario.CrearMesa(1, 8);

        Asignar(2, [de8, de2]).ShouldBe([de2.Id]);
    }

    [Fact]
    public void Respeta_la_capacidad_minima_de_la_mesa()
    {
        var grande = Escenario.CrearMesa(4, 6);
        var mediana = Escenario.CrearMesa(2, 4);

        // Para tres, la de 4-6 no compensa: se queda la de 2-4.
        Asignar(3, [grande, mediana]).ShouldBe([mediana.Id]);
    }

    [Fact]
    public void Salta_las_mesas_ocupadas_por_una_reserva_que_se_solapa()
    {
        var a = Escenario.CrearMesa(1, 4);
        var b = Escenario.CrearMesa(1, 4);
        var ocupada = new OcupacionMesa(a.Id, new IntervaloTiempo(Cena.Inicio.AddMinutes(-30), Cena.Inicio.AddMinutes(30)));

        Asignar(2, [a, b], ocupada).ShouldBe([b.Id]);
    }

    [Fact]
    public void Una_reserva_que_acaba_justo_cuando_empieza_esta_no_bloquea_la_mesa()
    {
        var mesa = Escenario.CrearMesa(1, 4);
        var anterior = new OcupacionMesa(mesa.Id, new IntervaloTiempo(Cena.Inicio.AddMinutes(-90), Cena.Inicio));

        Asignar(2, [mesa], anterior).ShouldBe([mesa.Id]);
    }

    [Fact]
    public void Solo_bloquean_las_ocupaciones_de_su_propia_mesa()
    {
        var a = Escenario.CrearMesa(1, 4);
        var b = Escenario.CrearMesa(1, 4);
        var ocupadaB = new OcupacionMesa(b.Id, Cena);

        Asignar(2, [a, b], ocupadaB).ShouldBe([a.Id]);
    }

    [Fact]
    public void Sin_mesas_libres_no_asigna_nada()
    {
        var mesa = Escenario.CrearMesa(1, 4);

        Asignar(2, [mesa], new OcupacionMesa(mesa.Id, Cena)).ShouldBeEmpty();
    }

    [Fact]
    public void Un_grupo_que_no_cabe_en_ninguna_mesa_no_tiene_sitio()
    {
        Asignar(9, [Escenario.CrearMesa(1, 4), Escenario.CrearMesa(1, 6)]).ShouldBeEmpty();
    }

    [Fact]
    public void Combina_mesas_combinables_de_la_misma_sala_para_un_grupo_grande()
    {
        var a = Escenario.CrearMesa(1, 4, combinable: true);
        var b = Escenario.CrearMesa(1, 4, combinable: true);

        Asignar(7, [a, b]).ShouldBe([a.Id, b.Id], ignoreOrder: true);
    }

    [Fact]
    public void No_combina_mesas_de_salas_distintas()
    {
        var enSala = Escenario.CrearMesa(1, 4, combinable: true, sala: Escenario.SalaPrincipal);
        var enTerraza = Escenario.CrearMesa(1, 4, combinable: true, sala: Escenario.Terraza);

        Asignar(7, [enSala, enTerraza]).ShouldBeEmpty();
    }

    [Fact]
    public void No_combina_mesas_que_no_son_combinables()
    {
        Asignar(7, [Escenario.CrearMesa(1, 4), Escenario.CrearMesa(1, 4)]).ShouldBeEmpty();
    }

    [Fact]
    public void Combina_las_menos_mesas_posibles()
    {
        var pequena1 = Escenario.CrearMesa(1, 2, combinable: true);
        var pequena2 = Escenario.CrearMesa(1, 2, combinable: true);
        var grande1 = Escenario.CrearMesa(1, 4, combinable: true);
        var grande2 = Escenario.CrearMesa(1, 4, combinable: true);

        // Para ocho bastan dos mesas de cuatro: no hace falta gastar las cuatro.
        Asignar(8, [pequena1, pequena2, grande1, grande2]).ShouldBe([grande1.Id, grande2.Id], ignoreOrder: true);
    }

    [Fact]
    public void No_combina_mas_de_cuatro_mesas()
    {
        var mesas = Enumerable.Range(0, 5).Select(_ => Escenario.CrearMesa(1, 2, combinable: true)).ToArray();

        // Diez comensales necesitarían cinco mesas de dos.
        Asignar(10, mesas).ShouldBeEmpty();
        Asignar(8, mesas).Count.ShouldBe(4);
    }

    [Fact]
    public void Prefiere_una_sola_mesa_a_combinar()
    {
        var sola = Escenario.CrearMesa(1, 6);
        var c1 = Escenario.CrearMesa(1, 4, combinable: true);
        var c2 = Escenario.CrearMesa(1, 4, combinable: true);

        Asignar(5, [c1, c2, sola]).ShouldBe([sola.Id]);
    }

    [Fact]
    public void Las_mesas_ocupadas_no_se_combinan()
    {
        var a = Escenario.CrearMesa(1, 4, combinable: true);
        var b = Escenario.CrearMesa(1, 4, combinable: true);

        Asignar(7, [a, b], new OcupacionMesa(b.Id, Cena)).ShouldBeEmpty();
    }
}
