using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Reservas.Dominio.Correos;
using Reservas.Dominio.Gestion;
using Reservas.Dominio.Negocios;

namespace Reservas.Infraestructura.Persistencia.Configuraciones;

internal sealed class CorreoPendienteConfiguracion : IEntityTypeConfiguration<CorreoPendiente>
{
    public void Configure(EntityTypeBuilder<CorreoPendiente> builder)
    {
        builder.ToTable("correos_pendientes");
        builder.HasKey(c => c.Id);

        // El dominio es inmutable en estas propiedades (solo get): EF no las mapea por convención.
        builder.Property(c => c.NegocioId).IsRequired();
        builder.Property(c => c.ReservaId);
        builder.Property(c => c.Tipo).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(c => c.Destinatario).HasMaxLength(254).IsRequired();
        builder.Property(c => c.Asunto).HasMaxLength(200).IsRequired();
        builder.Property(c => c.Cuerpo).IsRequired();
        builder.Property(c => c.CreadoEn).IsRequired();
        builder.Property(c => c.UltimoError).HasMaxLength(500);

        builder.HasOne<Negocio>().WithMany().HasForeignKey(c => c.NegocioId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Reserva>().WithMany().HasForeignKey(c => c.ReservaId).OnDelete(DeleteBehavior.Cascade);

        // El proceso de envío solo mira los correos que quedan por enviar: un índice parcial los
        // encuentra sin recorrer el histórico de todos los ya enviados.
        builder.HasIndex(c => c.ProximoIntentoEn)
            .HasFilter("enviado_en IS NULL AND abandonado = false");

        builder.HasIndex(c => c.ReservaId);
    }
}
