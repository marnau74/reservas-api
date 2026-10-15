# Desplegar en Azure (Container Apps + PostgreSQL)

Alternativa a la demo de Render para un entorno de empresa: la misma imagen de Docker en **Azure
Container Apps** (escala a cero sin tráfico) con **PostgreSQL Flexible Server**, descrito en Bicep
([`infra/azure/main.bicep`](../infra/azure/main.bicep)) y desplegado desde GitHub Actions con OIDC
([`.github/workflows/azure.yml`](../.github/workflows/azure.yml)). El porqué, en el
[ADR 0008](adr/0008-plano-de-despliegue-en-azure.md).

> El workflow valida el Bicep en cada cambio, pero **solo despliega si se lanza a mano**. Este
> repositorio no tiene ningún entorno de Azure creado.

## Qué se crea

| Recurso | Configuración | Por qué |
|---|---|---|
| Container App `reservas-api` | 0,25 vCPU, 0,5 GiB, de 0 a 1 réplicas, HTTPS | Sin tráfico no consume cómputo; una réplica porque migra al arrancar y ejecuta las tareas programadas |
| PostgreSQL Flexible Server 17 | Burstable B1ms, 32 GB, copias 7 días, TLS obligatorio | El más pequeño; la restricción de exclusión necesita PostgreSQL |
| Extensión `btree_gist` autorizada | `azure.extensions` | Sin ella falla la primera migración (Azure solo deja crear extensiones autorizadas) |
| Regla de firewall «servicios de Azure» | `0.0.0.0` | La base de datos solo admite conexiones desde Azure, no desde internet |
| Log Analytics | 30 días | Registros de la API |
| Presupuesto mensual | 10 (moneda de la suscripción), avisos al 80 % real y 100 % previsto | Que un error de configuración no cueste dinero sin enterarse |

Los secretos (contraseña de PostgreSQL, clave JWT, contraseña de la demo) llegan como parámetros
seguros desde los secretos de GitHub y se guardan cifrados en la Container App.

## Coste aproximado

- Container Apps: el consumo incluye cada mes una cuota gratuita de vCPU-segundos y peticiones que
  una demo no agota; con escala a cero, sin tráfico es 0.
- PostgreSQL B1ms: unos 12-15 € al mes, o gratis 12 meses con la cuenta gratuita de Azure (750 h de B1ms).
- Log Analytics: los primeros GB al mes son gratuitos.

Las condiciones cambian: compruébalas en la calculadora de precios de Azure antes de desplegar.

## Preparar el despliegue (una vez)

Con la [CLI de Azure](https://learn.microsoft.com/cli/azure/) y una suscripción:

```bash
# 1. Grupo de recursos (Spain Central, o la región que prefieras)
az group create --name reservas-rg --location spaincentral

# 2. Identidad para GitHub Actions, con permiso solo sobre ese grupo
az ad app create --display-name reservas-api-github
APP_ID=$(az ad app list --display-name reservas-api-github --query "[0].appId" -o tsv)
az ad sp create --id "$APP_ID"
az role assignment create --assignee "$APP_ID" --role Contributor \
  --scope "$(az group show --name reservas-rg --query id -o tsv)"

# 3. Confianza OIDC: solo el entorno «azure» de este repositorio puede iniciar sesión
az ad app federated-credential create --id "$APP_ID" --parameters '{
  "name": "github-entorno-azure",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:<usuario>/reservas-api:environment:azure",
  "audiences": ["api://AzureADTokenExchange"]
}'
```

En GitHub (Settings › Environments) se crea el entorno **`azure`** (con aprobación manual si se
quiere) y en él:

| Tipo | Nombre | Valor |
|---|---|---|
| Variable | `AZURE_CLIENT_ID` | `$APP_ID` |
| Variable | `AZURE_TENANT_ID` | `az account show --query tenantId -o tsv` |
| Variable | `AZURE_SUBSCRIPTION_ID` | `az account show --query id -o tsv` |
| Variable | `AZURE_GRUPO` | `reservas-rg` |
| Variable | `CORS_ORIGEN` | Web que llama a la API (opcional, sin barra final) |
| Secreto | `POSTGRES_CONTRASENA` | 16 caracteres o más (`openssl rand -base64 24`) |
| Secreto | `JWT_CLAVE` | 32 caracteres o más (`openssl rand -base64 48`) |
| Secreto | `DEMO_CONTRASENA` | Contraseña de las cuentas de la demo |
| Secreto | `CORREO_AVISOS` | Correo que recibe los avisos de presupuesto |

No hace falta ningún secreto de Azure: el inicio de sesión es por OIDC.

## Desplegar

Actions › **azure** › Run workflow. El workflow:

1. valida el Bicep;
2. construye la imagen y la publica en GitHub Container Registry (`ghcr.io/<usuario>/reservas-api:<commit>`);
3. inicia sesión en Azure y despliega el Bicep con esa imagen;
4. espera a que `/health` responda (la primera petición despierta la réplica).

La primera vez, el paquete de GHCR se crea privado: hay que hacerlo **público** (Packages ›
reservas-api › Package settings) para que Container Apps pueda descargarlo sin credenciales, y
relanzar el workflow.

Desplegar a mano, sin GitHub:

```bash
export IMAGEN_API=ghcr.io/<usuario>/reservas-api:<etiqueta> POSTGRES_CONTRASENA=… JWT_CLAVE=… \
       DEMO_CONTRASENA=… CORREO_AVISOS=…
az deployment group create -g reservas-rg -f infra/azure/main.bicep -p infra/azure/main.bicepparam
```

## Borrarlo todo

```bash
az group delete --name reservas-rg
```
