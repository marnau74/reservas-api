# Changelog

Formato basado en [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/); versionado
[semántico](https://semver.org/lang/es/).

## [Sin publicar]

### Añadido
- Solución .NET 10 por capas (dominio, aplicación, infraestructura y API), entorno local
  con .NET Aspire y PostgreSQL, y observabilidad con OpenTelemetry.
- Normas comunes: nulos estrictos, avisos como errores, analizadores recomendados y
  versiones de NuGet centralizadas.
- Tipo `Resultado` para errores de negocio sin excepciones.
- Tests de dominio, de integración (health checks y ProblemDetails) y de arquitectura
  (dependencias entre capas).
- CI con formato, compilación, tests y control de privacidad.
