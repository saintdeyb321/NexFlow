# Diagnóstico

Base inicial: `39432d7bf870bf5e0ba370738b05e3bd750bc9ab`. Reanudación: `1bba6bbb1655ce9ce0ab4f4c8eb346b09920b460`, main, árbol e índice limpios al entrar. El propietario publicó la implementación entre ambas intervenciones. Resultados finales: árbol local sobre el segundo SHA, con el parche Gemini de esta reanudación; no CI remoto.

## Causas verificadas

| Hallazgo | Código actual y corrección | Prueba |
|---|---|---|
| Inicio natural respaldado se perdía si la política local clasificaba Information | `TurnUnderstandingPipeline.cs:286`: admitir preparación respaldada también desde Information, sin escritura ni consentimiento | `Natural_reservation_preparation_does_not_require_a_patient_name_or_commit` |
| Necesidad dental no resolvía un servicio informativo | `CommerceTurnResolver.cs:88-110`: contrastar nombre propuesto con catálogo autorizado, fragmento literal y coincidencia única; sólo información, nunca selección clínica | `Service_orientation_can_offer_only_real_information_without_selecting_a_treatment` |
| Propuesta semántica podía romper ambigüedad del catálogo | `CommerceTurnResolver.cs:95`: no aceptar sugerencia si el resolver devuelve Ambiguous | `Semantic_service_suggestions_cannot_invent_entities_or_select_a_reservation`, casos inexistente, ID ajeno, evidencia fabricada, revocación y ambigüedad |
| Menú principal reutilizaba picker de reserva | `ConversationRecoveryService.cs:40,359,604`: opciones raíz separadas del picker, sin borrar datos y con revalidación de lista vigente | `The_main_menu_does_not_become_the_current_service_picker_or_erase_the_draft` |
| Nombre descartado sobrevivía en RealCustomerName | `ConversationRecoveryService.cs:286`, `ConversationFlows.cs:100`, `BookingFlow.ClearDraft`: limpiar nombre al descartar/terminar | `A_discarded_patient_name_is_not_identity_for_the_next_reservation`, legacy/CTA |
| Cita registrada confundida con horario público/nueva preparación | `InformationalRequest.cs:19-33`, `DialoguePolicy.cs:40-45`, `TurnUnderstandingPipeline.cs:214`: acotar sólo el atajo de horarios y orientar a verificación/humano | Set congelado manage, con cita sintética previamente registrada |
| Pregunta por doctor recibía fallback genérico | `SecretaryPresentation.cs:9`, `TurnUnderstandingPipeline.cs:211`: declarar falta de agenda profesional y ofrecer capacidades licenciadas | `A_professional_question_without_reservations_cannot_advertise_an_unlicensed_agenda` |
| Ejemplo de tarde contradecía preferencia de mañana | `SecretaryPresentation.AskTime`: preguntar dentro de franja sin fabricar TargetTime | `A_morning_preference_never_prompts_with_an_afternoon_example_or_selects_a_time` |
| Descartar pedido y preparar reserva perdía intención nueva | `TurnUnderstandingPipeline.cs:218`, `AiResponseOrchestrator.cs:237`, `ConversationRecoveryService.cs:280`: continuación en CAS vinculada a workspace/conversación/mensaje/pregunta, TTL 10 min y otra decisión obligatoria | `A_mixed_discard_and_new_reservation_waits_for_a_bound_decision_and_preserves_only_verified_date` |
| UNKNOWN se presentaba como cuota agotada | `AiQuotaOptions.cs:31`, `AiResponseOrchestrator.cs:271`: configuración no apta → 503; límite conocido → 429 | `Unknown_global_quota_never_calls_providers_and_does_not_block_a_zero_token_menu` |
| **Reanudación: Gemini texto/chat rechazados con cuota válida** | `GeminiAiProvider.cs:94-104,129-133`: faltaba maxOutputTokens fuera de JSON. Se fija 1024 en los tres modos; MIME JSON/temperatura determinista siguen exclusivos de JSON | `Gemini_bounds_each_supported_mode_before_admission_without_forcing_json`: dos rojos UnboundedTokenEnvelope antes, tres verdes después |

Nombres de pruebas en `NaturalExperienceTests`, salvo UNKNOWN y Gemini en `AiGlobalAdmissionTests`. Rutas de automatización bajo `backend/NexFlow.Application/Features/Automation/`; cuota bajo `Features/AI/Router`, Gemini en `backend/NexFlow.Infrastructure/Engines/AI`.

## Arquitectura y compatibilidad

- Reutiliza handler, AiRouter, intérprete, pipeline, pregunta vigente, motores y CAS. Nuevas directivas de información/navegación no son consentimiento. `NextReservation` es interno y se rechaza si viene del JSON del proveedor.
- Continuación dentro de contextJson existente, sin migración ni estado paralelo. Continuación mixta automática sólo legacy; CTA conserva descarte/foco existente. `EnableContextualFocus` permanece desactivado por defecto.
- Pausa comercial opt-in por workspace, sin alterar licencias. Recuperación de efectos registrados precede al guard, conservando notificaciones/idempotencia y lecturas históricas autorizadas.
- ResponseComposer sin cambios ni activación de composición IA. ResponsePlan ya rechaza texto vacío; se prueba ese contrato sin duplicar el resguardo.
- El gate local verifica consentimiento/revisión, CAS, tenencia y entregas inciertas con simulación; no certifica infraestructura real.

## Pendientes

**P0 histórico de reserva inesperada: PENDIENTE DE EVIDENCIA.** El arrastre local de nombre es real y corregido, pero no explica por sí solo una confirmación histórica. Faltan trazas sanitizadas de mensajes, resumen/revisión, consentimiento y recibo persistido; no autorizar reservas reales por este informe.

NLU Groq/Gemini, modelos disponibles, cuotas efectivas, valoración humana, tokens/latencia reales, Firestore multirréplica real y capacidad de 20 negocios: NO VERIFICADOS. Los 35 casos congelados usan JSON controlado; no son evaluación ciega independiente.
