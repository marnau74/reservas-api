using Microsoft.EntityFrameworkCore;

using Reservas.Aplicacion.Abstracciones;
using Reservas.Dominio.Correos;

namespace Reservas.Infraestructura.Persistencia;

/// <summary>La bandeja de salida en PostgreSQL. Varias instancias de la API pueden vaciarla a la vez sin enviar dos veces el mismo correo.</summary>
public sealed class BandejaCorreos(ReservasDbContext db) : IBandejaCorreos
{
    public async Task<IReadOnlyList<CorreoPendiente>> ReclamarAsync(
        DateTimeOffset ahora,
        TimeSpan reserva,
        int maximo,
        CancellationToken cancellationToken)
    {
        var momento = ahora.ToUniversalTime();
        var hasta = (ahora + reserva).ToUniversalTime();

        // En un solo UPDATE atómico se eligen los correos que toca enviar y se les aplaza el próximo
        // intento: «FOR UPDATE SKIP LOCKED» hace que si otra instancia está eligiendo a la vez, cada
        // una salte los que la otra ya tiene en la mano en lugar de esperarla. Como el UPDATE
        // confirma al instante, no se mantiene ningún bloqueo mientras se habla con el servidor
        // de correo, que puede tardar; si el proceso muere, el correo vuelve a tocar al pasar la reserva.
        var ids = await db.Database.SqlQuery<Guid>($"""
            UPDATE correos_pendientes
            SET proximo_intento_en = {hasta}
            WHERE id IN (
                SELECT id FROM correos_pendientes
                WHERE enviado_en IS NULL AND abandonado = false AND proximo_intento_en <= {momento}
                ORDER BY proximo_intento_en
                LIMIT {maximo}
                FOR UPDATE SKIP LOCKED)
            RETURNING id AS "Value"
            """).ToListAsync(cancellationToken);

        return await db.CorreosPendientes
            .Where(correo => ids.Contains(correo.Id))
            .OrderBy(correo => correo.CreadoEn)
            .ToListAsync(cancellationToken);
    }

    public Task GuardarAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}

public sealed class RepositorioMantenimiento(ReservasDbContext db) : IRepositorioMantenimiento
{
    public Task<int> PurgarClavesIdempotenciaAsync(DateTimeOffset antesDe, CancellationToken cancellationToken)
    {
        var limite = antesDe.ToUniversalTime();

        return db.ClavesIdempotencia
            .Where(clave => clave.ActualizadaEn < limite)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public Task<int> PurgarTokensRefrescoAsync(DateTimeOffset antesDe, CancellationToken cancellationToken)
    {
        var limite = antesDe.ToUniversalTime();

        return db.TokensRefresco
            .Where(token => token.ExpiraEn < limite || (token.RevocadoEn != null && token.RevocadoEn < limite))
            .ExecuteDeleteAsync(cancellationToken);
    }

    public Task<int> PurgarCorreosAsync(DateTimeOffset antesDe, CancellationToken cancellationToken)
    {
        var limite = antesDe.ToUniversalTime();

        return db.CorreosPendientes
            .Where(correo => (correo.EnviadoEn != null && correo.EnviadoEn < limite) || (correo.Abandonado && correo.CreadoEn < limite))
            .ExecuteDeleteAsync(cancellationToken);
    }
}
