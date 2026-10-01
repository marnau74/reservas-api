using Reservas.Aplicacion.Abstracciones;
using Reservas.Aplicacion.Correos;
using Reservas.Dominio.Comun;
using Reservas.Dominio.Correos;
using Reservas.Dominio.Gestion;
using Reservas.Dominio.Negocios;

namespace Reservas.Aplicacion.Mantenimiento;

/// <summary>
/// Las tareas que se repiten con el tiempo: caducar reservas sin confirmar, recordar las próximas,
/// borrar los datos personales que ya no hacen falta y limpiar tablas que crecen sin parar. Cada
/// una es idempotente: ejecutarla dos veces seguidas, o desde dos instancias a la vez, no duplica ni
/// rompe nada, porque cada reserva se cambia con control de concurrencia.
/// </summary>
public sealed class MantenimientoProgramado(
    IRepositorioReservas reservas,
    IRepositorioNegocios negocios,
    IRepositorioMantenimiento mantenimiento,
    OpcionesCorreo opcionesCorreo,
    TimeProvider reloj)
{
    /// <summary>Un recordatorio no llega a las reservas que empiezan en menos de esto: ya no da tiempo a nada.</summary>
    public static readonly TimeSpan AntelacionMinimaRecordatorio = TimeSpan.FromHours(1);

    /// <summary>Con cuánta antelación se recuerda una reserva confirmada.</summary>
    public static readonly TimeSpan AntelacionRecordatorio = TimeSpan.FromHours(24);

    /// <summary>Los datos del cliente se conservan 24 meses desde que hizo la reserva (RGPD: solo el tiempo necesario).</summary>
    public const int MesesRetencionClientes = 24;

    /// <summary>La respuesta guardada de una petición idempotente solo sirve mientras el cliente pueda reintentar.</summary>
    public static readonly TimeSpan RetencionClaves = TimeSpan.FromHours(24);

    public static readonly TimeSpan RetencionTokens = TimeSpan.FromDays(7);

    public static readonly TimeSpan RetencionCorreos = TimeSpan.FromDays(30);

    private const int TamanoLote = 50;
    private const int MaximoPasadas = 20;

    /// <summary>Cancela las reservas pendientes cuyo plazo para confirmar pasó, y libera sus mesas.</summary>
    public Task<int> CaducarPendientesAsync(CancellationToken cancellationToken)
    {
        var ahora = reloj.GetUtcNow();

        return ProcesarAsync(
            ct => reservas.ListarPendientesCaducadasAsync(ahora, TamanoLote, ct),
            async (reserva, ct) =>
            {
                var caducada = reserva.Caducar(ahora);
                return caducada.EsFallo ? caducada : await reservas.ActualizarAsync(reserva, [], ct);
            },
            cancellationToken);
    }

    /// <summary>Programa el correo de recordatorio de las reservas confirmadas que empiezan dentro de las próximas 24 horas.</summary>
    public Task<int> ProgramarRecordatoriosAsync(CancellationToken cancellationToken)
    {
        var ahora = reloj.GetUtcNow();
        var tramo = new IntervaloTiempo(ahora + AntelacionMinimaRecordatorio, ahora + AntelacionRecordatorio);
        var negociosVistos = new Dictionary<Guid, Negocio?>();

        return ProcesarAsync(
            ct => reservas.ListarParaRecordatorioAsync(tramo, TamanoLote, ct),
            async (reserva, ct) =>
            {
                if (!negociosVistos.TryGetValue(reserva.NegocioId, out var negocio))
                {
                    negocio = await negocios.ObtenerAsync(reserva.NegocioId, ct);
                    negociosVistos[reserva.NegocioId] = negocio;
                }

                if (negocio is null)
                {
                    return Resultado.Fallo(ErroresAplicacion.NegocioNoEncontrado);
                }

                var marcada = reserva.MarcarRecordatorioProgramado();
                if (marcada.EsFallo)
                {
                    return marcada;
                }

                var correo = PlantillasCorreo.Crear(TipoCorreo.Recordatorio, reserva, negocio, opcionesCorreo, ahora);
                return await reservas.ActualizarAsync(reserva, [correo], ct);
            },
            cancellationToken);
    }

    /// <summary>Borra el nombre, el correo y el teléfono de las reservas viejas que ya no están activas. La reserva se conserva.</summary>
    public Task<int> AnonimizarClientesAsync(CancellationToken cancellationToken)
    {
        var limite = reloj.GetUtcNow().AddMonths(-MesesRetencionClientes);

        return ProcesarAsync(
            ct => reservas.ListarAnonimizablesAsync(limite, TamanoLote, ct),
            async (reserva, ct) =>
            {
                var anonimizada = reserva.Anonimizar();
                return anonimizada.EsFallo ? anonimizada : await reservas.ActualizarAsync(reserva, [], ct);
            },
            cancellationToken);
    }

    /// <summary>Borra lo que ya no sirve: respuestas de idempotencia, tokens de renovación y correos antiguos.</summary>
    public async Task<ResumenPurga> PurgarAsync(CancellationToken cancellationToken)
    {
        var ahora = reloj.GetUtcNow();

        return new ResumenPurga(
            await mantenimiento.PurgarClavesIdempotenciaAsync(ahora - RetencionClaves, cancellationToken),
            await mantenimiento.PurgarTokensRefrescoAsync(ahora - RetencionTokens, cancellationToken),
            await mantenimiento.PurgarCorreosAsync(ahora - RetencionCorreos, cancellationToken));
    }

    /// <summary>
    /// Aplica una operación a lotes de reservas hasta que no quedan. Si una falla (normalmente porque
    /// otra instancia la cambió antes), se vuelve a consultar, porque tras un conflicto el repositorio
    /// descarta lo que tenía cargado; la que falló se salta en el resto de esta ejecución, para que una
    /// que falle siempre no deje sin procesar a todas las que vienen detrás (ya volverá a intentarse en la
    /// siguiente ejecución). Cada pasada procesa alguna o aparta una más, así que siempre termina.
    /// </summary>
    internal static async Task<int> ProcesarAsync(
        Func<CancellationToken, Task<IReadOnlyList<Reserva>>> listar,
        Func<Reserva, CancellationToken, Task<Resultado>> aplicar,
        CancellationToken cancellationToken)
    {
        var total = 0;
        var fallidas = new HashSet<Guid>();

        for (var pasada = 0; pasada < MaximoPasadas; pasada++)
        {
            var lote = (await listar(cancellationToken)).Where(reserva => !fallidas.Contains(reserva.Id)).ToList();
            if (lote.Count == 0)
            {
                break;
            }

            foreach (var reserva in lote)
            {
                if ((await aplicar(reserva, cancellationToken)).EsFallo)
                {
                    fallidas.Add(reserva.Id);
                    break;
                }

                total++;
            }
        }

        return total;
    }
}

public sealed record ResumenPurga(int ClavesIdempotencia, int TokensRefresco, int Correos);
