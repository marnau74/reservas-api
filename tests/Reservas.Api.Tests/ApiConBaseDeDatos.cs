using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

using Reservas.Infraestructura.Persistencia;
using Reservas.Tests.Comunes;

namespace Reservas.Api.Tests;

/// <summary>
/// La API arrancada de verdad contra un PostgreSQL real y su propia base de datos, que crea y
/// migra la propia aplicación al iniciarse (como en desarrollo), con dos negocios de prueba y un
/// reloj que controla el test. Se comparte entre los tests de una clase: arrancar la API cuesta
/// más que un test.
/// </summary>
public class ApiConBaseDeDatos(ServidorPostgres servidor) : IAsyncLifetime
{
    /// <summary>Negocio con dos mesas (de 1 a 2 y de 1 a 4 comensales).</summary>
    public const string NegocioDosMesas = "bar-la-plaza";

    /// <summary>Negocio con una sola mesa de 1 a 4 comensales: cualquier reserva la ocupa entera.</summary>
    public const string NegocioUnaMesa = "una-mesa";

    private const int DiasDisponibles = 40;

    private Fabrica? _fabrica;
    private int _fechasUsadas;

    /// <summary>Hora actual de la API. Empieza el 1 de septiembre de 2026 a las 10:00 UTC.</summary>
    public FakeTimeProvider Reloj { get; } = new(new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero));

    public string CadenaConexion { get; private set; } = string.Empty;

    /// <summary>
    /// Ajustes de configuración de la API. Por defecto, límites de peticiones muy altos para que no
    /// interfieran con los tests que no hablan de ellos.
    /// </summary>
    protected virtual IReadOnlyDictionary<string, string?> Ajustes { get; } = new Dictionary<string, string?>
    {
        ["Desarrollo:SembrarDatosDemo"] = "false",
        ["Limites:Lectura:Permisos"] = "100000",
        ["Limites:Lectura:VentanaSegundos"] = "60",
        ["Limites:Escritura:Permisos"] = "100000",
        ["Limites:Escritura:VentanaSegundos"] = "60",
        ["Limites:Autenticacion:Permisos"] = "100000",
        ["Limites:Autenticacion:VentanaSegundos"] = "60",
    };

    public HttpClient CrearCliente() =>
        (_fabrica ?? throw new InvalidOperationException("La API no está iniciada.")).CreateClient();

    /// <summary>Los servicios de la API en marcha, para inspeccionar cómo está configurada.</summary>
    public IServiceProvider Servicios => (_fabrica ?? throw new InvalidOperationException("La API no está iniciada.")).Services;

    public ReservasDbContext NuevoContexto() => ServidorPostgres.CrearContexto(CadenaConexion);

    /// <summary>
    /// Un día local distinto cada vez, todos dentro del plazo de reserva, para que los tests que
    /// comparten la misma API no se disputen las mesas de un mismo día.
    /// </summary>
    public DateOnly SiguienteFecha()
    {
        var indice = Interlocked.Increment(ref _fechasUsadas);

        return indice > DiasDisponibles
            ? throw new InvalidOperationException("Se han agotado los días de prueba de esta API.")
            : new DateOnly(2026, 9, 10).AddDays(indice);
    }

    public async ValueTask InitializeAsync()
    {
        // Base de datos vacía y sin migrar: la migra la API al arrancar.
        CadenaConexion = await servidor.CrearBaseDeDatosSinMigrarAsync();
        _fabrica = new Fabrica(CadenaConexion, Ajustes, Reloj);

        // Crear un cliente arranca la API, que crea las tablas; después ya se pueden sembrar datos.
        using var arranque = _fabrica.CreateClient();

        await using var db = NuevoContexto();
        await SembrarAsync(db);
    }

    /// <summary>Datos de partida de la base de datos. Por defecto, los dos negocios de prueba.</summary>
    protected virtual async Task SembrarAsync(ReservasDbContext db)
    {
        await SembradorDemo.CrearNegocioAsync(db, NegocioDosMesas);
        await SembradorDemo.CrearNegocioAsync(db, NegocioUnaMesa, [(1, 4, false)]);
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);

        if (_fabrica is not null)
        {
            await _fabrica.DisposeAsync();
        }
    }

    private sealed class Fabrica(string cadenaConexion, IReadOnlyDictionary<string, string?> ajustes, FakeTimeProvider reloj)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:reservas", cadenaConexion);

            foreach (var (clave, valor) in ajustes)
            {
                builder.UseSetting(clave, valor);
            }

            builder.ConfigureTestServices(servicios =>
            {
                servicios.RemoveAll<TimeProvider>();
                servicios.AddSingleton<TimeProvider>(reloj);
            });
        }
    }
}
