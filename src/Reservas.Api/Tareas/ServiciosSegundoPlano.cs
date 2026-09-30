using Reservas.Aplicacion.Correos;
using Reservas.Aplicacion.Mantenimiento;

namespace Reservas.Api.Tareas;

/// <summary>Ajustes de las tareas en segundo plano, en la sección <c>Tareas</c> de la configuración.</summary>
public sealed class OpcionesTareas
{
    public const string Seccion = "Tareas";

    /// <summary>
    /// Si esta instancia envía correos y hace el mantenimiento. Con varias instancias pueden estar todas
    /// activas (no se pisan entre sí), pero conviene una sola cuando se quiera reducir carga. Los tests
    /// lo apagan para dirigir las tareas a mano, sin depender del reloj.
    /// </summary>
    public bool Activas { get; set; } = true;

    /// <summary>Cada cuántos segundos se vacía la bandeja de correos.</summary>
    public int SegundosCorreos { get; set; } = 10;

    /// <summary>Cada cuántos segundos se caducan reservas y se programan recordatorios.</summary>
    public int SegundosMantenimiento { get; set; } = 60;
}

/// <summary>Envía los correos de la bandeja de salida cada pocos segundos.</summary>
public sealed partial class ProcesadorCorreosServicio(
    IServiceScopeFactory ambitos,
    OpcionesTareas opciones,
    TimeProvider reloj,
    ILogger<ProcesadorCorreosServicio> registro) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var temporizador = new PeriodicTimer(TimeSpan.FromSeconds(opciones.SegundosCorreos), reloj);

        while (await SiguienteAsync(temporizador, stoppingToken))
        {
            try
            {
                await using var ambito = ambitos.CreateAsyncScope();
                var resumen = await ambito.ServiceProvider.GetRequiredService<ProcesarCorreos>().EjecutarAsync(stoppingToken);

                if (resumen.Enviados + resumen.Fallidos + resumen.Abandonados > 0)
                {
                    Resumen(registro, resumen.Enviados, resumen.Fallidos, resumen.Abandonados);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // Una pasada fallida (por ejemplo, base de datos caída un momento) no debe parar el proceso.
            catch (Exception excepcion)
#pragma warning restore CA1031
            {
                Fallo(registro, excepcion);
            }
        }
    }

    internal static async Task<bool> SiguienteAsync(PeriodicTimer temporizador, CancellationToken cancellationToken)
    {
        try
        {
            return await temporizador.WaitForNextTickAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Correos: {Enviados} enviados, {Fallidos} para reintentar, {Abandonados} abandonados.")]
    private static partial void Resumen(ILogger registro, int enviados, int fallidos, int abandonados);

    [LoggerMessage(Level = LogLevel.Error, Message = "Fallo al procesar la bandeja de correos; se reintentará en la siguiente pasada.")]
    private static partial void Fallo(ILogger registro, Exception excepcion);
}

/// <summary>
/// Caduca las reservas sin confirmar y programa los recordatorios cada minuto; y cada hora borra los
/// datos personales que ya no hacen falta y limpia las tablas que crecen sin parar.
/// </summary>
public sealed partial class MantenimientoServicio(
    IServiceScopeFactory ambitos,
    OpcionesTareas opciones,
    TimeProvider reloj,
    ILogger<MantenimientoServicio> registro) : BackgroundService
{
    private static readonly TimeSpan IntervaloLimpieza = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var temporizador = new PeriodicTimer(TimeSpan.FromSeconds(opciones.SegundosMantenimiento), reloj);
        DateTimeOffset? ultimaLimpieza = null;

        while (await ProcesadorCorreosServicio.SiguienteAsync(temporizador, stoppingToken))
        {
            await using var ambito = ambitos.CreateAsyncScope();
            var mantenimiento = ambito.ServiceProvider.GetRequiredService<MantenimientoProgramado>();

            // Cada tarea va por separado: que una falle no impide las demás.
            await EjecutarAsync("caducar reservas pendientes", () => mantenimiento.CaducarPendientesAsync(stoppingToken), stoppingToken);
            await EjecutarAsync("programar recordatorios", () => mantenimiento.ProgramarRecordatoriosAsync(stoppingToken), stoppingToken);

            var ahora = reloj.GetUtcNow();
            if (ultimaLimpieza is null || ahora - ultimaLimpieza >= IntervaloLimpieza)
            {
                ultimaLimpieza = ahora;
                await EjecutarAsync("anonimizar clientes antiguos", () => mantenimiento.AnonimizarClientesAsync(stoppingToken), stoppingToken);
                await EjecutarAsync("purgar datos caducados", async () =>
                {
                    var purgado = await mantenimiento.PurgarAsync(stoppingToken);
                    return purgado.ClavesIdempotencia + purgado.TokensRefresco + purgado.Correos;
                }, stoppingToken);
            }
        }
    }

    private async Task EjecutarAsync(string tarea, Func<Task<int>> accion, CancellationToken cancellationToken)
    {
        try
        {
            var afectadas = await accion();

            if (afectadas > 0)
            {
                Hecha(registro, tarea, afectadas);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Se está apagando la aplicación.
        }
#pragma warning disable CA1031 // Una tarea fallida no debe parar el proceso ni las demás tareas.
        catch (Exception excepcion)
#pragma warning restore CA1031
        {
            Fallo(registro, tarea, excepcion);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Mantenimiento «{Tarea}»: {Afectadas} elementos.")]
    private static partial void Hecha(ILogger registro, string tarea, int afectadas);

    [LoggerMessage(Level = LogLevel.Error, Message = "Mantenimiento «{Tarea}» ha fallado; se reintentará en la siguiente pasada.")]
    private static partial void Fallo(ILogger registro, string tarea, Exception excepcion);
}
