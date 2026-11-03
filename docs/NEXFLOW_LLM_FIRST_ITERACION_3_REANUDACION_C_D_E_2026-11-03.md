# NEXFLOW LLM-FIRST — ITERACIÓN 3: REANUDACIÓN DE FASES C, D Y E

Fecha de referencia: 2026-11-03 (America/Lima).
Repositorio: https://github.com/saintdeyb321/NexFlow
Rama exclusiva: `feature/llm-first-agent`
SHA comprobado de la rama en GitHub: `920fcaebe78fa236f94c5888c58e112dab9008fa` (commit «Fase C,D,E»; el título del commit NO significa que las fases estén terminadas).
SHA de `main` comprobado: `5f5ff850e31df3fa73cb2c7f02f65ba6081173c7`.

## 0. EVIDENCIA Y DICTAMEN ACTUAL

Auditoría externa del contenido publicado en GitHub:

- `LlmFirstShadowAgent` existe; invoca `IAiRouter`, toma contexto, propone hasta tres lecturas autorizadas y acepta hasta dos llamadas lógicas (decisión y síntesis tras lecturas). No tiene hook de entrada en WhatsApp ni envía respuestas: `AgentTurnResult.Publishable == false`.
- `AgentReadTools` reutiliza `IBusinessReadTools` y valida workspace, fuente y hechos no sensibles. Sus capacidades actuales son Services, Products, Locations, Profile, Hours; NO disponibilidad real de profesionales ni mutaciones.
- `FirestoreAgentEvaluationClaims` crea un marcador atómico `exists=false` por workspace y hash de SourceMessageId; una huella integra conversación/cliente/texto. Replays idénticos son Duplicate, identidad cambiada Conflict. Es una protección AT-MOST-ONCE de evaluación, no exactly-once de respuesta.
- P1 replay del shadow: evidencia de corrección en código y tests, pero TRX locales 61/61, 1997/1997 y G8 130/101/29 sólo documentados; no hay ejecución GitHub Actions de la rama comprobada durante esta auditoría.
- El claim sólo guarda `version`, `requestFingerprint` y `startedAt`. NO conserva una propuesta/recibo ni estado terminal y NO expira automáticamente. Si la IA falló después del claim, un reintento no recupera respuesta: bloqueante para cualquier ruta visible al consumidor.
- `CandidateResponse` es texto libre no validado semánticamente para publicación. `FactReferences` valida índices de lecturas Found pero no acredita que cada afirmación en texto corresponda a hechos. Por tanto no publicar CandidateResponse directamente.
- Las pruebas y el corpus anteriores usan modelos simulados; NLU real, cuota Free Tier y latencia real UNKNOWN. P0 histórico de reserva inesperada sigue sin resolverse.
- La bitácora previa afirma «sin commits/push», pero GitHub YA contiene un commit en la rama. Codex debe reconciliar el estado LOCAL con la rama REMOTA antes de continuar; nunca reset, checkout destructivo, force-push ni merge a main.

**Dictamen:** GO para desarrollar C/D/E EN RAMA EXPERIMENTAL; NO-GO para conectar WhatsApp productivo, reservas reales, pagos, lanzamiento comercial o despliegue Oracle.

## 1. PROPÓSITO DEL PIVOTE

Cambiar el dueño del diálogo: el agente LLM-first comprende la intención, los matices, la pregunta previa, las interrupciones, el cambio de tema y las referencias; puede seleccionar HERRAMIENTAS DE LECTURA autorizadas y explicar con naturalidad datos realmente verificados. Las reglas rígidas anteriores NO deben interceptar mensajes abiertos en la ruta experimental.

El backend sigue siendo autoridad exclusiva sobre datos, módulos, consentimiento, concurrencia, idempotencia, permisos, disponibilidad, fecha/hora, persistencia y operaciones; no ampliar capacidades transaccionales en C/D/E. No crear árboles de regex para frases o dentistas.

Ejemplo indispensable: «Hola quiero sacarme el diente» -> entender necesidad de orientación relacionada con extracción dental, verificar qué servicios existen, no diagnosticar ni afirmar que se ofrece extracción sin catálogo. «Quiero reservar» -> iniciar conversación de preparación, no reservar todavía. «Quiero hacerme una consulta» en medio del flujo -> responder la consulta sin borrar borrador. «Quiero cancelar la reserva» -> preguntar correctamente por el alcance; «A la que estamos preparando» -> mantener acto pendiente de cancelación, nunca regresar a solicitar la sede; pedir confirmación de descarte específica y segura antes de tocar el borrador.

## 2. REGLAS DE EJECUCIÓN

- Leer `AGENTS.md`, `docs/NEXFLOW_PIVOTE_AGENTE_CONVERSACIONAL_LLM_FIRST_2026-11-03.md`, `docs/NEXFLOW_LLM_FIRST_ITERACION_2_FASES_C_D_E_2026-11-03.md`, `docs/auditorias/llm-first-agent/ESTADO.md`, `ITERACION_2_AUDITORIA.md`, `ITERACION_2_CIERRE.md` y este archivo.
- Verificar `git status --short`, `git branch --show-current`, `git rev-parse HEAD`, `git rev-parse main`, `git rev-parse origin/main`, `git rev-parse origin/feature/llm-first-agent`. Se conoce 920fcae en rama remota y 5f5ff85 en main en el instante de auditoría. No asumir que son los mismos en la estación local hoy; proteger cambios sin seguimiento.
- No repetir Fase A/B ni la auditoría P1 completa. Revisar sólo compatibilidad del cierre del claim con el nuevo circuito. Conservar TRX y negativos anteriores.
- Trabajar EXCLUSIVAMENTE sobre rama feature. Sin commit, push, merge, migraciones, cambios de `appsettings*`, Oracle, Evolution, WhatsApp real, modelos externos, reservas/pedidos/solicitudes reales ni datos de pacientes. No cambiar main.
- Permitir evolución incremental C -> D -> E. No detenerse porque falte permiso de proveedores reales: completar trabajo offline y marcar esa aceptación externa BLOCKED. Detenerse por un NUEVO riesgo demostrado de P0, aislamiento intertenant, consentimiento o efectos no autorizados; corregir y documentar, sin eludir el gate.

## 3. FASE C — CIRCUITO CONVERSACIONAL REAL AISLADO, CON RESULTADO RECUPERABLE

### C0. Corregir la limitación del claim antes de una vía publicable

Diseñar/implementar un protocolo durable e idempotente para la evaluación y la eventual respuesta, sin eliminar la semántica segura del claim actual.

**Invariantes obligatorios:**

1. Clave estable: workspace+conversación+SourceMessageId, validando identidad completa y generación/versión de contexto; no basarse en GUID por ejecución.
2. La concurrencia nunca admite dos ejecuciones simultáneas del modelo para el mismo mensaje. Replay idéntico devuelve resultado **recuperable** si existe, o estado/explicación segura si la ejecución está en curso/falló; nunca declara éxito sin respuesta duradera.
3. Diferenciar estados como `Started`, `Prepared`, `Rejected/Failed`, `Sent/Enqueued` sólo si existe una semántica verificable. Elegir el almacenamiento siguiendo el patrón de durabilidad existente. Un `Started` huérfano no puede bloquear silenciosamente al consumidor para siempre: definir observación, reconciliación y salida segura. No liberar una ejecución incierta para recobrar IA sin prueba de no efecto; si es sólo un resultado informativo, razonar explícitamente las garantías de reintento.
4. La propuesta validada se guarda antes de marcar una respuesta publicable y se revalida contra versión de contexto, humano y licencia. Evitar incluir texto sensible en documentos de presupuesto o logs; revisar privacidad/retención y crecimiento por negocio.
5. No crear un segundo outbox ni publicar automáticamente desde la ruta de evaluación. Integrar a futuro con `OutboundMessageService` y sus deduplicaciones/UnknownDelivery; nada de HTTP a Evolution desde el agente.
6. Pruebas rojas y verdes: caída antes del claim, después del claim, después del proveedor, después de resultado preparado, replay durante ejecución, repetición tras reinicio, distinta identidad, cambio de contexto, humano, pérdidas de ACK, dos réplicas, expiración/retención. Separar «evaluación una vez» de «entrega de mensaje exactamente una vez» (no atribuir esta garantía a WhatsApp).

La implementación podrá conservar `FirestoreAgentEvaluationClaims` para SHADOW at-most-once y añadir un coordinador de resultados para la vía futura, si preserva esos invariantes. No sobreescribir silenciosamente los marcadores anteriores. Justificar tamaño/operación/costos; sin cambios de esquema innecesarios.

### C1. Dueño del diálogo LLM-first

- Crear un punto de entrada experimental REAL del turno (harness/integración aislada en backend) después de comprobaciones de seguridad/tenant/humano/replay/recovery de efectos y ANTES de `ConversationRecoveryService.RecoverAsync` y `DialoguePolicy.Classify`.
- En el workspace sintético, un único propietario de interpretación por turno: LLM-first. En todos los demás, legacy intacto. No hacer dos respuestas ni dos mutaciones por mensaje. Flag desactivado por defecto con allowlist estricta; no habilitar la nueva ruta en la API productiva por mero registro DI.
- Agente decide `Information`, `Clarify`, `PrepareReservation` (sólo preparación, sin persistencia comercial), `ClarifyCancellationScope`, `ProposeDiscardDraft` (propuesta con pregunta vigente), `HumanAssistance`. Pregunta vigente con QuestionId, SourceMessageId, WorkspaceId/ConversationId, TTL, estado y fingerprint. No perder acto pendiente por convertir la respuesta en `Intent=Reservation`.
- Reutilizar `IAiRouter`, herramientas autorizadas, `ConversationContextDto`, `ConversationQuestion`, CAS existente. No crear estado de negocio paralelo ni reescribir motores.
- Incorporar una **salida de texto publicable con anclaje factual independiente**: la IA puede crear una propuesta narrativa, pero el backend debe verificar que los hechos presentados derivan de `VerifiedFact`/herramientas y que no se afirman acciones no realizadas, precios, médicos u horarios no disponibles. `FactReferences` por índice no basta para validar una oración inventada. Favorecer un plan tipado de actos + segmentos de hechos verificados que permitan una realización natural acotada. Si no es demostrablemente seguro, responder con alternativa determinista clara; no publicar CandidateResponse libre.
- Mantener prompt injection del catálogo/historial como datos, no instrucciones; máximos de herramientas/turnos, timeout, tokens/calls y fallback seguro. No hacer herramientas de escritura en esta fase.
- Simular una conversación de 8+ turnos EN EL CIRCUITO NUEVO, no sólo llamadas al shadow; verificar estado después de cada turno, una sola respuesta propuesta y ausencia de efectos.

### C2. Evidencia de aceptación fase C

- `legacy` bit-a-bit o comportamiento equivalente con flag OFF.
- Ruta experimental funciona con intención/órdenes simuladas pero por circuito completo, incluye 8 turnos y variantes NO codificadas individualmente.
- Reproducción exitosa de cancelación de borrador sin pedir sede, consulta lateral sin limpiar draft, orientación dental sin catálogo inventado.
- Cero outbound real, cero reservas/pedidos/solicitudes, cero cross-tenant, cero respuestas publicadas sin anclaje, cero doble ejecución en replay.
- Registrar **qué pruebas no demuestran NLU real**.

## 4. FASE D — EVALUACIÓN CONVERSACIONAL Y EJECUTABLE DE GROQ/GEMINI BAJO AUTORIZACIÓN

- Preparar un harness offline reproducible que ejecute conversaciones completas e intercepte la salida del agente (no WhatsApp). Debe incluir estado inicial, fixtures de catálogo por negocio, secuencia de turnos, estados esperados, resultados de tools, acciones no permitidas y verificadores automáticos.
- Preparar un protocolo para 40–60 conversaciones independientes. No llamar «ciego independiente» al corpus que crea el mismo implementador. Puede crear **corpus de desarrollo no ciego** y mecanismo para cargar luego un set externo sellado con hash antes de ejecución; la aceptación independiente se marca BLOCKED hasta recibirlo.
- Métricas: intención/acto, alcance de cancelación, coherencia del estado, relevancia de pregunta, claridad, factualidad, alucinaciones, repeticiones, llamadas por turno, tiempo, fallbacks y tokens reales sólo si proveedor reporta. Agregar matriz falla -> causa probable -> archivo -> repro, con benchmarks legacy/shadow/agente bajo mismos fixtures.
- Incluir modismos y typos peruanos, necesidad vs nombre comercial, consultas dentales ambiguas (sin diagnóstico), sede ambigua, frustración, interrupciones, datos incompletos, menú opcional, privacidad, cambios de tema, turno tardío y handoff.
- Para proveedor real, **sólo preparar** runner `--offline` por defecto y `--live` protegido por permiso explícito, cuotas verificadas y presupuesto de tokens; no ejecutarlo ahora. No usar datos personales ni exponer claves.
- Metas aspiracionales a medir posteriormente: >=90% intención general, >=95% seguimiento de referencias/cancelación, claridad humana >=4/5, 0 efectos indebidos; no declararlas PASSED con mocks.

## 5. FASE E — RESILIENCIA, CERTIFICACIÓN LOCAL Y DECISIÓN

- Revisar y demostrar degradación útil ante JSON inválido, modelo no disponible, 429, timeout, presupuesto agotado, fallos de lectura, invalidación de pregunta, replay/carreras y fallo de almacenamiento. Las respuestas de error no deben retomar automáticamente formularios antiguos si el agente experimental es dueño del turno.
- Instrumentar contadores sin PII: decisiones, rechazo por causa, latencia, herramientas, uso de IA y costos estimados si datos reales; distinguir uso lógico vs intentos HTTP por fallback. Mantener cuotas UNKNOWN si el panel no se consultó. Evitar reservas conservadoras que impidan todas las llamadas.
- Revisar crecimiento/costo/retención de claims Firestore y coordinación de resultados. No borrar ni liberar marcadores inciertos como «solución». Probar con fake y, si hay fixtures de emulador aislado disponibles, ejecutar contrato real de Firestore emulado sin producción.
- Ejecutar: pruebas dirigidas; `.github/workflows/g8_certification.py run current` con un TRX NUEVO; `run historical` con evidencia nueva; `dotnet build backend/backend.slnx --no-restore`; `git diff --check`; tests de simulador si toca cuotas. No cambiar los 29 negativos históricos esperados, ni hashes/corpus G8.
- Comparar seguridad con flag OFF y flag ON. No ejecutar GitHub Actions de despliegue ni CI remoto sin permiso expreso.
- Entregar GO/NO-GO para **siguiente evaluación experimental**, no GO comercial. P0 reserva histórica, NLU real, benchmark ciego, cuotas efectivas y aceptación humana pendientes = NO_GO_LIVE.

## 6. ARCHIVOS CLAVE

Nuevos existentes en rama:
- `backend/NexFlow.Application/Features/Automation/Agent/AgentContracts.cs`
- `backend/NexFlow.Application/Features/Automation/Agent/AgentReadTools.cs`
- `backend/NexFlow.Application/Features/Automation/Agent/LlmFirstShadowAgent.cs`
- `backend/NexFlow.Infrastructure/Persistence/Firestore/FirestoreAgentEvaluationClaims.cs`
- `backend/NexFlow.Tests/Concierge/LlmFirstShadowTests.cs`
- `backend/NexFlow.Tests/AgentEvaluationClaimsTests.cs`
- `backend/NexFlow.Application/DependencyInjection/DependencyInjection.cs`
- `backend/NexFlow.Infrastructure/Persistence/DependencyInjection/DependencyInjection.cs`

Integración/acceso:
- `backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/AiResponseOrchestrator.cs`
- `backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/ConversationRecoveryService.cs`
- `backend/NexFlow.Application/Features/Automation/Dialogue/Policy/DialoguePolicy.cs`
- `backend/NexFlow.Application/Features/Automation/Dialogue/Contracts/ConversationQuestion.cs`
- `backend/NexFlow.Application/Abstractions/Cache/IConversationCache.cs`
- `backend/NexFlow.Application/Features/AI/Router/AiRouter.cs`
- `backend/NexFlow.Application/Features/AI/Router/AiTelemetry.cs`
- `backend/NexFlow.Application/Features/Automation/Dialogue/Composition/ResponseComposer.cs`
- `backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/Flows/ConversationFlows.cs`
- `backend/NexFlow.Infrastructure/Engines/AI/{GroqAiProvider,GeminiAiProvider}.cs`
- `backend/NexFlow.Infrastructure/Persistence/Firestore/FirestoreAiRequestAdmission.cs`
- `backend/NexFlow.Tests/Concierge/NaturalExperienceTests.cs`

Documentación: `docs/auditorias/llm-first-agent/ESTADO.md`, `ITERACION_2_CIERRE.md`, `ITERACION_2_EVALUACION.md`; crear `ITERACION_3_CIERRE.md` al finalizar.

## 7. FORMA DE INFORMAR A CADA FASE

Por C, D y E, actualizar ESTADO.md con: objetivo, rutas afectadas, diseño/decisiones, TRX y comandos exactos, PASS/FAIL/BLOCKED/NOT_RUN, datos/hallazgos nuevos, riesgos y siguiente paso. Conservar reportes previos, preservar evidencia roja/verde y producir un diff final revisable que incluya archivos no rastreados.

No inventar resultados. No llamar «implementación del agente nueva» a otra ruta de regex. No declarar «estilo AxionLabs» sin evidencia comparativa real. Cerrar la iteración tras E, no iniciar F, push, merge o deploy.
