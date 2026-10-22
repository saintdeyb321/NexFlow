# Concierge 3.0 — Sprint 4: rendimiento y garantías

## Alcance y evidencia

Checkout local efectivo; referencia aportada: `b038c6ea16517143d0f7d086b4b1275464124a29`. No se ejecutó Git ni se verificó ese SHA. Se conservaron los archivos existentes y se compararon únicamente los archivos intervenidos contra sus textos iniciales. Sin servicios reales, proveedores pagados, migraciones, cambios de infraestructura ni frontend.

## Fase A: principales costes y selección

| Fuente confirmada | Decisión |
|---|---|
| Contexto: cada recuperación consulta Redis y Firestore; cada Save añade lectura CAS y escritura. Entitlements vuelve a consultar workspace/licencia/módulos. | Medir, conservar. Las lecturas posteriores a interpretación/composición y anteriores al transporte protegen toma humana, revocación y concurrencia. No se añadió reutilización de contexto entre estas barreras. |
| Ofertas: Type se consulta completo antes de filtrar y mostrar cinco. El precio referencial leía un ID y luego volvía a consultar el tipo por nombre. | Una sola optimización de catálogo: transportar el servicio puntual mediante el `OfferingQueryPlan` existente, exclusivamente dentro del turno. |
| Menús y selección: sedes repetidas en una construcción; disponibilidad puede recalcularse al continuar/decorar. | Reusar sólo el booleano de sedes dentro de BuildOptions. Conservar revalidaciones de disponibilidad. Añadir observabilidad, no una caché global. |

Requieren medición real: p50/p95, documentos por catálogo, bytes serializados, retries de Firestore, ocupación SQL y contención del presupuesto. Requieren otro alcance/autorización: invalidación distribuida, índices, retención/TTL, particionar presupuestos o rediseñar locks. Ninguna implementada.

Anclas: `ContextRecoveryService.cs:31–56`, `EntitlementService.cs:59–97`, `OfferingService.cs:24–61`, `FirestoreCatalogRepository.cs:139–144`, `ConversationFlows.cs:467–542`.

## Tres recorridos actuales: derivación estática

Precondiciones: conversación automática existente, entrada nueva sin replay/errores, licencia completa, sin borrador anterior. A tiene horario global; B tiene PDF CURRENT válido; C tiene dos sedes y servicio inequívoco, pero aún no sede elegida. Los recuentos cambian si faltan datos, hay reintentos o se confirma una operación.

| Recorrido | Invocaciones de estado y negocio | PostgreSQL deducido | Redis deducido | IA / salida |
|---|---|---|---|---|
| A: «¿Qué horarios tienen?» | StateService recupera contexto 2 veces; shortcut 1; Save CAS 1. Sedes 1 y calendario global 1. | 2 snapshots de licencia, cada uno workspace/licencia/módulos; resolución entrante 1 y saliente 2 consultas. Total esperado 9 consultas en estos caminos, sin contar worker/webhook. | 3 GET; 1 SET por Save, más recargas si caché ausente/desactualizada. | 0 IA; una respuesta textual durable. |
| B: «¿Qué servicios ofrecen?» | 5 recuperaciones y 1 Save CAS. Una consulta SERVICE para comprensión. PDF CURRENT: 2 lecturas del artefacto y hash que consulta perfil, categorías, SERVICE y sedes. | 8 snapshots de licencia; 1 búsqueda SQL por SourceMessageId; resolución entrante 1 y saliente 2. Esperado 28 consultas bajo esas precondiciones. | 5 GET; 1 SET por Save, más recargas. | 0 IA; PDF por outbound durable. Sin PDF, texto y aviso con cooldown: costes adicionales variables. |
| C: «Quiero reservar una limpieza dental para mañana» | 5 recuperaciones y 2 Save CAS. Perfil 1; SERVICE 1; sedes 3 (resolver, prompt y menú persistido). Sin consulta de agenda todavía: falta sede. | 9 snapshots de licencia; 2 búsquedas por SourceMessageId (replay inicial y entrada de BookingFlow); resolución entrante 1 y saliente 2. Esperado 32 consultas. No crea reserva ni Outbox comercial. | 5 GET; 2 SET por Save, más recargas. | 0 IA; pregunta con sedes reales. Tras elegir sede/fecha/hora, el motor añade horarios, servicio puntual y agenda SQL; al confirmar revalida y persiste reserva/Outbox. |

Son invocaciones deducidas, no sentencias SQL capturadas ni coste facturado. Un snapshot de licencia válido en background hace tres consultas de repositorio; no se memoiza. Los counts excluyen ingreso/FIFO, creación/cierre de conversación, fallbacks y retries SDK/EF.

Lecturas de `conversationStates`, sin retry: A=4, B=6, C=7 (recuperaciones + CAS). Cada recuperación mantiene autoridad Firestore incluso con Redis hit. Además hay tres búsquedas iniciales de outbound; StateService consulta conversación dos veces, hace UpsertConsumer y AddMessage; la salida prepara, reclama, confirma y relee su ledger. Esos repositorios y transacciones añaden documentos/operaciones. Una query de catálogo devuelve N servicios, otra puede leerlos para el hash; una query de horarios devuelve varios días. **Cinco resultados visibles nunca equivalen a cinco lecturas Firestore.** No se suman estos números como factura total.

Rutas: `ProcessIncomingMessageCommandHandler.cs:51–159`, `ConversationStateService.cs:53–136`, `AiResponseOrchestrator.cs:60–324`, `CatalogGenerationService.cs:88–143`, `FirestoreConversationRepository.cs:61–219`, `DefaultInstanceResolver.cs:27–60`.

## Mediciones locales reproducibles

Fixture existente, handler/recovery/pipeline/flujos/Knowledge reales; transporte RPC Firestore en memoria para contexto. Guard, StateService, catálogos, motor y outbound están sustituidos por las dependencias ya existentes del fixture. **No son totales de producción ni tiempos de red**: StateService del fixture recupera contexto una vez, no dos; PDF/hash y ledger outbound no ejecutan sus lecturas reales.

| Entrada | RPC lectura de contexto | Commits | Sedes | Listas de ofertas | Perfil / horario | Entitlement | IA | Salidas |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Horarios | 3 | 1 | 1 | 0 | 0 / 1 | 1 | 0 | 1 |
| Servicios | 5 | 1 | 0 | 1 | 0 / 0 | 7 | 0 | 1 |
| Reserva para mañana | 6 | 2 | 3 | 1 | 1 / 0 | 8 | 0 | 1 |
| Dirección del negocio | 4 | 1 | 1 | 0 | 0 / 0 | 2 | 0 | 1 |

TRX: `backend/NexFlow.Tests/TestResults/concierge-sprint4/sprint4-complete.trx`; cada fila está en StdOut de `Sprint4_turn_cost_records_repository_calls_without_claiming_external_latency`.

## Fase B: tres mejoras acotadas

1. **Precio del servicio del borrador:** una lectura actual por ID alimenta el plan factual existente, sin búsqueda completa por nombre. Se comprueba actividad/tipo, ámbito del servicio y existencia actual de sede. ChatFlow/composición vuelven a validar módulos/versión/modo. Prueba de resumen completo: 1 GetServiceById, 0 GetServices, precio actualizado, mismo borrador. Ahorro estimado por código: una query SERVICE/N documentos; no se midieron N ni dinero. Durante COLLECT_TIME se conservan las lecturas adicionales del servicio/disponibilidad de la continuación. Constructores anteriores sin repositorio de sedes conservan el camino Knowledge; DI productiva ya registra ese repositorio.
2. **Menú:** con LOCATIONS + BUSINESS_HOURS y global vacío, 2 llamadas a sedes antes, 1 después. Reproducción inicial y verificación final mediante el mismo mock existente. Dos turnos hacen dos lecturas: no hay snapshot compartido ni TTL nuevo.
3. **Intención informativa local:** solicitudes inequívocas de dirección del negocio reutilizan PublicTopic/AnswerInformation y LOCATIONS, sin proveedor ni presupuesto IA. Gramática acotada de consulta; cambios, entidades desconocidas y mensajes mixtos no se resuelven por esta regla. Precio, horarios, perfil, listado, selección vigente y continuación mantienen rutas locales. Ambigüedad real conserva extractor estructurado.

Corrección de seguridad asociada a opciones: fingerprint serializado incorpora fecha, hora y preferencia, además de goal/paso/sede/servicio. Opciones antiguas se refrescan, incluidas etiquetas ambiguas: no caen a IA para elegir arbitrariamente. Listas con el formato histórico se invalidan de forma segura una vez; no se borra el borrador. La selección siempre reconstruye opciones actuales.

No se eliminó ninguna lectura CAS, de modo humano, de licencia, disponibilidad, ledger u Outbox. Redis sigue siendo caché (TTL 30 min). No hay snapshots entre turnos ni un segundo estado. `IsRepair` amplio queda como P2 fuera de estas mejoras: no se amplió su lenguaje sin un defecto directamente reproducido. Las regresiones existentes de consentimiento condicionado permanecen aprobadas.

## IA y telemetría

Presupuesto `ai-budget` intacto: transacción, dedupe por mensaje, límites cliente/workspace y ventana sin retroceso. Un documento por workspace puede contender; las pruebas sintéticas de concurrencia no demuestran capacidad real. No se cambió por intuición.

Se reutiliza ILogger y Stopwatch mediante un helper interno, sin otro almacén/monitor: Turn, ContextRecovery, Interpretation, KnowledgeQuery, Offering*Read, ResponseGeneration, Composition, Delivery y AiProvider. Campos nuevos: fase, resultado, milisegundos, proveedor/motivo finitos y número de invocaciones al proveedor. `MenuLocations/QueriesAvoided=1` sólo se emite cuando se reutilizó efectivamente el dato. Sin teléfonos, nombres, texto, prompts, IDs de reservas ni excepciones completas en estos eventos. Errores de logging no cambian resultados de negocio.

ResponseGeneration/Turn son tiempos inclusivos: **no sumar fases anidadas**. Offering*Read mide I/O del repositorio, no toda la enumeración posterior. Delivery es intento/ledger, no recepción o lectura de WhatsApp. AiProvider cuenta llamadas del router, no cada HTTP interno de Gemini ni tokens facturados. Fallback transitorio existente preservado; deadline conjunto 20 s, modelos y límites intactos. JSON acotado, módulos/reloj/goal/paso; no se agregó historial ni PII al prompt. No se ofrecen p50/p95 ni ahorros de latencia sin observación real.

## Validación y límites

- Build: `dotnet build backend/backend.slnx --no-restore --disable-build-servers -m:1 --verbosity quiet`: **0 errores, 3 advertencias anteriores** (CS8981 en refactor y Designer; CS8602 en FirestoreCatalogRepository:228).
- Suite dirigida: `dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --disable-build-servers -m:1 --filter '(FullyQualifiedName~NexFlow.Tests.Concierge|FullyQualifiedName~AssistantConversationTests|FullyQualifiedName~ZeroTokenKnowledgeAuthorizationTests|FullyQualifiedName~AssistantDurabilityTests|FullyQualifiedName~AssistantRedisRecoveryTests|FullyQualifiedName~AssistantRc1|FullyQualifiedName~AiResilienceTests)&Category!=ProviderIntegration'` con TRX `sprint4-complete`: **872 ejecutadas/aprobadas, 0 fallidas, 0 omitidas**, 31 s. Sin mocks/fakes nuevos ni corpus JSON modificados.
- Evidencia intermedia conservada: baseline VSTest anulada por conexión testhost; baseline aislada 5/10 (reproducciones y fixture sin resolutor comercial); regression 866/869; final 868/869; verified 869/869. Se conectó el resolutor comercial existente para medir la ruta real. Se corrigió la selección textual obsoleta y se actualizaron sólo dos verificaciones dependientes de Knowledge/consulta eliminada y precio fijo del fixture; continuidad, consentimiento, pertenencia e idempotencia siguen comprobados. La segunda lectura de continuación permaneció y se corrigió su conteo de prueba. VSTest requirió ejecución fuera del sandbox para loopback local.

Cambios backend: `TurnUnderstandingPipeline.cs`, `InformationalRequest.cs`, `ConversationRecoveryService.cs`, `ContextRecoveryService.cs`, `ProcessIncomingMessageCommandHandler.cs`, `AiResponseOrchestrator.cs`, `OutboundMessageService.cs`, `OfferingService.cs`, `KnowledgeService.cs`, `AiRouter.cs`, nuevo `ConversationDiagnostics.cs`; pruebas `InformationalRecoverySprint1Tests.cs`, `AssistantConversationTests.cs`, `NaturalBookingTests.cs`, `ComposedConversationTests.cs`, `AiResilienceTests.cs`. Frontend: ninguno.

**Criterios locales 1–10: OK para auditoría.** No hay certificación de rendimiento productivo. Pendiente, con autorización separada: número WhatsApp controlado para información → reserva → precio → continuar y revocación/toma humana; observar fases, latencias, volumen de logs, intentos de proveedor, retries del presupuesto y consumo SQL/Redis/Firestore. Requerir acceso de observación a esas métricas y despliegue autorizado; no proveedores pagados ni servicios compartidos desde esta tarea. Sin Sprint 5, commit, push ni despliegue.
