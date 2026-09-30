using Reservas.Aplicacion.Abstracciones;
using Reservas.Dominio.Comun;
using Reservas.Dominio.Personal;

namespace Reservas.Aplicacion.Personal;

/// <summary>Gestión de las personas que trabajan en el negocio, reservada a su propietario.</summary>
public sealed class ServicioUsuarios(
    IRepositorioUsuarios usuarios,
    IRepositorioTokensRefresco tokens,
    IHasherContrasenas hasher,
    TimeProvider reloj)
{
    public async Task<IReadOnlyList<Usuario>> ListarAsync(SesionUsuario sesion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sesion);

        return await usuarios.ListarAsync(sesion.NegocioId, cancellationToken);
    }

    public async Task<Resultado<Usuario>> CrearAsync(
        SesionUsuario sesion,
        string email,
        string nombre,
        Rol rol,
        string contrasena,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sesion);

        // Los propietarios solo se crean al dar de alta el negocio: así nadie se «asciende» a sí
        // mismo ni deja al negocio sin nadie al mando.
        if (rol is not (Rol.Personal or Rol.Encargado))
        {
            return Resultado.Fallo<Usuario>(ErroresAplicacion.RolNoPermitido);
        }

        if (!EsContrasenaAceptable(contrasena))
        {
            return Resultado.Fallo<Usuario>(ErroresAplicacion.ContrasenaDebil);
        }

        var usuario = Usuario.Crear(sesion.NegocioId, email, nombre, rol, hasher.Hashear(contrasena), reloj.GetUtcNow());
        if (usuario.EsFallo)
        {
            return usuario;
        }

        var guardado = await usuarios.AgregarAsync(usuario.Valor, cancellationToken);

        return guardado.EsFallo ? Resultado.Fallo<Usuario>(guardado.Error) : usuario;
    }

    /// <summary>Desactiva a un usuario y cierra todas sus sesiones. Solo encuentra usuarios del propio negocio.</summary>
    public async Task<Resultado> DesactivarAsync(SesionUsuario sesion, Guid usuarioId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sesion);

        var usuario = await usuarios.ObtenerAsync(usuarioId, cancellationToken);
        if (usuario is null || usuario.NegocioId != sesion.NegocioId)
        {
            return Resultado.Fallo(ErroresAplicacion.UsuarioNoEncontrado);
        }

        if (usuario.Id == sesion.UsuarioId || usuario.Rol == Rol.Propietario)
        {
            return Resultado.Fallo(ErroresAplicacion.UsuarioProtegido);
        }

        var resultado = usuario.Desactivar();
        if (resultado.EsFallo)
        {
            return resultado;
        }

        await tokens.RevocarTodosAsync(usuario.Id, reloj.GetUtcNow(), cancellationToken);
        await usuarios.GuardarAsync(cancellationToken);

        return resultado;
    }

    /// <summary>Longitud antes que rarezas: al menos 10 caracteres con letras y números.</summary>
    public static bool EsContrasenaAceptable(string? contrasena) =>
        contrasena is { Length: >= 10 and <= 128 } && contrasena.Any(char.IsLetter) && contrasena.Any(char.IsDigit);
}
