using Microsoft.EntityFrameworkCore;

using Reservas.Aplicacion.Abstracciones;
using Reservas.Dominio.Gestion;
using Reservas.Dominio.Locales;
using Reservas.Dominio.Negocios;
using Reservas.Dominio.Personal;

namespace Reservas.Infraestructura.Persistencia;

/// <param name="opciones">Configuración de EF Core.</param>
/// <param name="contexto">
/// El negocio de la petición. Si tiene uno, todas las consultas de datos de negocio se limitan a él
/// (ver <see cref="OnModelCreating"/>); sin contexto o sin negocio, no se filtra nada.
/// </param>
public sealed class ReservasDbContext(DbContextOptions<ReservasDbContext> opciones, IContextoNegocio? contexto = null) : DbContext(opciones)
{
    public DbSet<Negocio> Negocios => Set<Negocio>();

    public DbSet<Sala> Salas => Set<Sala>();

    public DbSet<Mesa> Mesas => Set<Mesa>();

    public DbSet<Horario> Horarios => Set<Horario>();

    public DbSet<Cierre> Cierres => Set<Cierre>();

    public DbSet<Reserva> Reservas => Set<Reserva>();

    public DbSet<OcupacionMesaEntidad> OcupacionesMesa => Set<OcupacionMesaEntidad>();

    public DbSet<ClaveIdempotenciaEntidad> ClavesIdempotencia => Set<ClaveIdempotenciaEntidad>();

    public DbSet<Usuario> Usuarios => Set<Usuario>();

    public DbSet<TokenRefresco> TokensRefresco => Set<TokenRefresco>();

    /// <summary>Se lee en cada consulta (EF lo trata como un parámetro), no al crear el modelo.</summary>
    private Guid? NegocioActual => contexto?.NegocioId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // Necesaria para que una restricción de exclusión pueda combinar la igualdad de un
        // identificador (uuid) con el solapamiento de un rango de tiempo.
        modelBuilder.HasPostgresExtension("btree_gist");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ReservasDbContext).Assembly);

        // AISLAMIENTO ENTRE NEGOCIOS. Cada tabla con datos de un negocio tiene un filtro global:
        // cuando la petición viene del personal de un negocio, EF añade «negocio_id = ese negocio»
        // a TODAS las consultas, también a las que se olviden de pedirlo. Un identificador de otro
        // negocio no da error de permisos: sencillamente no existe (404), sin revelar que está ahí.
        modelBuilder.Entity<Sala>().HasQueryFilter(e => NegocioActual == null || EF.Property<Guid>(e, ConstantesPersistencia.NegocioId) == NegocioActual);
        modelBuilder.Entity<Mesa>().HasQueryFilter(e => NegocioActual == null || EF.Property<Guid>(e, ConstantesPersistencia.NegocioId) == NegocioActual);
        modelBuilder.Entity<Horario>().HasQueryFilter(e => NegocioActual == null || EF.Property<Guid>(e, ConstantesPersistencia.NegocioId) == NegocioActual);
        modelBuilder.Entity<Cierre>().HasQueryFilter(e => NegocioActual == null || EF.Property<Guid>(e, ConstantesPersistencia.NegocioId) == NegocioActual);
        modelBuilder.Entity<Reserva>().HasQueryFilter(e => NegocioActual == null || e.NegocioId == NegocioActual);
        modelBuilder.Entity<OcupacionMesaEntidad>().HasQueryFilter(e => NegocioActual == null || e.NegocioId == NegocioActual);
        modelBuilder.Entity<Usuario>().HasQueryFilter(e => NegocioActual == null || e.NegocioId == NegocioActual);

        // Sin filtro, a propósito: «negocios» es público (se busca por su slug), los tokens de
        // renovación se buscan por su huella antes de saber de quién son, y las claves de
        // idempotencia las gestiona el middleware sin sesión.
    }
}
