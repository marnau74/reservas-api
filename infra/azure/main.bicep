// Reservas API en Azure: Container Apps (escala a cero) + PostgreSQL Flexible Server + registros en
// Log Analytics y una alerta de presupuesto. Se despliega en un grupo de recursos ya creado:
//   az deployment group create -g <grupo> -f infra/azure/main.bicep -p infra/azure/main.bicepparam
// Ver docs/despliegue-azure.md. Los secretos llegan como parámetros seguros: nada sensible va en el repo.

targetScope = 'resourceGroup'

@description('Prefijo de los nombres de los recursos (minúsculas y números).')
@minLength(3)
@maxLength(16)
param nombre string = 'reservas'

@description('Región. Por defecto la del grupo de recursos.')
param ubicacion string = resourceGroup().location

@description('Imagen de la API (p. ej. ghcr.io/<usuario>/reservas-api:<commit>). Tiene que ser pública o poder descargarse sin credenciales.')
param imagen string

@description('Usuario administrador de PostgreSQL.')
param postgresUsuario string = 'reservas_admin'

@description('Contraseña del administrador de PostgreSQL.')
@secure()
@minLength(16)
param postgresContrasena string

@description('Clave con la que la API firma los tokens de sesión (32 caracteres o más).')
@secure()
@minLength(32)
param jwtClave string

@description('Sembrar el negocio ficticio de la demo.')
param sembrarDemo bool = true

@description('Contraseña de las cuentas de la demo (obligatoria si se siembra).')
@secure()
param demoContrasena string = ''

@description('Web que llama a la API desde el navegador, sin barra final. Vacío: ninguna.')
param corsOrigen string = ''

@description('Presupuesto mensual del grupo de recursos, en la moneda de la suscripción.')
param presupuestoMensual int = 10

@description('Correo que recibe los avisos de presupuesto (al 80 % real y al 100 % previsto).')
param correoAvisos string

@description('Fecha de inicio del presupuesto: primer día de un mes, yyyy-MM-01. Por defecto, el mes actual.')
param inicioPresupuesto string = '${utcNow('yyyy-MM')}-01'

var sufijo = uniqueString(resourceGroup().id)
var etiquetas = {
  proyecto: 'reservas-api'
  gestionado: 'bicep'
}

// --- Registros -----------------------------------------------------------------------------------
resource registros 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${nombre}-registros-${sufijo}'
  location: ubicacion
  tags: etiquetas
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

// --- PostgreSQL ----------------------------------------------------------------------------------
// Burstable B1ms: el tamaño más pequeño (y el que entra en la cuenta gratuita de 12 meses).
// Acceso público con TLS obligatorio y solo desde servicios de Azure. Una red privada (VNet) sería lo
// propio en producción, pero dobla el coste y la complejidad para una sola API (ver el ADR 8).
resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: '${nombre}-pg-${sufijo}'
  location: ubicacion
  tags: etiquetas
  sku: {
    name: 'Standard_B1ms'
    tier: 'Burstable'
  }
  properties: {
    version: '17'
    administratorLogin: postgresUsuario
    administratorLoginPassword: postgresContrasena
    storage: {
      storageSizeGB: 32
      autoGrow: 'Disabled'
    }
    backup: {
      backupRetentionDays: 7
      geoRedundantBackup: 'Disabled'
    }
    highAvailability: { mode: 'Disabled' }
    network: { publicNetworkAccess: 'Enabled' }
  }

  resource baseDeDatos 'databases' = {
    name: 'reservas'
    properties: {
      charset: 'UTF8'
      collation: 'en_US.utf8'
    }
  }

  // Azure solo deja crear las extensiones autorizadas en el servidor. La restricción que impide solapar
  // reservas (EXCLUDE USING gist) necesita btree_gist: sin esto, la primera migración falla.
  resource extensiones 'configurations' = {
    name: 'azure.extensions'
    properties: {
      value: 'BTREE_GIST'
      source: 'user-override'
    }
  }

  // 0.0.0.0 - 0.0.0.0 es la regla especial de Azure: «permitir servicios de Azure» (no abre internet)
  resource soloAzure 'firewallRules' = {
    name: 'servicios-de-azure'
    properties: {
      startIpAddress: '0.0.0.0'
      endIpAddress: '0.0.0.0'
    }
  }
}

// --- Container Apps ------------------------------------------------------------------------------
resource entorno 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${nombre}-entorno-${sufijo}'
  location: ubicacion
  tags: etiquetas
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: registros.properties.customerId
        sharedKey: registros.listKeys().primarySharedKey
      }
    }
  }
}

var cadenaConexion = 'Host=${postgres.properties.fullyQualifiedDomainName};Database=reservas;Username=${postgresUsuario};Password=${postgresContrasena};SSL Mode=Require'

resource api 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${nombre}-api'
  location: ubicacion
  tags: etiquetas
  // La API migra al arrancar: la extensión tiene que estar autorizada y la base de datos creada antes
  dependsOn: [
    postgres::extensiones
    postgres::baseDeDatos
    postgres::soloAzure
  ]
  properties: {
    managedEnvironmentId: entorno.id
    configuration: {
      // Los secretos se guardan cifrados en la app y se pasan como variables de entorno
      secrets: [
        { name: 'cadena-conexion', value: cadenaConexion }
        { name: 'jwt-clave', value: jwtClave }
        { name: 'demo-contrasena', value: empty(demoContrasena) ? 'sin-demo' : demoContrasena }
      ]
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
    }
    template: {
      containers: [
        {
          name: 'api'
          image: imagen
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: concat([
            { name: 'ConnectionStrings__reservas', secretRef: 'cadena-conexion' }
            { name: 'Jwt__Clave', secretRef: 'jwt-clave' }
            // Una sola réplica: migrar al arrancar es seguro y las tareas programadas no se duplican
            { name: 'BaseDeDatos__MigrarAlArrancar', value: 'true' }
            // Detrás del proxy de Container Apps la IP real llega en X-Forwarded-For
            { name: 'Proxy__ConfiarEnCabecerasReenviadas', value: 'true' }
            { name: 'Demo__Sembrar', value: string(sembrarDemo) }
            { name: 'Demo__Contrasena', secretRef: 'demo-contrasena' }
            // Sin servidor de correo, el código de la reserva se devuelve en la respuesta (ver docs/despliegue.md)
            { name: 'Publico__MostrarCodigoGestion', value: 'true' }
          ], empty(corsOrigen) ? [] : [
            { name: 'Cors__Origenes__0', value: corsOrigen }
          ])
          probes: [
            {
              type: 'Liveness'
              httpGet: { path: '/alive', port: 8080 }
              periodSeconds: 30
            }
            {
              type: 'Readiness'
              httpGet: { path: '/health', port: 8080 }
              periodSeconds: 10
              failureThreshold: 6
            }
          ]
        }
      ]
      // Escala a cero sin tráfico (no se paga cómputo) y nunca más de una réplica
      scale: {
        minReplicas: 0
        maxReplicas: 1
        rules: [
          {
            name: 'peticiones'
            http: { metadata: { concurrentRequests: '50' } }
          }
        ]
      }
    }
  }
}

// --- Presupuesto ---------------------------------------------------------------------------------
resource presupuesto 'Microsoft.Consumption/budgets@2023-11-01' = {
  name: '${nombre}-presupuesto'
  properties: {
    category: 'Cost'
    amount: presupuestoMensual
    timeGrain: 'Monthly'
    timePeriod: { startDate: inicioPresupuesto }
    notifications: {
      real80: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 80
        thresholdType: 'Actual'
        contactEmails: [ correoAvisos ]
      }
      previsto100: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 100
        thresholdType: 'Forecasted'
        contactEmails: [ correoAvisos ]
      }
    }
  }
}

@description('Dirección pública de la API.')
output url string = 'https://${api.properties.configuration.ingress.fqdn}'

@description('Servidor de PostgreSQL.')
output postgres string = postgres.properties.fullyQualifiedDomainName
