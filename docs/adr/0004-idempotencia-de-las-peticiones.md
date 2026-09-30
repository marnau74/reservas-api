# 0004 · Idempotencia de las peticiones con `Idempotency-Key`

- **Estado:** aceptada
- **Fecha:** 2026-09-30

## Contexto

Un cliente móvil pulsa «reservar» y la conexión se corta antes de recibir la respuesta. No sabe
si la reserva se hizo. Si reintenta, puede crear una segunda reserva; si no lo hace, puede
quedarse sin mesa creyendo que la tiene. Con el reintento automático de muchos clientes HTTP, y
con usuarios que pulsan dos veces, pasa más de lo que parece.

La API tiene que permitir **reintentar con seguridad** una operación que crea algo.

## Decisión

El cliente envía en cada operación de creación una cabecera `Idempotency-Key` con una clave única
(entre 8 y 64 caracteres: letras, números, guiones). El servidor guarda qué hizo con cada clave:

| Situación | Respuesta |
|---|---|
| Primera vez que se ve la clave | Se procesa la petición y **se guarda su respuesta** |
| La misma petición con la misma clave, ya completada | Se **devuelve la respuesta guardada** (cabecera `Idempotency-Replayed: true`); no se ejecuta nada |
| La misma clave mientras la primera aún se procesa | `409` `idempotencia.en_curso`: que reintente en unos segundos |
| La misma clave con una petición distinta | `422` `idempotencia.clave_reutilizada`: es un error del cliente |
| Falta la cabecera, o tiene formato inválido | `400` |

Detalles de diseño:

- **La «misma petición»** se identifica con la huella SHA-256 del método, la ruta y el cuerpo. La
  ruta incluye el negocio, así que una clave no se puede reutilizar en otro negocio.
- **Quién gana** cuando llegan varias peticiones con la misma clave a la vez lo decide la clave
  primaria de una tabla de PostgreSQL (`claves_idempotencia`): solo una puede insertarla. No hay
  bloqueos en la aplicación, así que funciona con varias instancias de la API.
- **Se guardan todas las respuestas excepto los errores 5xx.** Un error de validación o de
  conflicto es una respuesta legítima que se repite igual. Un fallo del servidor no debe quedar
  fijado: se libera la clave para que el cliente pueda reintentar. Lo mismo si el procesamiento
  lanza una excepción.
- **Una petición «en curso» que lleva más de dos minutos sin cambios se da por abandonada** (el
  proceso pudo morir a mitad) y otra petición puede retomarla, con una actualización condicional
  para que solo una lo consiga.
- **Es un middleware, no un filtro del endpoint.** Guarda la respuesta HTTP tal como se envió
  (estado, tipo de contenido, `Location` y cuerpo), sea cual sea el endpoint. Se activa con una
  marca en los endpoints que lo necesitan.

## Alternativas descartadas

- **Solo confiar en la restricción de exclusión de las mesas (ADR 0003):** evita solapar mesas,
  pero un reintento de una reserva ya creada devolvería «mesa ocupada» (por sí misma) en lugar de
  la reserva original.
- **Clave natural (cliente + hora):** un mismo cliente puede querer dos reservas a la misma hora
  para grupos distintos, y no cubre otras operaciones.
- **Guardar las claves en memoria o en Redis:** no sobreviven a un reinicio o añaden un servicio;
  PostgreSQL ya está y la fila de la clave no necesita más que una tabla.
- **Un filtro de endpoint que serializa el resultado:** habría que reconstruir cabeceras y cuerpo
  a partir de cada tipo de resultado; el middleware ve la respuesta ya escrita.

## Consecuencias

- Una petición rechazada por validación queda ligada a su clave: para corregirla hay que usar una
  clave nueva. Es lo habitual en las APIs con idempotencia y hay un test que lo documenta.
- La tabla crece con cada petición. Hace falta purgar las claves antiguas (la respuesta guardada
  solo sirve mientras el cliente pueda reintentar, unas 24 horas). Está previsto como tarea
  programada en la fase 5, y hay un índice por fecha de actualización para hacerlo sin recorrer la
  tabla.
- Los tests del middleware con un almacén falso cubren lo que con un servidor real es difícil de
  provocar (un 5xx, una excepción a mitad); los de integración cubren las peticiones simultáneas
  reales.
