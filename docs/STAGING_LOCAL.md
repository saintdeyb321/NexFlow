# Staging local aislado

Este entorno crea recursos nuevos bajo el proyecto Compose `nexflow-staging`:

- PostgreSQL `nexflow_staging`, Redis y Firestore Emulator con volúmenes propios.
- API y Migrator construidos desde `backend/Dockerfile`. El Migrator aplica las migraciones sólo a `postgres` interno antes de iniciar la API.
- Firestore Emulator con el proyecto desechable `nexflow-staging-local`; no usa `GOOGLE_APPLICATION_CREDENTIALS` ni Firestore real.

La composición base no inicia Evolution, n8n, Cloudinary ni ningún servicio de producción. Los trabajos que requieran n8n fallarán de forma durable dentro del entorno aislado y no son parte de esta prueba conversacional.

## Inicio

Instala Docker Desktop con Compose v2 y ejecuta desde la raíz:

```powershell
pwsh -File .\scripts\staging\Start-LocalStaging.ps1
```

En el primer inicio se crea `.env.staging`, ignorado por Git, a partir de `.env.staging.example`. El script genera secretos aleatorios exclusivamente para PostgreSQL, la autenticación de Evolution y n8n local. No muestra sus valores. Comprueba `/health/live` y `/health/ready` en `http://127.0.0.1:58081`.

Para detener sin borrar los volúmenes:

```powershell
pwsh -File .\scripts\staging\Stop-LocalStaging.ps1
```

No uses `docker compose down -v` durante una conversación que quieras conservar. Si se requiere reiniciar todos los datos sintéticos, el operador debe eliminar explícitamente sólo los volúmenes del proyecto `nexflow-staging`, nunca recursos por nombre genérico.

## Proveedores IA reales

Añade en el archivo local `.env.staging` únicamente credenciales de prueba con presupuesto autorizado:

```text
STAGING_GROQ_API_KEY=...
STAGING_GROQ_MODEL=...
STAGING_GEMINI_API_KEY=...
STAGING_GEMINI_MODEL=...
STAGING_GEMINI_FALLBACK_MODEL=...
```

La API recibe esos valores como `Groq__ApiKey`, `Groq__Model`, `Gemini__ApiKey`, `Gemini__Model` y `Gemini__FallbackModel`. `AiRouter` conserva Groq como primario para extracción y Gemini como fallback transitorio. No hay una sonda de proveedor que envíe mensajes por su cuenta: la primera conversación del número de prueba será la comprobación real y quedará limitada al workspace sintético. La telemetría `AiHttpAttempt`, `AiUsage` y `ConversationPhase` no incluye texto de clientes, prompts ni claves. La configuración de Staging suprime el log informativo que contiene correos del middleware de identidad.

## Workspace y datos sintéticos

El migrator carga módulos, plantillas y el workspace interno del sistema. No existe una herramienta de seeding de negocio que pueda saltarse Firebase Auth, las licencias o los controladores de NexFlow. Para mantener el mismo camino que el producto:

1. Usa un proyecto Firebase **no productivo** con un usuario de prueba y un JWT válido para ese proyecto. El backend actual valida tokens contra `https://securetoken.google.com/{Firebase:ProjectId}`; no está configurado para Firebase Auth Emulator.
2. Con una identidad de superadministrador de staging, llama a `POST /api/superadmin/clients/provision` con `TemplateCode: "FULL"`, una fecha futura y un correo sintético. Guarda el `WorkspaceId` devuelto.
3. Con la identidad dueña y `X-Workspace-Id: <WorkspaceId>`, crea perfil, una sede, horarios, servicios reservables y productos con los endpoints existentes. Los controladores mantienen módulos, permisos y validaciones del producto.

No escribas directamente en PostgreSQL o Firestore para aparentar que las rutas web funcionaron. La creación inicial requiere que el operador configure el Firebase Auth de staging y la membresía superadministradora; no hay bypass local seguro en este repositorio.

## Evolution y teléfono de prueba

El repositorio implementa el cliente de Evolution, pero no contiene una imagen, versión de servidor, OpenAPI ni ruta persistente de Evolution comprobadas. Por eso el perfil opcional rechaza iniciarse hasta que el operador fije esos tres datos para una instalación de prueba:

```text
STAGING_EVOLUTION_IMAGE=<imagen-y-tag-auditados>
STAGING_EVOLUTION_RUNTIME_ENV_FILE=./staging/evolution/.env
STAGING_EVOLUTION_DATA_PATH=<ruta-persistente-documentada-por-esa-imagen>
```

El archivo indicado por `STAGING_EVOLUTION_RUNTIME_ENV_FILE` permanece ignorado. Debe configurar la instancia con sus propios `evolution-postgres` y `evolution-redis` del perfil, y hacer coincidir su clave API con `STAGING_EVOLUTION_API_KEY`. No reutilices una instancia, una base, Redis, una sesión ni una clave de producción.

Cuando ese contrato esté verificado, inicia el perfil:

```powershell
pwsh -File .\scripts\staging\Start-LocalStaging.ps1 -WithEvolution
```

La API se conecta internamente a `http://evolution:8080` y configura el webhook interno `http://api:8080/api/webhooks/evolution/messages-upsert`. El handler exige el HMAC por instancia derivado de `Evolution__WebhookKey`; el modo legado está desactivado.

Con el JWT de la dueña de staging y el `WorkspaceId` sintético, solicita el QR mediante:

```http
POST http://127.0.0.1:58081/api/business/whatsapp/connect
Authorization: Bearer <JWT-DE-STAGING>
X-Workspace-Id: <WORKSPACE-ID-SINTETICO>
```

Muestra el `QrBase64` recibido al teléfono de prueba autorizado y escanéalo manualmente. No escanees con una cuenta de cliente. Después de que el estado indique `CONNECTED`, envía desde ese teléfono mensajes naturales al número de la instancia: una reserva, una edición parcial, una pregunta lateral, un pedido sin la palabra “pedido”, una negación y una solicitud humana. Consulta `GET /api/business/whatsapp/status?refresh=true` con los mismos encabezados entre casos.

No confirmes ninguna reserva o pedido fuera del workspace sintético. Una respuesta de Evolution, la API o un proveedor que no sea inequívoca no autoriza repetir a ciegas el mensaje ni crear otra instancia.
