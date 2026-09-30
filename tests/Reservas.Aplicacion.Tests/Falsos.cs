using Reservas.Aplicacion.Abstracciones;
using Reservas.Dominio.Comun;
using Reservas.Dominio.Disponibilidad;
using Reservas.Dominio.Gestion;
using Reservas.Dominio.Locales;
using Reservas.Dominio.Negocios;

namespace Reservas.Aplicacion.Tests;

/// <summary>Repositorio de negocios en memoria.</summary>
internal sealed class RepositorioNegociosFalso(Negocio? negocio, ConfiguracionLocal configuracion) : IRepositorioNegocios
{
    public int LlamadasAConfiguracion { get; private set; }

    public Task<Negocio?> ObtenerPorSlugAsync(string slug, CancellationToken cancellationToken) =>
        Task.FromResult(negocio is not null && negocio.Slug == slug ? negocio : null);

    public Task<Negocio?> ObtenerAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(negocio is not null && negocio.Id == id ? negocio : null);

    public Task<ConfiguracionLocal> ObtenerConfiguracionAsync(Guid negocioId, CancellationToken cancellationToken)
    {
        LlamadasAConfiguracion++;
        return Task.FromResult(configuracion);
    }
}

/// <summary>
/// Repositorio de reservas en memoria. Permite simular que, entre el cálculo de disponibilidad y el
/// guardado, otra petición se queda con la mesa: <see cref="SimularQueOtraPeticionGana"/>.
/// </summary>
internal sealed class RepositorioReservasFalso : IRepositorioReservas
{
    private readonly List<Reserva> _reservas = [];
    private readonly List<OcupacionMesa> _ocupaciones = [];

    /// <summary>Si es verdadero, cada intento de guardar falla con «mesa ocupada» porque otra petición ganó esa mesa.</summary>
    public bool SimularQueOtraPeticionGana { get; set; }

    /// <summary>Si no es nulo, el siguiente intento de guardar devuelve este error.</summary>
    public ErrorDominio? ErrorAlAgregar { get; set; }

    public int IntentosDeAgregar { get; private set; }

    public int Actualizaciones { get; private set; }

    public IReadOnlyList<Reserva> Reservas => _reservas;

    public void Sembrar(Reserva reserva)
    {
        _reservas.Add(reserva);
        _ocupaciones.AddRange(reserva.MesaIds.Select(mesa => new OcupacionMesa(mesa, reserva.Intervalo)));
    }

    public Task<Resultado> AgregarAsync(Reserva reserva, CancellationToken cancellationToken)
    {
        IntentosDeAgregar++;

        if (ErrorAlAgregar is { } error)
        {
            return Task.FromResult(Resultado.Fallo(error));
        }

        if (SimularQueOtraPeticionGana)
        {
            // Otra petición se ha quedado con estas mesas justo antes: se refleja en las ocupaciones.
            _ocupaciones.AddRange(reserva.MesaIds.Select(mesa => new OcupacionMesa(mesa, reserva.Intervalo)));
            return Task.FromResult(Resultado.Fallo(ErroresReserva.MesaOcupada));
        }

        Sembrar(reserva);
        return Task.FromResult(Resultado.Exito());
    }

    public Task<Reserva?> ObtenerAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_reservas.FirstOrDefault(r => r.Id == id));

    public Task<Reserva?> ObtenerPorCodigoAsync(string codigoGestion, CancellationToken cancellationToken) =>
        Task.FromResult(_reservas.FirstOrDefault(r => r.CodigoGestion == codigoGestion));

    public Task<Resultado> ActualizarAsync(Reserva reserva, CancellationToken cancellationToken)
    {
        Actualizaciones++;
        return Task.FromResult(Resultado.Exito());
    }

    public Task<IReadOnlyList<Reserva>> ListarPorInicioAsync(Guid negocioId, IntervaloTiempo tramo, CancellationToken cancellationToken)
    {
        IReadOnlyList<Reserva> lista = [.. _reservas
            .Where(r => r.NegocioId == negocioId && r.Intervalo.Inicio >= tramo.Inicio && r.Intervalo.Inicio < tramo.Fin)
            .OrderBy(r => r.Intervalo.Inicio)];
        return Task.FromResult(lista);
    }

    public Task<IReadOnlyList<OcupacionMesa>> ObtenerOcupacionesAsync(Guid negocioId, IntervaloTiempo ventana, CancellationToken cancellationToken)
    {
        IReadOnlyList<OcupacionMesa> activas = [.. _ocupaciones.Where(o => o.Intervalo.Solapa(ventana))];
        return Task.FromResult(activas);
    }
}

/// <summary>Un negocio de prueba: comida y cena todos los días y dos mesas (una de 1 a 2 y otra de 1 a 4).</summary>
internal static class Escenario
{
    public static readonly DateOnly Sabado = new(2026, 10, 3);
    public static readonly DateTimeOffset UnMesAntes = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);

    public static Negocio CrearNegocio() =>
        Negocio.Crear("bar-la-plaza", "Bar La Plaza (demo)", "Europe/Madrid", PoliticasReserva.PorDefecto).Valor;

    public static ConfiguracionLocal CrearLocal(params (int Minima, int Maxima)[] mesas)
    {
        var sala = Guid.NewGuid();
        var horarios = Enum.GetValues<DayOfWeek>()
            .SelectMany(dia => new[]
            {
                Horario.Crear(dia, Turno.Comida, new TimeOnly(13, 0), new TimeOnly(15, 30), 30).Valor,
                Horario.Crear(dia, Turno.Cena, new TimeOnly(20, 0), new TimeOnly(22, 30), 30).Valor,
            })
            .ToList();

        var mesasLocal = (mesas.Length > 0 ? mesas : [(1, 2), (1, 4)])
            .Select((m, i) => Mesa.Crear(sala, $"Mesa {i + 1}", m.Minima, m.Maxima, esCombinable: false).Valor)
            .ToList();

        return new ConfiguracionLocal(mesasLocal, horarios, []);
    }

    public static Reserva ReservaPendiente(Negocio negocio, ConfiguracionLocal local, DateTimeOffset ahora, int comensales = 2)
    {
        var inicio = negocio.Zona.AUtc(Sabado, new TimeOnly(21, 0))!.Value;
        return Reserva.Crear(
            negocio.Id,
            new IntervaloTiempo(inicio, inicio + negocio.Politicas.DuracionEstandar),
            comensales,
            DatosCliente.Crear("Ana Pérez", "ana@example.com").Valor,
            [local.Mesas[0].Id],
            OrigenReserva.Publica,
            ahora).Valor;
    }
}
