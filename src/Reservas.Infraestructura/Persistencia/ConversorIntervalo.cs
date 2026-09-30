using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using NpgsqlTypes;

using Reservas.Dominio.Comun;

namespace Reservas.Infraestructura.Persistencia;

/// <summary>Guarda un <see cref="IntervaloTiempo"/> como una columna <c>tstzrange</c>.</summary>
internal sealed class ConversorIntervalo()
    : ValueConverter<IntervaloTiempo, NpgsqlRange<DateTime>>(
        intervalo => PeriodoPostgres.Desde(intervalo),
        rango => PeriodoPostgres.AIntervalo(rango));
