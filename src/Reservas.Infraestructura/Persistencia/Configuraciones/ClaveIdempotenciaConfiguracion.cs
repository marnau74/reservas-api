using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Reservas.Infraestructura.Persistencia.Configuraciones;

internal sealed class ClaveIdempotenciaConfiguracion : IEntityTypeConfiguration<ClaveIdempotenciaEntidad>
{
    public void Configure(EntityTypeBuilder<ClaveIdempotenciaEntidad> builder)
    {
        builder.ToTable("claves_idempotencia");
        builder.HasKey(c => c.Clave);

        builder.Property(c => c.Clave).HasMaxLength(64);
        builder.Property(c => c.HuellaPeticion).HasMaxLength(64).IsRequired();
        builder.Property(c => c.Completada).IsRequired();
        builder.Property(c => c.TipoContenido).HasMaxLength(100);
        builder.Property(c => c.Ubicacion).HasMaxLength(300);
        builder.Property(c => c.CreadaEn).IsRequired();
        builder.Property(c => c.ActualizadaEn).IsRequired();

        // Para purgar las claves antiguas sin recorrer toda la tabla.
        builder.HasIndex(c => c.ActualizadaEn);
    }
}
