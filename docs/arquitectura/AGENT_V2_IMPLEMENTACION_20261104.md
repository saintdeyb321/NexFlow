# AgentV2 — implementación local y ensayo autorizado

Estado actualizado: **2026-11-05**. HEAD local y remoto de `feature/llm-first-agent`: **`d5f8fc515c4b127183535426eba207ead0d865f8`**, comprobados en Git y mediante GitHub REST. `main` permanece en `5f5ff850e31df3fa73cb2c7f02f65ba6081173c7`. La implementación del día anterior **sí fue publicada posteriormente** en d5f8fc5; queda rectificada la descripción de «diff no publicado». Las evidencias del 04/11 que siguen corresponden a la sesión original sobre 652a277. Las correcciones del 05/11 son un nuevo diff local, todavía sin commit/push ni certificación remota propia.

Se preservaron los cambios previos de `ESTADO.md` y los archivos del propietario sin seguimiento. No se abrieron los archivos privados de evaluación. Sin commit, push, merge, despliegue, proveedores reales ni operaciones de Oracle.

## Diagnóstico demostrado

- `ProcessIncomingMessageCommandHandler.HandleCoreAsync`: antes del parche sólo `IAgentDemoPolicy.Protects` desviaba el ingreso a DEMO. Los demás turnos pasaban a `ConversationStateService`, rutas rápidas de conocimiento y `AiResponseOrchestrator`. La nueva selección está antes de esas decisiones lingüísticas (archivo:70).
- `AiResponseOrchestrator.RespondCoreAsync`: `TryResumeAsync` de pedidos/reservas (87/93), `RecoverAsync` (120) y `PrepareAsync` (130) preceden a los motores. No basta con cambiar una clase del modelo para entregar al LLM el control del diálogo.
- `AgentHostProtection.Load` (17–25): el rol sin señales DEMO es Legacy. La selección del nuevo motor requiere la protección DEMO ya existente; no se habilita en un tenant comercial.
- `AgentResponseAuthority.Prepare` (129): elimina el texto libre de la propuesta y publica segmentos realizados por el servidor. Es un límite intencional del prototipo anterior, no evidencia de NLU del proveedor.
- Prueba roja de routing: `routing-red.trx`, 1 ejecutada/0 aprobadas/1 fallida. El handler no invocaba el nuevo ingreso. Verde posterior: `routing-green-2.trx`, 1/1.
- Defecto encontrado al componer la nueva ruta: el journal compartido podía devolver un recibo del motor anterior como si fuese AgentV2. `journal-engine-red.trx`, 1/0/1. La recuperación ahora exige el marcador AgentV2 y su revisión en el contexto asociado, además de versión, identidad y permisos. La regresión original se conserva.

No se atribuye una causa nueva al P0 histórico de reserva inesperada: sigue **PENDIENTE**.

## Arquitectura implementada

`Webhook/worker existentes → guard existente → AgentV2Ingress → AgentV2Runtime → AiRouter → herramientas tipadas → AiRouter → verificador → journal/contexto CAS → AgentDemoChannel/outbound existentes`.

| Componente | Responsabilidad y límite |
|---|---|
| `AgentV2Ingress.TryHandleAsync` | Selección explícita por workspace/revisión, apagada por defecto; exige instancia/teléfono DEMO autorizados. Selección deshabilitada, eliminada después de usarla o revisión distinta no vuelve silenciosamente a Legacy. Los tenants ordinarios no añaden lecturas de contexto. |
| `AgentV2Runtime.EvaluateAsync` | Núcleo común sin DialoguePolicy, TurnUnderstandingPipeline ni flujos de negocio Legacy. JSON estricto, máximo 3 llamadas de diálogo + 1 revisión semántica y 3 herramientas por turno; plazo de 60 s. No crea una segunda memoria durable. |
| Contexto | Perfil público autorizado (nombre/descripción/zona), reloj local, módulos, últimos 4 mensajes acotados, slots/preparación y pregunta vigente del mismo chat. No envía RUC, email, teléfono del perfil ni nombre del paciente desde el borrador. Memoria adicional mínima `ConversationContextDto.AgentV2`: revisión, objetivo y referencias verificadas. |
| `AgentV2Tools.For` | Registro JSON Schema filtrado por capacidades/licencias. El modelo no puede proporcionar WorkspaceId, SQL ni nombres de herramientas arbitrarias. Revalidación de módulos antes de ejecutar y antes de completar. |
| Lecturas | Perfil, sedes, horarios, FAQs, servicios y productos mediante BusinessReadTools. IDs/nombres de sedes proceden de referencias autorizadas. Publicación no equivale a stock ni disponibilidad. |
| Appointments | Disponibilidad mediante el motor actual; preparación reversible mediante AgentBookingDrafts y sus validadores existentes de evidencia/entidad. No expone confirmación de reserva ni cancelación de una cita persistida. |
| Accommodation | Tipos de habitación sólo mediante IDs de SERVICES mapeados expresamente; reglas sólo si existen FAQs autorizadas. No existe motor de inventario por noche: no se anuncia esa herramienta. |
| Handoff | Herramienta explícita con fragmento del mensaje real; reutiliza la autoridad de handoff y el canal durable. Persiste control humano antes del ACK, sin prometer aceptación/notificación del negocio. |
| Recuperación/salida | FirestoreAgentTurnJournal existente: claim, fencing, CAS, resultados y tombstones. AgentDemoChannel/OutboundMessageService existentes: no repost de Sent/Failed/Attempting/UnknownDelivery. No se afirma exactly-once de entrega ni atomicidad Firestore/PostgreSQL. |
| Observabilidad | Motor, identidad de build, revisión, hashes de workspace/mensaje, herramientas registradas ejecutadas, etapas, contadores, duración y clasificación segura de errores. Sin prompts, claves ni PII en logs operativos. |

Configuración opcional: `AgentV2:Workspaces:<Guid>:Enabled`, `Revision`, `CapabilityPacks` (`appointments`/`accommodation`) y `RoomTypeServiceIds`. Se registra en DI y se vincula a configuración sin modificar appsettings ni staging. No se ha activado ningún workspace. El piloto sigue requiriendo identidad/atestación DEMO y bloqueo de escritores comerciales del host ya existentes. No es una garantía de migración global de tenants entre hosts configurados de manera distinta.

### Publicación natural — corrección del 05/11

La concatenación exacta dejó de ser la única vía de publicación. El modelo redacta el mensaje con anclajes `{{id}}` para los hechos. El backend sustituye esos anclajes con valores autorizados, conserva la redacción circundante y hace una revisión semántica separada antes de completar el mismo journal/outbound. Los IDs los produce la herramienta del workspace, nunca el modelo.

Controles deterministas: referencias/fuentes/duplicados, sintaxis, límites, números y símbolos monetarios fuera de anclajes, entidades comerciales copiadas fuera de ellos, precio vinculado a oferta + moneda + importe, fechas y disponibilidad renderizadas desde el motor y zona autorizada. La revisión devuelve fragmentos que deben cubrir literalmente TODO el borrador, con referencias coincidentes; omisiones, tipos desconocidos, ambigüedad, JSON inválido y errores del proveedor rechazan la propuesta. El modo extractivo anterior queda limitado a hechos tipados; una FAQ no puede hacerse pasar por recibo comercial.

**Política para prosa libre:** se admiten cortesía, empatía, preguntas y orientación tentativa apoyada en las descripciones reales. Las afirmaciones comerciales deben estar ancladas. Se prohíben diagnósticos, promesas de stock/cupo, confirmaciones/cancelaciones o reglas que no devuelvan herramientas autorizadas. Un rechazo publica como alternativa hasta dos hechos seleccionados y una pregunta útil; no llama a Legacy ni ejecuta una reparación automática del proveedor.

**Riesgo residual explícito:** sujeto, polaridad, inferencias, cantidades escritas con palabras y afirmaciones encubiertas requieren juicio semántico. Esa revisión es otra llamada de AiRouter con instrucciones/contexto distintos; puede usar el mismo modelo/proveedor y sus errores pueden correlacionarse. No es una prueba formal ni una garantía absoluta de ausencia de alucinaciones. La exactitud de los valores anclados y las barreras comerciales son deterministas; la calidad/fiabilidad del juicio semántico necesita ensayo real y revisión humana. No se declara certificada con mocks ni habilita operaciones comerciales.

Tampoco están implementados: búsqueda semántica independiente del modelo sobre grandes catálogos, memoria resumida libre, descarte natural de preparación, confirmaciones comerciales AgentV2, operaciones hoteleras por estancia, pedidos, stock físico, pagos ni un editor de capacidades en el panel. Las garantías de los motores existentes siguen intactas; no se anuncian como capacidades nuevas de este piloto.

## Evidencia original de pruebas — sesión del 04/11

Todos los TRX nuevos están en `backend/TestResults/AgentV2_20261104/`; los rojos se conservan. Son pruebas con interpretación inyectada y repositorios sintéticos, **no pruebas de comprensión de Groq/Gemini ni de Firestore Emulator real**.

| Requisito | Prueba exacta |
|---|---|
| Routing previo a Legacy | `Selected_AgentV2_owns_the_turn_before_any_legacy_model_menu_or_business_flow` |
| Misma arquitectura/dos negocios y datos distintos | `Same_runtime_uses_authorized_workspace_context_and_different_capability_adapters` |
| No mezclar recibos de motores | `Legacy_journal_receipt_cannot_be_mistaken_for_an_AgentV2_response` |
| Outbound durable, replay y retirada de selección | `Actual_ingress_delivers_once_via_existing_outbound_and_configuration_removal_cannot_enter_legacy` |
| Entrega incierta/kill switch | `Unknown_delivery_is_not_reposted_and_disabled_selection_never_falls_back` |
| Control humano | `Model_selected_handoff_persists_human_ownership_before_one_ack_and_suppresses_later_automation` |
| Tenant/licencia/herramienta/argumentos | `Tool_provenance_rejects_foreign_workspace_and_drops_facts_from_denied_results`, `Unregistered_tools_and_workspace_or_invalid_arguments_never_execute`, `Revoked_modules_remove_tools_and_reject_in_flight_reads`, `Model_invented_foreign_service_id_cannot_become_a_preparation` |
| Preparación recuperable sin efectos comerciales | `Preparation_uses_existing_slot_validation_and_keeps_its_memory_on_replay_without_commercial_effects` |
| CAS/concurrencia/control humano durante espera | `Concurrent_turn_replay_does_not_repeat_the_model_and_late_result_loses_CAS` |
| JSON, timeout y límites | `Invalid_provider_or_budget_result_cannot_publish_or_fall_back_to_legacy` |
| Texto libre no autoautorizado | `Natural_candidate_is_observable_but_self_citation_cannot_authorize_outbound_or_journaling`, `Independent_verifier_rejects_invented_citations_or_prose` |
| Compatibilidad apagado/documentos anteriores | `Revision_disabled_scope_and_legacy_context_compatibility_are_fail_closed`, `Unselected_legacy_workspace_requires_no_extra_context_or_business_reads` |
| Groq exclusivo sin fallback | `Exclusive_Groq_trial_has_no_fallback_on_429_for_conversation_tasks` |

Regresión dirigida final: `directed-final.trx`: **26 ejecutadas, 26 PASS, 0 FAIL, 0 omitidas**. Build `dotnet build backend/backend.slnx --no-restore --disable-build-servers --tl:off -m:1`: **PASS**, 0 errores/0 advertencias en el build incremental. Compilaciones previas de pruebas mostraron las 3 advertencias preexistentes CS8981 (dos migraciones históricas) y CS8602 en FirestoreCatalogRepository:228; no se modificaron esos archivos.

Gates finales ejecutados una sola vez sobre este diff local:

| Gate | Ejecutadas | PASS | FAIL | Omitidas | Decisión |
|---|---:|---:|---:|---:|---|
| `current-final.trx` | 2188 | 2188 | 0 | 0 | PASS, test exit0 y validador exit0 |
| `historical-final.trx` | 130 | 101 | 29 | 0 | EXPECTED_HISTORICAL_INCOMPATIBILITIES, test exit1 y validador exit0 |

Los 29 fallos históricos conservan exactamente identidad/motivo aprobados. H06 permanece `H06/4/A: ReservationEffects`. Hashes de los cuatro corpus/evaluadores congelados verificados sin modificaciones. Los JSON `.g8.json` adyacentes contienen conteos y motivos; no hay Run ID remoto para este diff. Comandos:

```powershell
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --disable-build-servers --tl:off -m:1 --filter 'FullyQualifiedName~AgentV2&Category!=ProviderIntegration' --logger 'trx;LogFileName=directed-final.trx' --results-directory backend/TestResults/AgentV2_20261104
dotnet build backend/backend.slnx --no-restore --disable-build-servers --tl:off -m:1
python -B .github/workflows/g8_certification.py run current --trx backend/TestResults/AgentV2_20261104/current-final.trx
python -B .github/workflows/g8_certification.py run historical --trx backend/TestResults/AgentV2_20261104/historical-final.trx
git diff --check
```

`git diff --check`: PASS. Antes de los tests se eliminaron del entorno hijo las variables de corpus/evaluación privados y las autorizaciones de ensayos reales; los filtros oficiales excluyen ProviderIntegration/Integration/RedisIntegration. No se ejecutaron emuladores, proveedores, WhatsApp, ARM64 ni CI remoto. No se atribuyen esos gates a esta implementación.

Archivos de esta tarea: cinco nuevos `.cs` en `backend/NexFlow.Application/Features/Automation/AgentV2/`; nuevos `AgentV2RoutingTests.cs`, `AgentV2RuntimeTests.cs` y `AgentV2LiveTrialTests.cs`; cambios de handler, contexto opcional, AiRouter, los dos registros DI y los fixtures `AssistantConversationTests.cs`/`AgentDialogueFixture.cs`; este informe y un checkpoint añadido a ESTADO sin sustituir el contenido previo. Frontend, corpus congelados, motores transaccionales, reglas de consentimiento, appsettings, staging y esquemas no cambiaron.

## Ensayo Groq autorizado — BLOQUEADO antes del primer HTTP

Autorización recibida: una conversación sintética odontológica, exclusivamente Groq `openai/gpt-oss-120b`, ≤12 intentos HTTP, ≤30.000 tokens reservados acumulados, sin fallback ni repetición automática. **0 HTTP realizados. No existe conversación observada que entregar ni una respuesta real que transcribir.**

Preflight actualizado del **05/11**, en el entorno efectivo del testhost: clave ausente tanto en proceso como en variables del usuario; cuota verificada ausente en ambos. Marcador one-shot e informe previo ausentes. **BLOCKED / 0 HTTP**. No se buscaron claves en appsettings, archivos privados u Oracle. La existencia/cuota del modelo tampoco se certifica sin evidencia del proveedor.

Runner preparado: `AgentV2LiveTrialTests.Authorized_single_Groq_conversation_records_actual_AgentV2_answers_without_commercial_operations`. Usa Groq/AiRouter reales para diálogo y revisión, herramientas reales sobre los fixtures sintéticos existentes, journal y outbound con transporte interceptado. No usa interpretaciones/revisiones predeterminadas en ese recorrido, PostgreSQL, Firestore real ni WhatsApp. Registra candidato, texto efectivamente enviado al transporte interceptado, herramientas/hechos, memoria, latencia y usage. No ejecutado todavía.

El presupuesto HTTP existente se comparte entre todos los turnos **incluida la revisión**, sin reembolso, fallback ni repetición automática: 12 HTTP/30.000 tokens reservados. El primer posible HTTP crea atómicamente `agentv2-groq-20261104.started`; un marcador o informe anterior bloquea nuevas llamadas. No borrarlos para repetir un ensayo incierto. El reloj del negocio es sintético, con un minuto entre turnos: no es una medición de throughput. El informe incluye identidad del build/hashes de fuentes; un diff local no equivale a un SHA nuevo certificado.

Antes de ejecutarlo: verificar cuota/modelo y configurar la clave **sólo en el entorno local**, nunca por chat. Después, en la misma sesión de PowerShell autorizada:

```powershell
# Sólo después de la verificación de cuota y del entorno sintético:
$env:NEXFLOW_LLM_QUOTAS_VERIFIED = '1'
$env:NEXFLOW_AGENT_V2_TRIAL_AUTHORIZED = 'GROQ_120B_SYNTHETIC_ONE_CONVERSATION'
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --filter 'FullyQualifiedName~AgentV2LiveTrialTests' --logger 'trx;LogFileName=agentv2-live-once.trx' --results-directory backend/TestResults/AgentV2_20261104
```

El runner no se ejecutó en esta sesión. Si falta un requisito lo declara bloqueado; no simula respuestas. Las conversaciones candidatas se deben evaluar por una persona, separando calidad semántica, grounding y autorización de publicación. Un JSON válido o el PASS de presupuestos no equivale a una evaluación conversacional aprobada. El corpus privado sigue sin abrirse.

## Decisión

Implementación natural funcional comprobada offline; **NO-GO para activación DEMO/WhatsApp o despliegue** hasta evaluar modelo real, revisión humana y CI/emulador sobre el nuevo SHA. P0 histórico no determinado. No se atribuyen resultados sintéticos al modelo real.

## Cierre del 05/11 — custodia, correcciones y gates

- **Custodia:** los dos archivos publicados `NEXFLOW_HOLDOUT_PRIVADO_V2_2026-11-04.json` y `NEXFLOW_RUBRICA_PRIVADA_V2_2026-11-04.md` se copiaron sin visualizar contenido a una carpeta privada fuera del repositorio, con ACL de usuario/SYSTEM y SHA-256 idénticos (2/2). Manifiesto local en `%LOCALAPPDATA%/NexFlow/PrivateEvaluationCustody/exposed-v2-20261105-6b5cd232890e4ee4b79a34d2ccf719b3/`. Se preparó `git rm --cached` de ambos y se añadieron ignores; los originales locales permanecen. La retirada pública requiere el commit aprobado y no borra la historia. Material **EXPOSED_NOT_BLIND**; no se usó para ajustar código/prompts.
- **Causas reproducidas:** concatenación exacta impedía prosa válida (`natural-red.trx`); la cuota agotada dejaba el turno sin respuesta durable (`quota-reply-red.trx`); proyectar LastQuestion sin procedencia podía enviar un resumen heredado privado (`question-privacy-red.trx`); el preparador admitía nombres/menús pero rechazaba referencias naturales respaldadas por la conversación (`reference-red.trx`: 1 fallo/3 negativos seguros). Todos los rojos se conservan.
- **Continuidad:** se recuerda la pregunta publicada con workspace, conversación, versión y `QuestionSourceMessageId` en la memoria opcional existente. Sólo se proyectan sus últimos 500 caracteres cuando coinciden las referencias; documentos anteriores sin ese campo usan la proyección tipada mínima. Hasta cinco referencias tipadas (Service/Product/Location/RoomType) proceden únicamente de hechos realmente publicados, incluidos los hechos del fallback; se conservan durante interrupciones.
- **Preparación por referencia:** el modelo puede proponer ID, fragmento literal actual y QuestionId. El preparador existente exige pregunta vigente del mismo chat, publicación AgentV2, tipo de entidad y exactamente una candidata actualmente autorizada para ese campo; revalida dependencias como antes. Una referencia ambigua/obsoleta/ajena o un ID de producto coincidente con un ID de servicio no selecciona nada. No se fabrican evidencias ni menús y no se convierte esto en consentimiento comercial.
- **Cuota:** la revisión consume su propia admisión idempotente. Se mantienen 6 llamadas/minuto por cliente y 60 por workspace: dos turnos de lectura + respuesta + revisión pueden consumir el límite del cliente. El agotamiento prepara una respuesta segura en el journal sin otra llamada, reembolso ni pérdida del borrador. No se ampliaron límites productivos.
- **Datos:** descripción y precio de la oferta son hechos del catálogo autorizado; importes conservan la convención existente de unidades menores/100. La disponibilidad usa UTC del motor convertido con la zona del perfil, con aviso de consulta instantánea y ausencia de reserva. No se añadió inventario de hotel, stock, pedidos, pagos ni confirmación/cancelación comercial.

### Evidencia nueva (modelos inyectados, 0 HTTP)

Directorio: `backend/TestResults/AgentV2_20261105/`. `acceptance-final.trx`: **76 ejecutadas/76 PASS/0 FAIL/0 omitidas** (65 AgentV2 + 11 regresiones del preparador existente). El TRX contiene transcripciones completas sintéticas con las etiquetas `INJECTED_INTERPRETATION_AND_REVIEW_NOT_REAL_NLU` e `INJECTED_MODELS_REAL_JOURNAL_INTERCEPTED_WIRE`.

Texto observado en el transporte interceptado: «Lo vemos con calma. Podemos revisar Evaluación odontológica: PEN 60.00. ¿Qué te gustaría precisar?». Fue redactado por la respuesta de modelo **inyectada** y publicado por AgentV2 después de validación; no es una respuesta de Groq. Precio/nombre salen de las herramientas, no de cifras propuestas por esa respuesta.

| Requisito | Prueba exacta |
|---|---|
| Prosa natural + precio vinculado | `Independently_reviewed_natural_text_preserves_wording_and_backend_bound_price` |
| Publicación real por journal/outbound interceptado, una vez | `Natural_text_is_really_journaled_and_sent_once_through_intercepted_outbound` |
| Clínica, sede con error, preparación y consulta lateral | `Clinic_multiturn_preparation_typo_location_and_price_interruption_preserve_context_without_writes` |
| Otro sector/capacidades/datos propios | `Hotel_uses_same_runtime_with_room_facts_and_policies_without_inventing_inventory` |
| Pregunta vigente y privacidad de contexto anterior | `Current_published_question_survives_history_truncation_without_using_a_stale_menu`, `Private_profile_and_patient_fields_or_wrong_history_roles_do_not_enter_model_or_review_context` |
| Preparación por referencia válida/ambigua/obsoleta/ajena/colisión de tipo | `Preparation_references_require_current_question_and_one_authorized_entity_per_field` |
| Importes/fechas/entidades/citas inventadas | `Invalid_values_entities_or_citations_are_rejected_before_review`, `Extractive_compatibility_cannot_bypass_provenance_or_publish_a_free_business_receipt` |
| Polaridad/cobertura y afirmaciones no sustentadas | `Omitted_claims_unverified_polarity_and_unsupported_speech_acts_cannot_publish`, `A_review_cannot_certify_a_free_business_assertion_without_an_anchor` |
| Disponibilidad del motor, no confirmación | `Availability_wording_uses_engine_UTC_result_converted_by_the_authorized_business_zone` |
| Permisos/humano/CAS durante revisión | `Changes_during_independent_review_suppress_natural_publication` |
| Fallos/rechazos sin retry y alternativa útil | `Semantic_review_failures_fail_closed_without_retries`, `Rejected_or_unavailable_review_provides_only_selected_verified_facts_and_no_retry` |
| Cuota agotada, replay y borrador | `Exhausted_real_client_window_returns_a_durable_safe_reply_without_extra_model_or_draft_loss` |
| Groq exclusivo para diálogo y revisión | `Exclusive_Groq_trial_has_no_fallback_on_429_for_conversation_tasks` (ComplexChat y ResponseComposition) |

Build backend final: PASS, 0 errores/0 advertencias incremental. Compilaciones anteriores de pruebas mostraron únicamente las tres advertencias preexistentes ya descritas. Se conservan los primeros gates verdes y los rojos de reproducción; las repeticiones del gate completo correspondieron a cambios posteriores demostrados de privacidad y preparación por referencia, no a reruns para ocultar fallos.

| Evidencia final del 05/11 | Ejecutadas | PASS | FAIL | Omitidas | Resultado |
|---|---:|---:|---:|---:|---|
| `acceptance-final.trx` | 76 | 76 | 0 | 0 | PASS |
| `current-acceptance-final.trx` | 2227 | 2227 | 0 | 0 | PASS, validador exit0 |
| `historical-acceptance-final.trx` | 130 | 101 | 29 | 0 | EXPECTED_HISTORICAL_INCOMPATIBILITIES, validador exit0 |

Histórico: dotnet test exit1 esperado, 29 identidades/motivos exactos comprobados y hashes congelados intactos; H06 continúa `H06/4/A: ReservationEffects`. Los `.g8.json` y el log completo histórico son adyacentes. `source-manifest.json` identifica por SHA-256 los nueve archivos fuente de este cierre y la base d5f8fc5; `synthetic-transcripts.json` extrae las tres pruebas de conversación/salida del TRX final. `git diff --check` y comprobación del índice: PASS. Compilación explícita del proyecto de tests: PASS, sin ejecutar el runner de pago. Los env de corpus privados y autorizaciones de pruebas reales se quitaron del proceso hijo de los gates. Comandos finales:

```powershell
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --disable-build-servers --tl:off -m:1 --filter '(FullyQualifiedName~AgentV2&Category!=ProviderIntegration)|FullyQualifiedName~AgentPreparationTests' --logger 'trx;LogFileName=acceptance-final.trx' --results-directory backend/TestResults/AgentV2_20261105
dotnet build backend/backend.slnx --no-restore --disable-build-servers --tl:off -m:1
python -B .github/workflows/g8_certification.py run current --trx backend/TestResults/AgentV2_20261105/current-acceptance-final.trx
python -B .github/workflows/g8_certification.py run historical --trx backend/TestResults/AgentV2_20261105/historical-acceptance-final.trx
git diff --check
```

Archivos de este cierre: `.gitignore`; retirada del índice de los dos materiales expuestos; `AgentV2Contracts.cs`, `AgentV2AnswerVerifier.cs`, `AgentV2Runtime.cs`, `AgentV2Tools.cs` y el adaptador de preparación `AgentBookingDrafts.cs`; `AgentV2RuntimeTests.cs`, `AgentV2LiveTrialTests.cs`; nuevos `AgentV2GroundingTests.cs` y `AgentV2NaturalConversationTests.cs`; este informe y ESTADO. Sin frontend, motores transaccionales, migraciones, infraestructura, appsettings, secretos ni staging modificados.

### Publicación y certificación pendientes

Después de la aprobación del conjunto, un único commit/push **sólo** a `feature/llm-first-agent`. No se ha ejecutado. Los cuatro workflows existentes deben correr sobre el **nuevo SHA**: `rc3-release-checks.yml`, `rc3-reservation-integration.yml`, `concierge-redis-integration.yml`, `agent-runtime-arm64-certification.yml` (input `release_sha` igual al SHA despachado; no habilitar custodia de imagen sin autorización adicional). Verificar head_sha, Run ID/attempt, conclusión, conteos/omisiones, TRX e identity.json y los smokes ARM64. No reutilizar verdes de d5f8fc5.

Decisiones: **OFFLINE_GO** para revisión de código/evidencias; **PROVIDER_EVAL_GO BLOCKED** por clave/cuota; **DEMO_DEPLOY_GO NO** y **COMMERCIAL_GO NO**. Persisten la calibración semántica/humana, reemplazo del holdout expuesto por otro independiente, CI/emulador del nuevo SHA y P0 histórico. No se ha activado la ruta en ningún negocio ni realizado operaciones comerciales.
