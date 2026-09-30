using Reservas.Dominio.Comun;

namespace Reservas.Dominio.Personal;

public static class ErroresPersonal
{
    public static readonly ErrorDominio UsuarioInvalido =
        new("usuario.invalido", "El usuario necesita un correo válido, un nombre de hasta 100 caracteres y un rol.");

    public static readonly ErrorDominio UsuarioYaDesactivado =
        new("usuario.ya_desactivado", "Ese usuario ya estaba desactivado.");
}
