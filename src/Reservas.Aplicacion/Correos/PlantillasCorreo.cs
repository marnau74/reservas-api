using System.Globalization;
using System.Text;

using Reservas.Dominio.Correos;
using Reservas.Dominio.Gestion;
using Reservas.Dominio.Negocios;

namespace Reservas.Aplicacion.Correos;

/// <summary>Ajustes de los correos que dependen de dónde esté publicada la web del negocio.</summary>
public sealed record OpcionesCorreo
{
    public const string MarcaCodigo = "{codigo}";

    /// <summary>
    /// Dirección de la página donde el cliente gestiona su reserva; <c>{codigo}</c> se sustituye por
    /// el código secreto de esa reserva.
    /// </summary>
    public string UrlGestion { get; init; } = "http://localhost:3000/reservas/{codigo}";

    /// <summary>Componer el enlace de una reserva concreta.</summary>
    public string EnlaceDe(Reserva reserva)
    {
        ArgumentNullException.ThrowIfNull(reserva);

        return UrlGestion.Replace(MarcaCodigo, Uri.EscapeDataString(reserva.CodigoGestion), StringComparison.Ordinal);
    }
}

/// <summary>
/// Los textos de los correos, en español y en texto plano. Las horas se dan en la hora local del
/// negocio, y los nombres de días y meses están escritos aquí para no depender de la cultura
/// instalada en el servidor (un contenedor mínimo suele tener solo la invariante).
/// </summary>
public static class PlantillasCorreo
{
    private static readonly string[] Dias = ["domingo", "lunes", "martes", "miércoles", "jueves", "viernes", "sábado"];

    private static readonly string[] Meses =
    [
        "enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre",
    ];

    public static CorreoPendiente Crear(
        TipoCorreo tipo,
        Reserva reserva,
        Negocio negocio,
        OpcionesCorreo opciones,
        DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(reserva);
        ArgumentNullException.ThrowIfNull(negocio);
        ArgumentNullException.ThrowIfNull(opciones);

        var (asunto, cuerpo) = tipo switch
        {
            TipoCorreo.Solicitud => Solicitud(reserva, negocio, opciones),
            TipoCorreo.Confirmacion => Confirmacion(reserva, negocio, opciones),
            TipoCorreo.Cancelacion => Cancelacion(reserva, negocio),
            TipoCorreo.Recordatorio => Recordatorio(reserva, negocio, opciones),
            _ => throw new ArgumentOutOfRangeException(nameof(tipo)),
        };

        return CorreoPendiente.Crear(negocio.Id, reserva.Id, tipo, reserva.Cliente.Email, asunto, cuerpo, ahora);
    }

    private static (string Asunto, string Cuerpo) Solicitud(Reserva reserva, Negocio negocio, OpcionesCorreo opciones)
    {
        var caduca = negocio.Zona.ALocal(reserva.CaducaEn);

        var cuerpo = Escribir(
            reserva,
            negocio,
            "Hemos recibido tu reserva:",
            $"Para que quede confirmada, confírmala antes de las {Hora(caduca)} del {Fecha(caduca)}:",
            opciones.EnlaceDe(reserva),
            "Si no la has pedido tú o ya no la necesitas, ignora este correo: la reserva caduca sola y la mesa se libera.");

        return ($"Confirma tu reserva en {negocio.Nombre}", cuerpo);
    }

    private static (string Asunto, string Cuerpo) Confirmacion(Reserva reserva, Negocio negocio, OpcionesCorreo opciones)
    {
        var cuerpo = Escribir(
            reserva,
            negocio,
            "Tu reserva está confirmada:",
            "Si no puedes venir, cancélala aquí para dejar la mesa libre a otras personas:",
            opciones.EnlaceDe(reserva),
            "¡Te esperamos!");

        return ($"Reserva confirmada en {negocio.Nombre}", cuerpo);
    }

    private static (string Asunto, string Cuerpo) Cancelacion(Reserva reserva, Negocio negocio)
    {
        var cuerpo = Escribir(
            reserva,
            negocio,
            "Tu reserva ha sido cancelada:",
            enlaceIntroduccion: null,
            enlace: null,
            "Si quieres reservar otro día, puedes hacerlo de nuevo cuando quieras.");

        return ($"Reserva cancelada en {negocio.Nombre}", cuerpo);
    }

    private static (string Asunto, string Cuerpo) Recordatorio(Reserva reserva, Negocio negocio, OpcionesCorreo opciones)
    {
        var cuerpo = Escribir(
            reserva,
            negocio,
            "Te recordamos tu reserva:",
            "Si no puedes venir, avísanos cancelándola aquí:",
            opciones.EnlaceDe(reserva),
            "¡Hasta pronto!");

        return ($"Recordatorio de tu reserva en {negocio.Nombre}", cuerpo);
    }

    private static string Escribir(
        Reserva reserva,
        Negocio negocio,
        string introduccion,
        string? enlaceIntroduccion,
        string? enlace,
        string despedida)
    {
        var inicio = negocio.Zona.ALocal(reserva.Intervalo.Inicio);
        var personas = reserva.Comensales == 1 ? "1 persona" : $"{reserva.Comensales.ToString(CultureInfo.InvariantCulture)} personas";

        var texto = new StringBuilder();
        texto.Append("Hola ").Append(reserva.Cliente.Nombre).Append(":\n\n");
        texto.Append(introduccion).Append("\n\n");
        texto.Append("  ").Append(negocio.Nombre).Append('\n');
        texto.Append("  ").Append(Mayuscula(Fecha(inicio, conDia: true))).Append(", a las ").Append(Hora(inicio)).Append('\n');
        texto.Append("  ").Append(personas).Append("\n\n");

        if (enlaceIntroduccion is not null && enlace is not null)
        {
            texto.Append(enlaceIntroduccion).Append('\n').Append(enlace).Append("\n\n");
        }

        texto.Append(despedida).Append('\n');

        return texto.ToString();
    }

    private static string Hora(DateTime local) => local.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static string Fecha(DateTime local, bool conDia = false)
    {
        var dia = local.Day.ToString(CultureInfo.InvariantCulture);
        var fecha = $"{dia} de {Meses[local.Month - 1]} de {local.Year.ToString(CultureInfo.InvariantCulture)}";

        return conDia ? $"{Dias[(int)local.DayOfWeek]} {fecha}" : fecha;
    }

    private static string Mayuscula(string texto) => char.ToUpperInvariant(texto[0]) + texto[1..];
}
