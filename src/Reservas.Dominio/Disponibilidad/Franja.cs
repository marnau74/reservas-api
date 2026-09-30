using Reservas.Dominio.Comun;
using Reservas.Dominio.Locales;

namespace Reservas.Dominio.Disponibilidad;

/// <summary>Una hora a la que se puede reservar, con las mesas que quedarían asignadas.</summary>
/// <param name="HoraLocal">Hora local del negocio (la que ve el cliente).</param>
/// <param name="Turno">Turno al que pertenece.</param>
/// <param name="Intervalo">Tramo real (UTC) que ocuparía la reserva.</param>
/// <param name="MesaIds">Mesas asignadas: una, o varias combinadas si el grupo es grande.</param>
public sealed record Franja(TimeOnly HoraLocal, Turno Turno, IntervaloTiempo Intervalo, IReadOnlyList<Guid> MesaIds);
