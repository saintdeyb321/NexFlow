# Cambios

Base: `7c4782b35bd811cc26a502f2ae2b013e554f0238`. Índice vacío. Los dos planes nuevos del usuario no se editaron.

| Archivo | Cambio y razón |
|---|---|
| backend/NexFlow.Application/Features/Automation/Dialogue/Understanding/EntityMentionResolvers.cs | ResolveCommercial tolera subsecuencia del nombre/alias con hasta dos ediciones individuales; preserva colisiones y calificadores con/sin. ResolveEvidence y AiEvidenceValidator intactos. |
| backend/NexFlow.Application/Features/Automation/Dialogue/Understanding/CommerceTurnResolver.cs | Construye OfferingQuery específico a partir de entidad literal y acto respaldados, Question conservada y sede autorizada; referencias locales y semánticas ligadas a pregunta vigente; aclara referencias inválidas. Hint legacy solo para elegir tipo de catálogo amplio revalidado. CurrentPurchaseReference conserva sus restricciones de escritura. |
| backend/NexFlow.Application/Features/Automation/Dialogue/Understanding/TurnUnderstandingPipeline.cs | StartOperation respaldado puede preparar reserva vacía sin requerir un slot preextraído. No modifica confirmación ni motores. |
| backend/NexFlow.Tests/Concierge/AgentStabilizationTests.cs | Nuevo: 27 casos sobre transcripción A–N, catálogo, erratas, ámbito/tenant, licencia, referencias y ordinales. Usa fixture existente, reloj Nov 1 Lima y JSON controlado, no modelos reales. |
| backend/NexFlow.Tests/Concierge/WhatsAppConversationalRepairTests.cs | Solo visibilidad private→internal de Speak para reutilizar el adaptador de prueba. Ninguna aserción original cambiada. |
| .agents/skills/{systematic-debugging,code-testing}/ | Skills oficiales fijadas y licencias, sin scripts ejecutados. |
| docs/auditorias/agente-conversacional/2026-11-02/ | Siete documentos de trazabilidad exigidos. |

No cambian contratos públicos, DTO persistente, flags, límites, AiRouter, prompts IA, motores transaccionales, frontend, staging, appsettings, infraestructura, migraciones, corpus/evaluadores ni validador G8. No se añadieron dependencias ni reglas por las frases del incidente.

Se preservaron artefactos rojos en `backend/TestResults/AgentStabilization20261102/`. La suite vigente completa se ejecutó una sola vez; los tres errores hallados se corrigieron con repetición del subconjunto afectado, sin borrar ni reescribir ese TRX.
