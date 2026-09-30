using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Reservas.Dominio.Negocios;

namespace Reservas.Infraestructura.Persistencia.Configuraciones;

internal sealed class NegocioConfiguracion : IEntityTypeConfiguration<Negocio>
{
    public void Configure(EntityTypeBuilder<Negocio> builder)
    {
        builder.ToTable("negocios");
        builder.HasKey(n => n.Id);

        builder.Property(n => n.Slug).HasMaxLength(40).IsRequired();
        builder.HasIndex(n => n.Slug).IsUnique();

        builder.Property(n => n.Nombre).HasMaxLength(100).IsRequired();

        // La zona horaria se guarda como su identificador («Europe/Madrid»).
        builder.Property(n => n.Zona)
            .HasConversion(zona => zona.Id, id => ZonaHorariaNegocio.Crear(id).Valor)
            .HasMaxLength(64)
            .IsRequired();

        // Las políticas viven en la misma fila que el negocio (columnas politicas_*).
        builder.OwnsOne(n => n.Politicas, politicas =>
        {
            politicas.Property(p => p.AntelacionMinima).IsRequired();
            politicas.Property(p => p.DiasMaximosAntelacion).IsRequired();
            politicas.Property(p => p.MaxComensalesOnline).IsRequired();
            politicas.Property(p => p.DuracionEstandar).IsRequired();
        });
        builder.Navigation(n => n.Politicas).IsRequired();
    }
}
