# Iteración 2 — Auditoría local y cierre del defecto de replay

## Identidad y conservación

Rama `feature/llm-first-agent`. HEAD/main local y ambas refs remotas verificadas: `5f5ff850e31df3fa73cb2c7f02f65ba6081173c7`. Índice vacío. Se conservaron los seis archivos de código de Iteración1, su checkpoint y los dos documentos del propietario sin seguimiento. No hubo checkout, stash, reset, commit, push ni migración.

Se inspeccionaron completos AgentContracts, AgentReadTools, LlmFirstShadowAgent, LlmFirstShadowTests y DI, siguiendo AiResponseOrchestrator, AiRouter, ConversationContextDto/IConversationCache, ConversationQuestion y ContextRecoveryService. Los hashes de los siete archivos del manifiesto anterior coincidían **antes de editar**. Los TRX existentes se leyeron y su SHA256 coincidió con `backend/TestResults/LlmFirst20261103/identity-local.json`:

| Evidencia recibida | Verificación local |
|---|---|
| iteration1-reviewed.trx | PASS:39/39,0 fallos/omisiones |
| current-iteration1.trx | PASS:1975/1975,0 fallos/omisiones |
| historical-iteration1.trx | PASS del contrato esperado130/101/29,0 omisiones |
| Identidad y hashes | PASS al entrar, no evidencia GitHub/producción |

Estos archivos no se sobrescribieron. Son evidencia del árbol anterior; los nuevos resultados tienen rutas/nombres distintos.

## Hallazgo bloqueante P1 — Evaluación repetida del mismo mensaje

**CONFIRMADO Y REPRODUCIDO.** En el LlmFirstShadowAgent recibido, `EvaluateAsync` generaba un `evaluationId` aleatorio para cada invocación y lo incorporaba al presupuesto. Sólo comparaba el mensaje contra algunos marcadores legacy; no existía una admisión duradera exclusiva del shadow. El propio test `Repeated_shadow_evaluations_charge_their_own_budget_without_business_effects` admitía seis llamadas del mismo mensaje.

`iteration2-audit-red.trx`:2 ejecutadas,0 PASS/2 FAIL/0 omitidas. Ambos duplicados resultaron Accepted=true:

- `Audit_replay_after_recreating_the_agent_does_not_call_the_model_again`.
- `Audit_concurrent_duplicate_is_rejected_while_the_first_model_call_is_running`.

No se reprodujeron reservas duplicadas ni envíos: el shadow no tiene esos efectos. El defecto era duplicación de intentos IA/consumo y ausencia de deduplicación propia. No explica ni resuelve el P0 histórico de reservas.

**Gate activado:** la especificación principal, §0, exige: «Si el agente shadow [...] omite deduplicación [...] detener el avance al terminar el arreglo y pruebas del defecto». Por ello se corrige y verifica este hallazgo y se detiene **antes de C/D/E**. No se integra el agente al inbound ni se habilita el piloto.

## Corrección mínima

- Nuevo `IAgentEvaluationClaims` con Started/Duplicate/Conflict. Dependencia requerida; no fallback en memoria ni admisión abierta si falla el almacén.
- `FirestoreAgentEvaluationClaims` reutiliza Firestore y el namespace de presupuesto existente: `workspaces/{workspace}/conversationStates/ai-budget/shadowEvaluations/{SHA256(sourceMessageId)}`. Es un marcador de admisión IA, **no otro borrador ni estado conversacional**.
- Creación atómica con precondición exists=false. Sólo una réplica puede comenzar la evaluación. Otro intento lee el marcador: misma huella → Duplicate; mensaje reasignado a otro texto/cliente/conversación → Conflict. Workspace pertenece a la ruta autenticada.
- Se guarda únicamente versión1, huella SHA256 de la identidad de petición y startedAt UTC. No almacena teléfonos, texto, respuesta ni source/conversation IDs en claro. Es metadato pseudonimizado, no anonimización legal certificada.
- El guard se ejecuta tras autorización/humano/replay legacy/licencia, antes del historial, presupuesto y router. Duplicate devuelve `EvaluationReplay`, conflicto `EvaluationIdentityConflict`, ambos con0 llamadas lógicas. Se elimina el GUID aleatorio del presupuesto.
- No se libera ni caduca automáticamente el marcador por timeout, error, cambio de ventana o pérdida de ACK. Un fallo previo al commit permite una admisión posterior; si el commit ocurrió pero se perdió la respuesta, el siguiente intento no vuelve a entrar al modelo.
- No se guardan resultados ni se afirma que el replay terminó correctamente. Es **at-most-once**, no exactly-once de respuesta: una caída tras la admisión puede dejar la evaluación sin resultado recuperable. Esto se acepta sólo para el shadow interno apagado; C requiere revisar recuperación/resultados duraderos antes de una vía de consumidor.
- Cada petición nueva admitida añade una escritura de metadatos; un duplicado consulta un marcador. Retención, volumen/costo y limpieza requieren política antes del piloto. No se añadió TTL que reabra replays ni se borraron datos.

## Matriz de auditoría negativa

| Requisito | Estado verificado | Evidencia |
|---|---|---|
| No outbound ni motor comercial | PASS local | Dependencias del shadow sólo router/lecturas/claims; pruebas Bookings/Orders vacíos; sin servicio outbound |
| No altera borrador/pregunta/CAS persistido | PASS local | `Shadow_keeps_cancel_reference_and_information_without_mutating_the_durable_draft` compara contexto completo; marcadores separados del contexto |
| Workspace y licencia | PASS local | `Disabled_foreign_human_replay_and_unknown_delivery_never_reach_the_model`, `Read_tools_recheck_workspace_licenses_before_consulting_restricted_repositories` |
| Procedencia de facts/privacidad de lecturas | PASS local | `Read_tool_provenance_cannot_be_substituted_by_another_workspace_or_private_data`; tools revalidan source/tenant/Sensitive |
| Sin texto sensible en telemetría estándar | PASS por inspección | Contadores con outcomes/failure kinds; sin prompts/cuerpos en logging de agente/claims |
| Historial de clientes reales anonimizado | BLOCKED | No hay anonimizador general; sólo datos sintéticos autorizados, sin proveedores reales |
| Control humano y resultados tardíos | PASS local | `Stale_snapshot_handoff_revocation_and_expired_questions_cannot_validate_a_proposal` |
| Replay secuencial/concurrente/reinicio | FAIL antes → PASS después | Dos rojos nuevos, más atomicidad/reinicio del SDK con fake |
| Caída de almacén/ACK incierto | PASS local | `Audit_failures_do_not_duplicate_model_work_or_reopen_uncertain_claims`, `Failed_or_uncertain_claims_never_reopen_a_marker_that_was_committed` |
| Fail-closed de proveedor y texto no publicable | PASS local | `Tools_and_model_failures_have_a_bounded_safe_exit_without_effects`; Publishable=false incondicional |
| Allowlist apagada por defecto | PASS por código/test | Enabled=false, workspace explícito; ningún config real editado |
| Redis/Firestore/PostgreSQL reales | NOT_RUN | SDK Firestore con transporte InMemoryFirestore, no servicios/emuladores |
| C/D/E completos, NLU real y piloto | BLOCKED | Regla de parada del §0, autorización externa y P0 pendiente |

## Cambio de prueba de presupuesto, explícito

La prueba anterior de seis evaluaciones con un mismo ID contradice el nuevo requisito de deduplicación. Se versionó a `Distinct_shadow_messages_charge_their_own_budget_without_business_effects`: conserva seis concesiones, séptima denegación, contador6 y cero efectos; ahora usa **IDs distintos**. Los duplicados se cubren por los dos tests rojos/verdes específicos. No se relajaron aserciones, no se tocaron corpus/evaluadores congelados y se conserva el TRX anterior.

Pruebas adicionales directas de claims: ganador único, persistencia tras recrear servicio/dos días, cambio de texto/cliente/conversación, aislamiento de workspace, pérdida de ACK, cancelación, límites de longitud, reloj UTC y esquema desconocido. Conteos finales y comandos en ITERACION_2_CIERRE.
