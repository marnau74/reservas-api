# Imagen de la API para desplegarla (Render, un VPS con Docker…).
# Se construye desde la raíz del repositorio:  docker build -t reservas-api .

# --- Compilación -------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS compilar
WORKDIR /origen

# Primero solo lo que decide las dependencias: mientras no cambien, la restauración de paquetes
# (lo más lento) se reutiliza de la caché de Docker aunque cambie el código.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/Reservas.Dominio/Reservas.Dominio.csproj src/Reservas.Dominio/
COPY src/Reservas.Aplicacion/Reservas.Aplicacion.csproj src/Reservas.Aplicacion/
COPY src/Reservas.Infraestructura/Reservas.Infraestructura.csproj src/Reservas.Infraestructura/
COPY src/Reservas.ServiceDefaults/Reservas.ServiceDefaults.csproj src/Reservas.ServiceDefaults/
COPY src/Reservas.Api/Reservas.Api.csproj src/Reservas.Api/
RUN dotnet restore src/Reservas.Api/Reservas.Api.csproj

COPY src ./src
RUN dotnet publish src/Reservas.Api/Reservas.Api.csproj -c Release --no-restore -o /publicado /p:UseAppHost=false

# --- Ejecución ---------------------------------------------------------------------------------
# Solo el entorno de ejecución de ASP.NET Core (sin SDK ni compiladores). Trae los datos de zonas
# horarias que necesita el dominio (Europe/Madrid).
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=compilar /publicado .

# Npgsql intenta negociar cifrado GSS (Kerberos) con PostgreSQL y, sin esta librería, lo anota como
# un error en el registro de cada arranque. Es pequeña y evita el ruido.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

# No se ejecuta como administrador: el usuario «app» viene ya creado en la imagen.
USER $APP_UID

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=0
EXPOSE 8080

ENTRYPOINT ["dotnet", "Reservas.Api.dll"]
