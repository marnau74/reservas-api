using Reservas.Dominio.Comun;

namespace Reservas.Dominio.Locales;

public static class ErroresLocal
{
    public static readonly ErrorDominio MesaInvalida =
        new("local.mesa_invalida", "La mesa necesita nombre y una capacidad mínima y máxima entre 1 y 30, con la mínima no mayor que la máxima.");

    public static readonly ErrorDominio HorarioInvalido =
        new("local.horario_invalido", "El horario necesita una hora de cierre no anterior a la de apertura y un intervalo entre 5 y 120 minutos.");
}
