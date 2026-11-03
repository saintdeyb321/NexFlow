# Iteración 3 — C, D y E: cierre local experimental

2026-11-03. **GO para revisión y posterior evaluación aislada autorizada; NO_GO_LIVE.** No se inicia F.

## Identidad y alcance

- Rama exclusiva: `feature/llm-first-agent`, HEAD `920fcaebe78fa236f94c5888c58e112dab9008fa`. Al ingreso se comprobaron HEADs remotos: feature920fcae y main `5f5ff850e31df3fa73cb2c7f02f65ba6081173c7`. Las refs locales finales conservan esos SHAs. Este informe certifica el **diff local sin publicar**, no el contenido inalterado de920fcae ni CI remoto. Índice vacío.
- Se preservó el documento del propietario `docs/NEXFLOW_LLM_FIRST_ITERACION_3_REANUDACION_C_D_E_2026-11-03.md`, inicialmente sin seguimiento. El estado anterior «sin commits/push» describía otra sesión, no la identidad actual.
- Ningún cambio frontend, appsettings, dependencias, motores comerciales, migraciones, corpus/validadores G8, Oracle o proveedores. Sin commit/push/merge/dispatch/despliegue. P0 histórico de reserva inesperada sigue PENDIENTE.

## C — Resultado durable y propietario del diálogo

`AgentDialogueRuntime.HandleAsync` coordina claim → planificación → realización independiente → commit de contexto+respuesta → revalidación. `Handles` exige flag **false por defecto**, workspace explícito y `AgentExperimentScope` backend. Ningún endpoint, header o worker abre ese scope. El harness utiliza el handler/orchestrator reales con repositorios y salida interceptados.

El hook conserva guardas, control humano y recuperación de efectos existentes; entra antes de atajos lingüísticos/Recovery/Classify legacy. Respeta `ShouldAiRespond`, texto y origen del consumidor. Off/sin scope deja legacy. `IConversationalAgent` usa `AgentOwner`; el planificador común conserva `IAiRouter`, Groq/Gemini, presupuesto y hasta2 llamadas lógicas/3 lecturas, con deadline20s. Llamada lógica no equivale a HTTP: el router/proveedor puede hacer fallback.

`FirestoreAgentTurnJournal` (métodos `BeginAsync`, `CompleteAsync`, `ObserveAsync`) conserva los claims shadow anteriores intactos. Nuevo namespace `workspaces/{workspace}/agentTurnResults/{hash}` con clave JSON canónica conversación/source, huella de identidad completa, fencing y versión/fingerprint del contexto. Started/Prepared/Failed son estados comprobables; no hay estados ficticios Sent/Enqueued.

- Claim antes del modelo. Sólo un propietario; replay no vuelve a interpretar. ACK perdido de Prepared recupera exactamente el resultado guardado, incluso al recrear runtime/journal.
- Resultado seguro y contexto se guardan en una transacción CAS. Contexto cambiado, humano, deadline o lease ajeno impiden aplicar la preparación tardía. Se revalidan conversación/cliente/licencia/módulos antes de devolver hechos cacheados.
- Started vence por defecto a30s: al observar se convierte en fallo terminal con explicación; no se libera una evaluación incierta ni se reembolsa presupuesto. Durante ejecución se devuelve estado observable, nunca éxito ficticio. No se depende de una tarea en memoria para reconstruir el resultado.
- No hay segundo outbox, escritura comercial ni envío. La salida del harness es preview Pending; no se afirma entrega exactly-once. Una futura conexión al canal debe usar OutboundMessageService y UnknownDelivery existentes, con revisión separada.
- Retención lógica por defecto24h, configurable1–168h: al observar un Prepared vencido se reemplaza su payload por tombstone seguro. **No hay purga física programada**: payloads no observados pueden permanecer más tiempo. No activar con datos reales hasta aprobar limpieza que conserve la deduplicación. Tombstones/claims crecen por ID; no se borran para reabrir IA.
- Base mínima:2 transacciones de escritura por turno aceptado (Started y contexto+resultado), más lecturas/revalidación. Replay no gasta IA; reconciliación puede escribir tombstone. Lecturas de catálogo reutilizan la fachada limitada, sin listeners nuevos. Costos Spark reales UNKNOWN, no medidos con transporte fake.

`AgentResponseAuthority.Prepare` jamás publica `CandidateResponse`. El LLM propone acto, lecturas, orden de referencias, apertura/pregunta tipadas. Backend realiza nombres/precios/monedas exactos desde VerifiedFact del repositorio; descarta shapes privados, inválidos o sin fuente. Texto libre/instrucciones del modelo no se guardan en el recibo. Contenido de catálogo/historial se pasa como datos; no concede herramientas ni acciones. Esto es **redacción acotada**, no certificación semántica de prosa libre ni verificación de la veracidad del dato introducido por el negocio.

Preparar/reanudar sólo modifica el contexto existente y conserva datos conocidos. Cancelar exige alcance → pregunta de descarte con QuestionId/workspace/conversación/source/TTL/fingerprint → respuesta afirmativa inequívoca sobre esa referencia. «De acuerdo», preguntas y condiciones no descartan. No se cancelan citas registradas. Interrupciones informativas conservan el acto pendiente. Handoff invalida su marcador de cancelación y conserva el borrador, sin afirmar que se notificó a una persona.

No se implementan edición libre de todos los slots, disponibilidad nueva, pedidos/solicitudes, confirmaciones comerciales, pagos ni acceso privado a citas. Contextos CTA Focus se rechazan; no se activa CTA. Snapshot acotado: seis turnos/400 caracteres, slots, presencia de nombre sin valor, pregunta vigente y capacidades. Sólo se usa información sintética: no es un anonimizador general del historial.

## Defectos nuevos demostrados y correcciones

| Evidencia conservada | Causa / corrección |
|---|---|
| c0-c-repro.trx:16/14/2/0 | Concatenar conversación/source con `:` permitía colisión; ahora clave JSON canónica. Pregunta de servicio usaba paso de sede; ahora estado/pregunta concordantes. |
| d-development-first.trx:71/69/2/0 | Handoff nuevo retenía marcador propio de cancelación; ahora lo invalida. Search era ignorado por el fixture heredado; extensión del fixture respeta búsqueda/ámbito. No se cambió el corpus. |
| e-schema-repro.trx:2/1/1/0 | Journal aceptaba un estado enum desconocido; ahora rechaza schema/status desconocidos. Se valida también generación del lease. El intento anterior e-schema-red falló por una ruta incorrecta del fake, no prueba el defecto productivo. |
| e-directed.trx:124/120/4/0 | Proyección de sedes vaciaba facts de un contrato legacy sin References: se conserva el fallback de lectura anterior, nunca se publica como LocationName si no tiene ese shape. Otros3 fallos eran Dispose no configurado del mock HTTP estricto, corregidos en harness. |
| e-resume-red.trx:1/0/1/0 | Reanudar preparación conocida volvía a pedir servicio; se preserva el paso y se reutiliza la proyección RequestedDataPrompt de ConversationQuestion, extraída sin cambiar texto legacy. |
| e-state-guard-red.trx:1/0/1/0 | Hook experimental ignoraba ShouldAiRespond=false. Corregido antes del gate completo; no se elude supresión del servicio de estado. |

`c0-owner-red.trx` tiene4 PASS pese al nombre. Intentos sandbox sin TRX no cuentan como aprobados. No se ocultó evidencia roja.

## D — Conversaciones completas y comparación

Corpus **de desarrollo no ciego**, creado por el implementador:40 conversaciones,133 turnos; SHA256 `98AB7CD025D0A3C6378507179FC5AEC06073AFED270BB18DDBB5F178FFE729EE`. Expectativas intactas desde primera ejecución. Familias: necesidades dentales sin diagnóstico, typos/modismos, referencias, ambigüedad de sede, frustración, interrupciones, cambio de intención, privacidad, revocación, consentimiento, replay, expiración y humano.

Se observaron399 turnos en fixtures independientes equivalentes. Interpretaciones legacy/agente son preparadas; shadow no posee ni cambia el borrador. Por tanto **los siguientes números son coincidencia de política del harness, NO precisión NLU ni benchmark de naturalidad**. Las expectativas se diseñaron para el contrato experimental; no se interpreta que el shadow deba realizar mutaciones.

| Ruta | Turnos | Coincide estado/respuesta esperados | Llamadas lógicas | Consultas de offerings | Respuestas idénticas consecutivas | P50/P95 local ms |
|---|---:|---:|---:|---:|---:|---|
| legacy |133|66|89|22|23|4.16 /20.21|
| shadow |133|70|196|43|60|3.07 /7.56|
| agente |133|133|192|43|12|6.09 /11.99|

HTTP real0, efectos comerciales0 en las tres rutas. Tokens reales null/UNKNOWN. Las consultas de offerings no representan todas las herramientas: cada resultado registra ToolCalls por separado. Latencia es local con mocks/SDK fake, no del proveedor. Repetición textual es un indicador bruto, no una calificación humana. Factualidad del agente se limita a realización backend verificable; claridad/utilidad humana y comprensión espontánea BLOCKED.

Antes: «Quiero hacerme una consulta» durante reserva regresaba a sede; referencia a cancelación perdía su acto. Ahora, en el circuito de ocho turnos: consulta → datos reales de catálogo, borrador conservado; cancelar → alcance; «la que estamos preparando» → confirmación de descarte; «Sí» ligado a esa pregunta → borra sólo preparación; cero reservas/pedidos/envíos. No demuestra que Gemini/Groq reconozcan esas expresiones.

Runner offline reproducible:

```powershell
python -B scripts/llm-first/evaluate.py --offline --no-build --output backend/TestResults/LlmFirstIteration3_20261103/offline-evaluation-final
```

Directorio ya usado: para repetir, elegir uno NUEVO. Genera evaluation.trx, identity.json,40 JSON, summary.json y turns.csv. Conjunto externo: `--corpus <ruta-privada> --sha256 <hash-congelado> --output <directorio-nuevo>`; no se pasan sus expectativas al modelo. Se conserva fuera de Git por decisión del evaluador independiente.

Live **preparado, NO ejecutado**: requiere autorización independiente y variables `NEXFLOW_LLM_LIVE_AUTHORIZED=1`, `NEXFLOW_LLM_QUOTAS_VERIFIED=1`, `NEXFLOW_TEST_GROQ_API_KEY/MODEL`, `NEXFLOW_TEST_GEMINI_API_KEY/MODEL`. Nunca se buscan claves en appsettings/Oracle/archivos privados ni se pasan por CLI. Tras aprobación/cuotas: mismo runner con `--live --case <id> --corpus <ruta-sintética> --sha256 <hash> --max-http 12 --max-token-reservation 30000 --output <nuevo>`. Usa AiRouter real (Groq principal; Gemini sólo si realmente hay fallback). No atribuir ejecución a Gemini si no aparece en metadatos. Un marcador por hash evita relanzar muestras inciertas; revisarlo antes de cualquier autorización de nueva muestra, nunca borrarlo automáticamente.

Cap compartido de todos los HTTP, incluso timeout/429/fallback; reservas conservadoras byte-bound+1024 sin reembolso. No son cuotas gratuitas verificadas ni tokens consumidos. Registra sólo uso reportado o null, proveedor/status/latencia y no claves/cuerpos operativos. Se prueban los límites y fallback con HTTP interceptado. Ese ensayo NO equivale a llamar al modelo.

## E — Resultados reales

Evidencia nueva en `backend/TestResults/LlmFirstIteration3_20261103/`; se conservan todos los TRX anteriores.

| Comprobación | Ejecutadas | PASS | FAIL | Omitidas | Dictamen |
|---|---:|---:|---:|---:|---|
| C inicial, c-reviewed-local.trx |31|31|0|0|PASS|
| D+C, d-development-green.trx |71|71|0|0|PASS|
| E dirigida final, e-directed-final.trx |126|126|0|0|PASS|
| Runner offline final, evaluation.trx |40|40|0|0|PASS|
| G8 vigente, current-iteration3.trx |2078|2078|0|0|PASS; ejecución completa única|
| G8 histórico, historical-iteration3.trx |130|101|29 esperados|0|EXPECTED_HISTORICAL_INCOMPATIBILITIES, validador exit0|
| Python preflight, test_evaluate.py |4|4|0|0|PASS|
| Build solución --no-restore |—|—|0 errores|—|PASS;0 warnings,5.23s|

G8 mantiene identidades, motivos y cuatro hashes congelados. H06 exacto `H06/4/A: ReservationEffects`. No se convirtió ningún rojo histórico a verde. 1997→2078 suma81 casos nuevos sin quitar los anteriores. Las integraciones reales y ProviderIntegration se excluyen por el filtro oficial existente; no cuentan como certificadas.

```powershell
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --disable-build-servers --tl:off -m:1 --filter "FullyQualifiedName~AgentTurnJournalTests|FullyQualifiedName~AgentDialogueTests|FullyQualifiedName~AgentDialogueResilienceTests|FullyQualifiedName~AgentEvaluationBudgetTests|FullyQualifiedName~LlmFirstShadowTests|FullyQualifiedName~AgentEvaluationClaimsTests|FullyQualifiedName~AiGlobalAdmissionTests" --logger "trx;LogFileName=e-directed-final.trx" --results-directory backend/TestResults/LlmFirstIteration3_20261103
python -B scripts/llm-first/test_evaluate.py
python -B .github/workflows/g8_certification.py run current --trx backend/TestResults/LlmFirstIteration3_20261103/current-iteration3.trx
python -B .github/workflows/g8_certification.py run historical --trx backend/TestResults/LlmFirstIteration3_20261103/historical-iteration3.trx
dotnet build backend/backend.slnx --no-restore
git diff --check
```

Estos nombres TRX ya existen: no reutilizarlos. El validador oficial borra el destino existente; en esta intervención se usaron nombres nuevos. Build/testhost requirieron ejecución local autorizada fuera del sandbox; sin ello VSTest no arrancaba y Python no podía escribir su temporal. La compilación previa mostró3 advertencias preexistentes (CS8981×2,CS8602); el build final incremental tuvo0. No se modificaron esos archivos.

| Requirement | Evidence |
|---|---|
| Una evaluación, caída/ACK/replay/reinicio | `Crash_boundaries_never_reexecute_a_claim_and_recover_only_committed_results`; `Restart_recovers_public_result_without_model_or_outbound` |
| Concurrencia, CAS, lease y expiración | `Concurrent_replicas_have_one_owner_and_one_durable_result`; `Late_owner_cannot_commit_after_context_takeover_or_expiration`; `Lease_cannot_substitute_a_different_context_generation_and_no_raw_input_is_stored`; `Result_expiration_retains_a_tombstone_and_does_not_restart_IA` |
| Ocho turnos, consulta lateral y cancelación ligada | `Eight_turn_owner_circuit_reads_facts_preserves_interruptions_and_discards_only_after_bound_consent`; `Information_preserves_pending_discard_and_ambiguous_consent_cannot_clear_it` |
| Pregunta fiel y continuidad | `Preparation_question_metadata_matches_the_question_actually_asked`; `Resuming_preparation_does_not_repeat_known_service_or_lose_the_next_missing_field` |
| ACL/tenant/humano/UnknownDelivery/respuesta tardía | `Cached_fact_is_not_republished_after_module_revocation_or_identity_change`; `Foreign_workspace_and_legitimate_cancellation_do_not_reach_IA`; `Change_during_model_work_never_applies_the_late_preparation` |
| Texto factual independiente y moneda exacta | `Model_prose_cannot_publish_invented_facts_and_mixed_currencies_stay_exact`; contratos de procedencia de `LlmFirstShadowTests` intactos |
| JSON/429/timeout/presupuesto/lectura/cancelación | `Provider_failure_is_durable_safe_and_not_reexecuted`; `Denied_internal_budget_has_zero_model_calls_and_a_recoverable_explanation`; `Read_failure_yields_safe_durable_explanation_without_resuming_legacy_form`; `Cancellation_after_claim_propagates_and_orphan_is_not_reexecuted` |
| HTTP/fallback medidos y límites | `HTTP_and_conservative_token_caps_cover_fallback_without_unaccounted_attempts`; `Real_adapters_and_router_fallback_are_measured_with_intercepted_HTTP_not_real_NLU` |
| Legacy off y guardas intactas | `Flag_or_harness_scope_off_keeps_legacy_and_cannot_enable_real_traffic`; `State_service_suppression_is_a_guard_not_a_legacy_linguistic_interceptor`;2078 casos G8 |
| Corpus completo/no ciego/externo sellado | `Offline_development_conversation_compares_observed_routes_without_claiming_real_NLU`;4 pruebas `EvaluationPreflightTests` |

## Archivos del diff

- Application, modificados: `DependencyInjection/DependencyInjection.cs`; `Agent/{AgentContracts,AgentReadTools,LlmFirstShadowAgent}.cs`; `Dialogue/Contracts/ConversationQuestion.cs`; `ProcessMessage/ProcessIncomingMessageCommandHandler.cs`; `ProcessMessage/Services/AiResponseOrchestrator.cs` (rutas Agent/Dialogue bajo Features/Automation).
- Application, nuevos: `Features/Automation/Agent/{AgentDialogueContracts,AgentDialogueRuntime,AgentResponseAuthority}.cs`.
- Infrastructure: registro en `Persistence/DependencyInjection/DependencyInjection.cs`; nuevo `Persistence/Firestore/FirestoreAgentTurnJournal.cs`. Claims anteriores sin cambios.
- Tests: fixture compartido `AssistantConversationTests.cs`, recurso en `NexFlow.Tests.csproj`; nuevos `AgentTurnJournalTests.cs`, `Concierge/{AgentDialogueFixture,AgentDialogueTests,AgentDialogueResilienceTests,AgentConversationEvaluationTests,AgentLiveEvaluationTests,AgentEvaluationBudgetTests}.cs` y `AgentDevelopmentConversations.json`.
- Runner: `scripts/llm-first/{evaluate,test_evaluate}.py`. Documentos: ESTADO.md y este cierre. Frontend: ninguno. Índice vacío, todo sin publicar.

## Gates pendientes y límites

**GO revisión técnica local/offline. NO_GO_LIVE, piloto de canal y aceptación comercial BLOCKED.** Falta evaluación independiente sellada, revisión humana, proveedores autorizados/uso real/cuotas/costos, y Firestore Emulator real (FIRESTORE_EMULATOR_HOST no configurado). No hay Run ID CI ni resultados externos atribuibles al diff. P0 histórico no determinado; no activar confirmaciones reales ni esta ruta en un negocio real.

El corpus mide política con intenciones inyectadas, no comprensión abierta. La redacción deliberadamente acotada puede ser repetitiva;12 repeticiones no implican aprobación humana. No se añade diagnóstico médico ni selección de profesionales. Antes de datos reales, aprobar retención física, crecimiento de tombstones y confidencialidad del historial. Las pruebas no certifican almacenes distribuidos reales ni entrega WhatsApp.

La siguiente acción autorizable es revisar el diff y, si se aprueba, una muestra externa acotada con el runner sellado, sin motores comerciales ni canal real. No se publica candidata, no se despliega y no se inicia F.
