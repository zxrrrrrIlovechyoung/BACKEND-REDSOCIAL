# Despliegue de RedSocial

## Estado actual

- Backend desplegado en Azure App Service.
- Base de datos desplegada en Azure SQL Database.
- Código de producción: rama `main`.
- Frontend desplegado en su versión base.

## Backend

### Azure App Service

- Aplicación: `redsocial-api-raul-2026`
- Región: Canada East.
- Sistema operativo: Windows.
- Pila seleccionada: `.NET 10 (LTS)`.
- Proyecto real: `net8.0`.
- Compatibilidad configurada:

```text
DOTNET_ROLL_FORWARD=Major
```

El runtime .NET 10 se usa porque .NET 8 ya no aparece en el formulario actual de Azure. El código del proyecto continúa apuntando a `net8.0`.

### Variables de entorno

Se configuraron en App Service, sin guardar secretos en Git:

```text
ASPNETCORE_ENVIRONMENT=Production
DOTNET_ROLL_FORWARD=Major
Jwt__Issuer=RedSocialAPI
Jwt__Audience=RedSocialClientes
Jwt__ExpireMinutes=60
Jwt__Key=<secreto privado>
Logging__FilePath=D:\home\LogFiles\Application\redsocial-.json
```

La conexión SQL se agregó como cadena de conexión de App Service con el nombre `DefaultConnection`. La contraseña no forma parte de esta documentación.

### Base de datos

- Servidor: `redsocial-sql-raul-2026.database.windows.net`
- Base: `RedSocialDB`

Las migraciones se ejecutaron en orden:

```text
migraciones/001 ... migraciones/019
BACKEND/database/02_LogsAcceso.sql
BACKEND/database/03_Auditoria.sql
```

Antes de desplegar el backend se confirmó que las tablas existían en Azure SQL.

### Integración continua

El despliegue se realiza desde GitHub Actions:

- Repositorio: `zxrrrrrIlovechyoung/BACKEND-REDSOCIAL`
- Rama: `main`
- Autenticación: OIDC mediante identidad administrada.
- Workflow: `.github/workflows/main_redsocial-api-raul-2026.yml`

El workflow publica únicamente el proyecto API:

```yaml
dotnet build src/RedSocial.API/RedSocial.API.csproj --configuration Release
dotnet publish src/RedSocial.API/RedSocial.API.csproj -c Release -o "${{env.DOTNET_ROOT}}/myapp"
```

Esto evita publicar la solución completa en una misma carpeta y garantiza que se incluya la dependencia `Microsoft.IdentityModel.Protocols.dll` requerida por JWT.

### Archivos subidos y persistencia

Las fotos de perfil y de las publicaciones no se guardan en la base de datos. La API guarda en local y producción usando el mismo contrato de URL (`/uploads/...`):

- En desarrollo: `wwwroot/uploads`.
- En App Service: `%HOME%/data/redsocial-media/uploads`.

La carpeta de producción queda fuera del paquete publicado y usa el almacenamiento compartido de App Service. Por eso permanece disponible después de reiniciar la aplicación y puede ser leída por las instancias escaladas. No se debe usar `D:\local` para archivos permanentes.

La selección se hace automáticamente por entorno:

```json
"Storage": {
  "Provider": "Local",
  "LocalRoot": ""
}
```

En local, `LocalRoot` vacío resuelve a `wwwroot`; en producción, a la carpeta `HOME/data/redsocial-media`. No requiere migración SQL porque la base de datos ya almacena las rutas relativas de los archivos.

Para verificarlo en Azure:

1. Publicar este cambio en `main`.
2. Subir una nueva foto de perfil.
3. Reiniciar el App Service.
4. Confirmar que la foto siga visible.
5. Revisar en Kudu/Advanced Tools que exista `D:\home\data\redsocial-media\uploads\perfiles`.

Esta solución no agrega un servicio de pago. Si el proyecto crece, el siguiente reemplazo natural es Azure Blob Storage privado; la interfaz `IFileStorage` deja preparada esa sustitución sin modificar controladores ni la base de datos.

### Comprobación

Endpoint público de prueba:

```text
https://redsocial-api-raul-2026-c5ezb9f4g5djeeg9.canadaeast-01.azurewebsites.net/api/momentos/publico
```

Respuesta confirmada:

```json
{"exito":true,"mensaje":"","datos":[]}
```

El arreglo vacío indica que el endpoint funciona y que todavía no existen publicaciones.

## Frontend base

Frontend base desplegado desde el repositorio `FRONTEND-REDSOCIAL`, rama `main`, usando Azure Static Web Apps en el plan gratuito.

- URL: `https://happy-pebble-0ee8a850f.8.azurestaticapps.net`
- Preset: React.
- Ubicación de la aplicación: `/`.
- Ubicación de salida: `dist`.

El workflow de Static Web Apps compila con la URL pública del backend:

La variable de compilación requerida será:

```text
VITE_API_URL=https://redsocial-api-raul-2026-c5ezb9f4g5djeeg9.canadaeast-01.azurewebsites.net/api
```

El backend permite el origen del frontend mediante:

```text
Cors__Origenes__0=https://happy-pebble-0ee8a850f.8.azurestaticapps.net
```

Para soportar recargas directas de rutas de React Router se agregó `public/staticwebapp.config.json`, que redirige las rutas internas a `index.html`.

Los ajustes de diseño y adaptación móvil se realizarán en una etapa posterior.

## Reglas de seguridad

- No subir contraseñas, claves JWT ni cadenas de conexión a Git.
- No copiar valores secretos en capturas o documentación pública.
- Mantener la base de datos con acceso de red limitado.
- Revisar los logs de App Service y Grafana durante las pruebas.
