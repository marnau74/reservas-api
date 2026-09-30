using Reservas.Aplicacion.Abstracciones;
using Reservas.Dominio.Comun;
using Reservas.Dominio.Locales;

namespace Reservas.Aplicacion.Local;

/// <summary>
/// Configuración del local por su encargado: salas, mesas, horarios y cierres. Cada operación
/// recibe el negocio de la sesión; un identificador de otro negocio se trata como inexistente.
/// </summary>
public sealed class ServicioLocal(IRepositorioLocal local)
{
    // --- Salas ---

    public Task<IReadOnlyList<Sala>> ListarSalasAsync(Guid negocioId, CancellationToken cancellationToken) =>
        local.ListarSalasAsync(negocioId, cancellationToken);

    public async Task<Resultado<Sala>> CrearSalaAsync(Guid negocioId, string nombre, CancellationToken cancellationToken)
    {
        var sala = Sala.Crear(nombre);
        if (sala.EsExito)
        {
            await local.AgregarSalaAsync(negocioId, sala.Valor, cancellationToken);
        }

        return sala;
    }

    public Task<Resultado> EliminarSalaAsync(Guid negocioId, Guid salaId, CancellationToken cancellationToken) =>
        local.EliminarSalaAsync(negocioId, salaId, cancellationToken);

    // --- Mesas ---

    public Task<IReadOnlyList<Mesa>> ListarMesasAsync(Guid negocioId, CancellationToken cancellationToken) =>
        local.ListarMesasAsync(negocioId, cancellationToken);

    public async Task<Resultado<Mesa>> CrearMesaAsync(
        Guid negocioId,
        Guid salaId,
        string nombre,
        int capacidadMinima,
        int capacidadMaxima,
        bool esCombinable,
        CancellationToken cancellationToken)
    {
        if (!await local.ExisteSalaAsync(negocioId, salaId, cancellationToken))
        {
            return Resultado.Fallo<Mesa>(ErroresAplicacion.SalaNoEncontrada);
        }

        var mesa = Mesa.Crear(salaId, nombre, capacidadMinima, capacidadMaxima, esCombinable);
        if (mesa.EsExito)
        {
            await local.AgregarMesaAsync(negocioId, mesa.Valor, cancellationToken);
        }

        return mesa;
    }

    public Task<Resultado> EliminarMesaAsync(Guid negocioId, Guid mesaId, CancellationToken cancellationToken) =>
        local.EliminarMesaAsync(negocioId, mesaId, cancellationToken);

    // --- Horarios ---

    public Task<IReadOnlyList<Registrado<Horario>>> ListarHorariosAsync(Guid negocioId, CancellationToken cancellationToken) =>
        local.ListarHorariosAsync(negocioId, cancellationToken);

    public async Task<Resultado<Registrado<Horario>>> CrearHorarioAsync(
        Guid negocioId,
        DayOfWeek dia,
        Turno turno,
        TimeOnly inicio,
        TimeOnly fin,
        int intervaloMinutos,
        CancellationToken cancellationToken)
    {
        var horario = Horario.Crear(dia, turno, inicio, fin, intervaloMinutos);
        if (horario.EsFallo)
        {
            return Resultado.Fallo<Registrado<Horario>>(horario.Error);
        }

        var id = await local.AgregarHorarioAsync(negocioId, horario.Valor, cancellationToken);

        return Resultado.Exito(new Registrado<Horario>(id, horario.Valor));
    }

    public Task<Resultado> EliminarHorarioAsync(Guid negocioId, Guid horarioId, CancellationToken cancellationToken) =>
        local.EliminarHorarioAsync(negocioId, horarioId, cancellationToken);

    // --- Cierres ---

    public Task<IReadOnlyList<Registrado<Cierre>>> ListarCierresAsync(Guid negocioId, CancellationToken cancellationToken) =>
        local.ListarCierresAsync(negocioId, cancellationToken);

    public async Task<Resultado<Registrado<Cierre>>> CrearCierreAsync(
        Guid negocioId,
        DateOnly fecha,
        Turno? turno,
        string motivo,
        CancellationToken cancellationToken)
    {
        var motivoLimpio = motivo?.Trim() ?? string.Empty;

        if (motivoLimpio.Length is 0 or > 200 || (turno is { } t && !Enum.IsDefined(t)))
        {
            return Resultado.Fallo<Registrado<Cierre>>(ErroresLocal.CierreInvalido);
        }

        var cierre = new Cierre(fecha, turno, motivoLimpio);
        var id = await local.AgregarCierreAsync(negocioId, cierre, cancellationToken);

        return Resultado.Exito(new Registrado<Cierre>(id, cierre));
    }

    public Task<Resultado> EliminarCierreAsync(Guid negocioId, Guid cierreId, CancellationToken cancellationToken) =>
        local.EliminarCierreAsync(negocioId, cierreId, cancellationToken);
}
