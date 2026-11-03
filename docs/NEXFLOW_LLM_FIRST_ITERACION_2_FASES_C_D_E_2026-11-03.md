# NexFlow — Iteración 2 del agente LLM-first: fases C, D y E

**Fecha de referencia:** 2026-11-03 (Perú).  
**Repositorio:** https://github.com/saintdeyb321/NexFlow  
**Rama de trabajo:** `feature/llm-first-agent`  
**Base estable que NO se debe cambiar:** `main` → `5f5ff850e31df3fa73cb2c7f02f65ba6081173c7`  
**Objetivo:** pasar de un shadow con interpretaciones simuladas a un circuito de diálogo LLM-first verificable, aislado y seguro, sin convertirlo todavía en un sistema de reservas reales.

> **Importante sobre la evidencia:** el 2026-11-03 se comprobó desde GitHub que `feature/llm-first-agent` y `main` remotas apuntan al mismo SHA y que los nuevos archivos del agente NO están publicados. La Iteración 1 está en el árbol **local** de Codex, sin commits ni push. Lo informado por Codex (39/39 dirigidas; 1975/1975 vigentes; G8 130/101/29; build PASS) es **evidencia reportada**, pendiente de inspección independiente del diff y de sus archivos TRX. No afirmar que esos tests estén verificados desde GitHub.

## 0. Prerrequisitos obligatorios: auditar el trabajo local sin destruirlo

1. Trabajar solo en `C:\Proyectos\NexFlow`. Leer `AGENTS.md`, `docs/NEXFLOW_PIVOTE_AGENTE_CONVERSACIONAL_LLM_FIRST_2026-11-03.md`, `docs/auditorias/llm-first-agent/ESTADO.md` y este plan. Si alguno no existe, registrar `BLOCKED`; no suponer su contenido.
2. Registrar `git branch --show-current`, `git rev-parse HEAD`, `git status --short`, `git diff --stat`, `git diff --cached --stat`, y enumerar **también** archivos sin seguimiento que forman parte del agente. El diff normal de Git NO incluye archivos nuevos no rastreados.
3. Asegurar que la rama es `feature/llm-first-agent` y que `main` local y remoto no se han alterado. No reset, clean, stash automático, checkout destructivo, cherry-pick, merge, commit o push.
4. Leer completos `AgentContracts.cs`, `AgentReadTools.cs`, `LlmFirstShadowAgent.cs`, el DI correspondiente y `LlmFirstShadowTests.cs`; seguir referencias reales a `AiResponseOrchestrator`, `IAiRouter`, `ConversationContextDto`, `ConversationQuestion` y `IConversationCache`.
5. **Auditoría negativa** (con test o inspección demostrable): el shadow no envía mensajes, no modifica borradores persistidos, no llama motores de confirmación, no filtra datos de otros workspaces, no registra texto sensible, no se ejecuta en modo humano, no duplica intentos ante replays, no falla de forma abierta cuando el proveedor no está disponible. Comprobar que la allowlist experimental está desactivada por defecto.
6. Verificar las pruebas y artefactos locales de Iteración 1 sin reescribir los TRX; si no existen o no corresponden al árbol actual, marcar `NOT_VERIFIED`. Corregir antes de seguir cualquier falla de seguridad real.
7. Entregar una matriz `PASS / FAIL / BLOCKED / NOT_RUN`, sin llamar `PASS` a evidencia solamente reportada.

**Regla de bloqueo:** Si el agente shadow tiene efectos no autorizados, mezcla tenants, omite deduplicación o facilita reservar sin consentimiento, detener el avance al terminar el arreglo y pruebas del defecto. No sacrificar seguridad para completar tres fases.

## Fase C — Agente como propietario del diálogo en un entorno DEMO aislado

### C1. Resultado requerido

Un único `IConversationalAgent` que **comprende y conduce** el turno cuando la modalidad experimental está activa para un workspace sintético, en lugar de pasar previamente los mensajes abiertos por `ConversationRecoveryService` y `DialoguePolicy`. La capa legacy sigue completamente igual cuando está deshabilitada. No confundir 'LLM-first' con dar al modelo acceso libre a mutaciones.

### C2. Integración y estado

- Crear una entrada clara en el punto de orquestación **después** de las barreras de autorización, control humano, deduplicación/replay y recuperación de efectos; **antes** de los atajos lingüísticos legacy. En el workspace experimental no ejecutar ambos controladores como propietarios del turno.
- Construir `AgentTurnContext` con: mensaje actual; historia mínima relevante sin exceso de PII; pregunta/acto pendiente ligado a origen, workspace, conversación y TTL; estado autorizado del borrador; capacidades efectivas; preferencia de idioma; y datos verificados disponibles. No duplicar fuente de verdad ni crear un segundo borrador persistente.
- `AgentTurnResult` debe distinguir, como mínimo, respuesta informativa, aclaración, propuesta de herramienta de lectura, propuesta de descarte de **borrador** (no cancelación de cita registrada), orientación/handoff, rechazo por falta de capacidad, y fallo de proveedor. Los resultados de modelo son **propuestas**; backend aplica política y estado.
- Referencias anafóricas y decisiones pendientes: «A la que estamos preparando» debe conservar que la pregunta previa versaba sobre *cancelar*; no devolver solo `Intent=Reservation`, no borrar la decisión pendiente, y no solicitar sede otra vez. El backend mantiene confirmación explícita, fingerprint, TTL, CAS y ninguna mutación antes de consentimiento aplicable.
- Una pregunta informativa durante reserva no pierde borrador ni fuerza elegir sede. Una continuación posterior puede reanudar la reserva en su punto correcto.

### C3. Herramientas de lectura

- Reutilizar contratos y servicios existentes (lectura autorizada de catálogo de servicios/productos, sedes y horarios; lectura del estado del propio borrador), bajo autorización real por tenant, módulos y capacidades.
- Solo permitir herramientas declaradas en allowlist; nombres, parámetros y longitudes estrictos, límite de resultados, tiempo máximo y cancelación, revalidación de alcance y estado justo antes de usarlos. No SQL / Firestore / URLs arbitrarias entregadas por el LLM. No hacer fallback a datos de otro workspace.
- Separar **necesidad semántica** de **servicio vendible**: «sacarme el diente» puede asociarse a una extracción dental como necesidad, pero solo se puede afirmar que existe o se puede reservar si una lectura verifica el servicio. Si no está, explicar con tacto y ofrecer orientación real, evaluación verificada o asesor; nunca diagnosticar.
- Si la lectura carece de datos o falla: respuesta natural y honesta, no inventar precio, profesional, stock, disponibilidad ni operación.
- Herramientas con efectos comerciales permanecen **deshabilitadas**; incluso `check_slots` no debe crear reservas ni escribir efectos. Preparación de borrador solo si la implementación puede probar que es reversible, autorizada, versionada y no envía confirmaciones.

### C4. Política de generación natural y costos

- Una sola voz del asistente, español sencillo de Perú, habitualmente 1–3 frases, respuesta directa antes del siguiente paso. No exponer «slot», «borrador», «intención», «esquema», «evidence», «provider unavailable» al cliente.
- LLM produce respuesta usando exclusivamente hechos verificados de herramientas para afirmaciones de negocio. Validar **estructuralmente** hechos sensibles (entidades, precios, fechas, disponibilidad y estado de reserva) y usar fallback seguro legible si la salida viola el contrato. El validador no debe convertirse en un diccionario de frases obligatorias.
- Preferir **1 llamada al modelo** para turnos sin herramienta; máximo **1 decisión + 1 composición** cuando una herramienta sea necesaria, salvo excepción explícita y medida. No hacer llamada oculta al sistema legacy ni llamar múltiples proveedores por defecto.
- Clasificar fallos con causas distintas: timeout, cuota real 429, presupuesto interno, HTTP, salida no conforme, evidencia insuficiente, lectura no disponible. No etiquetar un fallo técnico como incapacidad del cliente para explicarse. Dejar `ProviderUnavailable` histórico como UNKNOWN si no hay trazas.
- Medir intentos, motivo de fallback, latencia, límites, tokens reportados, abandono de turno y claridad; los límites de Groq/Gemini gratuitos siguen `UNKNOWN` hasta consultar cuotas de las cuentas reales. No activar globalmente `AiQuota.Enabled` sin calibración.
- Flag por workspace sintético y apagado por defecto; mantener `EnableContextualFocus`, composición legacy y motores pedidos apagados en el piloto. No modificar licencias o variables de Oracle.

### C5. Gates de fase C

1. La misma secuencia de **ocho turnos** funciona con el agente como dueño del turno y mantiene cancelación/referencia/continuidad; comparación con legacy, no solo mocks del resultado final.
2. «Hola quiero sacarme el diente», «Quiero extraerme el diente», «Quiero hacerme una consulta» dentro de reserva y «A la que estamos preparando» son manejados con orientación adecuada **cuando el modelo proporciona una interpretación válida**. La comprensión espontánea del proveedor real sigue `NOT_RUN` sin autorización.
3. Soporte de lectura autorizada y grounding; cobertura negativa de datos inexistentes, ambigüedad, nombre de sede repetido, scopes ajenos, permisos revocados, prompt injection en texto de catálogo, fallos HTTP, timeout, duplicados, handoff humano.
4. No acciones comerciales ni outbound real en el harness. Legacy bit-a-bit o comportamiento funcional equivalente cuando el flag está apagado.

## Fase D — Evaluación independiente y comprobación de comprensión

### D1. Corpus y método

- Construir un conjunto **ciego e independiente del implementador** de 40–60 conversaciones completas, no solo mensajes sueltos. Mantener los ocho turnos originales como regresión pública separada, nunca como único criterio de inteligencia.
- Casos: lenguaje cotidiano peruano, errores ortográficos, necesidad vs servicio registrado, seguimiento y anáforas, cancelación de borrador vs reserva real, interrupciones, frustración, cambios de tema, negaciones, horarios ambiguos, usuario mayor, producto de catálogo sin inventario, rechazo de citas de terceros, multi-tenant y peticiones sin información.
- Congelar inputs y hash **antes** de analizar resultados. No modificar casos para hacerlos pasar. Etiquetar criterios observables por evaluadores separados: intención, utilidad, pregunta pertinente, continuidad, anclaje factual, seguridad, claridad.

### D2. Tres niveles de evidencia, nunca confundirlos

**Nivel 1 — unitarios/contratos**: proveedores falsos y herramientas simuladas, verifican política, contexto y seguridad.  
**Nivel 2 — integración local**: almacenes aislados/emulados, tránsito por runtime real hasta salida interceptada, incluyendo concurrency/replay.  
**Nivel 3 — LLM real (requiere autorización separada)**: usar Groq/Gemini con datos enteramente ficticios y presupuesto acordado, sin WhatsApp real, sin claves en informes ni PII; no ejecutar hasta autorización del propietario.

Si Nivel 3 no está autorizado: `BLOCKED / NOT_RUN`, jamás estimar exactitud real a partir de mocks. Puede quedar preparada una herramienta de evaluación `--offline` con configuración de límites, dry-run, trazas sanitizadas y bloqueo explícito de modo real.

### D3. Metas iniciales, no resultados asumidos

- ≥90 % de intención comunicativa correcta sobre corpus ciego con **LLM real**.
- ≥95 % de referencias/cancelaciones correctamente entendidas sin acción errónea.
- Claridad humana promedio ≥4/5 y mínimo de preguntas repetitivas.
- 0 cambios transaccionales sin consentimiento, 0 cross-tenant, 0 información de negocio inventada, 0 confirmaciones reales.
- p50/p95 de latencia, tokens entrada/salida e intentos por conversación reportados, no inventados.
- Comparar legacy, shadow y nuevo agente con la misma batería; identificar y clasificar cada regresión.

### D4. Gate de fase D

Crear informe reproducible con conversaciones anonimizadas/ficticias, hashes, metodología, contadores reales, matriz de fallos, resultados numéricos únicamente de lo **realmente ejecutado** y evidencias preservadas. Si la comprensión del modelo no está medida, no declarar 'al nivel AxionLabs' ni 'listo para producción'.

## Fase E — Resiliencia y preparación de piloto (NO despliegue)

> La especificación original solo nombra A–D. **E es una fase añadida en esta iteración** para preparar una evaluación aislada sin mezclarla con un despliegue comercial.

### E1. Límite operacional y fallos

- Inspeccionar y probar `AiRouter`, `GroqAiProvider`, `GeminiAiProvider`, `AiTelemetry`, límites internos y `FirestoreAiRequestAdmission` **sin** activar límites de cuota real inexistentes. La herramienta de simulación existente puede orientar escenarios de 10/20/30 negocios, pero los resultados continúan `UNKNOWN` sin datos efectivos.
- Presupuestos configurables por turno y por workspace, tope de pasos/herramientas, tamaño de historial, reintentos acotados y política de fallback que no duplique respuestas ni reservas. No crear más de una 'respuesta principal' al mismo mensaje. Backpressure controlado y degradación útil si ambos proveedores caen.
- No registrar teléfonos, mensajes clínicos, claves, prompts completos ni PII en telemetría estándar. Trazabilidad por IDs ofuscados, tasas de errores, número de herramientas, latencia y tokens disponibles.
- Revisar persistencia con CAS y invalidación por humano, turno duplicado, respuesta tardía del proveedor, TTL y cambio de workspace. Nunca reintentar UnknownDelivery automáticamente.
- Auditar tool-injection y desviaciones: el contenido de herramientas es dato no confiable; el modelo no ejecuta instrucciones dentro del catálogo ni decide permisos.

### E2. Certificación local, sin modificar las antiguas evidencias

- Ejecutar tests dirigidos nuevos + suites oficiales `.github/workflows/g8_certification.py` `current` y `historical`, builds de proyectos tocados y `git diff --check`; generar archivos TRX nuevos y conservar los previos.
- Histórico G8 esperado debe preservar exactamente identidades/hashes/razones (la cifra reportada de referencia es 130/101/29, comprobar el runner real).
- Certificar legacy con flag desactivado y aislado con flag activado. Separar pruebas mock, integración, proveedor real y aceptación humana. No modificar tests congelados ni 'arreglar' expectativas para esconder regresiones.
- No ejecutar GitHub Actions del branch ni desplegar hasta una publicación posterior expresamente autorizada.

### E3. Preparación de decisión GO/NO-GO

Entregar checklist: seguridad, datos verificables, estados, provider real, cuotas efectivas, p95, claridad, fallback, multi-tenant, P0 histórico. Este gate puede terminar correctamente en `NO_GO_PILOT` si faltan autorizaciones/evidencias: no es un fracaso de la iteración.

**NO hacer**: commits, push, merge a `main`, cambios a Oracle, migraciones, llamadas reales a modelos o WhatsApp, confirmaciones de citas reales, activar CTA global o cuotas globales, ni cambiar permisos/licencias. Mantener pedidos/solicitudes fuera del piloto.

## Archivos principales a leer y potencialmente tocar

**Nuevos locales (Iteración 1):**
- `backend/NexFlow.Application/Features/Automation/Agent/AgentContracts.cs`
- `backend/NexFlow.Application/Features/Automation/Agent/AgentReadTools.cs`
- `backend/NexFlow.Application/Features/Automation/Agent/LlmFirstShadowAgent.cs`
- `backend/NexFlow.Tests/Concierge/LlmFirstShadowTests.cs`
- Los registros DI que Codex agregó en Application e Infrastructure.

**Integración con contratos existentes, solo donde haga falta:**
- `backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/AiResponseOrchestrator.cs`
- `backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/ConversationRecoveryService.cs`
- `backend/NexFlow.Application/Features/Automation/Dialogue/Contracts/ConversationQuestion.cs`
- `backend/NexFlow.Application/Abstractions/Cache/IConversationCache.cs`
- `backend/NexFlow.Application/Features/Automation/Dialogue/Understanding/TurnUnderstandingPipeline.cs`
- `backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/Flows/ConversationFlows.cs`
- `backend/NexFlow.Application/Features/AI/Router/AiRouter.cs`
- `backend/NexFlow.Application/Features/AI/Router/AiTelemetry.cs`
- `backend/NexFlow.Infrastructure/Engines/AI/{GroqAiProvider,GeminiAiProvider}.cs`
- `backend/NexFlow.Infrastructure/Persistence/Firestore/FirestoreAiRequestAdmission.cs`
- `backend/NexFlow.Application/Features/Automation/Dialogue/Composition/ResponseComposer.cs`
- `backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/AssistantRecoveryOptions.cs`
- `backend/NexFlow.Tests/Concierge/{NaturalExperienceTests,AgentStabilizationTests}.cs`
- `backend/NexFlow.Tests/AiGlobalAdmissionTests.cs`

**Documentación/artefactos de continuidad:**
- `docs/auditorias/llm-first-agent/ESTADO.md` (actualizar por fase)
- `docs/auditorias/llm-first-agent/ITERACION_2_AUDITORIA.md`
- `docs/auditorias/llm-first-agent/ITERACION_2_EVALUACION.md`
- `docs/auditorias/llm-first-agent/ITERACION_2_CIERRE.md`
- `backend/TestResults/LlmFirst20261103/` (ignorado por Git, nunca borrar)

## Orden estricto para Codex y entregables

1. Auditar Iteración 1 local y registrar hallazgos reales; preservar archivos rastreados/no rastreados.
2. Ejecutar **Fase C → Fase D → Fase E**, en ese orden, sin abrir fases F o siguientes.
3. Aplicar pruebas rojas/verdes de cambios estructurales; no soluciones lingüísticas puntuales por regex. Cambiar el comportamiento del agente, no los motores duros.
4. Completar un checkpoint al final de cada fase con `PASS / FAIL / BLOCKED / NOT_RUN`, paths, comandos, TRX y decisiones tomadas.
5. Si hay bug P0/replay/tenant/consent, detener avance y registrarlo sin maquillar el resultado.
6. Si Nivel 3 (LLM real) está bloqueado por falta de permiso, sí completar fases C/D/E offline **sin declararlas aceptadas en producción**.
7. Entregar al final: archivos modificados/nuevos, matriz de conversaciones, métricas offline, riesgos de seguridad, comparación contra legacy, suites exactas, resultado GO/NO-GO, `git status --short` y confirmación de `main` intacta.

**Éxito de esta iteración:** un agente que realmente tenga una vía propia de diálogo en un entorno aislado, componga respuestas sobre lecturas de negocio autorizadas y demuestre corrección de estado con pruebas amplias. La comprensión real y la equivalencia con AxionLabs solo podrán afirmarse tras la evaluación externa autorizada.
