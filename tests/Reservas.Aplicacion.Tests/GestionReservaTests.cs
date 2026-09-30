using Microsoft.Extensions.Time.Testing;

using Reservas.Aplicacion.Correos;
using Reservas.Aplicacion.Disponibilidad;
using Reservas.Aplicacion.Negocios;
using Reservas.Aplicacion.ReservasPublicas;
using Reservas.Dominio.Gestion;

using Shouldly;

namespace Reservas.Aplicacion.Tests;

public class GestionReservaTests
{
    private readonly FakeTimeProvider _reloj = new(Escenario.UnMesAntes);
    private readonly Reservas.Dominio.Negocios.Negocio _negocio = Escenario.CrearNegocio();
    private readonly Reservas.Aplicacion.Abstracciones.ConfiguracionLocal _local = Escenario.CrearLocal();
    private readonly RepositorioNegociosFalso _negocios;
    private readonly RepositorioReservasFalso _reservas = new();
    private readonly Reserva _reserva;

    public GestionReservaTests()
    {
        _negocios = new RepositorioNegociosFalso(_negocio, _local);
        _reserva = Escenario.ReservaPendiente(_negocio, _local, Escenario.UnMesAntes);
        _reservas.Sembrar(_reserva);
    }

    [Fact]
    public async Task Consultar_una_reserva_devuelve_la_reserva_y_su_negocio()
    {
        var resultado = await new ConsultarReserva(_reservas, _negocios).EjecutarAsync(_reserva.CodigoGestion, TestContext.Current.CancellationToken);

        resultado.Valor.Reserva.ShouldBe(_reserva);
        resultado.Valor.Negocio.ShouldBe(_negocio);
    }

    [Fact]
    public async Task Un_codigo_desconocido_no_se_encuentra_en_ninguna_operacion()
    {
        var ct = TestContext.Current.CancellationToken;

        (await new ConsultarReserva(_reservas, _negocios).EjecutarAsync("desconocido", ct)).Error.Codigo.ShouldBe("reserva.no_encontrada");
        (await new ConfirmarReserva(_reservas, _negocios, new OpcionesCorreo(), _reloj).EjecutarAsync("desconocido", ct)).Error.Codigo.ShouldBe("reserva.no_encontrada");
        (await new CancelarReserva(_reservas, _negocios, new OpcionesCorreo(), _reloj).EjecutarAsync("desconocido", ct)).Error.Codigo.ShouldBe("reserva.no_encontrada");
    }

    [Fact]
    public async Task Confirmar_una_reserva_pendiente_la_confirma_y_la_guarda()
    {
        var resultado = await new ConfirmarReserva(_reservas, _negocios, new OpcionesCorreo(), _reloj).EjecutarAsync(_reserva.CodigoGestion, TestContext.Current.CancellationToken);

        resultado.EsExito.ShouldBeTrue();
        resultado.Valor.Reserva.Estado.ShouldBe(EstadoReserva.Confirmada);
        _reservas.Actualizaciones.ShouldBe(1);
    }

    [Fact]
    public async Task No_se_puede_confirmar_pasado_el_plazo_y_no_se_guarda_nada()
    {
        _reloj.Advance(Reserva.TiempoParaConfirmar);

        var resultado = await new ConfirmarReserva(_reservas, _negocios, new OpcionesCorreo(), _reloj).EjecutarAsync(_reserva.CodigoGestion, TestContext.Current.CancellationToken);

        resultado.Error.Codigo.ShouldBe("reserva.caducada");
        _reserva.Estado.ShouldBe(EstadoReserva.Pendiente);
        _reservas.Actualizaciones.ShouldBe(0);
    }

    [Fact]
    public async Task Cancelar_libera_la_reserva_y_una_segunda_cancelacion_es_un_conflicto()
    {
        var cancelar = new CancelarReserva(_reservas, _negocios, new OpcionesCorreo(), _reloj);
        var ct = TestContext.Current.CancellationToken;

        (await cancelar.EjecutarAsync(_reserva.CodigoGestion, ct)).Valor.Reserva.Estado.ShouldBe(EstadoReserva.Cancelada);

        var segunda = await cancelar.EjecutarAsync(_reserva.CodigoGestion, ct);
        segunda.Error.Codigo.ShouldBe("reserva.transicion_invalida");
        _reservas.Actualizaciones.ShouldBe(1);
    }

    [Fact]
    public async Task No_se_puede_confirmar_una_reserva_cancelada()
    {
        _reserva.Cancelar();

        var resultado = await new ConfirmarReserva(_reservas, _negocios, new OpcionesCorreo(), _reloj).EjecutarAsync(_reserva.CodigoGestion, TestContext.Current.CancellationToken);

        resultado.Error.Codigo.ShouldBe("reserva.transicion_invalida");
    }

    // --- Consulta de negocio y disponibilidad -------------------------------------------------

    [Fact]
    public async Task Consultar_un_negocio_inexistente_es_un_error()
    {
        var resultado = await new ConsultarNegocio(_negocios).EjecutarAsync("no-existe", TestContext.Current.CancellationToken);

        resultado.Error.Codigo.ShouldBe("negocio.no_encontrado");
    }

    [Fact]
    public async Task La_disponibilidad_de_un_dia_ofrece_las_franjas_libres()
    {
        var consulta = new ConsultarDisponibilidad(_negocios, new ServicioDisponibilidad(_negocios, _reservas), _reloj);

        var resultado = await consulta.EjecutarAsync(new SolicitudDisponibilidad("bar-la-plaza", Escenario.Sabado.AddDays(1), 2), TestContext.Current.CancellationToken);

        resultado.Valor.Franjas.Count.ShouldBe(12);
        resultado.Valor.Negocio.ShouldBe(_negocio);
    }

    [Fact]
    public async Task La_disponibilidad_de_un_negocio_inexistente_es_un_error()
    {
        var consulta = new ConsultarDisponibilidad(_negocios, new ServicioDisponibilidad(_negocios, _reservas), _reloj);

        var resultado = await consulta.EjecutarAsync(new SolicitudDisponibilidad("no-existe", Escenario.Sabado, 2), TestContext.Current.CancellationToken);

        resultado.Error.Codigo.ShouldBe("negocio.no_encontrado");
    }

    [Fact]
    public async Task La_disponibilidad_devuelve_los_errores_de_las_politicas_del_negocio()
    {
        var consulta = new ConsultarDisponibilidad(_negocios, new ServicioDisponibilidad(_negocios, _reservas), _reloj);

        var resultado = await consulta.EjecutarAsync(new SolicitudDisponibilidad("bar-la-plaza", Escenario.Sabado, 50), TestContext.Current.CancellationToken);

        resultado.Error.Codigo.ShouldBe("reserva.demasiados_comensales_online");
    }
}
