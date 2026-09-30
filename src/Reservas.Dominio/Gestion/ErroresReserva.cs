using Reservas.Dominio.Comun;

namespace Reservas.Dominio.Gestion;

/// <summary>Errores de negocio de las reservas. Sus códigos son estables: los ve el cliente de la API.</summary>
public static class ErroresReserva
{
    public static readonly ErrorDominio ComensalesInvalidos =
        new("reserva.comensales_invalidos", "El número de comensales debe ser mayor que cero.");

    public static readonly ErrorDominio DemasiadosComensalesOnline =
        new("reserva.demasiados_comensales_online", "Para grupos tan grandes hay que llamar al local.");

    public static readonly ErrorDominio DemasiadoLejos =
        new("reserva.demasiado_lejos", "Todavía no se admiten reservas para esa fecha.");

    public static readonly ErrorDominio ClienteInvalido =
        new("reserva.cliente_invalido", "El cliente necesita un nombre y un correo electrónico válidos.");

    public static readonly ErrorDominio SinMesa =
        new("reserva.sin_mesa", "Una reserva necesita al menos una mesa.");

    public static readonly ErrorDominio TransicionInvalida =
        new("reserva.transicion_invalida", "La reserva no está en un estado que permita esta operación.");

    public static readonly ErrorDominio Caducada =
        new("reserva.caducada", "La reserva pendiente ha caducado por no confirmarse a tiempo.");

    public static readonly ErrorDominio AunNoEsHora =
        new("reserva.aun_no_es_hora", "Todavía no ha pasado el margen para dar la reserva por no presentada.");

    public static readonly ErrorDominio NoCaducaAun =
        new("reserva.no_caduca_aun", "La reserva todavía está dentro del plazo para confirmarse.");

    public static readonly ErrorDominio MesaOcupada =
        new("reserva.mesa_ocupada", "Alguna de las mesas ya está reservada a esa hora.");

    public static readonly ErrorDominio ConflictoConcurrencia =
        new("reserva.conflicto_concurrencia", "La reserva ha cambiado mientras se procesaba esta operación. Inténtalo de nuevo.");
}
