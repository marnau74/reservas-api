// Parámetros del despliegue. Los secretos y el correo se leen de variables de entorno en el momento de
// desplegar (en GitHub Actions vienen de los secretos del repositorio): aquí no se escribe ninguno.
using 'main.bicep'

param nombre = 'reservas'
param imagen = readEnvironmentVariable('IMAGEN_API')
param postgresContrasena = readEnvironmentVariable('POSTGRES_CONTRASENA')
param jwtClave = readEnvironmentVariable('JWT_CLAVE')
param sembrarDemo = true
param demoContrasena = readEnvironmentVariable('DEMO_CONTRASENA', '')
param corsOrigen = readEnvironmentVariable('CORS_ORIGEN', '')
param presupuestoMensual = 10
param correoAvisos = readEnvironmentVariable('CORREO_AVISOS')
