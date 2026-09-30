using Microsoft.EntityFrameworkCore;

using Reservas.Dominio.Comun;
using Reservas.Dominio.Gestion;
using Reservas.Dominio.Locales;
using Reservas.Dominio.Negocios;
using Reservas.Infraestructura.Persistencia;
using Reservas.Tests.Comunes;

namespace Reservas.Infraestructura.Tests;

/// <summary>
/// Base de los tests que necesitan PostgreSQL: cada test recibe una base de datos propia, ya
/// migrada, y datos de partida (un negocio con una sala y tres mesas).
/// </summary>
public abstract class BaseDeDatosTest(ServidorPostgres servidor) : IAsyncLifetime
{
    protected static readonly DateTimeOffset Ahora = new(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);

    // Sábado 3 de octubre de 2026, de 21:00 a 22:30 hora de Madrid.
    protected static readonly IntervaloTiempo Cena = new(
        new DateTimeOffset(2026, 10, 3, 19, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 10, 3, 20, 30, 0, TimeSpan.Zero));

    protected string CadenaConexion { get; private set; } = string.Empty;

    protected Negocio Negocio { get; private set; } = null!;

    protected Sala Sala { get; private set; } = null!;

    protected Mesa Mesa1 { get; private set; } = null!;

    protected Mesa Mesa2 { get; private set; } = null!;

    protected Mesa Mesa3 { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        CadenaConexion = await servidor.CrearBaseDeDatosAsync();

        Negocio = Negocio.Crear("bar-la-plaza", "Bar La Plaza (demo)", "Europe/Madrid", PoliticasReserva.PorDefecto).Valor;
        Sala = Sala.Crear("Sala principal").Valor;
        Mesa1 = Mesa.Crear(Sala.Id, "Mesa 1", 1, 4, esCombinable: false).Valor;
        Mesa2 = Mesa.Crear(Sala.Id, "Mesa 2", 1, 4, esCombinable: false).Valor;
        Mesa3 = Mesa.Crear(Sala.Id, "Mesa 3", 1, 6, esCombinable: true).Valor;

        await using var db = NuevoContexto();
        db.Negocios.Add(Negocio);
        db.Salas.Add(Sala);
        db.Mesas.AddRange(Mesa1, Mesa2, Mesa3);
        AsignarNegocio(db, Negocio.Id, Sala, Mesa1, Mesa2, Mesa3);
        await db.SaveChangesAsync();
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    protected ReservasDbContext NuevoContexto() => ServidorPostgres.CrearContexto(CadenaConexion);

    /// <summary>Marca a qué negocio pertenecen las entidades del local (columna negocio_id, en la sombra del modelo).</summary>
    protected static void AsignarNegocio(ReservasDbContext db, Guid negocioId, params object[] entidades)
    {
        foreach (var entidad in entidades)
        {
            db.Entry(entidad).Property("NegocioId").CurrentValue = negocioId;
        }
    }

    protected static DatosCliente Cliente(int numero = 1) =>
        DatosCliente.Crear($"Cliente {numero}", $"cliente{numero}@example.com").Valor;

    protected Reserva NuevaReserva(IntervaloTiempo? intervalo = null, int comensales = 2, params Mesa[] mesas) =>
        Reserva.Crear(
            Negocio.Id,
            intervalo ?? Cena,
            comensales,
            Cliente(),
            [.. (mesas.Length > 0 ? mesas : [Mesa1]).Select(m => m.Id)],
            OrigenReserva.Publica,
            Ahora).Valor;

    protected static IntervaloTiempo Desplazado(IntervaloTiempo intervalo, int minutos) =>
        new(intervalo.Inicio.AddMinutes(minutos), intervalo.Fin.AddMinutes(minutos));
}
