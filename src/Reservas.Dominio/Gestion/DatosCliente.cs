using System.Net.Mail;

using Reservas.Dominio.Comun;

namespace Reservas.Dominio.Gestion;

/// <summary>
/// Lo mínimo que se guarda de un cliente: nombre, correo y, opcionalmente, teléfono.
/// Datos mínimos por diseño (RGPD): nada que no se necesite para atender la reserva.
/// </summary>
public sealed record DatosCliente
{
    private DatosCliente(string nombre, string email, string? telefono)
    {
        Nombre = nombre;
        Email = email;
        Telefono = telefono;
    }

    /// <summary>Lo que queda de un cliente cuyos datos se han borrado (RGPD): la reserva se conserva, la persona no.</summary>
    public static readonly DatosCliente Anonimo = new("Cliente anonimizado", "anonimizado@invalid", null);

    public bool EstaAnonimizado => Email == Anonimo.Email;

    public string Nombre { get; }

    public string Email { get; }

    public string? Telefono { get; }

    public static Resultado<DatosCliente> Crear(string nombre, string email, string? telefono = null)
    {
        var nombreLimpio = nombre?.Trim() ?? string.Empty;
        var emailLimpio = email?.Trim() ?? string.Empty;
        var telefonoLimpio = string.IsNullOrWhiteSpace(telefono) ? null : telefono.Trim();

        var valido =
            nombreLimpio.Length is >= 1 and <= 100
            && EsCorreoValido(emailLimpio)
            && (telefonoLimpio is null || telefonoLimpio.Length <= 30);

        return valido
            ? Resultado.Exito(new DatosCliente(nombreLimpio, emailLimpio, telefonoLimpio))
            : Resultado.Fallo<DatosCliente>(ErroresReserva.ClienteInvalido);
    }

    private static bool EsCorreoValido(string email) =>
        email.Length is > 0 and <= 254
        && MailAddress.TryCreate(email, out var direccion)
        && direccion.Address == email;
}
