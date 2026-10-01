using Reservas.Aplicacion.Abstracciones;
using Reservas.Dominio.Comun;
using Reservas.Dominio.Personal;

namespace Reservas.Aplicacion.Personal;

/// <summary>Inicio de sesión con correo y contraseña.</summary>
public sealed class IniciarSesion(
    IRepositorioUsuarios usuarios,
    IRepositorioTokensRefresco tokens,
    IHasherContrasenas hasher,
    IEmisorTokensAcceso emisor,
    OpcionesSesion opciones,
    TimeProvider reloj)
{
    public async Task<Resultado<SesionIniciada>> EjecutarAsync(string email, string contrasena, CancellationToken cancellationToken)
    {
        var ahora = reloj.GetUtcNow();
        var usuario = await usuarios.ObtenerPorEmailAsync(Usuario.NormalizarEmail(email), cancellationToken);

        // Un correo desconocido, una cuenta bloqueada o desactivada y una contraseña incorrecta se
        // responden igual y tardan parecido: quien lo prueba no averigua qué correos existen.
        // El intento se anota ANTES de comprobar la contraseña y con la cuenta bloqueada para los demás: si se anotara
        // después, cien intentos simultáneos pasarían todos el control del bloqueo antes de que se sumase ninguno.
        if (usuario is null || !await usuarios.AnotarIntentoAsync(usuario, ahora, cancellationToken))
        {
            hasher.Verificar(usuario?.HashContrasena ?? hasher.HashFalso, contrasena);
            return Resultado.Fallo<SesionIniciada>(ErroresAplicacion.CredencialesInvalidas);
        }

        if (!hasher.Verificar(usuario.HashContrasena, contrasena))
        {
            return Resultado.Fallo<SesionIniciada>(ErroresAplicacion.CredencialesInvalidas);
        }

        // La contraseña era buena: el intento anotado no cuenta como fallo.
        usuario.RegistrarAcceso();

        return Resultado.Exito(await EmitirSesionAsync(usuario, tokens, emisor, opciones, ahora, cancellationToken));
    }

    /// <summary>Crea el token de renovación y el de acceso, y guarda todos los cambios pendientes (también los del usuario).</summary>
    internal static async Task<SesionIniciada> EmitirSesionAsync(
        Usuario usuario,
        IRepositorioTokensRefresco tokens,
        IEmisorTokensAcceso emisor,
        OpcionesSesion opciones,
        DateTimeOffset ahora,
        CancellationToken cancellationToken)
    {
        var plano = TokensRefresco.Generar();
        var refresco = TokenRefresco.Crear(usuario.Id, TokensRefresco.Hashear(plano), ahora, opciones.DuracionRefresco);

        await tokens.AgregarAsync(refresco, cancellationToken);
        await tokens.GuardarAsync(cancellationToken);

        return new SesionIniciada(emisor.Emitir(usuario, ahora), plano, refresco.ExpiraEn, usuario);
    }
}

/// <summary>Cambia un token de renovación por una sesión nueva. Cada token sirve una sola vez.</summary>
public sealed class RenovarSesion(
    IRepositorioUsuarios usuarios,
    IRepositorioTokensRefresco tokens,
    IEmisorTokensAcceso emisor,
    OpcionesSesion opciones,
    TimeProvider reloj)
{
    public async Task<Resultado<SesionIniciada>> EjecutarAsync(string tokenRefresco, CancellationToken cancellationToken)
    {
        var ahora = reloj.GetUtcNow();
        var token = await tokens.ObtenerPorHashAsync(TokensRefresco.Hashear(tokenRefresco), cancellationToken);

        if (token is null)
        {
            return Resultado.Fallo<SesionIniciada>(ErroresAplicacion.TokenInvalido);
        }

        if (token.EstaRevocado)
        {
            // Alguien presenta un token que ya se gastó: o lo robaron o el cliente está mal hecho.
            // Se cierran todas las sesiones de ese usuario y tiene que volver a entrar.
            await tokens.RevocarTodosAsync(token.UsuarioId, ahora, cancellationToken);
            return Resultado.Fallo<SesionIniciada>(ErroresAplicacion.TokenInvalido);
        }

        var usuario = await usuarios.ObtenerAsync(token.UsuarioId, cancellationToken);

        if (!token.EstaActivo(ahora) || usuario is null || !usuario.PuedeIniciarSesion(ahora))
        {
            return Resultado.Fallo<SesionIniciada>(ErroresAplicacion.TokenInvalido);
        }

        // Se gasta de forma atómica: si dos peticiones presentan el mismo token a la vez, solo una lo consigue.
        if (!await tokens.ConsumirAsync(token.Id, ahora, cancellationToken))
        {
            return Resultado.Fallo<SesionIniciada>(ErroresAplicacion.TokenInvalido);
        }

        return Resultado.Exito(await IniciarSesion.EmitirSesionAsync(usuario, tokens, emisor, opciones, ahora, cancellationToken));
    }
}

/// <summary>Cierra la sesión revocando su token de renovación. Es idempotente: cerrar dos veces no es un error.</summary>
public sealed class CerrarSesion(IRepositorioTokensRefresco tokens, TimeProvider reloj)
{
    public async Task EjecutarAsync(string tokenRefresco, CancellationToken cancellationToken)
    {
        var token = await tokens.ObtenerPorHashAsync(TokensRefresco.Hashear(tokenRefresco), cancellationToken);

        if (token is not null)
        {
            token.Revocar(reloj.GetUtcNow());
            await tokens.GuardarAsync(cancellationToken);
        }
    }
}
