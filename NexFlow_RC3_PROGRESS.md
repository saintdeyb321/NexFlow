# NexFlow RC-3 — memoria de progreso

Actualización: 2026-10-19 (America/Lima).
Antes de cada sesión leer `AGENTS.md`, `NexFlow_PLAN_MAESTRO_CORRECCION_MVP_RC3.md` y este registro.

## Estado

- Sprint actual: **1**, implementado y verificado localmente; detenido para revisión. Sprint 0 aprobado por el usuario.
- HEAD local al iniciar Sprint 1: `7c7f6d071378d7c38f224208c6172abf1892a7f5`; árbol limpio, conservado. Base de aplicación: `67016bdb37c054711e57e0ac3c048b9999eedae6`. Sin consulta a GitHub.
- Autorización: exclusivamente NF-01/02/03/06. **Próximo sprint autorizado: ninguno. Sprint 2 NO autorizado.**
- Veredicto: **SPRINT 1 APROBABLE**. No constituye GO de despliegue: NF-04/05 y evidencia operacional pendientes.

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

## Validación Sprint 1 y pendientes

- `dotnet build backend/backend.slnx --no-restore`: correcto. Último build incremental: cero errores/advertencias; recompilación previa mostró los tres warnings preexistentes CS8981 x2 / CS8602 x1.
- `dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore`: **531 totales, 530 aprobadas, 1 fallida, 0 omitidas**. Única roja: NF-05, Sprint 2. NF-02/03 y control del incidente verdes.
- Filtro relacionado de concurrencia, handoff, IA, durabilidad, Redis, Evolution y Sprint 1: **354 aprobadas, 0 fallidas, 0 omitidas**. Las tres clases nuevas aportan 26 casos; cubren permisos, timeout/recuperación del perfil, borradores/licencias, FIFO/dead letter, lease expirado y correlación sin PII.
- `node --test tests/rc3-inbox-browser.test.mjs` desde frontend: **3 totales, 1 aprobada, 2 fallidas NF-04, 0 omitidas**; respuestas HTTP simuladas, sin modificar frontend ni debilitar/omitir las pruebas.
- SQLite ejecuta los predicados reales con adaptación exclusivamente del sufijo de locks; **no certifica concurrencia de dos workers ni SKIP LOCKED PostgreSQL**. Se mantienen sin cambio NOT EXISTS, locks y single-claim. Recuperación de operaciones comerciales/replay/handoff y transporte concurrente cubierta con los fakes existentes.
- Pendiente en Oracle: imagen activa, trazas/estados del incidente y evidencia del proveedor. Verificar que el colector conserve scopes WorkspaceId/InboundId; validar costo del diagnóstico con volumen real. No se ejecutó nada en servidores ni servicios externos.
- No autorizar Sprint 2 ni despliegue automáticamente. Causa operacional única: **PENDIENTE DE EVIDENCIA**.
