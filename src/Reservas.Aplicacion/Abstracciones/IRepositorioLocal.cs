using Reservas.Dominio.Comun;
using Reservas.Dominio.Locales;

namespace Reservas.Aplicacion.Abstracciones;

/// <summary>Un valor del dominio que no lleva identidad propia, junto con la que le da la base de datos.</summary>
public sealed record Registrado<T>(Guid Id, T Valor);

/// <summary>
/// Salas, mesas, horarios y cierres de un negocio, para que su encargado los configure. Todo se
/// lee y escribe dentro de un negocio: un identificador de otro negocio simplemente «no existe».
/// </summary>
public interface IRepositorioLocal
{
    Task<IReadOnlyList<Sala>> ListarSalasAsync(Guid negocioId, CancellationToken cancellationToken);

    Task<bool> ExisteSalaAsync(Guid negocioId, Guid salaId, CancellationToken cancellationToken);

    Task AgregarSalaAsync(Guid negocioId, Sala sala, CancellationToken cancellationToken);

    /// <summary>Borra la sala, o devuelve <c>sala.no_encontrada</c> o <c>sala.con_mesas</c>.</summary>
    Task<Resultado> EliminarSalaAsync(Guid negocioId, Guid salaId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Mesa>> ListarMesasAsync(Guid negocioId, CancellationToken cancellationToken);

    Task AgregarMesaAsync(Guid negocioId, Mesa mesa, CancellationToken cancellationToken);

    /// <summary>Borra la mesa, o devuelve <c>mesa.no_encontrada</c> o <c>mesa.con_reservas</c>.</summary>
    Task<Resultado> EliminarMesaAsync(Guid negocioId, Guid mesaId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Registrado<Horario>>> ListarHorariosAsync(Guid negocioId, CancellationToken cancellationToken);

    Task<Guid> AgregarHorarioAsync(Guid negocioId, Horario horario, CancellationToken cancellationToken);

    Task<Resultado> EliminarHorarioAsync(Guid negocioId, Guid horarioId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Registrado<Cierre>>> ListarCierresAsync(Guid negocioId, CancellationToken cancellationToken);

    Task<Guid> AgregarCierreAsync(Guid negocioId, Cierre cierre, CancellationToken cancellationToken);

    Task<Resultado> EliminarCierreAsync(Guid negocioId, Guid cierreId, CancellationToken cancellationToken);
}
