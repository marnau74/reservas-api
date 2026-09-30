using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Reservas.Infraestructura.Persistencia;

/// <summary>
/// Solo la usan las herramientas de EF (<c>dotnet ef migrations add</c>) para crear el
/// contexto sin arrancar la aplicación. No se conecta a ninguna base de datos: para generar una
/// migración basta con conocer el modelo.
/// </summary>
internal sealed class FabricaReservasDbContext : IDesignTimeDbContextFactory<ReservasDbContext>
{
    public ReservasDbContext CreateDbContext(string[] args)
    {
        var opciones = new DbContextOptionsBuilder<ReservasDbContext>().UseNpgsql("Host=localhost;Database=reservas_diseno");
        OpcionesReservas.Configurar(opciones);

        return new ReservasDbContext(opciones.Options);
    }
}
