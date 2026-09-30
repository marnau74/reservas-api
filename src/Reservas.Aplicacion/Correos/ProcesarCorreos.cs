using Reservas.Aplicacion.Abstracciones;

namespace Reservas.Aplicacion.Correos;

/// <param name="Enviados">Correos enviados en esta pasada.</param>
/// <param name="Fallidos">Correos que fallaron y quedan para reintentar.</param>
/// <param name="Abandonados">Correos que agotaron sus intentos y ya no se envían.</param>
public sealed record ResumenEnvio(int Enviados, int Fallidos, int Abandonados);

/// <summary>
/// Vacía la bandeja de salida: envía los correos que ya toca. Cada fallo se anota y programa un
/// reintento más tarde; un correo que falla no bloquea a los demás ni detiene el proceso.
/// </summary>
public sealed class ProcesarCorreos(IBandejaCorreos bandeja, IEnviadorCorreo enviador, TimeProvider reloj)
{
    /// <summary>Cuántos correos se toman en cada pasada.</summary>
    public const int TamanoLote = 20;

    /// <summary>
    /// Tiempo que un correo tomado queda reservado para este proceso. Debe ser mayor que lo que tarda
    /// enviar un lote; si el proceso muere, otro lo recoge pasado este plazo.
    /// </summary>
    public static readonly TimeSpan Reserva = TimeSpan.FromMinutes(5);

    public async Task<ResumenEnvio> EjecutarAsync(CancellationToken cancellationToken)
    {
        var enviados = 0;
        var fallidos = 0;
        var abandonados = 0;

        var lote = await bandeja.ReclamarAsync(reloj.GetUtcNow(), Reserva, TamanoLote, cancellationToken);

        foreach (var correo in lote)
        {
            try
            {
                await enviador.EnviarAsync(new MensajeCorreo(correo.Destinatario, correo.Asunto, correo.Cuerpo), cancellationToken);
                correo.MarcarEnviado(reloj.GetUtcNow());
                enviados++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Se está apagando la aplicación: no es un fallo del correo. Queda reservado y se recogerá después.
                break;
            }
#pragma warning disable CA1031 // Cualquier fallo del envío (red, servidor, dirección) se anota y se reintenta.
            catch (Exception excepcion)
#pragma warning restore CA1031
            {
                correo.RegistrarFallo($"{excepcion.GetType().Name}: {excepcion.Message}", reloj.GetUtcNow());

                if (correo.Abandonado)
                {
                    abandonados++;
                }
                else
                {
                    fallidos++;
                }
            }

            // Se guarda tras cada correo: si el proceso muere a mitad, no se reenvía el que ya salió.
            await bandeja.GuardarAsync(cancellationToken);
        }

        return new ResumenEnvio(enviados, fallidos, abandonados);
    }
}
