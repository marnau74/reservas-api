using Reservas.Dominio.Personal;

namespace Reservas.Aplicacion.Abstracciones;

public interface IRepositorioTokensRefresco
{
    Task AgregarAsync(TokenRefresco token, CancellationToken cancellationToken);

    Task<TokenRefresco?> ObtenerPorHashAsync(string hashToken, CancellationToken cancellationToken);

    /// <summary>
    /// Revoca un token que sigue activo, en una sola operación atómica. Devuelve <c>false</c> si
    /// otra petición ya lo había gastado. Se guarda al momento.
    /// </summary>
    Task<bool> ConsumirAsync(Guid tokenId, DateTimeOffset ahora, CancellationToken cancellationToken);

    /// <summary>Revoca todas las sesiones renovables de un usuario (cierre forzado o token robado). Se guarda al momento.</summary>
    Task RevocarTodosAsync(Guid usuarioId, DateTimeOffset ahora, CancellationToken cancellationToken);

    Task GuardarAsync(CancellationToken cancellationToken);
}
