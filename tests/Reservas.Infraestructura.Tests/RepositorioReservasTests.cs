using Microsoft.EntityFrameworkCore;

using Npgsql;

using Reservas.Dominio.Comun;
using Reservas.Dominio.Gestion;
using Reservas.Dominio.Locales;
using Reservas.Infraestructura.Persistencia;
using Reservas.Tests.Comunes;

using Shouldly;

namespace Reservas.Infraestructura.Tests;

public class RepositorioReservasTests(ServidorPostgres servidor) : BaseDeDatosTest(servidor)
{
    private async Task<Resultado> Agregar(Reserva reserva)
    {
        await using var db = NuevoContexto();
        return await new RepositorioReservas(db).AgregarAsync(reserva, [], TestContext.Current.CancellationToken);
    }

    private async Task<int> ContarAsync(Func<ReservasDbContext, IQueryable<object>> consulta)
    {
        await using var db = NuevoContexto();
        return await consulta(db).CountAsync(TestContext.Current.CancellationToken);
    }

    // --- Guardar y leer -----------------------------------------------------------------

    [Fact]
    public async Task Una_reserva_se_guarda_y_se_lee_con_todos_sus_datos()
    {
        var reserva = NuevaReserva(Cena, 6, Mesa1, Mesa3);
        (await Agregar(reserva)).EsExito.ShouldBeTrue();

        await using var db = NuevoContexto();
        var leida = await new RepositorioReservas(db).ObtenerAsync(reserva.Id, TestContext.Current.CancellationToken);

        leida.ShouldNotBeNull();
        leida.NegocioId.ShouldBe(Negocio.Id);
        leida.Intervalo.ShouldBe(Cena);
        leida.Intervalo.Inicio.Offset.ShouldBe(TimeSpan.Zero);
        leida.Comensales.ShouldBe(6);
        leida.Cliente.ShouldBe(reserva.Cliente);
        leida.MesaIds.ShouldBe([Mesa1.Id, Mesa3.Id]);
        leida.Estado.ShouldBe(EstadoReserva.Pendiente);
        leida.CodigoGestion.ShouldBe(reserva.CodigoGestion);
        leida.CreadaEn.ShouldBe(Ahora);
    }

    [Fact]
    public async Task Buscar_una_reserva_que_no_existe_devuelve_nulo()
    {
        await using var db = NuevoContexto();

        (await new RepositorioReservas(db).ObtenerAsync(Guid.NewGuid(), TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task Cada_mesa_de_la_reserva_queda_ocupada()
    {
        var reserva = NuevaReserva(Cena, 6, Mesa1, Mesa3);
        await Agregar(reserva);

        (await ContarAsync(db => db.OcupacionesMesa.Where(o => o.ReservaId == reserva.Id && o.Activa))).ShouldBe(2);
    }

    // --- La restricción de exclusión ---------------------------------------------------------

    [Fact]
    public async Task Dos_reservas_para_la_misma_mesa_a_la_misma_hora_no_caben()
    {
        (await Agregar(NuevaReserva())).EsExito.ShouldBeTrue();

        var segunda = await Agregar(NuevaReserva());

        segunda.EsFallo.ShouldBeTrue();
        segunda.Error.ShouldBe(ErroresReserva.MesaOcupada);
    }

    [Fact]
    public async Task La_reserva_rechazada_no_deja_nada_guardado()
    {
        await Agregar(NuevaReserva());
        await Agregar(NuevaReserva());

        (await ContarAsync(db => db.Reservas)).ShouldBe(1);
        (await ContarAsync(db => db.OcupacionesMesa)).ShouldBe(1);
    }

    [Theory]
    [InlineData(-30)]  // empieza antes y acaba durante la otra
    [InlineData(30)]   // empieza durante la otra
    [InlineData(1)]    // se pisan un minuto
    [InlineData(-89)]  // se pisan un minuto por el otro lado
    public async Task Los_tramos_que_se_pisan_en_la_misma_mesa_se_rechazan(int desplazamientoMinutos)
    {
        await Agregar(NuevaReserva());

        var resultado = await Agregar(NuevaReserva(Desplazado(Cena, desplazamientoMinutos)));

        resultado.Error.ShouldBe(ErroresReserva.MesaOcupada);
    }

    [Theory]
    [InlineData(90)]   // empieza justo cuando acaba la otra
    [InlineData(-90)]  // acaba justo cuando empieza la otra
    [InlineData(300)]  // mucho después
    public async Task Los_tramos_que_solo_se_tocan_o_estan_separados_caben_en_la_misma_mesa(int desplazamientoMinutos)
    {
        await Agregar(NuevaReserva());

        var resultado = await Agregar(NuevaReserva(Desplazado(Cena, desplazamientoMinutos)));

        resultado.EsExito.ShouldBeTrue();
    }

    [Fact]
    public async Task La_misma_hora_en_mesas_distintas_es_posible()
    {
        await Agregar(NuevaReserva(Cena, 2, Mesa1));

        (await Agregar(NuevaReserva(Cena, 2, Mesa2))).EsExito.ShouldBeTrue();
    }

    [Fact]
    public async Task Una_reserva_de_varias_mesas_es_todo_o_nada()
    {
        await Agregar(NuevaReserva(Cena, 2, Mesa1));

        // Pide la mesa 2 (libre) y la mesa 1 (ocupada): no se debe quedar con la mesa 2.
        var resultado = await Agregar(NuevaReserva(Cena, 6, Mesa2, Mesa1));

        resultado.Error.ShouldBe(ErroresReserva.MesaOcupada);
        (await ContarAsync(db => db.OcupacionesMesa.Where(o => o.MesaId == Mesa2.Id))).ShouldBe(0);
        (await Agregar(NuevaReserva(Cena, 2, Mesa2))).EsExito.ShouldBeTrue();
    }

    [Fact]
    public async Task Cancelar_una_reserva_libera_su_mesa()
    {
        var reserva = NuevaReserva();
        await Agregar(reserva);

        await using (var db = NuevoContexto())
        {
            var repositorio = new RepositorioReservas(db);
            var guardada = (await repositorio.ObtenerAsync(reserva.Id, TestContext.Current.CancellationToken))!;
            guardada.Cancelar().EsExito.ShouldBeTrue();
            (await repositorio.ActualizarAsync(guardada, [], TestContext.Current.CancellationToken)).EsExito.ShouldBeTrue();
        }

        (await Agregar(NuevaReserva())).EsExito.ShouldBeTrue();
    }

    [Fact]
    public async Task Una_reserva_completada_tambien_libera_su_mesa_pero_conserva_el_historico()
    {
        var reserva = Reserva.Crear(Negocio.Id, Cena, 2, Cliente(), [Mesa1.Id], OrigenReserva.Personal, Ahora).Valor;
        await Agregar(reserva);

        await using (var db = NuevoContexto())
        {
            var repositorio = new RepositorioReservas(db);
            var guardada = (await repositorio.ObtenerAsync(reserva.Id, TestContext.Current.CancellationToken))!;
            guardada.Sentar().EsExito.ShouldBeTrue();
            guardada.Completar().EsExito.ShouldBeTrue();
            await repositorio.ActualizarAsync(guardada, [], TestContext.Current.CancellationToken);
        }

        (await ContarAsync(db => db.Reservas.Where(r => r.Estado == EstadoReserva.Completada))).ShouldBe(1);
        (await ContarAsync(db => db.OcupacionesMesa.Where(o => !o.Activa))).ShouldBe(1);
        (await Agregar(NuevaReserva())).EsExito.ShouldBeTrue();
    }

    [Fact]
    public async Task La_garantia_esta_en_la_base_de_datos_y_no_solo_en_el_repositorio()
    {
        await Agregar(NuevaReserva());                                       // mesa 1, a las 21:00
        var otra = NuevaReserva(Desplazado(Cena, 600), 2, Mesa2);            // otra reserva real, en otra mesa y hora
        await Agregar(otra);

        // Un INSERT directo, saltándose el repositorio y todo el código de la aplicación:
        // ata esa otra reserva también a la mesa 1, en un tramo que se pisa con la primera.
        await using var conexion = new NpgsqlConnection(CadenaConexion);
        await conexion.OpenAsync(TestContext.Current.CancellationToken);
        await using var insertar = new NpgsqlCommand(
            "INSERT INTO ocupaciones_mesa (reserva_id, mesa_id, negocio_id, periodo, activa) " +
            "VALUES (@reserva, @mesa, @negocio, tstzrange(@inicio, @fin, '[)'), true)",
            conexion);
        insertar.Parameters.AddWithValue("reserva", otra.Id);
        insertar.Parameters.AddWithValue("mesa", Mesa1.Id);
        insertar.Parameters.AddWithValue("negocio", Negocio.Id);
        insertar.Parameters.AddWithValue("inicio", Cena.Inicio.AddMinutes(30).UtcDateTime);
        insertar.Parameters.AddWithValue("fin", Cena.Fin.AddMinutes(30).UtcDateTime);

        var error = await Should.ThrowAsync<PostgresException>(() => insertar.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));

        error.SqlState.ShouldBe(PostgresErrorCodes.ExclusionViolation);
        error.ConstraintName.ShouldBe("ex_ocupaciones_mesa_sin_solapes");
    }

    // --- Concurrencia ---------------------------------------------------------------------

    [Fact]
    public async Task Veinte_peticiones_simultaneas_para_la_misma_mesa_producen_una_sola_reserva()
    {
        var resultados = await CompetirPorLaMesaAsync(Mesa1, [.. Enumerable.Repeat(Cena, 20)]);

        resultados.Count(r => r.EsExito).ShouldBe(1);
        resultados.Count(r => r.Error == ErroresReserva.MesaOcupada).ShouldBe(19);

        (await ContarAsync(db => db.Reservas)).ShouldBe(1);
        (await ContarAsync(db => db.OcupacionesMesa)).ShouldBe(1);
    }

    [Fact]
    public async Task Cada_ronda_de_peticiones_simultaneas_tiene_exactamente_un_ganador()
    {
        // Cinco rondas de veinte peticiones, cada una por una hora distinta de la misma mesa.
        for (var ronda = 0; ronda < 5; ronda++)
        {
            var resultados = await CompetirPorLaMesaAsync(Mesa2, [.. Enumerable.Repeat(Desplazado(Cena, ronda * 200), 20)]);

            resultados.Count(r => r.EsExito).ShouldBe(1, $"ronda {ronda}");
        }

        (await ContarAsync(db => db.Reservas)).ShouldBe(5);
    }

    [Fact]
    public async Task Con_tramos_que_se_pisan_entre_si_las_reservas_aceptadas_nunca_se_solapan()
    {
        // Veinte reservas desplazadas 10 minutos cada una, de 90 minutos: casi todas chocan con
        // varias. No se sabe cuáles ganan (depende de quién llegue antes), pero las aceptadas
        // no pueden solaparse entre sí.
        var tramos = Enumerable.Range(0, 20).Select(i => Desplazado(Cena, i * 10)).ToArray();

        var resultados = await CompetirPorLaMesaAsync(Mesa3, tramos);

        resultados.Count(r => r.EsExito).ShouldBeGreaterThanOrEqualTo(1);
        resultados.ShouldAllBe(r => r.EsExito || r.Error == ErroresReserva.MesaOcupada);

        await using var db = NuevoContexto();
        var aceptadas = await db.OcupacionesMesa.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
        aceptadas.Count.ShouldBe(resultados.Count(r => r.EsExito));

        var intervalos = aceptadas.Select(o => PeriodoAIntervalo(o.Periodo)).ToList();
        for (var i = 0; i < intervalos.Count; i++)
        {
            for (var j = i + 1; j < intervalos.Count; j++)
            {
                intervalos[i].Solapa(intervalos[j]).ShouldBeFalse($"{intervalos[i]} y {intervalos[j]} se solapan");
            }
        }
    }

    [Fact]
    public async Task Si_dos_personas_modifican_la_misma_reserva_a_la_vez_gana_la_primera()
    {
        var reserva = NuevaReserva();
        await Agregar(reserva);

        await using var dbA = NuevoContexto();
        await using var dbB = NuevoContexto();
        var repositorioA = new RepositorioReservas(dbA);
        var repositorioB = new RepositorioReservas(dbB);
        var deA = (await repositorioA.ObtenerAsync(reserva.Id, TestContext.Current.CancellationToken))!;
        var deB = (await repositorioB.ObtenerAsync(reserva.Id, TestContext.Current.CancellationToken))!;

        deA.Confirmar(Ahora).EsExito.ShouldBeTrue();
        deB.Cancelar().EsExito.ShouldBeTrue();

        (await repositorioA.ActualizarAsync(deA, [], TestContext.Current.CancellationToken)).EsExito.ShouldBeTrue();
        var segunda = await repositorioB.ActualizarAsync(deB, [], TestContext.Current.CancellationToken);

        segunda.Error.ShouldBe(ErroresReserva.ConflictoConcurrencia);

        await using var comprobacion = NuevoContexto();
        var final = await new RepositorioReservas(comprobacion).ObtenerAsync(reserva.Id, TestContext.Current.CancellationToken);
        final!.Estado.ShouldBe(EstadoReserva.Confirmada);
    }

    [Fact]
    public async Task Actualizar_una_reserva_que_no_se_obtuvo_del_repositorio_es_un_error_de_programacion()
    {
        var reserva = NuevaReserva();
        await Agregar(reserva);

        await using var db = NuevoContexto();
        var repositorio = new RepositorioReservas(db);

        await Should.ThrowAsync<InvalidOperationException>(
            () => repositorio.ActualizarAsync(reserva, [], TestContext.Current.CancellationToken));
    }

    // --- Consulta de ocupaciones ------------------------------------------------------------

    [Fact]
    public async Task Las_ocupaciones_devueltas_son_las_activas_que_se_solapan_con_la_ventana()
    {
        var dentro = NuevaReserva(Cena, 2, Mesa1);
        var fuera = NuevaReserva(Desplazado(Cena, 600), 2, Mesa2);
        var cancelada = NuevaReserva(Cena, 2, Mesa3);
        await Agregar(dentro);
        await Agregar(fuera);
        await Agregar(cancelada);

        await using (var db = NuevoContexto())
        {
            var repositorio = new RepositorioReservas(db);
            var aCancelar = (await repositorio.ObtenerAsync(cancelada.Id, TestContext.Current.CancellationToken))!;
            aCancelar.Cancelar();
            await repositorio.ActualizarAsync(aCancelar, [], TestContext.Current.CancellationToken);
        }

        await using var consulta = NuevoContexto();
        var ocupaciones = await new RepositorioReservas(consulta).ObtenerOcupacionesAsync(
            Negocio.Id, Desplazado(Cena, 30), TestContext.Current.CancellationToken);

        ocupaciones.Count.ShouldBe(1);
        ocupaciones[0].MesaId.ShouldBe(Mesa1.Id);
        ocupaciones[0].Intervalo.ShouldBe(Cena);
    }

    [Fact]
    public async Task Las_ocupaciones_de_otro_negocio_no_aparecen()
    {
        await Agregar(NuevaReserva());

        await using var db = NuevoContexto();
        var ocupaciones = await new RepositorioReservas(db).ObtenerOcupacionesAsync(
            Guid.NewGuid(), Cena, TestContext.Current.CancellationToken);

        ocupaciones.ShouldBeEmpty();
    }

    // --- Auxiliares ---------------------------------------------------------------------------

    /// <summary>
    /// Lanza todas las peticiones a la vez, cada una con su propio contexto y su propia
    /// conexión, como harían peticiones HTTP simultáneas. Una señal común hace que todas
    /// intenten guardar en el mismo instante.
    /// </summary>
    private async Task<Resultado[]> CompetirPorLaMesaAsync(Mesa mesa, IntervaloTiempo[] tramos)
    {
        var salida = new TaskCompletionSource();

        var peticiones = tramos.Select(async (tramo, indice) =>
        {
            var reserva = Reserva.Crear(
                Negocio.Id, tramo, 2, DatosCliente.Crear($"Cliente {indice}", $"cliente{indice}@example.com").Valor,
                [mesa.Id], OrigenReserva.Publica, Ahora).Valor;

            await using var db = NuevoContexto();
            var repositorio = new RepositorioReservas(db);

            await salida.Task;
            return await repositorio.AgregarAsync(reserva, [], TestContext.Current.CancellationToken);
        }).ToArray();

        salida.SetResult();
        return await Task.WhenAll(peticiones);
    }

    private static IntervaloTiempo PeriodoAIntervalo(NpgsqlTypes.NpgsqlRange<DateTime> rango) =>
        new(
            new DateTimeOffset(DateTime.SpecifyKind(rango.LowerBound, DateTimeKind.Utc)),
            new DateTimeOffset(DateTime.SpecifyKind(rango.UpperBound, DateTimeKind.Utc)));
}
