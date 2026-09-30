using Microsoft.EntityFrameworkCore;

namespace Reservas.Infraestructura.Persistencia;

/// <summary>
/// Opciones de EF Core comunes a todos los sitios que crean el contexto: la API, las
/// migraciones y los tests. Un único punto evita que se desincronicen (por ejemplo, con la
/// convención de nombres de las tablas).
/// </summary>
public static class OpcionesReservas
{
    public static DbContextOptionsBuilder Configurar(DbContextOptionsBuilder opciones)
    {
        ArgumentNullException.ThrowIfNull(opciones);

        return opciones.UseSnakeCaseNamingConvention();
    }
}
