using Microsoft.EntityFrameworkCore;

using Reservas.Aplicacion.Personal;
using Reservas.Infraestructura.Persistencia;

namespace Reservas.Api.Arranque;

/// <summary>
/// Lo que la API hace con la base de datos al arrancar: aplicar las migraciones y, si se pide,
/// sembrar el negocio de demostración. Los dos pasos dependen de la configuración, no del entorno,
/// para poder usarlos en desarrollo, en tests y en la demo pública sin cambiar el código.
/// </summary>
public static class InicioBaseDeDatos
{
    /// <summary>Aplica las migraciones al arrancar. Por defecto solo en desarrollo.</summary>
    public const string MigrarAlArrancar = "BaseDeDatos:MigrarAlArrancar";

    /// <summary>Crea el negocio ficticio y sus cuentas. Por defecto solo en desarrollo.</summary>
    public const string Sembrar = "Demo:Sembrar";

    /// <summary>Contraseña de las cuentas de demostración. Obligatoria fuera de desarrollo.</summary>
    public const string Contrasena = "Demo:Contrasena";

    /// <summary>Si además se crean unas reservas de ejemplo para que la agenda no esté vacía.</summary>
    public const string ReservasDeEjemplo = "Demo:ReservasDeEjemplo";

    public static async Task PrepararAsync(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var configuracion = app.Configuration;
        var esDesarrollo = app.Environment.IsDevelopment();

        var migrar = esDesarrollo || configuracion.GetValue<bool>(MigrarAlArrancar);

        // «Desarrollo:SembrarDatosDemo» es el interruptor antiguo que usan los tests para controlar sus datos.
        var sembrarPorDefecto = esDesarrollo && configuracion.GetValue("Desarrollo:SembrarDatosDemo", true);
        var sembrar = configuracion.GetValue(Sembrar, sembrarPorDefecto);

        if (!migrar && !sembrar)
        {
            return;
        }

        var contrasena = sembrar ? ContrasenaDeLasCuentas(configuracion, esDesarrollo) : string.Empty;

        await using var ambito = app.Services.CreateAsyncScope();
        var db = ambito.ServiceProvider.GetRequiredService<ReservasDbContext>();

        if (migrar)
        {
            // EF Core toma un bloqueo en la base de datos mientras migra: si arrancan varias
            // instancias a la vez, una migra y las demás esperan.
            await db.Database.MigrateAsync();
        }

        if (sembrar)
        {
            await SembradorDemo.SembrarSiHaceFaltaAsync(db, contrasena);

            if (configuracion.GetValue(ReservasDeEjemplo, true))
            {
                await SembradorDemo.SembrarReservasDeEjemploAsync(db, ambito.ServiceProvider.GetRequiredService<TimeProvider>());
            }
        }
    }

    private static string ContrasenaDeLasCuentas(IConfiguration configuracion, bool esDesarrollo)
    {
        var configurada = configuracion[Contrasena] ?? configuracion["Desarrollo:ContrasenaDemo"];

        if (string.IsNullOrWhiteSpace(configurada))
        {
            // La contraseña de desarrollo está escrita en el código y en el README: en un entorno
            // accesible desde internet, cualquiera podría entrar con ella.
            return esDesarrollo
                ? SembradorDemo.ContrasenaDemoPorDefecto
                : throw new InvalidOperationException($"Con «{Sembrar}» activo fuera de desarrollo hay que configurar «{Contrasena}».");
        }

        return !esDesarrollo && !ServicioUsuarios.EsContrasenaAceptable(configurada)
            ? throw new InvalidOperationException($"«{Contrasena}» debe tener al menos 10 caracteres, con letras y números.")
            : configurada;
    }
}
