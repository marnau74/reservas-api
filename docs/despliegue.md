# Desplegar la demo (Render + Neon)

La demo es la API con un negocio **ficticio**, unas cuentas de personal y unas reservas de
ejemplo. Corre en un servicio web de [Render](https://render.com) con la base de datos en
[Neon](https://neon.tech) (PostgreSQL gestionado). Los dos ofrecen un plan gratuito (revisa sus condiciones al desplegar); las decisiones y
sus límites están en el [ADR 0007](adr/0007-despliegue-de-la-demo.md).

## 1. La base de datos en Neon

1. Crea un proyecto en Neon con **PostgreSQL 17** (la misma versión que usan los tests).
2. Copia la cadena de conexión **directa**, la que **no** lleva `-pooler` en el nombre del servidor.
   Las migraciones y las colas por mesa necesitan sesiones normales de PostgreSQL, y el *pooler* en
   modo transacción no las da.
3. Pásala al formato de Npgsql:

   ```
   Host=ep-xxxx.eu-central-1.aws.neon.tech;Database=neondb;Username=neondb_owner;Password=…;SSL Mode=Require
   ```

## 2. La API en Render

1. En Render: **New → Blueprint** y elige el repositorio. Lee `render.yaml`.
2. Rellena las variables que se piden (todas son secretos o dependen de tu web):

   | Variable | Qué poner |
   |---|---|
   | `ConnectionStrings__reservas` | La cadena de Neon del paso anterior |
   | `Demo__Contrasena` | Contraseña de las cuentas de la demo (10 o más caracteres, con letras y números) |
   | `Cors__Origenes__0` | La web que llamará a la API, sin barra final: `https://mi-bar.example` |
   | `Correo__UrlGestion` | Página de gestión de esa web, con `{codigo}` donde va el código: `https://mi-bar.example/reservas/{codigo}` |

   `Jwt__Clave` la genera Render sola y la conserva entre despliegues.
3. Cuando termine el despliegue, `https://<tu-servicio>.onrender.com/health` debe responder `Healthy`.

En el primer arranque la API aplica las migraciones y siembra los datos de demostración. El registro
mostrará un error de EF Core por la tabla `__EFMigrationsHistory` que no existe: es normal en una
base de datos vacía y solo ocurre esa vez.

## 3. Comprobarlo

```bash
API=https://<tu-servicio>.onrender.com

# Ver las horas libres de un día
curl "$API/api/v1/negocios/bar-la-plaza/disponibilidad?fecha=2026-10-10&comensales=2"

# Entrar como personal (usa la contraseña que pusiste en Demo__Contrasena)
curl -X POST "$API/api/v1/auth/login" -H "Content-Type: application/json" \
  -d '{"email":"personal@demo.example","contrasena":"…"}'
```

Cuentas de demostración: `propietario@demo.example`, `encargado@demo.example` y
`personal@demo.example`. Son de mentira, como el negocio: el dominio `.example` está reservado y
no existe.

## Qué esperar del plan gratuito

- **Render duerme el servicio tras un rato sin tráfico** (unos 15 minutos en el momento de escribir
  esto) y la primera petición tarda hasta un minuto en despertarlo. Mientras duerme no se envían correos ni se ejecutan las tareas
  programadas; **al despertar se ponen al día solas**, porque el estado vive en la base de datos:
  los correos pendientes siguen en la bandeja y las tareas son idempotentes.
- **Neon suspende el cómputo tras unos minutos sin uso** y la primera consulta tarda un instante más.
  La API reintenta las conexiones fallidas.
- Sin servidor de correo configurado, los correos solo se anotan en el registro y el código de la
  reserva se devuelve en la respuesta (`Publico__MostrarCodigoGestion=true`) para poder probar el
  flujo completo. Con un proveedor SMTP, define `Correo__Servidor`, `Correo__Puerto`,
  `Correo__Usuario`, `Correo__Contrasena`, `Correo__Remitente` y `Correo__UsarTls=true`, y pon
  `Publico__MostrarCodigoGestion` a `false`: el código pasa a llegar solo por correo.

## Otra forma: un VPS con Docker

La imagen no depende de Render. Con cualquier servidor que tenga Docker:

```bash
docker build -t reservas-api .
docker run -d --name reservas-api -p 8080:8080 \
  -e ConnectionStrings__reservas="Host=…;Database=…;Username=…;Password=…;SSL Mode=Require" \
  -e Jwt__Clave="$(openssl rand -base64 48)" \
  -e BaseDeDatos__MigrarAlArrancar=true \
  -e Proxy__ConfiarEnCabecerasReenviadas=true \
  reservas-api
```

Y delante, un proxy que termine el HTTPS (Caddy, nginx…). Fuera de desarrollo la API no arranca sin
`Jwt__Clave` (mínimo 32 caracteres), y con `Demo__Sembrar=true` exige `Demo__Contrasena`.

## Todas las variables

| Variable | Por defecto | Para qué |
|---|---|---|
| `ConnectionStrings__reservas` | — | Cadena de conexión a PostgreSQL |
| `Jwt__Clave` | — (obligatoria) | Firma de los tokens de sesión, 32 caracteres o más |
| `BaseDeDatos__MigrarAlArrancar` | `false` (`true` en desarrollo) | Aplicar las migraciones al arrancar |
| `Demo__Sembrar` | `false` (`true` en desarrollo) | Crear el negocio ficticio y sus cuentas |
| `Demo__Contrasena` | — | Contraseña de las cuentas de la demo (obligatoria si se siembra fuera de desarrollo) |
| `Demo__ReservasDeEjemplo` | `true` | Crear reservas de ejemplo al sembrar |
| `Proxy__ConfiarEnCabecerasReenviadas` | `false` | Leer la IP real del cliente de `X-Forwarded-For` (solo detrás de un proxy) |
| `Cors__Origenes__N` | ninguno | Webs que pueden llamar a la API desde un navegador |
| `Publico__MostrarCodigoGestion` | `false` | Devolver el código de gestión en la respuesta (solo sin correo) |
| `Correo__Servidor` | vacío | Servidor SMTP; vacío = los correos solo se anotan en el registro |
| `Correo__UrlGestion` | `http://localhost:3000/reservas/{codigo}` | Enlace de gestión que va en los correos |
| `Tareas__Activas` | `true` | Enviar correos y hacer el mantenimiento en esta instancia |
| `Limites__Lectura__Permisos`, `…Escritura…`, `…Autenticacion…` | 120 / 20 / 10 por minuto | Límite de peticiones por IP |
