using Reservas.Dominio.Comun;
using Reservas.Dominio.Correos;
using Reservas.Dominio.Disponibilidad;
using Reservas.Dominio.Gestion;

namespace Reservas.Aplicacion.Abstracciones;

/// <summary>
/// Lo que los casos de uso necesitan de la persistencia de reservas. La aplicación define
/// esta interfaz y la infraestructura la implementa, de modo que el negocio no sabe si los
/// datos están en PostgreSQL, en memoria o en otra parte.
/// </summary>
/// <remarks>
/// Cada instancia es una unidad de trabajo: una reserva obtenida con
/// <see cref="ObtenerAsync"/> se modifica con sus métodos de dominio y se guarda con
/// <see cref="ActualizarAsync"/> en la misma instancia.
/// </remarks>
public interface IRepositorioReservas
{
    /// <summary>
    /// Guarda una reserva nueva reservando sus mesas. Si otra reserva activa ya ocupa alguna a
    /// esa hora, no guarda nada y devuelve <c>reserva.mesa_ocupada</c>: lo garantiza la base de
    /// datos, no una comprobación previa, así que es correcto incluso con peticiones simultáneas.
    /// Los <paramref name="correos"/> se guardan en la misma transacción: o hay reserva y correos, o nada.
    /// </summary>
    Task<Resultado> AgregarAsync(Reserva reserva, IReadOnlyCollection<CorreoPendiente> correos, CancellationToken cancellationToken);

    /// <summary>Busca una reserva por su identificador, o <c>null</c> si no existe.</summary>
    Task<Reserva?> ObtenerAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Busca una reserva por el código secreto de su enlace, o <c>null</c> si no existe.</summary>
    Task<Reserva?> ObtenerPorCodigoAsync(string codigoGestion, CancellationToken cancellationToken);

    /// <summary>
    /// Guarda los cambios de una reserva obtenida con <see cref="ObtenerAsync"/>. Si alguien la
    /// modificó entretanto, no guarda nada y devuelve <c>reserva.conflicto_concurrencia</c>. Los
    /// <paramref name="correos"/> se guardan en la misma transacción que el cambio.
    /// </summary>
    Task<Resultado> ActualizarAsync(Reserva reserva, IReadOnlyCollection<CorreoPendiente> correos, CancellationToken cancellationToken);

    /// <summary>Todas las reservas de un negocio cuyo inicio cae en el tramo, por orden de hora (sin seguimiento: solo lectura).</summary>
    Task<IReadOnlyList<Reserva>> ListarPorInicioAsync(Guid negocioId, IntervaloTiempo tramo, CancellationToken cancellationToken);

    /// <summary>Mesas ocupadas por reservas activas de un negocio en un tramo de tiempo.</summary>
    Task<IReadOnlyList<OcupacionMesa>> ObtenerOcupacionesAsync(
        Guid negocioId,
        IntervaloTiempo ventana,
        CancellationToken cancellationToken);

    // --- Tareas programadas: sin sesión, sobre todos los negocios. Devuelven reservas con seguimiento. ---

    /// <summary>Reservas pendientes cuyo plazo de confirmación ya pasó.</summary>
    Task<IReadOnlyList<Reserva>> ListarPendientesCaducadasAsync(DateTimeOffset ahora, int maximo, CancellationToken cancellationToken);

    /// <summary>Reservas confirmadas que empiezan dentro del tramo y todavía no tienen recordatorio programado.</summary>
    Task<IReadOnlyList<Reserva>> ListarParaRecordatorioAsync(IntervaloTiempo tramo, int maximo, CancellationToken cancellationToken);

    /// <summary>Reservas ya no activas, creadas antes de la fecha, con los datos del cliente todavía sin borrar.</summary>
    Task<IReadOnlyList<Reserva>> ListarAnonimizablesAsync(DateTimeOffset creadasAntesDe, int maximo, CancellationToken cancellationToken);
}
