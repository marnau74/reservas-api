# Changelog

Formato basado en [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/); versionado
[semántico](https://semver.org/lang/es/).

## [Sin publicar]

### Corregido
- El bloqueo tras cinco contraseñas incorrectas se podía saltar con intentos simultáneos: cada uno leía
  el mismo contador y se perdían. Ahora cada intento se anota antes de comprobar la contraseña, con la
  fila de la cuenta bloqueada hasta guardarlo.
- Borrar los datos de un cliente (o anonimizarlos a los 24 meses) dejaba copias en la respuesta guardada
  para los reintentos y en los correos de la reserva. Ahora se borran en la misma transacción.
- El código de gestión de las reservas aparecía en las trazas (`url.path`): ahora se sustituye por
  `{codigo}`.
- En la parte privada, una petición sin sesión válida dejaba las consultas sin filtro de negocio. Ahora
  el filtro falla cerrado: no se ve ningún negocio.
- Una reserva que fallara siempre en el mantenimiento dejaba sin procesar a las que venían detrás.

### Cambiado
- Los listados de la parte privada también tienen límite de peticiones (antes solo las escrituras).

## [1.0.0] - 2026-09-30

### Añadido

**Base**
- Solución .NET 10 por capas (dominio, aplicación, infraestructura y API), entorno local con
  .NET Aspire y observabilidad con OpenTelemetry.
- Normas comunes: nulos estrictos, avisos como errores, analizadores recomendados y versiones de
  NuGet centralizadas. Tipo `Resultado` para errores de negocio sin excepciones (ADR 0001).
- CI con formato, compilación, tests y control de privacidad.

**Dominio**
- Negocio con zona horaria y políticas de reserva; mesas, salas, horarios y cierres.
- Reserva con máquina de estados (pendiente, confirmada, sentada, completada, cancelada y no
  presentada), caducidad de las pendientes y margen de cortesía para no presentados.
- Cálculo de disponibilidad por franjas y asignación de mesas: la más ajustada al grupo o, si no
  cabe, la combinación de menos mesas de una sala.
- Conversión entre hora local y UTC con reglas para los cambios de hora (ADR 0002).

**Persistencia**
- EF Core y PostgreSQL 17: migraciones, nombres en snake_case, el tramo de cada reserva como
  `tstzrange` y las mesas como `uuid[]`.
- **Restricción de exclusión** que impide reservar dos veces la misma mesa a la misma hora, y cola
  por mesa para que las peticiones simultáneas no se interbloqueen (ADR 0003).
- Concurrencia optimista (`xmin`) que traduce los conflictos de la base de datos a errores de negocio.

**API pública**
- Datos de un negocio, disponibilidad de un día para un grupo, y crear, consultar, confirmar y
  cancelar una reserva con el código secreto de su enlace, con contrato OpenAPI.
- Casos de uso en la capa de aplicación, sin librerías de mediadores. Al reservar, si otra petición
  simultánea se queda con la mesa elegida, se recalcula y se prueba con otra libre.
- Idempotencia con `Idempotency-Key` guardada en PostgreSQL: repetir una petición devuelve la
  misma respuesta y no crea otra reserva (ADR 0004).
- Errores estándar `application/problem+json` (RFC 9457) con un código de negocio estable, y
  validación con FluentValidation con los errores agrupados por campo.
- Límite de peticiones por IP con políticas distintas para lecturas, escrituras y autenticación.

**Parte privada**
- Agenda del día, reservas apuntadas por teléfono o en persona (nacen confirmadas), y llegada,
  completar, no presentada y cancelar. Configuración del local por su encargado y gestión de
  usuarios por su propietario.
- Sesiones con access token JWT de 15 minutos y token de renovación de un solo uso: reutilizar uno
  ya gastado cierra todas las sesiones de esa persona. Contraseñas con el hasher de ASP.NET Core
  Identity y bloqueo de la cuenta tras 5 fallos seguidos (ADR 0005).
- Tres roles acumulativos (personal, encargado, propietario).
- Aislamiento entre negocios con dos defensas independientes: filtro global de EF Core por
  `negocio_id` y comprobación explícita en los casos de uso. Un identificador de otro negocio da 404.

**Correos y tareas programadas**
- Correos al cliente: solicitud de confirmación, confirmación, cancelación y recordatorio 24 horas
  antes. Se guardan en una bandeja de salida en la misma transacción que la reserva y los envía un
  proceso en segundo plano con reintentos espaciados (1 min, 5 min, 30 min, 2 h, 12 h) y abandono
  tras seis fallos; varias instancias pueden vaciar la bandeja a la vez sin duplicar (ADR 0006).
- Envío por SMTP con MailKit y Mailpit en el entorno local de Aspire (bandeja en el puerto 8025).
- Tareas: caducar reservas sin confirmar a los 30 minutos, programar recordatorios, anonimizar
  clientes de reservas de más de 24 meses y purgar claves de idempotencia, tokens y correos antiguos.
- Derecho de supresión: `DELETE /api/v1/reservas/gestion/{codigo}` borra los datos personales del
  cliente de una reserva que ya no está activa.

**Despliegue**
- Imagen de Docker en dos fases (sin SDK en la imagen final, sin administrador), plano para Render
  (`render.yaml`) con la base de datos en Neon y guía con todas las variables (`docs/despliegue.md`,
  ADR 0007). Se probó la imagen real contra un PostgreSQL en contenedor.
- Fuera de desarrollo la API no arranca sin `Jwt:Clave` de 32 caracteres, con `Demo:Sembrar` sin
  contraseña propia o con un origen CORS o un enlace de gestión mal escritos.
- CORS restringido a los orígenes configurados (ninguno por defecto), cabeceras de seguridad
  (`nosniff`, CSP, `X-Frame-Options`, `Cache-Control: no-store`, HSTS), y el contrato OpenAPI solo se
  publica en desarrollo.
- Migraciones al arrancar y datos de demostración configurables (`BaseDeDatos:MigrarAlArrancar`,
  `Demo:Sembrar`), con unas reservas de ejemplo ficticias.
- `/health` y `/alive` también fuera de desarrollo, solo con el estado y sin detalles.

**Calidad**
- Tests de dominio, de casos de uso con repositorios falsos y de integración contra PostgreSQL y
  Mailpit reales (Testcontainers), incluidas peticiones simultáneas; las reglas críticas se han
  comprobado rompiéndolas a propósito.
- **Contrato OpenAPI guardado en el repositorio** (`tests/Reservas.Api.Tests/Contrato/openapi.v1.json`):
  un test falla ante cualquier cambio de rutas, parámetros o respuestas hasta que alguien lo revise
  y lo acepte.
- Reglas de arquitectura además de la dirección de las capas: los endpoints no conocen la
  infraestructura, la API solo toca EF Core en el arranque, los contratos no exponen tipos del
  dominio, los casos de uso son clases selladas, cada repositorio cumple un puerto y el dominio no
  tiene propiedades públicas modificables ni excepciones.
- Un test recorre todas las rutas y falla si una de la parte privada no exige sesión o si una que
  escribe no tiene límite de peticiones.
- CodeQL (`security-extended`, también semanal), revisión de paquetes vulnerables y en desuso en la
  CI, y Dependabot mensual.

### Cambiado
- **El código de gestión ya no se devuelve al crear una reserva** (`Publico:MostrarCodigoGestion` es
  `false` por defecto): solo llega en el enlace del correo, lo que hace que confirmar demuestre que
  el cliente controla ese correo.
- La huella de una petición idempotente incluye quién la hace: otra persona no puede reutilizar una
  clave ajena para leer la respuesta guardada.
- El contexto de base de datos es uno por petición en lugar de un *pool* de contextos reutilizados,
  porque lleva el negocio de la sesión.
- La CI ejecuta cada proyecto de tests por separado en lugar de `dotnet test` sobre la solución.
- Se omite `PATCH` de reservas, previsto en la guía: una reserva es inmutable y se corrige
  cancelándola y apuntando otra.

### Corregido
- Las políticas de reserva por defecto eran un objeto compartido y EF Core no admite que dos
  negocios compartan uno: ahora cada negocio tiene su propia copia.

[Sin publicar]: https://github.com/marnau74/reservas-api/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/marnau74/reservas-api/releases/tag/v1.0.0
