# NexFlow RC-3 — memoria de progreso

Actualización: 2026-10-20 (America/Lima). La adenda de recuperación final al pie es el estado vigente; las secciones anteriores conservan evidencia histórica.
Antes de cada sesión leer `AGENTS.md`, `NexFlow_PLAN_MAESTRO_CORRECCION_MVP_RC3.md` y este registro.

## Estado

- Estado vigente: **recuperación final RC-3, fases 0–8 locales completadas**; detalle/evidencia en adenda final. Historial Sprints 0–7 preservado.
- HEAD local/main: `df5cfac0159bdf65ad915f9197927303b6df69cd`. Estado inicial limpio; `88db9e0` retira caché y `df5cfac` contiene seguridad/tests/CI/docs 5–7. Nuevas correcciones de recuperación **solo en diff local sin commit**, no incluidas en ese SHA.
- Autorización: concluir todo el alcance LOCAL de recuperación final; sin Oracle, servicios SaaS, workflows remotos, migraciones productivas, commit/push ni despliegue.
- Validación vigente: **708 backend aprobadas / 24 integraciones omitidas**, **83 TypeScript + 145 navegador = 228 frontend aprobadas**, builds/lint y publish API/Migrator ARM64 correctos. Smoke codec Windows 3 formatos OK; ARM64 real pendiente.
- Veredicto: **CODE COMPLETE — LISTO PARA VALIDACIÓN DE PREPRODUCCIÓN — RELEASE PENDIENTE / NO-GO**. No habilitar nuclear, no mezclar escritores antiguos, no avanzar con despliegue sin evidencia/autorización operacional.

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

## Sprint 4 — NF-09 e integridad cruzada (2026-10-19)

Referencia y árbol inicial: `96e472cdd041ed7443088b85b4e1510274bff4ab`, limpio. Se leyeron contrato, plan, progreso e incidente; se preservan las secciones históricas. Únicamente backend, pruebas relacionadas y este registro modificados. No se ejecutaron servicios externos, Docker, migraciones, commit, push ni despliegue.

### Hallazgos y correcciones verificadas

| Área | Evidencia / garantía real |
|---|---|
| NF-09, servicios | CONFIRMADO en código: DeleteService hacía soft-delete sin consultar SQL. Ahora consulta las sedes de reservas `Confirmed` con inicio >= reloj UTC por `WorkspaceId + ServiceId`, sin tracking, y devuelve 409 `Service.FutureReservations`. La actualización también impide desactivar, quitar RequiresReservation, cambiar duración o retirar una sede con reservas futuras. Cambios de nombre/precio y sedes no afectadas siguen permitidos. Sin reservas futuras conserva baja lógica y reactivación. No borra historia ni cancela reservas. |
| Sedes | Guarda SQL existente conservada. Repositorio Firestore sigue comprobando referencias, incluyendo inactivas/legacy, y realiza cambio de principal, eliminación de horarios y sede e invalidación PDF dentro de su transacción. Pruebas con repositorios reales/SDK y transporte simulado. Disponibilidad y reagendamiento ahora rechazan una sede que ya no existe; creación ya la comprobaba. |
| Carrera entre bases | REPRODUCIDA para servicio y sede: pausar la baja después de leer ausencia SQL; confirmar una reserva; continuar la escritura Firestore. Ambas operaciones terminan y queda una reserva referenciando sede ausente/servicio inactivo. Dos pruebas `Characterization_cross_store_guard...` documentan el defecto pendiente: su resultado verde verifica la reproducción, NO una solución de la carrera. |
| Concurrencia de reservas | REPRODUCIDO antes del parche: tres casos rojos de reagendar/cancelar/completar devolvían conflicto xmin sin descartar entidad/evento del tracker. Ahora las cuatro rutas (también CancelActive del bot) descartan reserva y evento de Outbox perdedores mediante IUnitOfWork existente. Cuatro regresiones verdes. Dos contextos EF con versión avanzada manualmente prueban que una cancelación ganadora no se sobrescribe por un reagendamiento antiguo. SourceMessageId, IDs, UTC, estados, xmin y Outbox SQL existentes se conservan. |
| Historia de reservas | REPRODUCIDO: renombrar servicio conserva ServiceId/LocationId/SourceMessageId/UTC; el panel resuelve el nombre del catálogo actual. No existe etiqueta original persistida. No se inventó backfill ni se añadieron columnas/DTO/cambios de UI. Propuesta condicionada abajo. |
| Horario cancelado | REPRODUCIDO: disponibilidad considera libre el horario cancelado, pero el índice único sin filtro impide otra reserva idéntica. Coinciden modelo EF y migration histórica ReservationConcurrency. Prueba SQLite ejerce unicidad real local, NO confirma DDL de Oracle. Corregir el índice necesita autorización de migración; no se modificó. |
| Pedidos | Snapshots reales de producto activo, nombre, precio y moneda; cantidades positivas, overflow y monedas mixtas conservan política PendingReview, sin total inventado. Se añadió una comprobación transaccional de documentos de catálogo y creación del pedido en Firestore: cambio entre lectura autorizada y commit rechaza el envío (ConcurrencyException/409 global), sin persistir precio obsoleto. Replay devuelve snapshot histórico sin recalcular; distinto ID produce otro pedido. No se añadió LocationId: el contrato actual de pedido no modela sede. |
| Solicitudes/avisos | CONFIRMADO: el replay devolvía solicitud existente sin recuperar una notificación fallida. Ahora vuelve a intentar el aviso con ID SHA256 derivado de workspace+módulo+tipo+ID comercial. SQL usa la PK existente como deduplicación, incluso si el aviso ya fue leído; recupera únicamente 23505 de PK_Notifications del mismo workspace, no otros constraints/tenants. RequestService usa el registro realmente persistido. OrderFlow aplica el mismo patrón y cierra borrador después del aviso. ACK perdido tras escritura comercial o de aviso recupera el mismo efecto; no duplica solicitudes/pedidos. |

No se modificó autenticación, filtros de permisos, licencias, mecanismo de cooldown de avisos PDF, envío WhatsApp ni UnknownDelivery. Consulta informativa y regresiones RC-1/RC-1.1/Sprints 1–3 permanecen en la suite completa. Los contratos HTTP existentes permanecen; la nueva denegación funcional de servicios es 409.

### Decisiones de concurrencia y ventanas residuales

**No se atribuye atomicidad PostgreSQL/Firestore a una consulta Any/Distinct, a TransactionScope ni a un recheck.** Se implementa la guarda mínima sobre reservas ya confirmadas, con fallo cerrado si SQL no responde. La validación de creación/reagendamiento sigue centralizada en ReservationEngine para web y WhatsApp. La carrera empieza tras la lectura SQL y termina al confirmarse la modificación Firestore; una reserva que ya leyó configuración también puede confirmar después de la baja. La solución integral requiere coordinación de todos esos escritores, no un mutex en memoria.

Se descartó mantener una transacción SQL larga bloqueada durante llamadas Firestore: eleva contención, usa conexiones mientras espera otra dependencia y no resuelve por sí sola un ACK incierto/commit remoto tardío. Los advisory locks existentes se limitan a notificaciones; no hay hoy coordinador de ciclo de vida de servicio/sede reutilizable que cubra reservas y cambios Firestore. Tampoco se añadió un recheck engañoso que solo estreche la ventana ni compensación que cancele una reserva confirmada.

**Propuesta pendiente de autorización (no implementada):** fence durable por workspace/recurso y versión, compartido por altas/reagendamientos y modificaciones; estados de cambio pendiente/confirmado, ID estable y escrituras Firestore condicionadas por esa operación. SQL debe comprobar el fence dentro de la transacción de reserva. Aplicación remota fuera de transacción SQL larga y recuperación de ACK incierto con el Outbox/worker existente antes de liberar el fence; no 2PC. Requiere acordar su modelo/migración y ampliar escritores/coordinación. Pruebas de aceptación: dos procesos, corte antes/después de cada commit, restart, operación antigua después de expirar lease, sin liberar el recurso por timeout ambiguo; verificar locks/aislamiento en PostgreSQL real autorizado. Un lock temporal solo no satisface esos fallos.

Solicitudes/pedidos Firestore y avisos SQL tampoco son atómicos. Recuperación implementada usa el mismo inbound durable mientras conserve reintentos; no confirma la notificación hasta que SQL acepta su escritura. Si el inbound llega a DeadLetter antes del aviso o no existe SourceMessageId estable, NO hay reconciliación independiente nueva. Operación comercial puede quedar confirmada y aviso pendiente; no reenviar WhatsApp ni repetir manualmente la operación comercial para corregir el aviso. El estado del aviso SQL no significa entrega de WhatsApp: Sent/Failed/UnknownDelivery existentes conservan su significado, sin reenvío automático de inciertos.

Para reconciliación independiente se propone intención de aviso en la misma escritura Firestore del negocio y extensión autorizada del worker existente para materializar el mismo ID SQL; no crear otro Outbox/worker. Los avisos históricos aleatorios no se pueden asociar inequívocamente con la nueva identidad: replay explícito de un negocio previo al parche puede producir un aviso adicional, nunca otro negocio por el mismo ID. No se reinyectaron históricos. Pedidos web generan un ID nuevo en cada POST; solicitudes sin SourceMessageId también. Retries HTTP manuales de esos contratos pueden duplicar negocio y necesitan un contrato de idempotencia estable acordado (no se inventó en frontend ni se afirmó cobertura).

### Cambios estructurales propuestos, NO ejecutados

1. **Etiquetas históricas (mejora condicionada):** nullable ServiceNameSnapshot/LocationNameSnapshot en Reservation, tomados de datos autorizados al crear, nunca de IA. DTO aditivo opcional y UI con fallback marcado como nombre actual para filas antiguas; nunca asignar nombres actuales como hechos históricos. Migration aditiva, desplegar lector compatible y después escritor; datos anteriores null y sin alteración de reservas válidas. Rollback de aplicación mantiene columnas; eliminar columnas después perdería snapshots nuevos, por lo que exige aprobación y conservación previa. No se puede recuperar el nombre original de filas antiguas con evidencia local disponible. No es parche urgente de chat.
2. **Índice de horarios cancelados:** sustituir en una nueva migration aprobada el índice único actual por uno con el mismo conjunto de columnas y filtro de estados ocupantes (`Status <> 'Cancelled'`, conforme a IsTimeSlotAvailableAsync). Conservar Serializable/xmin, no eliminar filas ni modificar migration histórica. Antes de migrar, verificar duplicados y DDL real con acceso autorizado. Down al índice sin filtro NO es seguro si ya se reutilizaron horarios cancelados: debe bloquearse ante duplicados, nunca borrar/alterar historia automáticamente. Operación y rollback requieren decisión explícita.
3. **Fence y reconciliación:** diseñar/autorizar el protocolo de ciclo de vida descrito arriba y sus cambios durables antes de afirmar que NF-09 queda cerrado ante concurrencia distribuida.

### Pruebas reales del Sprint 4

| Comando / alcance | Resultado |
|---|---|
| `dotnet build backend/backend.slnx --no-restore` final | Correcto, exit 0; **0 errores, 0 advertencias** incremental. En recompilación anterior reaparecieron solo CS8981 x2 y CS8602 x1 preexistentes. |
| `dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore` final | **628 aprobadas / 0 fallidas / 0 omitidas**, baseline 588 conservado + **40 casos nuevos**. Incluye Sprint 3 PDF completo y callback HTTP, asistentes, reservas, pedidos, solicitudes, sedes y Evolution simulado. |
| Filtro `FullyQualifiedName~Rc3Sprint4\|FullyQualifiedName~ReservationsPeruTimeTests\|FullyQualifiedName~ReservationsRepositoryTests` sobre el mismo proyecto, --no-restore | **58 aprobadas / 0 fallidas / 0 omitidas**. |
| Reproducción previa `FullyQualifiedName~Lost_xmin_write` | **0 aprobadas / 3 fallidas** antes del parche; se conservan expectativas y se añade cancelación activa al cierre, cuatro casos verdes. |
| Frontend: `node --import ./tests/reservations-loader.mjs --test tests/reservations-agenda.test.ts` | **32 aprobadas / 0 fallidas / 0 omitidas**. |
| Frontend: `node --test --test-concurrency=1 tests/reservations-browser.test.mjs tests/artifacts-browser.test.mjs tests/categories-browser.test.mjs tests/category-management.test.ts tests/workspace-navigation.test.ts tests/whatsapp-state.test.ts tests/navigation-browser.test.mjs tests/evolution-navigation-browser.test.mjs` | **123 aprobadas / 0 fallidas / 0 omitidas** (reservas navegador 36; PDF/categorías navegador 33; categorías unidad 13; navegación/WhatsApp 41). Total frontend **155/155**. |

El primer comando frontend combinado sin cargador y con fixtures concurrentes terminó exit 1: agenda TypeScript/JSX sin su loader y bootstrap de categorías no cargado. Se repitió agenda con su loader existente y navegadores en serie (los Vite compartían optimizador y mostraban reoptimización concurrente); no se cambiaron expectativas, timeouts ni frontend para forzar verde. El error React deliberado de la fixture de error boundary fue contenido como esperaba su prueba. No se volvieron a ejecutar build/lint frontend porque no se modificó frontend, conforme a AGENTS. Inbox 18 no se repitió: sin cambios de su código ni contrato en este sprint.

Cobertura obligatoria: baja con/sin futuras y pasadas/canceladas (controlador + consulta SQL); duración/RequiresReservation/sedes; dos tenants; carreras servicio/sede; baja de sede/principal/horarios/referencias; reactivación; regla común web/bot; EF xmin y limpieza de perdedores; snapshots de pedidos/replay/concurrencia/ACK perdido/inactivos/monedas/overflow; solicitudes con ACK perdido y fallo SQL antes/después de commit, aviso leído y aislamiento; aviso de pedido fallido y recuperación del borrador; regresiones informativas y PDF dentro de la suite existente. Cuatro casos nuevos son caracterizaciones verdes de problemas PENDIENTES (dos carreras, etiquetas históricas, índice cancelado); no se cuentan como defectos corregidos.

Límites: SQLite ejecuta filtros/unicidad/predicados EF reales, con xmin avanzado manualmente; NO prueba locks, Serializable ni generación xmin de PostgreSQL. Error 23505/constraint se simula para la rama de recuperación. Firestore usa SDK real sobre RPC en memoria, sin red, extendido únicamente con precondición Exists y ArrayContains necesarios; NO demuestra transacción entre bases, índices/costo/contención reales. La nueva validación transaccional de pedidos añade lecturas de catálogo (una por ítem más lectura del pedido), costo/latencia reales pendientes. No se validó Oracle, Firestore real, WhatsApp real ni PDFs generados.

### Inventario exacto de archivos del Sprint 4

Rutas relativas a `C:\Proyectos\NexFlow`:

- Backend implementación: `backend/NexFlow.API/Controllers/Business/ServicesController.cs`; `backend/NexFlow.Application/Abstractions/IReservationRepository.cs`; `backend/NexFlow.Application/Engines/Reservation/ReservationEngine.cs`; `backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/Flows/OrderFlow.cs`; `backend/NexFlow.Application/Features/Notifications/NotificationService.cs`; `backend/NexFlow.Application/Features/Requests/RequestService.cs`; `backend/NexFlow.Domain/Entities/System/Notification.cs`; `backend/NexFlow.Infrastructure/Persistence/Firestore/FirestoreOrderRepository.cs`; `backend/NexFlow.Infrastructure/Persistence/PostgreSQL/Repositories/NotificationRepository.cs`; `backend/NexFlow.Infrastructure/Persistence/PostgreSQL/Repositories/ReservationRepository.cs`.
- Backend pruebas: `backend/NexFlow.Tests/Fakes/InMemoryFirestore.cs`; `backend/NexFlow.Tests/ReservationsPeruTimeTests.cs`; `backend/NexFlow.Tests/ReservationsRepositoryTests.cs`; nuevos `backend/NexFlow.Tests/Rc3Sprint4IntegrityTests.cs` y `backend/NexFlow.Tests/Rc3Sprint4CommercialTests.cs`.
- Registro: `NexFlow_RC3_PROGRESS.md`.
- Frontend, esquemas, migraciones, configuración, secretos e infraestructura: **ningún cambio**.

**SPRINT 4 NO APROBABLE como cierre completo del DoD; NO-GO de integridad integral.** Parches mínimos comprobados, sin regresiones locales; carrera entre bases e índice cancelado siguen reproducidos y requieren autorización de cambios estructurales. Se detiene para revisión. **Sprint 5 NO AUTORIZADO.**

## Sprint 4.1 — recuperación del commit parcial 16efe69 (2026-10-19)

### Estado recuperado y límites

Se ejecutaron primero `git status --short`, `git log -3 --oneline`, `git diff --stat`, `git diff --check`. HEAD completo: `16efe690278cb8bbab82ee2a3b6e9c2001147d5c`, árbol limpio; sin cambios locales adicionales. Se releen contrato, plan, registro e incidente. No se rehízo la implementación publicada: ya contenía coordinador, entidad/configuración SQL, fence, worker, DI, motor/repositorios coordinados, nueva migración/Designer/snapshot, fake y adaptación de pruebas.

Faltaban pruebas de recuperación/ACK/fences y convivencia de ediciones, SQL exportado y documento de arquitectura; no había constructores rotos ni archivos sintácticamente incompletos. El registro todavía indicaba Sprint 4. Validación inmediata antes de nuevas ediciones: build correcto (0 errores, warnings conocidos CS8981 x2 y CS8602 x1), **628/628 tests**. No se confundió falta de PostgreSQL local con regresión: `psql`/`postgres` no disponibles en PATH; no se instaló nada ni se llamó Oracle.

### Correcciones respecto de 16efe69

- **CONFIRMADO/REPRODUCIDO:** SaveService pendiente podía sobrescribir nombre/precio confirmado después. Prueba roja encontró `Service` en vez del nombre concurrente. Ahora la intención incluye UpdateTime observado y su transacción exige igualdad. Una edición inocua ganadora se conserva; una edición inocua que reintentara restaurar campos críticos después de otra transacción falla con conflicto. Marcador Rejected arbitrado antes de completar SQL. No se añadió ETag al contrato HTTP; la versión pertenece al payload durable interno.
- **CONFIRMADO/REPRODUCIDO:** UnsupportedAction quedaba Pending tras reinicios. Se clasifica payload inválido; con identidad válida, worker sella rechazo en Firestore antes de completar SQL. Applied previo gana; red/timeout/permisos no se convierten en rechazo. Identidad corrupta sigue bloqueada para revisión administrativa, no force-unlock.
- **CONFIRMADO/REPRODUCIDO:** Down nuevo no tenía `END;`; corregido y scripts regenerados. No se tocaron migraciones históricas ni esquema real.
- **CONFIRMADO por implementación relacionada:** worker nuevo no participaba en TenantLifecycleLock que ya mantenían middleware y Outbox. Ahora verifica workspace existente/no Deleting bajo la misma guarda; límite de intento 30 s. No se retiró la guarda del middleware para simular ausencia de SQL durante IO. Worker confirma SQL también ante resultado funcional Rejected; la API sigue devolviendo errores. Una integración local comprueba que esa finalización no vuelve a rollback por lanzar el error después de guardarla.
- Repetición de baja ya completada no crea nuevo epoch ni invalida otra vez el catálogo. Sede ya ausente retorna éxito sin escritura. Reintento de intención todavía Pending conserva ID/versión; un intento antiguo no desactiva un servicio reactivado con fence nuevo.
- Casos de recuperación añadidos: fallos antes/después de commit de intención SQL, ACK Firestore perdido, SQL falla antes/después de completar, caída antes de Firestore, otro worker/objetos sobre estado compartido, duplicados, cancelación del host, recursos/workspaces independientes y web/bot concurrentes. Se comprueba estado final e inexistencia de negocio duplicado.

### Garantías y pendientes P0/P1

**P0:** captura SQL antes de leer Firestore, validación dentro del Serializable con locks Location→Service, intención durable monotónica, fence terminal atómico con negocio, CAS de finalización y recuperación independiente de página siguen implementados. Las dos caracterizaciones originales ya transformadas en 16efe69 conservan sus aserciones de rechazo de baja y datos intactos; no se eliminaron ni debilitaron. Nuevos tests cubren reserva con lectura obsoleta, dos coordinadores, restart lógico, ACK perdido y ediciones críticas/inocuas. No se afirma atomicidad entre bases ni garantía distribuida probada por los fakes.

**Limitación J explícita:** los row locks de recursos no abarcan Firestore. Sin embargo, el protocolo de borrado de tenant preexistente usa una transacción/advisory lock compartido durante la petición, y ahora también durante el intento de recuperación. Suprimirla abriría un bypass frente al borrado nuclear. Se preserva justificadamente, con IO del worker limitado a 30 s; conexión ocupada/contención y el caso combinado de borrado, limpieza parcial y ACK incierto requieren validación/decisión de integración. No se rediseñó el borrado de tenants. No se declara cumplida la ausencia total de SQL durante IO remoto.

**P1 CORREGIDO localmente:** índice único parcial con `Status <> 'Cancelled'`, coherente con Confirmed/Pending/Completed ocupantes. Pruebas ejecutan dos cancelaciones históricas y una vigente con misma clave, rechazo de otra vigente, unicidad independiente SourceMessageId incluso tras cancelar y reagendamiento a clave ocupada sin reescribir datos. ModelSnapshot/Designer del commit parcial permanecen sincronizados y sin cambios en esta recuperación. Up/Down exportados; no migración aplicada.

Down exige ambas tablas bajo ACCESS EXCLUSIVE, rechaza si existen epochs (incluso Ready) o historial duplicado y no modifica reservas. Solo suprime entrada EF history si las guardas pasan. Rollback seguro de aplicación mantiene el esquema y requiere binario compatible con fences/UpdateTime; no volver a API preprotocolo con escrituras. Después de uso normal Down se rechazará: no borrar metadatos/historia para forzarlo. Rollout mixto con API antigua no es seguro; detener escrituras/drenar procesos antes de activar.

Snapshots históricos de nombres, reconciliación independiente de notificaciones agotadas y evidencias operacionales del incidente siguen fuera de alcance. No se tocaron frontend, IA, n8n, PDF, secretos, AGENTS ni plan.

### Validación real

| Comando / evidencia | Resultado |
|---|---|
| Build inmediato recuperado y build final: `dotnet build backend/backend.slnx --no-restore` | Correctos, **0 errores**. Recompilación con **3 warnings preexistentes**: CS8981 x2 y CS8602 x1 (línea desplazada a 228); incremental final **0 warnings**. Ningún error de compilación/DI/constructor encontrado; no restauraciones ni dependencias nuevas. |
| Suite inmediata `dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore` | **628 aprobadas, 0 fallidas, 0 omitidas** antes de modificar. |
| Tres reproducciones nuevas antes del parche | **0 aprobadas, 3 fallidas**, por nombre perdido, intención inválida y terminador Down. No eran fallos de herramientas/dependencias. |
| Suite final explícita, mismo comando | **669 aprobadas, 0 fallidas, 0 omitidas**: baseline 628 conservado + **41 casos nuevos** en esta recuperación. Incluye RC-1/RC-1.1 y RC-3 Sprints 0–4, Outbox y PDF/callback previos. |
| Dirigida intermedia `FullyQualifiedName~Rc3Sprint41\|FullyQualifiedName~ReservationsRepositoryTests` | **46 aprobadas, 0 fallidas, 0 omitidas**, antes de sumar los dos casos finales de repetición de baja y el de identidad corrupta (incluidos luego en los 669). |
| `dotnet ef migrations script 20261008050017_TrackWorkspaceWhatsAppConnections 20261019170831_CoordinateReservationResourcesAndReuseCancelledSlots --no-build --project backend/NexFlow.Infrastructure --startup-project backend/NexFlow.Infrastructure --output backend/NexFlow.Infrastructure/Migrations/Sql/20261019170831_Up.sql -- --offline` | Exit 0, SQL generado y revisado; ninguna conexión ni aplicación. |
| Mismo comando invirtiendo migraciones, output `20261019170831_Down.sql` | Exit 0, guardas/sintaxis revisadas estáticamente; no ejecutado por PostgreSQL. |
| `dotnet ef migrations has-pending-model-changes --no-build --project backend/NexFlow.Infrastructure --startup-project backend/NexFlow.Infrastructure -- --offline` | Exit 0: **sin cambios pendientes del modelo**. Aviso herramienta EF 10.0.10/runtime 10.0.11; no actualización. |

El test nuevo sobre Down se precisó para permitir DELETE de `__EFMigrationsHistory` generado por EF, manteniendo prohibición de DML sobre reservas. La revisión final agregó validación de identidad también al camino de sellado: una identidad corrupta permanece bloqueada y solo se retoma al restaurar en la fixture la intención original revisada, sin bajar epoch ni borrar datos. No se debilitaron pruebas históricas. No se ejecutaron build/lint/frontend porque no se modificó frontend.

Límites de pruebas: Firestore SDK sobre transporte RPC en memoria; coordinador SQL de producción sobre SQLite con adaptador que elimina FOR UPDATE, quita CAST jsonb y sustituye advisory lock por SELECT 1. Se ejercen filtros/CAS/orden/guardas/commit EF, no locks PG ni SSI/40001/jsonb reales. Semáforos existen únicamente en fake de tests. Restart local = nuevos objetos/contextos con almacenamiento compartido de fixture, no caída OS/proceso ni dos procesos PostgreSQL reales.

Documento nuevo `NexFlow_RC3_SPRINT4_1_CONSISTENCIA.md`: arquitectura, máquina de estados, secuencia, ventanas de ACK, locks, límites, migración, rollout/rollback y matriz futura con dos conexiones/procesos en PostgreSQL y Firestore aislados. No se ejecutó esa matriz externa ni se certificó staging/producción.

### Archivos de esta recuperación (respecto de 16efe69)

- Backend contratos/implementación: `backend/NexFlow.Application/Features/Reservations/IReservationResourceCoordinator.cs`; `backend/NexFlow.Infrastructure/Persistence/Firestore/FirestoreCatalogRepository.cs`; `backend/NexFlow.Infrastructure/Persistence/Firestore/FirestoreLocationRepository.cs`; `backend/NexFlow.Infrastructure/Persistence/Firestore/ReservationResourceFence.cs`; `backend/NexFlow.Infrastructure/Persistence/PostgreSQL/Repositories/ReservationResourceCoordinator.cs`; `backend/NexFlow.Infrastructure/Workers/ReservationResourceRecoveryWorker.cs`.
- Migración/SQL: corrección del Down en `backend/NexFlow.Infrastructure/Migrations/20261019170831_CoordinateReservationResourcesAndReuseCancelledSlots.cs`; nuevos `backend/NexFlow.Infrastructure/Migrations/Sql/20261019170831_Up.sql` y `20261019170831_Down.sql`. Designer/snapshot existentes no modificados.
- Tests: `backend/NexFlow.Tests/Fakes/InMemoryReservationResourceCoordinator.cs`; `backend/NexFlow.Tests/ReservationsRepositoryTests.cs`; nuevos `backend/NexFlow.Tests/Rc3Sprint41RecoveryTests.cs` y `backend/NexFlow.Tests/Rc3Sprint41SqlBoundaryTests.cs`.
- Documentación: nuevo `NexFlow_RC3_SPRINT4_1_CONSISTENCIA.md` y este registro. Historial 0–4 preservado.
- Frontend, secretos, .env, infraestructura, Docker, datos reales, AGENTS y plan maestro: **sin cambios**.

Diff final revisado respecto de 16efe69, incluidos archivos nuevos, scripts y documentación; comprobación de whitespace sin errores tras quitar únicamente líneas vacías finales de los SQL exportados. Sin cambios ajenos al alcance.

**SPRINT 4.1 NO APROBABLE como certificación distribuida completa.** Parches locales completos y verdes; P0 requiere la matriz real y decisión sobre el criterio de ciclo de vida/SQL durante IO antes de GO. P1 listo para revisión local, no aplicado. No hay bloqueo de compilación ni pruebas fallidas. Sin commit, push, despliegue o instalaciones. **Detenido para revisión; Sprint 5 NO AUTORIZADO.**

## Adenda — Sprint 4.2 autorizado, 2026-10-19

Referencia local verificada `e1af63324409ce7b6c2b0fe141403569069f11e3`, coincide con la autorización; status/diff inicial limpios. Leídos AGENTS, plan maestro, progreso, consistencia 4.1 e incidente. No se reconstruyeron los sprints anteriores.

### Hallazgos y decisiones

- Revisión dirigida confirma captura de epochs antes de Firestore, validación SQL Serializable con FOR UPDATE, orden Location→Service, intención monotónica, fence terminal y CAS. No se confirmó un defecto nuevo que justifique modificar producción. Los escritores de alta/reagendamiento siguen en ReservationEngine.
- Excepción de ciclo de vida preservada: middleware/worker mantienen guarda compartida durante IO; scheduler/borrado usan exclusiva. Quitarla sin alternativa demostrada abriría un bypass. Worker tiene deadline 30 s; eliminación existente 5 min. No hay métricas reales todavía ni certificación de commit servidor posterior al purge.
- No existía `.github/workflows/`. Se añadió uno separado exclusivamente manual, permisos contents read, runner temporal, PostgreSQL 15.10 y Firestore Emulator demo. Sin secretos del SaaS, login cloud, despliegue ni ejecución/push del workflow.
- Herramientas adicionales documentadas ANTES de preparar CI: SDK 10.0.401, Node 22.14.0, Temurin 21.0.8+9, Firebase CLI 14.12.0; instalación solo en runner. Coste limitado a minutos/almacenamiento de Actions según plan, no servicios de negocio. Ninguna dependencia NuGet nueva o instalación local.

### Nuevas pruebas y evidencia real

24 Facts de integración, Category=Integration, compilados/descubiertos y **no ejecutados**. PostgreSQL Npgsql real sin interceptores de reescritura: locks, dos conexiones, Serializable 40001, 23505, xmin, orden/deadlock 40P01, reserva/Outbox atómicos, rollback/replay y tenant independiente. Firestore SDK Emulator: negocio+fence, conflicto, epochs, ACK postcommit simulado, recuperación con worker nuevo, terminal inválido, edición inocua ganadora, historia/tenant. Migración Up/Down reales preparados en bases UUID descartables; Down rechazado por epochs/historia incompatible sin pérdida de datos. TenantDeletionWorker real preparado frente a guarda compartida, deadline acelerado, ACK tardío de commit observado y recuperación bloqueada tras borrar; pool/cancelación/liberación con observación SQL.

Ocho pruebas de seguridad del harness **ejecutadas y aprobadas**: solo config sintética explícita; rechazo de host/base/usuario/password ajenos y emulador/proyecto ausentes o cloud. Opt-in ausente muestra integración omitida; opt-in activado con servicios caídos falla, no hace skip. CI exige TRX con todas las integraciones ejecutadas y aprobadas.

| Comando / evidencia | Resultado real |
|---|---|
| `dotnet build backend/backend.slnx --no-restore`, inicial | 0 errores; 3 warnings preexistentes CS8981 x2 y CS8602 x1. MSBuild no completó primera invocación bajo sandbox; reejecución autorizada normal exitosa. |
| Mismo build final incremental | **0 errores, 0 warnings**. |
| `dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore`, baseline | **669 aprobadas / 0 fallidas / 0 omitidas**. |
| Mismo test final con `--logger "console;verbosity=quiet"` | **677 aprobadas / 0 fallidas / 24 omitidas / 701 total**, 8 s. 669 anteriores intactas + 8 unitarias nuevas; omitidas son exclusivamente integraciones pendientes. |
| `dotnet ef migrations has-pending-model-changes --no-build --project backend/NexFlow.Infrastructure --startup-project backend/NexFlow.Infrastructure -- --offline` | Exit 0, sin cambios del modelo; aviso EF tools 10.0.10/runtime 10.0.11, sin actualización. |
| YAML/JSON | Parseo local correcto con parser ya instalado; activación solo manual/contents read/sin referencias a secrets. No valida descarga/ejecución remota de Actions. |

En el harness se corrigieron errores iniciales de compilación IClock/ValueTask y warnings nuevos de aserciones antes de la suite final; ningún test existente fue debilitado. No restore local, builds de frontend ni suites frontend: frontend intacto.

### Archivos de Sprint 4.2

- `backend/NexFlow.Tests/Integration/`: nuevos `IsolatedStores.cs`, `IntegrationSafetyTests.cs`, `IntegrationCollection.cs`, `PostgresResourceIntegrationTests.cs`, `FirestoreResourceIntegrationTests.cs`, `ReservationMigrationIntegrationTests.cs`, `TenantLifecycleIntegrationTests.cs`; `emulator/firebase.json` y `emulator/firestore.rules` exclusivos de tests.
- `.github/workflows/rc3-reservation-integration.yml` nuevo (workflow separado autorizado).
- `NexFlow_RC3_SPRINT4_2_INTEGRACION.md` nuevo; este registro actualizado preservando historial 0–4.1.
- Código productivo, migraciones/snapshot/SQL, frontend, infraestructura de despliegue, Docker, secretos/.env, AGENTS y plan maestro intactos.

### Pendientes y veredicto

**IMPLEMENTACIÓN DE PRUEBAS COMPLETA — CERTIFICACIÓN PENDIENTE. NO-GO de certificación distribuida.** No hay PostgreSQL/Java en PATH; no se instalaron ni ejecutaron servicios en Windows. Ninguno de los 24 casos constituye evidencia verde de motor/emulador. Workflow escrito y parseado, **no ejecutado**; resultados/times reales pendientes. No se aplicó Up/Down a ninguna base en esta sesión.

El informe 4.2 contiene matriz, comandos manuales seguros, diferencias Emulator/Cloud, límites de deadline/ACK/reinicio, rollout sin escrituras mixtas y rollback solo compatible conservando epochs/fences/historia. La medición pendiente no prueba rendimiento ni ausencia de huérfanos con commit servidor aún en vuelo tras purge; no retirar lock ni afirmar integridad cloud con esos límites.

Siguiente acción permitida tras revisión: propietario publica revisión aprobada mediante autorización aparte y ejecuta el workflow manual aislado; evaluar TRX (24/24 sin skips), tiempos, locks/pool y ventana de tenant antes de certificar. Sin Oracle, Firestore Cloud, GitHub remoto, servicios de negocio, commit/push, despliegue ni credenciales reales. **Detenido después de Sprint 4.2. Próximo sprint autorizado: ninguno; Sprint 5 NO AUTORIZADO.**

## Adenda — Sprint 4.2 primera ejecución: prevuelo bloqueado, 2026-10-19

Usuario autorizó exactamente una corrida manual del workflow aprobado en `8af3419658f1e29b8b80145638d5f58fa3030820`, condicionada a main remoto inalterado y prevuelo completo; sin reintentos, cambios de producto ni Sprint 5.

- HEAD local exacto/rama main verificados, status inicial limpio. Workflow local: manual exclusivo, contents read, servicios sintéticos en runner temporal, sin secretos SaaS/deploy/Oracle. Blob Git local del workflow `0582500ff0349816bcc59d3567acc532b44281f0`.
- **B / BLOQUEANTE de ejecución:** `Get-Command gh` no lo encuentra; cuatro rutas convencionales de gh.exe comprobadas sin resultado. CLI/autenticación/permisos Actions no verificables. No se instaló nada ni buscaron tokens o credenciales alternativas.
- **F / BLOQUEANTE de prevuelo:** SHA remoto, workflow publicado y ausencia de una corrida equivalente activa no se verificaron; no se afirma que main remoto coincida ni que no haya ejecuciones de otros usuarios.
- **F / certificación pendiente:** cero workflows iniciados por esta sesión, sin Run ID/enlace de ejecución/SHA ejecutado/jobs/conclusión/TRX. 24 integraciones no ejecutadas por esta sesión; no hay evidencia nueva de Up/Down, SQLSTATE, xmin, locks, pool o Firestore. No se atribuye defecto al producto/harness sin resultados.
- Referencia histórica: 677 unitarias aprobadas y 24 integraciones omitidas en la preparación. No se repitieron build/tests locales porque esta sesión modifica exclusivamente documentación; no se presentan números anteriores como nueva ejecución.
- Persisten riesgo del commit servidor todavía en vuelo después del purge, guarda SQL durante Firestore y diferencias Emulator/Cloud. No hay GO de Oracle/producción ni aprobación de Sprint 5.

Actualizados únicamente `NexFlow_RC3_SPRINT4_2_INTEGRACION.md` (controles, clasificación, tabla sin resultados inventados y pasos manuales de UI) y este registro. Historial preservado; código/backend/frontend/workflow/migraciones/secretos intactos.

**CERTIFICACIÓN PENDIENTE.** Próxima acción: propietario verifica main remoto y ausencia de corrida activa, abre Actions → RC3 disposable reservation integration → Run workflow/main una vez si conserva el SHA autorizado; conserva Run ID, SHA, conclusión y TRX. Si main cambió, solicitar nueva revisión. No reintentar fallo ni iniciar Sprint 5. Detenido sin consumir un dispatch en esta sesión; sin commit/push/deploy/Oracle/servicios productivos.

## Autorización integral Sprints 5–7 — Sprint 5, 2026-10-19

Nuevo encargo autoriza secuencia local completa sin aprobaciones intermedias, conservando límites externos como VALIDACIÓN FINAL PENDIENTE. Base `8af3419658f1e29b8b80145638d5f58fa3030820`; dos documentos modificados previamente (`NexFlow_RC3_PROGRESS.md`, `NexFlow_RC3_SPRINT4_2_INTEGRACION.md`) preservados. AGENTS/plan/progreso/consistencia/integración/incidente revisados; plan y AGENTS no modificados.

**Sprint 5 OK localmente.** Reproducciones iniciales: seis fallos (4 uploads truncados/falsos aceptados; handler y registro del borrado nuclear habilitados). Se conservaron aserciones. Nuevas reproducciones detectaron timestamp n8n long fuera de rango → excepción, corregida a 401 antes de tocar almacenes. El harness de lectura se ajustó para capturar posición al Dispose (no consultar stream cerrado), manteniendo límite real de lectura.

- NF-10: API limita cuerpo multipart y archivo a 5 MB; lectura manual de 8192 bytes acotada incluso con Length falso; MIME/extensión/envelope concordantes y decodificación completa JPG/PNG/WebP, solo estáticos hasta 16 MP. Hasta dos decodificaciones simultáneas por proceso; se suben únicamente píxeles recodificados sin metadata, nombre UUID/extensión canónica y ruta workspace autenticado. Cancelación se propaga; logs solo tipo de error y WorkspaceId.
- Decoder: **SkiaSharp + NativeAssets.Linux.NoDependencies 4.153.1** fijados, licencia MIT; paquete incluye linux-arm64, verificado localmente. No se modifican Docker/Cloudinary. Se consultaron fuentes oficiales NuGet porque no existía codec suficiente en el repo. Se descartó ImageSharp 4.1.3 tras comprobar que Release exige una clave de licencia; no se eludió esa condición ni se fijó una versión vulnerable. Se restauraron dependencias tras cada cambio necesario; ImageSharp ya no está referenciado.
- NF-11 mitigado mediante producto: `TenantDeletionSafety` deshabilita borrado definitivo sin flag/override administrativo. Handler devuelve Workspace.DeletionUnavailable (HTTP 503), scheduler también lo rechaza antes de SQL y el worker nuclear no se registra en el host. Trabajos/tenants existentes no se modifican. Implementación del worker se conserva para harness aislado y futura certificación; no está habilitada para producción. Suspensión permanece disponible. Ventana remota y limpieza externa no se presentan como resueltas.
- Evolution HMAC derivado por instancia, comparación constante, validación de dueño/evento, fromMe, idempotencia, retiro legacy y UnknownDelivery ya existen; pruebas reutilizadas. No se cambian banderas/secretos. n8n mantiene firma/timestamp/scope/workspace/generación/hash y callbacks tardíos; solo se corrigió el timestamp fuera del rango representable.
- Notificaciones: pruebas Request/Order con ACK perdido, error posterior al negocio y replay conservan ID y un único registro comercial/notificación. Identidad por workspace/módulo/operación ya existe. No se añadió segundo Outbox ni reenvío de UnknownDelivery. Al agotarse inbox/outbox se conserva terminal durable para reconciliación operacional; no se afirma entrega de aviso que falló.
- Caché/sesiones: QueryPersistence existente separa usuario/workspace, fingerprint de permisos, generación de sesión y purga; no se duplicó. Chromium comprueba 401/403/READ revocado, cambio de workspace y respuestas tardías; no se alteró autorización frontend/backend.

Validación Sprint 5: backend dirigido `Rc3Sprint5SecurityTests|Evolution|CatalogCallbackTests|CatalogRecoveryHttpTests|Rc3Sprint4CommercialTests`: **191 aprobadas, 0 fallidas, 0 omitidas**, 6 s. Nuevas pruebas de seguridad: 22 casos (incluidos controles válidos de tres formatos, pixel stream truncado, tamaños, workspace, cancelación y protección nuclear). `node --test tests/rc3-inbox-browser.test.mjs tests/evolution-navigation-browser.test.mjs`: **25 aprobadas/0 fallidas/0 omitidas**, 29,63 s; console.error sintético esperado del ErrorBoundary, sin exposición en UI. Compilación de proyectos afectados correcta durante tests; build completo requerido se ejecutará en validación final. Diff revisado: únicamente seguridad/codec/tests/docs del alcance, sin migraciones ni datos externos.

Archivos fase 5: nuevos `backend/NexFlow.API/Uploads/ValidatedImage.cs`, `backend/NexFlow.Application/Abstractions/TenantDeletionSafety.cs`, `backend/NexFlow.Tests/Rc3Sprint5SecurityTests.cs`; modificados `StorageController.cs`, `Controllers/Webhooks/N8nWebhookController.cs`, `Controllers/SuperAdmin/ClientsController.cs`, `NexFlow.API.csproj`, `DeleteClientCommandHandler.cs`, `TenantDeletionScheduler.cs`, registro DI Infrastructure y este progreso. Continúa automáticamente Sprint 6: resiliencia/medición local, sin workflows externos.

## Sprint 6 — resiliencia y operabilidad local

**OK local / PENDIENTE OPERACIONAL.** Se reutilizó circuito actual de cola SQL/FIFO/leases/retries y diagnósticos, contexto Firestore autoritativo, cache Redis prescindible, notificaciones idempotentes, proveedores tipados, deadline PDF y outbound UnknownDelivery. No se añadieron servicios, métricas con contenido ni cachés alternativos. Logs de cola ya exponen edad/estado/IDs y aviso con cooldown; las pruebas Sprint 1/RC-1/Redis comprueban caída de logging sin alterar negocio.

Comando dirigido: filtro `AiResilience|AssistantRedisRecovery|AssistantDurability|AssistantRc1|Rc3Sprint1Queue|Rc3Sprint1Trace|Rc3Sprint1Profile|CatalogRecovery|Rc3Sprint6|Rc3Sprint4Commercial|Rc3Sprint0Incident` sobre csproj explícito, --no-restore, TRX local ignorado `backend/NexFlow.Tests/TestResults/rc3-sprint6.trx`. **206 aprobadas / 0 fallidas / 0 omitidas**, 2 s. Casos existentes cubren proveedores IA fallidos/timeout/fallback, Redis obsoleto/caído/recuperado, Firestore transitorio, Evolution rechazado/desconectado/entrega incierta, n8n 404/503/timeout, workflow aceptado sin callback (incluye indisponibilidad de Gotenberg detrás de n8n), reinicio lógico/leases, dead letters, handoff, replay de reservas/pedidos y workspaces. No se fingió llamada directa a Gotenberg: no existe ese cliente en el backend.

Medición nueva `Rc3Sprint6LocalMeasurementTests`: 32 lotes de dos workspaces (64 turnos) con orquestador real, SDK/RPC en memoria y proveedores simulados; warmup excluido. Resultado capturado del TRX: **p50 0,6664 ms / p95 2,1877 ms / p99 6,1303 ms por lote**, **128 read RPC attempts / 64 commit RPC attempts**, cero llamadas de proveedor y efectos comerciales. Contadores añadidos exclusivamente a InMemoryFirestore; no datos sensibles ni cambios al runtime. No son latencia de red, lecturas facturables Cloud, carga concurrente comercial ni SLO demostrado; cada workspace tiene fixture separada y el aislamiento compartido se demuestra en las regresiones existentes.

No se optimizaron lecturas sin evidencia de presión: contexto durable debe seguir venciendo a Redis. Timeout por defecto inspeccionado en código: Groq 5 s, Gemini 12 s (configurables 1–60), n8n HTTP 30 s; generación asíncrona 5 min separada. El runbook fijará medidas/umbrales provisionales para aceptación operacional. CPU/RAM ARM64, backlog real, cuotas/modelos IA, Firestore Count/índices/coste, proveedores y entrega móvil siguen pendientes; no bloquean preparación local ni autorizan despliegue.

Archivos fase 6: nuevo `backend/NexFlow.Tests/Rc3Sprint6LocalMeasurementTests.cs`; contadores nuevos en `backend/NexFlow.Tests/Fakes/InMemoryFirestore.cs`; progreso actualizado. Diff de estas ediciones revisado; no parche productivo adicional al no demostrarse otro defecto. Continúa automáticamente Sprint 7 (higiene, builds completos, suites existentes y documentos de release).

## Sprint 7 — preparación final y cierre de Sprints 5–7

**OK local; validación externa pendiente, sin detener trabajo independiente.** No se rehicieron Sprints 0–4.2 ni otro harness de integración. No llamadas a Oracle/Firebase/Firestore Cloud/proveedores de negocio ni ejecución de workflows. Se consultaron NuGet/docs oficiales para elegir un decoder actual y comprobar licencia/API al faltar uno en código local; pruebas siempre con fakes.

Higiene: único artefacto generado versionado encontrado fue `frontend/.firebase/hosting.ZGlzdA.cache`. Añadido ignore `frontend/.firebase/`; `git rm --cached -- frontend/.firebase/hosting.ZGlzdA.cache` deja **solo esa eliminación staged** y mantiene el archivo en disco (Test-Path True). No reset/clean/commit/push; migraciones y entidades intactas. Se conservaron configs Hosting equivalentes y se fijó comando explícito con firebase.nexflow.json en runbook. No bin/obj/node_modules/dist trackeados.

Automatización: nuevo `.github/workflows/rc3-release-checks.yml`, manual, contents read, runner Windows 2025 para Chromium instalado, SDK 10.0.401/Node 24.18.1. Restore/build backend, publish Release, **test csproj explícito** excluyendo Category=Integration, TRX con mínimo 702 aprobadas sin skips, npm ci/build/lint, 83 TS y browsers existentes. Native commands fail-fast mediante PowerShell. Artefactos solo TRX sintéticos 7 días. YAML parseado localmente, sin referencias a secrets; **NO ejecutado**, ni se modificó workflow 4.2 aprobado.

Se añadieron dos pruebas finales de la restricción nuclear (endpoint 503 con política SuperAdmin intacta, scheduler directo denegado incluso sin proveedor SQL), sin modificar ninguna prueba existente. Suite de seguridad nueva queda en 24 casos; medición Sprint 6 añade uno: **25 nuevas** sobre baseline 677.

### Resultados finales reales

| Validación | Resultado |
|---|---|
| `dotnet build backend/backend.slnx --no-restore` | Exit 0, **0 errores/0 warnings** en incremental final al cerrar seguridad/resiliencia. |
| `dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --logger "console;verbosity=quiet"` | **702 aprobadas, 0 fallidas, 24 omitidas, 726 total**, 9 s. 677 previas conservadas; las 24 omitidas son exclusivamente PG/Emulator no disponibles, no aprobadas. |
| Publish Release linux-arm64, self-contained false, UseAppHost=false | Exit 0, restore RID requerido; **3 warnings históricos** CS8981 x2 y CS8602 x1. libSkiaSharp.so exportado con ELF 7F454C46 / machine **183 (AArch64)**. No ejecución nativa ARM64 ni Docker. |
| `npm run build` | Exit 0; dist correcto. JS 710,71 kB (>500 kB warning conocido), CSS 50,73 kB. |
| `npm run lint` | Exit 0. |
| `node --import ./tests/reservations-loader.mjs --test tests/*.test.ts` | **83/83**, 0 fallidas/omitidas, 1,54 s. |
| `node --test --test-concurrency=1 tests/*.test.mjs` | **139/139**, 0 fallidas/omitidas, 146,70 s. Todos los browsers/Hosting existentes: artifacts, categories, evolution-navigation, hours, navigation, rc3-inbox, reservations, settings y Hosting. |

Frontend total distinto **222**; no sumar las 25 dirigidas de Sprint 5 otra vez. Includes validación real de timeouts HTTP 20/15 s, permisos/caché/identidad, borradores/revalidación, fechas, categorías, sedes, horarios, PDFs PRODUCT/SERVICE y estado WhatsApp. ErrorBoundary imprime error sintético esperado en consola; no falla ni expone detalles en UI. Pedidos/solicitudes se validan por suites backend; no hay un browser dedicado de pedidos. Login/usuarios/proveedores reales se señalan explícitamente como pendientes, no como pruebas ejecutadas.

Checklist `NexFlow_RC3_RELEASE_CHECKLIST.md`: matriz funcional local vs real, resultados, prioridades y CODE COMPLETE separado de RELEASE. Runbook `NexFlow_RC3_DEPLOY_RUNBOOK.md`: Oracle ARM64/config efectiva y overrides/Caddy; servicios compartidos; build limpio por SHA/digests; backup y restauración aislada; migrator y ReservationResourceStates; drenar escritores antiguos; health por HTTPS/cuerpo; pruebas autorizadas; retiro legacy separado; cuatro Outbox históricos sin replay; rollback solo compatible con epochs/fences/historia; secretos/volúmenes intactos. Es preparación, **ninguno de sus comandos externos fue ejecutado**.

### Inventario final del encargo (además de cambios previos preservados)

- Backend API: `backend/NexFlow.API/Controllers/StorageController.cs`, `Controllers/SuperAdmin/ClientsController.cs`, `Controllers/Webhooks/N8nWebhookController.cs`, `NexFlow.API.csproj`; nuevo `Uploads/ValidatedImage.cs`.
- Backend Application: `backend/NexFlow.Application/Features/SuperAdmin/Workspaces/DeleteClientCommandHandler.cs`; nuevo `Abstractions/TenantDeletionSafety.cs`.
- Backend Infrastructure: `backend/NexFlow.Infrastructure/Persistence/DependencyInjection/DependencyInjection.cs`, `Persistence/PostgreSQL/Repositories/TenantDeletionScheduler.cs`.
- Tests: nuevos `backend/NexFlow.Tests/Rc3Sprint5SecurityTests.cs`, `Rc3Sprint6LocalMeasurementTests.cs`; contadores en `Fakes/InMemoryFirestore.cs`.
- CI/higiene: nuevo `.github/workflows/rc3-release-checks.yml`; `.gitignore`; eliminación del caché Firebase solo del índice. Frontend de aplicación/contratos/dependencias npm **sin modificaciones**.
- Docs: nuevos `NexFlow_RC3_RELEASE_CHECKLIST.md`, `NexFlow_RC3_DEPLOY_RUNBOOK.md`; este progreso. `NexFlow_RC3_SPRINT4_2_INTEGRACION.md` mantiene las ediciones previas de prevuelo, sin reconstrucción ni nuevas ediciones en este encargo.
- Sin migraciones nuevas/cambios de esquema, Docker/infra de despliegue, .env/secretos/credenciales, AGENTS o plan maestro.

### Riesgos y decisión final

P0 potencial de borrado tardío **mitigado**: nuclear deshabilitado por producto/scheduler/host, sin bypass administrativo ni alteración de jobs/datos. No se declara segura su implementación retenida ni limpieza externa certificada. P1 RELEASE: 24 integraciones/Up-Down/PG reales pendientes, runtime ARM64 del codec/proveedores reales, configuración Oracle/health, permisos/cuentas reales, respaldo/restauración/rollback y retiro legacy coordinado. P2: métricas/costos/colas en carga real, revisión de estados terminales sin replay ciego, tres warnings C# históricos y bundle grande.

Notificaciones preservan identidad estable y negocio no duplicado; agotamiento permanece terminal durable para reconciliación por operación, no reenvío automático. Reservas conservan motor, epochs/fences/UTC/SourceMessageId y coordinación; no se prometen garantías distribuidas basadas en fakes. No se identificó un P0 local sin mitigación en funciones habilitadas, sin certificar ausencia universal de defectos.

**CODE COMPLETE — VALIDACIÓN PREPRODUCCIÓN PENDIENTE. RELEASE NO-GO.** No falta una autorización intermedia para otro sprint: fases locales 5–7 terminadas; siguientes pasos son revisión de diff, publicación por propietario y validaciones externas/operacionales autorizadas por separado. Sin ejecución de workflows/Oracle/deploy/commit/push. Detenido al concluir las tres fases.

## Recuperación final RC-3 — 2026-10-20

### Fase 0 — completada
HEAD local/main `df5cfac0159bdf65ad915f9197927303b6df69cd`, status inicial limpio, diff --check sin errores. Log y stats confirman `88db9e0` solo retira caché generada y `df5cfac` contiene Sprints 5–7. No hay implementaciones que fusionar. Documentos obligatorios revisados; se conserva historial, AGENTS y plan maestro. Ninguna operación remota/commit/push.

### Fase 1 — completada; matriz de alcance

| Área | Implementado | Probado | Pendiente | Severidad | Acción |
|---|---|---|---|---|---|
| Admin / borrado | Backend rechaza, host sin worker | Tests handler/endpoint/scheduler/DI existentes | UI ofrece Eliminar indebidamente | P1 UX | Retirar acción y diálogo; explicar suspensión |
| Admin / sesiones | Hook filtra callbacks, App remonta por identidad | Regresiones globales de identidad | Cobertura browser admin específica | P1 | Simular suspensión/reactivación y respuesta tardía |
| Uploads | Codec real, 5 MB / 16 MP, normalización | 24 casos seguridad (incluye otras guardas) históricos | UI sin guarda propia de respuesta tras desmontar | P1 potencial | Reproducir; corregir solo si se demuestra |
| Reservas | Epochs/fences/índice parcial | Tests locales publicados | 24 PG/Emulator; Up real; pool | P1 release | Mantener harness y ejecución pendiente |
| WhatsApp / pedidos / PDF | Durabilidad/idempotencia/cuotas/HMAC | Suites publicadas | Proveedores reales / incidente / 4 PDF históricos | P1 operacional | Regresión local; no replay incierto |
| ARM64 | Publish y biblioteca AArch64 | Inspección ELF histórica | Carga/ejecución real codec en contenedor | P1 release | Smoke reproducible sin credenciales |
| CI / release | Dos workflows manuales y runbook | Revisión estática histórica | Sin Run ID; docs SHA antiguos | P1 release | Revisar workflows; actualizar evidencia |
| Métricas / bundle | Contadores fake y avisos existentes | Medición local histórica | Costes/carga cloud; warnings antiguos | P2 | Registrar, sin refactor |

Próxima acción: Fase 2, reproducción browser y ajustes mínimos de integración. Borrado nuclear permanece bloqueado; no hay autorización operacional de despliegue.

### Fase 2 — completada
Reproducción Chromium inicial: 2 aprobadas / 2 fallidas. Demostrados Eliminar ejecutable y onChange de upload tardío tras desmontar/cambiar workspace. Tras parche: 4/4 aprobadas, 0 omitidas (9,29 s). UI retira acción/diálogo destructivo, explica restricción y jobs antiguos pausados; conserva suspensión/reactivación. Hook no expone handleDelete y rechaza callbacks iniciados desde sesión obsoleta. Página impide doble confirmación síncrona. Upload aborta al desmontar e ignora éxito/error/finalización de sesión anterior, sin alterar codec ni contrato. Pruebas además cubren 503 sin estado ficticio y permisos admin. Backend reutiliza pruebas de endpoint/handler/scheduler/DI; se agrega cancelación legítima sin scheduler. Archivos frontend: WorkspaceCard, SuperAdminPage, useSuperAdmin, ImageUploader y tres archivos de fixture/browser de navegación. Tests backend: Rc3Sprint5SecurityTests. No otro módulo reimplementado.

### Fase 3 — completada localmente
Diff publicado Sprint 5 revisado: control WorkspaceMember/carpeta, validación real y límites codec, HMAC/timestamp n8n y restricción nuclear en tres fronteras. Regresiones existentes identificadas para HMAC instancia, idempotencia, UnknownDelivery, caché Redis vs Firestore, epochs/fences, cuotas IA/PDF y notificaciones. No se demostró vulnerabilidad adicional que justifique otro cambio productivo backend. Se conserva semáforo local solo para admisión de decodificación, no como garantía de negocio. Cada stream está acotado; no se afirma límite absoluto de memoria global bajo concurrencia arbitraria. PG/Firestore real, migración, limpieza, incidente, 4 PDF históricos y retiro legacy siguen PENDIENTES OPERACIONALES. Próxima fase: validación completa nueva; cifras anteriores son históricas.

### Fase 4 — completada localmente
Ejecuciones NUEVAS: restore backend exit 0; build inicial 0 errores/3 warnings históricos, incremental posterior 0 errores/0 warnings. Suite explícita final: **708 aprobadas / 0 fallidas / 24 omitidas / 732 total**, 17 s, TRX local ignorado rc3-final-recovery.trx. Baseline 702 preservado + 6 casos nuevos. Los 24 opt-in PG/Emulator NO son aprobados.

Durante la regresión admin se demostraron otros dos defectos del alcance: frontend omitía Reason obligatorio (confirmado por HTTP local 400 antes del handler); Reactivate activaba workspace pero dejaba licencia Suspended (dos pruebas rojas temporal/permanente). Corregidos enviando razón explícita y restaurando solo suspensión de licencia, sin cambiar fechas ni renovar una vencida, con invalidación de entitlements. Se agregaron pruebas de vigencia expirada, contrato HTTP y lista de miembros por WorkspaceId autenticado. Un error del fixture sin configuration provider y el nombre de rol supuesto Agent se corrigieron a los contratos reales AddInMemoryCollection/Member; no se debilitaron aserciones. Archivos adicionales: admin.service.ts, License.cs, ReactivateClientCommand.cs y Rc3AdminRecoveryTests.cs.

Frontend npm ci exit 0 (lockfile intacto), build exit 0 y dist generado (JS 710,37 kB; aviso >500), lint exit 0. TypeScript **83/83** (4,68 s; segunda ejecución técnica JUnit 83/83, no sumar). Navegador/Hosting **145/145**, 0 fallidas/omitidas, 192,95 s. Total frontend distinto **228**. Incluye seis casos nuevos admin/uploads, doble confirmación, 503, sesiones tardías, contratos provisión/renovación/módulos y job antiguo pausado. ErrorBoundary registra error sintético esperado sin exposición al usuario. No certifica identidad/proveedores reales ni CRUD de usuarios no implementado.

npm ci reportó 4 vulnerabilidades altas y scripts pendientes de @firebase/util/protobufjs. npm audit identifica una cadena transitiva firebase→firestore→grpc-js (GHSA-m9gg-hp2v-232j y GHSA-f596-whhp-79r4); recomendación automática implica downgrade mayor Firebase 9.14.0 y no se aplicó. Código usa Firebase app/auth; no importa Firestore/grpc, y bundle no contiene getAuthContext/grpc-js. No se ha demostrado exposición de esos métodos Node en Hosting estático; se registra riesgo de dependencia para revisión, no se declara auditoría limpia ni se cambia lockfile por fuerza. Próxima fase: smoke ARM64 y artefactos.

### Fase 5 — preparación local completada / ARM64 real PENDIENTE OPERACIONAL
Dockerfile existente auditado sin modificar: contexto backend, API/Migrator net10.0, Release, final/migrator separados, usuario no root. API Production valida RuntimeConfiguration antes de infraestructura; no migra al iniciar. Migrator es one-shot PG+SystemCatalogSeeder y puede aplicar TODOS los pendientes: exige revisión previa. API y Migrator publican linux-arm64, exit 0 (API 3 warnings antiguos; Migrator sin warnings nuevos). Skia lib exportada ELF AArch64 183; no confundir inspección con ejecución.

Herramienta nueva `backend/tools/CodecSmoke/{NexFlow.CodecSmoke.csproj,Program.cs}` sin paquetes nuevos ni startup de host. Compila contra SkiaSharp.dll del publish aprobado; en runtime usa deps/runtimeconfig/bibliotecas reales de la imagen API. Build local 0 errores/0 warnings. Encode/decode/normalización PNG/JPEG/WebP **3 PASS Windows X64**; modo --require-arm64 rechazó Windows (exit 1 esperado). Runbook 5.1 prepara extracción de referencia desde contenedor detenido y ejecución no root/readonly/red deshabilitada/sin secretos, montando solo DLL del smoke. Docker no disponible, no instalado/ejecutado. Imagen ARM64 y smoke real aún sin digest/evidencia.

### Fase 6 — completada estáticamente / CI remoto PENDIENTE OPERACIONAL
Ambos workflows existentes revisados y YAML parseado con PyYAML 6.0.3: workflow_dispatch único, contents read, timeout 30 min, sin secrets/deploy. Release Windows conserva Chromium instalado, SDK/Node fijados, npm ci, tests explícitos, publish y agrega smoke Windows; TRX exige al menos 708 locales sin skips. Frontend genera JUnit y rechaza casos faltantes/skipped/fallidos (mínimos 83 y 145). Se comprobó formato real JUnit: no atributos de conteo en raíz, por eso se cuentan nodos testcase/failure/error/skipped. Ejecución técnica TS 83/83 y navegación 19/19 (22,44 s) valida parser/reporters; no sumar a totales distintos. Lint final exit 0.

Integración Ubuntu conserva PostgreSQL temporal 15.10 y Firestore Emulator demo, fixture loopback/sintética, limpieza emulators:exec/runner y TRX 7 días; se agrega guarda 708 locales sin omisiones, además de 24 integraciones. No tercer workflow. No CLI ni dispatch remoto, sin Run ID/SHA ejecutado/conclusión CI; no CI verde declarado. Procedimiento exacto de UI Actions añadido al mismo runbook. Próxima fase: cerrar documentación de release y revisar diff.

### Fase 7 — completada
Checklist/runbook/progreso corrigen base vigente a `df5cfac` + diff final local SIN COMMIT. No se borran cifras/decisiones históricas. Inventario y resultados vigentes separados de historia; CI sin Run ID, smoke ARM64 no ejecutado, 24 integraciones omitidas y migración no aplicada explícitos. Riesgos P0 mitigado (nuclear bloqueado), P1 operacionales y P2 dependencia npm/warnings/carga registrados sin afirmar salud real. Lint final y navegación JUnit 19/19 verificados; guardas de descubrimiento/omisión usan nodos reales. Runbook contiene nombres de configuración exigidos, no valores ni secretos. El procedimiento Bash del smoke se preparó en artefacto bin ignorado; Bash no disponible, no se ejecutó ni validó su sintaxis con shell. No se instalaron herramientas.

### Fase 8 — preparación local completada / RELEASE NO-GO
Runbook preparado: SHA final limpio/digests → build ARM64 API+Migrator → smoke contenedor temporal sin red/credenciales → 24 integraciones aisladas → inventario/esquema/history en lectura → backup privado con restauración aislada comprobada → drenar TODOS los escritores antiguos → migrator una vez solo con aprobación operacional → únicamente APIs/workers compatibles → health/canal/funcionales dos tenants y servicios compartidos intactos → Hosting dist/config explícita → rollback solo aplicación compatible. Sin imagen compatible: pausar escrituras y corregir hacia delante. No volver a API preepochs ni borrar fences/filas/historia para forzar Down.

**Pendientes operacionales conservados:** 24 PG/Emulator y evidencia TRX; imagen/digest y smoke ARM64; configuración/health/recursos Oracle, cuentas/canal/proveedores, respaldo/restauración y rollback; causa real incidente y 4 PDF históricos; retiro legacy solo después de probar todas las instancias. Limpieza nuclear no certificada y deshabilitada backend/UI; no basta un CI verde para habilitarla. No se aplicaron migraciones, reenviaron UnknownDelivery ni reinyectaron eventos históricos.

### Inventario final de recuperación (diff respecto de df5cfac)
- Backend productivo: `backend/NexFlow.Application/Features/SuperAdmin/Workspaces/ReactivateClientCommand.cs`, `backend/NexFlow.Domain/Entities/License.cs`.
- Backend tests: `backend/NexFlow.Tests/Rc3Sprint5SecurityTests.cs`; nuevo `backend/NexFlow.Tests/Rc3AdminRecoveryTests.cs`.
- Herramienta offline nueva: `backend/tools/CodecSmoke/NexFlow.CodecSmoke.csproj`, `backend/tools/CodecSmoke/Program.cs` (no integra host/imagen productiva ni añade NuGet).
- Frontend productivo: `frontend/src/components/ui/ImageUploader.tsx`, `frontend/src/features/admin/components/WorkspaceCard.tsx`, `frontend/src/features/admin/pages/SuperAdminPage.tsx`, `frontend/src/features/admin/hooks/useSuperAdmin.ts`, `frontend/src/features/admin/services/admin.service.ts`.
- Frontend tests: `frontend/tests/navigation-browser.test.mjs`, `frontend/tests/fixtures/navigation-api.ts`, `frontend/tests/fixtures/navigation-preview.tsx`.
- CI: `.github/workflows/rc3-release-checks.yml`, `.github/workflows/rc3-reservation-integration.yml` (los dos existentes, sin tercero).
- Docs: `NexFlow_RC3_RELEASE_CHECKLIST.md`, `NexFlow_RC3_DEPLOY_RUNBOOK.md`, este `NexFlow_RC3_PROGRESS.md`.
- Sin cambios a AGENTS/plan maestro, Docker, esquemas/migraciones, .env/secretos, lockfiles ni infraestructura de despliegue. Archivos bin/obj/dist/JUnit/TRX generados ignorados por Git.

**CODE COMPLETE — LISTO PARA VALIDACIÓN DE PREPRODUCCIÓN — RELEASE PENDIENTE.** Preparación de staging aislado lista; activación de release/Oracle **NO-GO** hasta cerrar puertas operacionales. Próxima acción exacta: revisión del diff por propietario, publicación independiente de SHA final aprobado, dispatch de ambos workflows por UI con SHA/Run ID/TRX y smoke de imagen ARM64 temporal. No más sprints ni rediseños locales autorizados por este cierre; no commit/push/despliegue ejecutados.

Revisión final: HEAD permanece df5cfac; 16 archivos versionados modificados y 3 nuevos (19 en inventario), sin cambios ajenos. git diff --check exit 0 (solo avisos de conversión LF/CRLF). YAML final de ambos workflows parseado y controles manual/read-only intactos; TRX confirma 708 ejecutadas/aprobadas de 732 descubiertas, con 24 opt-in omitidas según consola. JUnit TS 83 y navegación 19 sin error/fallo/skip; 19 es subconjunto de las 145 browser, no cobertura adicional. No procesos de tests pendientes ni operaciones externas de release. Detenido para auditoría final.
