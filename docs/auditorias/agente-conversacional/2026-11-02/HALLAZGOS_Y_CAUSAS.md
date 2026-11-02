# Hallazgos y causas

P0 histórico de reserva inesperada continúa NO DETERMINADO; esta investigación no utiliza trazas privadas ni efectos reales.

1. P1 CONFIRMADO/REPRODUCIDO: ResolveCommercial solo compara formas exactas/plurales; ResolveEvidence limita una edición y misma cantidad de palabras. «enjuague bocal» y «enjuague bocal sin slcohol» no encuentran el nombre con tamaño al final. Corrección comercial separada: subsecuencia ordenada, palabras distintas, máximo dos ediciones individuales de un carácter; palabras cortas exactas, todas las colisiones conservadas. No se amplió AiEvidenceValidator ni la validación de identidades de sedes/nombres/fechas.
2. P1 CONFIRMADO/REPRODUCIDO: CommerceTurnResolver no materializaba un OfferingQuery específico desde SearchTerm/Question semánticos. ChatFlow terminaba usando KnowledgeService sin la distinción de stock del plan. Corrección: fragmento presente en mensaje, acto respaldado y resolución contra catálogo autorizado; hechos/precios del backend y Stock conservado. Una propuesta no fundamentada produce aclaración.
3. P1 CONFIRMADO/REPRODUCIDO: TurnUnderstandingPipeline requería un slot determinado además del StartOperation respaldado; sin datos devolvía información de procedimiento. Se prepara borrador vacío con pregunta inicial; esto no crea consentimiento ni efectos. No se añadieron palabras para «Quieor».
4. P2 REPRODUCIDO en prueba negativa: «con» se eliminaba de CommercialWords y podía confundir con/sin. Se conserva como parte de la entidad comercial, igual que «sin».
5. NOT_REPRODUCED hasta aquí: ordinal de lista vigente. El menú de servicios se ordena alfabéticamente; los IDs persistidos corresponden al texto realmente mostrado. No cambiar orden por expectativa histórica de una captura con otro catálogo.
6. INVENTARIO_NO_DISPONIBLE: no hay repositorio/contrato de stock en Application/Domain; Stock es categoría de pregunta, no unidades verificadas. CAT-06/07 de inventario real bloqueados por diseño de producto, no se implementa inventario.
7. P1 CONFIRMADO/REPRODUCIDO: la rama local de CommerceTurnResolver recuperaba OfferingReference.Read sin validar la vigencia ni intención de CurrentQuestion. Tras 11 minutos o una pregunta nueva ajena podía responder por el artículo anterior. La rama semántica con referencia inexistente retornaba null y caía en catálogo general. Corrección: referencia vinculada a pregunta vigente, intención, workspace y borrador actual; referencia inválida aclara en vez de ampliar la consulta.
8. Control de regresión del parche: la ruta específica nueva debe respetar sede explícita, sin heredar implícitamente la del borrador. Un test rojo de sede detectó el filtro faltante en el parche y se añadió resolución autorizada antes de leer productos. No se atribuye este defecto nuevo al SHA base.

## Referencias verificadas y decisiones

| Hallazgo | Archivo:método/línea actual | Evidencia / estado |
|---|---|---|
| Erratas y sufijo omitido | EntityMentionResolvers.cs:114 ResolveCommercial; 137 CommercialSpelling; 149 OneEdit | Dos variantes rojas iniciales; tres positivas y cuatro negativas verdes. Coste O(términos de nombre × términos de consulta), 8 términos de consulta, 32 por nombre, 200 caracteres en ruta aproximada. |
| Pérdida del hecho Stock | CommerceTurnResolver.cs:64; ConversationFlows.cs:853 ChatFlow; KnowledgeService.cs:129 QueryOfferingsAsync | E devolvía catálogo sin aviso y mezclaba otros artículos; ahora OfferingQuery específico solo contiene producto autorizado y precio vigente. |
| Preparación bloqueada | TurnUnderstandingPipeline.cs:267–277 | StartOperation se descartaba sin slots. Dos expresiones × dos modos reproducidas; ningún efecto hasta resumen/consentimiento. |
| Referencia vieja | CommerceTurnResolver.cs:104,175,423 CurrentOfferingReference | Pruebas current/expired/foreign/new-question; antes expiración/pregunta nueva usaban producto anterior y fallback podía listar todo. |
| Consentimiento | ConversationQuestion.cs:33 Current; :76 CanConfirmReservation | No modificado. Huella, identidad/revisión, TTL, control humano y estado CONFIRM siguen vigentes; A–N crea una sola reserva tras N. |
| Cuota | AssistantRecoveryOptions.cs:8; FirestoreConversationStateRepository.cs:61 TryConsumeAiBudgetAsync | No modificado: 6/minuto cliente, documento ai-budget, source deduplicado. A–N final usa 3 llamadas controladas/minuto. |
| Compatibilidad | CommerceTurnResolver.cs:246 catalogReference; :418 CurrentPurchaseReference | La única suite completa reveló tres regresiones del parche; 253 pruebas afectadas verdes tras corregirlas. No se tocaron sus aserciones. |

Las rutas sin prefijo de esta tabla son relativas a `backend/NexFlow.Application/Features/Automation/Dialogue/Understanding/`, salvo Flows/ConversationFlows.cs, Knowledge/KnowledgeService.cs, Contracts/ConversationQuestion.cs y las opciones/repositorio indicados en CAMBIOS. No se interpretan estos fixtures como trazas del workspace productivo.

## Recorrido contrastado (sin ampliar auditoría)

- InboundMessageWorker.cs:60–72 procesa el sobre durable reclamado, comprueba workspace/source/instancia/teléfono y llama al handler; no se modificó worker/webhook.
- ProcessIncomingMessageCommandHandler.cs:54–88 aplica guard, reanuda respuesta durable, valida estado/control humano y llama al orchestrator. Las pruebas reutilizan su handler real; guard/transportes son fakes, no se afirma prueba HTTP real.
- AiResponseOrchestrator.cs:73–108 reanuda recibos, recuperación y pipeline; TurnUnderstandingPipeline:173 pide admisión de cuota antes del intérprete. CommerceTurnResolver decide únicamente consultas/propuestas tipadas.
- AiResponseOrchestrator.cs:351–361 verifica workspace/source/StateVersion, persiste OfferingReference e información; :381–411 compone y recuerda la pregunta vigente. La conservación de un selector de reserva tras una interrupción es deliberada: menú/consulta que reanuda ese selector no es lista obsoleta.
- BookingFlow/OrderFlow siguen revalidando antes de los motores/repositorios; el resultado pasa por Outbound durable, sin llamadas directas al proveedor. Las pruebas comerciales existentes comprueban PendingReview, fingerprint, concurrencia y recuperación de notificación. Persistencia PostgreSQL/Firestore/Redis reales: NOT_RUN.

## Límites pendientes

- P0 histórico: NO DETERMINADO sin correlación/trazas de operación/source/outbound del incidente. Cero permisos para investigar Oracle aquí.
- Los JSON IA son controlados. Calidad semántica Gemini/Groq, tokens y latencia de proveedor: NOT_RUN, autorización específica ausente.
- Un SearchTerm específico nuevo debe fundamentarse en fragmento literal del mensaje; si el modelo solo inventa/canoniza un nombre sin referencia vigente se pide aclaración. No se amplía la autoridad del LLM para suplir esa evidencia.
- El gate completo actual no se reejecutó tras los tres parches de compatibilidad. Su último TRX global sigue rojo, aunque los tres casos y su subconjunto están verdes; se requiere gate consolidado del futuro SHA antes de certificar release.
