// Entorno local completo con un solo comando: dotnet run --project src/Reservas.AppHost
// Levanta PostgreSQL en un contenedor y la API conectada a él, con el panel de Aspire
// (trazas, métricas y logs) en el navegador.

var builder = DistributedApplication.CreateBuilder(args);

// PostgreSQL 17: la misma versión que usan los tests y la demo pública (Neon).
var postgres = builder.AddPostgres("postgres")
    .WithImageTag("17")
    .WithDataVolume("reservas-postgres")
    .WithLifetime(ContainerLifetime.Persistent);

var baseDeDatos = postgres.AddDatabase("reservas");

builder.AddProject<Projects.Reservas_Api>("api")
    .WithReference(baseDeDatos)
    .WaitFor(baseDeDatos)
    .WithHttpHealthCheck("/health");

builder.Build().Run();
