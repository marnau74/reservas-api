using Reservas.Dominio.Comun;
using Reservas.Dominio.Locales;

namespace Reservas.Dominio.Disponibilidad;

/// <summary>
/// Decide qué mesas ocupa un grupo en un tramo de tiempo, entre las que están libres.
/// Criterio: primero una sola mesa, la más ajustada al grupo (para no gastar una mesa de
/// ocho en una pareja); si ninguna cabe, se combinan mesas combinables de una misma sala,
/// con las menos mesas posibles.
/// </summary>
public static class AsignadorMesas
{
    private const int MaximoMesasCombinadas = 4;

    /// <summary>Identificadores de las mesas asignadas, o una lista vacía si no hay sitio.</summary>
    public static IReadOnlyList<Guid> Asignar(
        int comensales,
        IReadOnlyCollection<Mesa> mesas,
        IReadOnlyCollection<OcupacionMesa> ocupaciones,
        IntervaloTiempo intervalo)
    {
        ArgumentNullException.ThrowIfNull(mesas);
        ArgumentNullException.ThrowIfNull(ocupaciones);

        var ocupadas = ocupaciones
            .Where(o => o.Intervalo.Solapa(intervalo))
            .Select(o => o.MesaId)
            .ToHashSet();

        var libres = mesas.Where(m => !ocupadas.Contains(m.Id)).ToList();

        var individual = libres
            .Where(m => m.Admite(comensales))
            .OrderBy(m => m.CapacidadMaxima)
            .ThenBy(m => m.Id)
            .FirstOrDefault();

        if (individual is not null)
        {
            return [individual.Id];
        }

        var combinacion = libres
            .Where(m => m.EsCombinable)
            .GroupBy(m => m.SalaId)
            .Select(sala => Combinar(sala, comensales))
            .Where(candidata => candidata.Count > 0)
            .OrderBy(candidata => candidata.Count)
            .ThenBy(candidata => candidata.Sum(m => m.CapacidadMaxima))
            .ThenBy(candidata => candidata[0].SalaId)
            .FirstOrDefault();

        return combinacion is null ? [] : [.. combinacion.Select(m => m.Id)];
    }

    /// <summary>Las mesas más grandes de la sala hasta cubrir el grupo, o vacío si no llegan.</summary>
    private static List<Mesa> Combinar(IEnumerable<Mesa> mesasDeLaSala, int comensales)
    {
        var elegidas = new List<Mesa>();
        var capacidad = 0;

        foreach (var mesa in mesasDeLaSala.OrderByDescending(m => m.CapacidadMaxima).ThenBy(m => m.Id).Take(MaximoMesasCombinadas))
        {
            elegidas.Add(mesa);
            capacidad += mesa.CapacidadMaxima;

            if (capacidad >= comensales)
            {
                return elegidas;
            }
        }

        return [];
    }
}
