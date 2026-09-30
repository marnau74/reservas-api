using Reservas.Dominio.Comun;

namespace Reservas.Dominio.Locales;

/// <summary>
/// Franja en la que se admiten reservas un día de la semana y un turno. <see cref="Inicio"/>
/// es la primera hora a la que se puede sentar un grupo y <see cref="Fin"/> la última
/// (la cocina cierra después de la última reserva, no antes). Las horas son locales del negocio.
/// </summary>
public sealed class Horario
{
    private Horario(DayOfWeek dia, Turno turno, TimeOnly inicio, TimeOnly fin, int intervaloMinutos)
    {
        Dia = dia;
        Turno = turno;
        Inicio = inicio;
        Fin = fin;
        IntervaloMinutos = intervaloMinutos;
    }

    public DayOfWeek Dia { get; }

    public Turno Turno { get; }

    public TimeOnly Inicio { get; }

    public TimeOnly Fin { get; }

    /// <summary>Cada cuántos minutos se ofrece una franja (por ejemplo, cada 15).</summary>
    public int IntervaloMinutos { get; }

    public static Resultado<Horario> Crear(DayOfWeek dia, Turno turno, TimeOnly inicio, TimeOnly fin, int intervaloMinutos)
    {
        var valido =
            Enum.IsDefined(dia) && Enum.IsDefined(turno)
            && fin >= inicio
            && intervaloMinutos is >= 5 and <= 120;

        return valido
            ? Resultado.Exito(new Horario(dia, turno, inicio, fin, intervaloMinutos))
            : Resultado.Fallo<Horario>(ErroresLocal.HorarioInvalido);
    }

    /// <summary>Las horas locales a las que se ofrece una franja, de la primera a la última.</summary>
    public IEnumerable<TimeOnly> HorasOfrecidas()
    {
        var ultima = (Fin.Hour * 60) + Fin.Minute;

        for (var minuto = (Inicio.Hour * 60) + Inicio.Minute; minuto <= ultima; minuto += IntervaloMinutos)
        {
            yield return new TimeOnly(minuto / 60, minuto % 60);
        }
    }
}
