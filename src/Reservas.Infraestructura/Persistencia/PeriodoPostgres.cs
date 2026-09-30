using Npgsql;

using NpgsqlTypes;

using Reservas.Dominio.Comun;

namespace Reservas.Infraestructura.Persistencia;

/// <summary>
/// Traduce el <see cref="IntervaloTiempo"/> del dominio al tipo <c>tstzrange</c> de PostgreSQL.
/// Los rangos son semiabiertos, <c>[inicio, fin)</c>, igual que en el dominio: PostgreSQL usa
/// esa misma convención para el operador de solapamiento <c>&amp;&amp;</c>, que es el que vigila
/// la restricción de exclusión.
/// </summary>
internal static class PeriodoPostgres
{
    public static NpgsqlRange<DateTime> Desde(IntervaloTiempo intervalo) =>
        new(intervalo.Inicio.UtcDateTime, lowerBoundIsInclusive: true, intervalo.Fin.UtcDateTime, upperBoundIsInclusive: false);

    public static IntervaloTiempo AIntervalo(NpgsqlRange<DateTime> rango) =>
        new(
            new DateTimeOffset(DateTime.SpecifyKind(rango.LowerBound, DateTimeKind.Utc)),
            new DateTimeOffset(DateTime.SpecifyKind(rango.UpperBound, DateTimeKind.Utc)));
}
