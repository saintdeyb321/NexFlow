# NexFlow LLM-first — Iteración 4: auditoría del código y fases F, G, H

**Fecha:** 2026-11-03 (Perú). **Repositorio:** https://github.com/saintdeyb321/NexFlow  
**Rama exclusiva:** `feature/llm-first-agent`. **`main` protegido:** `5f5ff850e31df3fa73cb2c7f02f65ba6081173c7`.  
**Último HEAD remoto verificado de la rama:** `e054223c1c105529f6779177e10c799e42ba6724` (commit `reanudacion`). Rama dos commits por delante de main. No cambiar main, no merge, no push ni commit sin permiso específico.

> Este documento audita el **código publicado en `e054223`** y los **resultados que el informe de Codex declara**. La suite `2078/2078`, las dirigidas `126/126` y las 40 conversaciones/399 turnos son evidencia **local reportada**, no CI independiente para `e054223`. GitHub no registra ejecuciones Actions de esa rama/commit al momento de esta revisión. Una versión local posterior debe inspeccionarse antes de trabajar.

## 0. Objetivo comercial y criterio de realidad

El cliente de NexFlow debe sentir que conversa con un secretario competente: explica lo que necesita en español cotidiano, el agente interpreta, consulta el negocio, guía a la siguiente acción y puede cambiar de tema sin quedar atrapado en formularios. No significa dar a la IA permiso para escribir directamente en PostgreSQL/Firestore, confirmar citas o inventar hechos.

**Arquitectura objetivo:** LLM propietario de comprensión y diálogo → herramientas explícitas verificadas → backend autoritativo para capacidades, hechos, borradores, consentimiento y efectos → respuesta natural anclada en datos autorizados → outbox robusto cuando se habilite un canal real. El modelo puede proponer; el backend decide, valida y persiste.

No confundir:
- Pruebas de política con JSON programado ≠ comprensión real del modelo.
- Propuesta `Publishable` del runtime ≠ entrega real al cliente.
- `main` con CI verde ≠ rama experimental certificada.
- Compatibilidad G8 ≠ cierre del P0 histórico de reservas inesperadas.
- Respuesta factual limitada ≠ naturalidad comparable a un demostrador comercial.

## 1. Dictamen de auditoría independiente, con código publicado

### Verificado en el repositorio

- `AgentDialogueRuntime.cs`: controlado por `Enabled`, `SyntheticWorkspaceId` y un `AgentExperimentScope` interno; no queda habilitado por una simple petición HTTP. El runtime usa journal, planner, realización y validaciones de contexto y permisos.
- `ProcessIncomingMessageCommandHandler.cs` y `AiResponseOrchestrator.cs`: el prototipo toma una ruta anterior a la recuperación lingüística legacy, tras barreras de entrada; **intercepta la salida en un preview `Pending`**, no llama al canal de entrega. Legacy sigue disponible con el flag apagado.
- `FirestoreAgentTurnJournal.cs`: documento por workspace y clave canónica conversación+source; transacciones de `Started` y `Prepared/Failed`, fingerprint, fence, fecha límite, snapshot de contexto, CAS; replays leen resultado en vez de llamar al LLM. No constituye outbox.
- `AgentResponseAuthority.cs`: NO publica la prosa libre `CandidateResponse` del modelo. Construye segmentos desde `VerifiedFact`, moneda y origen; protege el descarte del borrador con pregunta vigente y confirmación explícita.
- `AgentConversationEvaluationTests.cs`: construye evaluaciones con decisiones **inyectadas**, no NLU real. Los números offline son comportamiento esperado del harness.
- `AgentLiveEvaluationTests.cs`: conecta adaptadores Groq/Gemini reales sólo con autorización y credenciales sintéticas explícitas; **no se ejecutó** en la evidencia entregada.

### Hallazgos que la siguiente iteración debe abordar

**F-01 / P1 — El reporte `--live` no localiza el archivo real.** En `AgentLiveEvaluationTests.cs`, `WriteReport("live-" + scenario.Id, ...)` escribe `live-<id>.json`. En `scripts/llm-first/evaluate.py`, `report_paths = [output / (c["Id"] + ".json") ...]`. Esto omite observaciones y métricas de la ejecución real en el resumen. Además `complete` exige informes sólo en modo offline; una ejecución real podría dar PASS sin comprobar la presencia de ese reporte. Corregir con tests rojos/verdes, distinguiendo explícitamente failure del test, reporte incompleto y NLU validado.

**F-02 / P1 — El marcador `--live` bloquea todos los casos de un corpus.** Actualmente se guarda `backend/TestResults/llm-live-<hashCorpus>.started`, previo al directorio de salida y sin separar ID de caso, revisión de código ni proveedor/modelo. Un único caso o un fallo de preflight puede impedir evaluar cualquier otro caso con ese corpus. No borrar ni sobreescribir marcadores previos: versionar identidad por corpus+caso+revisión+modelo/configuración permitida; adoptar states de preflight/started/terminal/uncertain con reanudación que NUNCA duplique llamadas inciertas. El marcador debe reservarse inmediatamente antes de la primera acción con potencial HTTP, después del preflight completo; una excepción posterior no borra evidencia. Examinar crash/replay y retención.

**F-03 / P1 — No existe certificación Firestore Emulator de journal y CAS.** Los tests reportados usan SDK/fakes en memoria. Antes de almacenamiento real, verificar transacciones, fencing, concurrencia entre procesos, pérdida de ACK, expiración, límites de tamaño, índices/IAM y costes en ambiente desechable, no Oracle.

**G-01 / P1 PRODUCTO — Realización todavía rígida.** `AgentResponseAuthority.Prepare()` permite al LLM elegir `AgentOpening`/`AgentNextQuestion` y referencias, pero usa numerosas frases fijas. Es más seguro que publicar prosa cruda, pero aún no demuestra la calidez ni flexibilidad que requiere NexFlow. Diseñar un **realizador natural restringido por hechos** (ver fase G), SIN confiar en otro LLM como árbitro factual y SIN permitir que invente precios/servicios o consentimiento.

**G-02 / P1 PRODUCTO — No completa una reserva.** `PrepareReservation` inicia/continúa la preparación y pregunta por el siguiente campo, pero el agente experimental aún no implementa el llenado semántico general y verificado de sede, servicio, fecha, hora y nombre; tampoco valida disponibilidad real ni crea una cita. Es correcto para sandbox, insuficiente para el secretario final. Separar captura de slots del motor de confirmación. NO habilitar commit hasta resolver el P0.

**H-01 / P1 — Handoff incompleto y salida experimental.** `HumanAssistance` escribe estado de contexto, pero la ruta experimental no sincroniza íntegramente la conversación/canal ni notifica al negocio; es una preparación local, no handoff completado. Los tests no deben afirmar que hubo atención humana. Antes de canal real, integrar con `HumanHandoffService` / outbound existentes de manera explícita y certificada.

**H-02 / P1 — Retención física y privacidad.** El journal tiene tombstone lógico pero carece de purga física certificada. Contiene texto público generado y metadatos de correlación; el historial de 6 turnos no constituye anonimización. Definir volumen, controles de acceso, TTL/limpieza preservando antirreplay, y evitar PII en proveedor, logs o artefactos; no usar pacientes reales.

**H-03 / P0 histórico — Reservas inesperadas.** Continúa sin resolución demostrada. No autorizar reservas efectivas ni despliegue comercial con escritura hasta root cause + regresiones + certificación; no modificar silenciosamente las verificaciones H06/G8.

## 2. Fase F — Instrumentación de evaluación REAL y almacenamiento distribuido

**Propósito:** pasar de "tests verdes con guion" a capacidad de MEDIR si Groq/Gemini entienden conversaciones inéditas sin poner en riesgo a ningún cliente.

1. Inspeccionar `git status`, ramas y diferencia local contra SHA remoto `e054223`; preservar cambios de Codex posteriores y registrar procedencia. No hacer reset/clean/stash destructivo.
2. Corregir **F-01 y F-02 con test rojo/verde**, incluyendo preflight, resultados por caso, resumen correcto de HTTP/tokens/latencia, estados de incertidumbre, no reintento de ejecución incierta y conservación de artefactos.
3. Asegurar que `--live` no marque PASS de calidad si faltan observaciones, el modelo falla, el corpus no corresponde o hay tests saltados; distinguir tests de HTTP reales frente a stub/HTTP interceptado. Validar la identidad modelo/proveedor usado realmente.
4. Ejecutar el journal con Firestore Emulator y PG/Redis aislados si son necesarios. Probar dos instancias, CAS, stale result, ACK incierto, timeout y replays; preservar G8. Si el entorno aislado no está disponible, dejar evidencia `BLOCKED`, no afirmar integración real.
5. Preparar un set de **10-15 conversaciones totalmente sintéticas y no memorizadas** elegido/sellado por una persona o proceso independiente del implementador, con hash y rúbrica. Evaluar intención, utilidad, anáforas, cambio de objetivo, hechos inventados, reformulaciones, abstención y costos.
6. **No iniciar llamadas reales sin nueva autorización explícita del propietario**, verificación de cuotas/modelos/política de datos y presupuesto máximo. Dejar listo el modo live corregido y su dry-run. Si hay permiso explícito más adelante, ejecutar inicialmente UNA conversación bajo límites y revisar evidencia antes de seguir.

**Gate F:** informe del runner correcto, caso por caso, sin falsos positivos, logs sanitizados, replay seguro; Firestore Emulator PASS o BLOCKED distinguible; NLU real = NOT_RUN mientras no esté autorizado.

## 3. Fase G — Calidad conversacional y herramienta de preparación de citas SIN confirmar

**Propósito:** obtener respuestas variadas, comprensibles y útiles sin abrir permisos de escritura comercial.

1. Diseñar un `AgentConversationPlan` tipado con necesidad, objetivo actual, decisión pendiente, intención informativa, referencias anafóricas, slots candidatos y hechos verificados. Una petición informativa debe resolverse aunque haya borrador; la intención de cancelación no puede evaporarse al responder referencia.
2. Pasar de "la IA rellena un JSON y el servidor dice una frase fija" a un **realizador natural de hechos**. Alternativas aceptables: (a) plantilla composicional con actos de habla y variación comprobable, o (b) generación condicionada con únicamente fragmentos/IDs autorizados y validación estructural fuerte. No publicar un texto libre sin garantía factual verificable; no permitir salidas clínicas, promesas de disponibilidad ni consentimiento. Diseñar y probar una medida de no invención y redacción usable.
3. Conectar herramientas tipadas de lectura de catálogo/sede/horario, y resolución de necesidades semánticas a opciones reales. «Quiero sacarme el diente» debe permitir investigar servicios relacionados SIN afirmar que hay extracción ni asumir diagnóstico.
4. Implementar extracción/edición parcial de **slots de BORRADOR** (servicio, sede, fecha, franja, hora, identidad si autorizada) a partir de la respuesta semántica, siempre resuelta contra APIs existentes y persistida con CAS. Preguntar solo lo que falte; no repetir datos ya conocidos; no dar por ocupado/libre un horario sin motor de disponibilidad. No escribir reserva final. No forzar fechas/horas cuando el cliente solo consulta.
5. Admitir correcciones, negaciones, abandono y frustración, sin construir regex por cada expresión. El test de cancelación debe vincular la acción pendiente a pregunta/source/conversación/TTL/fingerprint; solo confirmación explícita puede descartar un borrador. Preguntas informativas no deben cambiar operaciones.
6. Hacer revisión humana en 10-15 conversaciones sintéticas, medir claridad y pertinencia (escala 1–5), repetir pruebas cambiando nombres/negocios/palabras para comprobar generalización. Separar esta evaluación de precisión NLU con proveedor.

**Gate G:** comparación completa original vs nuevo runtime en lenguaje natural, sin loops rígidos; 0 hechos comerciales inventados, 0 transacciones, 0 cruces tenant. No confundir redacción más variada con comprensión real.

## 4. Fase H — Endurecimiento de piloto privado y decisión de salida

1. Certificar invariantes de seguridad: workspace ACL, licencias, cambio de modo humano, replay, nonce/claim, journal CAS, salida tardía, UnknownDelivery, fallback Groq/Gemini, latencia/tokens, error de proveedores, retiro de permisos y recuperación tras caída.
2. Revisar y, si procede, implementar un contrato claro de `HandoffRequested` separado de `HandoffCompleted`; no decir que notificó a un humano si no hubo efecto/recibo real. No activar canal todavía.
3. Política de retención y limpieza física por lote con tombstones de deduplicación, sin deletes destructivos ni borrado completo de replays; comprobar volumen/costo Firestore estimado con hipótesis, no cuotas inventadas.
4. Auditoría específica del P0 histórico con reproducción y causa raíz o aislamiento efectivo comprobado. Si no se cierra, la salida permanece NO_GO_LIVE_RESERVATIONS.
5. Correr tests dirigidos, suite G8 actual e histórica, pruebas integradas aisladas, build y diff check; congelar TRX con nombres nuevos. No alterar corpus de evaluación, hashes históricos ni expectativas para fabricar verdes.
6. Emitir matriz **GO / NO_GO** desagregada: a) prototipo offline, b) ensayo con proveedor sintético y presupuesto, c) canal DEMO sin operaciones, d) reservas comerciales. La aprobación de a) jamás implica d).

**Gate H:** compatibilidad legacy, seguridad y métricas observables; `main` intacta y sin despliegue; quedan pendientes explicitados por severidad y evidencia.

## 5. Archivos principales (rutas reales verificadas)

- `backend/NexFlow.Application/Features/Automation/Agent/AgentContracts.cs`
- `backend/NexFlow.Application/Features/Automation/Agent/AgentDialogueContracts.cs`
- `backend/NexFlow.Application/Features/Automation/Agent/AgentDialogueRuntime.cs`
- `backend/NexFlow.Application/Features/Automation/Agent/LlmFirstShadowAgent.cs`
- `backend/NexFlow.Application/Features/Automation/Agent/AgentReadTools.cs`
- `backend/NexFlow.Application/Features/Automation/Agent/AgentResponseAuthority.cs`
- `backend/NexFlow.Infrastructure/Persistence/Firestore/FirestoreAgentTurnJournal.cs`
- `backend/NexFlow.Infrastructure/Persistence/Firestore/FirestoreAgentEvaluationClaims.cs`
- `backend/NexFlow.Application/Features/Automation/ProcessMessage/ProcessIncomingMessageCommandHandler.cs`
- `backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/AiResponseOrchestrator.cs`
- `backend/NexFlow.Application/Features/Automation/Dialogue/Contracts/ConversationQuestion.cs`
- `backend/NexFlow.Tests/Concierge/AgentLiveEvaluationTests.cs`
- `backend/NexFlow.Tests/Concierge/AgentConversationEvaluationTests.cs`
- `backend/NexFlow.Tests/Concierge/AgentDialogueTests.cs`
- `backend/NexFlow.Tests/Concierge/AgentDialogueResilienceTests.cs`
- `backend/NexFlow.Tests/Concierge/AgentDevelopmentConversations.json`
- `scripts/llm-first/evaluate.py`
- `scripts/llm-first/test_evaluate.py`
- `docs/auditorias/llm-first-agent/ESTADO.md`
- `docs/auditorias/llm-first-agent/ITERACION_3_CIERRE.md`

Usar `rg`/call graph para ubicar implementaciones existentes de `IWorkspaceSlotResolver`, disponibilidad, `HumanHandoffService`, consentimiento, CAS y salida; no inventar rutas. No reimplementar motores seguros.

## 6. Orden y prohibiciones

1. Preflight: comprobar nuevo SHA/cambios locales, respaldar evidencia intacta, leer `AGENTS.md` y documentos puntuales. Los informes de Iteración3 dicen 'sin commit' porque fueron redactados **antes** de la publicación posterior del commit remoto `e054223`; registrar ambas realidades sin reescribir el histórico.
2. F: corregir evaluador, certificación emulador / preparar benchmark.
3. G: mejorar interpretación/realizador/slots de borrador, con pruebas rojas y verdes.
4. H: resiliencia, retención, P0, informe y gates.
5. Actualizar checkpoint `docs/auditorias/llm-first-agent/ESTADO.md` al final de cada fase; crear `ITERACION_4_CIERRE.md` con evidencia y limitaciones.

**No**: commit/push/merge, cambios en `main`, despliegues, migraciones, Oracle, WhatsApp, clientes o datos reales, reservas confirmadas, pedidos, pagos, secretos, cuotas inventadas, cambios de configuración de producción, bypass de consentimiento, compensaciones inseguras ni reintentos de UnknownDelivery. No llamar Groq/Gemini real sin autorización independiente. Stop inmediato ante cross-tenant, consentimiento bypass, efectos comerciales, u otro P0/P1 que bloquee seguridad; corregir y probar, no ocultar.

## 7. Criterios de éxito

- Runner `--live` realmente produce métricas por caso o `BLOCKED`, sin `PASS` engañosos ni relanzar acciones inciertas.
- El agente resuelve las necesidades/anáforas sin abandonar contexto, pregunta solo lo necesario y redacta con hechos verificables.
- Las reservas se quedan en **borrador**. Cualquier ejecución comercial continúa bloqueada por el P0 hasta resolverlo.
- Pruebas offline e infraestructura integrada no se presentan como comprensión real del modelo.
- No se degrada el camino legacy ni se toca `main`.
- El cierre incluye SHA local/remoto, paths, tests/TRX exactos, conteos, HTTP reales, tokens reportados o UNKNOWN y GO/NO-GO diferenciados.
