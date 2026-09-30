namespace Reservas.Api;

/// <summary>Opciones de la API pública (sección <c>Publico</c> de la configuración).</summary>
public sealed class OpcionesPublicas
{
    public const string Seccion = "Publico";

    /// <summary>
    /// Si la respuesta de crear una reserva incluye el código secreto de gestión (y la cabecera
    /// <c>Location</c> que lo contiene). El código está pensado para llegar solo por correo: que el
    /// cliente confirme la reserva con él demuestra que controla ese correo. Por eso, por defecto, no
    /// se devuelve. Solo tiene sentido activarlo en desarrollo o en un entorno sin envío de correo.
    /// </summary>
    public bool MostrarCodigoGestion { get; set; }
}
