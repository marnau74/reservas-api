# Reservas API

API de reservas para bares y restaurantes, en .NET 10. Varios negocios en la misma API,
disponibilidad por franjas, reservas con confirmación por correo y gestión del día a día
del servicio.

> En construcción.

## El problema central

Dos personas no pueden reservar la misma mesa a la misma hora, aunque lleguen a la vez.
La garantía no está solo en el código: la da PostgreSQL con una restricción de exclusión
sobre los intervalos de cada mesa, y un test lanza reservas simultáneas para demostrarlo.

## Stack

.NET 10 · ASP.NET Core (minimal APIs) · EF Core · PostgreSQL · .NET Aspire · OpenTelemetry ·
xUnit v3 · Testcontainers

## Cómo ejecutarlo

Requiere el SDK de .NET 10 y Docker.

```bash
dotnet run --project src/Reservas.AppHost   # PostgreSQL + API, con el panel de Aspire
dotnet test --solution Reservas.slnx        # tests
```

## Estructura

```
src/
  Reservas.Dominio/          entidades y reglas de negocio, sin dependencias
  Reservas.Aplicacion/       casos de uso
  Reservas.Infraestructura/  EF Core, correo, tareas en segundo plano
  Reservas.Api/              endpoints HTTP
  Reservas.AppHost/          entorno local con .NET Aspire
  Reservas.ServiceDefaults/  observabilidad, health checks y resiliencia
tests/                       dominio, integración (API) y arquitectura
```

## Licencia

[MIT](LICENSE)
