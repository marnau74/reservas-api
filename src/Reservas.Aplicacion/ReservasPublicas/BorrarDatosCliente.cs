using Reservas.Aplicacion.Abstracciones;
using Reservas.Dominio.Comun;

namespace Reservas.Aplicacion.ReservasPublicas;

/// <summary>
/// Derecho de supresión (RGPD): el cliente pide con su código que se borren sus datos personales. La
/// reserva se conserva para las cuentas del negocio, pero sin nombre, correo ni teléfono. No se
/// permite mientras la reserva siga activa: no se puede borrar el contacto de quien va a venir.
/// </summary>
public sealed class BorrarDatosCliente(IRepositorioReservas reservas)
{
    public async Task<Resultado> EjecutarAsync(string codigo, CancellationToken cancellationToken)
    {
        var reserva = await reservas.ObtenerPorCodigoAsync(codigo, cancellationToken);
        if (reserva is null)
        {
            return Resultado.Fallo(ErroresAplicacion.ReservaNoEncontrada);
        }

        // Repetirlo es inofensivo: quien ya está anonimizado sigue estándolo.
        if (reserva.Cliente.EstaAnonimizado)
        {
            return Resultado.Exito();
        }

        var anonimizada = reserva.Anonimizar();

        return anonimizada.EsFallo ? anonimizada : await reservas.ActualizarAsync(reserva, [], cancellationToken);
    }
}
