using MailKit.Net.Smtp;
using MailKit.Security;

using Microsoft.Extensions.Logging;

using MimeKit;

using Reservas.Aplicacion.Abstracciones;

namespace Reservas.Infraestructura.Correo;

/// <summary>Ajustes del servidor de correo, en la sección <c>Correo</c> de la configuración.</summary>
public sealed class OpcionesSmtp
{
    public const string Seccion = "Correo";

    /// <summary>Servidor SMTP. Vacío: no se envía nada de verdad y los correos solo se anotan en el registro.</summary>
    public string? Servidor { get; set; }

    public int Puerto { get; set; } = 1025;

    /// <summary>Dirección desde la que se envía.</summary>
    public string Remitente { get; set; } = "reservas@localhost";

    public string NombreRemitente { get; set; } = "Reservas";

    /// <summary>Cifrar la conexión (STARTTLS o TLS directo según el puerto). Imprescindible con un servidor real.</summary>
    public bool UsarTls { get; set; }

    public string? Usuario { get; set; }

    /// <summary>Contraseña del servidor de correo: se da por variable de entorno o gestor de secretos, nunca en un fichero del repositorio.</summary>
    public string? Contrasena { get; set; }
}

/// <summary>Envía correos de texto plano por SMTP con MailKit.</summary>
public sealed class EnviadorSmtp(OpcionesSmtp opciones) : IEnviadorCorreo
{
    public async Task EnviarAsync(MensajeCorreo mensaje, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mensaje);

        var correo = new MimeMessage();
        correo.From.Add(new MailboxAddress(opciones.NombreRemitente, opciones.Remitente));
        correo.To.Add(MailboxAddress.Parse(mensaje.Destinatario));
        correo.Subject = mensaje.Asunto;
        correo.Body = new TextPart("plain") { Text = mensaje.Cuerpo };

        using var cliente = new SmtpClient();

        await cliente.ConnectAsync(
            opciones.Servidor ?? throw new InvalidOperationException("Falta Correo:Servidor."),
            opciones.Puerto,
            opciones.UsarTls ? SecureSocketOptions.StartTlsWhenAvailable : SecureSocketOptions.None,
            cancellationToken);

        if (!string.IsNullOrEmpty(opciones.Usuario))
        {
            await cliente.AuthenticateAsync(opciones.Usuario, opciones.Contrasena ?? string.Empty, cancellationToken);
        }

        await cliente.SendAsync(correo, cancellationToken);
        await cliente.DisconnectAsync(quit: true, cancellationToken);
    }
}

/// <summary>Sustituto cuando no hay servidor de correo configurado: solo lo anota, sin el contenido, que lleva enlaces secretos.</summary>
public sealed partial class EnviadorCorreoRegistro(ILogger<EnviadorCorreoRegistro> registro) : IEnviadorCorreo
{
    public Task EnviarAsync(MensajeCorreo mensaje, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mensaje);

        CorreoSinServidor(registro, mensaje.Asunto);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Correo «{Asunto}» no enviado: no hay servidor de correo configurado (sección Correo).")]
    private static partial void CorreoSinServidor(ILogger registro, string asunto);
}
