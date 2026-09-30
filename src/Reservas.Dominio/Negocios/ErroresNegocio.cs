using Reservas.Dominio.Comun;

namespace Reservas.Dominio.Negocios;

public static class ErroresNegocio
{
    public static readonly ErrorDominio ZonaHorariaInvalida =
        new("negocio.zona_horaria_invalida", "La zona horaria no existe.");

    public static readonly ErrorDominio SlugInvalido =
        new("negocio.slug_invalido", "El identificador debe tener entre 3 y 40 letras minúsculas, números o guiones.");

    public static readonly ErrorDominio NombreInvalido =
        new("negocio.nombre_invalido", "El nombre es obligatorio y no puede superar los 100 caracteres.");

    public static readonly ErrorDominio PoliticasInvalidas =
        new("negocio.politicas_invalidas", "Las políticas de reserva tienen valores fuera de rango.");
}
