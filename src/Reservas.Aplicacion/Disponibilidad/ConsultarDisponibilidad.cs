using Reservas.Aplicacion.Abstracciones;
using Reservas.Dominio.Comun;
using Reservas.Dominio.Disponibilidad;
using Reservas.Dominio.Gestion;
using Reservas.Dominio.Negocios;

namespace Reservas.Aplicacion.Disponibilidad;

/// <param name="Slug">Identificador público del negocio.</param>
/// <param name="Fecha">Día local del negocio.</param>
/// <param name="Comensales">Tamaño del grupo.</param>
public sealed record SolicitudDisponibilidad(string Slug, DateOnly Fecha, int Comensales);

public sealed record DisponibilidadDia(Negocio Negocio, DateOnly Fecha, int Comensales, IReadOnlyList<Franja> Franjas);

/// <summary>Qué horas se pueden reservar un día para un grupo, tal como las ve el público.</summary>
public sealed class ConsultarDisponibilidad(IRepositorioNegocios negocios, ServicioDisponibilidad disponibilidad, TimeProvider reloj)
{
    public async Task<Resultado<DisponibilidadDia>> EjecutarAsync(SolicitudDisponibilidad solicitud, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(solicitud);

        var negocio = await negocios.ObtenerPorSlugAsync(solicitud.Slug, cancellationToken);
        if (negocio is null)
        {
            return Resultado.Fallo<DisponibilidadDia>(ErroresAplicacion.NegocioNoEncontrado);
        }

        var franjas = await disponibilidad.CalcularAsync(
            negocio, solicitud.Fecha, solicitud.Comensales, OrigenReserva.Publica, reloj.GetUtcNow(), cancellationToken);

        return franjas.EsFallo
            ? Resultado.Fallo<DisponibilidadDia>(franjas.Error)
            : Resultado.Exito(new DisponibilidadDia(negocio, solicitud.Fecha, solicitud.Comensales, franjas.Valor));
    }
}
