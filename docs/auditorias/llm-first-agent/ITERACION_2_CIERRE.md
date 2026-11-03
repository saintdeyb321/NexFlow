# Iteración 2 — Cierre de auditoría y parada obligatoria

## Decisión

**NO_GO_PILOT. C/D/E: BLOCKED, no iniciadas.** La auditoría confirmó omisión de deduplicación en shadow. El §0 de `docs/NEXFLOW_LLM_FIRST_ITERACION_2_FASES_C_D_E_2026-11-03.md` ordena corregirla, probarla y detener el avance. Se cumple esa parada; las regresiones del arreglo no equivalen a completar Fase E.

Rama feature/llm-first-agent y HEAD/main5f5ff850e31df3fa73cb2c7f02f65ba6081173c7 preservados. Refs remotas consultadas: ambas mismo SHA. Índice vacío, sin commits/push/merge/migraciones/despliegue. Ningún acceso a Oracle ni a servicios productivos.

## Archivos de este arreglo

- Modificados conservando Iteración1: `Agent/AgentContracts.cs`, `Agent/LlmFirstShadowAgent.cs`, `NexFlow.Tests/Concierge/LlmFirstShadowTests.cs`, DI de Infrastructure.
- Nuevos: `NexFlow.Infrastructure/Persistence/Firestore/FirestoreAgentEvaluationClaims.cs`, `NexFlow.Tests/AgentEvaluationClaimsTests.cs`.
- Tres informes ITERACION_2 y actualización de ESTADO. Documentos del propietario conservados sin editar.
- DI de Application y AgentReadTools de Iteración1 siguen como estaban. Sin cambios de frontend, motores, DTO/almacén de contexto, appsettings, secretos, H06/G8 ni corpus históricos.

No se cambia un schema PostgreSQL ni se crea otro estado conversacional. Sólo metadatos de admisión IA en la infraestructura Firestore ya utilizada, ejercitados exclusivamente con el SDK fake. No se aplicaron escrituras en servicios reales.

## Pruebas

Evidencia nueva: `backend/TestResults/LlmFirstIteration2_20261103/`, sin alterar `backend/TestResults/LlmFirst20261103/`.

| Artefacto | Ejecutadas | PASS | FAIL | Omitidas |
|---|---:|---:|---:|---:|
| iteration2-audit-red.trx | 2 | 0 | 2 | 0 |
| iteration2-audit-first-green.trx | 41 | 41 | 0 | 0 |
| iteration2-audit-reviewed.trx | 53 | 53 | 0 | 0 |
| iteration2-audit-final.trx | 61 | 61 | 0 | 0 |
| current-audit-fix.trx | 1997 | 1997 | 0 | 0 |
| historical-audit-fix.trx | 130 | 101 | 29 esperados | 0 |

La prueba anterior de consumo se versiona con IDs distintos, manteniendo todas sus aserciones; nuevas pruebas exigen exactamente un intento en replays. Ninguna aserción histórica ni corpus fue alterado.

### Requirement | Evidence

| Requirement | Evidence |
|---|---|
| Replay secuencial y concurrente | `Audit_replay_after_recreating_the_agent_does_not_call_the_model_again`; `Audit_concurrent_duplicate_is_rejected_while_the_first_model_call_is_running` |
| Atomicidad y persistencia | `Atomic_claim_has_one_winner_and_survives_service_restart_and_budget_window_changes` |
| Vinculación de identidad/tenant | `A_source_message_cannot_be_rebound_to_a_different_request`; `Identical_provider_message_ids_are_isolated_by_workspace` |
| Timeout/almacén/ACK incierto | `Audit_failures_do_not_duplicate_model_work_or_reopen_uncertain_claims`; `Failed_or_uncertain_claims_never_reopen_a_marker_that_was_committed` |
| Configuración inválida/cancelación | `Identity_bounds_accept_the_limit_and_reject_the_next_character_before_storage`; `Missing_workspace_and_unqualified_clock_cannot_create_claims`; `Caller_cancellation_never_creates_a_claim`; `Incomplete_or_unknown_marker_schema_is_never_treated_as_a_new_claim` |

Comandos dirigidos y de cierre:

```powershell
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --filter "FullyQualifiedName~LlmFirstShadowTests.Audit_" --logger "trx;LogFileName=iteration2-audit-red.trx" --results-directory backend/TestResults/LlmFirstIteration2_20261103
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --filter "FullyQualifiedName~LlmFirstShadowTests|FullyQualifiedName~AgentEvaluationClaimsTests" --logger "trx;LogFileName=iteration2-audit-final.trx" --results-directory backend/TestResults/LlmFirstIteration2_20261103
python -B .github/workflows/g8_certification.py run current --trx backend/TestResults/LlmFirstIteration2_20261103/current-audit-fix.trx
python -B .github/workflows/g8_certification.py run historical --trx backend/TestResults/LlmFirstIteration2_20261103/historical-audit-fix.trx
dotnet build backend/backend.slnx --no-restore
git diff --check
```

Consolidada: PASS, validador exit0, test exit0. Histórico: EXPECTED_HISTORICAL_INCOMPATIBILITIES, validador exit0 aunque dotnet retorna1 por los29 rojos autorizados; identidades/motivos y cuatro hashes congelados intactos. H06 exacto `H06/4/A: ReservationEffects`. No se toleró ningún fallo vigente ni omisión. El filtro oficial separa los históricos y excluye categorías de integración real; no sustituye la certificación externa.

Build final `dotnet build backend/backend.slnx --no-restore`: exit0,0 errores/0 advertencias,3.00s. En una compilación de tests previa reaparecieron tres warnings preexistentes: dos CS8981 de migración refactor y CS8602 de FirestoreCatalogRepository:228; no se tocaron. `git diff --check`: PASS; índice vacío. Frontend build/lint NOT_RUN, sin cambios frontend. Sin pruebas remotas de GitHub ni modelos reales. Los conteos corresponden únicamente a ejecución local con fakes.

## Estado Git final completo

HEAD/main local y refs remotas observadas:5f5ff850e31df3fa73cb2c7f02f65ba6081173c7; rama feature/llm-first-agent. No hay staged. La mayor parte del árbol sigue siendo el trabajo sin publicar de Iteración1; no se ha perdido.

```text
 M backend/NexFlow.Application/DependencyInjection/DependencyInjection.cs
 M backend/NexFlow.Infrastructure/Persistence/DependencyInjection/DependencyInjection.cs
?? backend/NexFlow.Application/Features/Automation/Agent/AgentContracts.cs
?? backend/NexFlow.Application/Features/Automation/Agent/AgentReadTools.cs
?? backend/NexFlow.Application/Features/Automation/Agent/LlmFirstShadowAgent.cs
?? backend/NexFlow.Infrastructure/Persistence/Firestore/FirestoreAgentEvaluationClaims.cs
?? backend/NexFlow.Tests/AgentEvaluationClaimsTests.cs
?? backend/NexFlow.Tests/Concierge/LlmFirstShadowTests.cs
?? docs/NEXFLOW_LLM_FIRST_ITERACION_2_FASES_C_D_E_2026-11-03.md
?? docs/NEXFLOW_PIVOTE_AGENTE_CONVERSACIONAL_LLM_FIRST_2026-11-03.md
?? docs/auditorias/llm-first-agent/ESTADO.md
?? docs/auditorias/llm-first-agent/ITERACION_2_AUDITORIA.md
?? docs/auditorias/llm-first-agent/ITERACION_2_CIERRE.md
?? docs/auditorias/llm-first-agent/ITERACION_2_EVALUACION.md
```

## Limitaciones

La marca de admisión no guarda resultado y no se libera automáticamente: un fallo posterior al claim puede dejar el mensaje sin propuesta recuperable; un replay se informa como ya iniciado, nunca como éxito. La recuperación exactly-once de respuesta y el tratamiento de estados terminales deben diseñarse antes de una vía real de consumidor. No borrar marcadores para forzar reintentos.

Retención/costo de estos metadatos pseudonimizados, privacidad del historial real, validación independiente de texto libre y autoridad durable del diálogo siguen pendientes. Shadow permanece apagado/no publicable; P0 histórico pendiente, cuotas efectivas UNKNOWN. No se autoriza piloto ni confirmación real de reservas.
