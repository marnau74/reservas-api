# Changelog

Formato basado en [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/); versionado
[semántico](https://semver.org/lang/es/).

## [Sin publicar]

### Añadido
- Correos al cliente: solicitud de confirmación, confirmación, cancelación y recordatorio 24 horas
  antes. Se guardan en una bandeja de salida en la misma transacción que la reserva y los envía un
  proceso en segundo plano con reintentos espaciados (1 min, 5 min, 30 min, 2 h, 12 h) y abandono
  tras seis fallos; varias instancias pueden vaciar la bandeja a la vez sin duplicar (ADR 0006).
- Envío por SMTP con MailKit y Mailpit en el entorno local de Aspire (bandeja en el puerto 8025).
- Tareas programadas: caducar reservas sin confirmar a los 30 minutos, programar recordatorios,
  anonimizar clientes de reservas de más de 24 meses y purgar claves de idempotencia, tokens de
  renovación y correos antiguos.
- Derecho de supresión: `DELETE /api/v1/reservas/gestion/{codigo}` borra los datos personales del
  cliente de una reserva que ya no está activa.
- Tests: bandeja transaccional, reparto entre ocho procesos sin duplicados, reintentos con el reloj
  simulado, tareas contra PostgreSQL, envío real contra Mailpit en un contenedor, recorrido del
  cliente solo con lo que recibe por correo y los servicios en segundo plano arrancados con la API.

### Cambiado
- **El código de gestión ya no se devuelve al crear una reserva** (`Publico:MostrarCodigoGestion` es
  `false` por defecto): solo llega en el enlace del correo, lo que hace que confirmar demuestre que
  el cliente controla ese correo.
- `AddInfraestructura` recibe la configuración (sección `Correo`); sin servidor, los correos solo se
  anotan en el registro, sin su contenido.
- Parte privada de la API para el personal del negocio: agenda del día, reservas apuntadas por
  teléfono o en persona (nacen confirmadas), y llegada, completar, no presentada y cancelar.
- Configuración del local por su encargado (salas, mesas, horarios y cierres) y gestión de usuarios
  por su propietario.
- Sesiones con access token JWT de 15 minutos y token de renovación de un solo uso: reutilizar uno
  ya gastado cierra todas las sesiones de esa persona. Contraseñas con el hasher de ASP.NET Core
  Identity y bloqueo de la cuenta tras 5 fallos seguidos (ADR 0005).
- Tres roles acumulativos (personal, encargado, propietario) con políticas de autorización.
- Aislamiento entre negocios con dos defensas independientes: filtro global de EF Core por
  `negocio_id` y comprobación explícita en los casos de uso. Un identificador de otro negocio da 404.
- Límite de peticiones más estricto para el inicio de sesión y la renovación.
- Cuentas de demostración en desarrollo (propietario, encargado y personal del negocio ficticio).
- Tests: dominio de usuarios, filtro por negocio contra PostgreSQL, consumo atómico de tokens con 20
  peticiones a la vez, flujos de autenticación, autorización por rol y aislamiento entre dos
  negocios, y un test que recorre todas las rutas de la API para que ninguna de la parte privada se
  quede sin sesión ni sin límite de peticiones.

### Cambiado
- La huella de una petición idempotente incluye ahora quién la hace: otra persona no puede reutilizar
  una clave ajena para leer la respuesta guardada.
- El contexto de base de datos es uno por petición en lugar de un *pool* de contextos reutilizados,
  porque lleva el negocio de la sesión.
- Se omite `PATCH` de reservas, previsto en la guía: una reserva es inmutable y se corrige
  cancelándola y apuntando otra.
- API pública con contrato OpenAPI: datos de un negocio, disponibilidad de un día para un grupo,
  crear una reserva y consultarla, confirmarla o cancelarla con el código secreto de su enlace.
- Casos de uso en la capa de aplicación, sin librerías de mediadores. Al reservar, si otra
  petición simultánea se queda con la mesa elegida, se recalcula y se prueba con otra libre.
- Idempotencia con `Idempotency-Key` guardada en PostgreSQL: repetir una petición devuelve la
  misma respuesta y no crea otra reserva (ADR 0004).
- Errores estándar `application/problem+json` (RFC 9457) con un código de negocio estable en
  `code`, cada uno con su estado HTTP; validación de la forma de las peticiones con
  FluentValidation, con los errores agrupados por campo.
- Límite de peticiones por IP, con políticas distintas para lecturas y escrituras, `429` con
  `Retry-After` y soporte opcional para proxies inversos.
- Datos de demostración para el entorno local (negocio ficticio «bar-la-plaza»).
- Tests: casos de uso con repositorios falsos, middleware de idempotencia, repositorios y almacén
  de idempotencia contra PostgreSQL, y el flujo completo de un cliente por HTTP, incluidas 20
  peticiones simultáneas por la misma hora.

### Corregido
- Las políticas de reserva por defecto eran un objeto compartido y EF Core no admite que dos
  negocios compartan uno: ahora cada negocio tiene su propia copia.
- Persistencia con EF Core y PostgreSQL 17: modelo del dominio (negocios, salas, mesas, horarios,
  cierres y reservas), migraciones y nombres en snake_case. El tramo de cada reserva se guarda
  como `tstzrange` y las mesas como `uuid[]`.
- **Restricción de exclusión** que impide reservar dos veces la misma mesa a la misma hora, y
  cola por mesa para que las peticiones simultáneas no se interbloqueen (ADR 0003).
- Repositorio de reservas con concurrencia optimista (`xmin`) que traduce los conflictos de la
  base de datos a errores de negocio (`reserva.mesa_ocupada`, `reserva.conflicto_concurrencia`).
- Tests de infraestructura contra un PostgreSQL real con Testcontainers, cada test en su propia
  base de datos, incluidas 20 peticiones simultáneas por la misma mesa.
- La API se conecta a PostgreSQL (integración de Aspire, con health check de la base de datos)
  y aplica las migraciones al arrancar en desarrollo.
- Entidad `Sala` del dominio.
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
