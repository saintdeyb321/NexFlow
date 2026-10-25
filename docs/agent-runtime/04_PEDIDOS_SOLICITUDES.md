# F4 — Solicitudes de pedido y cierre agrupado F2–F4

Base por lectura de referencia local: `0aa3bfbe87e99008b44fdf7e0704a617899f77df`. G2 aprobado localmente; G3 aprobado para el contrato vigente, con doce incompatibilidades históricas P2 conservadas. No se ejecutó Git ni se afirma árbol limpio/remoto. Sin llamadas a proveedores, producción, WhatsApp o almacenes externos. No se inicia F5.

## Reproducción y causa

`RuntimeOrderRequestTests`, `f4-before.trx`: 5 ejecutadas, 3 aprobadas, 2 fallidas, 0 omitidas. Fallaban el inicio sin productos y su equivalente semántico respaldado. `CommerceTurnResolver` convertía la ausencia de artículos en Clarify; `OrderFlow` respondía antes de establecer ORDER. Además, el filtro léxico de candidatos impedía aprovechar una intención semántica de inicio sin sus palabras locales habituales.

## Parche y responsabilidades

- `OrderTurnKind.Start`, agregado al final del enum, distingue iniciar preparación de editar, cotizar, consultar o enviar. `DialoguePolicy.OrderAct` lo convierte en StartOperation.
- `CommerceTurnResolver` conserva la gramática inequívoca existente y acepta un inicio semántico sólo con Act=StartOperation, evidencia literal única, sin aclaración pendiente, pregunta, hipótesis ni negación. No se añadieron sinónimos ni excepciones por producto. No transfiere precios o identificadores IA al borrador.
- Sin producto inequívoco se propone empezar vacío, nunca un artículo supuesto. Las coincidencias ambiguas conservan la aclaración anterior; los servicios no se convierten en productos. Preguntas e hipótesis mantienen su ruta informativa.
- `OrderFlow` revalida licencia y catálogo activo, persiste ORDER/ORDER_COLLECT_ITEMS mediante el contexto CAS existente y pregunta productos/cantidades. Catálogo vacío no inicia preparación. La propuesta sigue vinculada a WorkspaceId, SourceMessageId y StateVersion. La referencia de pregunta F1 proyecta ese paso sin historial ni datos privados.
- `AiInterpreter` describe el inicio de borrador sin artículos como interpretación posible. Se conserva AiRouter; la IA no confirma ni envía pedidos.

No hay segundo estado, colección, motor ni selector. Las ediciones posteriores reutilizan OrderMentionResolver, OrderDraftMerger y su catálogo autorizado, referencias, cantidades, precios enteros y huella de resumen. No se alteraron OrderRepository, transacciones, Outbox, transporte, motores de reserva ni estado humano.

El resumen no constituye por sí mismo un envío: la solicitud explícita de envío sobre el borrador revisado conserva las guardas de catálogo y confirmación existentes. Cambiar precios exige el resumen/consentimiento renovado ya implementado. Un borrador vacío nunca se envía. La creación sigue siendo PendingReview y la respuesta de éxito ocurre después del efecto y NotifyOnce. No afirma pago, stock garantizado, aceptación, entrega ni recogida.

## Archivos por fase

### Backend F2

Aplicación: `Features/Knowledge/BusinessReadTools.cs` (nuevo), `KnowledgeModels.cs`, `KnowledgeService.cs`; `DependencyInjection/DependencyInjection.cs`; `Domain/Enums/ConversationIntent.cs`; `Features/AI/Interpretation/AiInterpreter.cs`; `Dialogue/Understanding/TurnUnderstandingPipeline.cs`; `ProcessMessage/Services/AiResponseOrchestrator.cs`; ChatFlow en `ProcessMessage/Services/Flows/ConversationFlows.cs`.

Pruebas: `Concierge/RuntimeReadToolsTests.cs` (nuevo), `AssistantConversationTests.cs`, `AssistantLicenseBudgetTests.cs`.

### Backend F3

Aplicación: BookingFlow en `ConversationFlows.cs`; `AiResponseOrchestrator.cs`, `ConversationRecoveryService.cs`; `Dialogue/Understanding/NameSlotResolver.cs`, `WorkspaceSlotResolver.cs`, `CommercialCourtesy.cs`, `TurnUnderstandingPipeline.cs`; `Dialogue/Contracts/TurnUnderstanding.cs`; `Abstractions/Cache/IConversationCache.cs`; `AiInterpreter.cs`.

Pruebas: `Concierge/RuntimeNaturalBookingTests.cs` (nuevo); ajustes de interacción para solicitar opciones explícitamente en `AssistantRc11Tests.cs`, `AssistantConversationTests.cs`, `AssistantLicenseBudgetTests.cs`, `Concierge/DialogueIntegrationTests.cs`, `NaturalBookingTests.cs`, `Sprint51ConversationTests.cs`, `InformationalRecoverySprint1Tests.cs`, `CommerceConversationTests.cs`, `ReleaseClosureTests.cs`, `ComposedConversationTests.cs`. Se mantienen las aserciones de seguridad/efectos y las comprobaciones de horarios después de pedir opciones. Ningún corpus JSON modificado.

### Backend F4

`Dialogue/Contracts/CommerceTurn.cs`, `ConversationQuestion.cs`; `Dialogue/Policy/DialoguePolicy.cs`; `Dialogue/Understanding/CommerceTurnResolver.cs`; `ProcessMessage/Services/Flows/OrderFlow.cs`; `Features/AI/Interpretation/AiInterpreter.cs`; `Tests/Concierge/RuntimeOrderRequestTests.cs` (nuevo).

Las rutas abreviadas de aplicación corresponden a `backend/NexFlow.Application/Features/Automation/`, salvo las que indican Knowledge, AI, Domain, Abstractions o DependencyInjection; las pruebas están en `backend/NexFlow.Tests/`. Frontend: sin cambios. Documentación: los tres informes `02_HERRAMIENTAS_LECTURA.md`, `03_RESERVAS_NATURALES.md`, `04_PEDIDOS_SOLICITUDES.md`.

## Evidencia ejecutada

TRX y logs locales: `backend/NexFlow.Tests/TestResults/agent-runtime-f234/`.

| Ejecución | Ejecutadas | Aprobadas | Fallidas | Omitidas |
|---|---:|---:|---:|---:|
| F2 final: f2-g2-final.trx | 1010 | 1010 | 0 | 0 |
| F3 contrato vigente: f3-current-contract.trx | 914 | 914 | 0 | 0 |
| F3 con corpus históricos: f3-functional-and-historical.trx | 1044 | 1032 | 12 | 0 |
| F4 comercio dirigido: f4-commerce.trx | 105 | 105 | 0 | 0 |
| F1–F4 final con corpus: f234-combined-with-historical.trx | 1052 | 1040 | 12 | 0 |
| F1–F4 final contrato vigente: f234-current-contract.trx | 922 | 922 | 0 | 0 |

La prueba final añadida después de `f4-commerce.trx` verifica envío vacío y cancelación confirmada; está incluida en ambas ejecuciones conjuntas. RuntimeOrderRequestTests aporta ocho casos, todos aprobados en la ejecución conjunta.

Filtro conjunto ampliado:

```text
(FullyQualifiedName~Concierge|FullyQualifiedName~Assistant|FullyQualifiedName~Knowledge|FullyQualifiedName~AiResilienceTests|FullyQualifiedName~CatalogChatTests|FullyQualifiedName~ReservationsPeruTimeTests)&Category!=Integration&Category!=RedisIntegration&Category!=ProviderIntegration
```

La ejecución del contrato vigente añade `&FullyQualifiedName!~CommercialCertificationTests&FullyQualifiedName!~QualityHoldout61Tests`. Separó 130 casos históricos: 118 aprobados y 12 fallidos en la ampliada. **No se presenta esa exclusión como suite completa verde.** Los fallos mantienen exclusivamente VerifiedFact por exigir horas automáticas tras una franja: cinco negocios, casos -09/-10, y H10/H21. Corpus y evaluadores permanecen intactos. Evidencia roja conservada; requieren auditoría de compatibilidad del contrato, no una modificación oportunista de expectativas.

La batería conserva pruebas existentes de durabilidad, licencias/revocación, FAQ, interrupciones, replay, fencing/CAS, confirmación, hora/zona/DST, control humano, catálogo mutable, escrituras únicas, notificación fallida y recuperación, aislamiento y UnknownDelivery. Las nuevas pruebas verifican inicio vacío persistente, consulta sin descarte, multítem, corrección, envío PendingReview único, ausencia de IA en la ruta inequívoca, semántica con/sin evidencia, catálogo vacío/licencia denegada, respuesta incierta sin reenvío, takeover y cancelación confirmada.

Son pruebas simuladas locales con fixtures existentes y repositorios reales sobre el fake Firestore existente. No equivalen a emuladores reales, PostgreSQL/Redis, CI, Gemini/Groq o WhatsApp. No se ejecutaron suites frontend, categorías de integración ni suites ajenas al alcance.

Build por fase: `dotnet build backend/backend.slnx --no-restore`, correcto en F2, F3 y F4; logs `f2-build.log`, `f3-build.log`, `f4-build.log`. No restore. En compilaciones de tests se observaron tres advertencias preexistentes: CS8981 en las dos partes de la migración `20260929030327_refactor`, y CS8602 en FirestoreCatalogRepository:228. No se modificaron.

## Recuperación y límites

Crear una solicitud, persistir contexto y notificar no son una transacción distribuida. Se conserva ORDER_COMMIT antes del efecto, identidad determinista y GetOrderById/TryResume con NotifyOnce. La batería existente reproduce el fallo después de crear y antes de notificar; su recuperación no crea una segunda solicitud. El ACK externo incierto continúa UnknownDelivery sin reenvío automático. No se promete entrega física ni aceptación del negocio.

No se modificaron expectativas de holdout, motores ni garantías transaccionales. La revisión de cambios propios se hizo contra las capturas locales previas, sin Git; se eliminaron diferencias de fin de archivo ajenas al comportamiento. No se certifica ausencia de cambios locales anteriores. No se modificaron frontend, infraestructura, secretos o migraciones.

Optimizaciones verificadas: preguntar hora y aceptar nombre inequívoco no llama IA; preguntar la hora preferida no solicita cuadrícula; iniciar pedido inequívoco tampoco llama IA. No se midió ahorro de tokens de proveedores, costes, carga real o reducción de lecturas físicas Firestore. Las herramientas limitan invocaciones/salida; ciertos repositorios siguen leyendo colecciones antes de paginar.

## Dictamen

- G2: **OK local**.
- G3: **OK local para F3; compatibilidad histórica P2 pendiente de auditoría**.
- G4: **OK local** para el alcance implementado y las verificaciones indicadas, sin P0/P1 abierto demostrado.
- Regresión ampliada y certificación comercial: **PENDIENTE**, doce aserciones históricas incompatibles, corpus ciego independiente, revisión humana y pruebas reales autorizadas aún no disponibles. No se declara GO de producción.

Se detiene en F4 para auditoría conjunta; no se implementa F5 ni se ejecutan commit, push o despliegue.
