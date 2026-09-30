using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Reservas.Dominio.Locales;
using Reservas.Dominio.Negocios;

namespace Reservas.Infraestructura.Persistencia.Configuraciones;

// Las salas, mesas, horarios y cierres pertenecen a un negocio. En el dominio no llevan esa
// referencia (el negocio es su contexto, no un dato suyo), pero en la base de datos todas las
// tablas tienen negocio_id: es la columna por la que se aísla cada negocio de los demás.
// Se declara como propiedad en la sombra de EF.

internal sealed class SalaConfiguracion : IEntityTypeConfiguration<Sala>
{
    public void Configure(EntityTypeBuilder<Sala> builder)
    {
        builder.ToTable("salas");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Nombre).HasMaxLength(100).IsRequired();

        builder.Property<Guid>(ConstantesPersistencia.NegocioId);
        builder.HasOne<Negocio>().WithMany().HasForeignKey(ConstantesPersistencia.NegocioId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(ConstantesPersistencia.NegocioId);
    }
}

internal sealed class MesaConfiguracion : IEntityTypeConfiguration<Mesa>
{
    public void Configure(EntityTypeBuilder<Mesa> builder)
    {
        builder.ToTable(
            "mesas",
            tabla => tabla.HasCheckConstraint(
                "ck_mesas_capacidad",
                "capacidad_minima >= 1 AND capacidad_minima <= capacidad_maxima AND capacidad_maxima <= 30"));
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Nombre).HasMaxLength(100).IsRequired();

        // El dominio es inmutable (propiedades solo con get): EF no las mapea por convención.
        builder.Property(m => m.CapacidadMinima).IsRequired();
        builder.Property(m => m.CapacidadMaxima).IsRequired();
        builder.Property(m => m.EsCombinable).IsRequired();

        builder.Property<Guid>(ConstantesPersistencia.NegocioId);
        builder.HasOne<Negocio>().WithMany().HasForeignKey(ConstantesPersistencia.NegocioId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Sala>().WithMany().HasForeignKey(m => m.SalaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(ConstantesPersistencia.NegocioId);
    }
}

internal sealed class HorarioConfiguracion : IEntityTypeConfiguration<Horario>
{
    public void Configure(EntityTypeBuilder<Horario> builder)
    {
        builder.ToTable(
            "horarios",
            tabla =>
            {
                tabla.HasCheckConstraint("ck_horarios_intervalo", "intervalo_minutos BETWEEN 5 AND 120");
                tabla.HasCheckConstraint("ck_horarios_fin", "fin >= inicio");
            });

        // El dominio no da identidad a un horario: la tiene solo la base de datos.
        builder.Property<Guid>("Id");
        builder.HasKey("Id");

        builder.Property(h => h.Dia).HasConversion<short>();
        builder.Property(h => h.Turno).HasConversion<short>();
        builder.Property(h => h.Inicio).IsRequired();
        builder.Property(h => h.Fin).IsRequired();
        builder.Property(h => h.IntervaloMinutos).IsRequired();

        builder.Property<Guid>(ConstantesPersistencia.NegocioId);
        builder.HasOne<Negocio>().WithMany().HasForeignKey(ConstantesPersistencia.NegocioId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(ConstantesPersistencia.NegocioId);
    }
}

internal sealed class CierreConfiguracion : IEntityTypeConfiguration<Cierre>
{
    public void Configure(EntityTypeBuilder<Cierre> builder)
    {
        builder.ToTable("cierres");

        builder.Property<Guid>("Id");
        builder.HasKey("Id");

        builder.Property(c => c.Turno).HasConversion<short?>();
        builder.Property(c => c.Motivo).HasMaxLength(200).IsRequired();

        builder.Property<Guid>(ConstantesPersistencia.NegocioId);
        builder.HasOne<Negocio>().WithMany().HasForeignKey(ConstantesPersistencia.NegocioId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(ConstantesPersistencia.NegocioId, nameof(Cierre.Fecha));
    }
}
