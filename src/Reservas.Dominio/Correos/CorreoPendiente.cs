namespace Reservas.Dominio.Correos;

public enum TipoCorreo
{
    /// <summary>Reserva hecha por internet: pide al cliente que la confirme con su enlace.</summary>
    Solicitud = 1,

    Confirmacion = 2,

    Cancelacion = 3,

    /// <summary>Aviso el día antes de una reserva confirmada.</summary>
    Recordatorio = 4,
}

/// <summary>
/// Un correo que hay que enviar, guardado en la base de datos en la misma transacción que el
/// hecho que lo provoca (patrón «bandeja de salida»). Así no puede haber una reserva sin su correo
/// ni un correo de una reserva que no llegó a existir, aunque el servidor de correo esté caído.
/// Un proceso aparte lo envía, con reintentos espaciados.
/// </summary>
public sealed class CorreoPendiente
{
    /// <summary>Intentos fallidos tras los cuales se deja de insistir.</summary>
    public const int MaximoIntentos = 6;

    /// <summary>Espera antes de cada reintento: tras el primer fallo un minuto, tras el segundo cinco, etc.</summary>
    public static readonly TimeSpan[] Esperas =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(2),
        TimeSpan.FromHours(12),
    ];

    // Constructor para que EF Core reconstruya el correo desde la base de datos.
    private CorreoPendiente()
    {
        Destinatario = null!;
        Asunto = null!;
        Cuerpo = null!;
    }

    private CorreoPendiente(Guid negocioId, Guid? reservaId, TipoCorreo tipo, string destinatario, string asunto, string cuerpo, DateTimeOffset creadoEn)
    {
        Id = Guid.NewGuid();
        NegocioId = negocioId;
        ReservaId = reservaId;
        Tipo = tipo;
        Destinatario = destinatario;
        Asunto = asunto;
        Cuerpo = cuerpo;
        CreadoEn = creadoEn.ToUniversalTime();
        ProximoIntentoEn = CreadoEn;
    }

    public Guid Id { get; }

    public Guid NegocioId { get; }

    public Guid? ReservaId { get; }

    public TipoCorreo Tipo { get; }

    public string Destinatario { get; }

    public string Asunto { get; }

    public string Cuerpo { get; }

    public DateTimeOffset CreadoEn { get; }

    public int Intentos { get; private set; }

    /// <summary>Desde cuándo se puede (re)intentar el envío.</summary>
    public DateTimeOffset ProximoIntentoEn { get; private set; }

    public DateTimeOffset? EnviadoEn { get; private set; }

    /// <summary>Se agotaron los intentos: no se envía más y hay que mirarlo a mano.</summary>
    public bool Abandonado { get; private set; }

    public string? UltimoError { get; private set; }

    public bool EstaPendiente => EnviadoEn is null && !Abandonado;

    public static CorreoPendiente Crear(
        Guid negocioId,
        Guid? reservaId,
        TipoCorreo tipo,
        string destinatario,
        string asunto,
        string cuerpo,
        DateTimeOffset ahora)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinatario);
        ArgumentException.ThrowIfNullOrWhiteSpace(asunto);
        ArgumentException.ThrowIfNullOrWhiteSpace(cuerpo);

        return new CorreoPendiente(negocioId, reservaId, tipo, destinatario, asunto, cuerpo, ahora);
    }

    /// <summary>Reserva el correo para un proceso durante un rato, para que otro no lo envíe a la vez.</summary>
    public void Reservar(DateTimeOffset hasta) => ProximoIntentoEn = hasta.ToUniversalTime();

    public void MarcarEnviado(DateTimeOffset ahora)
    {
        EnviadoEn = ahora.ToUniversalTime();
        UltimoError = null;
    }

    /// <summary>Anota un fallo y programa el siguiente intento, o abandona el correo si ya se intentó demasiado.</summary>
    public void RegistrarFallo(string error, DateTimeOffset ahora)
    {
        Intentos++;
        UltimoError = error.Length > 500 ? error[..500] : error;

        if (Intentos >= MaximoIntentos)
        {
            Abandonado = true;
            return;
        }

        ProximoIntentoEn = (ahora + Esperas[Intentos - 1]).ToUniversalTime();
    }
}
