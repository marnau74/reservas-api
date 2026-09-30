using System.Globalization;

namespace Reservas.Api.Contratos;

/// <summary>
/// Formatos estrictos de fecha y hora del contrato de la API. Se parsean con un formato exacto y
/// cultura invariante: el analizador por defecto aceptaría «10/03/2026», que es el 3 de octubre
/// o el 10 de marzo según el país, y una API pública no puede tener esa ambigüedad.
/// </summary>
public static class Formatos
{
    public const string Fecha = "yyyy-MM-dd";

    public const string Hora = "HH:mm";

    public static bool TryParseFecha(string? texto, out DateOnly fecha) =>
        DateOnly.TryParseExact(texto, Fecha, CultureInfo.InvariantCulture, DateTimeStyles.None, out fecha);

    public static bool TryParseHora(string? texto, out TimeOnly hora) =>
        TimeOnly.TryParseExact(texto, Hora, CultureInfo.InvariantCulture, DateTimeStyles.None, out hora);

    /// <summary>Un entero positivo escrito solo con dígitos (rechaza signos, decimales y separadores).</summary>
    public static bool TryParseComensales(string? texto, out int comensales) =>
        int.TryParse(texto, NumberStyles.None, CultureInfo.InvariantCulture, out comensales) && comensales > 0;

    public static string DeFecha(DateOnly fecha) => fecha.ToString(Fecha, CultureInfo.InvariantCulture);

    public static string DeHora(TimeOnly hora) => hora.ToString(Hora, CultureInfo.InvariantCulture);
}
