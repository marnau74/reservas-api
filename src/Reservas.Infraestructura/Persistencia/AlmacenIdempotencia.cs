using Microsoft.EntityFrameworkCore;

using Npgsql;

using Reservas.Aplicacion.Abstracciones;

namespace Reservas.Infraestructura.Persistencia;

/// <summary>
/// Implementación en PostgreSQL de <see cref="IAlmacenIdempotencia"/>. La clave primaria de la
/// tabla es la que decide qué petición gana cuando llegan varias con la misma clave a la vez:
/// solo una puede insertarla.
/// </summary>
public sealed class AlmacenIdempotencia(ReservasDbContext db) : IAlmacenIdempotencia
{
    /// <summary>Una petición «en curso» que lleva más que esto sin cambios se da por abandonada (el proceso murió).</summary>
    private static readonly TimeSpan TiempoMaximoEnCurso = TimeSpan.FromMinutes(2);

    public async Task<ResultadoAdquisicion> AdquirirAsync(
        string clave,
        string huellaPeticion,
        DateTimeOffset ahora,
        CancellationToken cancellationToken)
    {
        // Dos intentos: si al leer la clave existente resulta que se acaba de liberar, se vuelve a insertar.
        for (var intento = 0; intento < 2; intento++)
        {
            db.ClavesIdempotencia.Add(new ClaveIdempotenciaEntidad(clave, huellaPeticion, ahora));

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return new ResultadoAdquisicion(EstadoAdquisicion.Adquirida);
            }
            catch (DbUpdateException excepcion) when (ErroresPostgres.Es(excepcion, PostgresErrorCodes.UniqueViolation))
            {
                db.ChangeTracker.Clear();
            }

            var existente = await db.ClavesIdempotencia.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Clave == clave, cancellationToken);

            if (existente is null)
            {
                continue;
            }

            if (existente.HuellaPeticion != huellaPeticion)
            {
                return new ResultadoAdquisicion(EstadoAdquisicion.HuellaDistinta);
            }

            if (existente.Completada)
            {
                return new ResultadoAdquisicion(
                    EstadoAdquisicion.Repetida,
                    new RespuestaGuardada(existente.EstadoHttp ?? 0, existente.TipoContenido, existente.Cuerpo ?? string.Empty, existente.Ubicacion));
            }

            if (ahora - existente.ActualizadaEn < TiempoMaximoEnCurso)
            {
                return new ResultadoAdquisicion(EstadoAdquisicion.EnCurso);
            }

            // Abandonada: se la queda quien consiga actualizarla primero. La condición sobre
            // ActualizadaEn hace que, si varias peticiones lo intentan a la vez, solo una acierte.
            var antigua = existente.ActualizadaEn;
            var filas = await db.ClavesIdempotencia
                .Where(c => c.Clave == clave && !c.Completada && c.ActualizadaEn == antigua)
                .ExecuteUpdateAsync(cambios => cambios.SetProperty(c => c.ActualizadaEn, ahora), cancellationToken);

            return new ResultadoAdquisicion(filas == 1 ? EstadoAdquisicion.Adquirida : EstadoAdquisicion.EnCurso);
        }

        return new ResultadoAdquisicion(EstadoAdquisicion.EnCurso);
    }

    public async Task CompletarAsync(string clave, RespuestaGuardada respuesta, DateTimeOffset ahora, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(respuesta);

        await db.ClavesIdempotencia
            .Where(c => c.Clave == clave)
            .ExecuteUpdateAsync(
                cambios => cambios
                    .SetProperty(c => c.Completada, true)
                    .SetProperty(c => c.EstadoHttp, respuesta.EstadoHttp)
                    .SetProperty(c => c.TipoContenido, respuesta.TipoContenido)
                    .SetProperty(c => c.Cuerpo, respuesta.Cuerpo)
                    .SetProperty(c => c.Ubicacion, respuesta.Ubicacion)
                    .SetProperty(c => c.ActualizadaEn, ahora),
                cancellationToken);
    }

    public async Task LiberarAsync(string clave, CancellationToken cancellationToken)
    {
        await db.ClavesIdempotencia
            .Where(c => c.Clave == clave && !c.Completada)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
