# Cierre incremental LLM-first F–K

Base de esta reanudación: feature/llm-first-agent `f537ceea9dae400d6e04f2da1916f0747a4beea0`; main local/ref `5f5ff850e31df3fa73cb2c7f02f65ba6081173c7`. Inicio: índice vacío, único untracked del propietario `docs/NEXFLOW_LLM_FIRST_PLAN_MAESTRO_CIERRE_F_K_2026-11-03.md`, preservado. La publicación de F/G parcial es posterior al checkpoint e054223; no atribuirle verificaciones que no se ejecutaron.

## F — implementada offline; externos pendientes

Evidencia previa conservada: Iteración4 runner Python13/13 y claims/presupuesto5/5; se verificaron archivos y conteos TRX al recuperar. Los reportes son mocks/HTTP interceptado. Corpus externo y rúbrica independiente no existen (confirmación del propietario); NLU/proveedores NOT_RUN. Emulador no configurado: BLOCKED. No se buscaron claves ni hubo llamadas reales.

## G — reanudada

`backend/TestResults/LlmFirstFinal_20261103/g-resumed-validation.trx`:30 ejecutadas,29 PASS,1 FAIL,0 omitidas. El fallo es del escenario: esperaba caída de disponibilidad, pero el fixture existente sí acepta10:00. Se configura explícitamente esa caída sin alterar la aserción; se añade escenario positivo y referencias con TTL. La anterior ejecución no realizada por error de revisión automática no tiene TRX y no cuenta como prueba. La ejecución local volvió a estar disponible en esta reanudación.

G cerrada offline: g-completed.trx34/34 PASS; fallo adicional de opciones ocultas reproducido en g-filtered-reference-red.trx1 FAIL, corregido sin modificar expectativas. Opciones y texto usan el mismo conjunto de hechos filtrados, sin lista redundante. No NLU real.

## H — seguridad offline verificada

h-isolation-retention.trx18/18 PASS:7 mutadores de reservas,3 de pedidos,3 bypass EF y delegación de lectura/otro workspace; protección independiente de IA y Enabled. Retención por lote conserva claims/tombstones y elimina texto vencido; no hay purga automática de datos reales. Request de humano separado de notificación/completado. Nuevo contrato de Firestore Emulator preparado con dos clientes independientes, no ejecutado por ausencia de endpoint aislado; ninguna migración ni conexión externa. P0 original sigue abierto, pendiente de terminar la ruta aislada de I.

## I — canal DEMO y cierre offline

`i-channel-reviewed.trx`25/25; `i-channel-kill-di.trx`28/28; `i-final-directed.trx`66/66; último `i-completed.trx`67/67,0 fallos/omisiones (ejecuciones solapadas de desarrollo, no sumarlas). Se usa el handler, guard, estado, orquestador y OutboundMessageService existentes sobre SDK Firestore con transporte fake. IMessageGateway interceptado; cero WhatsApp real. La instancia/teléfono/workspace se autorizan en backend; ningún parámetro o cabecera abre AgentDemoScope. Ruta antes de interceptores lingüísticos, sin duplicar respuesta legacy/DEMO. TestPhones admite1–5 contactos canónicos; autorización de operador expresa; configuración vacía desactivada conserva legacy. Enabled=false en un workspace protegido es kill switch, no fallback comercial.

`i-kill-reload-red.trx`1 FAIL reprodujo IOptions congelado; ahora la política registrada usa IOptionsMonitor y observa nuevas instancias de configuración. `i-kill-boundary-red.trx`1 FAIL reprodujo caída a legacy al apagar entre ingreso y orquestación; ruta protegida ahora rechaza sin fallback. Ambos rojos preservados. Error de compilación transitorio en el test por importación Moq corregido; intentos sin TRX no cuentan como tests ejecutados.

Preparación en journal y obligación de envío tienen recibos separados. Replay recupera sin IA; UnknownDelivery no repite HTTP. Pending sólo sale si corresponde al texto/identidad preparados y sigue autorizado. Revocación/toma humana bloquean datos aún no enviados. Handoff persiste el modo Human antes del ACK durable; recuperación tras fallo entre ambos no duplica el ACK. Esto no acredita notificación al negocio ni aceptación de un asesor: estados Requested/Persisted/Notified/Completed distintos, no se inventan los dos últimos. P0 histórico sigue ABIERTO; mutaciones y respuesta legacy tardía están AISLADAS_SOLO_DEMO en pruebas locales, no se declara causa histórica resuelta.

Walkthrough10 turnos en `Ten_turn_preparation_preserves_partial_edits_and_side_questions_until_bound_discard`: consulta → captura múltiple → pregunta lateral → cambio de sede → hora puntual validada por motor de lectura → corrección de paciente → consulta → alcance de descarte → pregunta vigente → descarte consentido. Estado y cero efectos comprobados en cada turno; texto completo en StdOut del TRX. Interpretaciones inyectadas, no comprensión real.

## J — BLOCKED_EXTERNAL_EVALUATION; mecanismo listo

Corpus independiente10–15 y rúbrica no existen (confirmado por el propietario). No se generó un sustituto etiquetado como independiente, ni se alteró corpus congelado. El evaluador externo prepara fuera del repo un JSON Version1, Provenance explícita y Conversations10–15. Cada caso: Id alfanumérico/guion único, Category, InitialDraft y Turns3–12; cada turno usa el esquema `AgentEvaluationTurn` de AgentConversationEvaluationTests (Text, Decision, Read/Search opcionales, Goal/Pending/Contains esperados, RemoveModule, AdvanceMinutes, ReplayOf, Outcome). Límite500000 bytes; expectativas sólo en evaluador, nunca enviadas al modelo. Congelar hash SHA256, SHA candidato limpio, fuentes, modelos, rúbrica y fecha antes del ensayo. El implementador no debe abrir ese archivo.

Comando del evaluador: `python -B scripts/llm-first/evaluate.py --live --corpus RUTA_PRIVADA --sha256 HASH --case ID --max-http 12 --max-token-reservation 30000 --output RUTA_NUEVA`. Antes, preflight con los mismos argumentos y `--dry-run`. Requiere autorización separada, cuotas verificadas y entorno NEXFLOW_LLM_LIVE_AUTHORIZED=1 / NEXFLOW_LLM_QUOTAS_VERIFIED=1, NEXFLOW_TEST_GROQ_API_KEY/MODEL y NEXFLOW_TEST_GEMINI_API_KEY/MODEL; no buscar claves en appsettings/Oracle ni pegarlas en chat. Primera autorización sólo una conversación y tope compartido12 intentos/30000 tokens reservados; puede aprobarse un tope menor. No ampliar muestra automáticamente ni borrar marcadores Started/Uncertain/terminal. Errores/fallback también consumen intentos. Tokens reservados no son tokens realmente cobrados. Guardar identity.json, evaluation.trx, live-ID.json, summary.json y marcador, sin PII ni claves.

Dos revisores humanos independientes, si es posible, puntúan comprensión/objetivo, referencias, interrupciones/cancelaciones, pertinencia, naturalidad, claridad, preguntas repetidas y finalización1–5, registrando desacuerdos; seguridad exige0 violaciones y precios/fechas/datos exactos. Umbrales propuestos del contrato: intenciones≥90%, referencias/cancelaciones≥95%, claridad≥4/5; resultados actuales UNKNOWN. Calcular p50/p95 y tokens sólo desde evidencia reportada, atribuir únicamente al proveedor llamado; sin tarifas verificadas, costos UNKNOWN. Comparación legacy/shadow/agente con desarrollo es política simulada, no benchmark ciego ni superioridad comercial.

## K — gate local final completado

G8 Python9/9 PASS. Runner Python13/13 PASS en f-runner-revalidated-green.log; primer intento sandbox errores TEMP, log conservado. `f-preflight.json`: Preflight,0 HTTP, modelos UNKNOWN; primer comando omitió --output y salió2 sin ejecutar pruebas/proveedor. Ningún script Python o workflow necesitó cambios nuevos en este cierre.

| Comprobación | Ejecutadas | PASS | FAIL | Omitidas | Evidencia nueva |
|---|---:|---:|---:|---:|---|
| Dirigidos G/H/I y DI finales |67|67|0|0|i-completed.trx|
| G8 vigente, una ejecución final |2123|2123|0|0|k-current.trx y k-current.g8.json, PASS|
| G8 congelado, una ejecución final |130|101|29|0|k-historical.trx y k-historical.g8.json, EXPECTED_HISTORICAL_INCOMPATIBILITIES|
| Python runner |13|13|0|0|f-runner-revalidated-green.log|
| Python validador G8 |9|9|0|0|k-g8-validator.log|
| Journal Firestore Emulator real |0|0|0|0|BLOCKED: endpoint aislado ausente, no se invocó la prueba|
| Proveedores/WhatsApp/CI/ARM64 |0|0|0|0|NOT_RUN: no autorización/entorno, sin Run IDs ni imagen nueva|

Los29 rojos históricos se conservan como tales (dotnet exit1); el validador salió0 al comprobar inventario, motivos y hashes exactos. H06 permanece **H06/4/A: ReservationEffects**, sin debilitar consentimiento. No se tocaron corpus, pruebas congeladas ni validador. No sumar dirigidos a la suite total: se solapan. Las40 conversaciones de desarrollo se ejecutaron dentro de2123; no se relanzó el corpus completo para fabricar otra comparación.

Comandos finales desde raíz (ejecutados fuera del sandbox Windows cuando VSTest/TEMP lo requerían; sin conexiones reales):

```powershell
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --disable-build-servers --tl:off -m:1 --filter "FullyQualifiedName~AgentPreparationTests|FullyQualifiedName~AgentDemoChannelTests|FullyQualifiedName~AgentRegistrationTests|FullyQualifiedName~AgentDemoSafetyTests|FullyQualifiedName~AgentJournalRetentionTests|FullyQualifiedName~AgentDialogueTests|FullyQualifiedName~AgentDialogueResilienceTests" --logger "trx;LogFileName=i-completed.trx" --results-directory backend/TestResults/LlmFirstFinal_20261103
python -B -m unittest discover -s scripts/llm-first -p test_evaluate.py
python -B -m unittest discover -s .github/workflows -p test_g8_certification.py
python -B scripts/llm-first/evaluate.py --dry-run --output backend/TestResults/LlmFirstFinal_20261103/preflight-unused
python -B .github/workflows/g8_certification.py run current --trx backend/TestResults/LlmFirstFinal_20261103/k-current.trx
python -B .github/workflows/g8_certification.py run historical --trx backend/TestResults/LlmFirstFinal_20261103/k-historical.trx
dotnet build backend/backend.slnx --no-restore --disable-build-servers --tl:off -m:1
git diff --check
git ls-remote origin refs/heads/main refs/heads/feature/llm-first-agent
```

Build final **exit0,0 errores/0 advertencias** incremental (k-build.log). Durante recompilación previa aparecieron3 advertencias existentes: dos CS8981 de migration refactor y CS8602 de FirestoreCatalogRepository; no se alteraron. Frontend no cambió y no se compiló. No restore ni instalación, migración, Docker o servicio externo.

Hashes SHA256 de evidencia (raw bytes; índice completo29 TRX en evidence-index.json, preservando rojos):

- i-completed.trx: `39a0983d114376bb85245cf08f624afac386992496bc79032f058a7f95ad5160`.
- k-current.trx: `f2b4869bc7356e5126ce76578b44f76516828601f6f6058b91a480e25bb8f6f7`.
- k-historical.trx: `7c1614c81173ce42fb141f1be0181f9fbb1380fcacc6146f3921309b8550eac6`.

Walkthrough observado en g-ten-turn-observed.txt extraído del StdOut de i-completed.trx:10 turnos,13 llamadas lógicas simuladas,5 herramientas,0 HTTP real. Primera consulta servicios60/90 PEN verificados; captura sede/servicio/fecha/franja/paciente en un turno; edición sede conserva fecha/paciente; hora10:00 pasa lectura de disponibilidad; consulta lateral no borra slots; descarte con pregunta vinculada elimina sólo borrador. Latencias de VSTest/fixtures no equivalen a p50/p95 de proveedor; tokens/costos/modelos reales UNKNOWN. Recuperación del mismo mensaje agrega0 llamadas IA; entrega inicial revalida journal/capacidades con lecturas backend adicionales, no otra IA.

## Matriz de aceptación verificable

| Requisito | Evidencia exacta (tests locales con scripts/SDK fake, salvo donde se indica) |
|---|---|
| F: ningún PASS live sin reporte/uso/correlación | test_missing_or_partial_live_report_never_passes; test_provider_model_and_transport_are_checked_independently |
| F: admisión HTTP/intentoy presupuesto conservador | Two_runner_instances_reserve_one_HTTP_attempt_and_leave_uncertainty_durable; HTTP_and_conservative_token_caps_cover_fallback_without_unaccounted_attempts; test_unreadable_legacy_marker_is_uncertain |
| G: captura múltiple, variantes de negocio y hechos | Semantic_multiple_fields_are_validated_and_staged_without_commercial_effects; Model_prose_cannot_publish_invented_facts_and_mixed_currencies_stay_exact |
| G: edición/consultas/fecha/hora y descarte vinculados | Ten_turn_preparation_preserves_partial_edits_and_side_questions_until_bound_discard; Exact_time_is_kept_only_when_the_authorized_engine_returns_that_instant |
| G: ambigüedad/referencias actuales/TTL | Ambiguous_or_invented_entity_never_changes_the_draft; Numeric_anaphora_is_bound_to_current_options_and_expiry; Factual_selection_exposes_only_the_entities_actually_presented |
| G: DI y protocolos | Application_registration_resolves_the_planner_drafts_runtime_and_single_DEMO_channel; Provider_failure_is_durable_safe_and_not_reexecuted |
| H: P0 aislado,7 mutadores+3 pedidos+3 bypass EF | Protected_workspace_cannot_reach_any_reservation_mutator_even_with_AI_off; Order_writes_are_blocked_before_the_persistence_adapter; Direct_EF_reservation_writes_cannot_bypass_the_DEMO_barrier |
| H: permisos, CAS, fencing, tenant, cancelación | Change_during_model_work_never_applies_the_late_preparation; Cached_fact_is_not_republished_after_module_revocation_or_identity_change; Foreign_workspace_and_legitimate_cancellation_do_not_reach_IA; Cancellation_after_claim_propagates_and_orphan_is_not_reexecuted; AgentTurnJournalTests dentro de2123 |
| H: retención física sin readmitir/ni borrar dueño vivo | Physical_compaction_removes_payload_and_keeps_the_replay_tombstone; Compaction_is_bounded_scoped_and_does_not_erase_a_live_owner |
| I: canal único/default legacy/DEMO allowlist | Authorized_DEMO_uses_the_existing_durable_outbound_without_experiment_scope_or_legacy_interpretation; Flag_or_harness_scope_off_keeps_legacy_and_cannot_enable_real_traffic; Only_server_authorized_instance_and_test_recipient_can_enter_and_kill_switch_preserves_protection |
| I: replay/duplicados/UnknownDelivery/Pending inseguro | Recovery_reuses_the_prepared_result_and_never_reposts_uncertain_delivery; Simultaneous_duplicate_has_one_model_evaluation_and_one_transport; Unattributed_legacy_pending_text_cannot_be_sent_as_a_DEMO_answer |
| I: handoff durable sin inventar notificación/completado | Human_request_is_not_reported_as_notification_or_completed_handoff; Handoff_persists_human_mode_before_its_single_ack_without_claiming_business_notification; Handoff_ack_survives_failure_between_durable_mode_and_outbound_creation |
| I: revocación/manualtakeover/apagado recargado sin legacy | Prepared_but_unsent_facts_are_not_delivered_after_revocation_or_manual_takeover; Registered_policy_observes_a_reloaded_kill_switch_without_reopening_commercial_writes; Kill_between_ingress_and_orchestration_cannot_fall_through_to_legacy |
| Infraestructura real | Two_emulator_clients_enforce_one_owner_atomic_context_and_physical_tombstone preparado, NO EJECUTADO; no certifica cross-process/IAM |
| J: independencia/semántica/calidad humana | BLOCKED: corpus/rúbrica no existen; mecanismo por hash y prueba test_external_corpus_requires_hash_before_dotnet; no confundir con NLU |
| K: gate/build/integridad/rollback | TRX, hashes y comandos de arriba; RELEASE_CANDIDATE.md, sin despliegue ni imagen nueva |

## Archivos de esta intervención

Backend modificado: Application/DependencyInjection/DependencyInjection.cs; Agent/{AgentContracts,AgentDialogueRuntime,AgentResponseAuthority,LlmFirstShadowAgent}.cs; ProcessMessage/{ProcessIncomingMessageCommandHandler,Services/AiResponseOrchestrator,Services/OutboundMessageService}.cs; Infrastructure/Persistence/{DependencyInjection/DependencyInjection,Firestore/FirestoreAgentTurnJournal}.cs. Nuevos: Agent/{AgentDemoChannel,AgentDemoPolicy,DemoCommercialGuards}.cs y Infrastructure/Persistence/PostgreSQL/Context/DemoReservationWriteInterceptor.cs.

Pruebas modificadas: AssistantConversationTests.cs y Concierge/{AgentDialogueFixture,AgentPreparationTests}.cs. Nuevas: Concierge/{AgentDemoChannelTests,AgentDemoSafetyTests,AgentJournalRetentionTests,AgentRegistrationTests}.cs e Integration/AgentJournalEmulatorTests.cs. No frontend ni nuevos paquetes/frameworks. Scripts, workflows, Docker, migraciones y appsettings intactos.

Documentos: ESTADO.md modificado; este CIERRE_FINAL_LLM_FIRST.md y RELEASE_CANDIDATE.md nuevos. Plan F–K del propietario sigue untracked sin cambios nuestros. Índice vacío; nuevos archivos todavía sin agregar. Inventario exacto, hashes de archivos rastreados/untracked y patch en backend/TestResults/LlmFirstFinal_20261103, ignorados y no stageados. Remotos finales coinciden con inicio: feature f537cee, main5f5ff85; no hay nuevo commit/SHA publicado ni CI atribuible a este diff.

## Riesgos y aceptación de fases

| Fase | Resultado real |
|---|---|
| F | PASS offline; emulador/proveedor BLOCKED/NOT_RUN |
| G | PASS funcional offline con interpretación inyectada; naturalidad/semántica real NO VERIFICADAS |
| H | PASS barreras/retención/replay en fixtures; IAM/cross-process/purga real BLOCKED |
| I | PASS ingreso/salida/handoff simulados; WhatsApp real NOT_RUN |
| J | BLOCKED_EXTERNAL_EVALUATION, no certificación comercial |
| K | PASS gates/build/diff locales y candidata preparada; CI/ARM64/destino/imagen PENDING |

Privacidad: CompactBatchAsync sólo se probó con SDK fake, sin ejecución programada ni API. El payload vence24h lógicamente y se elimina al observar/compactar; sin visita/compactación podría permanecer. No elimina contexto ni historial/outbound. Tombstone conserva fence/identidad indefinidamente para evitar replay, crecimiento1 documento/turno: coste y caducidad operativa de privacidad requieren volumen/tarifas/revisión antes de PII. No se certificó limpieza Firestore real ni IAM. El modelo sólo selecciona actos/frases acotados y hechos, no se publica prosa libre: naturalidad sigue limitada y necesita J. El ACK de humano no prueba aviso/recepción por asesor. Kill switch observa proveedores recargables; env necesita reinicio autorizado y HTTP ya enviado no se puede deshacer. No existe exactly-once de transporte.

P0 histórico **ABIERTO** sin trazas de Oracle; no se inventa causa ni solución. **AISLADO_SOLO_DEMO offline** por ausencia de writer en runtime+decoradores+interceptor, barreras configuradas también con IA apagada, y rechazo del viejo Pending verbal. No autoriza operaciones reales ni sustituye CI/integraciones. Rollback a imagen sin barreras exige aislar/pausar ingreso DEMO primero. No hubo acceso a Oracle, mensajes, IA externa, datos reales ni migraciones.

## Próximo paso y gates

**OFFLINE_GO=GO**, sólo árbol local evaluado y datos sintéticos. **PROVIDER_EVAL_GO=NO_GO/BLOCKED** por corpus, rúbrica y autorización/cuotas. **DEMO_DEPLOY_GO=NO_GO/WAITING_FOR_OWNER** por SHA final, cuatro CI, emulador/ARM64, custodia y destino/ventana autorizados. **COMMERCIAL_GO=NO_GO** por P0 histórico y aceptación/privacidad reales pendientes. Revisar este diff, después el propietario autoriza separadamente commit/push y cuatro workflows del mismo SHA. Ninguna publicación, workflow, proveedor, WhatsApp o despliegue ejecutado; main intacta. Detenido para auditoría, no se abre otra fase.
