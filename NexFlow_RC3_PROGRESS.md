# NexFlow RC-3 — memoria de progreso

Actualización: 2026-10-19 (America/Lima).
Antes de cada sesión leer `AGENTS.md`, `NexFlow_PLAN_MAESTRO_CORRECCION_MVP_RC3.md` y este registro.

## Estado

- Sprint actual: **3**, implementado y verificado localmente; detenido para revisión. Sprint 2 aprobado por el usuario.
- HEAD local al iniciar Sprint 3: `cf238ee276824e84355176bbab40817e8bda8b84`; árbol limpio, conservado. Sin consulta a GitHub.
- Autorización: exclusivamente NF-08/12. **Próximo sprint autorizado: ninguno. Sprint 4 NO autorizado.**
- Veredicto: **SPRINT 3 APROBABLE** localmente. No constituye GO de despliegue; las causas de los cuatro eventos históricos y validación de servicios reales siguen pendientes.

## Evidencia y decisiones del Sprint 0 (histórico)

- Ver diagnóstico/matriz/archivos de código y propuesta de parche mínimo en `INCIDENTE_CAUSA_RAIZ.md`.
- Reproducidos NF-02 (perfil antes del try), NF-03 (identidad compartida de avisos), NF-04 (fallo parcial oculta Inbox) y NF-05 (frase de selección no determinista).
- NF-01: predicado FIFO comprobado con adaptador SQLite; no valida locks ni concurrencia PostgreSQL.
- Secuencia exacta con intérprete/transporte simulados sanos: cinco respuestas, Hola? sin IA, cero operaciones comerciales. No reproduce el silencio operacional.
- NF-06: Processed significa turno finalizado, no entrega; preservar alertas existentes y UnknownDelivery sin reenvío.
- En Sprint 0 no se modificó código de aplicación. Los parches autorizados posteriormente se registran abajo.

## Archivos creados por Sprint 0

- `backend/NexFlow.Tests/Rc3Sprint0IncidentTests.cs` (5 casos).
- `frontend/tests/rc3-inbox-browser.test.mjs` (3 casos) y `rc3-inbox-preview.server.mjs`.
- `frontend/tests/fixtures/rc3-inbox-api.ts`, `rc3-inbox-preview.tsx`, `rc3-inbox-preview.html`.
- `INCIDENTE_CAUSA_RAIZ.md` y este registro.

## Validación del Sprint 0 (histórico)

- Baseline backend antes de reproducciones: **500/500**.
- Backend con pruebas nuevas: **502 aprobadas / 3 fallidas / 0 omitidas, total 505**. Rojas: NF-02, NF-03 (Sprint 1), NF-05 (Sprint 2); sin debilitar ni omitir expectativas.
- Navegador Inbox: **1 aprobada / 2 fallidas / 0 omitidas**. Fallan ambos errores parciales NF-04 (Sprint 2), conservando datos en caché.
- Build backend correcto; build frontend correcto; lint frontend correcto.
- Advertencias conocidas: CS8981 x2 y CS8602 x1 al recompilar backend; chunk JS >500 kB en frontend. No corregidas por estar fuera de alcance.
- Comandos exactos y límites de los fakes registrados en el diagnóstico. Ninguna dependencia instalada.

## Pendientes antes de un GO de despliegue

Imagen realmente activa durante el incidente; salida agregada SQL/logs del intervalo UTC; recepción webhook y estados outbound/Evolution; modo/licencia; errores HTTP de Inbox; contraste ZIP/remoto si se aporta evidencia.
No ejecutar Oracle ni publicar. No reinyectar Outbox PDF histórico ni UnknownDelivery.
**Operaciones externas ejecutadas: ninguna.** Sin commit, push, deploy, secretos, migraciones o infraestructura modificados.

## Sprint 1 — implementación y garantías

- **NF-03 OK:** source outbound = `request.MessageId`; presupuesto IA sin cambios. Inbounds distintos responden independientemente y el replay mantiene la identidad durable. La prueba histórica que esperaba un aviso compartido entre cuatro inbounds se actualizó a 10 respuestas para 10 turnos, conservando exactamente seis llamadas IA.
- **NF-02 OK:** el adaptador Firestore clasifica únicamente Unavailable/ResourceExhausted/DeadlineExceeded en una excepción tipada, sin añadir dependencia gRPC a Application. Intérprete devuelve ProviderUnavailable + Unavailable/Timeout, sin consultar IA. Permisos, configuración inválida, conflictos y cancelaciones se propagan. Zona configurada inválida ya no se sustituye silenciosamente. Middleware conserva HTTP 503 para esta excepción, evitando una regresión en consumidores existentes del perfil.
- La reproducción NF-02 mantiene sus aserciones y ahora usa el repositorio real con fallo RPC simulado, en lugar de un mock que omite la nueva frontera de clasificación.
- **NF-01 OK local:** diagnóstico de cabezas Failed/Processing de más de cinco minutos, con identificador del siguiente inbound bloqueado. Muestreo de hasta 50 cabezas/minuto y presupuesto de tres segundos; alerta agrupada por workspace con cooldown SQL durable de una hora. No modifica claims, estados ni orden. Transiciones RetryScheduled/DeadLetter registradas después de persistir; cinco intentos y backoff anteriores intactos.
- **NF-06 OK local:** scope de logging WorkspaceId/InboundId interno enlaza handler/outbound, preparación, intento, aceptación del proveedor y resultado durable; modo humano/supresión identificables. No se registran contenido, teléfono, tokens ni IDs de sesión/proveedor. Fallos usan categorías/tipos/códigos seguros. Processed y Sent no se presentan como entrega al destinatario.
- Logging/alertas no revierten operaciones durables; observación fuera de la transacción del turno. Se reutilizan notificaciones, cooldown e idempotencia. UnknownDelivery permanece sin segundo transporte.
- Sin frontend/parser, esquemas, dependencias, configuración, plan maestro o AGENTS modificados.

## Archivos de Sprint 1

Backend (rutas relativas a `backend/`):

- `NexFlow.API/Middleware/GlobalExceptionMiddleware.cs`
- `NexFlow.Application/Abstractions/BusinessProfileUnavailableException.cs` (nuevo)
- `NexFlow.Application/Abstractions/Repositories/IInboundMessageRepository.cs`
- `NexFlow.Application/Features/AI/Interpretation/AiInterpreter.cs`
- `NexFlow.Application/Features/Automation/ProcessMessage/ProcessIncomingMessageCommandHandler.cs`
- `NexFlow.Application/Features/Automation/ProcessMessage/Services/AiResponseOrchestrator.cs`
- `NexFlow.Application/Features/Automation/ProcessMessage/Services/OutboundMessageService.cs`
- `NexFlow.Infrastructure/Persistence/Firestore/FirestoreBusinessProfileRepository.cs`
- `NexFlow.Infrastructure/Persistence/PostgreSQL/Repositories/InboundMessageRepository.cs`
- `NexFlow.Infrastructure/Workers/InboundMessageWorker.cs`
- `NexFlow.Tests/AssistantDurabilityTests.cs`
- `NexFlow.Tests/Fakes/InMemoryFirestore.cs`
- `NexFlow.Tests/Rc3Sprint0IncidentTests.cs`
- `NexFlow.Tests/Rc3Sprint1ProfileTests.cs` (nuevo)
- `NexFlow.Tests/Rc3Sprint1QueueTests.cs` (nuevo)
- `NexFlow.Tests/Rc3Sprint1TraceTests.cs` (nuevo)

Registros: `NexFlow_RC3_PROGRESS.md`, `INCIDENTE_CAUSA_RAIZ.md`. Frontend: ninguno.

## Validación Sprint 1 y pendientes (histórico)

- `dotnet build backend/backend.slnx --no-restore`: correcto. Último build incremental: cero errores/advertencias; recompilación previa mostró los tres warnings preexistentes CS8981 x2 / CS8602 x1.
- `dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore`: **531 totales, 530 aprobadas, 1 fallida, 0 omitidas**. Única roja: NF-05, Sprint 2. NF-02/03 y control del incidente verdes.
- Filtro relacionado de concurrencia, handoff, IA, durabilidad, Redis, Evolution y Sprint 1: **354 aprobadas, 0 fallidas, 0 omitidas**. Las tres clases nuevas aportan 26 casos; cubren permisos, timeout/recuperación del perfil, borradores/licencias, FIFO/dead letter, lease expirado y correlación sin PII.
- `node --test tests/rc3-inbox-browser.test.mjs` desde frontend: **3 totales, 1 aprobada, 2 fallidas NF-04, 0 omitidas**; respuestas HTTP simuladas, sin modificar frontend ni debilitar/omitir las pruebas.
- SQLite ejecuta los predicados reales con adaptación exclusivamente del sufijo de locks; **no certifica concurrencia de dos workers ni SKIP LOCKED PostgreSQL**. Se mantienen sin cambio NOT EXISTS, locks y single-claim. Recuperación de operaciones comerciales/replay/handoff y transporte concurrente cubierta con los fakes existentes.
- Pendiente en Oracle: imagen activa, trazas/estados del incidente y evidencia del proveedor. Verificar que el colector conserve scopes WorkspaceId/InboundId; validar costo del diagnóstico con volumen real. No se ejecutó nada en servidores ni servicios externos.
- Al cerrar Sprint 1, Sprint 2 aún no estaba autorizado. La autorización posterior y resultados se registran debajo. Causa operacional única: **PENDIENTE DE EVIDENCIA**.

## Sprint 2 — criterios y decisiones

- **NF-05 OK:** el parser existente reconoce frases completas de selección con cardinales/ordinales. Se conserva fingerprint, índice/página, revalidación de datos y módulos; menú caducado se actualiza, selección inválida aclara. No extrae índices de DNI, teléfonos, cantidades, fechas, horas o instrucciones mixtas. No confirma operaciones comerciales. Borradores/replay/interrupciones y cuotas existentes conservados.
- La secuencia del incidente sigue generando cinco respuestas, sin pedidos/reservas. Ahora usa **dos llamadas IA en lugar de tres**: selección sin picker válido se aclara determinísticamente. Se actualizó únicamente ese conteo del control; las aserciones de aceptación NF-05 y las dos NF-04 no se cambiaron.
- **NF-04 OK:** errores/reintentos independientes por panel; caché autorizada sigue visible con aviso de desactualización ante 500/503/network. Acciones bloqueadas durante error/revalidación; 401/403 o READ revocado ocultan datos. Recuperación tras denegación espera ambas consultas. Estado de servidor permanece en TanStack Query; el único estado nuevo es el envío de la recuperación conjunta, ligado a identidad. Borrador de texto/dialogo ligado a usuario/workspace/chat. Polling continúa en 15 s y se pausa en pestaña oculta, sin reenvíos.
- **NF-07 OK:** AddMessage escribe mensaje y máximo de actividad en una transacción; conserva timestamp original y TTL de 90 días. Confirmación outbound compara contra el snapshot que ya leía, sin lectura extra. CreateConversation también conserva el máximo en caso de reescritura; GetOrCreate genera un documento nuevo. Handoff/cierre/fallos no escriben lastMessageAt. Actualización optimista frontend tampoco retrocede la actividad.
- Costo: una lectura de conversación por AddMessage, necesaria para comparar bajo concurrencia, manteniendo las dos escrituras; reintentos de conflicto pueden repetir lecturas. CreateConversation añade una lectura para impedir el bypass por upsert. Sin listener, colección, índice, campo o contrato nuevo.
- Fake Firestore ampliado para ejecutar filtros, orden y cursores reales del contrato usado por las pruebas; no equivale a certificar el motor ni sus índices/cuotas reales.

## Archivos de Sprint 2

Backend:

- `backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/ConversationRecoveryService.cs`
- `backend/NexFlow.Infrastructure/Persistence/Firestore/FirestoreConversationRepository.cs`
- `backend/NexFlow.Tests/Fakes/InMemoryFirestore.cs`
- `backend/NexFlow.Tests/Rc3Sprint0IncidentTests.cs`
- `backend/NexFlow.Tests/Rc3Sprint2MenuTests.cs` (nuevo)
- `backend/NexFlow.Tests/Rc3Sprint2ActivityTests.cs` (nuevo)

Frontend:

- `frontend/src/features/conversations/hooks/useConversations.ts`
- `frontend/src/features/conversations/pages/InboxPage.tsx`
- `frontend/src/features/conversations/components/ConversationThread.tsx`
- `frontend/tests/rc3-inbox-browser.test.mjs`
- `frontend/tests/rc3-inbox-preview.server.mjs`
- `frontend/tests/fixtures/rc3-inbox-api.ts`
- `frontend/tests/fixtures/rc3-inbox-preview.tsx`
- `frontend/tests/fixtures/rc3-inbox-store.ts` (nuevo; no modifica fixtures de otros módulos)

Registros: `NexFlow_RC3_PROGRESS.md`, `INCIDENTE_CAUSA_RAIZ.md`.

## Validación Sprint 2 y pendientes (histórico)

| Comando | Resultado real final |
|---|---|
| `dotnet build backend/backend.slnx --no-restore` | Correcto, 0 errores; 3 warnings preexistentes: CS8981 x2, CS8602 x1. |
| `dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore` | **565 aprobadas, 0 fallidas, 0 omitidas**: 531 previas + 34 nuevas. Incluye controles Sprint 1, reservas/pedidos/handoff, Redis, UnknownDelivery y secuencia del incidente. |
| `npm run build` desde frontend | Correcto; `dist` generado. Warning conocido de chunk JS >500 kB (710,65 kB). |
| `npm run lint` desde frontend | Correcto, exit 0. |
| `node --test tests/rc3-inbox-browser.test.mjs` desde frontend | **18 aprobadas, 0 fallidas, 0 omitidas**: tres originales + quince nuevas. Incluye recuperación independiente/conjunta, sin caché, 0/500/503, 401/403 con GET pendiente, revocación READ, selección, workspace/respuesta tardía, cinco estados outbound, móvil y pausa real de polling 16 s. |
| `node --test tests/workspace-navigation.test.ts tests/whatsapp-state.test.ts tests/navigation-browser.test.mjs tests/evolution-navigation-browser.test.mjs` desde frontend | **41 aprobadas, 0 fallidas, 0 omitidas**. Error React deliberado de la fixture de error boundary contenido correctamente; no error de aplicación real. |

Las tres rojas anteriores (NF-05 + dos NF-04) están verdes, sin skips ni debilitamiento de esas aserciones. Revisión del diff limitada al sprint; sin cambios de contratos, infraestructura, dependencias, configuración, secretos, plan maestro, AGENTS, PDF, n8n o Evolution.

Pendiente: validación con Firestore/PostgreSQL reales, índices/cuotas/latencias bajo carga y evidencia operacional de Oracle. Los fakes verifican intercalados/serialización/lógica, no certifican disponibilidad, límites Spark ni locks PostgreSQL. Nuevas transacciones pueden reintentar bajo contención. Nada ejecutado en servicios externos; sin commit, push, migraciones ni despliegue. **Sprint 3 NO autorizado.**

## Sprint 3 — NF-08 / NF-12

El estado de autorización anterior corresponde al cierre histórico de Sprint 2. Posteriormente el usuario aprobó ese sprint y autorizó exclusivamente el presente alcance.

### NF-08: causa y estrategia principal

**CONFIRMADO:** CatalogController/ServicesController enviaban invalidación a Channel en memoria con DropWrite sin esperarla. No existía vínculo durable entre confirmación de la mutación y esa tarea. GetArtifact ya comprobaba hash, pero faltaba una barrera compartida con escritores para resolver carreras durante lectura/publicación.

**OK — invalidación vinculada a la transacción de fuente en Firestore:**

- Guardado/baja de productos y servicios, alta/edición/borrado de categorías: leen artefactos afectados dentro de la transacción existente y cambian PersistenceVersion; Current pasa conservadoramente a Stale. SHARED abarca PRODUCT y SERVICE. Las bajas de offering también son transaccionales.
- Perfil comercial y sedes forman parte del hash: sus escritores cercan ambos ámbitos dentro del mismo commit. No se modificaron reservas/pedidos.
- Todas las lecturas del grupo de artefactos preceden a sus escrituras, incluyendo SHARED; no se usa lectura seguida de escritura desprotegida.
- Una mutación confirmada y su invalidación se confirman juntas. Si Firestore rechaza el commit, no se confirma la mutación. Si se pierde el ACK después de commit, los dos cambios ya son durables: el cliente debe consultar antes de repetir. No existe una tarea crítica pendiente en RAM ni una segunda escritura SQL para esa invalidación.
- GetArtifact valida Current y Stale contra la fuente y relee la versión después del hash; reintenta hasta tres veces ante carreras. Si no puede confirmar, falla; no devuelve Current por suposición. Hash idéntico restaura Current sin alterar Origin, PDF, diseño ni fecha. El camino de reutilización de generación también relee la versión.
- Mismo hash/diseño de un PDF generado devuelve Current sin reservar cuota ni crear Outbox. Diseño diferente sigue generando. Uploaded mantiene su origen y su política previa: hash como snapshot de la fuente al subir, no como prueba semántica del contenido manual; una modificación obliga a revalidar. Solicitar generación de un Uploaded sigue permitido con confirmación.
- Durante Generating cambia la versión para cercar callbacks ya leídos; se conserva plazo/identidad y el callback valida hash actual antes de publicar. Una subida pendiente pierde su lease de publicación al cambiar la fuente: el intento durable existente permite limpiar solo el archivo no referenciado, sin eliminar el PDF previo.
- Los controladores ya no producen tareas de invalidación volátiles. GenericTaskQueue y su registro/worker se conservan; se revisaron referencias restantes y no se eliminó infraestructura compartida.

### Garantías reales y ventanas

La atomicidad descrita cubre únicamente documentos de la misma transacción Firestore. La reserva Firestore + encolado PostgreSQL continúa sin atomicidad distribuida: la reserva puede sobrevivir a un error/ACK SQL incierto, conserva su cuota y expira por recuperación existente; no se reembolsa ni reencola a ciegas.

Las respuestas Current se validan en un punto de lectura/commit; una edición posterior puede volverlas obsoletas. URLs públicas de Cloudinary entregadas previamente no pueden revocarse mediante estos cambios. El endpoint devuelve Stale/Failed/Generating para versiones no confirmadas; WhatsApp solo obtiene PDF automático mediante GetArtifact y Current. No se añadieron tablas, documentos, campos, índices, nuevos servicios ni migraciones.

Costo: una lectura y, si existe, una actualización de artefacto por scope afectado y mutación; SHARED/perfil/sedes tienen hasta dos. GetArtifact añade una segunda lectura del artefacto para validar la barrera; Stale también calcula hash. Conflictos pueden repetir lecturas. No se añadió un barrido periódico de todos los catálogos: recuperación/revalidación al leer, idempotente tras reinicio. Carga y límites Spark reales pendientes.

### NF-12: máquina de estados y evidencia

**OK local / PENDIENTE operacional:**

- NotGenerated → Generating: reserva de generación y cuota atómicas en Firestore; un perdedor de CAS no consume cuota. Máximo tres generaciones por workspace/día compartidas entre ámbitos, sin refund.
- Generating → Current: únicamente callback válido, no expirado, con workspace/scope/GenerationId/SourceHash coherentes y CAS. Duplicados secuenciales o concurrentes conservan el primer resultado durable. La carrera todavía activa devuelve conflicto para exigir validación nueva; no publica ciegamente. HMAC/timestamp permanecen intactos.
- Current → Stale: mutación de fuente dentro del mismo commit; Stale → Current solo si la fuente vuelve a coincidir con el PDF anterior. No se regenera automáticamente.
- Generating → Failed: deadline configurado (cinco minutos), cancelación lógica o entrega Outbox definitivamente fallida. Cancelled se representa con Failed + Catalog.GenerationCancelled, sin enum/contrato nuevo. PDF anterior se conserva.
- Outbox: HTTP 404/503/timeout se reintentan con la identidad estable hasta el límite existente de cinco intentos; JSON/identidad inválida es terminal. Processed acredita aceptación del webhook, no renderización PDF. Los eventos Failed no forman parte de ClaimAsync.
- Un workflow aceptado cuyo Gotenberg/Cloudinary falla o no retorna callback solo puede clasificarse localmente como ausencia de callback/timeout, no como causa específica del renderer/storage. RecoveryWorker persiste Failed al alcanzar el workspace después del deadline; si Firestore está caído, se recupera al volver. No se promete un SLA exacto de cinco minutos bajo indisponibilidad o cola de workspaces.
- Idempotency-Key se conserva hacia n8n; backend garantiza una sola publicación válida, no que n8n cree físicamente un solo archivo. Verificar deduplicación/reintentos del workflow externamente antes de certificar ese comportamiento. UnknownDelivery de WhatsApp mantiene su ciclo durable sin segundo transporte automático.
- Cuatro eventos históricos: no se leyeron ni reinyectaron. Su causa concreta no es demostrable con código local. No atribuirlos a NF-08 ni a un proveedor específico.

### Archivos de Sprint 3

Backend:

- `backend/NexFlow.API/Controllers/Business/CatalogController.cs`
- `backend/NexFlow.API/Controllers/Business/ServicesController.cs`
- `backend/NexFlow.API/Controllers/Webhooks/N8nWebhookController.cs`
- `backend/NexFlow.Application/Features/Business/CatalogGenerationService.cs`
- `backend/NexFlow.Domain/Entities/Catalog/CatalogArtifact.cs`
- `backend/NexFlow.Infrastructure/Persistence/Firestore/FirestoreCatalogArtifactRepository.cs`
- `backend/NexFlow.Infrastructure/Persistence/Firestore/FirestoreCatalogRepository.cs`
- `backend/NexFlow.Infrastructure/Persistence/Firestore/FirestoreBusinessProfileRepository.cs`
- `backend/NexFlow.Infrastructure/Persistence/Firestore/FirestoreLocationRepository.cs`
- `backend/NexFlow.Tests/CatalogCallbackTests.cs`
- `backend/NexFlow.Tests/CatalogHttpTests.cs`
- `backend/NexFlow.Tests/Fakes/CatalogFixture.cs`
- `backend/NexFlow.Tests/Rc3Sprint3ArtifactConsistencyTests.cs` (nuevo, 23 casos)

Frontend: `frontend/src/features/artifacts/components/ArtifactGenerator.tsx`, `frontend/tests/artifacts-browser.test.mjs`, `frontend/tests/fixtures/artifacts-api.ts`. Solo distingue el resultado Current reutilizado en el toast, usando el contrato existente.

Registro: `NexFlow_RC3_PROGRESS.md`.

### Pruebas y resultados reales

- `dotnet build backend/backend.slnx --no-restore`: correcto, último incremental con cero errores/advertencias. Recompilación previa: CS8981 x2 y CS8602 x1 preexistentes (línea desplazada por cambios).
- `dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore`: **588 aprobadas, 0 fallidas, 0 omitidas** (565 anteriores + 23 nuevas). Incluye sprints 0/1/2 sin skips ni cambios de expectativas.
- Filtro de catálogo/Outbox/EvolutionMedia: **133 aprobadas, 0 fallidas, 0 omitidas** antes de los tres últimos casos de carreras de lectura/reutilización; estos también pasan en la suite final de 588.
- Nuevos casos: PRODUCT/SERVICE, Generated/Uploaded, categorías SHARED, fallo/ACK de commit, reinicio, lectura indisponible, cambios concurrentes/tenant separado, callback/publicación concurrente, hash/diseño idénticos, cancelación de upload por cambio de fuente y limpieza, perfil/sede/baja, callbacks duplicados firmados, endpoint Stale y barrera durante lectura/reutilización.
- Suites existentes ejercitan cuota/concurrencia, callbacks obsoletos/HMAC/deadline, worker sin página, 404/503/timeout de n8n mediante HttpMessageHandler local, fallos de almacenamiento y UnknownDelivery sin reenvío.
- `npm run build`: correcto, dist generado; warning conocido de chunk JS >500 kB (710,71 kB).
- `npm run lint`: correcto, exit 0.
- `node --test tests/artifacts-browser.test.mjs tests/categories-browser.test.mjs`: **33 aprobadas, 0 fallidas, 0 omitidas**, incluidas recuperación HTTP real por timeout en localhost y reutilización sin cuota. No servicios externos.
- Diff revisado únicamente en alcance. Sin cambios de configuración, secretos, dependencias, plan, AGENTS, infraestructura, reservas/pedidos o workflows; sin commit, push, migraciones o despliegue. **Próximo sprint: NO AUTORIZADO (Sprint 4).**

### EVIDENCIA OPERACIONAL PENDIENTE

Consultas PostgreSQL de solo lectura propuestas; **NO ejecutadas**. No seleccionar PayloadJson ni Error libre. Los códigos Status provienen del enum vigente: 0 Pending, 1 Processed, 2 Failed, 3 Processing.

```sql
BEGIN TRANSACTION READ ONLY;
SELECT "Status", "RetryCount", count(*) AS total,
       min("CreatedAt") AS oldest_utc, max("CreatedAt") AS newest_utc
FROM "OutboxMessages"
WHERE "EventType" = 'CATALOG_GENERATION_REQUESTED'
GROUP BY "Status", "RetryCount"
ORDER BY "Status", "RetryCount";

SELECT "Id", "WorkspaceId", "Status", "RetryCount", "CreatedAt",
       "ProcessedAt", "NextRetryAt", "LeaseUntil",
       CASE WHEN "Error" IN ('HttpRequestException', 'TaskCanceledException',
                            'TimeoutException', 'JsonException', 'InvalidOperationException')
            THEN "Error" WHEN "Error" IS NULL THEN NULL
            ELSE 'OTHER_REDACTED' END AS safe_error_kind
FROM "OutboxMessages"
WHERE "EventType" = 'CATALOG_GENERATION_REQUESTED' AND "Status" = 2
ORDER BY "CreatedAt" DESC
LIMIT 100;
COMMIT;
```

Contrastar esos IDs/tiempos con imagen/digest activo y logs privados sanitizados del worker/n8n: HTTP de aceptación, número de intentos, ejecución del workflow, resultado del paso Gotenberg/Cloudinary y callback (código/fecha/ID, sin payload/URL firmada/secretos/PII). Error SQL puede contener solo el tipo de excepción y no distingue por sí solo 404 de 503.

Verificar en entorno autorizado la deduplicación por Idempotency-Key/GenerationId y política de retry del callback ante 409/503. Medir consumo/contención Firestore, índices existentes y latencia de scans. Los dobles locales no certifican locks PostgreSQL, cuotas/índices reales, renderización, disponibilidad ni almacenamiento externo. No reinyectar los cuatro eventos como método diagnóstico.
