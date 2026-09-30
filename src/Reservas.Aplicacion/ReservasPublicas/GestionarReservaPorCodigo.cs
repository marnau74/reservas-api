using Reservas.Aplicacion.Abstracciones;
using Reservas.Aplicacion.Correos;
using Reservas.Dominio.Comun;
using Reservas.Dominio.Correos;
using Reservas.Dominio.Gestion;
using Reservas.Dominio.Negocios;

namespace Reservas.Aplicacion.ReservasPublicas;

/// <summary>Una reserva junto con su negocio: hace falta para mostrar la hora en la zona horaria del local.</summary>
public sealed record ReservaConNegocio(Reserva Reserva, Negocio Negocio);

/// <summary>Consulta una reserva con el código secreto de su enlace.</summary>
public sealed class ConsultarReserva(IRepositorioReservas reservas, IRepositorioNegocios negocios)
{
    public async Task<Resultado<ReservaConNegocio>> EjecutarAsync(string codigo, CancellationToken cancellationToken)
    {
        var reserva = await reservas.ObtenerPorCodigoAsync(codigo, cancellationToken);

        return reserva is null
            ? Resultado.Fallo<ReservaConNegocio>(ErroresAplicacion.ReservaNoEncontrada)
            : await Componer.ConNegocioAsync(reserva, negocios, cancellationToken);
    }
}

/// <summary>Confirma una reserva pendiente con el código del enlace del correo.</summary>
public sealed class ConfirmarReserva(IRepositorioReservas reservas, IRepositorioNegocios negocios, OpcionesCorreo opcionesCorreo, TimeProvider reloj)
{
    public async Task<Resultado<ReservaConNegocio>> EjecutarAsync(string codigo, CancellationToken cancellationToken)
    {
        var reserva = await reservas.ObtenerPorCodigoAsync(codigo, cancellationToken);
        if (reserva is null)
        {
            return Resultado.Fallo<ReservaConNegocio>(ErroresAplicacion.ReservaNoEncontrada);
        }

        return await Componer.TrasCambioAsync(
            reserva, reserva.Confirmar(reloj.GetUtcNow()), reservas, negocios, TipoCorreo.Confirmacion, opcionesCorreo, reloj.GetUtcNow(), cancellationToken);
    }
}

/// <summary>Cancela una reserva con el código del enlace, liberando sus mesas.</summary>
public sealed class CancelarReserva(IRepositorioReservas reservas, IRepositorioNegocios negocios, OpcionesCorreo opcionesCorreo, TimeProvider reloj)
{
    public async Task<Resultado<ReservaConNegocio>> EjecutarAsync(string codigo, CancellationToken cancellationToken)
    {
        var reserva = await reservas.ObtenerPorCodigoAsync(codigo, cancellationToken);
        if (reserva is null)
        {
            return Resultado.Fallo<ReservaConNegocio>(ErroresAplicacion.ReservaNoEncontrada);
        }

        return await Componer.TrasCambioAsync(
            reserva, reserva.Cancelar(), reservas, negocios, TipoCorreo.Cancelacion, opcionesCorreo, reloj.GetUtcNow(), cancellationToken);
    }
}

internal static class Componer
{
    public static async Task<Resultado<ReservaConNegocio>> ConNegocioAsync(
        Reserva reserva,
        IRepositorioNegocios negocios,
        CancellationToken cancellationToken)
    {
        var negocio = await negocios.ObtenerAsync(reserva.NegocioId, cancellationToken);

        return negocio is null
            ? Resultado.Fallo<ReservaConNegocio>(ErroresAplicacion.NegocioNoEncontrado)
            : Resultado.Exito(new ReservaConNegocio(reserva, negocio));
    }

    /// <summary>Guarda el cambio de estado si la transición de dominio fue válida y devuelve la reserva resultante.</summary>
    /// <summary>
    /// Guarda un cambio de estado ya aplicado a la reserva, junto con el correo que corresponda (si
    /// lo hay) en la misma transacción.
    /// </summary>
    public static async Task<Resultado<ReservaConNegocio>> TrasCambioAsync(
        Reserva reserva,
        Resultado transicion,
        IRepositorioReservas reservas,
        IRepositorioNegocios negocios,
        TipoCorreo? correo,
        OpcionesCorreo opcionesCorreo,
        DateTimeOffset ahora,
        CancellationToken cancellationToken)
    {
        if (transicion.EsFallo)
        {
            return Resultado.Fallo<ReservaConNegocio>(transicion.Error);
        }

        var completa = await ConNegocioAsync(reserva, negocios, cancellationToken);
        if (completa.EsFallo)
        {
            return completa;
        }

        CorreoPendiente[] correos = correo is { } tipo
            ? [PlantillasCorreo.Crear(tipo, reserva, completa.Valor.Negocio, opcionesCorreo, ahora)]
            : [];

        var guardado = await reservas.ActualizarAsync(reserva, correos, cancellationToken);

        return guardado.EsFallo ? Resultado.Fallo<ReservaConNegocio>(guardado.Error) : completa;
    }
}
