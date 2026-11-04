# NEXFLOW LLM-FIRST — PLAN MAESTRO DE CIERRE Y CANDIDATA DE DESPLIEGUE

**Fecha de auditoría:** 2026-11-03 (Perú)  
**Repositorio:** `https://github.com/saintdeyb321/NexFlow.git`  
**Rama de desarrollo:** `feature/llm-first-agent`  
**HEAD remoto observado:** `f537ceea9dae400d6e04f2da1916f0747a4beea0` («Fase F,G a medias»)  
**`main` protegido:** `5f5ff850e31df3fa73cb2c7f02f65ba6081173c7`  
**Objetivo:** terminar la arquitectura LLM-first y dejar **una candidata desplegable para DEMO aislado**, con plan de pruebas y reversión; la operación comercial real necesita certificación independiente y resolución/aislamiento comprobado del P0.

> Documento redactado a partir de la versión publicada en GitHub. No es una auditoría del árbol local sin publicar, ni ejecuta tests .NET ni acredita infraestructura, proveedores o WhatsApp reales. El estado local tiene prioridad tras inspección rigurosa. Los SHA de esta portada son un **punto de referencia**, no instrucciones de reset.

## 0. Dictamen inicial y trabajo verificado

| Componente | Evidencia observada | Estado |
|---|---|---|
| A/B | Shadow, contratos, tools, tests reproducidos de incidente | Hecho en iteraciones anteriores |
| C/D/E | Runtime, journal duradero, CAS/fencing, autoridad factual, corpus desarrollo 40 conversaciones | Implementado en commit `e054223`; pruebas `2078/2078` **reportadas localmente**, no CI |
| F | `evaluate.py`, `evaluation_evidence.py`, `AgentHttpAttempt` corregidos; en checkpoint `13/13` Python y `5/5` HTTP interceptado | **F offline reportada PASS**, live/emulador no |
| G | `AgentConversationPlan`, `AgentBookingDrafts`, etapas de slots, estilo composicional, nuevas pruebas `AgentPreparationTests` | **En curso**; no hay cierre consolidado ni NLU real |
| H | Seguridad integral, retención física, handoff, P0, certificado para release | **No cerrada** |
| Git | Feature `f537cee`, main `5f5ff85`, ahead 3 | Verificado remotamente; **0 GitHub Actions en branch** |
| Iteración 4 | `ESTADO.md` contiene F PASS y G en curso; `ITERACION_4_CIERRE.md` ausente en remoto | No declarar cierre |

**Brechas de producto demostrables en código:**

- `AgentDialogueRuntime` requiere `AgentExperimentScope` (no creado por HTTP/worker) y `_experiment` devuelve un `MessageRecord` preview con `Status=Pending`; la nueva IA **no responde aún por un WhatsApp DEMO real**.
- `AgentResponseAuthority` continúa componiendo muchas frases determinadas; `CandidateResponse` del LLM no se publica. La naturalidad y comprensión no están medidas de forma independiente.
- `AgentBookingDrafts` prepara/edita datos y puede consultar disponibilidad, pero **no ejecuta una reserva**, ni demuestra flujo comercial completo, ni resuelve el P0 histórico.
- Las 40 conversaciones anteriores tienen **decisiones de IA inyectadas**. El dueño confirmó que aún NO hay conjunto independiente sellado de 10–15 conversaciones ni rúbrica humana. Groq/Gemini reales, cuotas efectivas y p95 de proveedor: UNKNOWN/NOT_RUN.
- El journal conserva marcadores/tombstones; limpieza física y privacidad de datos reales pendientes.

### Pautas innegociables

1. **Jamás alterar `main` ni Oracle comercial.** Trabajar solamente en `feature/llm-first-agent`. No reset/clean/stash automático, merge, cherry-pick destructivo. No commit/push/despliegue sin una autorización posterior explícita.
2. **La IA dirige significado y diálogo.** Backend controla legitimidad de catálogo, tenant, operaciones, consentimiento, idempotencia, disponibilidad, recibos, outbox y persistencia. No sustituir comprensión por árboles de keywords/regex, ni crear una regla por frase.
3. **No se ejecutan llamadas reales a Groq/Gemini ni mensajes WhatsApp reales sin autorización y presupuesto separado.** El plan debe dejar TODO preparado para esas pruebas, no simularlas como efectuadas.
4. **P0 histórico no cerrado = prohibido activar reservas comerciales reales.** Un DEMO read-only/draft-only debe tener bloqueo de efectos de escritura independiente del LLM y verificable. La disponibilidad sólo se presenta como lectura provisional, nunca una reserva confirmada.
5. Los resultados del implementador no reemplazan una evaluación independiente. No fabricar 90% de comprensión, >=4/5 humano, tokens/costos reales ni comparación AxionLabs.
6. Proteger H06/G8, workspace isolation, CAS, replay, pending questions/TTL, handoff humano, permisos/licencias, recepción y salida idempotentes, UnknownDelivery sin reenvío automático.
7. Estado del experimento apagado por defecto. No incluir secrets ni PII en logs, artefactos, prompts de prueba o código. Corpus sintético únicamente. `AgentExperimentScope` no puede abrirse por cabecera, parámetro ni entrada de usuario.

## 1. Preflight obligatorio de Codex (sin destruir estado)

- `git branch --show-current`, `git rev-parse HEAD`, `git status --short`, `git diff --stat`, `git diff --cached --stat`, `git ls-files --others --exclude-standard` y, si no expone secretos, `git ls-remote origin refs/heads/main refs/heads/feature/llm-first-agent`.
- Identificar cambios locales, staged y untracked y el verdadero último checkpoint. No asumir que el remoto `f537cee` equivale al checkout actual. Si la rama local divergió, **protegerla** y reportar conflicto sin reset; resolver preservando datos antes de editar.
- Leer `AGENTS.md`, `docs/NEXFLOW_PIVOTE_AGENTE_CONVERSACIONAL_LLM_FIRST_2026-11-03.md`, `docs/NEXFLOW_LLM_FIRST_ITERACION_4_F_G_H_2026-11-03.md`, `docs/auditorias/llm-first-agent/ESTADO.md`, `docs/auditorias/llm-first-agent/ITERACION_3_CIERRE.md` y los últimos artefactos de la Iteración 4 que existan localmente.
- Revisar código real y tests en `Features/Automation/Agent/` y `scripts/llm-first/`; verificar firmas/dependencias. Guardar evidencia anterior; nuevos TRX y JSON siempre en **directorios nuevos**. No editar ni sobrescribir corpus G8.
- Elaborar matriz de aceptación **PASS/FAIL/BLOCKED/NOT_RUN** y continuar desde donde quedó, no reimplementar A–E.

## 2. FASE F — CIERRE REAL DEL EVALUADOR Y ADMISIÓN

**Meta:** certificar que la herramienta de pruebas jamás anuncia PASS falso, jamás duplica HTTP incierto ni interpreta mocks como comprensión.

- Auditar estado actual de `scripts/llm-first/evaluate.py`, `evaluation_evidence.py`, `test_evaluate.py`, `AgentLiveEvaluationTests.cs`, `AgentHttpAttemptTests.cs` y `AgentEvaluationBudgetTests.cs`.
- Revalidar lo avanzado: `live-<caso>.json`, identidad SHA+caso+fuentes+modelos, claim atómico antes de HTTP, respuestas rechazadas/reintentos/fallback, TRX sin skips, extracción estricta de estado y métricas, tratamiento de fallos sin TRX. Crear nuevos tests sólo ante brechas y no reescribir los PASS previos.
- Investigar atomicidad/corrupción de marcadores, interrupción entre primer HTTP y persistencia, control de intentos con múltiples ejecuciones/procesos y no borrar `Started/Uncertain` para relanzar. Test `--dry-run` sin redes, claves ni efectos.
- Firestore Emulator real en infraestructura efímera local si está disponible: journal cross-process, CAS, transacciones, lease, vencimiento, ACK incierto, reintentos, conflictos de identidad, capacidad y reglas IAM. El SDK fake no sustituye esta certificación. Si falta emulador, `BLOCKED` con instrucciones reproducibles; jamás acceso a Firestore/Oracle comercial.
- Preparar importación y verificación de corpus externo sellado `--corpus --sha256` y rúbrica humana por evaluador independiente. **No existe todavía** ese paquete; no declarar aceptación real ni crear uno como si fuera ciego. Guardar únicamente el esquema y las instrucciones para que el dueño/evaluador lo aporte.
- Proveedor real: preparar preflight sin ejecución. Necesita aprobación explícita, credenciales de prueba sintéticas y modelo/cuotas por cuenta, max HTTP, reservas de tokens, fecha y hash de corpus. Si no hay aprobación, `BLOCKED/NOT_RUN`.

**Gate F:** runner y pruebas offline íntegros PASS, evidencia real UNKNOWN si no se ejecutó, emulador PASS o BLOCKED explícito. No gastar proveedores.

## 3. FASE G — TERMINAR COMPRENSIÓN, BORRADORES Y RESPUESTA NATURAL

**Meta:** asistente que conduce conversación libre como secretario, no un menú de 2015.

- Completar y probar `AgentBookingDrafts`, `AgentConversationPlan`, `AgentResponseAuthority`, `AgentDialogueRuntime`, `LlmFirstShadowAgent`, `AgentReadTools`. Verificar que DI real resuelve todas las dependencias y que el protocolo JSON del modelo coincide exactamente con contratos .NET (enum strings, opciones, campos nulos, `QuestionId`, índices, `FactReferences`). Probar JSON parcial, inválido, tokens, Prompt Injection y fallo de tools.
- La IA extrae objetivo, actos comunicativos, aclaración necesaria, consultas compuestas, referencias al turno anterior, *slots candidatos* (sede/servicio/fecha/franja/hora/nombre). **Ningún texto sin validar se convierte en ID ni operación**. El backend verifica evidencia, opciones autorizadas, reloj local, servicio/sede y restricciones; CAS persiste solo borradores reversibles.
- Correcciones de borrador y consultas laterales conservan slots verificados. Cambios de sede/servicio revalidan dependencias; fecha/hora dan preferencia hasta que exista disponibilidad autorizada, no prometen cupo. Si el cliente cambia de meta, el agente debe decidir si suspender, responder o preguntar alcance. «La que estamos preparando» conserva intención de cancelar y exige consentimiento vinculado antes del descarte.
- Redacción en español claro (es-PE), breve, variada y humana: admitir aclaraciones, corregir malentendidos, pedir UN dato pertinente, no repetir sede si ya conocida, no insistir en formulario. La información de negocio se realiza desde hechos y referencias verificadas; no publicar `CandidateResponse` libre que invente hechos. Preferir composición semántica tipada y un vocabulario natural acotado; si se introduce generación final condicionada, *no* usar validación lingüística insegura de texto libre como única barrera factual.
- Añadir tests metamórficos: misma necesidad con vocabulario diferente; nombres/sedes cruzados; negaciones; 2 objetivos en un turno; interrupción mientras se cancela; respuesta de frustración; «quiero sacarme el diente» sin diagnóstico; catálogos de negocios distintos; cliente indeciso; nombres no conocidos; disponibilidad no verificada.
- Asegurar que reserva de borrador NO ejecuta ReservationEngine ni escribe reservas finales, no bloquea turnos para siempre ni promete un horario sin motor. Hacer un walkthrough completo de 8–12 turnos y documentar respuestas observables con interpretaciones simuladas como **solo prueba de integración**.

**Gate G:** avance funcional end-to-end de borrador conversacional, edición, consulta, corrección, descarte, facts autorizados y safe fallback; cero cruces de workspace, cero transacciones, cero salidas a WhatsApp. Calidad LLM real sigue BLOQUEADA hasta ensayo independiente.

## 4. FASE H — CERRAR SEGURIDAD, JOURNAL, PRIVACIDAD Y P0

- Revisar API de journaling, Firestore composite document serialization, no plaintext sensible, fencing, CAS, duplicate, replay de múltiples mensajes, latest context/QuestionId/TTL, revocación, humano y UnknownDelivery. Probar caída después de claims, tras `Prepared`, antes de outbox, ACK perdido, cancelación/timeout y fallback del modelo. Nunca prometer exactly-once de transporte.
- **Retención física**: proponer/implementar en entorno desechable una política segura y costeable que elimine *payload sensible* tras TTL pero preserve deduplicación de IDs el tiempo necesario (tombstones con identidad y caducidad explícita). Si no se puede garantizar, bloquear datos reales y documentar presupuesto de crecimiento.
- **Handoff**: distinguir `HandoffRequested`, `HandoffPersisted`, `HandoffNotified` y `HandoffCompleted` por recibo real; no afirmar “ya hablas con un asesor” cuando solo se cambió contexto. Preparar integración con servicio durable existente y sus pruebas.
- **P0 reservas inesperadas**: reproducir en tests el camino que puede crear/transicionar una reserva sin consentimiento. Demostrar causa raíz, cierre con pruebas y re-ejecutar H06/G8, **o certificar un bloqueo independiente de todas las mutaciones de reserva en DEMO** que haga imposible dispararlas desde el nuevo runtime. Marcar por separado: P0_RESUELTO vs P0_AISLADO_EN_DEMO. Aislamiento no autoriza producción con reservas reales.
- Validar licencias, tasa de herramientas, tamaño de contexto, costos por workspace, backpressure, modo humano, error de canales y separación total tenant. Evidencia red/Firestore real sólo en entorno desechable y autorizado.

**Gate H:** no P0/P1 abierto en ruta DEMO; reservas reales expresamente bloqueadas a menos que P0 se resuelva **y se certifique**. G8 actual/histórico intactos.

## 5. FASE I — CONEXIÓN AUTORIZADA A CANAL DEMO SIN OPERACIONES COMERCIALES

**La rama actual no puede producir respuestas WhatsApp reales:** requiere `AgentExperimentScope` interno y devuelve un preview `Pending`. Diseñar **un modo diferente** de candidata DEMO que:

- Mantenga tres estados claramente separados: Legacy (default), Shadow interno, DemoAgent para un workspace de prueba. Ningún workspace real puede activarse por mensaje, URL, header o dato de cliente. Configuración backend, allowlist y revisión de autorización del operador; kill switch instantáneo; por defecto apagado.
- Reutilice `ProcessIncomingMessageCommandHandler`, `AiResponseOrchestrator`, `IOutboundMessageService` y el outbox existentes, no un segundo emisor WhatsApp ni una entrega no persistida. Evite `Pending` presentado como `Sent`. Comprobar `UnknownDelivery`/replay con fallo antes/después de persistencia de outbox.
- Separe resultados del journal (interpretación/preparación) de la obligación de entrega, sin enviarlos dos veces ni perder mensajes en fallos. No permita enviar al mismo turno una respuesta legacy y otra del nuevo agente.
- Bloquee de forma independiente **todas** las operaciones de creación/cancelación/reprogramación de reservas y pedidos para el workspace DEMO; solo lectura, orientación y borrador reversible. Handoff real se permite solo tras su contrato durable/certificado.
- Use fixtures y adaptadores de canal simulados; no enviar WhatsApp real sin aprobación específica. Mantener `main`/producción sin efectos y verificar compatibilidad cuando flags están apagados.

**Gate I:** listo para ensayar canal DEMO aislado con envío real **solo tras aprobación**. No habilitar datos de pacientes/clientes reales.

## 6. FASE J — ACEPTACIÓN SEMÁNTICA INDEPENDIENTE Y CALIDAD HUMANA

- No confundir corpus de desarrollo de 40 conversaciones con benchmark ciego. Solicitar del dueño/evaluador externo una selección de 10–15 conversaciones sintéticas nuevas, **archivo de entradas + hash SHA256**, y una rúbrica independiente congelada. El implementador no debe conocer las expectativas por adelantado ni ajustarlas a posteriori; mantener el conjunto fuera del repositorio público. Si no existe, estado `BLOCKED_EXTERNAL_EVALUATION` (no detener mejoras offline).
- Después de autorización explícita y cuotas verificadas: ensayo 1 conversación real con proveedor principal y reserva HTTP limitada. Revisar evidencia, latencia, costos y fallback. Ampliar a otras solo bajo aprobación renovada y sin pacientes reales. Marcar proveedor real usado, no atribuir uso a Gemini si sólo respondió Groq.
- Rúbrica humana por conversación: comprensión/objetivo, pertinencia, referencias/interruptions, hechos verificados, naturalidad, facilidad de uso, preguntas repetidas, finalización sin bloqueo. Dos evaluadores si es posible y registrar diferencias. Metas candidatas: ≥90 % de intenciones de casos inéditos, ≥95 % de referencias/cancelaciones, media de claridad ≥4/5; seguridad 0 violaciones. Son umbrales deseados, **no resultados asumidos**.
- Comparación legacy vs nuevo con mismas entradas, catálogo sintético y condiciones; registrar p50/p95, tokens reales reportados o UNKNOWN, intentos/turno, coste estimado basado en tarifas verificadas, respuestas útiles, errores y motivos. No afirmar superioridad sobre AxionLabs sin comparar pruebas representativas de ambos sistemas.

**Gate J:** PASS únicamente con evidencia auténtica y revisión humana independiente. Si bloqueado por corpus/autorización/modelos, la candidata puede estar lista **para desplegar DEMO read-only**, pero no se declara certificada como secretario comercial.

## 7. FASE K — RELEASE CANDIDATE ARM64, CI, ROLLBACK Y DECISIÓN

- Ejecutar tests dirigidos recientes, G8 `current` y `historical` mediante `.github/workflows/g8_certification.py` y nuevos TRX sin sobrescribir, build `dotnet build backend/backend.slnx --no-restore`, scripts Python, `git diff --check`. Separar integrado real vs fake. Verificar configuración de DI, licencias, compatibilidad legacy, migraciones necesarias (evitarlas) y coste de cuotas (UNKNOWN si no se verifican).
- Preparar un `RELEASE_CANDIDATE.md` con SHA exacto, diff respecto a `main`, ruta del flag/backend allowlist, scripts reproducibles de build ARM64, digest e imagen etiquetada por SHA, health `/health/live` y `/health/ready`, smoke tests sintéticos, rollback con imagen anterior, verificación de contenedor/Compose efectivo; sin modificar otros servicios. No adivinar credenciales, Compose real ni imagen actual Oracle.
- GitHub CI de branch: preparar checklist para ejecutar cuatro workflows existentes **una vez** que el usuario autorice publicar el HEAD y lanzar Actions en la rama. Los verdes anteriores pertenecen a main; no sirven para certificar el nuevo SHA.
- Emitir **4 decisiones separadas**: (a) OFFLINE_GO, (b) PROVIDER_EVAL_GO, (c) DEMO_DEPLOY_GO (isolated, no mutations), (d) COMMERCIAL_GO (solo con P0 resuelto, validación real, privacidad, fiabilidad, aprobación del dueño). Si falla un gate, registrar NO_GO/WAITING_FOR_OWNER con evidencia, no maquillar.
- El objetivo es **dejar una candidata lista para revisión y despliegue posterior**, NO ejecutar el despliegue desde Codex ni mover `main`. El operador autoriza explícitamente commit/push, workflows, despliegue, canales, datos de prueba y presupuestos por separado.

## 8. Protocolo de trabajo cuando se acaben tokens

- Cada fase se divide en pruebas rojas → arreglo mínimo → verde → checkpoint. `docs/auditorias/llm-first-agent/ESTADO.md` debe iniciar con la fase vigente y la siguiente acción **exacta**, incluyendo SHA y lista de archivos untracked. No sobrescribir sesiones anteriores.
- Después de cada gate, actualizar informe incremental `docs/auditorias/llm-first-agent/CIERRE_FINAL_LLM_FIRST.md` con conteos exactos y evidencia; no esperar al fin de todas las fases para documentarlo.
- Si una fase muestra fallo de tenant, consentimiento o duplicación comercial, corregir y probar inmediatamente; detener avance de fases dependientes si el riesgo sigue abierto. Continuar solo tareas offline independientes.
- Una interrupción por presupuesto/contexto debe terminar con salida clara: `COMPLETADO / PENDIENTE / BLOQUEADO / PRÓXIMO COMANDO`, no prometer trabajo en segundo plano.
- No pedir al usuario información ya registrada: el corpus externo/rúbrica **no existen todavía**; las llamadas reales/no autorizadas permanecen bloqueadas por defecto.

## 9. Archivos relacionados (rutas existentes en GitHub)

**Agente:**
- `backend/NexFlow.Application/Features/Automation/Agent/{AgentContracts,AgentConversationPlan,AgentBookingDrafts,AgentDialogueContracts,AgentDialogueRuntime,AgentReadTools,AgentResponseAuthority,LlmFirstShadowAgent}.cs`

**Orquestación y persistencia:**
- `backend/NexFlow.Application/Features/Automation/ProcessMessage/{ProcessIncomingMessageCommandHandler.cs,Services/AiResponseOrchestrator.cs}`
- `backend/NexFlow.Infrastructure/Persistence/Firestore/{FirestoreAgentTurnJournal,FirestoreAgentEvaluationClaims}.cs`
- `backend/NexFlow.Application/Features/Automation/Dialogue/Contracts/ConversationQuestion.cs`
- `backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/Flows/ConversationFlows.cs`

**Tests y evaluación:**
- `backend/NexFlow.Tests/Concierge/{AgentPreparationTests,AgentDialogueTests,AgentDialogueResilienceTests,AgentConversationEvaluationTests,AgentLiveEvaluationTests,AgentHttpAttemptTests}.cs`
- `backend/NexFlow.Tests/{AgentTurnJournalTests,AgentEvaluationClaimsTests}.cs`
- `scripts/llm-first/{evaluate.py,evaluation_evidence.py,test_evaluate.py}`
- `backend/NexFlow.Tests/Concierge/AgentDevelopmentConversations.json`

**Auditoría y release:**
- `docs/auditorias/llm-first-agent/{ESTADO.md,ITERACION_3_CIERRE.md}`
- `docs/NEXFLOW_LLM_FIRST_ITERACION_4_F_G_H_2026-11-03.md`
- `backend/Dockerfile`, `docker-compose.staging.yml`, `.github/workflows/` y el procedimiento de despliegue existente (revisar contra Oracle efectivo; no sustituir Compose por conjeturas).

## 10. Entrega final de Codex

1. Rama/HEAD local y remoto/main, estado Git exacto, cambios rastreados y untracked.
2. Fases F, G, H, I, J, K: aceptación `PASS/FAIL/BLOCKED/NOT_RUN`, de manera independiente.
3. Archivos modificados/nuevos agrupados por backend, frontend, tests, scripts, documentos.
4. Evidencia de NLU con modelo real separada de mocks, exactitud/claridad humana si existe, HTTP/tokens/latencia/costes observados.
5. P0 histórico: `RESUELTO`, `AISLADO_SOLO_DEMO` o `ABIERTO`, con pruebas concretas.
6. Resultado de build, suites G8, Firestore Emulator/CI e integraciones; historial de hashes y TRX intacto.
7. Matriz de release con 4 gates y por qué cada uno es GO/NO_GO.
8. Ruta y contenido de `RELEASE_CANDIDATE.md`, procedimiento de salud y rollback, **sin desplegar**.
9. Confirmación de que `main` no cambió; lista de permisos necesarios para los siguientes pasos.

**Regla final:** no convertir las fases bloqueadas por autorizaciones externas en “PASS”. Terminar la arquitectura offline y la candidata DEMO es el máximo alcance autónomo. La aptitud comercial exige pruebas reales, seguridad y autorización del propietario.
