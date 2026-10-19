# NexFlow RC-3 — memoria de progreso

Actualización: 2026-10-19 (America/Lima).
Antes de cada sesión leer `AGENTS.md`, `NexFlow_PLAN_MAESTRO_CORRECCION_MVP_RC3.md` y este registro.

## Estado

- Sprint actual: **2**, implementado y verificado localmente; detenido para revisión. Sprint 1 aprobado por el usuario.
- HEAD local al iniciar Sprint 2: `785a4f3c481b23033b76da0fc7f58aba2965aafc`; árbol limpio, conservado. Sin consulta a GitHub.
- Autorización: exclusivamente NF-04/05/07. **Próximo sprint autorizado: ninguno. Sprint 3 NO autorizado.**
- Veredicto: **SPRINT 2 APROBABLE**. No constituye GO de despliegue; la evidencia operacional del incidente continúa pendiente.

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

## Validación Sprint 2 y pendientes

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
