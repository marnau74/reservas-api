using Reservas.Dominio.Comun;

namespace Reservas.Dominio.Locales;

/// <summary>Una mesa de una sala, con el rango de comensales que admite.</summary>
public sealed class Mesa
{
    private Mesa(Guid id, Guid salaId, string nombre, int capacidadMinima, int capacidadMaxima, bool esCombinable)
    {
        Id = id;
        SalaId = salaId;
        Nombre = nombre;
        CapacidadMinima = capacidadMinima;
        CapacidadMaxima = capacidadMaxima;
        EsCombinable = esCombinable;
    }

    public Guid Id { get; }

    public Guid SalaId { get; }

    public string Nombre { get; }

    /// <summary>Menos comensales que esto no compensa ocupar la mesa.</summary>
    public int CapacidadMinima { get; }

    public int CapacidadMaxima { get; }

    /// <summary>Se puede juntar con otras mesas combinables de su sala para grupos grandes.</summary>
    public bool EsCombinable { get; }

    public static Resultado<Mesa> Crear(Guid salaId, string nombre, int capacidadMinima, int capacidadMaxima, bool esCombinable)
    {
        var valida =
            !string.IsNullOrWhiteSpace(nombre)
            && capacidadMinima is >= 1 and <= 30
            && capacidadMaxima is >= 1 and <= 30
            && capacidadMinima <= capacidadMaxima;

        return valida
            ? Resultado.Exito(new Mesa(Guid.NewGuid(), salaId, nombre.Trim(), capacidadMinima, capacidadMaxima, esCombinable))
            : Resultado.Fallo<Mesa>(ErroresLocal.MesaInvalida);
    }

    /// <summary>¿Cabe un grupo de este tamaño, sin combinar con otras mesas?</summary>
    public bool Admite(int comensales) => comensales >= CapacidadMinima && comensales <= CapacidadMaxima;
}
