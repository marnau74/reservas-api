# 0007 · Despliegue de la demo

- **Estado:** aceptada
- **Fecha:** 2026-09-30

## Contexto

El proyecto tiene que poder verse funcionando sin que quien lo mira instale nada, y sin que su
autor pague por ello. Eso impone un despliegue con planes gratuitos y datos ficticios. Al mismo
tiempo, es una API con datos de personas y sesiones: aunque sea una demo, no debe quedar
configurada de forma que sirva de mal ejemplo.

## Decisión

### Dónde

- **API en Render** (servicio web con Docker) y **PostgreSQL 17 en Neon**. Los dos ofrecen un plan
  gratuito (condiciones a la fecha de escribir esto: conviene revisarlas al desplegar) y se
  configuran con un fichero (`render.yaml`) y una cadena de conexión.
- La imagen no depende de Render: es una imagen de Docker normal que corre igual en un VPS.
- **No hay Kubernetes, Terraform ni servicios de cola.** Para una API y una base de datos, el
  despliegue es un fichero de 40 líneas; añadir más sería adornar, no resolver un problema.

### Cómo se construye la imagen

- **Dos fases:** el SDK compila y solo el entorno de ejecución de ASP.NET Core llega a la imagen final.
- **Los paquetes se restauran antes de copiar el código**, para que la caché de Docker los reutilice
  mientras no cambien las dependencias.
- **No corre como administrador:** usa el usuario `app` que ya trae la imagen.
- Construirla desde cero encontró un fallo que el equipo local no tenía: sin copiar `.editorconfig`,
  las migraciones dejan de contar como código generado y los analizadores (avisos como errores)
  rompen la compilación. Por eso se probó la imagen real contra un PostgreSQL en contenedor antes
  de darla por buena.

### Configuración: fuera de desarrollo la API arranca segura o no arranca

Todo es por variables de entorno, y varias comprobaciones **impiden arrancar** en lugar de arrancar
mal (un test por cada una):

| Falta o está mal | Qué pasa |
|---|---|
| `Jwt:Clave` ausente o de menos de 32 caracteres | No arranca: cualquiera podría firmar sus propios tokens |
| `Demo:Sembrar` sin `Demo:Contrasena` | No arranca: las cuentas de desarrollo tienen una contraseña escrita en el código |
| `Demo:Contrasena` débil | No arranca |
| `Cors:Origenes` con una barra final o una ruta | No arranca: un origen mal escrito no protegería nada y nadie lo notaría |
| `Correo:UrlGestion` sin `{codigo}` | No arranca: los correos saldrían con un enlace inútil |

Y por defecto, fuera de desarrollo: el contrato OpenAPI no se publica, no hay CORS para ningún
origen (no existe «*»), no se migra ni se siembra nada, y todas las respuestas llevan cabeceras de
seguridad (`nosniff`, `X-Frame-Options`, CSP restrictiva, `Cache-Control: no-store` en la API y
HSTS detrás del proxy).

### Migraciones

En el plan gratuito de Render no hay paso previo al despliegue, así que la API migra al arrancar
(`BaseDeDatos:MigrarAlArrancar`). EF Core toma un bloqueo en la base de datos mientras migra, de modo
que si arrancaran dos instancias a la vez, una migra y la otra espera. Es una decisión para una
demo con una instancia: en un despliegue con varias réplicas y datos reales, las migraciones serían
un paso propio del despliegue (un *job* antes de arrancar las instancias nuevas), y por eso la
opción está desactivada por defecto.

### Datos de la demo

- Negocio, cuentas y reservas **ficticios y marcados como tales**, con correos en `.example` (un
  dominio reservado que no existe) y nombres del tipo «Cliente demo 1».
- La contraseña de las cuentas **no** es la del README: se define al desplegar. Un test comprueba
  que la publicada no entra en una instancia configurada con otra.
- Sembrar es idempotente: reiniciar no duplica cuentas ni reservas.

### Health checks

`/health` responde solo `Healthy` o `Unhealthy`, sin detalles, y comprueba la conexión con la base de
datos; `/alive` solo que el proceso responde. Antes solo existían en desarrollo, pero Render los
necesita, y una respuesta de una palabra no revela nada aprovechable.

## Alternativas descartadas

- **Fly.io, Railway u otros:** perfectamente válidos y la imagen de Docker serviría igual; no se
  han probado, y con Render y Neon el despliegue completo cabe en un fichero.
- **Azure App Service o AWS:** mejor encaje con .NET en empresas, pero configurar una cuenta de nube
  para una demo pública añade coste y superficie de errores sin mejorar lo que se enseña.
- **Base de datos en el mismo servicio (SQLite):** la garantía central del proyecto es una
  restricción de exclusión de PostgreSQL; con otra base de datos la demo no demostraría lo importante.
- **Migrar con un ejecutable aparte (`dotnet ef migrations bundle`):** es lo correcto con varias
  réplicas, pero exige un paso previo que el plan gratuito no ofrece.

## Consecuencias

- **El plan gratuito duerme el servicio** tras un rato sin tráfico (unos 15 minutos en el momento de escribirlo). Mientras duerme no salen
  correos ni corren las tareas; al despertar se ponen al día solas, porque el estado está en la base
  de datos y las tareas son idempotentes. Es un buen banco de pruebas del diseño de la bandeja de salida.
- La primera petición tras un rato de inactividad tarda hasta un minuto (Render) más un instante (Neon).
- Sin proveedor de correo, la demo devuelve el código de gestión en la respuesta para que se pueda
  probar el flujo. Es exactamente el caso para el que existe esa opción, y se documenta cómo pasar a
  correo real.
- Cualquiera con la contraseña de la demo puede modificar sus datos. Son ficticios y no hay nada que
  proteger, pero la demo puede acabar con datos de prueba: se puede reiniciar borrando la base de
  datos en Neon; al arrancar se vuelve a sembrar.
