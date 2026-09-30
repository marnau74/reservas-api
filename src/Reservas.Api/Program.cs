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
using Reservas.Api.Validacion;
using Reservas.Aplicacion;
using Reservas.Infraestructura;
using Reservas.Infraestructura.Persistencia;

var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry, health checks, resiliencia y descubrimiento de servicios (Aspire).
builder.AddServiceDefaults();

// PostgreSQL: la cadena de conexión «reservas» la inyecta Aspire en local y el entorno en
// producción. La integración añade también reintentos, trazas y un health check de la base de datos.
builder.AddNpgsqlDbContext<ReservasDbContext>("reservas", configureDbContextOptions: opciones => OpcionesReservas.Configurar(opciones));

builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddInfraestructura();
builder.Services.AddAplicacion();

builder.Services.Configure<OpcionesPublicas>(builder.Configuration.GetSection(OpcionesPublicas.Seccion));
builder.Services.AddScoped<IValidator<SolicitudReservaDto>, ValidadorSolicitudReserva>();
builder.Services.AddScoped<IValidator<ConsultaDisponibilidadDto>, ValidadorConsultaDisponibilidad>();
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
        await SembradorDemo.SembrarSiHaceFaltaAsync(db);
    }
}

// /health (listo para recibir tráfico) y /alive (el proceso responde).
app.MapDefaultEndpoints();
app.MapEndpointsPublicos();

app.Run();

/// <summary>Visible para los tests de integración (WebApplicationFactory).</summary>
public partial class Program;
