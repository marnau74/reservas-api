using Microsoft.AspNetCore.Identity;

using Reservas.Aplicacion.Abstracciones;

namespace Reservas.Infraestructura.Persistencia;

/// <summary>
/// Contraseñas con el <see cref="PasswordHasher{TUser}"/> de ASP.NET Core Identity: PBKDF2 con
/// sal aleatoria y muchas iteraciones, en un formato que lleva su propia versión y por tanto se
/// puede reforzar más adelante sin invalidar las contraseñas ya guardadas.
/// </summary>
public sealed class HasherContrasenas : IHasherContrasenas
{
    // El hasher no usa el usuario para nada; el tipo es solo un parámetro.
    private static readonly object Nadie = new();

    private readonly PasswordHasher<object> _hasher = new();
    private readonly Lazy<string> _hashFalso;

    public HasherContrasenas() => _hashFalso = new Lazy<string>(() => Hashear(Guid.NewGuid().ToString("N")));

    public string HashFalso => _hashFalso.Value;

    public string Hashear(string contrasena) => _hasher.HashPassword(Nadie, contrasena);

    public bool Verificar(string hash, string contrasena) =>
        _hasher.VerifyHashedPassword(Nadie, hash, contrasena) is not PasswordVerificationResult.Failed;
}
