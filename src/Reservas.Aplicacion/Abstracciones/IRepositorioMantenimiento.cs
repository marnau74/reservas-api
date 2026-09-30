namespace Reservas.Aplicacion.Abstracciones;

/// <summary>Limpieza periódica de datos que ya no sirven. Cada método devuelve cuántas filas borró.</summary>
public interface IRepositorioMantenimiento
{
    /// <summary>Claves de idempotencia anteriores a la fecha: ya nadie puede reintentar con ellas.</summary>
    Task<int> PurgarClavesIdempotenciaAsync(DateTimeOffset antesDe, CancellationToken cancellationToken);

    /// <summary>Tokens de renovación caducados o revocados antes de la fecha.</summary>
    Task<int> PurgarTokensRefrescoAsync(DateTimeOffset antesDe, CancellationToken cancellationToken);

    /// <summary>Correos ya enviados o abandonados, anteriores a la fecha.</summary>
    Task<int> PurgarCorreosAsync(DateTimeOffset antesDe, CancellationToken cancellationToken);
}
