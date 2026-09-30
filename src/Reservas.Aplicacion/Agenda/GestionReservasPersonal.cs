using Reservas.Aplicacion.Abstracciones;
using Reservas.Aplicacion.Disponibilidad;
using Reservas.Aplicacion.ReservasPublicas;
using Reservas.Dominio.Comun;
using Reservas.Dominio.Gestion;
using Reservas.Dominio.Locales;
using Reservas.Dominio.Negocios;

namespace Reservas.Aplicacion.Agenda;

/// <summary>Las reservas de un día de un negocio, con sus mesas, tal como las ve el personal.</summary>
public sealed record AgendaDia(Negocio Negocio, DateOnly Fecha, IReadOnlyList<Reserva> Reservas, IReadOnlyList<Mesa> Mesas);

/// <param name="Fecha">Día local de la reserva.</param>
/// <param name="Hora">Hora local: una de las que ofrece la disponibilidad.</param>
public sealed record SolicitudReservaPersonal(DateOnly Fecha, TimeOnly Hora, int Comensales, string Nombre, string Email, string? Telefono);

/// <summary>
/// Lo que hace el personal con las reservas de su negocio: ver la agenda, apuntar una reserva por
/// teléfono o en persona y llevarla por sus estados (llega, se sienta, termina o no aparece).
/// Solo ve las reservas del negocio que indica la sesión; las de otros «no existen».
/// </summary>
public sealed class GestionReservasPersonal(
    IRepositorioNegocios negocios,
    IRepositorioReservas reservas,
    ServicioDisponibilidad disponibilidad,
    TimeProvider reloj)
{
    public async Task<Resultado<AgendaDia>> ObtenerAgendaAsync(Guid negocioId, DateOnly fecha, CancellationToken cancellationToken)
    {
        var negocio = await negocios.ObtenerAsync(negocioId, cancellationToken);
        if (negocio is null)
        {
            return Resultado.Fallo<AgendaDia>(ErroresAplicacion.NegocioNoEncontrado);
        }

        var tramo = new IntervaloTiempo(InicioDelDia(negocio, fecha), InicioDelDia(negocio, fecha.AddDays(1)));
        var lista = await reservas.ListarPorInicioAsync(negocioId, tramo, cancellationToken);
        var local = await negocios.ObtenerConfiguracionAsync(negocioId, cancellationToken);

        return Resultado.Exito(new AgendaDia(negocio, fecha, lista, local.Mesas));
    }

    /// <summary>Apunta una reserva hecha por el personal: nace confirmada y no le afectan los límites de las reservas por internet.</summary>
    public async Task<Resultado<ReservaConNegocio>> CrearAsync(
        Guid negocioId,
        SolicitudReservaPersonal solicitud,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(solicitud);

        var negocio = await negocios.ObtenerAsync(negocioId, cancellationToken);
        if (negocio is null)
        {
            return Resultado.Fallo<ReservaConNegocio>(ErroresAplicacion.NegocioNoEncontrado);
        }

        var cliente = DatosCliente.Crear(solicitud.Nombre, solicitud.Email, solicitud.Telefono);
        if (cliente.EsFallo)
        {
            return Resultado.Fallo<ReservaConNegocio>(cliente.Error);
        }

        return await new CreadorReservas(disponibilidad, reservas, reloj).CrearAsync(
            negocio, solicitud.Fecha, solicitud.Hora, solicitud.Comensales, cliente.Valor, OrigenReserva.Personal, cancellationToken);
    }

    public Task<Resultado<ReservaConNegocio>> CancelarAsync(Guid negocioId, Guid reservaId, CancellationToken cancellationToken) =>
        CambiarAsync(negocioId, reservaId, reserva => reserva.Cancelar(), cancellationToken);

    /// <summary>El grupo ha llegado y se sienta a la mesa.</summary>
    public Task<Resultado<ReservaConNegocio>> SentarAsync(Guid negocioId, Guid reservaId, CancellationToken cancellationToken) =>
        CambiarAsync(negocioId, reservaId, reserva => reserva.Sentar(), cancellationToken);

    /// <summary>El grupo ha terminado y la mesa queda libre.</summary>
    public Task<Resultado<ReservaConNegocio>> CompletarAsync(Guid negocioId, Guid reservaId, CancellationToken cancellationToken) =>
        CambiarAsync(negocioId, reservaId, reserva => reserva.Completar(), cancellationToken);

    /// <summary>El grupo no ha aparecido pasado el margen de cortesía: la mesa se libera.</summary>
    public Task<Resultado<ReservaConNegocio>> MarcarNoPresentadaAsync(Guid negocioId, Guid reservaId, CancellationToken cancellationToken) =>
        CambiarAsync(negocioId, reservaId, reserva => reserva.MarcarNoPresentada(reloj.GetUtcNow()), cancellationToken);

    private async Task<Resultado<ReservaConNegocio>> CambiarAsync(
        Guid negocioId,
        Guid reservaId,
        Func<Reserva, Resultado> transicion,
        CancellationToken cancellationToken)
    {
        var reserva = await reservas.ObtenerAsync(reservaId, cancellationToken);

        // Que sea del negocio se comprueba aquí y además lo garantiza el filtro de la persistencia:
        // cualquiera de las dos defensas basta para que otro negocio no vea ni toque esta reserva.
        if (reserva is null || reserva.NegocioId != negocioId)
        {
            return Resultado.Fallo<ReservaConNegocio>(ErroresAplicacion.ReservaNoEncontrada);
        }

        return await Componer.TrasCambioAsync(reserva, transicion(reserva), reservas, negocios, cancellationToken);
    }

    /// <summary>Instante UTC en que empieza un día local (si la medianoche no existe por un cambio de hora, la primera hora que sí).</summary>
    private static DateTimeOffset InicioDelDia(Negocio negocio, DateOnly fecha)
    {
        for (var hora = 0; hora < 4; hora++)
        {
            if (negocio.Zona.AUtc(fecha, new TimeOnly(hora, 0)) is { } inicio)
            {
                return inicio;
            }
        }

        throw new InvalidOperationException("La zona horaria no tiene ninguna hora válida al empezar el día.");
    }
}
