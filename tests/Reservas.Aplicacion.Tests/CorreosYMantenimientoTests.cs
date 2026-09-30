using Microsoft.Extensions.Time.Testing;

using Reservas.Aplicacion.Abstracciones;
using Reservas.Aplicacion.Agenda;
using Reservas.Aplicacion.Correos;
using Reservas.Aplicacion.Disponibilidad;
using Reservas.Aplicacion.Mantenimiento;
using Reservas.Aplicacion.ReservasPublicas;
using Reservas.Dominio.Comun;
using Reservas.Dominio.Correos;
using Reservas.Dominio.Gestion;
using Reservas.Dominio.Negocios;

using Shouldly;

namespace Reservas.Aplicacion.Tests;

/// <summary>Bandeja de salida en memoria, con la misma regla de reserva de correos que la real.</summary>
internal sealed class BandejaFalsa : IBandejaCorreos
{
    public List<CorreoPendiente> Correos { get; } = [];

    public int Guardados { get; private set; }

    public Task<IReadOnlyList<CorreoPendiente>> ReclamarAsync(DateTimeOffset ahora, TimeSpan reserva, int maximo, CancellationToken cancellationToken)
    {
        var lote = Correos.Where(c => c.EstaPendiente && c.ProximoIntentoEn <= ahora).Take(maximo).ToList();

        foreach (var correo in lote)
        {
            correo.Reservar(ahora + reserva);
        }

        return Task.FromResult<IReadOnlyList<CorreoPendiente>>(lote);
    }

    public Task GuardarAsync(CancellationToken cancellationToken)
    {
        Guardados++;
        return Task.CompletedTask;
    }
}

internal sealed class EnviadorFalso : IEnviadorCorreo
{
    public List<MensajeCorreo> Enviados { get; } = [];

    /// <summary>Si devuelve una excepción para un mensaje, ese envío falla con ella.</summary>
    public Func<MensajeCorreo, Exception?> Fallo { get; set; } = _ => null;

    public Task EnviarAsync(MensajeCorreo mensaje, CancellationToken cancellationToken)
    {
        if (Fallo(mensaje) is { } excepcion)
        {
            throw excepcion;
        }

        Enviados.Add(mensaje);
        return Task.CompletedTask;
    }
}

internal sealed class RepositorioMantenimientoFalso : IRepositorioMantenimiento
{
    public List<(string Tabla, DateTimeOffset AntesDe)> Purgas { get; } = [];

    public Task<int> PurgarClavesIdempotenciaAsync(DateTimeOffset antesDe, CancellationToken cancellationToken) => Registrar("claves", antesDe);

    public Task<int> PurgarTokensRefrescoAsync(DateTimeOffset antesDe, CancellationToken cancellationToken) => Registrar("tokens", antesDe);

    public Task<int> PurgarCorreosAsync(DateTimeOffset antesDe, CancellationToken cancellationToken) => Registrar("correos", antesDe);

    private Task<int> Registrar(string tabla, DateTimeOffset antesDe)
    {
        Purgas.Add((tabla, antesDe));
        return Task.FromResult(1);
    }
}

public class PlantillasCorreoTests
{
    private static readonly OpcionesCorreo Opciones = new() { UrlGestion = "https://bar.example/reservas/{codigo}" };
    private readonly Negocio _negocio = Escenario.CrearNegocio();
    private readonly ConfiguracionLocal _local = Escenario.CrearLocal();

    private Reserva Reserva(int comensales = 2, string nombre = "Ana Pérez") => Reservas.Dominio.Gestion.Reserva.Crear(
        _negocio.Id,
        new IntervaloTiempo(
            _negocio.Zona.AUtc(Escenario.Sabado, new TimeOnly(21, 0))!.Value,
            _negocio.Zona.AUtc(Escenario.Sabado, new TimeOnly(22, 30))!.Value),
        comensales,
        DatosCliente.Crear(nombre, "ana@example.com").Valor,
        [_local.Mesas[0].Id],
        OrigenReserva.Publica,
        Escenario.UnMesAntes).Valor;

    private CorreoPendiente Correo(TipoCorreo tipo, Reserva? reserva = null) =>
        PlantillasCorreo.Crear(tipo, reserva ?? Reserva(), _negocio, Opciones, Escenario.UnMesAntes);

    [Fact]
    public void La_solicitud_dice_que_reserva_cuando_y_hasta_cuando_confirmar_con_las_horas_del_local()
    {
        var reserva = Reserva();

        var correo = Correo(TipoCorreo.Solicitud, reserva);

        correo.Destinatario.ShouldBe("ana@example.com");
        correo.Asunto.ShouldBe("Confirma tu reserva en Bar La Plaza (demo)");
        correo.Cuerpo.ShouldContain("Hola Ana Pérez:");
        correo.Cuerpo.ShouldContain("Sábado 3 de octubre de 2026, a las 21:00");
        correo.Cuerpo.ShouldNotContain("19:00", Case.Insensitive, "la hora es la local del negocio, no UTC");
        correo.Cuerpo.ShouldContain("2 personas");
        correo.Cuerpo.ShouldContain("antes de las 12:30 del 1 de septiembre de 2026");
        correo.Cuerpo.ShouldContain($"https://bar.example/reservas/{reserva.CodigoGestion}");
        correo.Tipo.ShouldBe(TipoCorreo.Solicitud);
        correo.ReservaId.ShouldBe(reserva.Id);
        correo.NegocioId.ShouldBe(_negocio.Id);
    }

    [Fact]
    public void Confirmacion_y_recordatorio_llevan_el_enlace_para_cancelar()
    {
        var reserva = Reserva();

        Correo(TipoCorreo.Confirmacion, reserva).Cuerpo.ShouldContain($"https://bar.example/reservas/{reserva.CodigoGestion}");
        Correo(TipoCorreo.Recordatorio, reserva).Cuerpo.ShouldContain($"https://bar.example/reservas/{reserva.CodigoGestion}");
        Correo(TipoCorreo.Confirmacion, reserva).Asunto.ShouldBe("Reserva confirmada en Bar La Plaza (demo)");
        Correo(TipoCorreo.Recordatorio, reserva).Asunto.ShouldBe("Recordatorio de tu reserva en Bar La Plaza (demo)");
    }

    [Fact]
    public void La_cancelacion_no_lleva_ningun_enlace_ni_el_codigo_secreto()
    {
        var reserva = Reserva();

        var correo = Correo(TipoCorreo.Cancelacion, reserva);

        correo.Cuerpo.ShouldNotContain("https://");
        correo.Cuerpo.ShouldNotContain(reserva.CodigoGestion);
        correo.Cuerpo.ShouldContain("cancelada");
    }

    [Fact]
    public void Una_sola_persona_se_escribe_en_singular()
    {
        Correo(TipoCorreo.Confirmacion, Reserva(comensales: 1)).Cuerpo.ShouldContain("1 persona\n");
    }

    [Fact]
    public void Las_horas_siguen_la_hora_de_invierno_del_local()
    {
        var invierno = new DateOnly(2027, 1, 16);
        var inicio = _negocio.Zona.AUtc(invierno, new TimeOnly(21, 0))!.Value;
        var reserva = Reservas.Dominio.Gestion.Reserva.Crear(
            _negocio.Id, new IntervaloTiempo(inicio, inicio.AddMinutes(90)), 2, DatosCliente.Crear("Ana", "ana@example.com").Valor,
            [_local.Mesas[0].Id], OrigenReserva.Personal, Escenario.UnMesAntes).Valor;

        var cuerpo = Correo(TipoCorreo.Confirmacion, reserva).Cuerpo;

        inicio.UtcDateTime.Hour.ShouldBe(20, "en invierno Madrid va una hora por delante de UTC, no dos");
        cuerpo.ShouldContain("Sábado 16 de enero de 2027, a las 21:00");
    }

    [Fact]
    public void El_codigo_se_escapa_en_el_enlace()
    {
        var opciones = new OpcionesCorreo { UrlGestion = "https://bar.example/r?c={codigo}" };
        var reserva = Reserva();

        opciones.EnlaceDe(reserva).ShouldBe($"https://bar.example/r?c={Uri.EscapeDataString(reserva.CodigoGestion)}");
    }
}

public class ProcesarCorreosTests
{
    private readonly FakeTimeProvider _reloj = new(Escenario.UnMesAntes);
    private readonly BandejaFalsa _bandeja = new();
    private readonly EnviadorFalso _enviador = new();
    private readonly ProcesarCorreos _procesador;

    public ProcesarCorreosTests() => _procesador = new ProcesarCorreos(_bandeja, _enviador, _reloj);

    private CorreoPendiente Encolar(string destinatario = "ana@example.com")
    {
        var correo = CorreoPendiente.Crear(Guid.NewGuid(), null, TipoCorreo.Solicitud, destinatario, "Asunto", "Cuerpo", _reloj.GetUtcNow());
        _bandeja.Correos.Add(correo);
        return correo;
    }

    [Fact]
    public async Task Los_correos_pendientes_se_envian_y_se_marcan_como_enviados()
    {
        var correo = Encolar();

        var resumen = await _procesador.EjecutarAsync(TestContext.Current.CancellationToken);

        resumen.ShouldBe(new ResumenEnvio(1, 0, 0));
        _enviador.Enviados.ShouldBe([new MensajeCorreo("ana@example.com", "Asunto", "Cuerpo")]);
        correo.EnviadoEn.ShouldBe(_reloj.GetUtcNow());
        _bandeja.Guardados.ShouldBe(1);
    }

    [Fact]
    public async Task Un_correo_ya_enviado_no_se_vuelve_a_enviar()
    {
        Encolar();

        await _procesador.EjecutarAsync(TestContext.Current.CancellationToken);
        _reloj.Advance(TimeSpan.FromHours(1));
        var segunda = await _procesador.EjecutarAsync(TestContext.Current.CancellationToken);

        segunda.ShouldBe(new ResumenEnvio(0, 0, 0));
        _enviador.Enviados.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Un_fallo_programa_un_reintento_y_no_se_insiste_antes_de_tiempo()
    {
        var correo = Encolar();
        _enviador.Fallo = _ => new InvalidOperationException("servidor caído");

        var primera = await _procesador.EjecutarAsync(TestContext.Current.CancellationToken);

        primera.ShouldBe(new ResumenEnvio(0, 1, 0));
        correo.Intentos.ShouldBe(1);
        correo.UltimoError.ShouldNotBeNull().ShouldContain("servidor caído");
        correo.ProximoIntentoEn.ShouldBe(_reloj.GetUtcNow() + TimeSpan.FromMinutes(1));

        // Antes del minuto de espera no se toca; pasado el minuto, sí.
        _reloj.Advance(TimeSpan.FromSeconds(59));
        (await _procesador.EjecutarAsync(TestContext.Current.CancellationToken)).ShouldBe(new ResumenEnvio(0, 0, 0));
        correo.Intentos.ShouldBe(1);

        _enviador.Fallo = _ => null;
        _reloj.Advance(TimeSpan.FromSeconds(1));
        (await _procesador.EjecutarAsync(TestContext.Current.CancellationToken)).ShouldBe(new ResumenEnvio(1, 0, 0));
        correo.EnviadoEn.ShouldNotBeNull();
        correo.UltimoError.ShouldBeNull();
    }

    [Fact]
    public async Task Un_correo_que_falla_no_impide_enviar_los_demas()
    {
        Encolar("mala@example.com");
        Encolar("buena@example.com");
        _enviador.Fallo = m => m.Destinatario == "mala@example.com" ? new InvalidOperationException("dirección rechazada") : null;

        var resumen = await _procesador.EjecutarAsync(TestContext.Current.CancellationToken);

        resumen.ShouldBe(new ResumenEnvio(1, 1, 0));
        _enviador.Enviados.Select(m => m.Destinatario).ShouldBe(["buena@example.com"]);
    }

    [Fact]
    public async Task Tras_seis_fallos_el_correo_se_abandona_y_no_se_intenta_mas()
    {
        var correo = Encolar();
        _enviador.Fallo = _ => new InvalidOperationException("siempre falla");
        ResumenEnvio? ultimo = null;

        for (var i = 0; i < CorreoPendiente.MaximoIntentos; i++)
        {
            ultimo = await _procesador.EjecutarAsync(TestContext.Current.CancellationToken);
            _reloj.Advance(TimeSpan.FromDays(1));
        }

        ultimo.ShouldBe(new ResumenEnvio(0, 0, 1));
        correo.Abandonado.ShouldBeTrue();

        (await _procesador.EjecutarAsync(TestContext.Current.CancellationToken)).ShouldBe(new ResumenEnvio(0, 0, 0));
    }

    [Fact]
    public async Task Los_correos_se_toman_por_lotes_de_veinte()
    {
        for (var i = 0; i < 25; i++)
        {
            Encolar($"persona{i}@example.com");
        }

        (await _procesador.EjecutarAsync(TestContext.Current.CancellationToken)).Enviados.ShouldBe(ProcesarCorreos.TamanoLote);
        (await _procesador.EjecutarAsync(TestContext.Current.CancellationToken)).Enviados.ShouldBe(5);
    }

    [Fact]
    public async Task Un_correo_reservado_por_otro_proceso_no_se_envia_a_la_vez()
    {
        var correo = Encolar();
        // Otro proceso lo tiene reservado durante cinco minutos.
        await _bandeja.ReclamarAsync(_reloj.GetUtcNow(), ProcesarCorreos.Reserva, 10, TestContext.Current.CancellationToken);

        (await _procesador.EjecutarAsync(TestContext.Current.CancellationToken)).Enviados.ShouldBe(0);

        // Si ese proceso muriera, pasado el plazo el correo se recoge.
        _reloj.Advance(ProcesarCorreos.Reserva);
        (await _procesador.EjecutarAsync(TestContext.Current.CancellationToken)).Enviados.ShouldBe(1);
        correo.EnviadoEn.ShouldNotBeNull();
    }

    [Fact]
    public async Task Si_se_cancela_el_proceso_a_mitad_el_correo_no_se_da_por_fallido()
    {
        var correo = Encolar();
        using var cancelacion = new CancellationTokenSource();
        _enviador.Fallo = _ =>
        {
            cancelacion.Cancel();
            return new OperationCanceledException(cancelacion.Token);
        };

        await _procesador.EjecutarAsync(cancelacion.Token);

        correo.Intentos.ShouldBe(0);
        correo.EstaPendiente.ShouldBeTrue();
    }
}

/// <summary>Cada cambio de una reserva guarda el correo que corresponde junto a él, y solo si el cambio se hace.</summary>
public class CorreosDeLosCasosDeUsoTests
{
    private readonly FakeTimeProvider _reloj = new(Escenario.UnMesAntes);
    private readonly Negocio _negocio = Escenario.CrearNegocio();
    private readonly ConfiguracionLocal _local = Escenario.CrearLocal();
    private readonly RepositorioNegociosFalso _negocios;
    private readonly RepositorioReservasFalso _reservas = new();
    private readonly OpcionesCorreo _opciones = new();

    public CorreosDeLosCasosDeUsoTests() => _negocios = new RepositorioNegociosFalso(_negocio, _local);

    private static readonly SolicitudCrearReserva Solicitud =
        new("bar-la-plaza", Escenario.Sabado, new TimeOnly(21, 0), 2, "Ana Pérez", "ana@example.com", null);

    private CrearReserva CrearPublica() =>
        new(_negocios, new ServicioDisponibilidad(_negocios, _reservas), _reservas, _opciones, _reloj);

    private Reserva Sembrada()
    {
        var reserva = Escenario.ReservaPendiente(_negocio, _local, Escenario.UnMesAntes);
        _reservas.Sembrar(reserva);
        return reserva;
    }

    [Fact]
    public async Task Una_reserva_por_internet_guarda_la_solicitud_de_confirmacion_con_su_codigo()
    {
        var resultado = await CrearPublica().EjecutarAsync(Solicitud, TestContext.Current.CancellationToken);

        var correo = _reservas.Correos.ShouldHaveSingleItem();
        correo.Tipo.ShouldBe(TipoCorreo.Solicitud);
        correo.Destinatario.ShouldBe("ana@example.com");
        correo.ReservaId.ShouldBe(resultado.Valor.Reserva.Id);
        correo.Cuerpo.ShouldContain(resultado.Valor.Reserva.CodigoGestion);
    }

    [Fact]
    public async Task Si_la_reserva_no_se_puede_guardar_no_queda_ningun_correo()
    {
        _reservas.SimularQueOtraPeticionGana = true;

        var resultado = await CrearPublica().EjecutarAsync(Solicitud, TestContext.Current.CancellationToken);

        resultado.EsFallo.ShouldBeTrue();
        _reservas.Correos.ShouldBeEmpty();
    }

    [Fact]
    public async Task Una_reserva_del_personal_guarda_directamente_la_confirmacion()
    {
        var gestion = new GestionReservasPersonal(_negocios, _reservas, new ServicioDisponibilidad(_negocios, _reservas), _opciones, _reloj);

        var resultado = await gestion.CrearAsync(
            _negocio.Id, new SolicitudReservaPersonal(Escenario.Sabado, new TimeOnly(21, 0), 2, "Luis", "luis@example.com", null), TestContext.Current.CancellationToken);

        resultado.EsExito.ShouldBeTrue();
        _reservas.Correos.ShouldHaveSingleItem().Tipo.ShouldBe(TipoCorreo.Confirmacion);
    }

    [Fact]
    public async Task Confirmar_guarda_la_confirmacion_y_confirmar_fuera_de_plazo_no_guarda_nada()
    {
        var reserva = Sembrada();
        var confirmar = new ConfirmarReserva(_reservas, _negocios, _opciones, _reloj);

        _reloj.Advance(Reserva.TiempoParaConfirmar);
        (await confirmar.EjecutarAsync(reserva.CodigoGestion, TestContext.Current.CancellationToken)).EsFallo.ShouldBeTrue();
        _reservas.Correos.ShouldBeEmpty();
    }

    [Fact]
    public async Task Confirmar_a_tiempo_guarda_un_correo_de_confirmacion()
    {
        var reserva = Sembrada();

        await new ConfirmarReserva(_reservas, _negocios, _opciones, _reloj).EjecutarAsync(reserva.CodigoGestion, TestContext.Current.CancellationToken);

        _reservas.Correos.ShouldHaveSingleItem().Tipo.ShouldBe(TipoCorreo.Confirmacion);
    }

    [Fact]
    public async Task Cancelar_guarda_el_aviso_de_cancelacion_tanto_del_cliente_como_del_personal()
    {
        var porCliente = Sembrada();
        await new CancelarReserva(_reservas, _negocios, _opciones, _reloj).EjecutarAsync(porCliente.CodigoGestion, TestContext.Current.CancellationToken);

        var porPersonal = Sembrada();
        var gestion = new GestionReservasPersonal(_negocios, _reservas, new ServicioDisponibilidad(_negocios, _reservas), _opciones, _reloj);
        await gestion.CancelarAsync(_negocio.Id, porPersonal.Id, TestContext.Current.CancellationToken);

        _reservas.Correos.Select(c => c.Tipo).ShouldBe([TipoCorreo.Cancelacion, TipoCorreo.Cancelacion]);
    }

    [Fact]
    public async Task Sentar_completar_o_dar_por_no_presentado_no_envian_ningun_correo()
    {
        var reserva = Sembrada();
        reserva.Confirmar(_reloj.GetUtcNow());
        var gestion = new GestionReservasPersonal(_negocios, _reservas, new ServicioDisponibilidad(_negocios, _reservas), _opciones, _reloj);

        (await gestion.SentarAsync(_negocio.Id, reserva.Id, TestContext.Current.CancellationToken)).EsExito.ShouldBeTrue();
        (await gestion.CompletarAsync(_negocio.Id, reserva.Id, TestContext.Current.CancellationToken)).EsExito.ShouldBeTrue();

        _reservas.Correos.ShouldBeEmpty();
    }
}

public class MantenimientoProgramadoTests
{
    private readonly FakeTimeProvider _reloj = new(Escenario.UnMesAntes);
    private readonly Negocio _negocio = Escenario.CrearNegocio();
    private readonly ConfiguracionLocal _local = Escenario.CrearLocal();
    private readonly RepositorioReservasFalso _reservas = new();
    private readonly RepositorioMantenimientoFalso _limpieza = new();
    private readonly MantenimientoProgramado _mantenimiento;

    public MantenimientoProgramadoTests() =>
        _mantenimiento = new MantenimientoProgramado(
            _reservas, new RepositorioNegociosFalso(_negocio, _local), _limpieza, new OpcionesCorreo(), _reloj);

    private Reserva Sembrar(Action<Reserva>? preparar = null, DateTimeOffset? creada = null)
    {
        var reserva = Escenario.ReservaPendiente(_negocio, _local, creada ?? _reloj.GetUtcNow());
        preparar?.Invoke(reserva);
        _reservas.Sembrar(reserva);
        return reserva;
    }

    [Fact]
    public async Task Una_reserva_pendiente_caduca_justo_al_acabar_su_plazo_y_no_antes()
    {
        var reserva = Sembrar();

        _reloj.Advance(Reserva.TiempoParaConfirmar - TimeSpan.FromSeconds(1));
        (await _mantenimiento.CaducarPendientesAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
        reserva.Estado.ShouldBe(EstadoReserva.Pendiente);

        _reloj.Advance(TimeSpan.FromSeconds(1));
        (await _mantenimiento.CaducarPendientesAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
        reserva.Estado.ShouldBe(EstadoReserva.Cancelada);
        reserva.OcupaMesas.ShouldBeFalse();
    }

    [Fact]
    public async Task Las_reservas_confirmadas_no_caducan_nunca()
    {
        var reserva = Sembrar(r => r.Confirmar(_reloj.GetUtcNow()));

        _reloj.Advance(TimeSpan.FromDays(5));

        (await _mantenimiento.CaducarPendientesAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
        reserva.Estado.ShouldBe(EstadoReserva.Confirmada);
    }

    [Fact]
    public async Task Todas_las_pendientes_vencidas_caducan_aunque_sean_mas_que_un_lote()
    {
        var reservas = Enumerable.Range(0, 120).Select(_ => Sembrar()).ToList();

        _reloj.Advance(Reserva.TiempoParaConfirmar);

        (await _mantenimiento.CaducarPendientesAsync(TestContext.Current.CancellationToken)).ShouldBe(120);
        reservas.ShouldAllBe(r => r.Estado == EstadoReserva.Cancelada);
    }

    [Fact]
    public async Task El_recordatorio_se_programa_una_sola_vez_dentro_de_las_ultimas_24_horas()
    {
        var reserva = Sembrar(r => r.Confirmar(_reloj.GetUtcNow()));

        // Faltan tres días: todavía no.
        _reloj.SetUtcNow(reserva.Intervalo.Inicio - TimeSpan.FromDays(3));
        (await _mantenimiento.ProgramarRecordatoriosAsync(TestContext.Current.CancellationToken)).ShouldBe(0);

        // Faltan 23 horas: sí, y una sola vez aunque la tarea se repita.
        _reloj.SetUtcNow(reserva.Intervalo.Inicio - TimeSpan.FromHours(23));
        (await _mantenimiento.ProgramarRecordatoriosAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
        (await _mantenimiento.ProgramarRecordatoriosAsync(TestContext.Current.CancellationToken)).ShouldBe(0);

        var correo = _reservas.Correos.ShouldHaveSingleItem();
        correo.Tipo.ShouldBe(TipoCorreo.Recordatorio);
        correo.ReservaId.ShouldBe(reserva.Id);
        reserva.RecordatorioProgramado.ShouldBeTrue();
    }

    [Fact]
    public async Task No_hay_recordatorio_para_reservas_pendientes_ni_para_las_que_empiezan_en_menos_de_una_hora()
    {
        var pendiente = Sembrar();
        var inminente = Sembrar(r => r.Confirmar(_reloj.GetUtcNow()));

        _reloj.SetUtcNow(inminente.Intervalo.Inicio - TimeSpan.FromMinutes(30));

        (await _mantenimiento.ProgramarRecordatoriosAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
        _reservas.Correos.ShouldBeEmpty();
        pendiente.RecordatorioProgramado.ShouldBeFalse();
    }

    [Fact]
    public async Task Los_clientes_de_reservas_viejas_ya_terminadas_se_anonimizan_y_las_activas_no()
    {
        var vieja = Sembrar(r => r.Cancelar(), creada: Escenario.UnMesAntes);
        var activaVieja = Sembrar(r => r.Confirmar(Escenario.UnMesAntes), creada: Escenario.UnMesAntes);
        var reciente = Sembrar(r => r.Cancelar(), creada: Escenario.UnMesAntes.AddMonths(20));

        _reloj.SetUtcNow(Escenario.UnMesAntes.AddMonths(MantenimientoProgramado.MesesRetencionClientes).AddDays(1));

        (await _mantenimiento.AnonimizarClientesAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
        vieja.Cliente.EstaAnonimizado.ShouldBeTrue();
        activaVieja.Cliente.EstaAnonimizado.ShouldBeFalse("una reserva activa no se anonimiza aunque sea antigua");
        reciente.Cliente.EstaAnonimizado.ShouldBeFalse();

        (await _mantenimiento.AnonimizarClientesAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task La_purga_borra_lo_anterior_al_plazo_de_cada_tabla()
    {
        await _mantenimiento.PurgarAsync(TestContext.Current.CancellationToken);

        var ahora = _reloj.GetUtcNow();
        _limpieza.Purgas.ShouldBe(
        [
            ("claves", ahora - MantenimientoProgramado.RetencionClaves),
            ("tokens", ahora - MantenimientoProgramado.RetencionTokens),
            ("correos", ahora - MantenimientoProgramado.RetencionCorreos),
        ]);
    }

    [Fact]
    public async Task Borrar_los_datos_de_una_reserva_por_peticion_del_cliente_solo_vale_si_no_esta_activa()
    {
        var reserva = Sembrar();
        var caso = new BorrarDatosCliente(_reservas);
        var ct = TestContext.Current.CancellationToken;

        (await caso.EjecutarAsync("desconocido", ct)).Error.Codigo.ShouldBe("reserva.no_encontrada");
        (await caso.EjecutarAsync(reserva.CodigoGestion, ct)).Error.Codigo.ShouldBe("reserva.activa");
        reserva.Cliente.EstaAnonimizado.ShouldBeFalse();

        reserva.Cancelar();
        (await caso.EjecutarAsync(reserva.CodigoGestion, ct)).EsExito.ShouldBeTrue();
        reserva.Cliente.EstaAnonimizado.ShouldBeTrue();

        // Repetirlo no es un error.
        (await caso.EjecutarAsync(reserva.CodigoGestion, ct)).EsExito.ShouldBeTrue();
    }
}
