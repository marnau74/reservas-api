using Microsoft.EntityFrameworkCore;

using Npgsql;

using Reservas.Aplicacion;
using Reservas.Aplicacion.Abstracciones;
using Reservas.Dominio.Comun;
using Reservas.Dominio.Personal;

namespace Reservas.Infraestructura.Persistencia;

public sealed class RepositorioUsuarios(ReservasDbContext db) : IRepositorioUsuarios
{
    public Task<Usuario?> ObtenerPorEmailAsync(string email, CancellationToken cancellationToken) =>
        // El inicio de sesión ocurre antes de saber a qué negocio pertenece quien entra.
        db.Usuarios.IgnoreQueryFilters().FirstOrDefaultAsync(usuario => usuario.Email == email, cancellationToken);

    public Task<Usuario?> ObtenerAsync(Guid id, CancellationToken cancellationToken) =>
        db.Usuarios.FirstOrDefaultAsync(usuario => usuario.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Usuario>> ListarAsync(Guid negocioId, CancellationToken cancellationToken) =>
        await db.Usuarios
            .AsNoTracking()
            .Where(usuario => usuario.NegocioId == negocioId)
            .OrderBy(usuario => usuario.Nombre)
            .ToListAsync(cancellationToken);

    public async Task<Resultado> AgregarAsync(Usuario usuario, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(usuario);

        db.Usuarios.Add(usuario);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return Resultado.Exito();
        }
        catch (DbUpdateException excepcion) when (ErroresPostgres.Es(excepcion, PostgresErrorCodes.UniqueViolation))
        {
            db.Entry(usuario).State = EntityState.Detached;
            return Resultado.Fallo(ErroresAplicacion.EmailEnUso);
        }
    }

    public async Task<bool> AnotarIntentoAsync(Usuario usuario, DateTimeOffset ahora, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(usuario);

        var estrategia = db.Database.CreateExecutionStrategy();

        return await estrategia.ExecuteAsync(async () =>
        {
            await using var transaccion = await db.Database.BeginTransactionAsync(cancellationToken);

            // FOR UPDATE: otro intento sobre la misma cuenta espera aquí hasta que este confirme. Sin el bloqueo, dos
            // intentos simultáneos leen el mismo contador, suman uno cada uno y guardan el mismo número: se pierde uno.
            await db.Database.ExecuteSqlAsync($"SELECT 1 FROM usuarios WHERE id = {usuario.Id} FOR UPDATE", cancellationToken);
            await db.Entry(usuario).ReloadAsync(cancellationToken);

            if (!usuario.PuedeIniciarSesion(ahora))
            {
                await transaccion.CommitAsync(cancellationToken);
                return false;
            }

            usuario.RegistrarIntento(ahora);
            await db.SaveChangesAsync(cancellationToken);
            await transaccion.CommitAsync(cancellationToken);

            return true;
        });
    }

    public Task GuardarAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}

public sealed class RepositorioTokensRefresco(ReservasDbContext db) : IRepositorioTokensRefresco
{
    public Task AgregarAsync(TokenRefresco token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        db.TokensRefresco.Add(token);
        return Task.CompletedTask;
    }

    public Task<TokenRefresco?> ObtenerPorHashAsync(string hashToken, CancellationToken cancellationToken) =>
        db.TokensRefresco.FirstOrDefaultAsync(token => token.HashToken == hashToken, cancellationToken);

    public async Task<bool> ConsumirAsync(Guid tokenId, DateTimeOffset ahora, CancellationToken cancellationToken)
    {
        // «Solo si sigue sin revocar» dentro del propio UPDATE: entre dos peticiones simultáneas
        // con el mismo token, PostgreSQL deja pasar a una y la otra ve cero filas cambiadas.
        var momento = ahora.ToUniversalTime();

        var filas = await db.TokensRefresco
            .Where(token => token.Id == tokenId && token.RevocadoEn == null)
            .ExecuteUpdateAsync(cambios => cambios.SetProperty(token => token.RevocadoEn, momento), cancellationToken);

        return filas == 1;
    }

    public async Task RevocarTodosAsync(Guid usuarioId, DateTimeOffset ahora, CancellationToken cancellationToken)
    {
        var momento = ahora.ToUniversalTime();

        await db.TokensRefresco
            .Where(token => token.UsuarioId == usuarioId && token.RevocadoEn == null)
            .ExecuteUpdateAsync(cambios => cambios.SetProperty(token => token.RevocadoEn, momento), cancellationToken);
    }

    public Task GuardarAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
