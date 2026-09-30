using Reservas.Dominio.Comun;

namespace Reservas.Dominio.Negocios;

/// <summary>Reglas de cada negocio para las reservas que hace el público.</summary>
public sealed record PoliticasReserva
{
    private PoliticasReserva(
        TimeSpan antelacionMinima,
        int diasMaximosAntelacion,
        int maxComensalesOnline,
        TimeSpan duracionEstandar)
    {
        AntelacionMinima = antelacionMinima;
        DiasMaximosAntelacion = diasMaximosAntelacion;
        MaxComensalesOnline = maxComensalesOnline;
        DuracionEstandar = duracionEstandar;
    }

    /// <summary>Con cuánto tiempo mínimo se puede reservar («hasta 1 hora antes»).</summary>
    public TimeSpan AntelacionMinima { get; }

    /// <summary>Con cuántos días como máximo se puede reservar.</summary>
    public int DiasMaximosAntelacion { get; }

    /// <summary>Comensales máximos en una reserva online; para más, se llama al local.</summary>
    public int MaxComensalesOnline { get; }

    /// <summary>Cuánto tiempo ocupa una mesa cada reserva.</summary>
    public TimeSpan DuracionEstandar { get; }

    public static PoliticasReserva PorDefecto { get; } =
        new(TimeSpan.FromHours(1), 60, 10, TimeSpan.FromMinutes(90));

    public static Resultado<PoliticasReserva> Crear(
        TimeSpan antelacionMinima,
        int diasMaximosAntelacion,
        int maxComensalesOnline,
        TimeSpan duracionEstandar)
    {
        var validas =
            antelacionMinima >= TimeSpan.Zero && antelacionMinima <= TimeSpan.FromDays(7)
            && diasMaximosAntelacion is >= 1 and <= 365
            && maxComensalesOnline is >= 1 and <= 50
            && duracionEstandar >= TimeSpan.FromMinutes(15) && duracionEstandar <= TimeSpan.FromHours(6);

        return validas
            ? Resultado.Exito(new PoliticasReserva(antelacionMinima, diasMaximosAntelacion, maxComensalesOnline, duracionEstandar))
            : Resultado.Fallo<PoliticasReserva>(ErroresNegocio.PoliticasInvalidas);
    }
}
