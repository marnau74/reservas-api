using Reservas.Dominio.Locales;
using Reservas.Dominio.Negocios;

namespace Reservas.Dominio.Disponibilidad;

/// <summary>Todo lo que hace falta para calcular la disponibilidad de un negocio.</summary>
/// <param name="Negocio">El negocio, con su zona horaria y sus políticas.</param>
/// <param name="Mesas">Todas las mesas del local.</param>
/// <param name="Horarios">Horarios semanales de reservas.</param>
/// <param name="Cierres">Días u horas en los que no se abre.</param>
/// <param name="Ocupaciones">Mesas ya ocupadas por reservas activas.</param>
public sealed record DatosDisponibilidad(
    Negocio Negocio,
    IReadOnlyCollection<Mesa> Mesas,
    IReadOnlyCollection<Horario> Horarios,
    IReadOnlyCollection<Cierre> Cierres,
    IReadOnlyCollection<OcupacionMesa> Ocupaciones);
