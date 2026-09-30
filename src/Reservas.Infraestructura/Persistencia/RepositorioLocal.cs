using Microsoft.EntityFrameworkCore;

using Npgsql;

using Reservas.Aplicacion;
using Reservas.Aplicacion.Abstracciones;
using Reservas.Dominio.Comun;
using Reservas.Dominio.Locales;

namespace Reservas.Infraestructura.Persistencia;

/// <summary>
/// Salas, mesas, horarios y cierres. Todas las consultas piden el negocio de forma explícita
/// (además del filtro global del contexto): dos defensas independientes para un mismo riesgo.
/// </summary>
public sealed class RepositorioLocal(ReservasDbContext db) : IRepositorioLocal
{
    private const string IdHorarioOCierre = "Id";

    // --- Salas ---

    public async Task<IReadOnlyList<Sala>> ListarSalasAsync(Guid negocioId, CancellationToken cancellationToken) =>
        await db.Salas.AsNoTracking()
            .Where(sala => EF.Property<Guid>(sala, ConstantesPersistencia.NegocioId) == negocioId)
            .OrderBy(sala => sala.Nombre)
            .ToListAsync(cancellationToken);

    public Task<bool> ExisteSalaAsync(Guid negocioId, Guid salaId, CancellationToken cancellationToken) =>
        db.Salas.AnyAsync(
            sala => sala.Id == salaId && EF.Property<Guid>(sala, ConstantesPersistencia.NegocioId) == negocioId,
            cancellationToken);

    public async Task AgregarSalaAsync(Guid negocioId, Sala sala, CancellationToken cancellationToken)
    {
        db.Salas.Add(sala);
        db.Entry(sala).Property(ConstantesPersistencia.NegocioId).CurrentValue = negocioId;
        await db.SaveChangesAsync(cancellationToken);
        db.Entry(sala).State = EntityState.Detached;
    }

    public async Task<Resultado> EliminarSalaAsync(Guid negocioId, Guid salaId, CancellationToken cancellationToken)
    {
        if (!await ExisteSalaAsync(negocioId, salaId, cancellationToken))
        {
            return Resultado.Fallo(ErroresAplicacion.SalaNoEncontrada);
        }

        try
        {
            await db.Salas
                .Where(sala => sala.Id == salaId && EF.Property<Guid>(sala, ConstantesPersistencia.NegocioId) == negocioId)
                .ExecuteDeleteAsync(cancellationToken);

            return Resultado.Exito();
        }
        catch (Exception excepcion) when (ErroresPostgres.Es(excepcion, PostgresErrorCodes.ForeignKeyViolation))
        {
            // Lo decide la clave foránea de la base de datos: una mesa creada justo ahora también cuenta.
            return Resultado.Fallo(ErroresAplicacion.SalaConMesas);
        }
    }

    // --- Mesas ---

    public async Task<IReadOnlyList<Mesa>> ListarMesasAsync(Guid negocioId, CancellationToken cancellationToken) =>
        await db.Mesas.AsNoTracking()
            .Where(mesa => EF.Property<Guid>(mesa, ConstantesPersistencia.NegocioId) == negocioId)
            .OrderBy(mesa => mesa.Nombre)
            .ToListAsync(cancellationToken);

    public async Task AgregarMesaAsync(Guid negocioId, Mesa mesa, CancellationToken cancellationToken)
    {
        db.Mesas.Add(mesa);
        db.Entry(mesa).Property(ConstantesPersistencia.NegocioId).CurrentValue = negocioId;
        await db.SaveChangesAsync(cancellationToken);
        db.Entry(mesa).State = EntityState.Detached;
    }

    public async Task<Resultado> EliminarMesaAsync(Guid negocioId, Guid mesaId, CancellationToken cancellationToken)
    {
        try
        {
            var filas = await db.Mesas
                .Where(mesa => mesa.Id == mesaId && EF.Property<Guid>(mesa, ConstantesPersistencia.NegocioId) == negocioId)
                .ExecuteDeleteAsync(cancellationToken);

            return filas == 0 ? Resultado.Fallo(ErroresAplicacion.MesaNoEncontrada) : Resultado.Exito();
        }
        catch (Exception excepcion) when (ErroresPostgres.Es(excepcion, PostgresErrorCodes.ForeignKeyViolation))
        {
            return Resultado.Fallo(ErroresAplicacion.MesaConReservas);
        }
    }

    // --- Horarios ---

    public async Task<IReadOnlyList<Registrado<Horario>>> ListarHorariosAsync(Guid negocioId, CancellationToken cancellationToken)
    {
        var filas = await db.Horarios.AsNoTracking()
            .Where(horario => EF.Property<Guid>(horario, ConstantesPersistencia.NegocioId) == negocioId)
            .Select(horario => new { Id = EF.Property<Guid>(horario, IdHorarioOCierre), Horario = horario })
            .ToListAsync(cancellationToken);

        return [.. filas
            .OrderBy(fila => fila.Horario.Dia == DayOfWeek.Sunday ? 7 : (int)fila.Horario.Dia)
            .ThenBy(fila => fila.Horario.Inicio)
            .Select(fila => new Registrado<Horario>(fila.Id, fila.Horario))];
    }

    public async Task<Guid> AgregarHorarioAsync(Guid negocioId, Horario horario, CancellationToken cancellationToken)
    {
        db.Horarios.Add(horario);
        var entrada = db.Entry(horario);
        entrada.Property(ConstantesPersistencia.NegocioId).CurrentValue = negocioId;
        await db.SaveChangesAsync(cancellationToken);

        var id = (Guid)entrada.Property(IdHorarioOCierre).CurrentValue!;
        entrada.State = EntityState.Detached;
        return id;
    }

    public async Task<Resultado> EliminarHorarioAsync(Guid negocioId, Guid horarioId, CancellationToken cancellationToken)
    {
        var filas = await db.Horarios
            .Where(horario => EF.Property<Guid>(horario, IdHorarioOCierre) == horarioId
                && EF.Property<Guid>(horario, ConstantesPersistencia.NegocioId) == negocioId)
            .ExecuteDeleteAsync(cancellationToken);

        return filas == 0 ? Resultado.Fallo(ErroresAplicacion.HorarioNoEncontrado) : Resultado.Exito();
    }

    // --- Cierres ---

    public async Task<IReadOnlyList<Registrado<Cierre>>> ListarCierresAsync(Guid negocioId, CancellationToken cancellationToken)
    {
        var filas = await db.Cierres.AsNoTracking()
            .Where(cierre => EF.Property<Guid>(cierre, ConstantesPersistencia.NegocioId) == negocioId)
            .Select(cierre => new { Id = EF.Property<Guid>(cierre, IdHorarioOCierre), Cierre = cierre })
            .ToListAsync(cancellationToken);

        return [.. filas.OrderBy(fila => fila.Cierre.Fecha).Select(fila => new Registrado<Cierre>(fila.Id, fila.Cierre))];
    }

    public async Task<Guid> AgregarCierreAsync(Guid negocioId, Cierre cierre, CancellationToken cancellationToken)
    {
        db.Cierres.Add(cierre);
        var entrada = db.Entry(cierre);
        entrada.Property(ConstantesPersistencia.NegocioId).CurrentValue = negocioId;
        await db.SaveChangesAsync(cancellationToken);

        var id = (Guid)entrada.Property(IdHorarioOCierre).CurrentValue!;
        entrada.State = EntityState.Detached;
        return id;
    }

    public async Task<Resultado> EliminarCierreAsync(Guid negocioId, Guid cierreId, CancellationToken cancellationToken)
    {
        var filas = await db.Cierres
            .Where(cierre => EF.Property<Guid>(cierre, IdHorarioOCierre) == cierreId
                && EF.Property<Guid>(cierre, ConstantesPersistencia.NegocioId) == negocioId)
            .ExecuteDeleteAsync(cancellationToken);

        return filas == 0 ? Resultado.Fallo(ErroresAplicacion.CierreNoEncontrado) : Resultado.Exito();
    }
}
