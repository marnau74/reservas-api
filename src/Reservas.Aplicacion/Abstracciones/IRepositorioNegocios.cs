using Reservas.Dominio.Locales;
using Reservas.Dominio.Negocios;

namespace Reservas.Aplicacion.Abstracciones;

/// <summary>Lectura de los negocios y de la configuración de su local.</summary>
public interface IRepositorioNegocios
{
    /// <summary>Busca un negocio por el identificador de su URL pública, o <c>null</c> si no existe.</summary>
    Task<Negocio?> ObtenerPorSlugAsync(string slug, CancellationToken cancellationToken);

    /// <summary>Busca un negocio por su identificador, o <c>null</c> si no existe.</summary>
    Task<Negocio?> ObtenerAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Mesas, horarios y cierres de un negocio.</summary>
    Task<ConfiguracionLocal> ObtenerConfiguracionAsync(Guid negocioId, CancellationToken cancellationToken);
}

/// <summary>Lo que define cómo se puede reservar en un local.</summary>
public sealed record ConfiguracionLocal(
    IReadOnlyList<Mesa> Mesas,
    IReadOnlyList<Horario> Horarios,
    IReadOnlyList<Cierre> Cierres);
