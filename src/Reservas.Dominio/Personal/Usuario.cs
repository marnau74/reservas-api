using System.Net.Mail;

using Reservas.Dominio.Comun;

namespace Reservas.Dominio.Personal;

/// <summary>
/// Una persona que trabaja en un negocio y entra en la parte privada de la API. Un usuario
/// pertenece a un único negocio, y de ahí sale a qué datos tiene acceso.
/// </summary>
/// <remarks>
/// La contraseña nunca se guarda: solo su huella (<see cref="HashContrasena"/>). Comprobarla y
/// generarla es cosa de la infraestructura; el dominio decide qué pasa cuando alguien falla.
/// </remarks>
public sealed class Usuario
{
    /// <summary>Fallos seguidos que bloquean la cuenta.</summary>
    public const int MaximoIntentosFallidos = 5;

    /// <summary>Cuánto dura el bloqueo. Frena la fuerza bruta sin dejar a nadie fuera para siempre.</summary>
    public static readonly TimeSpan DuracionBloqueo = TimeSpan.FromMinutes(15);

    // Constructor para que EF Core reconstruya el usuario desde la base de datos.
    private Usuario()
    {
        Email = null!;
        Nombre = null!;
        HashContrasena = null!;
    }

    private Usuario(Guid negocioId, string email, string nombre, Rol rol, string hashContrasena, DateTimeOffset creadoEn)
    {
        Id = Guid.NewGuid();
        NegocioId = negocioId;
        Email = email;
        Nombre = nombre;
        Rol = rol;
        HashContrasena = hashContrasena;
        Activo = true;
        CreadoEn = creadoEn.ToUniversalTime();
    }

    public Guid Id { get; }

    public Guid NegocioId { get; }

    /// <summary>Correo en minúsculas: es el nombre de usuario y no puede repetirse en ningún negocio.</summary>
    public string Email { get; }

    public string Nombre { get; }

    public Rol Rol { get; }

    public string HashContrasena { get; }

    /// <summary>Un usuario desactivado ya no puede entrar, pero se conserva: sigue constando quién hizo qué.</summary>
    public bool Activo { get; private set; }

    public int IntentosFallidos { get; private set; }

    public DateTimeOffset? BloqueadoHasta { get; private set; }

    public DateTimeOffset CreadoEn { get; }

    public static string NormalizarEmail(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    public static Resultado<Usuario> Crear(
        Guid negocioId,
        string email,
        string nombre,
        Rol rol,
        string hashContrasena,
        DateTimeOffset ahora)
    {
        var correo = NormalizarEmail(email);
        var nombreLimpio = nombre?.Trim() ?? string.Empty;

        var valido =
            negocioId != Guid.Empty
            && EsCorreoValido(correo)
            && nombreLimpio.Length is >= 1 and <= 100
            && Enum.IsDefined(rol)
            && !string.IsNullOrEmpty(hashContrasena);

        return valido
            ? Resultado.Exito(new Usuario(negocioId, correo, nombreLimpio, rol, hashContrasena, ahora))
            : Resultado.Fallo<Usuario>(ErroresPersonal.UsuarioInvalido);
    }

    /// <summary>¿Puede intentar entrar ahora? Un usuario desactivado o bloqueado, no.</summary>
    public bool PuedeIniciarSesion(DateTimeOffset ahora) => Activo && !EstaBloqueado(ahora);

    public bool EstaBloqueado(DateTimeOffset ahora) => BloqueadoHasta is { } hasta && ahora < hasta;

    /// <summary>Anota una contraseña incorrecta; al llegar al máximo, bloquea la cuenta un rato.</summary>
    public void RegistrarFallo(DateTimeOffset ahora)
    {
        IntentosFallidos++;

        if (IntentosFallidos >= MaximoIntentosFallidos)
        {
            BloqueadoHasta = (ahora + DuracionBloqueo).ToUniversalTime();
            IntentosFallidos = 0;
        }
    }

    /// <summary>Una entrada correcta borra los fallos anteriores.</summary>
    public void RegistrarAcceso()
    {
        IntentosFallidos = 0;
        BloqueadoHasta = null;
    }

    public Resultado Desactivar()
    {
        if (!Activo)
        {
            return Resultado.Fallo(ErroresPersonal.UsuarioYaDesactivado);
        }

        Activo = false;
        return Resultado.Exito();
    }

    private static bool EsCorreoValido(string correo) =>
        correo.Length is > 0 and <= 254
        && MailAddress.TryCreate(correo, out var direccion)
        && direccion.Address == correo;
}
