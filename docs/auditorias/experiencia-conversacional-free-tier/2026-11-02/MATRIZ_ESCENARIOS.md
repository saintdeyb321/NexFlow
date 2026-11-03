# Matriz de escenarios

**PASS de mecanismo** significa handler/pipeline/intérprete/estado/flows existentes con JSON controlado y envío interceptado. No significa comprensión real Gemini/Groq. No se enviaron mensajes ni se registraron operaciones reales.

Prefijos: N=`NaturalExperienceTests`, A=`AgentStabilizationTests`, Q=`AiGlobalAdmissionTests`; N/A en `NexFlow.Tests.Concierge`, Q en `NexFlow.Tests`.

| Caso | Test exacto / evidencia | Resultado y límite |
|---|---|---|
| UX-01 necesidad dental con error | N.Service_orientation_can_offer_only_real_information_without_selecting_a_treatment | PASS; información autorizada, sin seleccionar tratamiento. NLU real NOT_RUN |
| UX-02 consulta contextual | N.Frozen_mechanism_evaluation_preserves_facts_current_context_and_no_unapproved_effects, grupos explore/ambiguous | PASS; propuesta semántica inyectada, no precisión real |
| UX-03 inicio natural | N.Natural_reservation_preparation_does_not_require_a_patient_name_or_commit | PASS; preparación sin efecto |
| UX-04 pedido descartado → reserva | N.A_mixed_discard_and_new_reservation_waits_for_a_bound_decision_and_preserves_only_verified_date | PASS; sí/no/TTL/workspace/General. Continuación automática legacy; CTA conserva descarte existente |
| UX-05 menú global | N.The_main_menu_does_not_become_the_current_service_picker_or_erase_the_draft | PASS; cinco reformulaciones congeladas adicionales |
| UX-06 agenda profesional inexistente | N.A_professional_question_without_reservations_cannot_advertise_an_unlicensed_agenda; N.Frozen_mechanism_evaluation_preserves_facts_current_context_and_no_unapproved_effects, professional | PASS; no inventa doctor ni ofrece agenda sin licencia |
| UX-07 orientación no diagnóstica | N.Service_orientation_can_offer_only_real_information_without_selecting_a_treatment; N.Semantic_service_suggestions_cannot_invent_entities_or_select_a_reservation | PASS; inexistente/ambigua/ajena/revocada no elige entidad |
| UX-08 abreviatura de sede | N.Location_abbreviations_keep_all_authorized_collisions_even_if_ai_proposes_one | PASS; sede única vs dos similares; pide aclaración si ambigua |
| UX-09 cita registrada | N.Frozen_mechanism_evaluation_preserves_facts_current_context_and_no_unapproved_effects, manage | PASS; cita sintética confirmada primero, no cambia registro sin titularidad |
| UX-10 nombre descartado | N.A_discarded_patient_name_is_not_identity_for_the_next_reservation | PASS legacy/CTA; **P0 histórico no determinado** |
| UX-11 franja coherente | N.A_morning_preference_never_prompts_with_an_afternoon_example_or_selects_a_time | PASS; PreferredTime conservado y TargetTime vacío |
| UX-12 retomada y confirmación | A.A_to_N_transcript_uses_current_ordinals_Lima_dates_and_only_current_explicit_consent | PASS; persistencia simulada, aceptación real pendiente |
| UX-13 ordinal/referencia vigente | A.An_ordinal_from_a_replaced_question_cannot_select_a_previous_service; A.Product_references_require_the_current_question_workspace_and_ttl | PASS; pregunta nueva, TTL, tenant y replay |
| UX-14 vacío/degradación | N.Empty_response_plans_are_rejected_before_any_ai_or_outbound; Q.Unknown_global_quota_never_calls_providers_and_does_not_block_a_zero_token_menu | PASS; guard existente probado, menú local disponible con proveedor bloqueado |
| UX-15 catálogo ≠ stock | A.Catalog_corrections_use_verified_products_and_never_claim_physical_stock | PASS legacy/CTA; no inventario |
| Pausa por workspace | N.Pilot_commerce_is_enforced_by_workspace_without_changing_licenses_or_another_business; N.The_request_flow_blocks_only_the_opted_in_workspace_before_any_creation_or_handoff | PASS; ningún opt-in real aplicado |
| Invitación sólo con capacidad | N.An_informational_suggestion_cannot_offer_reservations_without_the_required_capability | PASS; licencia y servicio informativo |
| Gemini JSON/texto/chat | Q.Gemini_bounds_each_supported_mode_before_admission_without_forcing_json | PASS; adaptadores reales con HTTP interceptado; proveedor real NOT_RUN |
| Límites/reloj | Q.Account_limits_are_atomic_shared_durable_and_do_not_roll_back_windows | PASS RPM/RPD/TPM/TPD; SDK Firestore con fake |
| Share y cuenta pequeña | Q.One_workspace_cannot_spend_the_share_reserved_for_others_or_reset_it_by_recreating_the_service; Q.Conservative_small_account_share_can_admit_zero_requests_and_never_bypasses_its_limit | PASS; ninguna admisión puede ser posible. Calibración BLOCKED |
| Configuración desconocida/parcial | Q.Incomplete_quota_configuration_never_calls_http_or_claims_quota_exhaustion; Q.Unknown_quota_identity_unbounded_output_and_cancellation_cannot_authorize_a_request | PASS; sin HTTP/consumo, cancelación se propaga |
| Cooldown/fallback/consumo | Q.Retry_after_is_durable_and_an_unbudgeted_fallback_never_calls_its_http_transport; Q.Router_uses_only_a_funded_fallback_and_charges_each_actual_attempt_once; Q.A_failed_http_attempt_stays_charged_and_a_store_failure_fails_closed | PASS; 429/timeout/agotamiento, transporte rechazado no se invoca, no reembolso |

## Set congelado y gates

`NaturalExperienceEvaluation.json`: 35 reformulaciones en siete grupos de cinco (menu/professional/manage/start/capability/ambiguous/explore). SHA256 LF `141744B0EBDF3A4EC3A42CE4C8C1EBEB44D59BB4C62B89C30E6274ED4FBF1EF7`, comprobado por `Evaluation_inputs_are_frozen_separately_from_the_development_examples`. **35/35 PASS de mecanismo**. Entradas no editadas para corregir las precedencias que detectaron.

No es corpus ciego independiente: fue preparado por el implementador con interpretaciones inyectadas. Objetivo ≥95 % de comprensión real sigue NOT_RUN. La evaluación humana de diez conversaciones completas, claridad de primera lectura, preguntas repetidas y abandono también está NOT_RUN.

Gate vigente incluye las regresiones existentes de consentimiento/revisión, CAS, tenencia, idempotencia, control humano, recuperación y UnknownDelivery. Histórico G8 conserva corpus/evaluadores/hashes y razones. Conteos en RESULTADOS; no sumamos suites solapadas como cobertura distinta.

Gemini/Groq reales y WhatsApp de prueba: BLOCKED por autorización externa. Trazas del P0: PENDIENTES. Estos bloqueos no se sustituyen por pruebas sintéticas.
