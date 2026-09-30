using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Reservas.Dominio.Gestion;
using Reservas.Dominio.Locales;
using Reservas.Dominio.Negocios;

namespace Reservas.Infraestructura.Persistencia.Configuraciones;

internal sealed class ReservaConfiguracion : IEntityTypeConfiguration<Reserva>
{
    public void Configure(EntityTypeBuilder<Reserva> builder)
    {
        builder.ToTable("reservas");
        builder.HasKey(r => r.Id);

        builder.HasOne<Negocio>().WithMany().HasForeignKey(r => r.NegocioId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(r => r.NegocioId);

        // El tramo de tiempo, como un rango de PostgreSQL: [inicio, fin).
        builder.Property(r => r.Intervalo)
            .HasConversion(new ConversorIntervalo())
            .HasColumnType("tstzrange")
            .HasColumnName("periodo")
            .IsRequired();

        builder.Property(r => r.Comensales).IsRequired();

        // El estado se guarda con su nombre: se lee en la base de datos sin tabla de códigos.
        builder.Property(r => r.Estado).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(r => r.CodigoGestion).HasMaxLength(22).IsRequired();
        builder.HasIndex(r => r.CodigoGestion).IsUnique();

        builder.Property(r => r.CreadaEn).IsRequired();

        builder.OwnsOne(r => r.Cliente, cliente =>
        {
            cliente.Property(c => c.Nombre).HasMaxLength(100).IsRequired();
            cliente.Property(c => c.Email).HasMaxLength(254).IsRequired();
            cliente.Property(c => c.Telefono).HasMaxLength(30);
        });
        builder.Navigation(r => r.Cliente).IsRequired();

        // Las mesas son una lista de identificadores (uuid[]) que EF lee y escribe a través
        // del campo privado del dominio.
        builder.Property<Guid[]>("_mesaIds").HasColumnName("mesa_ids").IsRequired();

        // Concurrencia optimista: PostgreSQL cambia xmin en cada modificación de la fila, y EF
        // lo incluye en el UPDATE. Si otra transacción la modificó antes, el UPDATE no toca
        // ninguna fila y se sabe que la reserva ya no es la que se leyó.
        builder.Property<uint>("xmin")
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();
    }
}

internal sealed class OcupacionMesaConfiguracion : IEntityTypeConfiguration<OcupacionMesaEntidad>
{
    public void Configure(EntityTypeBuilder<OcupacionMesaEntidad> builder)
    {
        builder.ToTable("ocupaciones_mesa");
        builder.HasKey(o => new { o.ReservaId, o.MesaId });

        builder.Property(o => o.NegocioId).IsRequired();
        builder.Property(o => o.Periodo).HasColumnType("tstzrange").IsRequired();

        builder.HasOne<Reserva>().WithMany().HasForeignKey(o => o.ReservaId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Mesa>().WithMany().HasForeignKey(o => o.MesaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(o => o.NegocioId);

        // LA GARANTÍA CENTRAL DEL PROYECTO: dos reservas activas no pueden ocupar la misma mesa
        // en tramos de tiempo que se solapen. La comprueba PostgreSQL al escribir, así que
        // sigue siendo cierta con cualquier número de peticiones simultáneas, algo que una
        // comprobación previa en el código («¿está libre?») no puede asegurar.
        //
        // EF Core no sabe expresar una restricción de exclusión, así que se crea en la migración
        // «RestriccionExclusionMesas» con SQL propio:
        //   EXCLUDE USING gist (mesa_id WITH =, periodo WITH &&) WHERE (activa)
        //   mesa_id WITH =   → la misma mesa
        //   periodo WITH &&  → tramos que se solapan
        //   WHERE (activa)   → las reservas canceladas o terminadas no bloquean nada
    }
}
