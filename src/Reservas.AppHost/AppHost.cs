// Entorno local completo con un solo comando: dotnet run --project src/Reservas.AppHost
// Levanta PostgreSQL y Mailpit en contenedores y la API conectada a ambos, con el panel de
// Aspire (trazas, métricas y logs) en el navegador. Los correos que envía la API no salen a
// internet: se leen en la bandeja de Mailpit, en http://localhost:8025.

var builder = DistributedApplication.CreateBuilder(args);

// PostgreSQL 17: la misma versión que usan los tests y la demo pública (Neon).
var postgres = builder.AddPostgres("postgres")
    .WithImageTag("17")
    .WithDataVolume("reservas-postgres")
    .WithLifetime(ContainerLifetime.Persistent);

var baseDeDatos = postgres.AddDatabase("reservas");

// Mailpit: un servidor de correo de pruebas que guarda todo lo que recibe y lo enseña en una web.
var mailpit = builder.AddContainer("mailpit", "axllent/mailpit")
    .WithEndpoint(port: 1025, targetPort: 1025, name: "smtp")
    .WithHttpEndpoint(port: 8025, targetPort: 8025, name: "bandeja");

var smtp = mailpit.GetEndpoint("smtp");

builder.AddProject<Projects.Reservas_Api>("api")
    .WithReference(baseDeDatos)
    .WaitFor(baseDeDatos)
    .WithEnvironment("Correo__Servidor", smtp.Property(EndpointProperty.Host))
    .WithEnvironment("Correo__Puerto", smtp.Property(EndpointProperty.Port))
    .WaitFor(mailpit)
    .WithHttpHealthCheck("/health");

builder.Build().Run();
