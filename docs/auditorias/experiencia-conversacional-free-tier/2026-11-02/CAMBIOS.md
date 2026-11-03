# Cambios

## Diferencia de esta reanudación frente a 1bba6bbb

1. `backend/NexFlow.Infrastructure/Engines/AI/GeminiAiProvider.cs`: añadir generación acotada a1024 tokens en texto/chat, igual que JSON. No fuerza JSON, no activa composición, no cambia modelo, claves ni reintentos.
2. `backend/NexFlow.Tests/AiGlobalAdmissionTests.cs`: doce casos adicionales (Gemini tres modos, cinco configuraciones parciales, una cuenta pequeña, tres tipos de fallback), más aserción de consumo al terminar cooldown. Se reutilizan InMemoryFirestore y transportes Moq; no servicios externos.
3. Los seis informes de este directorio: reemplazar contenido provisional por evidencia y limitaciones; actualizar estado.

No hay cambios de frontend, motores transaccionales, corpus/evaluadores congelados, workflows, appsettings, licencias reales, infraestructura o migraciones en esta reanudación. No se creó commit ni staging Git. Los dos documentos originales del propietario se conservan como fueron publicados.

## Implementación previa conservada en 1bba6bbb

Archivos backend respecto a39432d7, agrupados por responsabilidad:

- Contexto: `Abstractions/Cache/IConversationCache.cs` (continuación validada en el mismo contexto CAS).
- IA: `Features/AI/Interpretation/AiInterpreter.cs`, `Features/AI/Router/AiQuotaOptions.cs`, `AiTelemetry.cs` (directivas, prompt condicional, contrato/admisión y métricas).
- Diálogo: `Features/Automation/Dialogue/Contracts/CommerceTurn.cs`; `Composition/OfferingResponseFormatter.cs`, `SecretaryPresentation.cs`; `Policy/DialoguePolicy.cs`; `Understanding/CommerceTurnResolver.cs`, `InformationalRequest.cs`, `TurnUnderstandingPipeline.cs`.
- Orquestación: `Features/Automation/ProcessMessage/Services/AiResponseOrchestrator.cs`, `AssistantRecoveryOptions.cs`, `ConversationRecoveryService.cs`; `Flows/ConversationFlows.cs`, `OrderFlow.cs`.
- Infraestructura: `Engines/AI/GroqAiProvider.cs`, `GeminiAiProvider.cs`; `Persistence/Firestore/FirestoreAiRequestAdmission.cs`; `Persistence/DependencyInjection/DependencyInjection.cs`.

Prefijos anteriores: Application o Infrastructure bajo `backend/NexFlow.*` según corresponda. AiRouter, AiEvidenceValidator, motores de reserva/pedido y ResponseComposer no se reescribieron.

Pruebas/fixtures conservados: `AssistantConversationTests.cs`, `AssistantRc1Tests.cs`, `Rc3Sprint2MenuTests.cs`, `NexFlow.Tests.csproj`, `Concierge/NaturalExperienceTests.cs`, `Concierge/NaturalExperienceEvaluation.json`, `AiGlobalAdmissionTests.cs`. Simulador: `tools/ai/capacity_simulator.py`, `capacity_assumptions.example.json`, `test_capacity_simulator.py`.

### Cambios explícitos de estímulo, no aserciones

La primera consolidada conservada dio cuatro fallos. Una regresión real bloqueaba leer perfil al rechazar una operación; se acotó el atajo de cita a BusinessHours. Tres tests actuales pedían «menú» para obtener el picker del paso, incompatible con el nuevo menú principal global:

- `AssistantRc1Tests.Booking_picker_shows_each_offering_once_and_only_one_numbered_list`.
- `AssistantRc1Tests.Changing_menu_context_cannot_inherit_another_menus_offset`.
- `Rc3Sprint2MenuTests.Current_page_numbering_and_replay_preserve_the_draft_without_committing_a_reservation`.

Se usa «continuemos» para solicitar picker; fixtures manuales COLLECT_SERVICE no conservan un servicio ya seleccionado. **Se mantienen todas las aserciones** de IDs, paginación, replay y ausencia de efectos. Se preserva `current-final.trx` rojo. Ningún corpus/evaluador congelado ni aserción histórica fue alterado. Estos cambios ya estaban publicados antes de reanudar.

## Impacto y controles

Sin estado paralelo ni migración: continuación dentro de contextJson; cuota en documentos de Firestore existente, apagada por defecto. Nuevas lecturas/escrituras de admisión sólo al habilitarla. No aumentar llamadas IA por turno ni habilitar generación creativa. Pausa asistente opt-in por workspace, licencias e históricos intactos; CTA global permanece apagado. P0 histórico sin resolver.
