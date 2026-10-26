# Agent Runtime 4.0 — F7: historial y certificación técnica local

**Estado actualizado tras autorización del hotfix:** F7-01 resuelto localmente; G7.1 aprobado y F7 reanudada/completada para cierre técnico documental. GO local limitado al contrato vigente y GO para preparar verificación operativa; activación/comercial NO-GO. La evidencia roja y el primer dictamen se conservan íntegros en el registro siguiente. El cierre actualizado comienza en «F7-01 — Reproducción, corrección y verificación».

## Registro original de la parada F7

## Estado e identidad

**F7 INCOMPLETA / NO-GO.** Referencia local `85d349ac5690fbc3dcd3d97b72cf0c3a45e2ff23` en `main`, leída directamente de HEAD/ref. No se ejecutó Git ni se certificó limpieza, remoto o versión en Oracle. Evidencia de esta ejecución: árbol local más una prueba de reproducción añadida. No existe un nuevo commit candidato generado por este trabajo.

Se leyeron AGENTS, plan/prompts Runtime 4.0 y los informes 00–06/04_1. Los documentos previos describen resultados de sus propias etapas; no son resultados ejecutados en F7. La regla expresa de detenerse ante un P0/P1 nuevo prevalece sobre completar las demás verificaciones.

## F7-01 — P1 confirmado y reproducido

**Defecto:** pérdida de la referencia al resumen del pedido tras revocación de licencia durante el commit. Al restaurarse la licencia, un envío explícito posterior puede persistir precios nuevos sin presentarlos ni solicitar consentimiento sobre ellos.

### Reproducción local

Se amplió únicamente `backend/NexFlow.Tests/Concierge/CommerceSafetyTests.cs:163`, reutilizando su fixture `Durable`, OrderFlow, ContextRecoveryService, OfferingService y repositorios Firestore reales sobre el **InMemoryFirestore existente**. No se añadió simulador, fixture ni infraestructura. Las licencias y notificaciones utilizan los mocks ya existentes.

Método: `Reauthorization_after_a_revoked_commit_requires_consent_to_the_updated_quote`.

1. Sembrar producto sintético Pan integral a 250 unidades menores y preparar dos unidades. El resumen contiene `5.00`.
2. Enviar con fuente `revoked-commit`: primera consulta de módulos permite ORDERS+CATALOG; segunda consulta, inmediatamente antes del efecto, devuelve ninguno.
3. Comprobar dos consultas de licencia, cero pedidos y borrador conservado con cantidad dos. Estas aserciones pasan.
4. Restaurar ORDERS+CATALOG y actualizar catálogo a 300 por unidad.
5. Enviar con fuente nueva `restored-commit`, sin una consulta intermedia de resumen.
6. Exigir cero pedidos y nuevo resumen `6.00` con ORDER_CONFIRM antes de crear/notificar. **Falla la primera de estas garantías**: existe una solicitud por 600 unidades menores.

Mensaje exacto del TRX:

```text
A changed quote requires renewed review before creating an order; actual orders=1, total minor units=600.
```

La prueba deja como regresión las comprobaciones posteriores de importe nuevo, paso ORDER_CONFIRM y ausencia de notificación; **no llegaron a ejecutarse** porque la creación indebida ya hace fallar el test. No se presentan como aprobadas. Es reproducción directa del flujo productivo y repositorio sobre SDK fake, no prueba E2E del webhook, Emulator real, Oracle ni WhatsApp. No se afirma incidencia observada en producción.

### Causa raíz y alcance

| Evidencia local | Responsabilidad |
|---|---|
| `backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/Flows/OrderFlow.cs:108–112`, ProcessPlannedAsync/Submit | Compara huella sólo si el paso empieza por ORDER_COMMIT, ORDER_REVIEW u ORDER_CONFIRM. Un paso nulo evita todas esas comparaciones y llama Submit |
| Mismo archivo `:139–144`, función Submit | Guarda checkpoint CAS, revalida módulos y, si se revocaron, persiste CurrentStep=null, perdiendo la huella pero manteniendo ítems |
| Mismo archivo `:148–154` | Construye ExpectedCatalog con el catálogo recién leído y crea el pedido; ese snapshot ya contiene el precio nuevo |
| `backend/NexFlow.Infrastructure/Persistence/Firestore/FirestoreOrderRepository.cs:98–115`, CreateOrderAsync | El fence transaccional compara contra catálogo actual, correctamente coincidente con ese nuevo snapshot. No acredita que ese precio se haya mostrado al consumidor |
| `backend/NexFlow.Application/Features/Automation/Dialogue/Understanding/CommerceTurnResolver.cs:36–56`, ResolveAsync | Envío explícito genera Submit; no exige por sí mismo un paso con huella |
| `backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/AiResponseOrchestrator.cs:236–245` | Delega el efecto al mismo OrderFlow; el control posterior de StateVersion no reconstruye consentimiento perdido |

No es duplicación ni cruce de tenant demostrado. La licencia sí impide el primer efecto, el catálogo se revalida y el pedido sigue siendo PendingReview. **Eso no satisface el consentimiento sobre importes vigentes**, por lo que la clasificación es P1 y bloquea el cierre. No se modificó ninguna de estas clases.

### Corrección mínima propuesta, no implementada

Preservar o restablecer explícitamente el estado de revisión después de una revocación sin efecto durable y exigir una huella de resumen válida antes de cualquier nuevo envío. Si falta o difiere, mostrar resumen actual y exigir un nuevo consentimiento. No convertir la conservación de ORDER_COMMIT en un bloqueo permanente ni impedir recuperar una solicitud ya persistida. Mantener TryResume, CAS, identidad de operación, NotifyOnce y revalidación de licencia/catálogo. No borrar borradores ni recibos.

Regresión necesaria tras autorización: esta prueba hasta verde; precio sin cambios; ausencia/legacy de huella; revocación en ambas guardas; recuperación de commit ya persistido tras revocación; replay, concurrencia, notificación fallida y catálogo cambiado dentro de transacción. Extender al handler real para completar la evidencia de entrada. No requiere un motor nuevo ni migración por este diagnóstico.

## Build y pruebas realmente ejecutadas en F7

Directorio de artefactos: `backend/NexFlow.Tests/TestResults/agent-runtime-f7/`.

| Ejecución | Resultado real |
|---|---|
| `dotnet build backend/backend.slnx --no-restore` | Correcto, salida 0, 0 errores y 3 advertencias preexistentes; `build.log` |
| Prueba de reproducción añadida | 1 ejecutada, 0 aprobadas, 1 fallida, 0 omitidas; `order-consent-reproduction.trx` y `.log` |
| Contrato vigente, históricos completos, F3 v2 y suites adicionales F7 | **NO EJECUTADOS**: parada obligatoria por P1; no son omisiones de la ejecución anterior ni suites aprobadas |
| ARM64, servicios reales, proveedores y WhatsApp | No ejecutados; no autorizados en este encargo |

Comando de reproducción desde raíz:

```powershell
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --filter FullyQualifiedName~Reauthorization_after_a_revoked_commit_requires_consent_to_the_updated_quote --logger 'trx;LogFileName=order-consent-reproduction.trx' --results-directory backend/NexFlow.Tests/TestResults/agent-runtime-f7
```

La primera compilación del test nuevo produjo CS0019/CS1503 al usar Count como propiedad sobre IEnumerable. Se corrigió exclusivamente el test materializando ToArray; la ejecución posterior compiló y falló por la garantía comercial descrita. No se atribuye ese error de preparación al backend. El build de solución anterior a la prueba ya era correcto; la ejecución final de dotnet test recompiló correctamente el proyecto modificado.

Advertencias del build: dos CS8981 de `20260929030327_refactor` y su Designer; CS8602 en FirestoreCatalogRepository:228. Sin restore ni cambio de dependencias. Comandos .NET con ejecución local aprobada fuera del sandbox; sin servicios externos.

La referencia documental F6 es 1084 aprobadas/1096 ejecutadas, con 12 incompatibilidades históricas y 966 casos actuales aprobados, incluidos 12 F3 v2. **No se reprodujo ese resultado en F7 ni se certifica para esta entrega.** El nuevo test rojo amplía el conjunto y no se oculta para conservar una cifra previa.

Hashes de corpus al revisar F7, iguales a 04_1:

- CommercialCertificationCorpus.json: `E9C6311369F4D810B553BA546F3D06E7A4D6B6A901CC892AFEB2ECEBBF1E19F4`.
- QualityHoldout61.json: `3DFE02AC4CC1D6A067D1A875917615A4A3B3B73AB6D72C47EF2D63FE1F7E96E0`.

Los doce fallos históricos registrados exigían lista automática después de una franja, incompatible con F3. Quedan históricos pendientes de repetición, no se confunden con F7-01 ni se cambian corpus/evaluadores.

## Mapa consolidado para continuar la auditoría

Lo siguiente sintetiza la arquitectura documentada y leída en F0–F6; **no sustituye una verificación completa F7**, suspendida en el control de pedidos.

| Capa | Implementación y autoridad existente |
|---|---|
| Entrada | EvolutionWebhookController autentica instancia y resuelve workspace; Inbound SQL deduplica y worker conserva FIFO. No se toma tenant del texto/LLM |
| Estado | ConversationStateService, ContextRecoveryService y FirestoreConversationStateRepository; contexto durable CAS/StateVersion; Redis caché contrastada con durable |
| Continuidad F1 | ConversationQuestion opcional en el contexto existente, vigencia y huella de borrador/opciones; invitación, información, selección y consentimiento diferenciados |
| Comprensión | Recovery/DialoguePolicy/pipeline + resolutores locales y AiInterpreter por AiRouter compartido; evidencia y entidades autorizadas, no ejecución arbitraria |
| Lecturas F2 | BusinessReadTools: perfil público, productos, servicios, sedes, horarios, FAQ y disponibilidad; máximo tres llamadas/ocho segundos, salida limitada, licencia antes/después. No es loop de herramientas autónomo del LLM |
| Reservas F3 | BookingFlow + ReservationEngine existentes: preferencia sin hora inventada, consulta puntual de inicios libres, selección y consentimiento separados. Citas registradas con guard de privacidad/derivación humana |
| Pedidos F4 | OrderFlow/OrderDraftMerger + repositorio existente: borrador vacío/multítem, cotización, solicitud PendingReview, recibo y aviso recuperable. **Garantía de revisión incompleta en F7-01** |
| Respuesta F5 | ResponsePlan + VerifiedResponseLayout/ResponseComposer: bloques literales, agrupación de párrafos con orden fijo; sin paráfrasis libre. Fallback determinista |
| Salida | OutboundMessageService/ledger Firestore para texto y medios; UnknownDelivery sin reenvío ciego. Outbox SQL de eventos de negocio hacia workflow es un mecanismo distinto |
| Medición F6 | AiTelemetry y ConversationDiagnostics: intentos, uso informado/desconocido, rutas y fases; no nuevo almacén ni autoridad de negocio |

El mismo runtime usa datos/entitlements/contexto de cada workspace. No se crea agente, modelo entrenado, motor comercial, contenedor ni estado paralelo por empresa. IA propone intención/entidades/directivas y, opt-in, agrupación de hechos; backend decide permisos, datos, disponibilidad, transiciones y persistencia. No pagos, stock garantizado ni módulo de empleados.

F1 consolidó referencia vigente y algunas puertas de inicio/consulta duplicadas; F2 consolidó lecturas y proyección pública; F6 evitó un Save consecutivo demostrado. Persisten capas de clasificación en Recovery/Policy/Commerce y formateo rígido. Su reducción adicional es deuda, no autorización para refactor en F7. Revalidaciones de licencia/CAS/humano tras esperas son defensas, no duplicación prescindible.

## Calidad, economía y garantías pendientes

Comprensión semántica, continuidad, orientación, presentación y operaciones son ejes distintos. Fixtures de clínica, hotel y tienda y guiones históricos no equivalen a evaluación independiente. Preguntar hora preferida y aceptar nombres inequívocos mejora orientación local; no acredita comprensión de erratas inéditas con proveedores reales. La agrupación F5 no demuestra redacción natural plena. Preguntas de pasos, aclaraciones y resultados siguen siendo deterministas y pueden resultar rígidos; requieren revisión humana.

Una paráfrasis futura necesita hechos/relaciones/alcance/polaridad/condiciones y pregunta referenciada, con un verificador independiente que demuestre preservación semántica, fallback exacto, pruebas adversariales y límites de presupuesto/latencia. Validar números o pedir aprobación al mismo modelo no basta. No se habilita aquí.

P2 pendientes de evidencia: paginación física de colecciones y carga; contención de presupuesto/locks SQL; retención de contexto/PII y métricas; interpretación/proveedores, costo y latencia reales; compatibilidad de rollback y escritores antiguos. Usage ausente no es cero; admisión presupuestaria no es HTTP ni cargo; BusinessResult no cuenta efectos únicos. F6 documenta estas distinciones, pero su inspección y regresión finales F7 quedan pendientes.

No se ha demostrado en F7 un P0, una fuga entre workspaces, reserva duplicada o fallo de takeover; **tampoco se certifica su ausencia** al no completar las suites. P1 abierto: F7-01. P3: deuda documental/presentación a revisar sin parches cosméticos. No inventar un hallazgo productivo de Oracle a partir de estas pruebas.

## Checklist de cierre y archivos propios

- OK: identidad de ref local; lectura documental; build; reproducción localizada con fixtures existentes; evidencia roja conservada; ninguna modificación productiva.
- PENDIENTE: corrección autorizada de F7-01; regresión F0–F6 completa; cierre de matrices de seguridad/costos/naturalidad; runbook exacto y ARM64; versión activa y rollback; backups restaurables; piloto y evaluación independiente.
- Creado: `docs/agent-runtime/07_PILOTO_RELEASE.md` y este documento.
- Modificado: `backend/NexFlow.Tests/Concierge/CommerceSafetyTests.cs`, sólo una prueba nueva.
- Artefactos locales generados: build.log, order-consent-reproduction.log/.trx y salidas normales bin/obj de .NET. Sin frontend, código productivo, configuración, secretos, migraciones o documentos previos modificados por esta tarea.

**NO-GO técnico local y NO-GO de activación/comercial. Preparación del piloto suspendida.** El paso seguro es auditoría del P1 y autorización humana de su corrección mínima; después congelar otra candidata y repetir gates. No avanzar ni desplegar automáticamente.

## F7-01 — Reproducción, corrección y verificación

### Identidad de esta reanudación

HEAD/ref local main: **883370715122068fdb668872537590e14c92a25f**, coincidente con la referencia de la autorización. Lectura directa de archivos, sin Git. No se certifica limpieza, remoto ni imagen activa. Se capturaron las versiones iniciales de cada archivo intervenido para revisar el diff propio y conservar cambios anteriores. La candidata actual es esa base **más el diff local del hotfix y pruebas/documentos**, todavía sin un nuevo SHA publicado. No atribuir a esa base publicada la implementación nueva ni un resultado CI.

Se aplicaron AGENTS, ambos documentos maestros y los informes 00–08, contrastados con código. No se reimplementó F0–F6 ni se cambió AiRouter. La evidencia roja original del registro anterior permanece en agent-runtime-f7; resultados nuevos en agent-runtime-f71.

### Cambio mínimo e invariante positiva

Único archivo productivo modificado: backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/Flows/OrderFlow.cs, ProcessPlannedAsync.

- Antes: sólo se rechazaba una huella distinta bajo tres prefijos; si faltaba el prefijo, se enviaba. Ahora se exige objetivo ORDER y un paso exactamente igual a ORDER_REVIEW:Fingerprint u ORDER_CONFIRM:Fingerprint. ORDER_COMMIT:Fingerprint sólo es válido al recuperar su SourceMessageId original.
- Cotización completa y sede válida siguen siendo requisitos anteriores. Se exige además acción explícita IsSubmit del resolver existente o afirmación inequívoca de ConversationConfirmation sobre ORDER_CONFIRM coincidente. Una propuesta Submit aislada no concede consentimiento.
- Ante ausencia/discordancia de referencia o consentimiento: persistir ORDER_CONFIRM, resumen actual y pregunta específica, sin CreateOrder ni NotifyOnce. La huella existente vincula workspace, sede, productos, nombres, cantidades, precios/monedas y alcance comercial.
- Revocación detectada al inicio sobre borrador revisable: invalidar CurrentStep mediante CAS, conservar artículos y denegar. Si existe un commit de otra fuente con resultado todavía desconocido, conservarlo para recuperar su propia identidad; no liberarlo por suposición.
- Revocación después del checkpoint sigue invalidando la revisión y no produce efecto. La guarda de recuperación de catálogo conserva su comportamiento de denegación; la nueva validación positiva impide enviar al restaurarse la licencia sin otro resumen.
- TryResume permanece anterior a cualquier intento nuevo: pedido durable con misma identidad se recupera sin exigir otra compra/consentimiento, completa NotifyOnce y limpia el borrador mediante CAS. Otro mensaje no adelanta un commit pendiente.
- Sin cambios en OrderDraftMerger.Price, ConversationContextDto, FirestoreOrderRepository, motor, CAS, recibos, notificación, Outbox, transporte o esquema. El fence de catálogo transaccional continúa rechazando cambios durante la persistencia; no reemplaza el consentimiento.

No hay nueva estructura de consentimiento ni reglas lingüísticas añadidas. La referencia es el paso/huella backend ya existente. Un estado legacy incompleto vuelve a revisión, no se migra ni borra.

| Estado/escenario | Resultado verificado |
|---|---|
| Null, ORDER_COLLECT_ITEMS con artículos, huellas vacías/incorrectas, REVIEW/CONFIRM obsoletos | Resumen vigente y ORDER_CONFIRM, cero efecto/avisos, artículos conservados; consentimiento posterior crea una vez |
| Revocación en primera/segunda guarda, precio igual | Denegar e invalidar revisión; restauración exige otro resumen aunque el precio no cambie |
| Revocación tras leer catálogo y precio cambiado | Reproducción original pasa sin cambiar aserciones; PEN 6.00 mostrado antes de crear |
| Revocación en RecoverCatalog | Sin creación/aviso; al restablecer licencia presentar nueva revisión |
| Cantidad/sede modificada o huella de otro workspace | No coincide; revisión actual antes de persistir; scope/importe correctos tras consentimiento |
| ORDER_COMMIT sin efecto, misma huella y fuente original | Recupera una vez; otro SourceMessageId no adelanta la operación |
| ORDER_COMMIT ya durable/aviso fallido/licencia revocada | Recuperación existente, un pedido y aviso idempotente; sin compra nueva |
| Propuesta Submit con texto no consentido | AwaitingConfirmation, sin efecto |
| Handler real, revocación durante checkpoint | Conserva borrador; restaura/recotiza, confirma y replay sin segunda creación |

### Pruebas y preparaciones explícitas

CommerceSafetyTests añade **17 casos**: ocho estados incompletos/obsoletos, dos puntos de revocación sin cambio de precio, tres cambios de identidad del resumen, propuesta sin consentimiento, revocación al recuperar catálogo, handler real y checkpoint coincidente. Se conserva intacta la reproducción F7-01 anterior. Fixtures Durable, CommerceCertificationFixture, AssistantConversationTests.Fixture e InMemoryFirestore existentes; no infraestructura ni simuladores nuevos.

Dos pruebas históricas inyectaban SeedDraft(ORDER), cuyo paso era COLLECT_DATE, sin presentar resumen. Con la invariante positiva ya no pueden llegar directamente a creación:

1. AssistantRc1ConcurrencyTests.Order_commit_followed_by_context_failure_recovers_the_same_order_after_restart: se presenta y verifica el resumen antes de activar el fallo de persistencia; se reinicia sólo la observación de respuestas del fixture. Conserva las aserciones originales de IOException, un pedido, recuperación y borrador vacío.
2. AssistantConversationTests.Order_does_not_finalize_from_a_model_sentinel_without_explicit_user_command: se presenta el resumen antes del sentinel; mantiene cero pedidos por sentinel, envío sólo por comando explícito, cantidad y replay.

Son ajustes de precondición del contrato autorizado, no relajación de expectativas de consentimiento, idempotencia o durabilidad. Ningún corpus ni evaluador histórico se modificó.

Una suite adicional mostró un defecto de reloj del harness: CatalogRecoveryTests.Deadline_is_five_minutes_preserves_pdf_and_allows_retry_without_refund falló para PRODUCT/SERVICE a las 23:55:44 UTC, cuando sumar cinco minutos cambió de día de cuota. CatalogFixture.UsagePath usa el día del reloj real; CatalogGenerationService:223 usa UtcNow.Date del reloj inyectado. La primera cuota seguía siendo uno porque la segunda correspondía al día siguiente: no es consumo duplicado ni reembolso productivo. Se fijó **sólo ese caso** a mediodía UTC del mismo día; mismas aserciones y producto intacto. Los 16 casos pasan después. No se modificó CatalogFixture global ni código PDF productivo. La separación general entre reloj de lectura del helper y reloj inyectado permanece como deuda de tests, sin implicación productiva demostrada.

### Ejecuciones reales y comparación con F6

Comandos desde raíz, sobre backend/NexFlow.Tests/NexFlow.Tests.csproj; todos con --no-restore y TRX en backend/NexFlow.Tests/TestResults/agent-runtime-f71. Las repeticiones usan nombres de artefactos distintos para preservar evidencia.

| Artefacto | Ejecutadas | Aprobadas | Fallidas | Omitidas |
|---|---:|---:|---:|---:|
| p1-fixed.trx | 1 | 1 | 0 | 0 |
| g71-commerce.trx, antes de preparar resumen en test de crash | 153 | 152 | 1 | 0 |
| g71-commerce-final.trx | 153 | 153 | 0 | 0 |
| consent-final.trx | 50 | 50 | 0 | 0 |
| f7-all-with-historical.trx, antes de preparar resumen en test sentinel | 1113 | 1100 | 13 | 0 |
| f7-final-all-with-historical.trx | 1113 | 1101 | 12 | 0 |
| f7-closure-all-with-historical.trx, incluye checkpoint añadido | **1114** | **1102** | **12** | **0** |
| f7-additional-local.trx | **217** | **217** | **0** | **0** |
| f7-outbox-http.trx, cruce UTC del harness | 16 | 14 | 2 | 0 |
| f7-outbox-http-final.trx | **16** | **16** | **0** | **0** |
| p1-and-handler-final.trx, añade comprobación de pregunta ConfirmOrder vigente | 2 | 2 | 0 | 0 |

Build requerido: dotnet build backend/backend.slnx --no-restore, correcto; g71-build.log y f7-final-build.log, **0 errores/0 advertencias incrementales**. Recompilaciones de tests observaron los tres avisos preexistentes CS8981/CS8602 ya registrados; no se corrigieron archivos ajenos. No restore ni paquetes nuevos. El proyecto de tests compiló en dotnet test; no se afirma que backend.slnx por sí sola ejecute pruebas.

Filtro ampliado íntegro:

~~~text
(FullyQualifiedName~Concierge|FullyQualifiedName~Assistant|FullyQualifiedName~Knowledge|FullyQualifiedName~AiResilienceTests|FullyQualifiedName~CatalogChatTests|FullyQualifiedName~ReservationsPeruTimeTests|FullyQualifiedName~ZeroTokenKnowledgeAuthorizationTests)&Category!=Integration&Category!=RedisIntegration&Category!=ProviderIntegration
~~~

Filtro adicional: ReservationsApiTests, ReservationsRepositoryTests, Rc3Sprint41RecoveryTests, Rc3Sprint5SecurityTests, EvolutionInboundTests, EvolutionMediaTests, EvolutionSecurityTests e IntegrationSafetyTests; mismas exclusiones externas. Último filtro separado: CatalogRecoveryTests y CatalogRecoveryHttpTests. Integraciones reales y ProviderIntegration se excluyeron explícitamente: **no están aprobadas ni se cuentan entre cero omisiones**. No suites frontend o servicios reales.

Particiones del TRX final, sin sumar reejecuciones:

| Contrato | F6 histórico documentado | F7 ejecutado |
|---|---|---|
| Vigente sin los dos evaluadores históricos | 966/966 | **984/984** |
| Original completo | 118/130 | **118/130**: CommercialCertification 90/100 y QualityHoldout61 28/30 |
| F3 v2 incluido en vigente | 12/12 | **12/12** |
| Total ampliado | 1084/1096 | **1102/1114** |

Delta 18: reproducción F7 original ya publicada + 17 casos nuevos. Los 217+16 adicionales son disjuntos del filtro principal: **1347 casos distintos ejecutados, 1335 aprobados, 12 fallidos, 0 omitidos** en esos tres TRX finales. No es toda la suite del repositorio ni un global verde. No se agregan otras corridas a ese total.

Únicos mensajes históricos finales:

~~~text
clinic-09/-10, hotel-09/-10, restaurant-09/-10,
bakery-09/-10, limited-09/-10: VerifiedFact
H10/3/A: VerifiedFact
H21/3/A: VerifiedFact
~~~

Se contrastaron con 04_1 y el TRX F6: son exactamente las doce expectativas de hora automática tras franja, sin nuevo criterio fallido. F3 v2 conserva selección/consentimiento y pide preferencia sin inventar hora. Hashes de corpus iguales a los del registro original; evaluadores intactos. El GO local es del contrato vigente con esta incompatibilidad explícita, no de la suite histórica original.

## Auditoría arquitectónica final y límites de garantía

Rutas siguientes relativas al repositorio; métodos/ubicaciones del árbol revisado. No se reaudita indiscriminadamente todo el SaaS.

| Invariante | Evidencia de código actual y regresión ejecutada |
|---|---|
| Instancia autenticada/tenant | API/Controllers/Webhooks/EvolutionWebhookController.ReceiveMessage:39–117; IncomingMessageGuard.CheckMessageAsync:34–64. EvolutionSecurityTests/EvolutionInboundTests: clave por instancia, rechazo cruzado, envelope y recepción durable |
| Dedupe/FIFO | Infrastructure/Persistence/PostgreSQL/Repositories/InboundMessageRepository.GetAndLockNextMessagesAsync:76 y ProcessClaimedAsync:120; InboundMessageWorker:40–86. Ramas SQL y lease inspeccionadas; locks PostgreSQL reales pendientes, no certificados por SQLite |
| Una interpretación/estado | AiResponseOrchestrator.RespondAsync:66–131, DialoguePolicy, TurnUnderstandingPipeline.PrepareAsync:28–214, ConversationQuestion.Current:25. AssistantConversationTests y DialogueIntegrationTests: antecedentes, menú antiguo, invitación, pregunta y consentimiento distintos |
| Autorización informativa | Handler.TryHandleZeroTokenKnowledgeAsync, AiIntentAccess y BusinessReadTools.ReadAsync:42–106; ZeroTokenKnowledgeAuthorizationTests, AssistantLicenseBudgetTests y RuntimeReadToolsTests: FAQ/sedes/horarios revocados sin lectura, dependencias internas de reserva conservadas |
| Lectura acotada/grounding | BusinessReadTurn:16–26, KnowledgeService.QueryAsync:62–96/QueryBusinessHoursAsync:150. RuntimeReadToolsTests: fuentes, tenant, límite 3/8s, máximo cinco ofertas y salida limitada, ausencia frente a indisponibilidad. Paginación física no universal |
| Reservas | BookingFlow:337–400 y AnswerAvailabilityAsync:498; ReservationEngine.GetAvailabilityAtAsync:89, CreateReservationAsync:164–236. RuntimeNaturalBookingTests, F3 v2 y ReservationsPeruTimeTests; UTC, correcciones, inicio libre, lectura/selección/consentimiento separados. Motor único sin cambios |
| Pedidos/consentimiento | OrderFlow.ProcessPlannedAsync, TryResumeAsync, Submit y RecoverCatalog; OrderDraftMerger.Price:40–72; FirestoreOrderRepository.CreateOrderAsync:24–117. CommerceSafetyTests **36/36**, CommerceHardening/Conversation/LocationRecovery y handler nuevo |
| CAS y recuperación | FirestoreConversationStateRepository.UpsertStateAsync:29–56; ContextRecoveryService:34–62. AssistantRc1ConcurrencyTests, AssistantDurabilityTests, AssistantRedisRecoveryTests: versión, replay viejo y caché obsoleta no prevalecen |
| Entrega durable | OutboundMessageService.SendPreparedCoreAsync:135–202 y FinalizeUnconfirmedAsync:214. EvolutionMediaTests y AssistantRc1ConcurrencyTests: texto/PDF claim único, Failed/UnknownDelivery sin reenvío ciego, confirmación reparable sin transporte nuevo |
| Eventos de negocio | ReservationEngine reserva + Outbox Serializable; OutboxProcessorWorker.ProcessClaimedAsync:39–113. ReservationsRepositoryTests: colisión/rollback en SQLite; CatalogRecoveryTests/Http: HTTP 404/503/timeout simulado, reintentos/fallo terminal. n8n puede recibir entrega repetida: no equivale a segundo efecto comercial |
| Humano y privacidad | Orchestrator:67/110/297, claim outbound y ExistingReservationFlow.TryHandleAsync:28–72. AssistantRc1ConcurrencyTests, RuntimeRealizationTests, ExistingReservationSprint2Tests: bot suprimido, ACK único, cita registrada deriva antes de SQL privado. Mensaje físicamente en vuelo no puede retirarse |
| Plan factual | ResponseComposer.ComposeAsync:41–95 y VerifiedResponseLayout.VerifyAndRender:22–48. RuntimeRealizationTests **9/9** más ResponseComposerTests: orden de referencias fijo, hechos literales, inyección/operaciones/omisiones rechazadas, takeover/revocación |

Prefijos de archivos Application: backend/NexFlow.Application/Features/Automation/ProcessMessage/Services o Dialogue según clase; BusinessReadTools/KnowledgeService bajo Features/Knowledge. No hay nuevo router, bot por negocio, base paralela, motor de reservas/pedidos, pagos o empleados. Datos y permisos son del workspace confiable; el LLM no puede escoger tenant ni confirmar operaciones.

**Atomicidad real:** CAS de contexto y transacción del pedido son fronteras distintas; recuperación une efectos mediante fuente/identidad, no una transacción distribuida con PostgreSQL/Firestore/Redis/n8n. La creación de reserva y su Outbox se coordinan en SQL; entrega externa no es exactamente una vez por definición. SQLite/fakes no certifican xmin, locks, Serializable o contención real de PostgreSQL. Requieren gate aislado con el nuevo candidato.

### Responsabilidades residuales y severidad

- P0/P1 abierto demostrado: **ninguno tras el hotfix en las comprobaciones realizadas**. No es garantía universal ni sustituye CI/servicios reales. P1 F7-01 cerrado con prueba original intacta y regresiones.
- P2: clasificación aún repartida entre Recovery, DialoguePolicy y CommerceTurnResolver; el primer retorno conserva precedencia. Las pruebas actuales verifican los antecedentes y seguridad; no existe justificación nueva para fusionar todo en F7.
- P2: rutas informativas del handler y la fachada conviven. Sus guardas se prueban; autorización repetida tras esperas/CAS no se elimina como duplicación lingüística.
- P2: colecciones leídas antes de filtrar/paginar, perfil/entitlements/revalidaciones y locks SQL mantenidos durante dependencias externas. No saturación demostrada; medir carga/pool, no quitar FIFO ni cachear permisos.
- P2: retención Firestore/PII, exportación de métricas, compatibilidad de rollback, tags base móviles y aislamiento operativo pendientes. Redis TTL 30 min, contexto lógico 24 h y referencia 10 min son plazos distintos, no borrado durable automático.
- P3 de pruebas: dependencia temporal del helper de cuota PDF, caso ajustado explícitamente dentro de una ventana; no cambio de límites productivos.

## Evaluación conversacional y economía

| Eje | Qué está implementado/comprobado localmente | Qué no está certificado |
|---|---|---|
| Comprensión | Resolutores autorizados y AiInterpreter/AiRouter con contexto mínimo; evidencia independiente; variantes de fixtures y corpus | Gemini/Groq reales, erratas inéditas fuera del conjunto público, acierto comercial |
| Continuidad | Pregunta/referencia vigente, CAS, interrupciones conservan borradores, correcciones y expiración | Carga/canal real y corpus privado independiente |
| Orientación | Hora preferida primero, opciones reales a petición, pedido vacío, aclaración sin inventar datos | Utilidad/repetición percibida por clientes |
| Presentación | Hechos deterministas; F5 opt-in organiza párrafos sin reordenar, fallback exacto | Paráfrasis libre o naturalidad plena |
| Operaciones | Consentimiento, workspace/licencia, efecto/aviso único en pruebas y recuperación segura | Operación end-to-end con proveedores/almacenes reales autorizados |

El mismo código se ejercita con clínica multisede, hotel y tienda/panadería; corpus histórico añade restaurante/licencia limitada. No hay ramas nuevas por rubro. Los textos de pasos, confirmación y reparación siguen siendo deterministas: «¿Qué productos y cantidades…?», resumen «Así va tu pedido» y aclaraciones repetidas pueden sonar rígidos. No se midió satisfacción ni mejora semántica con IA real. Ordenar párrafos no resuelve esa limitación.

Propuesta fuera del MVP: representar claims verificables con factId, relación, alcance, polaridad, condiciones y referencia de pregunta; publicar reformulación sólo tras verificación semántica independiente, contraejemplos de negación/precio/fecha/unidad/condición, privacidad/inyección y evaluación humana ciega. Validar números o pedir al propio LLM que se apruebe no basta. No se implementa generador libre ni diccionario mayor.

Telemetría revisada: AiTelemetry.SendAsync:32 y Usage:60; ConversationDiagnostics:10–44; ambos proveedores registran cada PostAsync y usage disponible. AiResilienceTests **69/69** comprueba transporte simulado, fallback, tokens presentes/ausentes, scopes concurrentes y observador que falla sin alterar respuesta. RuntimeEfficiencyTests **3/3** conserva pregunta/contexto/replay y límites del experimento F6.

- Intento HTTP no es admisión ni cargo: router puede hacer fallback entre proveedores; Gemini tiene modelo alternativo. Task/rol del proveedor y motivo de fallback se analizan juntos; rol Primary/Alternate de HTTP no identifica por sí solo fallback del router.
- Usage sólo conserva enteros no negativos informados. Desconocido no es cero; no sumar Total nuevamente a Input+Output ni estimar tokens inexistentes.
- Logs nuevos usan WorkspaceKey hash, categorías y números; sin prompts, respuesta, nombre/teléfono o secretos. Tags no incluyen workspace. Esto no certifica todos los logs históricos del SaaS; custodia/retención operativa siguen pendientes.
- Presupuesto durable actual: default 6 fuentes/min por cliente y 60/workspace, ventana monotónica e idempotencia por fuente; no se cambiaron cuotas. Una fuente reintentada antes de respuesta durable puede originar más HTTP: no afirmar tope físico equivalente.
- Límites: interpretación 20 s compartidos, herramientas tres/8 s, composición default 800 ms y máximo 2 s. No hay deadline universal nuevo del turno inbound. Composición apagada por defecto.
- Instrumentos sin exporter instalado por esta tarea; comprobar recolección/retención antes del piloto. Métricas de repositorio no equivalen a documentos facturados; BusinessResult no cuenta filas únicas.

No se midió ahorro monetario, p95 SaaS ni costos por 1.000. Usar procedimiento de 07: costos por intento/modelo y usage/tarifa verificables + infraestructura/canal/almacenamiento atribuibles, cobertura desconocida explícita y mezcla local/IA/fallback. No vender las lecturas sintéticas evitadas de F6 como ahorro facturado. Límites sugeridos del piloto: participantes, ventana, gasto total y parada aprobados por propietario; no modificación automática de cuotas comerciales.

## Diff propio, release y decisiones finales

Backend productivo: OrderFlow.cs. Pruebas: CommerceSafetyTests.cs, AssistantRc1ConcurrencyTests.cs, AssistantConversationTests.cs y CatalogRecoveryTests.cs. Documentación: 07_PILOTO_RELEASE.md y este informe. Sin frontend, migraciones, modelos, configuración, infraestructura, secretos, AiRouter o motores modificados por esta entrega. Artefactos/logs/TRX y bin/obj normales de .NET generados localmente.

Revisión sólo de cambios propios contra capturas iniciales; no git diff, status ni afirmación de árbol limpio. La reproducción original y corpus conservan su contenido; no se sobrescriben resultados rojos. Build y pruebas no acceden a servicios productivos, no cambian esquema externo y no equivalen a WhatsApp real.

| Gate | Decisión y condiciones |
|---|---|
| G7.1 | **APROBADO LOCALMENTE**: reproducción, build, consentimiento/CAS/replay/avisos aprobados; preparación de dos fixtures históricos explícitamente corregida |
| Cerrar desarrollo local F7 | **GO para el contrato vigente**, sin nuevo P0/P1 demostrado; doce incompatibilidades originales visibles, no global verde |
| Preparar despliegue/verificación operativa | **GO documental**: procedimiento en 07; auditoría del diff y congelación/publicación humana de candidata nueva pendientes |
| Activar piloto supervisado | **NO-GO todavía**: nuevo SHA/CI, ARM64/codec, imagen activa, overrides reales, salud, backup restaurado, rollback y autorización pendientes |
| Comercial | **NO-GO**: evaluación privada independiente ≥30, revisión humana, proveedores/WhatsApp, carga y costos reales no ejecutados |

Primer paso seguro: auditoría humana del hotfix y evidencias; después congelar una nueva candidata y obtener su gate CI aislado. A continuación, operador aporta inventario sanitizado de imagen activa/overrides antes de diseñar la ventana. No asumir que Oracle ejecuta el commit base ni que el runbook histórico autoriza migraciones pendientes.

Rollback sólo a API compatible con el esquema/protocolo/contextos y sin reintroducir F7-01; no revierte pedidos/reservas ya confirmados. Sin digest seguro, pausar nuevas escrituras/piloto y corregir hacia delante. No restauración automática, Down, reenvío UnknownDelivery ni reproceso de mensajes históricos.

**F7 termina en preparación técnica local para auditoría. Ninguna aprobación local activa Oracle, proveedores o WhatsApp. Detención al cerrar esta entrega; no nueva fase ni despliegue.**
