# Reservas API

API de reservas para bares y restaurantes, en .NET 10. Varios negocios en la misma API,
disponibilidad por franjas, reservas con confirmación por correo y gestión del día a día
del servicio.

> En construcción.

## El problema central

Dos personas no pueden reservar la misma mesa a la misma hora, aunque lleguen a la vez.
La garantía no está solo en el código: la da PostgreSQL con una restricción de exclusión
sobre los intervalos de cada mesa, y un test lanza reservas simultáneas para demostrarlo.

## Lo que hay hecho

El **dominio**, que es donde vive la lógica de negocio y no depende de ningún framework:

- **Reservas con estados:** pendiente → confirmada → sentada → completada, o cancelada / no
  presentada. Las transiciones son métodos de la propia reserva y devuelven un resultado, no
  lanzan excepciones ([ADR 0001](docs/adr/0001-resultado-en-lugar-de-excepciones.md)).
- **Disponibilidad:** qué horas se pueden reservar un día para un grupo, aplicando cierres,
  antelación mínima y máxima, límite de comensales online y las mesas ya ocupadas.
- **Asignación de mesas:** la más ajustada al grupo; si no cabe en ninguna, la combinación
  de menos mesas de una misma sala.
- **Cambios de hora:** el 29 de marzo no existen las 02:30 y el 25 de octubre ocurren dos
  veces; el dominio decide qué hacer y los tests lo demuestran
  ([ADR 0002](docs/adr/0002-tiempo-utc-y-hora-local.md)).

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
