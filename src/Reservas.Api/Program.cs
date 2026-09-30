var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry, health checks, resiliencia y descubrimiento de servicios (Aspire).
builder.AddServiceDefaults();

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// /health (listo para recibir tráfico) y /alive (el proceso responde).
app.MapDefaultEndpoints();

app.Run();

/// <summary>Visible para los tests de integración (WebApplicationFactory).</summary>
public partial class Program;
