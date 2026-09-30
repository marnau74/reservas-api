# Changelog

Formato basado en [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/); versionado
[semántico](https://semver.org/lang/es/).

## [Sin publicar]

### Añadido
- Dominio de las reservas, sin dependencias de frameworks:
  - Negocio con zona horaria y políticas de reserva (antelación, días máximos, comensales
    online y duración), y mesas, horarios y cierres del local.
  - Reserva con máquina de estados (pendiente, confirmada, sentada, completada, cancelada y
    no presentada), caducidad de las pendientes y margen de cortesía para no presentados.
  - Cálculo de disponibilidad por franjas y asignación de mesas: la más ajustada al grupo o,
    si no cabe, la combinación de menos mesas de una sala.
  - Conversión entre hora local y UTC con reglas para los cambios de hora.
- ADR 0001 (errores con `Resultado`) y 0002 (tiempo UTC y hora local).
- 148 tests del dominio, comprobados con pruebas de mutación manuales sobre las reglas críticas.
- Solución .NET 10 por capas (dominio, aplicación, infraestructura y API), entorno local
  con .NET Aspire y PostgreSQL, y observabilidad con OpenTelemetry.
- Normas comunes: nulos estrictos, avisos como errores, analizadores recomendados y
  versiones de NuGet centralizadas.
- Tipo `Resultado` para errores de negocio sin excepciones.
- Tests de dominio, de integración (health checks y ProblemDetails) y de arquitectura
  (dependencias entre capas).
- CI con formato, compilación, tests y control de privacidad.
