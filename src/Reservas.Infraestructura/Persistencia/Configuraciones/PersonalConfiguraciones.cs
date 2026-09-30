using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Reservas.Dominio.Negocios;
using Reservas.Dominio.Personal;

namespace Reservas.Infraestructura.Persistencia.Configuraciones;

internal sealed class UsuarioConfiguracion : IEntityTypeConfiguration<Usuario>
{
    public void Configure(EntityTypeBuilder<Usuario> builder)
    {
        builder.ToTable("usuarios");
        builder.HasKey(u => u.Id);

        builder.HasOne<Negocio>().WithMany().HasForeignKey(u => u.NegocioId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(u => u.NegocioId);

        // El dominio es inmutable en estas propiedades (solo get): EF no las mapea por convención.
        builder.Property(u => u.Email).HasMaxLength(254).IsRequired();
        builder.Property(u => u.Nombre).HasMaxLength(100).IsRequired();
        builder.Property(u => u.Rol).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(u => u.HashContrasena).HasMaxLength(200).IsRequired();
        builder.Property(u => u.CreadoEn).IsRequired();

        // El correo es el nombre de usuario: único en toda la base de datos, no solo en un negocio.
        // La comprobación definitiva la hace PostgreSQL, así que dos altas simultáneas no pueden duplicarlo.
        builder.HasIndex(u => u.Email).IsUnique();
    }
}

internal sealed class TokenRefrescoConfiguracion : IEntityTypeConfiguration<TokenRefresco>
{
    public void Configure(EntityTypeBuilder<TokenRefresco> builder)
    {
        builder.ToTable("tokens_refresco");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.UsuarioId).IsRequired();
        builder.Property(t => t.HashToken).HasMaxLength(64).IsRequired();
        builder.Property(t => t.CreadoEn).IsRequired();
        builder.Property(t => t.ExpiraEn).IsRequired();

        builder.HasOne<Usuario>().WithMany().HasForeignKey(t => t.UsuarioId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(t => t.HashToken).IsUnique();
        builder.HasIndex(t => t.UsuarioId);

        // La consulta de mantenimiento que borra los tokens caducados recorre este índice.
        builder.HasIndex(t => t.ExpiraEn);
    }
}
