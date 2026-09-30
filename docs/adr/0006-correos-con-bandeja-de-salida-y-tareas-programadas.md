# 0006 · Correos con bandeja de salida y tareas programadas

- **Estado:** aceptada
- **Fecha:** 2026-09-30

## Contexto

Una reserva por internet tiene que avisar al cliente: pedirle que la confirme, confirmarle que está
hecha, avisarle si se cancela y recordársela el día antes. Enviar un correo dentro de la petición
que crea la reserva tiene dos fallos:

- **El servidor de correo puede estar caído** o ir lento. Si el envío falla después de guardar la
  reserva, el cliente no recibe el enlace para confirmarla y la mesa queda retenida sin que nadie
  lo sepa. Si falla antes, se pierde una reserva válida por un problema ajeno.
- **No hay transacción entre PostgreSQL y un servidor SMTP.** Siempre hay una ventana en la que
  una cosa ha ocurrido y la otra no.

Además, hay trabajo que no lo dispara ninguna petición: las reservas sin confirmar deben caducar,
los recordatorios deben salir a su hora y los datos personales no pueden guardarse para siempre.

## Decisión

### Bandeja de salida (*outbox*)

El correo se guarda en la tabla `correos_pendientes` **en la misma transacción** que la reserva o
el cambio de estado que lo provoca. O ocurre todo o no ocurre nada: no puede haber una reserva sin
su correo, ni el correo de una reserva que perdió la carrera por la mesa. Un test lanza 20 peticiones
simultáneas por la única mesa y comprueba que hay exactamente un correo por reserva creada.

Un proceso en segundo plano vacía la bandeja cada 10 segundos:

- **Toma un lote con un solo `UPDATE … WHERE id IN (SELECT … FOR UPDATE SKIP LOCKED)`** que además
  aplaza cinco minutos el próximo intento de esos correos. Así varias instancias de la API pueden
  vaciar la bandeja a la vez sin enviar dos veces lo mismo, y no se mantiene ningún bloqueo de base
  de datos mientras se habla con el servidor de correo, que puede tardar. Es una *reserva con
  caducidad*: si el proceso muere a mitad, el correo vuelve a estar disponible al pasar los cinco minutos.
- **Cada fallo se anota y espacia el reintento:** 1 minuto, 5, 30, 2 horas y 12 horas. Tras seis
  fallos el correo se **abandona** y queda visible, con su último error, para revisarlo a mano.
- El resultado se guarda tras cada correo, para no reenviar el que ya salió si el proceso muere.
- **Un fallo no bloquea a los demás:** un correo con una dirección rechazada no impide enviar el resto.

La garantía es *al menos una vez*: si el proceso muere justo entre enviar y guardar, el correo
puede salir dos veces. Se prefiere eso a perder uno; un correo de confirmación repetido es inofensivo.

### Qué correos hay

| Cuándo | Correo |
|---|---|
| Reserva por internet | **Solicitud:** pide confirmarla, con el enlace y el plazo de 30 minutos |
| Reserva apuntada por el personal, o confirmada por el cliente | **Confirmación**, con el enlace para cancelar |
| Reserva cancelada (por el cliente o por el personal) | **Cancelación**, sin enlace |
| 24 horas antes de una reserva confirmada | **Recordatorio**, una sola vez |

Son de texto plano, en español, con las horas del local (no UTC) y los nombres de días y meses
escritos en el código: un contenedor mínimo suele traer solo la cultura invariante y saldrían en inglés.

### El código de gestión ya solo llega por correo

Hasta ahora la respuesta de crear una reserva devolvía el código secreto para poder completar el flujo
sin correo. Ahora **no se devuelve** (`Publico:MostrarCodigoGestion` es `false` por defecto): el
cliente lo recibe en el enlace del correo, y confirmar la reserva demuestra que controla ese correo.
Sin esto, cualquiera podría reservar con el correo de otra persona y confirmarlo él mismo.

### Tareas programadas

Un segundo servicio en segundo plano ejecuta cada minuto:

- **Caducar reservas pendientes** pasados 30 minutos, liberando sus mesas.
- **Programar recordatorios** de las reservas confirmadas que empiezan entre dentro de 1 y 24 horas.
  Las que empiezan en menos de una hora no lo reciben: ya no da tiempo a nada.

Y cada hora:

- **Anonimizar clientes:** las reservas ya no activas, creadas hace más de **24 meses**, pierden
  nombre, correo y teléfono. La reserva se conserva (cuentas del negocio), la persona no.
- **Purgar** claves de idempotencia (24 h), tokens de renovación (7 días) y correos enviados (30 días).

Todas son **idempotentes y seguras con varias instancias**: cada reserva se cambia con control de
concurrencia (`xmin`), así que si dos instancias intentan programar el mismo recordatorio solo una
lo consigue. Un test lanza seis a la vez y comprueba que sale un único correo. Cada tarea va por
separado: que una falle no impide las demás. Todo usa `TimeProvider`, así que los tests mueven el
reloj en lugar de esperar.

No se usa Quartz ni Hangfire: para cuatro tareas periódicas, un `BackgroundService` con
`PeriodicTimer` y la base de datos como fuente de verdad basta, y no añade dependencias ni tablas propias.

### Derecho de supresión (RGPD)

`DELETE /api/v1/reservas/gestion/{codigo}` borra los datos personales del cliente de esa reserva. Solo
con la reserva ya no activa: no se puede borrar el contacto de quien va a venir. Repetirlo no es un
error. El derecho de acceso lo cubre `GET` sobre la misma ruta, que ya devuelve todo lo que se guarda
del cliente. El plazo de conservación de 24 meses se cuenta desde que se hizo la reserva.

## Alternativas descartadas

- **Enviar el correo dentro de la petición:** el fallo del servidor de correo se convierte en fallo de
  la reserva, y no hay forma de hacerlo atómico.
- **Una cola externa (RabbitMQ, un servicio de colas):** otro servicio que operar, para un volumen de
  unos pocos correos por minuto. PostgreSQL ya está, es transaccional y `SKIP LOCKED` da una cola correcta.
- **Bloquear las filas durante todo el envío (`FOR UPDATE` en una transacción larga):** retendría
  conexiones y bloqueos mientras el servidor SMTP tarda; con la reserva con caducidad no hace falta.
- **Hangfire o Quartz:** potentes, pero traen tablas, un panel y configuración propios.

## Consecuencias

- Un correo puede tardar hasta unos 10 segundos en salir, y minutos u horas si el servidor está caído.
- La tabla `correos_pendientes` crece y se purga a los 30 días. Los abandonados se ven con
  `WHERE abandonado`; queda pendiente un aviso o una métrica sobre ellos (fase 7).
- Sin servidor de correo configurado (`Correo:Servidor` vacío) los correos se anotan en el registro
  sin su contenido, que lleva enlaces secretos. En local, Aspire levanta Mailpit y los correos se leen
  en http://localhost:8025.
- La política de retención cuenta desde la creación de la reserva (que sí se puede indexar en SQL), no desde su fecha.
