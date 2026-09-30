using System.Security.Cryptography;

using Reservas.Dominio.Comun;

namespace Reservas.Dominio.Gestion;

/// <summary>
/// Una reserva: un grupo, un tramo de tiempo y una o varias mesas. El estado solo cambia con
/// los métodos de esta clase, que comprueban que la transición es válida y devuelven un
/// <see cref="Resultado"/> en lugar de lanzar excepciones: intentar confirmar una reserva ya
/// cancelada es una situación normal, no un fallo del programa.
/// </summary>
public sealed class Reserva
{
    /// <summary>Tiempo que tiene el cliente para confirmar una reserva pendiente.</summary>
    public static readonly TimeSpan TiempoParaConfirmar = TimeSpan.FromMinutes(30);

    /// <summary>Cortesía antes de dar por no presentado a un grupo que no llega.</summary>
    public static readonly TimeSpan MargenNoPresentada = TimeSpan.FromMinutes(15);

    private readonly Guid[] _mesaIds;

    private Reserva(
        Guid negocioId,
        IntervaloTiempo intervalo,
        int comensales,
        DatosCliente cliente,
        Guid[] mesaIds,
        EstadoReserva estado,
        DateTimeOffset creadaEn)
    {
        Id = Guid.NewGuid();
        NegocioId = negocioId;
        Intervalo = intervalo;
        Comensales = comensales;
        Cliente = cliente;
        _mesaIds = mesaIds;
        Estado = estado;
        CreadaEn = creadaEn.ToUniversalTime();
        CodigoGestion = GenerarCodigoGestion();
    }

    public Guid Id { get; }

    public Guid NegocioId { get; }

    public IntervaloTiempo Intervalo { get; }

    public int Comensales { get; }

    public DatosCliente Cliente { get; }

    public IReadOnlyList<Guid> MesaIds => _mesaIds;

    public EstadoReserva Estado { get; private set; }

    /// <summary>Secreto del enlace del correo para que el cliente consulte o cancele su reserva.</summary>
    public string CodigoGestion { get; }

    public DateTimeOffset CreadaEn { get; }

    /// <summary>Hasta cuándo puede confirmarse una reserva pendiente.</summary>
    public DateTimeOffset CaducaEn => CreadaEn + TiempoParaConfirmar;

    /// <summary>¿Bloquea todavía sus mesas? Las canceladas, completadas y no presentadas las liberan.</summary>
    public bool OcupaMesas => Estado is EstadoReserva.Pendiente or EstadoReserva.Confirmada or EstadoReserva.Sentada;

    public static Resultado<Reserva> Crear(
        Guid negocioId,
        IntervaloTiempo intervalo,
        int comensales,
        DatosCliente cliente,
        IReadOnlyCollection<Guid> mesaIds,
        OrigenReserva origen,
        DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(cliente);
        ArgumentNullException.ThrowIfNull(mesaIds);

        if (comensales <= 0)
        {
            return Resultado.Fallo<Reserva>(ErroresReserva.ComensalesInvalidos);
        }

        if (mesaIds.Count == 0)
        {
            return Resultado.Fallo<Reserva>(ErroresReserva.SinMesa);
        }

        // La reserva del público espera confirmación; la que hace el personal ya nace confirmada.
        var estado = origen == OrigenReserva.Publica ? EstadoReserva.Pendiente : EstadoReserva.Confirmada;

        return Resultado.Exito(new Reserva(negocioId, intervalo, comensales, cliente, [.. mesaIds.Distinct()], estado, ahora));
    }

    public Resultado Confirmar(DateTimeOffset ahora)
    {
        if (Estado != EstadoReserva.Pendiente)
        {
            return Resultado.Fallo(ErroresReserva.TransicionInvalida);
        }

        if (ahora >= CaducaEn)
        {
            return Resultado.Fallo(ErroresReserva.Caducada);
        }

        Estado = EstadoReserva.Confirmada;
        return Resultado.Exito();
    }

    public Resultado Cancelar()
    {
        if (Estado is not (EstadoReserva.Pendiente or EstadoReserva.Confirmada))
        {
            return Resultado.Fallo(ErroresReserva.TransicionInvalida);
        }

        Estado = EstadoReserva.Cancelada;
        return Resultado.Exito();
    }

    /// <summary>Cancela una reserva pendiente que no se confirmó a tiempo, liberando sus mesas.</summary>
    public Resultado Caducar(DateTimeOffset ahora)
    {
        if (Estado != EstadoReserva.Pendiente)
        {
            return Resultado.Fallo(ErroresReserva.TransicionInvalida);
        }

        if (ahora < CaducaEn)
        {
            return Resultado.Fallo(ErroresReserva.NoCaducaAun);
        }

        Estado = EstadoReserva.Cancelada;
        return Resultado.Exito();
    }

    public Resultado Sentar()
    {
        if (Estado != EstadoReserva.Confirmada)
        {
            return Resultado.Fallo(ErroresReserva.TransicionInvalida);
        }

        Estado = EstadoReserva.Sentada;
        return Resultado.Exito();
    }

    public Resultado Completar()
    {
        if (Estado != EstadoReserva.Sentada)
        {
            return Resultado.Fallo(ErroresReserva.TransicionInvalida);
        }

        Estado = EstadoReserva.Completada;
        return Resultado.Exito();
    }

    public Resultado MarcarNoPresentada(DateTimeOffset ahora)
    {
        if (Estado != EstadoReserva.Confirmada)
        {
            return Resultado.Fallo(ErroresReserva.TransicionInvalida);
        }

        if (ahora < Intervalo.Inicio + MargenNoPresentada)
        {
            return Resultado.Fallo(ErroresReserva.AunNoEsHora);
        }

        Estado = EstadoReserva.NoPresentada;
        return Resultado.Exito();
    }

    private static string GenerarCodigoGestion()
    {
        // 128 bits aleatorios en base64 seguro para URL: imposible de adivinar (22 caracteres).
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(16))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
