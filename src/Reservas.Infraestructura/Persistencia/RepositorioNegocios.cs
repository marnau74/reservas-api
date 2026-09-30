using Microsoft.EntityFrameworkCore;

using Reservas.Aplicacion.Abstracciones;
using Reservas.Dominio.Negocios;

namespace Reservas.Infraestructura.Persistencia;

/// <summary>Lectura de negocios y de la configuración de su local. Solo consulta: nada de lo que devuelve se modifica.</summary>
public sealed class RepositorioNegocios(ReservasDbContext db) : IRepositorioNegocios
{
    public Task<Negocio?> ObtenerPorSlugAsync(string slug, CancellationToken cancellationToken) =>
        db.Negocios.AsNoTracking().FirstOrDefaultAsync(negocio => negocio.Slug == slug, cancellationToken);

    public Task<Negocio?> ObtenerAsync(Guid id, CancellationToken cancellationToken) =>
        db.Negocios.AsNoTracking().FirstOrDefaultAsync(negocio => negocio.Id == id, cancellationToken);

    public async Task<ConfiguracionLocal> ObtenerConfiguracionAsync(Guid negocioId, CancellationToken cancellationToken)
    {
        // negocio_id no es una propiedad del dominio: se filtra por la propiedad en la sombra del modelo.
        var mesas = await db.Mesas.AsNoTracking()
            .Where(mesa => EF.Property<Guid>(mesa, ConstantesPersistencia.NegocioId) == negocioId)
            .ToListAsync(cancellationToken);

        var horarios = await db.Horarios.AsNoTracking()
            .Where(horario => EF.Property<Guid>(horario, ConstantesPersistencia.NegocioId) == negocioId)
            .ToListAsync(cancellationToken);

        var cierres = await db.Cierres.AsNoTracking()
            .Where(cierre => EF.Property<Guid>(cierre, ConstantesPersistencia.NegocioId) == negocioId)
            .ToListAsync(cancellationToken);

        return new ConfiguracionLocal(mesas, horarios, cierres);
    }
}
