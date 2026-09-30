namespace Reservas.Aplicacion.Abstracciones;

public sealed record MensajeCorreo(string Destinatario, string Asunto, string Cuerpo);

/// <summary>Envía un correo. Lanza una excepción si no puede, y el que lo llama decide reintentar.</summary>
public interface IEnviadorCorreo
{
    Task EnviarAsync(MensajeCorreo mensaje, CancellationToken cancellationToken);
}
