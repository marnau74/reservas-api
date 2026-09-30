using System.Globalization;

using Reservas.Dominio.Disponibilidad;
using Reservas.Dominio.Locales;
using Reservas.Dominio.Negocios;

namespace Reservas.Dominio.Tests;

/// <summary>Datos de prueba: un negocio pequeño con dos mesas y horarios de comida y cena.</summary>
internal static class Escenario
{
    public static readonly Guid SalaPrincipal = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static readonly Guid Terraza = Guid.Parse("22222222-2222-2222-2222-222222222222");

    /// <summary>Un instante UTC.</summary>
    public static DateTimeOffset Utc(int anio, int mes, int dia, int hora = 0, int minuto = 0) =>
        new(anio, mes, dia, hora, minuto, 0, TimeSpan.Zero);

    public static TimeOnly Hora(string texto) => TimeOnly.Parse(texto, CultureInfo.InvariantCulture);

    public static Negocio CrearNegocio(PoliticasReserva? politicas = null) =>
        Negocio.Crear("bar-la-plaza", "Bar La Plaza (demo)", "Europe/Madrid", politicas ?? PoliticasReserva.PorDefecto).Valor;

    public static Mesa CrearMesa(int minima, int maxima, bool combinable = false, Guid? sala = null) =>
        Mesa.Crear(sala ?? SalaPrincipal, $"Mesa {minima}-{maxima}", minima, maxima, combinable).Valor;

    public static Horario CrearHorario(DayOfWeek dia, Turno turno, string desde, string hasta, int intervaloMinutos) =>
        Horario.Crear(dia, turno, Hora(desde), Hora(hasta), intervaloMinutos).Valor;

    /// <summary>Comida (13:00 a 15:30) y cena (20:00 a 22:30) cada media hora, todos los días.</summary>
    public static IReadOnlyCollection<Horario> ComidaYCenaTodosLosDias() =>
    [
        .. Enum.GetValues<DayOfWeek>().SelectMany(dia => new[]
        {
            CrearHorario(dia, Turno.Comida, "13:00", "15:30", 30),
            CrearHorario(dia, Turno.Cena, "20:00", "22:30", 30),
        }),
    ];

    public static DatosDisponibilidad Datos(
        IReadOnlyCollection<Mesa>? mesas = null,
        IReadOnlyCollection<Horario>? horarios = null,
        IReadOnlyCollection<Cierre>? cierres = null,
        IReadOnlyCollection<OcupacionMesa>? ocupaciones = null,
        PoliticasReserva? politicas = null) =>
        new(
            CrearNegocio(politicas),
            mesas ?? [CrearMesa(1, 2), CrearMesa(1, 4)],
            horarios ?? ComidaYCenaTodosLosDias(),
            cierres ?? [],
            ocupaciones ?? []);
}
