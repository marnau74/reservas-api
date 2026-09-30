using Reservas.Dominio.Comun;

namespace Reservas.Dominio.Negocios;

/// <summary>
/// Único sitio donde se convierte entre la hora local del negocio y los instantes UTC.
/// Los horarios se guardan como hora local («cenas a las 21:00») y las reservas como
/// instantes reales, y los cambios de hora obligan a decidir qué pasa con las horas que no
/// existen o que ocurren dos veces:
/// <list type="bullet">
/// <item>Una hora que no existe (02:30 el día que se adelanta el reloj) no se puede
/// reservar: <see cref="AUtc"/> devuelve <c>null</c>.</item>
/// <item>Una hora que ocurre dos veces (02:30 el día que se atrasa) se interpreta como la
/// primera de las dos.</item>
/// </list>
/// </summary>
public sealed class ZonaHorariaNegocio
{
    private readonly TimeZoneInfo _zona;

    private ZonaHorariaNegocio(TimeZoneInfo zona) => _zona = zona;

    public string Id => _zona.Id;

    public static Resultado<ZonaHorariaNegocio> Crear(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return Resultado.Fallo<ZonaHorariaNegocio>(ErroresNegocio.ZonaHorariaInvalida);
        }

        try
        {
            return Resultado.Exito(new ZonaHorariaNegocio(TimeZoneInfo.FindSystemTimeZoneById(id)));
        }
        catch (TimeZoneNotFoundException)
        {
            return Resultado.Fallo<ZonaHorariaNegocio>(ErroresNegocio.ZonaHorariaInvalida);
        }
        catch (InvalidTimeZoneException)
        {
            return Resultado.Fallo<ZonaHorariaNegocio>(ErroresNegocio.ZonaHorariaInvalida);
        }
    }

    /// <summary>Instante UTC de una fecha y hora locales, o <c>null</c> si esa hora no existe.</summary>
    public DateTimeOffset? AUtc(DateOnly fecha, TimeOnly hora)
    {
        var local = fecha.ToDateTime(hora, DateTimeKind.Unspecified);

        if (_zona.IsInvalidTime(local))
        {
            return null;
        }

        var desplazamiento = _zona.IsAmbiguousTime(local)
            ? _zona.GetAmbiguousTimeOffsets(local).Max()
            : _zona.GetUtcOffset(local);

        return new DateTimeOffset(local, desplazamiento).ToUniversalTime();
    }

    public DateTime ALocal(DateTimeOffset instante) => TimeZoneInfo.ConvertTime(instante, _zona).DateTime;

    public DateOnly FechaLocal(DateTimeOffset instante) => DateOnly.FromDateTime(ALocal(instante));
}
