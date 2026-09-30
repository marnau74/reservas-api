using Microsoft.EntityFrameworkCore;

using Reservas.Dominio.Gestion;
using Reservas.Dominio.Locales;
using Reservas.Dominio.Negocios;

namespace Reservas.Infraestructura.Persistencia;

public sealed class ReservasDbContext(DbContextOptions<ReservasDbContext> opciones) : DbContext(opciones)
{
    public DbSet<Negocio> Negocios => Set<Negocio>();

    public DbSet<Sala> Salas => Set<Sala>();

    public DbSet<Mesa> Mesas => Set<Mesa>();

    public DbSet<Horario> Horarios => Set<Horario>();

    public DbSet<Cierre> Cierres => Set<Cierre>();

    public DbSet<Reserva> Reservas => Set<Reserva>();

    public DbSet<OcupacionMesaEntidad> OcupacionesMesa => Set<OcupacionMesaEntidad>();

    public DbSet<ClaveIdempotenciaEntidad> ClavesIdempotencia => Set<ClaveIdempotenciaEntidad>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // Necesaria para que una restricción de exclusión pueda combinar la igualdad de un
        // identificador (uuid) con el solapamiento de un rango de tiempo.
        modelBuilder.HasPostgresExtension("btree_gist");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ReservasDbContext).Assembly);
    }
}
