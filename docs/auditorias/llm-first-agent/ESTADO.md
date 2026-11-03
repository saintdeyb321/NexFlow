# LLM-first — Estado de continuidad

## Estado vigente — Iteración 2 detenida por el gate de auditoría

Rama feature/llm-first-agent. HEAD/main local y ambas refs remotas verificadas: `5f5ff850e31df3fa73cb2c7f02f65ba6081173c7`. Trabajo local de Iteración1 y documentos del propietario preservados; índice vacío. No iniciar C/D/E ni una fase F automáticamente.

Se leyeron especificaciones y todos los archivos del shadow, incluyendo no rastreados, contratos, DI y pruebas. Los siete hashes de archivos de Iteración1 coincidían antes de editar; se verificaron XML y SHA256 de sus TRX39/39,1975/1975 y130/101/29. Son resultados locales, no CI remoto. Los artefactos anteriores siguen intactos.

**P1 reproducido:** repetir el mismo workspace/mensaje ejecutaba otra vez el modelo, incluso concurrentemente o recreando el servicio. El GUID aleatorio de cada evaluación evadía una deduplicación propia. `iteration2-audit-red.trx`:2/0/2/0, duplicados Accepted=true. No hubo duplicación comercial ni outbound; P0 histórico sigue pendiente.

Se aplica la regla expresa del §0 de `docs/NEXFLOW_LLM_FIRST_ITERACION_2_FASES_C_D_E_2026-11-03.md`: ante omisión de deduplicación, corregir/probar y **detener el avance**. Por tanto C/D/E no se implementaron; no es un cierre conversacional ni aceptación de piloto.

**Corrección:** IAgentEvaluationClaims + FirestoreAgentEvaluationClaims, admisión atómica exists=false por workspace/sourceMessageId, con huella del texto/cliente/conversación. Sólo metadatos pseudonimizados bajo el presupuesto IA existente; no contexto/borrador paralelo, motores, migración ni outbound. Duplicate/Conflict se rechazan antes de historial/presupuesto/modelo, con0 llamadas. No se libera el marcador ante timeout, pérdida de ACK o cambio de día. El servicio no falla abierto si el almacén está caído.

Tradeoff explícito: at-most-once sin guardar resultado; una caída después del claim puede impedir recuperar una propuesta. No se presenta el replay como éxito. Recuperación exactly-once de respuesta, retención/limpieza/costo y cualquier vía de consumidor requieren revisión antes de C. Shadow sigue apagado por defecto y todo texto no publicable.

La antigua prueba que repetía seis veces el mismo ID se versionó a IDs distintos conservando las aserciones de cuota/efectos. Replays ahora tienen casos específicos secuenciales/concurrentes. No se tocaron aserciones/corpus históricos.

| Evidencia nueva en backend/TestResults/LlmFirstIteration2_20261103/ | Resultado |
|---|---|
| iteration2-audit-red.trx | 2 ejecutadas,0 PASS/2 FAIL/0 omitidas, preservado |
| iteration2-audit-first-green.trx | 41/41 PASS |
| iteration2-audit-reviewed.trx | 53/53 PASS |
| iteration2-audit-final.trx | 61/61 PASS |
| current-audit-fix.trx / .g8.json | 1997/1997,0 fallos/omisiones; validador PASS |
| historical-audit-fix.trx / .g8.json | 130/101/29,0 omisiones; validador exit0 exige motivos/hashes exactos; H06=H06/4/A: ReservationEffects |
| Backend build --no-restore | PASS,0 errores/0 advertencias;3.00s |
| git diff --check | PASS; main intacta, sin staged |

Todos los tests usan modelos simulados/SDK Firestore con fake; no equivalen a almacenes reales, comprensión de Groq/Gemini o WhatsApp. LLM externo0 HTTP, WhatsApp0 envíos, Oracle0 operaciones. No commits/push/migraciones/despliegues. Advertencias antiguas de una compilación previa documentadas en ITERACION_2_CIERRE.

Archivos de esta intervención: AgentContracts.cs, LlmFirstShadowAgent.cs, DI Infrastructure y LlmFirstShadowTests.cs; nuevos FirestoreAgentEvaluationClaims.cs y AgentEvaluationClaimsTests.cs. DI Application/AgentReadTools originales no alterados. Tres informes ITERACION_2 añadidos; estado Git completo y comandos en ITERACION_2_CIERRE. No cambios frontend ni secretos/configuraciones reales.

**NO_GO_PILOT; C/D/E BLOCKED.** Siguiente acción: revisar el arreglo de replay y este gate antes de autorizar reanudación de C. No preparar corpus supuestamente ciego desde el mismo implementador ni declarar aceptación NLU real. P0 histórico, causa ProviderUnavailable, cuotas efectivas y evaluación humana siguen pendientes. No ejecutar proveedores sin autorización específica.

## Registro histórico — Iteración 1 (conservado)

Base/main local y remoto verificados: `5f5ff850e31df3fa73cb2c7f02f65ba6081173c7`. Rama creada: `feature/llm-first-agent`; main no movida. Único archivo previo sin seguimiento: especificación `docs/NEXFLOW_PIVOTE_AGENTE_CONVERSACIONAL_LLM_FIRST_2026-11-03.md`, conservada sin editar.

## Alcance y estado

Fase A reproducida y prototipo mínimo de Fase B implementado. **Detener para revisión, no iniciar C/D.** Pruebas shadow dirigidas:39/39 PASS; gate vigente1975/1975, histórico esperado130/101/29 y build PASS. Las propuestas no se envían a WhatsApp ni se aplican al estado del negocio. P0 histórico sigue pendiente. No hay una nueva candidata publicada ni CI remoto de este diff.

Se aplicaron skills systematic-debugging/code-testing. El agente especializado test-engineer y catálogo de extensiones no están disponibles; investigación/plan/revisión se hicieron inline, con scratch fuera del repositorio. No se sustituyó por un subagente genérico.

## Fase A: causas y evidencia

| Componente | Evidencia | Conclusión |
|---|---|---|
| AiResponseOrchestrator | `RespondCoreAsync`, líneas101–111 | Recovery se ejecuta antes de PrepareAsync y su Response puede terminar el turno sin interpretación semántica |
| ConversationRecoveryService | `RecoverAsync`, líneas96–125 | RESERVATION_SCOPE detecta Draft, borra la referencia y devuelve únicamente Intent=Reservation; pierde el acto que motivó la aclaración |
| ReservationOperationClassifier | `ReferenceScope`, líneas9–17 | «preparando» identifica Draft correctamente; no es un fallo de reconocimiento de esa palabra |
| DialoguePolicy | `Classify`, línea51 | En reserva, el fallback es RequestedData; una consulta abierta puede convertirse en continuación del formulario |
| TurnUnderstandingPipeline | `PrepareCoreAsync`, rutas locales previas al intérprete | Recovery/slots/resume pueden decidir antes de IA. En el incidente reproducido «Quiero hacerme una consulta» acaba solicitando sede |
| CommerceTurnResolver | `ResolveAsync`, líneas58–110 | SearchTerm literal sin entidad determinada ni sugerencia autorizada termina en aclaración genérica; validar una operación no equivale a orientar una necesidad |
| AiInterpreter | `InterpretCoreAsync`, líneas291–310 | Diferencia timeout/rate-limit/JSON/configuración. **ProviderUnavailable real del incidente: UNKNOWN** sin trazas; se simula JSON inválido sólo como ejemplo diagnóstico |
| ResponseComposer | `ComposeAsync`, líneas44–92 | La opción actual no es reescritura conversacional general: agrupación verificada o apertura local. No se activa ni se modifica |

Rutas: `backend/NexFlow.Application/Features/Automation/` para orquestación/diálogo; intérprete en `Features/AI/Interpretation`. Las fuentes originales no se alteraron. `Legacy_scope_reference_loses_the_cancel_act_and_resumes_location_collection` prueba el defecto antiguo y ausencia de reservas/pedidos, sin convertirlo en consentimiento.

No se inspeccionó Oracle ni Docker. El SHA realmente desplegado y las trazas del segundo mensaje quedan pendientes de operador autorizado. No atribuir un JSON inválido simulado a Groq/Gemini reales.

## Prototipo implementado

Entrada interna `IConversationalAgent.EvaluateAsync(AgentTurnRequest)` → gate de workspace sintético/humano/replay/UnknownDelivery/licencia → snapshot autorizado → AiRouter → tools de lectura opcionales → propuesta tipada → revalidación de estado/pregunta/licencia → resultado **shadow no publicable**.

- `AgentShadow.Enabled=false` por defecto y un único `SyntheticWorkspaceId` explícito. Configuración sólo vinculada como contrato; no se editó appsettings ni se habilitó ningún negocio. El ID debe corresponder a datos realmente sintéticos; el flag no convierte datos reales en sintéticos.
- No hay hook en inbound, endpoint HTTP, worker, envío ni aplicación automática de resultados. La ruta se invoca desde el fixture aislado. Habilitar un setting no reemplaza el bot de WhatsApp. Esto mantiene la ruta antigua intacta mientras se evalúa la arquitectura.
- `AgentTurnContext` es una proyección inmutable de ConversationContextDto/ConversationQuestion existentes; no otro almacén. Incluye slots de preparación (no disponibilidad verificada), pregunta válida/TTL, acción pendiente y seis turnos como máximo,400 caracteres por turno. Omite ClientKey/teléfono del request, nombre del paciente y resumen completo del estado. Historial sólo sintético en estas pruebas: no es un anonimizador general de texto privado.
- El agente recibe todos los mensajes admitidos. No llama a DialoguePolicy ni a Recovery.RecoverAsync. Usa Recovery únicamente para presupuesto, no para decidir intención. La IA propone acto y herramientas; backend valida estructura, referencias, licencias y procedencia, sin confiar en el significado como consentimiento.
- `AgentReadTools` adapta IBusinessReadTools existente: Services, Products, Locations, Profile y Hours. Máximo3 lecturas, resultados autorizados y permisos comprobados antes/después por la fachada existente. Rechaza otro workspace, fuente o hechos sensibles. No existen tools para confirmar, cancelar, escribir, cobrar ni consultar información privada de citas.
- Máximo2 llamadas lógicas a AiRouter: una decisión; otra sólo tras solicitar lecturas. Sin rondas de herramientas adicionales. Deadline20s, entrada24000 caracteres, salida8000, propuesta1800; límites de lectura internos existentes. El router/proveedores mantienen fallback y admisión global existentes, sin cambiar modelos ni cuotas.
- Cada evaluación manual tiene presupuesto propio, sin reutilizar el recibo idempotente de otra evaluación del mismo mensaje. Repetirla consume presupuesto acotado; no repite ningún efecto comercial. Las únicas escrituras del prototipo son contadores de presupuesto existentes en el almacenamiento **simulado**; no guarda/modifica el contexto ni una reserva.
- `ProposeDiscardDraft` exige reserva activa, pregunta vigente ligada al contexto y PendingAction RESERVATION_SCOPE/CANCEL. Es propuesta de pedir descarte, nunca autorización ni ejecución. Un cambio de versión, pregunta, permisos, modo humano o entrega incierta invalida el resultado.
- `Accepted=true` significa propuesta estructuralmente admisible, **no redacción factual certificada**. `Publishable=false` y `ResponseValidation=UnverifiedShadowProposal` son incondicionales. CandidateResponse no tiene validador independiente suficiente para publicarse; se conserva sólo para comparación en TRX. Ningún LLM valida a otro LLM.

## Comparación del incidente (modelos simulados)

Las seis primeras entradas son las aportadas; se añaden «Sí» y consulta de servicios para observar continuidad sin transacción. El fixture usa negocio/catálogo sintéticos. Las respuestas IA son JSON preparados: se prueba integración, no comprensión real.

| Entrada | Ruta antigua observada | Propuesta shadow observada |
|---|---|---|
| Hola quiero sacarme el diente | No identifiqué un producto o servicio inequívoco… | Ofrece revisar servicios sin asumir tratamiento; Information |
| Quiero extraerme el diente | Se fuerza JSON inválido para observar ProviderUnavailable; no se conoce causa real | Orientación informativa; no diagnóstico |
| Quiero reservar | Pide sede | PrepareReservation, sin crear ni confirmar |
| Quiero hacerme una consulta | Insiste en sede | Information; preserva la preparación |
| Quiero cancelar la reserva | Pide alcance | ClarifyCancellationScope |
| A la que estamos preparando | Pierde cancelación y vuelve a sede | ProposeDiscardDraft con QuestionId exacto, pide decisión de descarte |
| Sí | Solicita elegir sede | Propuesta de aclaración; no ejecuta descarte ni operación |
| ¿Qué servicios ofrecen? | Catálogo y retorno al formulario | Propuesta informativa; ninguna mutación |

El shadow lee las mismas instantáneas de la ruta antigua **antes** de cada turno; no mantiene una conversación autónoma persistente. La propiedad durable de nuevas preguntas y la aplicación de propuestas pertenecen a una Fase C autorizada, no se simulan como implementadas.

## Pruebas y medición

Todas bajo `NexFlow.Tests.Concierge.LlmFirstShadowTests`. Evidencias ignoradas por Git en `backend/TestResults/LlmFirst20261103/`.

| Requirement | Evidence |
|---|---|
| Causa antigua y ocho turnos | `Legacy_scope_reference_loses_the_cancel_act_and_resumes_location_collection`; `Eight_turn_incident_compares_old_and_shadow_routes_without_real_nlu_or_business_effects` |
| Cancelación ligada a pregunta e interrupción sin pérdida | `Shadow_keeps_cancel_reference_and_information_without_mutating_the_durable_draft` |
| Necesidad no literal y servicio autorizado | `A_nonliteral_need_can_read_real_services_but_never_selects_or_confirms_a_treatment` |
| Off por defecto/tenant/humano/replay/entrega incierta | `Disabled_foreign_human_replay_and_unknown_delivery_never_reach_the_model` |
| JSON/herramienta/facts/pregunta/workspace/commit falsos | `Untrusted_json_tools_facts_questions_and_commit_commands_are_rejected` |
| CAS/pregunta caducada/revocación/toma humana | `Stale_snapshot_handoff_revocation_and_expired_questions_cannot_validate_a_proposal` |
| Cinco tipos de lectura autorizados | `Authorized_read_tools_return_only_the_requested_business_source` |
| Módulos revocados, sin consultar repositorio | `Read_tools_recheck_workspace_licenses_before_consulting_restricted_repositories` |
| Revocación durante lectura | `Revocation_during_a_read_removes_facts_before_the_agent_receives_them` |
| Procedencia y privacidad de facts | `Read_tool_provenance_cannot_be_substituted_by_another_workspace_or_private_data` |
| Loop, exceso de tools, timeout y cancelación | `Tools_and_model_failures_have_a_bounded_safe_exit_without_effects` |
| Repeticiones sin concesión gratuita ni efectos | `Repeated_shadow_evaluations_charge_their_own_budget_without_business_effects` |
| Snapshot sin nombre/resumen ni módulos ajenos | `Context_snapshot_uses_the_current_question_without_patient_identity_or_unrelated_modules` |

Rojo inicial `iteration1-red.trx`:21 ejecutadas,19 PASS/2 FAIL; dos contratos positivos del prototipo no implementado, junto a caracterización legacy. No se presenta ese rojo como prueba de fallo de proveedor real. Después hubo un error de compilación CS0103 por namespace de MessageKind; corregido. `iteration1-prototype.trx`:21/20/1 por aserción de texto JSON escapado, corregida parseando JSON. `iteration1-expanded.trx`:34/32/2 por comparar LastUpdated de contextos vacíos efímeros; fixtures ahora persisten primero el contexto sintético. No se debilitaron aserciones del negocio ni tests anteriores.

`iteration1-stable.trx`:36/36. `iteration1-reviewed.trx`:39/39,0 fallos/omisiones, incluyendo aserciones de motivos exactos para rechazos. En los ocho turnos hay8 llamadas lógicas,9217 caracteres de payload de entrada acumulados (excluye el contrato system repetido) y0 HTTP a proveedores. La consulta con herramienta separada usa2 llamadas. Tokens/fallbacks/latencia real de proveedor UNKNOWN/NOT_RUN. El resultado incluye duración local medida y llamadas; AiTelemetry existente conserva telemetría HTTP/modelo/uso si un proveedor se ejecuta en el futuro; nuevos contadores sólo usan outcomes/failure kinds, sin cuerpos ni PII.

### Cierre verificable

| Validación | Ejecutadas | PASS | FAIL | Omitidas | Resultado |
|---|---:|---:|---:|---:|---|
| Shadow dirigido, iteration1-reviewed.trx | 39 | 39 | 0 | 0 | PASS |
| Gate current, current-iteration1.trx | 1975 | 1975 | 0 | 0 | PASS, validador exit0 |
| G8 histórico, historical-iteration1.trx | 130 | 101 | 29 | 0 | EXPECTED_HISTORICAL_INCOMPATIBILITIES, validador exit0 |

Los29 rojos históricos no se vuelven verdes artificialmente: identidades/motivos exactos y hashes de los cuatro corpus/evaluadores congelados comprobados por el validador oficial. H06 sigue `H06/4/A: ReservationEffects`. El filtro vigente excluye integraciones reales y separa las dos clases históricas; no afirmar certificación de PostgreSQL/Firestore/Redis reales con este gate.

```powershell
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --filter "FullyQualifiedName~LlmFirstShadowTests" --logger "trx;LogFileName=iteration1-reviewed.trx" --results-directory backend/TestResults/LlmFirst20261103
python -B .github/workflows/g8_certification.py run current --trx backend/TestResults/LlmFirst20261103/current-iteration1.trx
python -B .github/workflows/g8_certification.py run historical --trx backend/TestResults/LlmFirst20261103/historical-iteration1.trx
dotnet build backend/backend.slnx --no-restore
git diff --check
```

Build final incremental: exit0,0 errores/0 advertencias,3.51s. La compilación de tests previa mostró tres advertencias preexistentes: dos CS8981 de la migración refactor y CS8602 de FirestoreCatalogRepository:228; sin cambios en esos archivos. Ninguna advertencia nueva pendiente. Frontend build/lint NOT_RUN: sin cambios frontend. Integraciones externas/proveedores/WhatsApp NOT_RUN. Todos los TRX rojos se conservan; no se repitió la consolidada tras pasar.

HEAD y main finales siguen en `5f5ff850e31df3fa73cb2c7f02f65ba6081173c7`, rama activa feature/llm-first-agent, índice vacío y diff check PASS. Cambios de código: sólo los seis archivos listados (dos DI, tres clases/contratos y un test); más este checkpoint nuevo. Especificación original sin seguimiento conservada. No se movió main ni se modificaron sus refs/commits.

## Archivos de esta iteración

- Nuevos: `backend/NexFlow.Application/Features/Automation/Agent/{AgentContracts,AgentReadTools,LlmFirstShadowAgent}.cs`.
- Nuevo: `backend/NexFlow.Tests/Concierge/LlmFirstShadowTests.cs`.
- Modificados: `backend/NexFlow.Application/DependencyInjection/DependencyInjection.cs` (dos registros), `backend/NexFlow.Infrastructure/Persistence/DependencyInjection/DependencyInjection.cs` (binding de opciones apagadas).
- Este checkpoint. La especificación previa del propietario continúa sin seguimiento y sin modificaciones.

Frontend, motores, contexto durable/DTO existente, guards de H06/G8, corpus/evaluadores, proveedores, PostgreSQL, Firestore, Redis, Evolution, migraciones y defaults anteriores sin cambios. No hay commit, push, merge ni despliegue.

## Límites y siguiente gate

GO sólo para revisión de este prototipo shadow sintético. NO-GO para activación DEMO/WhatsApp, NLU real, publicación de texto libre, confirmaciones o despliegue. P0 histórico pendiente. Sin prueba ciega ni acceso a trazas sanitizadas no hay causa determinada de ProviderUnavailable ni métricas de comprensión real.

Antes de C: aprobar contrato de respuesta factual independientemente verificable, retención/privacidad de historial real, propiedad durable de pregunta/acto en el mismo CAS, aplicación segura de preparación/descarte y replay de evaluaciones. Las lecturas se limitan a5 resultados por consulta con la fachada existente; el prototipo no implementa navegación exhaustiva ni debe usarse para afirmar que son todos los servicios. Disponibilidad, gestión de citas registradas y cualquier escritura siguen en motores/controles existentes. Modelos reales requieren autorización, cuota/presupuesto verificados y datos sintéticos. No se ejecuta ninguna de estas tareas en esta iteración.
