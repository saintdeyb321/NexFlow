# CTA F1 — diagnóstico y primera vertical

Fecha: 2026-10-31. Base local: `1ac887186d49ade0d6bcde6d117dfc9487557420`.
Árbol inicial: solo los dos documentos CTA aportados sin seguimiento; índice vacío. Sin consultas remotas.

## A. Evidencia previa a la implementación

- **CONFIRMADO / REPRODUCIDO, P1:** `AiResponseOrchestrator.RespondCoreAsync`, línea 160 de la base, rechaza ORDER → Reservation antes de ejecutar el flujo. `backend/TestResults/CTAF1/before.trx`: 1 ejecutada, 0 aprobadas, 1 fallida, 0 omitidas. El pedido mantiene `product-a × 2` y `product-b × 1`, pero el foco sigue ORDER. Cero pedidos/reservas, cero llamadas IA en ese turno. La aserción exige RESERVATION y falla.
- `ConversationContextDto` usa una sola proyección mutable: `CurrentGoal`, `CurrentStep`, sede, slots, artículos, pregunta, recuperación y referencias. Cambiar solo CurrentGoal mezclaría datos o permitiría aplicar una revisión al foco equivocado.
- **Compatibilidad viable:** `FirestoreConversationStateRepository.GetStateAsync/UpsertStateAsync`, líneas 24–49 de la base, serializa el DTO en el mismo `contextJson` y compara `StateVersion` dentro de una transacción. Admite propiedades opcionales sin colección, claves Redis ni migraciones nuevas. `ContextRecoveryService.SaveContextAsync` (línea 52 de la base) sigue siendo la única ruta de escritura utilizada.
- `ConversationQuestion.HasCurrentOrderReview/CanConfirmReservation` (líneas 43/50 de la base) ya verifica resumen vigente/consentimiento. Esas garantías se conservan y las preguntas CTA se ligan además a identidad de operación y revisión de activación.
- Los 28 casos `WhatsAppConversationalRepairTests` usan JSON de IA controlado y stores simulados; no son validación de Gemini/Groq/WhatsApp reales. El P0 histórico continúa **NO DETERMINADO CON TRAZA REAL PENDIENTE**.

## B. Decisión mínima de implementación

- Conservar los slots activos como proyección única para los motores. Añadir `Focus` opcional/versionado al DTO, con ámbito workspace/conversación, identidad de operación y revisión de activación. Una única instantánea suspendida del otro tipo.
- Captura selectiva: sede y artículos del pedido, o slots de reserva. Nunca copiar contexto entero, StateVersion, preguntas, menús, consentimientos ni marcadores de commit. Máximo 10 artículos y 16 KiB por instantánea; rechazar sin truncar si excede el límite.
- TTL absoluto según `AssistantRecovery.ContextExpiryHours` (24 horas por defecto), sin renovarlo mediante consultas sobre el otro foco. Al vencer, conservar evidencia pero no reactivar automáticamente.
- IA propone StartOperation/Resume con intención y evidencia literal. El backend verifica objetivo, módulos, ámbito, versión y estado; se bloquea la suspensión durante commit, recuperación pendiente o gestión de reservas existentes. No hay escrituras de negocio en el cambio de foco.
- Al retomar, revalidar mediante los flujos existentes, invalidar cualquier revisión anterior y no emitir consentimiento reutilizable desde el snapshot. Los primeros acuses no ejecutan transacciones.
- Flag aditivo `AssistantRecovery.EnableContextualFocus`, desactivado por defecto; habilitado solo en fixtures de esta vertical. No se cambia configuración productiva.
- Sin persistencia paralela, motores nuevos, LLM de composición adicional ni multi-intent general. Una interpretación por cambio semántico; ninguna segunda llamada para ejecutar o redactar el cambio.

## C. Límite operacional y reversión

Lectura de contextos legacy: Focus ausente conserva el comportamiento previo y el hash previo de preguntas. CAS y Redis siguen iguales. Una versión antigua del binario no preservaría propiedades desconocidas al reescribir contextoJson: **no mezclar escritores antiguos con activación CTA ni revertirlos mientras haya snapshots suspendidos**. El flag se mantiene apagado; publicación/activación requieren revisión separada. No se modifican staging, Oracle ni datos reales.

## D. Validación

**F1 IMPLEMENTADA / GO local para revisión, con el flag apagado.** La evidencia corresponde al árbol modificado sobre `1ac8871`, no a un nuevo SHA publicado. `origin/main` local también apunta a `1ac8871`; no se verificó el servidor remoto ni se ejecutó CI.

| Evidencia local en `backend/TestResults/CTAF1/` | Ejecutadas | PASS | FAIL | Omitidas |
|---|---:|---:|---:|---:|
| `before.trx`, bloqueo previo | 1 | 0 | 1 | 0 |
| `reference-before.trx`, referencia tras reanudación | 2 | 1 | 1 | 0 |
| `regression-final.trx`, vertical y garantías relacionadas | 192 | 192 | 0 | 0 |
| `current.trx`, suite vigente completa, una sola ejecución de cierre | 1764 | 1764 | 0 | 0 |
| `historical.trx`, corpus congelados | 130 | 101 | 29 conocidas | 0 |

El validador emitió `PASS` para vigente y `EXPECTED_HISTORICAL_INCOMPATIBILITIES` para histórico. Conserva todas las identidades y motivos, H06 exactamente `H06/4/A: ReservationEffects`, y los cuatro hashes fijados. Ningún corpus, evaluador congelado, workflow ni regla G8 se cambió.

Los **25 casos nuevos** están en `ContextualFocusTests`. Una prueba intermedia de fallo parcial (`safety-final.trx`) inyectó inicialmente la excepción durante la lectura anterior al cambio de foco; se corrigió el punto de inyección para fallar solo después del CAS, sin debilitar la aserción de conservación. La prueba posterior confirma recuperación sin otra llamada semántica. La reproducción referencial detectó que borrar la referencia vieja, aunque seguro, impedía «Deja solo uno» tras retomar. Se genera ahora una referencia nueva solo si la cotización actual verifica un único artículo; dos artículos siguen exigiendo precisión.

Comandos ejecutados:

```powershell
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --filter FullyQualifiedName~ContextualFocusTests --logger "trx;LogFileName=acceptance.trx" --results-directory backend/TestResults/CTAF1
# Regresión: filtro de siete clases, guardado en regression-final.trx:
# ContextualFocusTests, WhatsAppConversationalRepairTests, OrderConfirmationAuditTests,
# AssistantRc1ConcurrencyTests, AssistantRedisRecoveryTests, G8FocusedRegressionTests, AiResilienceTests.
dotnet build backend/backend.slnx --no-restore
python -B .github/workflows/g8_certification.py run current --trx backend/TestResults/CTAF1/current.trx
python -B .github/workflows/g8_certification.py run historical --trx backend/TestResults/CTAF1/historical.trx
git diff --check
```

Build final: **0 errores, 0 advertencias** en la compilación incremental. Las compilaciones iniciales mostraron los tres avisos anteriores (dos CS8981 de la migración `refactor` y CS8602 en `FirestoreCatalogRepository`); no se modificaron esos archivos.

### Cobertura de la vertical

| Control | Evidencia en los casos nuevos |
|---|---|
| Pedido → reserva → pedido; dos artículos exactos | `An_open_order_can_pause_for_a_reservation_and_return_without_business_effects`: cero pedidos y cero reservas |
| Revisión de cada operación y edición aislada | `Independent_drafts_need_new_reviews_and_only_one_order_is_committed`: snapshot de reserva inmutable al editar pedido; consentimientos separados; un efecto por cada autorización válida, en turnos diferentes |
| Restart y TTL | Servicio de recuperación y repositorio nuevos sobre el mismo SDK fake; reloj +25 h, sin renovación ni reactivación del snapshot vencido |
| Catálogo, precio y revocación | Tres variantes; no reuso de revisión anterior ni escritura por acuse antiguo |
| CAS | Dos escritores sobre la misma versión: un ganador y un conflicto; pedido conservado |
| Replay / UnknownDelivery | Mismo MessageId no cambia estado ni repite interpretación o efecto; transportes reales no ejecutados |
| Tenancy y humano | Envelope de otro workspace rechazado; takeover durante IA impide transición |
| Legacy y flag apagado | Lectura de contextJson sin Focus mediante repositorio; mismo hash legado; comportamiento anterior por defecto |
| IA insuficiente/fallida y decisiones pendientes | Tres respuestas insuficientes y tres tipos de recuperación/commit; ningún descarte ni transición |
| Pregunta antigua | Mismo OperationId pero revisión de activación anterior: no autoriza; relectura/revisión nueva |
| Consultas laterales | Sedes, servicios y disponibilidad puntual; ambos borradores intactos |
| Salida del modelo | `FocusProposal` enviado como JSON del proveedor se rechaza; solo el backend lo construye |

## E. Implementación y ejemplo

Archivos modificados: `IConversationCache.cs`, registro DI de Application, `AiInterpreter.cs`, `ConversationQuestion.cs`, `TurnUnderstanding.cs`, `TurnUnderstandingPipeline.cs`, `AiResponseOrchestrator.cs`, `AssistantRecoveryOptions.cs` y el fixture `AssistantConversationTests.cs`. Nuevos: `ConversationFocusState.cs`, `ConversationFocusService.cs`, `ContextualFocusTests.cs` y este informe. Los motores, repositorios productivos, `ContextRecoveryService`, `OrderFlow`, Redis, infraestructura y frontend no fueron modificados.

La fase se reconstruye desde los slots con `NextBookingStep` o una revisión nueva de pedido. El cambio de foco guarda ambos lados atómicamente en un CAS; las lecturas posteriores del flujo no son una transacción distribuida. Si fallan, el foco y ambos borradores quedan recuperables; el MessageId de transición permite continuar sin otra interpretación. La referencia de un solo producto se regenera desde los hechos del presupuesto actual, nunca desde una opción vieja.

Ejemplo **sintético**, no captura de WhatsApp:

```text
Antes: pedido abierto → «También quiero hacer una reserva»
       → exige terminar o descartar; foco ORDER.
Después: 2 Pan integral + 1 Queque → cambiar a reserva
         → pedido suspendido, solicita sede; cero efectos.
         «En Concepción, limpieza dental» → solicita fecha.
         «Mejor volvamos al pedido» → recupera 2 Pan integral + 1 Queque.
         Editar cantidad → reserva suspendida intacta.
         «Envía la solicitud» → revisión nueva, sin registrar pedido.
         «De acuerdo.» → cero efectos.
         «Confirmar» → un PendingReview; no confirma la reserva.
```

## F. Eficiencia, límites y gate siguiente

- Caso sintético completo medido en `current.trx`: **3 interpretaciones para 3 cambios de foco**, 173 ms de ejecución de fixture; los commits explícitos y replays no añaden interpretación. No es latencia de red ni p50/p95 de producción. El caso básico de ida/vuelta requiere 2 interpretaciones. Baseline del primer cambio: 0 llamadas, pero bloqueaba la tarea.
- Prompt comparable real construido por `AiInterpreter`: **6970 → 7357 caracteres, +387**, solo al habilitar CTA. Proveedor simulado. El contexto no recibe IDs, snapshot, precios ni datos personales del borrador suspendido. Tokens, precios y coste real: **NO MEDIDOS**. No se midió una tasa real de repreguntas.
- Una escritura CAS explícita adicional por transición, más los guardados/revalidaciones habituales del flujo y pregunta. Misma clave Redis/documento Firestore; sin persistencia paralela.
- Limitaciones intencionadas: un foco y un suspendido, máximo 10 artículos/16 KiB al suspender; no instrucciones mutables compuestas, no múltiples borradores de un tipo. Si termina el activo, se retoma el pendiente antes de iniciar otro. El snapshot caducado no se reanuda ni se borra automáticamente. El retorno semántico requiere interpretación disponible; permanecen las rutas locales del foco actual y asistencia humana.
- Configuración preparada, **no activada**: `AssistantRecovery:EnableContextualFocus` / variable `AssistantRecovery__EnableContextualFocus`. No mezclar escritores antiguos, ni apagar/revertir mientras haya snapshots sin un procedimiento de drenaje. La lectura legacy es compatible; no se promete compatibilidad de escritura con binarios antiguos que ignoran Focus.
- **P0 NO DETERMINADO CON TRAZA REAL PENDIENTE**. Los casos nuevos protegen el contrato local; no explican el incidente real. Se requieren correlación de mensajes, versiones de contexto, revisión/fingerprint, consentimiento, recibo y outbound originales sanitizados.
- **NO EJECUTADOS:** emuladores/stores externos reales, Redis real, Gemini/Groq reales, CI, ARM64 y WhatsApp. No Docker, migraciones, commits, push ni despliegue. Los dos documentos iniciales se conservan sin seguimiento y el índice sigue vacío.

Siguiente gate: auditoría de esta F1 local. Después, F2 limitada al ciclo de vida/revisión de operaciones, caducidad/descarte del suspendido y mejor proyección de referencias, con pruebas de proveedores autorizadas separadas. **No se inició F2. NO-GO para transacciones DEMO/producción mientras siga pendiente el P0 y la aceptación externa.**

## G. Árbol final sin preparar en el índice

```text
 M backend/NexFlow.Application/Abstractions/Cache/IConversationCache.cs
 M backend/NexFlow.Application/DependencyInjection/DependencyInjection.cs
 M backend/NexFlow.Application/Features/AI/Interpretation/AiInterpreter.cs
 M backend/NexFlow.Application/Features/Automation/Dialogue/Contracts/ConversationQuestion.cs
 M backend/NexFlow.Application/Features/Automation/Dialogue/Contracts/TurnUnderstanding.cs
 M backend/NexFlow.Application/Features/Automation/Dialogue/Understanding/TurnUnderstandingPipeline.cs
 M backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/AiResponseOrchestrator.cs
 M backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/AssistantRecoveryOptions.cs
 M backend/NexFlow.Tests/AssistantConversationTests.cs
?? backend/NexFlow.Application/Features/Automation/Dialogue/Contracts/ConversationFocusState.cs
?? backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/ConversationFocusService.cs
?? backend/NexFlow.Tests/Concierge/ContextualFocusTests.cs
?? docs/NEXFLOW_CODEX_PROMPT_01_FOCO_CONTEXTO_2026-10-31.md
?? docs/NEXFLOW_CTA_ARQUITECTURA_OBJETIVO_Y_BRECHAS_2026-10-31.md
?? docs/arquitectura/
```

La carpeta nueva contiene únicamente este informe. Los dos documentos CTA iniciales no se editaron. `git diff --cached --name-only` vacío; `git diff --check` PASS (avisos de normalización LF/CRLF de Git, sin errores). Sin commit, push, workflows ni despliegue.
