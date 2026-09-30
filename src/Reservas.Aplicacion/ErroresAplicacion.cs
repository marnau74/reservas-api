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
}
