# Informe final — Agente conversacional NexFlow

Fecha: 02/11/2026. Base/main **`7c4782b35bd811cc26a502f2ae2b013e554f0238`**, sin commit nuevo. Árbol inicial: dos planes del usuario sin seguimiento; índice vacío. Ambos planes preservados. No se modificaron configuración ni datos reales.

## Dictamen

**APTO PARA REVISIÓN LOCAL DEL DIFF**, con límites explícitos. Los defectos reproducidos de catálogo, preparación de reserva y referencia vigente tienen pruebas verdes, tanto legacy como CTA donde corresponde. **GATE GLOBAL VIGENTE PENDIENTE DE NUEVA EJECUCIÓN CONSOLIDADA**: la única suite completa ejecutada dio 1847 PASS / 3 FAIL; los tres fallos se corrigieron y la batería afectada posterior pasó 253/253. No se manipula el TRX completo ni se atribuye un PASS global que no ocurrió.

**NO_GO_PROVIDER_REAL, NO_GO_RESERVATIONS_REAL y NO_GO_DEPLOY**. P0 histórico de reserva inesperada sigue NO DETERMINADO. No hubo proveedores reales, WhatsApp, Oracle, migraciones, commit, push ni despliegue.

## Causas y cambios demostrados

1. El matcher comercial no combinaba errores acotados con sufijos omitidos del nombre autorizado. Se añadió una comparación comercial de subsecuencia, máximo dos ediciones de un carácter y sin resolver colisiones. Los calificadores cortos deben coincidir; «con» deja de eliminarse como ruido. No se modificó AiEvidenceValidator.
2. La interpretación específica con SearchTerm/Stock caía fuera del OfferingQuery y perdía la advertencia de inventario. Ahora produce plan determinista con fragmento literal fundamentado, sede autorizada si fue solicitada, producto/precio vigente y aviso de stock no verificable. No hay contrato de inventario y no se agregó uno.
3. StartOperation con evidencia no preparaba reserva si faltaban todos los slots. Ahora puede preparar un borrador vacío y preguntar la sede. No genera consentimiento ni escritura de negocio.
4. El resolver local reutilizaba una referencia de catálogo sin TTL/intención de pregunta. Ahora la referencia depende de CurrentQuestion y su huella/ámbito; una referencia no fundamentada provoca aclaración, sin ampliar arbitrariamente el catálogo. UseOfferingReference tiene prioridad sobre SearchTerm redundante del JSON.
5. Compatibilidad del parche: el tipo del PDF amplio todavía puede orientarse mediante un sobre legacy sin CurrentQuestion si sus IDs siguen autorizados/activos; no sirve para seleccionar entidad ni consentir. Una aclaración vigente del pedido conserva la consulta factual de sus propias líneas; la referencia para editar sigue más restringida.

Producción: **tres archivos**, `CommerceTurnResolver.cs`, `EntityMentionResolvers.cs`, `TurnUnderstandingPipeline.cs`. Pruebas: `AgentStabilizationTests.cs` nuevo (27 casos) y visibilidad internal del helper existente en `WhatsAppConversationalRepairTests.cs`; ninguna aserción anterior modificada. Lista completa y motivos: CAMBIOS.md. Hallazgos con métodos/líneas: HALLAZGOS_Y_CAUSAS.md.

## Pruebas reales ejecutadas

Todos los resultados son locales con fixtures controlados. TRX y JSON de validador: `backend/TestResults/AgentStabilization20261102/` (archivos ignorados, no evidencia remota).

| Ejecución | Ejecutadas | PASS | FAIL | Omitidas | Resultado |
|---|---:|---:|---:|---:|---|
| baseline.trx, antes de producción | 42 | 42 | 0 | 0 | PASS |
| reproduction.trx, pruebas nuevas antes del parche | 11 | 1 | 10 | 0 | FAIL esperado; causas verificadas |
| catalog-after.trx | 5 | 5 | 0 | 0 | PASS primer lote |
| routing-after.trx | 15 | 12 | 3 | 0 | FAIL; un calificador y dos supuestos de orden del fixture nuevo |
| transcript-after.trx | 15 | 15 | 0 | 0 | PASS |
| guards-before.trx | 11 | 5 | 6 | 0 | FAIL; ámbito/referencias y dos supuestos de pregunta del fixture nuevo |
| regression.trx | 148 | 146 | 2 | 0 | FAIL; se corrigió el fixture de pregunta realmente reemplazada |
| directed-final.trx | 339 | 339 | 0 | 0 | PASS |
| reference-precedence-before.trx | 5 | 5 | 0 | 0 | PASS local; no llegaba a semántica |
| reference-semantic-before.trx | 5 | 4 | 1 | 0 | FAIL que reprodujo la precedencia del JSON |
| directed-stable.trx | 340 | 340 | 0 | 0 | PASS |
| current.trx, única suite vigente completa | 1850 | 1847 | 3 | 0 | FAIL conservado; gate NO-GO de esa ejecución |
| compatibility-final.trx, posterior al último parche | 253 | 253 | 0 | 0 | PASS, incluidos los tres fallos completos |
| historical.trx, batería separada | 130 | 101 | 29 | 0 | Contrato **130/101/29 EXACTO**, validador exit 0 |

El histórico conserva exactamente casos y motivos autorizados (incluido **H06/4/A: ReservationEffects**), y los cuatro SHA-256 congelados del corpus/evaluadores. Su dotnet retorna 1 por las 29 incompatibilidades previstas; el validador devuelve EXPECTED_HISTORICAL_INCOMPATIBILITIES. No se tocaron corpus ni validadores. No se suman baterías solapadas como pruebas únicas.

Build inicial: PASS, 0 errores y tres advertencias existentes CS8981 (dos migraciones antiguas) y CS8602 (FirestoreCatalogRepository:228). **Build final incremental: PASS, 0 errores / 0 advertencias, 3.97 s**. La advertencia antigua no fue corregida ni ocultada; no se garantiza un clean-build sin ella. git diff --check PASS; sin cambios en staging.

### Comandos reproducibles

```powershell
dotnet build backend/backend.slnx --no-restore
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --filter "FullyQualifiedName~AgentStabilizationTests" --logger "trx;LogFileName=reproduction.trx" --results-directory backend/TestResults/AgentStabilization20261102
python -B .github/workflows/g8_certification.py run current --trx backend/TestResults/AgentStabilization20261102/current.trx
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --filter "FullyQualifiedName~AgentStabilizationTests|FullyQualifiedName~ReleaseClosureTests|FullyQualifiedName~CommerceConversationTests|FullyQualifiedName~CommerceSafetyTests|FullyQualifiedName~CommerceHardeningTests|FullyQualifiedName~RuntimeOrderRequestTests|FullyQualifiedName~CtaContinuityTests|FullyQualifiedName~CtaF4LocalTests|FullyQualifiedName~WhatsAppConversationalRepairTests" --logger "trx;LogFileName=compatibility-final.trx" --results-directory backend/TestResults/AgentStabilization20261102
python -B .github/workflows/g8_certification.py run historical --trx backend/TestResults/AgentStabilization20261102/historical.trx
git diff --check
```

Son los comandos ejecutados, no una invitación a sobreescribir la evidencia actual. Al repetir, usar otro nombre/directorio TRX. El baseline exacto y ejecuciones intermedias se detallan en BASELINE.md. El script G8 usa --no-build: compilar las pruebas mediante dotnet test dirigido antes si no existen binarios actuales.

## Requisito y evidencia decisiva

| Requisito | Evidencia | Estado |
|---|---|---|
| Erratas y catálogo/stock honesto | AgentStabilizationTests.Catalog_corrections_use_verified_products_and_never_claim_physical_stock | PASS local |
| Entidades ambiguas, inventadas, tenant/sede y revocación | AgentStabilizationTests.Specific_semantic_queries_are_grounded_in_the_current_authorized_catalog_scope | PASS local |
| Catálogo → nueva reserva | AgentStabilizationTests.A_backed_new_reservation_intent_leaves_the_catalog_without_old_options_or_consent | PASS legacy/CTA |
| Ordinal, fecha/hora Lima y consentimiento vigente | AgentStabilizationTests.A_to_N_transcript_uses_current_ordinals_Lima_dates_and_only_current_explicit_consent | PASS legacy/CTA |
| Pregunta reemplazada y referencias caducas | AgentStabilizationTests.An_ordinal_from_a_replaced_question_cannot_select_a_previous_service; AgentStabilizationTests.Product_references_require_the_current_question_workspace_and_ttl | PASS local |
| Borradores y revisión independiente | ContextualFocusTests.Independent_drafts_need_new_reviews_and_only_one_order_is_committed; CtaContinuityTests.Natural_absolute_and_incremental_changes_after_resume_preserve_the_other_draft | PASS local |
| CAS, idempotencia y entrega incierta | CommerceSafetyTests.Concurrent_commits_have_one_cas_winner_and_one_business_record; ContextualFocusTests.Repeated_source_or_unknown_delivery_does_not_repeat_transition_or_interpretation | PASS simulado |
| Límite de IA intacto | AssistantDurabilityTests.Atomic_ai_budget_enforces_both_limits_with_concurrency_duplicates_and_next_window | PASS simulado |
| G8/H06 históricos intactos | historical.g8.json, hashes/casos/motivos verificados | PASS contrato histórico, no 130 tests verdes |
| Gate actual consolidado del diff final | current.trx rojo + 253/253 afectados posteriores | PENDING nueva ejecución consolidada |
| Stock positivo/cero real | Sin contrato de inventario | BLOCKED_NO_INVENTORY |
| Modelos, WhatsApp, stores reales y P0 | Sin autorizaciones/servicios/trazas en este encargo | NOT_RUN / P0 pendiente |

Matriz completa CAT/CON/ORD/RES/SEC: MATRIZ_PRUEBAS.md; se distinguen rutas no comprobadas, como la navegación Volver en el conjunto nuevo. No se afirma un porcentaje global incluyendo bloqueos ni certificación comercial.

## Seguridad, coste y experiencia

- AI interpreta; backend lee datos del workspace, decide propuestas y revalida antes de persistir. No se modificaron consentimiento H06, fingerprint, CAS, motores, Outbox, UnknownDelivery ni toma humana.
- La transcripción A–N final hace **3 llamadas controladas** (E/F/G) tanto legacy como CTA, dentro de la misma ventana de seis; las demás son locales. Baseline E/G hacía una llamada cada uno y se detenía antes de avanzar. No hay medición anterior comparable de toda la cadena ni ahorro monetario acreditado. No se elevaron cuotas, retries ni llamadas de composición.
- Duración completa A–N en la última batería: ~267 ms legacy / ~319 ms CTA, con fakes y sin latencia de red; no es benchmark de producción ni del modelo. Tokens y coste Groq/Gemini: NOT_MEASURED, cero llamadas reales.
- Sin documentos nuevos de contexto ni historial paralelo. Se reutilizan LastIntent, CurrentQuestion, envelope y persistencia CAS existente. El filtro de sede explícita consulta solo cuando es necesario; nunca asume la sede de un borrador para catálogo global.

Ejemplo sintético verificado: E «Esta disponible el enjuague bocal?» → producto autorizado S/24.00 y aviso de stock sin confirmar; F corrige la mención y conserva el hecho; G «Quieor hacer una reserva» → COLLECT_LOCATION, cero efectos. I/J seleccionan la sede y la opción 2 realmente mostrada. K/L/M completan datos; «De acuerdo.» no confirma; N «Sí» ante el resumen vigente crea una única reserva del fixture. No prueba la causa del incidente histórico real.

## Pendientes y siguiente acción

1. Auditoría del diff local y nuevo gate consolidado sobre el futuro SHA aprobado por el propietario. No se hace commit/push ni dispatch desde este encargo.
2. Evaluación semántica independiente con proveedor real solo con autorización y presupuesto acotado; el harness F4 existente no se ejecutó ni se duplicó.
3. Correlación autorizada del P0 histórico o aislamiento acreditado antes de permitir reservas reales. No se declara solucionado a partir de estas pruebas.
4. Integraciones PostgreSQL/Firestore Emulator/Redis y WhatsApp: NOT_RUN aquí. El flag CTA permanece false por defecto y global; no activar en API compartida.

Skills usadas: systematic-debugging y code-testing desde sus commits oficiales y MIT (URLs, archivos y límites en SKILLS_ORIGEN.md). La metodología se aplicó inline: no se fingió disponibilidad de agentes Copilot. Frontend, staging e infraestructura sin cambios. Se termina en revisión, no se abre otra fase ni se publica.
