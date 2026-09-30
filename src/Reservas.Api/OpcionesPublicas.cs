namespace Reservas.Api;

/// <summary>Opciones de la API pública (sección <c>Publico</c> de la configuración).</summary>
public sealed class OpcionesPublicas
{
    public const string Seccion = "Publico";

    /// <summary>
    /// Si la respuesta de crear una reserva incluye el código secreto de gestión (y la cabecera
    /// <c>Location</c> que lo contiene). El código está pensado para llegar por correo, para que
    /// confirmar la reserva demuestre que el cliente controla ese correo. Mientras el envío de
    /// correos no esté activo, hay que devolverlo en la respuesta para poder completar el flujo.
    /// </summary>
    public bool MostrarCodigoGestion { get; set; } = true;
}
