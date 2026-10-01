namespace Reservas.Infraestructura.Persistencia;

/// <summary>Una petición ya vista, identificada por la clave <c>Idempotency-Key</c> del cliente.</summary>
public sealed class ClaveIdempotenciaEntidad
{
    public ClaveIdempotenciaEntidad(string clave, string huellaPeticion, DateTimeOffset creadaEn)
    {
        Clave = clave;
        HuellaPeticion = huellaPeticion;
        CreadaEn = creadaEn;
        ActualizadaEn = creadaEn;
    }

    public string Clave { get; }

    /// <summary>SHA-256 de la petición (método, ruta y cuerpo): detecta que se reutiliza la clave para otra cosa.</summary>
    public string HuellaPeticion { get; }

    public bool Completada { get; set; }

    public int? EstadoHttp { get; set; }

    public string? TipoContenido { get; set; }

    public string? Cuerpo { get; set; }

    public string? Ubicacion { get; set; }

    /// <summary>La reserva a la que se refiere la respuesta guardada: al borrar los datos del cliente se borra también esta copia.</summary>
    public Guid? ReservaId { get; set; }

    public DateTimeOffset CreadaEn { get; }

    /// <summary>Última vez que se tocó: una petición en curso que lleva demasiado sin cambios se da por abandonada.</summary>
    public DateTimeOffset ActualizadaEn { get; set; }
}
