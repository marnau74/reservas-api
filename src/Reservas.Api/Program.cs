using System.Text.Json;
using System.Text.Json.Serialization;

using FluentValidation;

using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Reservas.Api;
using Reservas.Api.Contratos;
using Reservas.Api.Endpoints;
using Reservas.Api.Idempotencia;
using Reservas.Api.Limites;
using Reservas.Api.Seguridad;
using Reservas.Api.Tareas;
using Reservas.Api.Validacion;
using Reservas.Aplicacion;
using Reservas.Aplicacion.Correos;
using Reservas.Infraestructura;
using Reservas.Infraestructura.Persistencia;

var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry, health checks, resiliencia y descubrimiento de servicios (Aspire).
builder.AddServiceDefaults();

// PostgreSQL: la cadena de conexión «reservas» la inyecta Aspire en local y el entorno en
// producción. El contexto es uno por petición (no un «pool» de contextos reutilizados) porque lleva
// el negocio de la sesión, y un contexto reutilizado podría arrastrar el de la petición anterior.
// Enrich añade a ese contexto los reintentos, las trazas y el health check de la base de datos.
builder.Services.AddDbContext<ReservasDbContext>((servicios, opciones) =>
{
    opciones.UseNpgsql(servicios.GetRequiredService<IConfiguration>().GetConnectionString("reservas"));
    OpcionesReservas.Configurar(opciones);
});
builder.EnrichNpgsqlDbContext<ReservasDbContext>();

builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddSeguridad(builder.Configuration, builder.Environment.IsDevelopment());
builder.Services.AddInfraestructura(builder.Configuration);

var opcionesCorreo = builder.Configuration.GetSection("Correo").Get<OpcionesCorreo>() ?? new OpcionesCorreo();
if (!opcionesCorreo.UrlGestion.Contains(OpcionesCorreo.MarcaCodigo, StringComparison.Ordinal))
{
    throw new InvalidOperationException($"«Correo:UrlGestion» debe contener {OpcionesCorreo.MarcaCodigo}, que se sustituye por el código de cada reserva.");
}

builder.Services.AddAplicacion(correo: opcionesCorreo);

// Envío de correos y mantenimiento en segundo plano. Se pueden apagar por instancia.
var opcionesTareas = builder.Configuration.GetSection(OpcionesTareas.Seccion).Get<OpcionesTareas>() ?? new OpcionesTareas();
builder.Services.AddSingleton(opcionesTareas);
if (opcionesTareas.Activas)
{
    builder.Services.AddHostedService<ProcesadorCorreosServicio>();
    builder.Services.AddHostedService<MantenimientoServicio>();
}

builder.Services.Configure<OpcionesPublicas>(builder.Configuration.GetSection(OpcionesPublicas.Seccion));
builder.Services.AddScoped<IValidator<SolicitudReservaDto>, ValidadorSolicitudReserva>();
builder.Services.AddScoped<IValidator<ConsultaDisponibilidadDto>, ValidadorConsultaDisponibilidad>();
builder.Services.AddScoped<IValidator<LoginDto>, ValidadorLogin>();
builder.Services.AddScoped<IValidator<RefrescoDto>, ValidadorRefresco>();
builder.Services.AddScoped<IValidator<CrearUsuarioDto>, ValidadorCrearUsuario>();
builder.Services.AddScoped<IValidator<ConsultaAgendaDto>, ValidadorConsultaAgenda>();
builder.Services.AddScoped<IValidator<CrearSalaDto>, ValidadorCrearSala>();
builder.Services.AddScoped<IValidator<CrearMesaDto>, ValidadorCrearMesa>();
builder.Services.AddScoped<IValidator<CrearHorarioDto>, ValidadorCrearHorario>();
builder.Services.AddScoped<IValidator<CrearCierreDto>, ValidadorCrearCierre>();
builder.Services.AddLimitesPeticiones(builder.Configuration);

builder.Services.ConfigureHttpJsonOptions(opciones =>
{
    // Los estados y turnos se escriben con su nombre («pendiente», «cena»), no con un número.
    opciones.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});

// Detrás de un proxy inverso (Render, Caddy…) la IP real del cliente llega en X-Forwarded-For.
// Solo se confía en esa cabecera si se pide expresamente: si la API está expuesta directamente,
// cualquiera podría falsearla para saltarse el límite de peticiones.
if (builder.Configuration.GetValue<bool>("Proxy:ConfiarEnCabecerasReenviadas"))
{
    builder.Services.Configure<ForwardedHeadersOptions>(opciones =>
    {
        opciones.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        opciones.KnownIPNetworks.Clear();
        opciones.KnownProxies.Clear();
    });
}

// Una petición mal formada (JSON roto, tipos que no encajan) es un 400 en todos los entornos.
// Por defecto, en desarrollo se lanza una excepción en lugar de responder, y acaba en un 500.
builder.Services.Configure<RouteHandlerOptions>(opciones => opciones.ThrowOnBadRequest = false);

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

if (builder.Configuration.GetValue<bool>("Proxy:ConfiarEnCabecerasReenviadas"))
{
    app.UseForwardedHeaders();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<IdempotenciaMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // En desarrollo la base de datos se crea y se actualiza al arrancar. En producción las
    // migraciones se aplican como un paso propio del despliegue, no desde la API.
    await using var ambito = app.Services.CreateAsyncScope();
    var db = ambito.ServiceProvider.GetRequiredService<ReservasDbContext>();
    await db.Database.MigrateAsync();

    // Un negocio ficticio para poder probar la API a mano. Los tests lo desactivan para controlar sus datos.
    if (builder.Configuration.GetValue("Desarrollo:SembrarDatosDemo", true))
    {
        await SembradorDemo.SembrarSiHaceFaltaAsync(db, builder.Configuration["Desarrollo:ContrasenaDemo"] ?? SembradorDemo.ContrasenaDemoPorDefecto);
    }
}

// /health (listo para recibir tráfico) y /alive (el proceso responde).
app.MapDefaultEndpoints();
app.MapEndpointsPublicos();
app.MapEndpointsPersonal();

app.Run();

/// <summary>Visible para los tests de integración (WebApplicationFactory).</summary>
public partial class Program;
