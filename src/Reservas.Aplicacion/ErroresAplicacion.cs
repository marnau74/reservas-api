using Reservas.Dominio.Comun;

namespace Reservas.Aplicacion;

/// <summary>Errores que surgen al ejecutar los casos de uso, no de una regla del dominio.</summary>
public static class ErroresAplicacion
{
    public static readonly ErrorDominio NegocioNoEncontrado =
        new("negocio.no_encontrado", "No existe ningún negocio con ese identificador.");

    public static readonly ErrorDominio ReservaNoEncontrada =
        new("reserva.no_encontrada", "No existe ninguna reserva con ese código.");

    public static readonly ErrorDominio FranjaNoDisponible =
        new("reserva.franja_no_disponible", "Ya no hay mesa disponible a esa hora para ese número de comensales.");

    // --- Sesión del personal ---

    /// <summary>El mismo error para un correo desconocido, una contraseña incorrecta y una cuenta bloqueada: no revela cuál es.</summary>
    public static readonly ErrorDominio CredencialesInvalidas =
        new("auth.credenciales_invalidas", "El correo o la contraseña no son correctos.");

    public static readonly ErrorDominio TokenInvalido =
        new("auth.token_invalido", "La sesión no es válida o ha caducado. Inicia sesión de nuevo.");

    // --- Usuarios ---

    public static readonly ErrorDominio UsuarioNoEncontrado =
        new("usuario.no_encontrado", "No existe ningún usuario con ese identificador.");

    public static readonly ErrorDominio EmailEnUso =
        new("usuario.email_en_uso", "Ya hay un usuario con ese correo electrónico.");

    public static readonly ErrorDominio ContrasenaDebil =
        new("usuario.contrasena_debil", "La contraseña debe tener al menos 10 caracteres, con letras y números.");

    public static readonly ErrorDominio RolNoPermitido =
        new("usuario.rol_no_permitido", "Solo se pueden crear usuarios con rol de personal o de encargado.");

    public static readonly ErrorDominio UsuarioProtegido =
        new("usuario.protegido", "No se puede desactivar a un propietario ni a uno mismo.");

    // --- Local ---

    public static readonly ErrorDominio SalaNoEncontrada =
        new("sala.no_encontrada", "No existe ninguna sala con ese identificador.");

    public static readonly ErrorDominio SalaConMesas =
        new("sala.con_mesas", "La sala todavía tiene mesas. Borra las mesas antes.");

    public static readonly ErrorDominio MesaNoEncontrada =
        new("mesa.no_encontrada", "No existe ninguna mesa con ese identificador.");

    public static readonly ErrorDominio MesaConReservas =
        new("mesa.con_reservas", "La mesa tiene reservas, pasadas o futuras, y no puede borrarse.");

    public static readonly ErrorDominio HorarioNoEncontrado =
        new("horario.no_encontrado", "No existe ningún horario con ese identificador.");

    public static readonly ErrorDominio CierreNoEncontrado =
        new("cierre.no_encontrado", "No existe ningún cierre con ese identificador.");
}
