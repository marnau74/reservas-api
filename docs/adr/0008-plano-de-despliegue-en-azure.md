# 0008 · Plano de despliegue en Azure con Bicep

- **Estado:** aceptada (complementa a la [0007](0007-despliegue-de-la-demo.md), no la sustituye)
- **Fecha:** 2026-10-16

## Contexto

La demo pública sigue en Render y Neon por lo que explica la 0007: gratis, un fichero y nada que
mantener. Allí se descartó Azure *para la demo*. Pero el destino habitual de una API .NET en una
empresa es Azure, y ahí el despliegue no se hace a mano en un portal: se describe como código,
se revisa en una *pull request* y se aplica desde el CI sin credenciales guardadas.

## Decisión

Añadir, junto a la demo, un plano de despliegue en Azure que se pueda aplicar tal cual:

- **Bicep**, no Terraform: es el lenguaje nativo de Azure, no necesita estado propio (el estado es
  Azure) y no añade otra herramienta. Si el resto de la infraestructura de una empresa estuviera en
  Terraform, se traduciría sin cambiar el diseño.
- **Azure Container Apps**, no App Service: la API ya es una imagen de Docker y Container Apps
  escala a cero sin tráfico. App Service cobra el plan aunque no haya peticiones.
- **PostgreSQL Flexible Server**: la garantía central (no solapar reservas) es una restricción de
  exclusión de PostgreSQL, así que Azure SQL no sirve. Necesita autorizar la extensión `btree_gist`
  (`azure.extensions`); sin ella la primera migración falla.
- **Una réplica como máximo**: la API migra al arrancar y ejecuta las tareas programadas; con más
  réplicas habría que separar las migraciones en un paso previo (lo dice ya la 0007) y desactivar
  las tareas en todas menos una (`Tareas__Activas`).
- **OIDC entre GitHub y Azure**: la identidad de GitHub Actions solo puede iniciar sesión desde el
  entorno `azure` del repositorio y solo tiene permisos sobre su grupo de recursos. No hay ningún
  secreto de Azure guardado.
- **Presupuesto con avisos** en el mismo Bicep: un error de configuración no debe costar dinero sin
  que nadie se entere.
- **Solo se despliega a mano.** El workflow valida el Bicep en cada cambio, pero el despliegue
  necesita lanzarlo una persona (y, si se configura, aprobarlo).

## Alternativas descartadas

- **Red privada (VNet) para PostgreSQL:** es lo propio con datos reales, pero exige integrar el
  entorno de Container Apps en una VNet y una zona DNS privada, y sube el coste. Para esta API, la
  base de datos admite solo conexiones desde servicios de Azure y con TLS obligatorio. Es lo primero
  que cambiaría con datos reales.
- **Azure Key Vault para los secretos:** los secretos de Container Apps se guardan cifrados y no se
  leen desde fuera; Key Vault añade rotación centralizada y auditoría, útil cuando hay varias apps.
- **Azure Container Registry:** GitHub Container Registry ya está junto al código y es gratis para
  imágenes públicas; con imágenes privadas, ACR con identidad administrada evitaría credenciales.

## Consecuencias

- El repositorio no tiene ningún entorno de Azure creado: el plano se valida en el CI, pero no hay
  una URL de Azure que probar. Los pasos para crearlo están en [`docs/despliegue-azure.md`](../despliegue-azure.md).
- Con escala a cero, la primera petición tras un rato sin tráfico tarda unos segundos.
- La imagen es la misma en Render, en Azure y en un VPS: el código no sabe dónde corre.
