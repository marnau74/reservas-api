namespace Reservas.Dominio.Comun;

/// <summary>
/// Tramo de tiempo real (en UTC) semiabierto: incluye el inicio y excluye el fin. Así, una
/// reserva que acaba a las 21:00 no choca con la siguiente que empieza a las 21:00.
/// </summary>
public readonly record struct IntervaloTiempo
{
    public IntervaloTiempo(DateTimeOffset inicio, DateTimeOffset fin)
    {
        if (fin <= inicio)
        {
            throw new ArgumentException("El fin debe ser posterior al inicio.", nameof(fin));
        }

        Inicio = inicio.ToUniversalTime();
        Fin = fin.ToUniversalTime();
    }

    public DateTimeOffset Inicio { get; }

    public DateTimeOffset Fin { get; }

    public TimeSpan Duracion => Fin - Inicio;

    public bool Solapa(IntervaloTiempo otro) => Inicio < otro.Fin && otro.Inicio < Fin;
}
