namespace Reservas.Aplicacion.Abstracciones;

/// <summary>
/// Guarda qué peticiones se han procesado ya, identificadas por la clave que envía el cliente
/// en <c>Idempotency-Key</c>. Permite reintentar una petición con seguridad: si el cliente no
/// recibió la respuesta (se cortó la conexión), la repite con la misma clave y obtiene la misma
/// respuesta en lugar de crear una segunda reserva.
/// </summary>
public interface IAlmacenIdempotencia
{
    /// <summary>
    /// Intenta reservar la clave para procesar una petición. Solo una petición con una clave
    /// obtiene <see cref="EstadoAdquisicion.Adquirida"/>; las demás ven qué pasó con la primera.
    /// </summary>
    Task<ResultadoAdquisicion> AdquirirAsync(string clave, string huellaPeticion, DateTimeOffset ahora, CancellationToken cancellationToken);

    /// <summary>Guarda la respuesta que se dio, para repetirla si llega la misma petición.</summary>
    Task CompletarAsync(string clave, RespuestaGuardada respuesta, DateTimeOffset ahora, CancellationToken cancellationToken);

    /// <summary>Libera la clave sin guardar respuesta (la petición falló): el cliente podrá reintentarla.</summary>
    Task LiberarAsync(string clave, CancellationToken cancellationToken);
}

public enum EstadoAdquisicion
{
    /// <summary>Primera vez que se ve esta clave: hay que procesar la petición.</summary>
    Adquirida = 1,

    /// <summary>La petición ya se procesó: hay que devolver la respuesta guardada.</summary>
    Repetida = 2,

    /// <summary>Otra petición con la misma clave se está procesando ahora mismo.</summary>
    EnCurso = 3,

    /// <summary>La clave se usó antes para una petición distinta: es un error del cliente.</summary>
    HuellaDistinta = 4,
}

public sealed record ResultadoAdquisicion(EstadoAdquisicion Estado, RespuestaGuardada? Respuesta = null);

/// <summary>Lo necesario para reproducir una respuesta HTTP.</summary>
/// <param name="EstadoHttp">Código de estado (201, 409…).</param>
/// <param name="TipoContenido">Tipo de contenido, o <c>null</c> si no hay cuerpo.</param>
/// <param name="Cuerpo">Cuerpo de la respuesta.</param>
/// <param name="Ubicacion">Cabecera <c>Location</c>, si la había.</param>
/// <param name="ReservaId">
/// La reserva a la que se refiere la respuesta, si se refiere a una. La respuesta lleva datos del cliente (y el código de
/// gestión): cuando se borran los datos de esa reserva, se borra también esta copia.
/// </param>
public sealed record RespuestaGuardada(int EstadoHttp, string? TipoContenido, string Cuerpo, string? Ubicacion, Guid? ReservaId = null);
