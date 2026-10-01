using Microsoft.EntityFrameworkCore;

using Npgsql;

using Reservas.Aplicacion.Abstracciones;
using Reservas.Dominio.Comun;
using Reservas.Dominio.Correos;
using Reservas.Dominio.Disponibilidad;
using Reservas.Dominio.Gestion;

namespace Reservas.Infraestructura.Persistencia;

/// <summary>
/// Implementación en PostgreSQL de <see cref="IRepositorioReservas"/>. Cada instancia es una
/// unidad de trabajo sobre un <see cref="ReservasDbContext"/>: tras un conflicto, el contexto
/// queda limpio y no debe reutilizarse para las reservas que se estaban guardando.
/// </summary>
public sealed class RepositorioReservas(ReservasDbContext db) : IRepositorioReservas
{
    public async Task<Resultado> AgregarAsync(Reserva reserva, IReadOnlyCollection<CorreoPendiente> correos, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reserva);
        ArgumentNullException.ThrowIfNull(correos);

        // La estrategia de ejecución permite reintentar toda la operación si el proveedor está
        // configurado para reintentar fallos transitorios (Aspire lo hace); si no, se ejecuta una vez.
        var estrategia = db.Database.CreateExecutionStrategy();

        try
        {
            await estrategia.ExecuteAsync(async () =>
            {
                DescartarPendientes();

                db.Reservas.Add(reserva);
                db.CorreosPendientes.AddRange(correos);
                db.OcupacionesMesa.AddRange(reserva.MesaIds.Select(mesaId => new OcupacionMesaEntidad(
                    reserva.Id,
                    mesaId,
                    reserva.NegocioId,
                    PeriodoPostgres.Desde(reserva.Intervalo),
                    reserva.OcupaMesas)));

                await using var transaccion = await db.Database.BeginTransactionAsync(cancellationToken);

                await HacerColaPorMesasAsync(reserva.MesaIds, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);

                await transaccion.CommitAsync(cancellationToken);
            });

            return Resultado.Exito();
        }
        catch (Exception excepcion) when (EsViolacionDeExclusion(excepcion))
        {
            // No se ha guardado nada: la transacción entera se ha deshecho.
            db.ChangeTracker.Clear();
            return Resultado.Fallo(ErroresReserva.MesaOcupada);
        }
    }

    public Task<Reserva?> ObtenerAsync(Guid id, CancellationToken cancellationToken) =>
        db.Reservas.FirstOrDefaultAsync(reserva => reserva.Id == id, cancellationToken);

    public Task<Reserva?> ObtenerPorCodigoAsync(string codigoGestion, CancellationToken cancellationToken) =>
        db.Reservas.FirstOrDefaultAsync(reserva => reserva.CodigoGestion == codigoGestion, cancellationToken);

    public async Task<Resultado> ActualizarAsync(Reserva reserva, IReadOnlyCollection<CorreoPendiente> correos, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reserva);
        ArgumentNullException.ThrowIfNull(correos);

        if (db.Entry(reserva).State == EntityState.Detached)
        {
            throw new InvalidOperationException(
                "Solo se pueden actualizar reservas obtenidas con ObtenerAsync en esta misma instancia del repositorio.");
        }

        // Una reserva cancelada, completada o no presentada libera sus mesas; una activa las mantiene.
        var ocupaciones = await db.OcupacionesMesa
            .Where(ocupacion => ocupacion.ReservaId == reserva.Id)
            .ToListAsync(cancellationToken);

        foreach (var ocupacion in ocupaciones)
        {
            ocupacion.Activa = reserva.OcupaMesas;
        }

        // Los correos van en el mismo SaveChanges (una sola transacción) que el cambio de estado.
        db.CorreosPendientes.AddRange(correos);

        try
        {
            if (!reserva.Cliente.EstaAnonimizado)
            {
                await db.SaveChangesAsync(cancellationToken);
                return Resultado.Exito();
            }

            // Borrar los datos del cliente borra también las copias que quedan de ellos fuera de la reserva: la respuesta
            // guardada para los reintentos (lleva su nombre, su correo y quizá el código) y sus correos, enviados o no.
            // Todo en la misma transacción: o se borra todo o nada.
            var estrategia = db.Database.CreateExecutionStrategy();
            await estrategia.ExecuteAsync(async () =>
            {
                await using var transaccion = await db.Database.BeginTransactionAsync(cancellationToken);
                await db.SaveChangesAsync(acceptAllChangesOnSuccess: false, cancellationToken);
                await db.ClavesIdempotencia.Where(clave => clave.ReservaId == reserva.Id).ExecuteDeleteAsync(cancellationToken);
                await db.CorreosPendientes.Where(correo => correo.ReservaId == reserva.Id).ExecuteDeleteAsync(cancellationToken);
                await transaccion.CommitAsync(cancellationToken);
            });

            db.ChangeTracker.AcceptAllChanges();
            return Resultado.Exito();
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return Resultado.Fallo(ErroresReserva.ConflictoConcurrencia);
        }
    }

    public async Task<IReadOnlyList<Reserva>> ListarPorInicioAsync(Guid negocioId, IntervaloTiempo tramo, CancellationToken cancellationToken)
    {
        var rango = PeriodoPostgres.Desde(tramo);

        // El tramo de una reserva se guarda como un valor convertido que PostgreSQL no puede
        // comparar por dentro, pero cada mesa ocupada lleva su propio rango (tstzrange, con índice):
        // se buscan por ahí las reservas que tocan el tramo y luego se afina por hora de inicio.
        var idsDelTramo = db.OcupacionesMesa
            .Where(ocupacion => ocupacion.NegocioId == negocioId && ocupacion.Periodo.Overlaps(rango))
            .Select(ocupacion => ocupacion.ReservaId);

        var candidatas = await db.Reservas
            .AsNoTracking()
            .Where(reserva => idsDelTramo.Contains(reserva.Id))
            .ToListAsync(cancellationToken);

        return [.. candidatas
            .Where(reserva => reserva.Intervalo.Inicio >= tramo.Inicio && reserva.Intervalo.Inicio < tramo.Fin)
            .OrderBy(reserva => reserva.Intervalo.Inicio)];
    }

    public async Task<IReadOnlyList<Reserva>> ListarPendientesCaducadasAsync(DateTimeOffset ahora, int maximo, CancellationToken cancellationToken)
    {
        // La caducidad es la creación más el plazo: se filtra por la columna de creación, que sí se
        // puede comparar en SQL.
        var creadasAntesDe = ahora - Reserva.TiempoParaConfirmar;

        return await db.Reservas
            .Where(reserva => reserva.Estado == EstadoReserva.Pendiente && reserva.CreadaEn <= creadasAntesDe)
            .OrderBy(reserva => reserva.CreadaEn)
            .Take(maximo)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Reserva>> ListarParaRecordatorioAsync(IntervaloTiempo tramo, int maximo, CancellationToken cancellationToken)
    {
        var rango = PeriodoPostgres.Desde(tramo);

        // Como en la agenda: las mesas ocupadas llevan su rango indexado, y por ahí se encuentran
        // las reservas que tocan el tramo; luego se afina por la hora de inicio.
        var idsDelTramo = db.OcupacionesMesa
            .Where(ocupacion => ocupacion.Activa && ocupacion.Periodo.Overlaps(rango))
            .Select(ocupacion => ocupacion.ReservaId);

        var candidatas = await db.Reservas
            .Where(reserva => idsDelTramo.Contains(reserva.Id)
                && reserva.Estado == EstadoReserva.Confirmada
                && !reserva.RecordatorioProgramado)
            .ToListAsync(cancellationToken);

        return [.. candidatas
            .Where(reserva => reserva.Intervalo.Inicio >= tramo.Inicio && reserva.Intervalo.Inicio <= tramo.Fin)
            .OrderBy(reserva => reserva.Intervalo.Inicio)
            .Take(maximo)];
    }

    public async Task<IReadOnlyList<Reserva>> ListarAnonimizablesAsync(DateTimeOffset creadasAntesDe, int maximo, CancellationToken cancellationToken)
    {
        var anonimo = DatosCliente.Anonimo.Email;

        return await db.Reservas
            .Where(reserva => reserva.CreadaEn < creadasAntesDe
                && (reserva.Estado == EstadoReserva.Cancelada
                    || reserva.Estado == EstadoReserva.Completada
                    || reserva.Estado == EstadoReserva.NoPresentada)
                && reserva.Cliente.Email != anonimo)
            .OrderBy(reserva => reserva.CreadaEn)
            .Take(maximo)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OcupacionMesa>> ObtenerOcupacionesAsync(
        Guid negocioId,
        IntervaloTiempo ventana,
        CancellationToken cancellationToken)
    {
        var rango = PeriodoPostgres.Desde(ventana);

        var ocupaciones = await db.OcupacionesMesa
            .AsNoTracking()
            .Where(ocupacion => ocupacion.NegocioId == negocioId
                && ocupacion.Activa
                && ocupacion.Periodo.Overlaps(rango))
            .ToListAsync(cancellationToken);

        return [.. ocupaciones.Select(ocupacion => new OcupacionMesa(ocupacion.MesaId, PeriodoPostgres.AIntervalo(ocupacion.Periodo)))];
    }

    /// <summary>
    /// Bloquea las filas de las mesas implicadas hasta el final de la transacción, siempre en el
    /// mismo orden (por identificador). Las peticiones que compiten por una mesa hacen cola en
    /// lugar de insertar todas a la vez: sin esto, con muchas peticiones simultáneas PostgreSQL
    /// detecta interbloqueos entre ellas (cada una ha insertado su fila y espera a la de las
    /// demás) y tarda un segundo en resolver cada uno. Con la cola, cuando le toca a una petición
    /// la ganadora ya ha confirmado y el conflicto es inmediato.
    ///
    /// Es solo una optimización de la espera: la garantía de no solapar reservas sigue siendo la
    /// restricción de exclusión, que actúa aunque este bloqueo no se hiciera.
    ///
    /// NO KEY UPDATE serializa a las peticiones entre sí sin bloquear las comprobaciones de clave
    /// foránea que hacen las demás escrituras sobre esas mesas.
    /// </summary>
    private async Task HacerColaPorMesasAsync(IReadOnlyList<Guid> mesaIds, CancellationToken cancellationToken)
    {
        var ids = mesaIds.ToArray();

        await db.Database.ExecuteSqlAsync(
            $"SELECT 1 FROM mesas WHERE id = ANY({ids}) ORDER BY id FOR NO KEY UPDATE",
            cancellationToken);
    }

    /// <summary>Quita del seguimiento lo que un intento anterior dejó a medias, sin tocar lo demás.</summary>
    private void DescartarPendientes()
    {
        foreach (var entrada in db.ChangeTracker.Entries().Where(e => e.State == EntityState.Added).ToList())
        {
            entrada.State = EntityState.Detached;
        }
    }

    private static bool EsViolacionDeExclusion(Exception excepcion) =>
        ErroresPostgres.Es(excepcion, PostgresErrorCodes.ExclusionViolation);
}
