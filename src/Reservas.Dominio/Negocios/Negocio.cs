using System.Text.RegularExpressions;

using Reservas.Dominio.Comun;

namespace Reservas.Dominio.Negocios;

/// <summary>Un bar o restaurante que gestiona sus reservas en la plataforma.</summary>
public sealed partial class Negocio
{
    // Constructor para que EF Core reconstruya el objeto desde la base de datos: las
    // propiedades se rellenan después con los valores guardados.
    private Negocio()
    {
        Slug = null!;
        Nombre = null!;
        Zona = null!;
        Politicas = null!;
    }

    private Negocio(Guid id, string slug, string nombre, ZonaHorariaNegocio zona, PoliticasReserva politicas)
    {
        Id = id;
        Slug = slug;
        Nombre = nombre;
        Zona = zona;
        Politicas = politicas;
    }

    public Guid Id { get; }

    /// <summary>Identificador en la URL pública: <c>/negocios/bar-la-plaza</c>.</summary>
    public string Slug { get; }

    public string Nombre { get; }

    public ZonaHorariaNegocio Zona { get; }

    public PoliticasReserva Politicas { get; }

    public static Resultado<Negocio> Crear(string slug, string nombre, string idZonaHoraria, PoliticasReserva politicas)
    {
        ArgumentNullException.ThrowIfNull(politicas);

        if (string.IsNullOrEmpty(slug) || slug.Length is < 3 or > 40 || !PatronSlug().IsMatch(slug))
        {
            return Resultado.Fallo<Negocio>(ErroresNegocio.SlugInvalido);
        }

        if (string.IsNullOrWhiteSpace(nombre) || nombre.Trim().Length > 100)
        {
            return Resultado.Fallo<Negocio>(ErroresNegocio.NombreInvalido);
        }

        var zona = ZonaHorariaNegocio.Crear(idZonaHoraria);

        return zona.EsFallo
            ? Resultado.Fallo<Negocio>(zona.Error)
            : Resultado.Exito(new Negocio(Guid.NewGuid(), slug, nombre.Trim(), zona.Valor, politicas with { })); // copia: no se comparte con otro negocio
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex PatronSlug();
}
