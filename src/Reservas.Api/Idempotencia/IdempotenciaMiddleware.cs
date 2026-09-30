using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using Reservas.Api.Errores;
using Reservas.Aplicacion.Abstracciones;

namespace Reservas.Api.Idempotencia;

/// <summary>Marca un endpoint como idempotente: exige la cabecera <c>Idempotency-Key</c> y repite su respuesta.</summary>
public sealed class RequiereIdempotencia;

/// <summary>
/// Hace idempotentes los endpoints marcados con <see cref="RequiereIdempotencia"/>. El cliente
/// envía una clave única por operación en <c>Idempotency-Key</c>:
/// <list type="bullet">
/// <item>La primera vez, la petición se procesa y su respuesta se guarda.</item>
/// <item>Si la misma petición llega otra vez (el cliente no recibió la respuesta y reintenta),
/// se devuelve la respuesta guardada y no se vuelve a ejecutar: no hay dos reservas.</item>
/// <item>Si la clave se reutiliza para una petición distinta, es un error del cliente (422).</item>
/// <item>Si llega mientras la primera aún se procesa, se rechaza con 409 para que reintente.</item>
/// </list>
/// Se guardan todas las respuestas salvo los errores 5xx: un fallo del servidor no debe quedar
/// fijado, el cliente tiene que poder reintentarlo.
/// </summary>
public sealed partial class IdempotenciaMiddleware(RequestDelegate siguiente)
{
    public const string Cabecera = "Idempotency-Key";
    public const string CabeceraRepetida = "Idempotency-Replayed";

    public async Task InvokeAsync(HttpContext contexto, IAlmacenIdempotencia almacen, TimeProvider reloj)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(almacen);
        ArgumentNullException.ThrowIfNull(reloj);

        if (contexto.GetEndpoint()?.Metadata.GetMetadata<RequiereIdempotencia>() is null)
        {
            await siguiente(contexto);
            return;
        }

        var clave = contexto.Request.Headers[Cabecera].ToString();

        if (clave.Length == 0)
        {
            await Rechazar(contexto, StatusCodes.Status400BadRequest, ErroresIdempotencia.ClaveRequerida);
            return;
        }

        if (!PatronClave().IsMatch(clave))
        {
            await Rechazar(contexto, StatusCodes.Status400BadRequest, ErroresIdempotencia.ClaveInvalida);
            return;
        }

        var huella = await CalcularHuellaAsync(contexto.Request);
        var adquisicion = await almacen.AdquirirAsync(clave, huella, reloj.GetUtcNow(), contexto.RequestAborted);

        switch (adquisicion.Estado)
        {
            case EstadoAdquisicion.Repetida:
                await Reproducir(contexto, adquisicion.Respuesta!);
                return;

            case EstadoAdquisicion.EnCurso:
                await Rechazar(contexto, StatusCodes.Status409Conflict, ErroresIdempotencia.EnCurso);
                return;

            case EstadoAdquisicion.HuellaDistinta:
                await Rechazar(contexto, StatusCodes.Status422UnprocessableEntity, ErroresIdempotencia.ClaveReutilizada);
                return;

            case EstadoAdquisicion.Adquirida:
            default:
                await ProcesarYGuardarAsync(contexto, clave, almacen, reloj);
                return;
        }
    }

    private async Task ProcesarYGuardarAsync(HttpContext contexto, string clave, IAlmacenIdempotencia almacen, TimeProvider reloj)
    {
        // La respuesta se escribe primero en memoria para poder guardarla antes de enviarla.
        var destino = contexto.Response.Body;
        await using var memoria = new MemoryStream();
        contexto.Response.Body = memoria;

        try
        {
            await siguiente(contexto);
        }
        catch
        {
            contexto.Response.Body = destino;
            await almacen.LiberarAsync(clave, CancellationToken.None);
            throw;
        }

        contexto.Response.Body = destino;

        var estado = contexto.Response.StatusCode;

        if (estado >= StatusCodes.Status500InternalServerError)
        {
            await almacen.LiberarAsync(clave, CancellationToken.None);
        }
        else
        {
            var cuerpo = Encoding.UTF8.GetString(memoria.GetBuffer(), 0, (int)memoria.Length);
            var ubicacion = contexto.Response.Headers.Location.ToString();

            await almacen.CompletarAsync(
                clave,
                new RespuestaGuardada(estado, contexto.Response.ContentType, cuerpo, ubicacion.Length == 0 ? null : ubicacion),
                reloj.GetUtcNow(),
                CancellationToken.None);
        }

        memoria.Position = 0;
        await memoria.CopyToAsync(destino, contexto.RequestAborted);
    }

    private static async Task Reproducir(HttpContext contexto, RespuestaGuardada respuesta)
    {
        contexto.Response.StatusCode = respuesta.EstadoHttp;
        contexto.Response.ContentType = respuesta.TipoContenido;
        contexto.Response.Headers[CabeceraRepetida] = "true";

        if (respuesta.Ubicacion is not null)
        {
            contexto.Response.Headers.Location = respuesta.Ubicacion;
        }

        await contexto.Response.WriteAsync(respuesta.Cuerpo, contexto.RequestAborted);
    }

    private static Task Rechazar(HttpContext contexto, int estado, Reservas.Dominio.Comun.ErrorDominio error) =>
        ProblemasApi.Crear(estado, error.Codigo, error.Mensaje).ExecuteAsync(contexto);

    /// <summary>SHA-256 del método, la ruta y el cuerpo: identifica «esta misma petición».</summary>
    private static async Task<string> CalcularHuellaAsync(HttpRequest peticion)
    {
        peticion.EnableBuffering();

        using var memoria = new MemoryStream();
        await peticion.Body.CopyToAsync(memoria);
        peticion.Body.Position = 0;

        var cabecera = Encoding.UTF8.GetBytes($"{peticion.Method}\n{peticion.Path}\n");
        var contenido = new byte[cabecera.Length + memoria.Length];
        cabecera.CopyTo(contenido, 0);
        memoria.ToArray().CopyTo(contenido, cabecera.Length);

        return Convert.ToHexStringLower(SHA256.HashData(contenido));
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{8,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex PatronClave();
}
