using Microsoft.EntityFrameworkCore;

using Reservas.Aplicacion.Abstracciones;
using Reservas.Infraestructura.Persistencia;

var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry, health checks, resiliencia y descubrimiento de servicios (Aspire).
builder.AddServiceDefaults();

// PostgreSQL: la cadena de conexión «reservas» la inyecta Aspire en local y el entorno en
// producción. La integración añade también reintentos, trazas y un health check de la base de datos.
builder.AddNpgsqlDbContext<ReservasDbContext>("reservas", configureDbContextOptions: opciones => OpcionesReservas.Configurar(opciones));
builder.Services.AddScoped<IRepositorioReservas, RepositorioReservas>();

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // En desarrollo la base de datos se crea y se actualiza al arrancar. En producción las
    // migraciones se aplican como un paso propio del despliegue, no desde la API.
    await using var ambito = app.Services.CreateAsyncScope();
    await ambito.ServiceProvider.GetRequiredService<ReservasDbContext>().Database.MigrateAsync();
}

// /health (listo para recibir tráfico) y /alive (el proceso responde).
app.MapDefaultEndpoints();

app.Run();

/// <summary>Visible para los tests de integración (WebApplicationFactory).</summary>
public partial class Program;
