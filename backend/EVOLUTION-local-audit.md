# Evolution: auditoría local y despliegue posterior

Fecha: 2026-10-08. Fuente: archivos actuales de `C:\Proyectos\NexFlow`.

La implementación y sus comprobaciones locales están terminadas. La certificación del proveedor instalado y del esquema PostgreSQL desplegado queda PENDIENTE en Oracle. No se desplegó, no se aplicaron migraciones a ninguna base externa y no se hicieron llamadas reales a Evolution, Cloudinary, Firebase ni n8n.

## 1. Problemas encontrados

- El middleware de aislamiento mantenía una transacción y un bloqueo de ciclo de vida durante las llamadas HTTP a Evolution. Las pruebas previas del servicio comprobaban otro DbContext y no detectaban esa transacción.
- Tras un acknowledgement de logout, la ausencia de la instancia o un estado distinto de cerrado podían liberar la vinculación. Un HTTP 400 con propietario nulo también constituía una prueba insuficiente.
- La clave compartida de webhooks heredados no tenía un interruptor para retirarla. La contradicción entre la instancia raíz y la instancia dentro de `data` solo se rechazaba para eventos de conexión.
- La resolución ordinaria de alias podía cambiar el nombre del workspace antes de que el guard rechazara un mensaje con workspace incorrecto. Los nombres heredados tampoco se escapaban en URLs de mensajes salientes: `?`/`#` podían truncar el identificador y dirigir la petición a otro nombre.
- La consulta automática del frontend forzaba siempre la consulta remota, y el temporizador final podía seguir consultando después de vincularse o con la página oculta.
- `frontend/firebase.json` publicaba `public` sin target. `public/index.html` era la bienvenida de Firebase y podía sobrescribir la entrada de Vite al copiar los recursos públicos.
- El repositorio no fija una versión/imagen de Evolution ni incluye su implementación o un contrato OpenAPI. Las consultas a la documentación oficial no devolvieron un contrato utilizable. No se accedió a GitHub.

## 2. Correcciones y garantías

- Solo los tres endpoints de WhatsApp cierran la transacción del middleware antes de ejecutar el servicio. Mantienen las comprobaciones de identidad, pertenencia y ciclo de vida; las demás rutas mantienen su comportamiento anterior.
- Adquirir la operación persistente ahora ocurre dentro de una transacción corta, con bloqueo compartido de ciclo de vida en PostgreSQL. La programación de eliminación obtiene el bloqueo exclusivo y rechaza un workspace con una operación WhatsApp vigente. No se mantiene la transacción durante las llamadas al proveedor.
- Un logout exitoso exige acknowledgement `SUCCESS` y una observación posterior de la instancia existente en `close`/`closed`. Si el proveedor rechaza con HTTP 400, se exige además prueba explícita `disconnectionReasonCode = 401`. Un propietario nulo, una instancia ausente o una respuesta ambigua no liberan el vínculo.
- Se incorporó `Evolution:AllowLegacyWebhookKey`: ausente conserva compatibilidad; falso retira la clave compartida; un valor inválido no autoriza la clave heredada. La clave HMAC por instancia sigue funcionando. Los mensajes también rechazan instancias contradictorias.
- La resolución ordinaria de instancias ahora solo consulta. La confirmación de alias se reserva a webhooks ya autenticados o al fetch verificado existente del proveedor. Los sobres antiguos en cola siguen resolviendo su propietario después de reparar el alias, sin cambiar otra vez el nombre. Se escapan los identificadores en las URLs de envío de texto, documentos e imágenes; se conserva el procesamiento durable existente.
- El frontend respeta el cache del backend al consultar automáticamente; Revisar estado fuerza una actualización. No habilita consultas con la página oculta, no reintenta errores automáticamente y cancela los temporizadores de revisión cuando se completa la conexión. Los intervalos se limitan al QR de 30 segundos o a una reconexión iniciada por el usuario de 60 segundos. Una revisión final solo ocurre si la operación sigue pendiente y visible.
- Ambos archivos Firebase Hosting apuntan a `dist` y al target `nexflow`, ya asociado a `nex-flow`. Se eliminó el HTML de bienvenida de `public`; el HTML de Vite identifica NexFlow. `.firebaserc`, `firebase.nexflow.json` y `.gitignore` ya eran compatibles y no se modificaron. NexFlow y AutoFlow AI comparten proyecto Firebase, pero se preserva la separación de sitios mediante el target explícito.
- Se verificó la implementación existente de aprovisionamiento: UUID normalizado y estable, independiente del nombre comercial; ninguna creación remota. Se preservaron los nombres válidos, la detección de alias ambiguos, el vínculo persistente, los permisos READ/CONFIGURE y el procesamiento durable de mensajes y PDF.

La operación sigue protegida por token y lease persistente de dos minutos, con un máximo de 90 segundos para su trabajo HTTP y timeout individual de 1–15 segundos. Un token vencido no publica resultados ni libera una operación posterior. Los fallos de red conservan la vinculación; un logout inconcluso permanece pendiente y requiere reintento explícito. Un acknowledgement perdido de creación se reconcilia consultando la instancia en el siguiente intento del usuario, sin crear otro nombre.

No existe atomicidad entre PostgreSQL y Evolution. Tras perder la respuesta de logout, puede ser necesario recuperar el acknowledgement o la prueba explícita del proveedor; no se fuerza una liberación local. Una lease vencida protege las escrituras locales, pero no puede cancelar retrospectivamente un efecto ya aceptado por Evolution. No se invoca la eliminación de instancias remotas.

## 3. Archivos modificados

Backend:

- [BusinessController.cs](C:/Proyectos/NexFlow/backend/NexFlow.API/Controllers/Business/BusinessController.cs)
- [EvolutionWebhookController.cs](C:/Proyectos/NexFlow/backend/NexFlow.API/Controllers/Webhooks/EvolutionWebhookController.cs)
- [TenantIsolationMiddleware.cs](C:/Proyectos/NexFlow/backend/NexFlow.API/Middleware/TenantIsolationMiddleware.cs)
- [ReleaseTenantLifecycleLockAttribute.cs](C:/Proyectos/NexFlow/backend/NexFlow.API/Middleware/ReleaseTenantLifecycleLockAttribute.cs) (nuevo)
- [IInstanceResolver.cs](C:/Proyectos/NexFlow/backend/NexFlow.Application/Abstractions/IInstanceResolver.cs)
- [DefaultInstanceResolver.cs](C:/Proyectos/NexFlow/backend/NexFlow.Infrastructure/Gateways/DefaultInstanceResolver.cs)
- [EvolutionConnectionService.cs](C:/Proyectos/NexFlow/backend/NexFlow.Infrastructure/Gateways/EvolutionConnectionService.cs)
- [EvolutionMessageGateway.cs](C:/Proyectos/NexFlow/backend/NexFlow.Infrastructure/Gateways/EvolutionMessageGateway.cs)
- [WhatsAppConnectionRepository.cs](C:/Proyectos/NexFlow/backend/NexFlow.Infrastructure/Persistence/PostgreSQL/Repositories/WhatsAppConnectionRepository.cs)
- [TenantDeletionScheduler.cs](C:/Proyectos/NexFlow/backend/NexFlow.Infrastructure/Persistence/PostgreSQL/Repositories/TenantDeletionScheduler.cs)
- [EvolutionConnectionTests.cs](C:/Proyectos/NexFlow/backend/NexFlow.Tests/EvolutionConnectionTests.cs)
- [EvolutionSecurityTests.cs](C:/Proyectos/NexFlow/backend/NexFlow.Tests/EvolutionSecurityTests.cs)
- [EvolutionMigrationTests.cs](C:/Proyectos/NexFlow/backend/NexFlow.Tests/EvolutionMigrationTests.cs)
- [EvolutionLifecycleTests.cs](C:/Proyectos/NexFlow/backend/NexFlow.Tests/EvolutionLifecycleTests.cs) (nuevo)
- [EvolutionFixture.cs](C:/Proyectos/NexFlow/backend/NexFlow.Tests/Fakes/EvolutionFixture.cs)
- [EVOLUTION-local-audit.md](C:/Proyectos/NexFlow/backend/EVOLUTION-local-audit.md) (este informe)

Frontend:

- [firebase.json](C:/Proyectos/NexFlow/frontend/firebase.json)
- [index.html](C:/Proyectos/NexFlow/frontend/index.html)
- `C:\Proyectos\NexFlow\frontend\public\index.html` (eliminado: plantilla de bienvenida, no datos de negocio)
- [WhatsAppTab.tsx](C:/Proyectos/NexFlow/frontend/src/features/business/components/WhatsAppTab.tsx)
- [whatsAppState.ts](C:/Proyectos/NexFlow/frontend/src/features/business/whatsAppState.ts)
- [whatsapp-state.test.ts](C:/Proyectos/NexFlow/frontend/tests/whatsapp-state.test.ts)
- [firebase-hosting.test.mjs](C:/Proyectos/NexFlow/frontend/tests/firebase-hosting.test.mjs) (nuevo)

## 4. Migraciones y modelo

No se crearon ni ajustaron migraciones o snapshot. El modelo actual coincide con el snapshot: `HasPendingModelChanges()` es falso.

Se validó sin abrir PostgreSQL el SQL generado por `20261008050017_TrackWorkspaceWhatsAppConnections`, junto con la migración anterior de identidad. La tabla tiene PK/FK exclusiva por WorkspaceId, versión de persistencia, vínculo nullable para legado, estado, QR/expiración, observación, token/lease, logout pendiente y confirmación/propietario anterior. `Workspaces.EvolutionInstanceName` mantiene su índice único.

El backfill existente solo asigna identidades a nombres NULL, vacíos o de espacios, sin sobrescribir nombres válidos ni crear sesiones remotas. Un workspace sin identidad todavía rechaza operaciones antes de llamar al proveedor, hasta aplicar esa migración. El Down de la última migración conserva las identidades y elimina la tabla de estado: no debe aplicarse durante sesiones activas, porque perdería su protección persistente.

No se comprobó el esquema de una base PostgreSQL instalada ni su historial real; hacerlo exige acceso al entorno que el propietario desplegará.

## 5. Pruebas y resultados ejecutados

| Comprobación local | Resultado final |
| --- | --- |
| `dotnet build backend/backend.slnx --no-restore --configuration Release` | OK, 0 errores; último build incremental: 0 advertencias; recompilaciones previas: 3 existentes |
| `dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --configuration Release --logger 'console;verbosity=minimal'` | OK, 124/124, 0 omitidas |
| En frontend: `npm.cmd run build` | OK |
| En frontend: `npm.cmd run lint` | OK, sin errores ni advertencias |
| `node --experimental-strip-types --test frontend/tests/whatsapp-state.test.ts frontend/tests/firebase-hosting.test.mjs` | OK, 16/16 |

Se agregaron 23 casos backend y 5 frontend. La suite completa incluye las pruebas PDF existentes. Se cubren aprovisionamiento/identidad estable, workspace legado sin identidad, QR inicial y expirado, doble conexión entre contextos, éxito y bloqueo de segundo vínculo, pérdida/recuperación, logout exitoso/fallido/ambiguo, aislamiento, autenticación y retirada de clave heredada, alias existentes y autenticación previa a su reparación, mensajes rechazados sin alterar otra identidad, URLs salientes seguras de texto/documentos/imágenes, observaciones obsoletas, cache sin llamadas HTTP, transacciones del middleware, metadata MVC real, protección ante eliminación y modelo/migración. Hosting se comprueba contra ambos JSON, el target local y el HTML real generado en `dist`.

La primera ejecución del test nuevo de Hosting dio 15/16 porque su lector JSON no admitía el BOM UTF-8 existente en Windows. Se corrigió el lector y se reejecutó: 16/16. No hay pruebas fallidas pendientes.

Advertencias reales: CS8981 en `20260929030327_refactor.cs` y su Designer; CS8602 en `FirestoreCatalogRepository.cs:198`; Vite advierte un chunk JavaScript de 669.41 kB, superior a 500 kB. No se alteraron esos módulos ni dependencias.

Las pruebas HTTP verifican lo que emite/acepta el cliente local mediante un HttpMessageHandler fake. Las pruebas relacionales usan SQLite en memoria; los adaptadores de las pruebas de ciclo de vida no certifican los bloqueos PostgreSQL. MVC genera sus endpoints reales sin arrancar un servidor ni el Program de producción. La generación SQL usa Npgsql sin abrir una conexión. Ninguna de estas pruebas certifica una versión instalada de Evolution.

## 6. Dependencias pendientes de verificar en Oracle

El siguiente es el contrato implementado por NexFlow, NO un contrato confirmado del proveedor remoto:

| Endpoint | Cliente local y respuesta exigida |
| --- | --- |
| `instance/fetchInstances` | GET, filtro `instanceName`; array con nombre exacto. Acepta objeto v2 plano o objeto legado dentro de `instance`; estado y `ownerJid`/`owner` para distinguir sesión previa. Metadatos incompletos impiden emparejar. |
| `instance/create` | POST con `instanceName`, integración `WHATSAPP-BAILEYS`, `qrcode: false`; exige confirmar existencia mediante fetch posterior. Conflicto no crea otro identificador. |
| `instance/connect/{instance}` | GET por acción explícita; `instance.state/status: open` o QR base64 reconocido. QR vacío/incompleto falla. |
| `instance/connectionState/{instance}` | No utilizado: no añade una segunda llamada de estado ni usa un estado cerrado aislado como prueba de logout. Su contrato tampoco quedó verificado. |
| `instance/logout/{instance}` | DELETE; `status: SUCCESS` y fetch posterior cerrado/existente. HTTP 400 solo permite confirmar con prueba explícita 401. |
| `webhook/set/{instance}` | POST, objeto `webhook`, URL única, `byEvents: false`, `base64: false`, eventos `MESSAGES_UPSERT`/`CONNECTION_UPDATE` y header HMAC `X-NexFlow-Webhook-Key`. |
| `instance/restart/{instance}` | PUT solo en reconexión explícita de una sesión ya vinculada con estado `connecting`; no genera QR ni reemplaza sesión. También requiere validación de versión. |

Antes de producción, identificar y fijar la imagen/versión compatible; contrastar métodos y JSON con su OpenAPI/implementación instalada. Comprobar en particular: headers personalizados de webhook, payload de eventos, propietario de sesión offline, retención de propietario después del logout, acknowledgement SUCCESS, estado cerrado posterior y prueba 401 para logout ya cerrado. Si la versión no proporciona esos datos, el sistema conserva el vínculo y puede bloquear el emparejamiento/logout; no debe sortearse borrando el estado o la sesión.

Verificar además esquema/historial real, unicidad de nombres y alias, bloqueos compartido/exclusivo PostgreSQL con dos procesos, carreras de conexión/eliminación, TLS/ruta de webhook y CORS del sitio NexFlow. No se ejecutaron pruebas E2E en navegador ni comprobaciones de consumo CPU/RAM Always Free; los tests frontend cubren política de estado/consultas y build. Los eventos antiguos después de una nueva tentativa de emparejamiento carecen de un identificador de sesión verificable del proveedor: el vínculo queda protegido de forma conservadora, pero esa frontera requiere comprobar el comportamiento de la versión instalada.

Mientras se permita la clave global heredada, su compromiso permitiría autenticar eventos para otras instancias: es un riesgo temporal conocido, no aislamiento criptográfico por instancia. Retirada segura: inventariar todas las instancias existentes, reconfigurar solo sus webhooks con HMAC por nombre exacto, comprobar eventos de mensaje/conexión para cada workspace y recién entonces deshabilitar la compatibilidad global. Reconfigurar un webhook no requiere logout ni un QR. Hacerlo desde un contexto administrativo seguro; no exponer secretos en navegador, comandos compartidos o logs. No se reconfiguraron instancias existentes durante esta auditoría.

## 7. Variables de entorno

Nombres usados por esta integración y su despliegue; no se incluyen valores:

- `Evolution__BaseUrl`
- `Evolution__ApiKey`
- `Evolution__WebhookUrl`
- `Evolution__WebhookKey`
- `Evolution__TimeoutSeconds`
- `Evolution__AllowLegacyWebhookKey` (nueva, opcional durante la transición)
- `ConnectionStrings__DefaultConnection`
- `Cors__AllowedOrigins__0`
- `ReverseProxy__KnownNetworks__0`
- `ASPNETCORE_ENVIRONMENT`
- `VITE_API_URL`
- `VITE_FIREBASE_API_KEY`
- `VITE_FIREBASE_AUTH_DOMAIN`
- `VITE_FIREBASE_PROJECT_ID`
- `VITE_FIREBASE_STORAGE_BUCKET`
- `VITE_FIREBASE_MESSAGING_SENDER_ID`
- `VITE_FIREBASE_APP_ID`

Se mantienen las demás variables de producción ya exigidas por NexFlow. No se modificaron secretos, credenciales ni `appsettings*`.

## 8. Procedimiento posterior recomendado (no ejecutado)

1. Respaldar PostgreSQL y los datos persistentes de Evolution. Conservar nombres y sesiones existentes; identificar versión y contratos antes de actualizar.
2. Revisar el historial de migraciones del destino y el SQL pendiente. Aplicar las migraciones existentes mediante el Migrator publicado de NexFlow, con configuración PostgreSQL del entorno. Comprobar tabla de estado, índice único y backfill; no editar ni reaplicar manualmente migraciones históricas.
3. Publicar el backend con las variables vigentes, ruta HTTPS de webhook y CORS del sitio correcto. Comprobar que aprovisionar/iniciar sesión solo reserve el identificador local.
4. En workspaces de prueba, ejecutar primera conexión, QR expirado, vínculo, doble clic/dos procesos, pérdida temporal, recuperación y logout confirmado/fallido. Validar también una instancia heredada. No probar cambio de número sobre sesiones productivas sin consentimiento del propietario.
5. Migrar la autenticación de webhooks según el inventario y el procedimiento anterior, conservando todas las sesiones. Deshabilitar la clave global solo después de comprobar cada instancia.
6. En `frontend`, ejecutar build, lint y los tests Node. Revisar `dist/index.html`; debe contener NexFlow y los assets compilados, sin la plantilla Firebase.
7. El propietario puede publicar posteriormente con `firebase deploy --project autoflow-ai-6397d --config firebase.nexflow.json --only hosting:nexflow`. El target resuelve exclusivamente a `nex-flow`, no al sitio predeterminado de AutoFlow AI. Ese comando NO se ejecutó aquí.
8. Verificar manualmente `https://nex-flow.web.app`, la visibilidad de la sección, el aislamiento de cache por workspace y la detención de consultas al vincular/ocultar/cambiar de sección. Ante incompatibilidades del proveedor, preservar sesiones y estado PostgreSQL; no utilizar rollback que borre la tabla de vinculación.

Incidencia de ejecución: antes de leer la prohibición del adjunto se ejecutaron tres consultas Git de solo lectura (estado, rama y HEAD), sin modificar el repositorio. Se comunicó al usuario y no se volvieron a ejecutar comandos Git. La revisión de cambios quedó limitada a los parches propios y sus archivos, sin Git. No se ejecutaron Docker, despliegues, commits, push ni cambios de rama.
