using System.Net.Http.Headers;
using System.Net.Http.Json;

using Reservas.Api.Contratos;
using Reservas.Dominio.Personal;
using Reservas.Infraestructura.Persistencia;
using Reservas.Tests.Comunes;

using Shouldly;

namespace Reservas.Api.Tests;

/// <summary>
/// La API con dos negocios de prueba, cada uno con su propietario, su encargado y su personal.
/// Sirve para comprobar tanto lo que cada rol puede hacer como que un negocio no ve al otro.
/// </summary>
public class ApiConPersonal(ServidorPostgres servidor) : ApiConBaseDeDatos(servidor)
{
    public const string Contrasena = "Contrasena-Segura-1";

    // Negocio A: «bar-la-plaza» (dos mesas). Negocio B: «una-mesa» (una sola mesa).
    public const string PropietarioA = "propietario.a@example.com";
    public const string EncargadoA = "encargado.a@example.com";
    public const string PersonalA = "personal.a@example.com";
    public const string PropietarioB = "propietario.b@example.com";
    public const string EncargadoB = "encargado.b@example.com";
    public const string PersonalB = "personal.b@example.com";

    protected override async Task SembrarAsync(ReservasDbContext db)
    {
        var a = await SembradorDemo.CrearNegocioAsync(db, NegocioDosMesas);
        var b = await SembradorDemo.CrearNegocioAsync(db, NegocioUnaMesa, [(1, 4, false)]);

        var creado = Reloj.GetUtcNow();
        await SembradorDemo.CrearUsuarioAsync(db, a.Negocio.Id, PropietarioA, "Propietaria A", Rol.Propietario, Contrasena, creado);
        await SembradorDemo.CrearUsuarioAsync(db, a.Negocio.Id, EncargadoA, "Encargado A", Rol.Encargado, Contrasena, creado);
        await SembradorDemo.CrearUsuarioAsync(db, a.Negocio.Id, PersonalA, "Personal A", Rol.Personal, Contrasena, creado);
        await SembradorDemo.CrearUsuarioAsync(db, b.Negocio.Id, PropietarioB, "Propietaria B", Rol.Propietario, Contrasena, creado);
        await SembradorDemo.CrearUsuarioAsync(db, b.Negocio.Id, EncargadoB, "Encargado B", Rol.Encargado, Contrasena, creado);
        await SembradorDemo.CrearUsuarioAsync(db, b.Negocio.Id, PersonalB, "Personal B", Rol.Personal, Contrasena, creado);
    }
}

/// <summary>Herramientas para probar la parte privada de la API como lo haría el personal de un negocio.</summary>
public abstract class PruebaPersonal(ApiConBaseDeDatos api) : PruebaApi(api)
{
    protected async Task<HttpResponseMessage> LoginAsync(string email, string contrasena = ApiConPersonal.Contrasena) =>
        await Api.CrearCliente().PostAsJsonAsync("/api/v1/auth/login", new LoginDto(email, contrasena), Cancelacion);

    protected async Task<SesionRespuesta> IniciarSesionAsync(string email, string contrasena = ApiConPersonal.Contrasena)
    {
        using var respuesta = await LoginAsync(email, contrasena);
        respuesta.EnsureSuccessStatusCode();
        return (await respuesta.Content.ReadFromJsonAsync<SesionRespuesta>(Cancelacion))!;
    }

    /// <summary>Un cliente HTTP que ya lleva la sesión de esa persona.</summary>
    protected async Task<HttpClient> ClienteDeAsync(string email)
    {
        var sesion = await IniciarSesionAsync(email);
        return ClienteConToken(sesion.TokenAcceso);
    }

    protected HttpClient ClienteConToken(string token)
    {
        var cliente = Api.CrearCliente();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return cliente;
    }

    protected static async Task<HttpResponseMessage> EnviarAsync(
        HttpClient cliente,
        HttpMethod metodo,
        string ruta,
        object? cuerpo = null,
        string? claveIdempotencia = null)
    {
        using var peticion = new HttpRequestMessage(metodo, ruta);

        if (cuerpo is not null)
        {
            peticion.Content = JsonContent.Create(cuerpo);
        }

        if (claveIdempotencia is not null)
        {
            peticion.Headers.Add("Idempotency-Key", claveIdempotencia);
        }

        return await cliente.SendAsync(peticion, Cancelacion);
    }

    protected static Task<HttpResponseMessage> ObtenerAsync(HttpClient cliente, string ruta) => EnviarAsync(cliente, HttpMethod.Get, ruta);

    protected static Task<HttpResponseMessage> PostAsync(HttpClient cliente, string ruta, object? cuerpo = null) =>
        EnviarAsync(cliente, HttpMethod.Post, ruta, cuerpo);

    protected static Task<HttpResponseMessage> BorrarAsync(HttpClient cliente, string ruta) => EnviarAsync(cliente, HttpMethod.Delete, ruta);

    protected static async Task<T> LeerAsync<T>(HttpResponseMessage respuesta) =>
        (await respuesta.Content.ReadFromJsonAsync<T>(Cancelacion))!;

    /// <summary>Apunta una reserva como personal y devuelve su respuesta ya comprobada.</summary>
    protected static async Task<ReservaGestionRespuesta> ApuntarReservaAsync(
        HttpClient cliente,
        DateOnly fecha,
        string hora = "21:00",
        int comensales = 2)
    {
        var solicitud = new SolicitudReservaDto(Formatos.DeFecha(fecha), hora, comensales, new ClienteDto("Luis Gómez", NuevoEmail(), "611 222 333"));

        using var respuesta = await EnviarAsync(cliente, HttpMethod.Post, "/api/v1/gestion/reservas", solicitud, Guid.NewGuid().ToString("N"));
        respuesta.StatusCode.ShouldBe(System.Net.HttpStatusCode.Created);

        return await LeerAsync<ReservaGestionRespuesta>(respuesta);
    }
}
