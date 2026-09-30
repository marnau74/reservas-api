using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

using Reservas.Aplicacion.Abstracciones;
using Reservas.Aplicacion.Correos;
using Reservas.Aplicacion.Mantenimiento;
using Reservas.Dominio.Comun;
using Reservas.Dominio.Correos;
using Reservas.Dominio.Gestion;
using Reservas.Dominio.Locales;
using Reservas.Infraestructura.Persistencia;
using Reservas.Tests.Comunes;

using Shouldly;

namespace Reservas.Infraestructura.Tests;

internal sealed class EnviadorEnMemoria : IEnviadorCorreo
{
    public List<MensajeCorreo> Enviados { get; } = [];

    public bool Falla { get; set; }

    public Task EnviarAsync(MensajeCorreo mensaje, CancellationToken cancellationToken)
    {
        if (Falla)
        {
            throw new InvalidOperationException("servidor de correo caído");
        }

        lock (Enviados)
        {
            Enviados.Add(mensaje);
        }

        return Task.CompletedTask;
    }
}

/// <summary>Los correos se guardan en la misma transacción que la reserva o el cambio que los provoca.</summary>
public class BandejaTransaccionalTests(ServidorPostgres servidor) : BaseDeDatosTest(servidor)
{
    private CorreoPendiente Correo(Reserva reserva, TipoCorreo tipo = TipoCorreo.Solicitud) =>
        CorreoPendiente.Crear(Negocio.Id, reserva.Id, tipo, reserva.Cliente.Email, "Asunto", "Cuerpo", Ahora);

    private async Task<int> ContarCorreosAsync()
    {
        await using var db = NuevoContexto();
        return await db.CorreosPendientes.CountAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task La_reserva_y_su_correo_se_guardan_juntos()
    {
        var reserva = NuevaReserva();
        await using (var db = NuevoContexto())
        {
            (await new RepositorioReservas(db).AgregarAsync(reserva, [Correo(reserva)], TestContext.Current.CancellationToken)).EsExito.ShouldBeTrue();
        }

        await using var lectura = NuevoContexto();
        var correo = await lectura.CorreosPendientes.SingleAsync(TestContext.Current.CancellationToken);
        correo.ReservaId.ShouldBe(reserva.Id);
        correo.Destinatario.ShouldBe(reserva.Cliente.Email);
        correo.Tipo.ShouldBe(TipoCorreo.Solicitud);
        correo.EstaPendiente.ShouldBeTrue();
    }

    [Fact]
    public async Task Si_la_reserva_choca_con_otra_no_queda_ni_reserva_ni_correo()
    {
        var ganadora = NuevaReserva();
        await using (var db = NuevoContexto())
        {
            await new RepositorioReservas(db).AgregarAsync(ganadora, [Correo(ganadora)], TestContext.Current.CancellationToken);
        }

        var perdedora = NuevaReserva();
        await using (var db = NuevoContexto())
        {
            var resultado = await new RepositorioReservas(db).AgregarAsync(perdedora, [Correo(perdedora)], TestContext.Current.CancellationToken);
            resultado.Error.ShouldBe(ErroresReserva.MesaOcupada);
        }

        (await ContarCorreosAsync()).ShouldBe(1, "solo el correo de la reserva que sí se guardó");
    }

    [Fact]
    public async Task Cambiar_el_estado_y_guardar_su_correo_es_una_sola_operacion()
    {
        var reserva = NuevaReserva();
        await using (var db = NuevoContexto())
        {
            await new RepositorioReservas(db).AgregarAsync(reserva, [], TestContext.Current.CancellationToken);
        }

        await using (var db = NuevoContexto())
        {
            var repositorio = new RepositorioReservas(db);
            var cargada = (await repositorio.ObtenerAsync(reserva.Id, TestContext.Current.CancellationToken))!;
            cargada.Cancelar();
            (await repositorio.ActualizarAsync(cargada, [Correo(cargada, TipoCorreo.Cancelacion)], TestContext.Current.CancellationToken)).EsExito.ShouldBeTrue();
        }

        await using var lectura = NuevoContexto();
        (await lectura.CorreosPendientes.SingleAsync(TestContext.Current.CancellationToken)).Tipo.ShouldBe(TipoCorreo.Cancelacion);
        (await lectura.Reservas.SingleAsync(TestContext.Current.CancellationToken)).Estado.ShouldBe(EstadoReserva.Cancelada);
    }

    [Fact]
    public async Task Si_otro_cambio_se_adelanta_ni_el_cambio_ni_su_correo_se_guardan()
    {
        var reserva = NuevaReserva();
        await using (var db = NuevoContexto())
        {
            await new RepositorioReservas(db).AgregarAsync(reserva, [], TestContext.Current.CancellationToken);
        }

        await using var uno = NuevoContexto();
        await using var otro = NuevoContexto();
        var repoUno = new RepositorioReservas(uno);
        var repoOtro = new RepositorioReservas(otro);
        var deUno = (await repoUno.ObtenerAsync(reserva.Id, TestContext.Current.CancellationToken))!;
        var deOtro = (await repoOtro.ObtenerAsync(reserva.Id, TestContext.Current.CancellationToken))!;

        deUno.Confirmar(Ahora);
        (await repoUno.ActualizarAsync(deUno, [Correo(deUno, TipoCorreo.Confirmacion)], TestContext.Current.CancellationToken)).EsExito.ShouldBeTrue();

        deOtro.Cancelar();
        var conflicto = await repoOtro.ActualizarAsync(deOtro, [Correo(deOtro, TipoCorreo.Cancelacion)], TestContext.Current.CancellationToken);

        conflicto.Error.ShouldBe(ErroresReserva.ConflictoConcurrencia);
        await using var lectura = NuevoContexto();
        (await lectura.CorreosPendientes.SingleAsync(TestContext.Current.CancellationToken)).Tipo.ShouldBe(TipoCorreo.Confirmacion);
    }
}

public class BandejaCorreosTests(ServidorPostgres servidor) : BaseDeDatosTest(servidor)
{
    private async Task<List<Guid>> EncolarAsync(int cuantos, DateTimeOffset? cuando = null)
    {
        await using var db = NuevoContexto();
        var correos = Enumerable.Range(0, cuantos)
            .Select(i => CorreoPendiente.Crear(Negocio.Id, null, TipoCorreo.Solicitud, $"persona{i}@example.com", "Asunto", "Cuerpo", cuando ?? Ahora))
            .ToList();
        db.CorreosPendientes.AddRange(correos);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return [.. correos.Select(c => c.Id)];
    }

    [Fact]
    public async Task Solo_se_toman_los_correos_que_ya_toca_enviar_y_se_reservan()
    {
        await EncolarAsync(2, Ahora);
        await EncolarAsync(1, Ahora.AddHours(1));

        await using (var db = NuevoContexto())
        {
            var lote = await new BandejaCorreos(db).ReclamarAsync(Ahora, TimeSpan.FromMinutes(5), 10, TestContext.Current.CancellationToken);

            lote.Count.ShouldBe(2);
            lote.ShouldAllBe(c => c.ProximoIntentoEn == Ahora.AddMinutes(5));
        }

        // Reservados: otra pasada inmediata no los ve; pasado el plazo, sí.
        await using var otra = NuevoContexto();
        var bandeja = new BandejaCorreos(otra);
        (await bandeja.ReclamarAsync(Ahora.AddMinutes(1), TimeSpan.FromMinutes(5), 10, TestContext.Current.CancellationToken)).ShouldBeEmpty();
        (await bandeja.ReclamarAsync(Ahora.AddMinutes(5), TimeSpan.FromMinutes(5), 10, TestContext.Current.CancellationToken)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task El_lote_respeta_el_maximo_y_los_enviados_o_abandonados_no_se_vuelven_a_tomar()
    {
        var ids = await EncolarAsync(5);

        await using (var db = NuevoContexto())
        {
            var enviado = await db.CorreosPendientes.FirstAsync(c => c.Id == ids[0], TestContext.Current.CancellationToken);
            enviado.MarcarEnviado(Ahora);
            var abandonado = await db.CorreosPendientes.FirstAsync(c => c.Id == ids[1], TestContext.Current.CancellationToken);
            for (var i = 0; i < CorreoPendiente.MaximoIntentos; i++)
            {
                abandonado.RegistrarFallo("error", Ahora);
            }

            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var lectura = NuevoContexto();
        var lote = await new BandejaCorreos(lectura).ReclamarAsync(Ahora.AddDays(30), TimeSpan.FromMinutes(5), 2, TestContext.Current.CancellationToken);

        lote.Count.ShouldBe(2);
        lote.Select(c => c.Id).ShouldNotContain(ids[0]);
        lote.Select(c => c.Id).ShouldNotContain(ids[1]);
    }

    [Fact]
    public async Task Ocho_procesos_a_la_vez_reparten_los_correos_sin_repetir_ninguno()
    {
        var ids = await EncolarAsync(80);
        var salida = new TaskCompletionSource();

        var procesos = Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var db = NuevoContexto();
            var bandeja = new BandejaCorreos(db);
            await salida.Task;

            var tomados = new List<Guid>();
            while (true)
            {
                var lote = await bandeja.ReclamarAsync(Ahora, TimeSpan.FromMinutes(5), 10, TestContext.Current.CancellationToken);
                if (lote.Count == 0)
                {
                    return tomados;
                }

                tomados.AddRange(lote.Select(c => c.Id));
            }
        }).ToArray();

        salida.SetResult();
        var repartidos = (await Task.WhenAll(procesos)).SelectMany(x => x).ToList();

        repartidos.Count.ShouldBe(80, "todos los correos se envían");
        repartidos.ShouldBeUnique("ninguno se envía dos veces");
        repartidos.ShouldBe(ids, ignoreOrder: true);
    }

    [Fact]
    public async Task El_ciclo_completo_envia_guarda_el_resultado_y_programa_los_reintentos()
    {
        await EncolarAsync(2);
        var reloj = new FakeTimeProvider(Ahora);
        var enviador = new EnviadorEnMemoria { Falla = true };

        await using (var db = NuevoContexto())
        {
            var resumen = await new ProcesarCorreos(new BandejaCorreos(db), enviador, reloj).EjecutarAsync(TestContext.Current.CancellationToken);
            resumen.Fallidos.ShouldBe(2);
        }

        await using (var lectura = NuevoContexto())
        {
            var correos = await lectura.CorreosPendientes.ToListAsync(TestContext.Current.CancellationToken);
            correos.ShouldAllBe(c => c.Intentos == 1 && c.ProximoIntentoEn == Ahora.AddMinutes(1) && c.UltimoError!.Contains("caído", StringComparison.Ordinal));
        }

        enviador.Falla = false;
        reloj.Advance(TimeSpan.FromMinutes(1));
        await using (var db = NuevoContexto())
        {
            (await new ProcesarCorreos(new BandejaCorreos(db), enviador, reloj).EjecutarAsync(TestContext.Current.CancellationToken)).Enviados.ShouldBe(2);
        }

        await using var final = NuevoContexto();
        (await final.CorreosPendientes.ToListAsync(TestContext.Current.CancellationToken)).ShouldAllBe(c => c.EnviadoEn != null && c.UltimoError == null);
        enviador.Enviados.Count.ShouldBe(2);
    }
}

/// <summary>Las tareas programadas contra PostgreSQL de verdad.</summary>
public class MantenimientoEnBaseDeDatosTests(ServidorPostgres servidor) : BaseDeDatosTest(servidor)
{
    private readonly FakeTimeProvider _reloj = new(Ahora);

    private MantenimientoProgramado Mantenimiento(ReservasDbContext db) =>
        new(new RepositorioReservas(db), new RepositorioNegocios(db), new RepositorioMantenimiento(db), new OpcionesCorreo(), _reloj);

    private async Task<Reserva> GuardarAsync(Action<Reserva>? preparar = null, IntervaloTiempo? intervalo = null, DateTimeOffset? creada = null, Mesa? mesa = null)
    {
        var reserva = Reserva.Crear(
            Negocio.Id, intervalo ?? Cena, 2, Cliente(), [(mesa ?? Mesa1).Id], OrigenReserva.Publica, creada ?? Ahora).Valor;
        preparar?.Invoke(reserva);

        await using var db = NuevoContexto();
        (await new RepositorioReservas(db).AgregarAsync(reserva, [], TestContext.Current.CancellationToken)).EsExito.ShouldBeTrue();

        return reserva;
    }

    [Fact]
    public async Task Una_reserva_pendiente_vencida_caduca_y_su_mesa_vuelve_a_estar_libre()
    {
        var reserva = await GuardarAsync();
        _reloj.Advance(Reserva.TiempoParaConfirmar);

        await using (var db = NuevoContexto())
        {
            (await Mantenimiento(db).CaducarPendientesAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
        }

        await using var lectura = NuevoContexto();
        (await lectura.Reservas.SingleAsync(TestContext.Current.CancellationToken)).Estado.ShouldBe(EstadoReserva.Cancelada);
        (await new RepositorioReservas(lectura).ObtenerOcupacionesAsync(Negocio.Id, Cena, TestContext.Current.CancellationToken)).ShouldBeEmpty();

        // Y otra reserva ya puede coger esa mesa a esa hora.
        var otra = NuevaReserva();
        (await new RepositorioReservas(lectura).AgregarAsync(otra, [], TestContext.Current.CancellationToken)).EsExito.ShouldBeTrue();
        reserva.Estado.ShouldBe(EstadoReserva.Pendiente, "el objeto de prueba no cambia: solo la fila de la base de datos");
    }

    [Fact]
    public async Task Las_pendientes_todavia_en_plazo_y_las_confirmadas_no_caducan()
    {
        await GuardarAsync();
        await GuardarAsync(r => r.Confirmar(Ahora), Desplazado(Cena, 300), mesa: Mesa2);
        _reloj.Advance(Reserva.TiempoParaConfirmar - TimeSpan.FromSeconds(1));

        await using var db = NuevoContexto();
        (await Mantenimiento(db).CaducarPendientesAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task El_recordatorio_se_programa_una_vez_incluso_con_dos_procesos_a_la_vez()
    {
        await GuardarAsync(r => r.Confirmar(Ahora));
        _reloj.SetUtcNow(Cena.Inicio - TimeSpan.FromHours(20));
        var salida = new TaskCompletionSource();

        var procesos = Enumerable.Range(0, 6).Select(async _ =>
        {
            await using var db = NuevoContexto();
            var mantenimiento = Mantenimiento(db);
            await salida.Task;
            return await mantenimiento.ProgramarRecordatoriosAsync(TestContext.Current.CancellationToken);
        }).ToArray();

        salida.SetResult();
        (await Task.WhenAll(procesos)).Sum().ShouldBe(1);

        await using var lectura = NuevoContexto();
        var correo = await lectura.CorreosPendientes.SingleAsync(TestContext.Current.CancellationToken);
        correo.Tipo.ShouldBe(TipoCorreo.Recordatorio);
        (await lectura.Reservas.SingleAsync(TestContext.Current.CancellationToken)).RecordatorioProgramado.ShouldBeTrue();

        // Una ejecución posterior tampoco lo repite.
        await using var otra = NuevoContexto();
        (await Mantenimiento(otra).ProgramarRecordatoriosAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task No_hay_recordatorio_fuera_de_la_ventana_de_24_horas_ni_para_reservas_canceladas()
    {
        await GuardarAsync(r => r.Confirmar(Ahora));
        var cancelada = await GuardarAsync(r => { r.Confirmar(Ahora); r.Cancelar(); }, Desplazado(Cena, 300), mesa: Mesa2);

        _reloj.SetUtcNow(Cena.Inicio - TimeSpan.FromDays(2));
        await using (var db = NuevoContexto())
        {
            (await Mantenimiento(db).ProgramarRecordatoriosAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
        }

        _reloj.SetUtcNow(Cena.Inicio - TimeSpan.FromMinutes(30));
        await using (var db = NuevoContexto())
        {
            (await Mantenimiento(db).ProgramarRecordatoriosAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
        }

        cancelada.Estado.ShouldBe(EstadoReserva.Cancelada);
    }

    [Fact]
    public async Task La_anonimizacion_borra_nombre_correo_y_telefono_en_la_base_de_datos_y_conserva_la_reserva()
    {
        var vieja = await GuardarAsync(r => r.Cancelar(), creada: Ahora);
        var activa = await GuardarAsync(r => r.Confirmar(Ahora), Desplazado(Cena, 300), creada: Ahora, mesa: Mesa2);
        _reloj.SetUtcNow(Ahora.AddMonths(MantenimientoProgramado.MesesRetencionClientes).AddDays(1));

        await using (var db = NuevoContexto())
        {
            (await Mantenimiento(db).AnonimizarClientesAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
        }

        await using var lectura = NuevoContexto();
        var guardadas = await lectura.Reservas.ToDictionaryAsync(r => r.Id, TestContext.Current.CancellationToken);

        guardadas[vieja.Id].Cliente.EstaAnonimizado.ShouldBeTrue();
        guardadas[vieja.Id].Cliente.Email.ShouldNotContain("cliente1");
        guardadas[vieja.Id].Cliente.Telefono.ShouldBeNull();
        guardadas[vieja.Id].Comensales.ShouldBe(2);
        guardadas[vieja.Id].Intervalo.ShouldBe(vieja.Intervalo);
        guardadas[activa.Id].Cliente.EstaAnonimizado.ShouldBeFalse();

        await using var repetida = NuevoContexto();
        (await Mantenimiento(repetida).AnonimizarClientesAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task Varias_reservas_anonimizadas_a_la_vez_no_chocan_entre_si()
    {
        for (var i = 0; i < 3; i++)
        {
            await GuardarAsync(r => r.Cancelar(), Desplazado(Cena, i * 300), creada: Ahora);
        }

        _reloj.SetUtcNow(Ahora.AddMonths(MantenimientoProgramado.MesesRetencionClientes).AddDays(1));

        await using var db = NuevoContexto();
        (await Mantenimiento(db).AnonimizarClientesAsync(TestContext.Current.CancellationToken)).ShouldBe(3);
    }

    [Fact]
    public async Task La_purga_borra_solo_lo_antiguo_de_cada_tabla()
    {
        var ct = TestContext.Current.CancellationToken;
        var usuario = await SembradorDemo.CrearUsuarioAsync(NuevoContexto(), Negocio.Id, "ana@example.com", "Ana", Dominio.Personal.Rol.Personal, "Contrasena-1234", Ahora);

        await using (var db = NuevoContexto())
        {
            var vieja = new ClaveIdempotenciaEntidad("clave-vieja-000001", "h", Ahora.AddDays(-3));
            var reciente = new ClaveIdempotenciaEntidad("clave-nueva-000001", "h", Ahora);
            db.ClavesIdempotencia.AddRange(vieja, reciente);

            var caducado = Dominio.Personal.TokenRefresco.Crear(usuario.Id, new string('a', 64), Ahora.AddDays(-30), TimeSpan.FromDays(14));
            var vigente = Dominio.Personal.TokenRefresco.Crear(usuario.Id, new string('b', 64), Ahora, TimeSpan.FromDays(14));
            var revocadoHaceMucho = Dominio.Personal.TokenRefresco.Crear(usuario.Id, new string('c', 64), Ahora.AddDays(-10), TimeSpan.FromDays(14));
            revocadoHaceMucho.Revocar(Ahora.AddDays(-9));
            db.TokensRefresco.AddRange(caducado, vigente, revocadoHaceMucho);

            var enviadoViejo = CorreoPendiente.Crear(Negocio.Id, null, TipoCorreo.Solicitud, "a@example.com", "A", "C", Ahora.AddDays(-60));
            enviadoViejo.MarcarEnviado(Ahora.AddDays(-60));
            var enviadoReciente = CorreoPendiente.Crear(Negocio.Id, null, TipoCorreo.Solicitud, "b@example.com", "A", "C", Ahora);
            enviadoReciente.MarcarEnviado(Ahora);
            var pendienteViejo = CorreoPendiente.Crear(Negocio.Id, null, TipoCorreo.Solicitud, "c@example.com", "A", "C", Ahora.AddDays(-60));
            db.CorreosPendientes.AddRange(enviadoViejo, enviadoReciente, pendienteViejo);

            await db.SaveChangesAsync(ct);
        }

        await using (var db = NuevoContexto())
        {
            var purga = await Mantenimiento(db).PurgarAsync(ct);
            purga.ShouldBe(new ResumenPurga(1, 2, 1));
        }

        await using var lectura = NuevoContexto();
        (await lectura.ClavesIdempotencia.Select(c => c.Clave).ToListAsync(ct)).ShouldBe(["clave-nueva-000001"]);
        (await lectura.TokensRefresco.Select(t => t.HashToken).ToListAsync(ct)).ShouldBe([new string('b', 64)]);
        (await lectura.CorreosPendientes.Select(c => c.Destinatario).ToListAsync(ct)).ShouldBe(["b@example.com", "c@example.com"], ignoreOrder: true);
    }
}
