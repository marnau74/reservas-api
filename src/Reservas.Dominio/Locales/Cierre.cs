namespace Reservas.Dominio.Locales;

/// <summary>
/// Un día en el que el negocio no abre: festivo, vacaciones, reforma. Sin turno, cierra todo
/// el día; con turno, solo esa parte (por ejemplo, la cena del día de Nochebuena).
/// </summary>
/// <param name="Fecha">Día local del cierre.</param>
/// <param name="Turno">Turno que cierra, o <c>null</c> para todo el día.</param>
/// <param name="Motivo">Explicación para el personal.</param>
public sealed record Cierre(DateOnly Fecha, Turno? Turno, string Motivo)
{
    public bool Cubre(DateOnly fecha, Turno turno) => Fecha == fecha && (Turno is null || Turno == turno);
}
