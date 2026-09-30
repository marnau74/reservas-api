using System.Security.Cryptography;
using System.Text;

namespace Reservas.Aplicacion.Personal;

internal static class TokensRefresco
{
    /// <summary>256 bits aleatorios en base64 seguro para URL: imposible de adivinar.</summary>
    public static string Generar() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>SHA-256 en hexadecimal. Basta una huella rápida: el token ya es aleatorio y de alta entropía, no una contraseña.</summary>
    public static string Hashear(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
