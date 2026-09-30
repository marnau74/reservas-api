using Reservas.Dominio.Comun;

namespace Reservas.Dominio.Disponibilidad;

/// <summary>Una mesa bloqueada durante un tramo de tiempo por una reserva que sigue activa.</summary>
public sealed record OcupacionMesa(Guid MesaId, IntervaloTiempo Intervalo);
