using Microsoft.Extensions.Time.Testing;

using Reservas.Aplicacion.Disponibilidad;
using Reservas.Aplicacion.ReservasPublicas;
using Reservas.Dominio.Gestion;

using Shouldly;

namespace Reservas.Aplicacion.Tests;

public class CrearReservaTests
{
    private readonly FakeTimeProvider _reloj = new(Escenario.UnMesAntes);
    private readonly Reservas.Dominio.Negocios.Negocio _negocio = Escenario.CrearNegocio();
    private readonly Reservas.Aplicacion.Abstracciones.ConfiguracionLocal _local = Escenario.CrearLocal();
    private readonly RepositorioNegociosFalso _negocios;
    private readonly RepositorioReservasFalso _reservas = new();
    private readonly CrearReserva _casoDeUso;

    public CrearReservaTests()
    {
        _negocios = new RepositorioNegociosFalso(_negocio, _local);
        _casoDeUso = new CrearReserva(_negocios, new ServicioDisponibilidad(_negocios, _reservas), _reservas, _reloj);
    }

    private static SolicitudCrearReserva Solicitud(string hora = "21:00", int comensales = 2, string slug = "bar-la-plaza", string email = "ana@example.com") =>
        new(slug, Escenario.Sabado, TimeOnly.Parse(hora, System.Globalization.CultureInfo.InvariantCulture), comensales, "Ana Pérez", email, null);

    [Fact]
    public async Task Crea_una_reserva_pendiente_en_la_franja_pedida()
    {
        var resultado = await _casoDeUso.EjecutarAsync(Solicitud(), TestContext.Current.CancellationToken);

        resultado.EsExito.ShouldBeTrue();
        var reserva = resultado.Valor.Reserva;
        reserva.Estado.ShouldBe(EstadoReserva.Pendiente);
        reserva.Comensales.ShouldBe(2);
        reserva.Intervalo.Inicio.ShouldBe(new DateTimeOffset(2026, 10, 3, 19, 0, 0, TimeSpan.Zero)); // 21:00 en horario de verano
        reserva.Intervalo.Duracion.ShouldBe(TimeSpan.FromMinutes(90));
        reserva.CreadaEn.ShouldBe(Escenario.UnMesAntes);
        _reservas.Reservas.ShouldContain(reserva);
        resultado.Valor.Negocio.ShouldBe(_negocio);
    }

    [Fact]
    public async Task Elige_la_mesa_mas_ajustada_al_grupo()
    {
        var resultado = await _casoDeUso.EjecutarAsync(Solicitud(comensales: 2), TestContext.Current.CancellationToken);

        // La mesa de 1 a 2 comensales, no la de 1 a 4.
        resultado.Valor.Reserva.MesaIds.Count.ShouldBe(1);
        resultado.Valor.Reserva.MesaIds[0].ShouldBe(_local.Mesas[0].Id);
    }

    [Fact]
    public async Task Un_negocio_inexistente_no_toca_nada()
    {
        var resultado = await _casoDeUso.EjecutarAsync(Solicitud(slug: "no-existe"), TestContext.Current.CancellationToken);

        resultado.Error.Codigo.ShouldBe("negocio.no_encontrado");
        _reservas.IntentosDeAgregar.ShouldBe(0);
    }

    [Fact]
    public async Task Un_cliente_invalido_se_rechaza_sin_calcular_disponibilidad()
    {
        var llamadasAntes = _negocios.LlamadasAConfiguracion;

        var resultado = await _casoDeUso.EjecutarAsync(Solicitud(email: "no-es-un-correo"), TestContext.Current.CancellationToken);

        resultado.Error.Codigo.ShouldBe("reserva.cliente_invalido");
        _negocios.LlamadasAConfiguracion.ShouldBe(llamadasAntes);
        _reservas.IntentosDeAgregar.ShouldBe(0);
    }

    [Fact]
    public async Task Una_hora_que_el_negocio_no_ofrece_no_esta_disponible()
    {
        var resultado = await _casoDeUso.EjecutarAsync(Solicitud(hora: "21:15"), TestContext.Current.CancellationToken);

        resultado.Error.Codigo.ShouldBe("reserva.franja_no_disponible");
        _reservas.IntentosDeAgregar.ShouldBe(0);
    }

    [Fact]
    public async Task Un_grupo_mayor_que_el_maximo_online_se_rechaza_con_su_error_de_negocio()
    {
        var resultado = await _casoDeUso.EjecutarAsync(Solicitud(comensales: 11), TestContext.Current.CancellationToken);

        resultado.Error.Codigo.ShouldBe("reserva.demasiados_comensales_online");
    }

    [Fact]
    public async Task Respeta_la_antelacion_minima_segun_el_reloj()
    {
        // Son las 20:30 locales del propio sábado: con una hora de antelación, las 21:00 ya no se ofrece.
        _reloj.SetUtcNow(new DateTimeOffset(2026, 10, 3, 18, 30, 0, TimeSpan.Zero));

        var resultado = await _casoDeUso.EjecutarAsync(Solicitud(hora: "21:00"), TestContext.Current.CancellationToken);

        resultado.Error.Codigo.ShouldBe("reserva.franja_no_disponible");
    }

    [Fact]
    public async Task Si_la_hora_esta_ocupada_por_otra_reserva_ya_guardada_no_esta_disponible()
    {
        var local = Escenario.CrearLocal();
        var negocios = new RepositorioNegociosFalso(_negocio, local);
        var reservas = new RepositorioReservasFalso();
        reservas.Sembrar(Escenario.ReservaPendiente(_negocio, local, Escenario.UnMesAntes));
        reservas.Sembrar(Escenario.ReservaPendiente(_negocio, local, Escenario.UnMesAntes)); // mismas mesas: las dos de la 1ª mesa
        var casoDeUso = new CrearReserva(negocios, new ServicioDisponibilidad(negocios, reservas), reservas, _reloj);

        // La mesa de 1 a 2 está ocupada, pero la de 1 a 4 sigue libre: aún cabe.
        (await casoDeUso.EjecutarAsync(Solicitud(), TestContext.Current.CancellationToken)).EsExito.ShouldBeTrue();
    }

    // --- Otra petición se queda con la mesa entre el cálculo y el guardado --------------------

    [Fact]
    public async Task Si_otras_peticiones_ganan_todas_las_mesas_se_rinde_sin_reintentar_indefinidamente()
    {
        // Cada intento se queda sin mesa por una petición simultánea. Tras dos intentos ya no
        // queda ninguna libre y el tercer cálculo no encuentra hueco: no hace falta un tercer guardado.
        var reservas = new RepositorioReservasFalso { SimularQueOtraPeticionGana = true };
        var negocios = new RepositorioNegociosFalso(_negocio, Escenario.CrearLocal());
        var casoDeUso = new CrearReserva(negocios, new ServicioDisponibilidad(negocios, reservas), reservas, _reloj);

        var resultado = await casoDeUso.EjecutarAsync(Solicitud(), TestContext.Current.CancellationToken);

        resultado.Error.Codigo.ShouldBe("reserva.franja_no_disponible");
        reservas.IntentosDeAgregar.ShouldBe(2);
    }

    [Fact]
    public async Task Una_peticion_simultanea_no_impide_reservar_si_queda_otra_mesa_libre()
    {
        var local = Escenario.CrearLocal();
        var negocios = new RepositorioNegociosFalso(_negocio, local);
        var reservas = new FalsoQueFallaSoloLaPrimeraVez();
        var casoDeUso = new CrearReserva(negocios, new ServicioDisponibilidad(negocios, reservas), reservas, _reloj);

        var resultado = await casoDeUso.EjecutarAsync(Solicitud(), TestContext.Current.CancellationToken);

        resultado.EsExito.ShouldBeTrue();
        reservas.IntentosDeAgregar.ShouldBe(2);
        // El primer intento eligió la mesa de 1 a 2; ganada por otra petición, el segundo usa la de 1 a 4.
        resultado.Valor.Reserva.MesaIds[0].ShouldBe(local.Mesas[1].Id);
    }

    [Fact]
    public async Task Un_error_que_no_es_de_mesa_ocupada_se_devuelve_sin_reintentar()
    {
        _reservas.ErrorAlAgregar = ErroresReserva.ConflictoConcurrencia;

        var resultado = await _casoDeUso.EjecutarAsync(Solicitud(), TestContext.Current.CancellationToken);

        resultado.Error.ShouldBe(ErroresReserva.ConflictoConcurrencia);
        _reservas.IntentosDeAgregar.ShouldBe(1);
    }

    /// <summary>Simula una única petición simultánea que gana la mesa que se había elegido.</summary>
    private sealed class FalsoQueFallaSoloLaPrimeraVez : Reservas.Aplicacion.Abstracciones.IRepositorioReservas
    {
        private readonly RepositorioReservasFalso _interno = new();

        public int IntentosDeAgregar { get; private set; }

        public async Task<Reservas.Dominio.Comun.Resultado> AgregarAsync(Reserva reserva, CancellationToken cancellationToken)
        {
            IntentosDeAgregar++;
            _interno.SimularQueOtraPeticionGana = IntentosDeAgregar == 1;
            return await _interno.AgregarAsync(reserva, cancellationToken);
        }

        public Task<Reserva?> ObtenerAsync(Guid id, CancellationToken cancellationToken) => _interno.ObtenerAsync(id, cancellationToken);

        public Task<Reserva?> ObtenerPorCodigoAsync(string codigoGestion, CancellationToken cancellationToken) =>
            _interno.ObtenerPorCodigoAsync(codigoGestion, cancellationToken);

        public Task<Reservas.Dominio.Comun.Resultado> ActualizarAsync(Reserva reserva, CancellationToken cancellationToken) =>
            _interno.ActualizarAsync(reserva, cancellationToken);

        public Task<IReadOnlyList<Reservas.Dominio.Disponibilidad.OcupacionMesa>> ObtenerOcupacionesAsync(
            Guid negocioId,
            Reservas.Dominio.Comun.IntervaloTiempo ventana,
            CancellationToken cancellationToken) => _interno.ObtenerOcupacionesAsync(negocioId, ventana, cancellationToken);
    }
}
