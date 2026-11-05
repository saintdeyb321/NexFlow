# AgentV2 — implementación local y ensayo autorizado

Fecha: 2026-11-04. Base local y rama remota `feature/llm-first-agent`: `652a277cccce40a9fc18eec5da3d04c66b899888`. `main` local/remota: `5f5ff850e31df3fa73cb2c7f02f65ba6081173c7`. Las referencias remotas se consultaron por el conector GitHub; `git ls-remote` no pudo resolver github.com en el shell. Este trabajo es un diff local, **no un nuevo SHA publicado/certificado**.

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
| `AgentV2Runtime.EvaluateAsync` | Núcleo común sin DialoguePolicy, TurnUnderstandingPipeline ni flujos de negocio Legacy. JSON estricto, máximo 3 llamadas de modelo y 3 herramientas por turno; plazo de 60 s. No crea una segunda memoria durable. |
| Contexto | Perfil público autorizado (nombre/descripción/zona), reloj local, módulos, últimos 4 mensajes acotados, slots/preparación y pregunta vigente del mismo chat. No envía RUC, email, teléfono del perfil ni nombre del paciente desde el borrador. Memoria adicional mínima `ConversationContextDto.AgentV2`: revisión, objetivo y referencias verificadas. |
| `AgentV2Tools.For` | Registro JSON Schema filtrado por capacidades/licencias. El modelo no puede proporcionar WorkspaceId, SQL ni nombres de herramientas arbitrarias. Revalidación de módulos antes de ejecutar y antes de completar. |
| Lecturas | Perfil, sedes, horarios, FAQs, servicios y productos mediante BusinessReadTools. IDs/nombres de sedes proceden de referencias autorizadas. Publicación no equivale a stock ni disponibilidad. |
| Appointments | Disponibilidad mediante el motor actual; preparación reversible mediante AgentBookingDrafts y sus validadores existentes de evidencia/entidad. No expone confirmación de reserva ni cancelación de una cita persistida. |
| Accommodation | Tipos de habitación sólo mediante IDs de SERVICES mapeados expresamente; reglas sólo si existen FAQs autorizadas. No existe motor de inventario por noche: no se anuncia esa herramienta. |
| Handoff | Herramienta explícita con fragmento del mensaje real; reutiliza la autoridad de handoff y el canal durable. Persiste control humano antes del ACK, sin prometer aceptación/notificación del negocio. |
| Recuperación/salida | FirestoreAgentTurnJournal existente: claim, fencing, CAS, resultados y tombstones. AgentDemoChannel/OutboundMessageService existentes: no repost de Sent/Failed/Attempting/UnknownDelivery. No se afirma exactly-once de entrega ni atomicidad Firestore/PostgreSQL. |
| Observabilidad | Motor, identidad de build, revisión, hashes de workspace/mensaje, herramientas registradas ejecutadas, etapas, contadores, duración y clasificación segura de errores. Sin prompts, claves ni PII en logs operativos. |

Configuración opcional: `AgentV2:Workspaces:<Guid>:Enabled`, `Revision`, `CapabilityPacks` (`appointments`/`accommodation`) y `RoomTypeServiceIds`. Se registra en DI y se vincula a configuración sin modificar appsettings ni staging. No se ha activado ningún workspace. El piloto sigue requiriendo identidad/atestación DEMO y bloqueo de escritores comerciales del host ya existentes. No es una garantía de migración global de tenants entre hosts configurados de manera distinta.

### Límite deliberado de publicación natural — NO RESUELTO

Una cita de hechos generada por el propio modelo **no certifica la semántica de su prosa**. El verificador independiente implementado sólo publica valores exactos autorizados o el recibo determinista de handoff. Una reformulación libre, incluso cordial y posiblemente correcta, permanece en `ProposedAnswer` para evaluación aislada; no se guarda como respuesta comercial ni se envía. Se publica una explicación segura de ese límite, sin volver a Legacy.

Por ello esta implementación **NO cumple todavía el criterio de secretaria natural publicable**. No se sustituyó la conversación natural por otra plantilla y se declaró éxito. Falta aprobar/demostrar un contrato verificable de composición más expresivo. No se incorporó un segundo LLM como supuesto certificado de veracidad.

Tampoco están implementados: búsqueda semántica independiente del modelo sobre grandes catálogos, memoria resumida libre, descarte natural de preparación, confirmaciones comerciales AgentV2, operaciones hoteleras por estancia, pedidos, stock físico, pagos ni un editor de capacidades en el panel. Las garantías de los motores existentes siguen intactas; no se anuncian como capacidades nuevas de este piloto.

## Evidencia de pruebas

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

No se encontraron `NEXFLOW_TEST_GROQ_API_KEY` ni `NEXFLOW_LLM_QUOTAS_VERIFIED=1` en el entorno autorizado. No se buscaron claves en appsettings, archivos privados u Oracle. La existencia/cuota del modelo tampoco se certifica sin evidencia del proveedor.

Runner preparado: `AgentV2LiveTrialTests.Authorized_single_Groq_conversation_records_actual_AgentV2_answers_without_commercial_operations`. Usa el adaptador Groq/AiRouter reales y herramientas de lectura reales sobre los fixtures sintéticos existentes. No usa IA predeterminada en ese recorrido, no hay SDK de Firestore real, PostgreSQL, WhatsApp ni canal de producción.

El presupuesto HTTP existente se comparte entre todos los turnos y no reembolsa errores. El primer posible HTTP crea atómicamente `agentv2-groq-20261104.started`; una ejecución posterior no puede reutilizar la autorización. No borrar ese marcador para repetir un ensayo incierto. Se conserva un informe creado exclusivamente una vez con textos sintéticos observados/candidatos frente a publicados, herramientas, estado, latencia, HTTP, modelo y usage realmente reportado. Incluye identidad del build y hashes de los archivos fuente; no representa un SHA nuevo congelado.

Antes de ejecutarlo: verificar cuota/modelo y configurar la clave **sólo en el entorno local**, nunca por chat. Después, en la misma sesión de PowerShell autorizada:

```powershell
# Sólo después de la verificación de cuota y del entorno sintético:
$env:NEXFLOW_LLM_QUOTAS_VERIFIED = '1'
$env:NEXFLOW_AGENT_V2_TRIAL_AUTHORIZED = 'GROQ_120B_SYNTHETIC_ONE_CONVERSATION'
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --filter 'FullyQualifiedName~AgentV2LiveTrialTests' --logger 'trx;LogFileName=agentv2-live-once.trx' --results-directory backend/TestResults/AgentV2_20261104
```

El runner no se ejecutó en esta sesión. Si falta un requisito lo declara bloqueado; no simula respuestas. Las conversaciones candidatas se deben evaluar por una persona, separando calidad semántica, grounding y autorización de publicación. Un JSON válido o el PASS de presupuestos no equivale a una evaluación conversacional aprobada. El corpus privado sigue sin abrirse.

## Decisión

Implementación experimental revisable; **NO-GO para piloto conversacional natural/WhatsApp o despliegue**. Comprensión real, publicación natural verificable, integración de journal en emulador/CI sobre el nuevo SHA y P0 histórico siguen pendientes. El siguiente paso útil es desbloquear la evaluación autorizada y revisar sus respuestas observadas, sin optimizar por frases de este diagnóstico ni abrir otra reconstrucción general.
