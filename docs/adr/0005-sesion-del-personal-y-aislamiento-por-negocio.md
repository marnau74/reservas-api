# 0005 · Sesión del personal y aislamiento entre negocios

- **Estado:** aceptada
- **Fecha:** 2026-09-30

## Contexto

Hasta ahora la API era pública: cualquiera reserva y gestiona *su* reserva con un código. La parte
privada es distinta: el personal de un local ve la agenda con los datos de sus clientes, configura
mesas y horarios y da de alta a sus compañeros. Hay dos riesgos serios:

1. **Que entre quien no debe** (contraseñas robadas o adivinadas, tokens robados).
2. **Que un negocio vea o toque los datos de otro.** Todos los negocios comparten la misma base de
   datos, así que un solo `WHERE negocio_id = …` olvidado en una consulta es una fuga de datos de
   clientes.

## Decisión

### Sesiones

- **Access token JWT de 15 minutos**, firmado con HMAC-SHA256, con el usuario, el negocio y el rol.
  La API lo valida sin consultar la base de datos.
- **Token de renovación opaco de 14 días**, 256 bits aleatorios. En la base de datos solo se guarda su
  huella SHA-256: quien lea la tabla no puede usarlos. **Sirve una sola vez:** cada renovación lo
  revoca y entrega otro. Gastarlo es un `UPDATE … WHERE revocado_en IS NULL`, así que si llegan
  diez peticiones a la vez con el mismo token, exactamente una lo consigue.
- **Reutilizar un token ya gastado cierra todas las sesiones de esa persona.** O lo han robado o el
  cliente está mal hecho; en ambos casos es más seguro obligar a entrar de nuevo.
- **Contraseñas con el `PasswordHasher` de ASP.NET Core Identity** (PBKDF2, sal aleatoria, formato
  con versión). No se usa Identity completo (`UserManager`, `IdentityDbContext`): trae una docena de
  tablas y conceptos (reclamaciones, inicios externos, tokens de teléfono) que no se usan, y
  ligaría el dominio a un framework. El bloqueo de cuentas es una regla del dominio (`Usuario`):
  5 fallos seguidos bloquean 15 minutos.
- **No se revela nada al fallar.** Correo desconocido, contraseña incorrecta, cuenta bloqueada y cuenta
  desactivada dan exactamente la misma respuesta, y en todos los casos se calcula una huella de
  contraseña (real o falsa) para que tarden parecido.
- El inicio de sesión y la renovación tienen su propio límite de peticiones por IP (10 por minuto),
  más estricto que el de las escrituras.
- La clave de firma es obligatoria fuera de desarrollo: la API no arranca sin ella. En desarrollo hay
  una clave fija y pública, y por eso nunca se acepta en otro entorno.

### Roles

Tres, acumulativos: **personal** (agenda y reservas), **encargado** (además, salas, mesas,
horarios y cierres) y **propietario** (además, usuarios). Se aplican con políticas de autorización
en los grupos de rutas. Los propietarios no se crean por la API, solo al dar de alta un negocio:
nadie puede darse más poder a sí mismo. Un propietario no puede desactivarse ni desactivar a otro.

### Aislamiento entre negocios: dos defensas independientes

1. **Filtro global de EF Core** sobre todas las tablas con datos de negocio (`salas`, `mesas`,
   `horarios`, `cierres`, `reservas`, `ocupaciones_mesa`, `usuarios`). Cuando la petición trae la
   sesión de un negocio, EF añade `negocio_id = ese negocio` a **todas** las consultas, incluidas
   las `ExecuteDelete`/`ExecuteUpdate`, también las que alguien escriba mañana y se olvide de
   pedirlo.
2. **Comprobación explícita** en los repositorios y casos de uso, que reciben el negocio como
   parámetro. Si el filtro fallara, esto sigue protegiendo; si esto se olvidara, sigue el filtro.
   Los tests de cada capa rompen una defensa a propósito para comprobar que la otra aguanta.

Lo que se decide con reglas concretas:

- **El negocio sale de la sesión, nunca de la URL ni del cuerpo.** No existe ningún parámetro con el
  que pedir los datos de otro.
- **El contexto de negocio solo existe en `/api/v1/gestion`.** En las rutas públicas se ignora aunque
  venga un token, porque allí el negocio lo indica la URL (`/negocios/{slug}`).
- **Un identificador de otro negocio da 404, no 403.** No se revela ni que exista.
- **El correo es único en toda la base de datos**, no solo dentro de un negocio: es el nombre de
  usuario y el inicio de sesión ocurre antes de saber a qué negocio pertenece quien entra. Lo
  garantiza un índice único, no una comprobación previa.
- El `DbContext` pasa a ser **uno por petición** en lugar de un *pool* de contextos reutilizados:
  lleva el negocio de la sesión, y un contexto reciclado podría arrastrar el de la petición anterior.
- Un test recorre **todas las rutas de la API** y falla si una de `/gestion` no exige sesión, si una
  ruta abierta no es de las públicas conocidas o si una que escribe no tiene límite de peticiones.
  Ya encontró un caso real: cinco `DELETE` sin límite.

### Idempotencia

La huella de una petición idempotente incluye ahora **quién la hace**: si otra persona reutiliza la
clave de idempotencia de una compañera de otro negocio, recibe un error en lugar de la respuesta guardada.

### Cambio respecto a la guía inicial

La guía preveía `PATCH /gestion/reservas/{id}`. Se ha omitido: una reserva es inmutable (hora, grupo y
mesas van juntas y las decide la disponibilidad), así que cambiarla es cancelarla y apuntar otra. Editar
sin más permitiría dejar una reserva en una mesa que ya no cabe o que ya está ocupada.

## Alternativas descartadas

- **Cookies de sesión:** la API está pensada para clientes distintos (web del negocio, móvil), y las
  cookies obligan a defenderse de CSRF.
- **Access tokens opacos consultados en la base de datos:** permitirían revocar al instante, pero
  cada petición pagaría una consulta. Se acepta el límite de abajo a cambio de una validación sin
  base de datos.
- **Un esquema o una base de datos por negocio:** aislamiento más fuerte, pero migraciones y
  conexiones multiplicadas por el número de negocios, y no se justifica con esta escala.
- **Seguridad a nivel de fila de PostgreSQL (RLS):** sería una tercera defensa, en la propia base de datos.
  Es el siguiente paso natural si el proyecto creciera; hoy añadiría complejidad con las
  conexiones agrupadas sin un beneficio proporcionado.

## Consecuencias

- **Un usuario desactivado conserva el access token que ya tenía hasta 15 minutos.** No puede renovarlo
  ni volver a entrar, y sus tokens de renovación se revocan al momento. Hay un test que documenta este límite.
- La tabla `tokens_refresco` crece con cada sesión. Hay un índice por caducidad para purgarla; el trabajo
  programado que lo hace llega en la fase 5, junto a la purga de claves de idempotencia.
- Cada consulta con sesión lleva un filtro más. El coste es mínimo porque todas las tablas filtradas
  tienen índice por `negocio_id`.
