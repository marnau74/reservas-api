using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

using Reservas.Tests.Comunes;

namespace Reservas.Api.Tests;

/// <summary>
/// La API arrancada de verdad contra un PostgreSQL real y su propia base de datos, que crea y
/// migra la propia aplicación al iniciarse (como en desarrollo). Se comparte entre los tests de
/// una clase: arrancar la API cuesta más que un test.
/// </summary>
public sealed class ApiConBaseDeDatos(ServidorPostgres servidor) : IAsyncLifetime
{
    private Fabrica? _fabrica;

    public HttpClient CrearCliente() => (_fabrica ?? throw new InvalidOperationException("La API no está iniciada.")).CreateClient();

    public async ValueTask InitializeAsync()
    {
        // Base de datos vacía y sin migrar: la migra la API al arrancar.
        var cadena = await servidor.CrearBaseDeDatosSinMigrarAsync();
        _fabrica = new Fabrica(cadena);
    }

    public async ValueTask DisposeAsync()
    {
        if (_fabrica is not null)
        {
            await _fabrica.DisposeAsync();
        }
    }

    private sealed class Fabrica(string cadenaConexion) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.UseSetting("ConnectionStrings:reservas", cadenaConexion);
    }
}
