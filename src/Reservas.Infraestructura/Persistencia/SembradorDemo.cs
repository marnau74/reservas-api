using Microsoft.EntityFrameworkCore;

using Reservas.Dominio.Locales;
using Reservas.Dominio.Negocios;
using Reservas.Dominio.Personal;

namespace Reservas.Infraestructura.Persistencia;

/// <summary>Un negocio de demostración ya guardado en la base de datos.</summary>
public sealed record NegocioDemo(Negocio Negocio, Sala Sala, IReadOnlyList<Mesa> Mesas);

/// <summary>
/// Crea negocios de demostración: para probar la API en local, para la demo pública y como datos
/// de partida de los tests. Todos los datos son ficticios.
/// </summary>
public static class SembradorDemo
{
    public const string SlugPorDefecto = "bar-la-plaza";

    /// <summary>
    /// Guarda un negocio en Madrid que abre todos los días con comida (13:00 a 15:30) y cena
    /// (20:00 a 22:30), con una franja cada media hora, y las mesas indicadas (por defecto, una
    /// de 1 a 2 comensales y otra de 1 a 4).
    /// </summary>
    public static async Task<NegocioDemo> CrearNegocioAsync(
        ReservasDbContext db,
        string slug = SlugPorDefecto,
        IReadOnlyList<(int Minima, int Maxima, bool Combinable)>? mesas = null,
        PoliticasReserva? politicas = null)
    {
        ArgumentNullException.ThrowIfNull(db);

        var negocio = Negocio.Crear(slug, $"Negocio {slug} (demo)", "Europe/Madrid", politicas ?? PoliticasReserva.PorDefecto).Valor;
        var sala = Sala.Crear("Sala principal").Valor;

        var mesasCreadas = (mesas ?? [(1, 2, false), (1, 4, false)])
            .Select((datos, indice) => Mesa.Crear(sala.Id, $"Mesa {indice + 1}", datos.Minima, datos.Maxima, datos.Combinable).Valor)
            .ToList();

        var horarios = Enum.GetValues<DayOfWeek>()
            .SelectMany(dia => new[]
            {
                Horario.Crear(dia, Turno.Comida, new TimeOnly(13, 0), new TimeOnly(15, 30), 30).Valor,
                Horario.Crear(dia, Turno.Cena, new TimeOnly(20, 0), new TimeOnly(22, 30), 30).Valor,
            })
            .ToList();

        db.Negocios.Add(negocio);
        db.Salas.Add(sala);
        db.Mesas.AddRange(mesasCreadas);
        db.Horarios.AddRange(horarios);

        // negocio_id no forma parte del dominio: se asigna en la propiedad en la sombra del modelo.
        foreach (var entidad in new object[] { sala }.Concat(mesasCreadas).Concat(horarios))
        {
            db.Entry(entidad).Property(ConstantesPersistencia.NegocioId).CurrentValue = negocio.Id;
        }

        await db.SaveChangesAsync();

        return new NegocioDemo(negocio, sala, mesasCreadas);
    }

    /// <summary>Contraseña de las cuentas de demostración en desarrollo. Ficticia, como todo lo demás: solo existe en local.</summary>
    public const string ContrasenaDemoPorDefecto = "Demo-Reservas-2026";

    /// <summary>Correos de las cuentas de demostración del negocio por defecto (propietario, encargado y personal).</summary>
    public static readonly (string Email, string Nombre, Rol Rol)[] CuentasDemo =
    [
        ("propietario@demo.example", "Propietaria (demo)", Rol.Propietario),
        ("encargado@demo.example", "Encargado (demo)", Rol.Encargado),
        ("personal@demo.example", "Camarera (demo)", Rol.Personal),
    ];

    /// <summary>Guarda un usuario de un negocio con la contraseña indicada.</summary>
    public static async Task<Usuario> CrearUsuarioAsync(
        ReservasDbContext db,
        Guid negocioId,
        string email,
        string nombre,
        Rol rol,
        string contrasena,
        DateTimeOffset? creadoEn = null)
    {
        ArgumentNullException.ThrowIfNull(db);

        var usuario = Usuario.Crear(negocioId, email, nombre, rol, new HasherContrasenas().Hashear(contrasena), creadoEn ?? DateTimeOffset.UtcNow).Valor;
        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync();

        return usuario;
    }

    /// <summary>
    /// Crea el negocio de demostración por defecto y sus tres cuentas si todavía no existen. Se puede
    /// llamar en cada arranque: solo añade lo que falta.
    /// </summary>
    public static async Task SembrarSiHaceFaltaAsync(ReservasDbContext db, string contrasena = ContrasenaDemoPorDefecto)
    {
        ArgumentNullException.ThrowIfNull(db);

        var negocio = await db.Negocios.FirstOrDefaultAsync(n => n.Slug == SlugPorDefecto);
        negocio ??= (await CrearNegocioAsync(db)).Negocio;

        if (!await db.Usuarios.IgnoreQueryFilters().AnyAsync(u => u.NegocioId == negocio.Id))
        {
            foreach (var (email, nombre, rol) in CuentasDemo)
            {
                await CrearUsuarioAsync(db, negocio.Id, email, nombre, rol, contrasena);
            }
        }
    }
}
