using Microsoft.Extensions.Time.Testing;

using Reservas.Aplicacion.Abstracciones;
using Reservas.Aplicacion.Agenda;
using Reservas.Aplicacion.Disponibilidad;
using Reservas.Aplicacion.Personal;

using Shouldly;

namespace Reservas.Aplicacion.Tests;

public class ContrasenasTests
{
    [Theory]
    [InlineData("Contrasena-1234", true)]
    [InlineData("abcdefghi1", true)]
    [InlineData("corta1A", false)]
    [InlineData("solo-letras-largas", false)]
    [InlineData("1234567890123", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void La_contrasena_necesita_longitud_letras_y_numeros(string? contrasena, bool esperado)
    {
        ServicioUsuarios.EsContrasenaAceptable(contrasena).ShouldBe(esperado);
    }

    [Fact]
    public void Una_contrasena_desmesurada_se_rechaza_para_no_obligar_al_servidor_a_procesarla()
    {
        ServicioUsuarios.EsContrasenaAceptable(new string('a', 200) + "1").ShouldBeFalse();
    }
}

/// <summary>
/// El aislamiento entre negocios tiene dos defensas: el filtro global de la persistencia y la
/// comprobación explícita en el caso de uso. Aquí se prueba la segunda por separado, con un
/// repositorio que (a propósito) no filtra nada.
/// </summary>
public class AislamientoEnCasosDeUsoTests
{
    private readonly FakeTimeProvider _reloj = new(Escenario.UnMesAntes);
    private readonly Reservas.Dominio.Negocios.Negocio _negocio = Escenario.CrearNegocio();
    private readonly ConfiguracionLocal _local = Escenario.CrearLocal();
    private readonly RepositorioReservasFalso _reservas = new();
    private readonly GestionReservasPersonal _gestion;
    private readonly Reservas.Dominio.Gestion.Reserva _reserva;

    public AislamientoEnCasosDeUsoTests()
    {
        var negocios = new RepositorioNegociosFalso(_negocio, _local);
        _gestion = new GestionReservasPersonal(negocios, _reservas, new ServicioDisponibilidad(negocios, _reservas), _reloj);
        _reserva = Escenario.ReservaPendiente(_negocio, _local, Escenario.UnMesAntes);
        _reservas.Sembrar(_reserva);
    }

    [Fact]
    public async Task Las_reservas_de_otro_negocio_no_se_pueden_tocar_aunque_el_repositorio_las_devuelva()
    {
        var otroNegocio = Guid.NewGuid();
        var ct = TestContext.Current.CancellationToken;

        (await _gestion.CancelarAsync(otroNegocio, _reserva.Id, ct)).Error.Codigo.ShouldBe("reserva.no_encontrada");
        (await _gestion.SentarAsync(otroNegocio, _reserva.Id, ct)).Error.Codigo.ShouldBe("reserva.no_encontrada");
        (await _gestion.CompletarAsync(otroNegocio, _reserva.Id, ct)).Error.Codigo.ShouldBe("reserva.no_encontrada");
        (await _gestion.MarcarNoPresentadaAsync(otroNegocio, _reserva.Id, ct)).Error.Codigo.ShouldBe("reserva.no_encontrada");

        _reserva.Estado.ShouldBe(Reservas.Dominio.Gestion.EstadoReserva.Pendiente);
        _reservas.Actualizaciones.ShouldBe(0);
    }

    [Fact]
    public async Task El_propio_negocio_si_puede_cancelar_su_reserva()
    {
        var resultado = await _gestion.CancelarAsync(_negocio.Id, _reserva.Id, TestContext.Current.CancellationToken);

        resultado.EsExito.ShouldBeTrue();
        _reserva.Estado.ShouldBe(Reservas.Dominio.Gestion.EstadoReserva.Cancelada);
    }

    [Fact]
    public async Task La_agenda_de_un_negocio_inexistente_es_un_error()
    {
        var resultado = await _gestion.ObtenerAgendaAsync(Guid.NewGuid(), Escenario.Sabado, TestContext.Current.CancellationToken);

        resultado.Error.Codigo.ShouldBe("negocio.no_encontrado");
    }

    [Fact]
    public async Task La_agenda_de_un_dia_solo_trae_las_reservas_que_empiezan_ese_dia_local()
    {
        var reservaDelDia = Escenario.ReservaPendiente(_negocio, _local, Escenario.UnMesAntes);
        _reservas.Sembrar(reservaDelDia);

        var dia = _negocio.Zona.FechaLocal(reservaDelDia.Intervalo.Inicio);
        var agenda = await _gestion.ObtenerAgendaAsync(_negocio.Id, dia, TestContext.Current.CancellationToken);
        var otroDia = await _gestion.ObtenerAgendaAsync(_negocio.Id, dia.AddDays(1), TestContext.Current.CancellationToken);

        agenda.Valor.Reservas.ShouldContain(reservaDelDia);
        otroDia.Valor.Reservas.ShouldNotContain(reservaDelDia);
    }
}
