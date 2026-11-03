# NexFlow — Pivote arquitectónico: agente conversacional LLM-first con herramientas seguras

**Fecha:** 2026-11-03 (Perú).  
**Repositorio:** https://github.com/saintdeyb321/NexFlow  
**Base examinada:** `5f5ff850e31df3fa73cb2c7f02f65ba6081173c7` (verificar HEAD en cada sesión).  
**Estado:** propuesta técnica, **NO implementada**, **NO autorización de despliegue**.  
**Propósito:** sustituir la experiencia «bot de reglas con IA auxiliar» por un asistente que entiende, conversa, orienta y utiliza herramientas verificadas. No construir un árbol infinito de reglas.

## 1. Por qué este pivote es necesario

Transcripción real, 2026-11-03:

- «Hola quiero sacarme el diente» → «No identifiqué un producto o servicio inequívoco…»
- «Quiero extraerme el diente» → «No pude interpretar tu consulta…»
- «Quiero reservar» → el flujo inicia sede correctamente.
- «Quiero hacerme una consulta» durante reserva → vuelve a exigir sede, sin atender interrupción.
- «Quiero cancelar la reserva» → el bot pregunta si habla de borrador o cita confirmada.
- «A la que estamos preparando» → vuelve a mostrar sedes. Se pierde la decisión conversacional que originó la aclaración.

**Hallazgo verificable en código:** `AiResponseOrchestrator.RespondCoreAsync` invoca `ConversationRecoveryService.RecoverAsync` antes de `TurnUnderstandingPipeline.PrepareAsync` y envía directamente cualquier `recovery.Response`. En `ConversationRecoveryService`, dentro del manejo de `PendingRecoveryAction == "RESERVATION_SCOPE"`, `ReservationOperationClassifier.ReferenceScope("A la que estamos preparando")` devuelve `Draft`, pero la rama devuelve `AiInterpretation { Intent = Reservation }` y borra `PendingRecoveryAction`; esa interpretación no expresa `CancelDraft`. El orquestador puede retomar la colección de sede. Este es un defecto de **estado/acto conversacional**, no solamente un problema del modelo.

**Otros mecanismos limitantes confirmados:**

- `DialoguePolicy.Classify` asigna `RequestedData` por defecto durante reservas; el estado del formulario gana demasiada prioridad.
- `TurnUnderstandingPipeline` tiene rutas locales que responden antes del modelo; el modelo no es siempre dueño de la intención conversacional.
- `CommerceTurnResolver` pide coincidencia única y evidencia estricta. Es correcto para permitir una operación, pero demasiado restrictivo si se usa para impedir **orientación informativa** ante una necesidad aproximada.
- `AiInterpreter` exige JSON de extracción complejo y evidencia literal; la interpretación puede fallar por formato, timeout o validación sin que el cliente reciba ayuda.
- `ResponseComposer.EnableAi` está apagado y la composición disponible es limitada; la mayoría de las frases visibles sigue siendo determinista.

**No confundir causas:** «No pude interpretar» puede significar timeout, error HTTP, formato JSON inválido, rechazo del validador o fallo del proveedor. Sin trazas sanitizadas no se puede atribuir a la inteligencia de Groq/Gemini. Registrar `AiFailureKind`, etapa y ruta de fallback, sin teléfonos ni textos privados.

## 2. Decisión de producto

Prioridad del piloto:

1. Servicios: entender necesidades, localizar servicios pertinentes sin diagnosticar y explicar información del negocio.
2. Catálogo de productos: informar sobre productos reales; no inventar stock ni registrar pedidos.
3. Reservas: preparar un borrador y orientar durante sede/servicio/fecha/hora/identidad; **sin autorizar nuevas confirmaciones reales** mientras el P0 histórico siga sin resolver o sin aislamiento operacional aprobado.
4. Handoff humano y explicaciones de límites reales.

Pedidos y solicitudes comerciales: **fuera del piloto**. Usar opt-in por workspace ya existente `AssistantRecovery.InformationalCommerceWorkspaces`, sin alterar licencias ni históricos. No activar globalmente.

Meta de experiencia: cualquier cliente puede escribir en sus palabras, con errores y cambios de tema. El asistente entiende qué necesita, responde con frases simples y pregunta lo mínimo imprescindible. No requiere aprender comandos o flujos rígidos.

## 3. Arquitectura objetivo

```text
WhatsApp / Evolution (inbound, deduplicación, aislamiento tenant)
    → Context Snapshot
        [últimos turnos relevantes + objetivo en curso + pregunta pendiente
         + estado validado del borrador + capacidades del workspace]
    → CONVERSATIONAL AGENT (LLM-first)
        [comprende el mensaje; resuelve anáforas, cambio de tema, interrupciones,
         reparación, intención de cancelación, siguiente paso y forma de responder]
        ↕
      SAFE BUSINESS TOOLS (lectura por defecto)
        search_services, get_service_details, search_products,
        list_locations, get_open_hours, read_booking_draft,
        check_slots, stage_booking_change, propose_discard_draft,
        request_human_handoff
        ↕
    BUSINESS AUTHORITY / POLICY GATE
        [ACL, tenant, facts, validez de IDs, CAS, pregunta vigente, consent,
         idempotencia, cuota, caducidad y estados de entrega]
    → NATURAL RESPONSE (LLM sobre hechos verificables; validación de salida)
    → OutboundMessageService / Evolution
```

### Propiedad de responsabilidades

- **LLM decide intención comunicativa, cómo orientar y cuándo llamar herramientas.** Puede contestar una interrupción informativa sin descartar el borrador. Puede reconocer «sacarme un diente» como consulta sobre extracción dental, sin diagnosticar ni suponer que el negocio la ofrece.
- **Backend decide lo que existe y lo que se puede hacer.** Herramientas verifican servicios, sedes, fechas, horarios y permisos; no se admite ID, precio ni disponibilidad inventados por el modelo.
- **Motor de reservas existente mantiene exclusividad transaccional.** Preparación, revalidación y confirmación siguen en `ReservationEngine`, `BookingFlow`, almacenes e idempotencia existentes.
- **Una sola voz conversacional de cara al cliente.** No introducir de entrada 3–5 agentes, framework nuevo, workflows distribuidos ni una refactorización de backend; para Free Tier, preferir un agente + herramientas limitadas.

### Semántica de turnos, no árbol de frases

Un estado resumido, tipado y bajo control del servidor, por ejemplo:

```json
{
  "activeGoal": "booking_draft",
  "activeStep": "choose_location",
  "pendingDialogueAct": {
    "type": "disambiguate_cancel_target",
    "sourceMessageId": "...",
    "expiresAt": "...",
    "options": ["current_draft", "registered_booking"]
  },
  "userPreferences": {},
  "verifiedDraft": {},
  "lastUserQuestion": "Quiero cancelar la reserva"
}
```

La forma JSON es **esquemática**: no crear nueva fuente de verdad ni duplicar `ConversationContextDto`; aprovechar la versión CAS y `ConversationQuestion` vigentes. El agente lee un **snapshot autorizado**, no escrituras manuales sobre estado.

El resultado semántico debe conservar **la relación entre pregunta y respuesta**, ejemplo:

- Cliente: «Quiero cancelar la reserva» → `request_cancel_draft_or_registered` (requiere alcance).
- Bot: «¿La que estamos preparando o una cita confirmada?» → guarda `pendingDialogueAct=disambiguate_cancel_target`.
- Cliente: «A la que estamos preparando» → `resolve_reference(current_draft)`; **no** `Intent=Reservation` a secas.
- Backend decide si ya existe autorización explícita suficiente para descartar un borrador o si exige un «Sí» ligado a pregunta vigente. En ningún caso se vuelve automáticamente a solicitar sede ni se cancela una cita registrada.

### Guion de ejemplo — comportamiento objetivo, no salida implementada

- Cliente: «Hola quiero sacarme el diente».
- Bot: «¡Hola! ¿Quieres información sobre una extracción dental? Puedo revisar si el negocio ofrece ese servicio y explicarte cómo reservar una evaluación. ¿Qué prefieres?»
- Cliente: «Quiero reservar».
- Bot: «Claro, te ayudo a preparar una cita. ¿Quieres una evaluación primero o ya sabes qué servicio necesitas?». La pregunta exacta depende del catálogo; no inventar disponibilidad ni tratamiento.
- Cliente: «Quiero hacerme una consulta».
- Bot: «Claro. Si quieres consultar primero, no hay problema: tu reserva queda en preparación. ¿Quieres saber qué incluye la evaluación o prefieres cambiar el servicio?». No exige sede como única salida.
- Cliente: «Quiero cancelar la reserva».
- Bot: «¿Te refieres a la cita que estamos preparando o a una cita que ya está confirmada?».
- Cliente: «A la que estamos preparando».
- Bot: «Entendido: la reserva que estamos preparando. No tocaré citas confirmadas. ¿Quieres descartar ese borrador?». En caso de respuesta «Sí» vigente, aplicar solo descarte seguro y decir que ya no se está preparando. Si el contrato existente permite aceptar el par cancelación+alcance como autorización completa, documentar y probar explícitamente; no asumirlo.

### Política de lenguaje

- Conversación natural, cercana, no infantilizante; español sencillo de Perú; 1–3 oraciones si bastan.
- Responder la pregunta real **antes** de exigir el próximo slot del formulario.
- Un único siguiente paso relevante, no menú genérico después de cada mensaje.
- No decir «producto o servicio inequívoco», «intención», «borrador», «flujo», «slot» salvo cuando una aclaración comprensible requiera hablar de una reserva en preparación.
- Sin diagnósticos odontológicos, promesas de extracción, profesional concreto, stock ni disponibilidad no verificados.
- Si no hay el servicio exacto: informar con honestidad y ofrecer servicios relacionados comprobados o asesor; no cerrar la conversación con un error técnico.
- Frustración y lenguaje molesto: reconocer brevemente el problema, corregir el rumbo, no repetir la pregunta fallida.

## 4. Qué **no** hacer

- NO agregar 50 regex nuevas para extracción/consulta/cancelación.
- NO permitir que el modelo escriba directamente en PG/Firestore/Redis.
- NO eliminar protecciones de consentimiento, idempotencia, UnknownDelivery, CAS ni tenant.
- NO invocar dos modelos en cada turno como configuración por defecto. Primero optimizar la única llamada realmente necesaria.
- NO asumir que más tokens corrigen un estado mal modelado: la cancelación falló por perder la decisión pendiente.
- NO activar `EnableContextualFocus`, `ResponseComposition.EnableAi`, `AiQuota.Enabled` ni comercializar reservas reales automáticamente.
- NO afirmar «nivel AxionLabs» sin prueba ciega y comparación de conversaciones.

## 5. Diseño de adaptador incremental (sin big bang)

**Fase A — Reproducir y observar:**

1. Guardar como prueba de regresión los ocho mensajes textuales del incidente en un fixture sanitario; comprobar que la decisión de cancelación no se pierde y que no hay escrituras comerciales.
2. Obtener trazas sanitizadas del mensaje «Quiero extraerme el diente»: resultado `AiFailureKind`, proveedor, HTTP outcome, latencia, `JsonException`/invalid evidence y prompt size. No leer ni publicar credenciales, teléfonos, payloads de cliente.
3. Inspeccionar el SHA realmente desplegado mediante `docker inspect nexflow_api --format '{{.Config.Image}}'` antes de correlacionar trazas.

**Fase B — Prototipo de agente conversacional en modo shadow:**

1. Introducir abstracciones pequeñas y aisladas: `IConversationalAgent`, `AgentTurnContext`, `AgentToolCall`, `AgentTurnResult`; no modificar motores de negocio.
2. El **agente recibe todos los mensajes abiertos**, incluida la aclaración de la cancelación; solo los ACK/replay, permisos y autorizaciones de seguridad pueden preemptar su interpretación. No usar `DialoguePolicy` para secuestrar conversaciones abiertas antes del modelo.
3. Implementar tools **read-only** primero y un adaptador que capture resultado natural verificable. Aprovechar `IBusinessReadTools`, `IOfferingService`, `IEntitlementService`, `IReservationEngine`/lecturas de reserva existentes.
4. Shadow para un workspace sintético: la respuesta/decisión del agente se guarda como evaluación sin enviarse al WhatsApp ni ejecutar efectos. Comparar con el bot anterior.
5. No asumir soporte nativo de tool calling en todos los modelos: usar JSON tipado con una instrucción de herramienta y validación server-side donde haga falta. Mantener el stack .NET y `IAiRouter` inicialmente.

**Fase C — Un solo propietario del diálogo en DEMO:**

1. Activar únicamente por **workspace allowlist** después de que shadow pase.
2. Enviar la respuesta natural del agente, con facts anclados a tool outputs y validación de salida (IDs, precios, disponibilidad, estado).
3. Control de turnos y de llamadas: 1 llamada LLM para consulta sin herramienta; para consulta con tools, a lo sumo 1 decisión + 1 composición, con cache factual autorizada cuando proceda; límites adaptativos por workspace y proveedor.
4. Interrupciones: responder la nueva consulta sin perder el estado, o cambiar objetivo solo con intención explícita; guardar `pendingDialogueAct` y resolver referencias anafóricas.
5. Reserva: permitir solo preparación y comprobaciones sin commit en piloto. P0 histórico requiere solución/aislamiento probado y aprobación separada antes de habilitar confirmaciones reales.

**Fase D — Aceptación:**

- Validar 40–60 conversaciones completas con casos no escritos por el implementador, nuevas variantes y varios negocios sintéticos; incluir mensajes con ruido, contradicciones, frustración, cancelaciones, tareas compuestas, interrupciones y preguntas imprevistas.
- Establecer métricas por versión: intención correcta **medida con modelo real**, continuidad, finalización de tarea informativa, preguntas innecesarias, claridad humana, alucinaciones, fallos externos, latencia p50/p95 y tokens p50/p95.
- Objetivos iniciales propuestos (no alcanzados): ≥90 % de intención comunicativa correcta en conjunto ciego, ≥95 % de resolución de referencias/cancelaciones sin acción errónea, ≥4/5 en claridad de respuestas, 0 operaciones comerciales no consentidas, 0 fugas tenant, 0 disponibilidad/stock inventados.
- Pruebas mock/synthetic y test de proveedor real **se reportan por separado**; no declarar PASS de NLU real desde JSON inyectado.
- Cuotas y costos efectivos de Groq/Gemini FREE siguen UNKNOWN; configurar solo después de verificar cuenta/modelos/TPM/TPD/RPM/RPD. Ensayos de proveedores reales solo con consentimiento del propietario, presupuesto de llamadas y datos enteramente ficticios.

## 6. Archivos de origen prioritarios

| Componente | Ruta relativa al repo |
|---|---|
| Orquestador y bypass de recuperación | `backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/AiResponseOrchestrator.cs` |
| Recuperación / cancelación con alcance | `backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/ConversationRecoveryService.cs` |
| Clasificador de alcance de cita | `backend/NexFlow.Application/Features/Automation/Dialogue/Policy/ReservationOperationClassifier.cs` |
| Política que fuerza `RequestedData` | `backend/NexFlow.Application/Features/Automation/Dialogue/Policy/DialoguePolicy.cs` |
| Interpretación semántica actual | `backend/NexFlow.Application/Features/Automation/Dialogue/Understanding/TurnUnderstandingPipeline.cs` |
| JSON NLU del proveedor | `backend/NexFlow.Application/Features/AI/Interpretation/AiInterpreter.cs` |
| Router y proveedores | `backend/NexFlow.Application/Features/AI/Router/AiRouter.cs`, `backend/NexFlow.Infrastructure/Engines/AI/{GroqAiProvider,GeminiAiProvider}.cs` |
| Validación de evidencia | `backend/NexFlow.Application/Features/Automation/Dialogue/Understanding/AiEvidenceValidator.cs` |
| Lector catálogo y sugerencias | `backend/NexFlow.Application/Features/Automation/Dialogue/Understanding/CommerceTurnResolver.cs` |
| Respuestas informativas | `backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/Flows/ConversationFlows.cs`, `.../Dialogue/Composition/{OfferingResponseFormatter,ResponseComposer,SecretaryPresentation}.cs` |
| Estado y pregunta autorizada | `backend/NexFlow.Application/Abstractions/Cache/IConversationCache.cs`, `backend/NexFlow.Application/Features/Automation/Dialogue/Contracts/ConversationQuestion.cs` |
| Preparación y motor reservas | `backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/Flows/ConversationFlows.cs`, `backend/NexFlow.Application/Engines/Reservation/ReservationEngine.cs` |
| Config por workspace | `backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/AssistantRecoveryOptions.cs` |
| Quotas, tokens | `backend/NexFlow.Infrastructure/Persistence/Firestore/FirestoreAiRequestAdmission.cs`, `backend/NexFlow.Application/Features/AI/Router/{AiQuotaOptions,AiTelemetry}.cs` |
| Tests existentes | `backend/NexFlow.Tests/Concierge/{NaturalExperienceTests,AgentStabilizationTests}.cs`, `backend/NexFlow.Tests/AiGlobalAdmissionTests.cs` |
| Evidencia previa | `docs/auditorias/experiencia-conversacional-free-tier/2026-11-02/`, `AGENTS.md` |

## 7. Seguridad y rollout

- HEAD GitHub certificado: `5f5ff850e31df3fa73cb2c7f02f65ba6081173c7`; los cuatro workflows de ese SHA pasaron el 2026-11-03, pero no demostraron NLU/UX reales.
- No inferir desde GitHub el SHA **efectivamente desplegado**: comprobar Docker en Oracle.
- Mantener producción/Oracle intactos al prototipar; nunca desplegar código desde un experimento sin aprobación, pruebas y rollback.
- P0 histórico de reserva inesperada **no resuelto**. No transacciones reales, no permitir tool `confirm_reservation` en el entorno experimental; la solución comercial exige incidente cerrado/aislamiento probado.
- No desactivar todos los seguros por liberar creatividad. Colocar control de seguridad **en tools**, no usarlo como sustituto de conversación.
- Mantener trazabilidad: `messageId`/`workspaceId`/`conversationId`/`StateVersion`, finalidad del tool, resultado, tokens, fallback, latencia y motivo de rechazo. No almacenar contenido privado en logs generales.

## 8. Instrucción para Codex, primera iteración

> Trabaja en `C:\Proyectos\NexFlow`. Lee `AGENTS.md` y este archivo. Prioriza **Fase A y un prototipo mínimo de Fase B**, no todo el proyecto de golpe. La arquitectura actual es un bot de reglas que antepone recuperación/diálogo local a la interpretación; no resuelvas esto añadiendo expresiones regulares ni frases para cada síntoma. Reproduce exactamente la transcripción del 2026-11-03 y confirma mediante tests la pérdida de `cancel_draft` cuando el usuario aclara «A la que estamos preparando». Investiga por qué la consulta sobre extracción dental termina en aclaración y por qué el proveedor devuelve `ProviderUnavailable` (si no hay logs, marca causa UNKNOWN y agrega telemetría sanitizada). Diseña `IConversationalAgent` + contexto de turno + herramientas de solo lectura en modo shadow, manteniendo `IReservationEngine`, validación, consentimiento y CAS como autoridad única. Comienza con una prueba conversacional de 8 turnos con intents no inyectados si es posible en evaluación offline del agente; si el proveedor es fake, no la presentes como comprensión real. Mantén defaults y rutas existentes intactos fuera de un workspace sintético. No commits, push, llamadas externas reales, cambios a Oracle, migraciones ni confirmaciones reales. Entrega tests rojos/verdes, trazas sin PII, diff y próximos pasos, y detente para revisión.

## 9. Referencias de diseño (orientativas, no dependencias obligatorias)

- https://developers.openai.com/tracks/building-agents — agente, herramientas, guardrails y tracing.
- https://developers.openai.com/api/docs/guides/agents/orchestration — agente de cara al cliente con especialistas como tools.
- https://learn.microsoft.com/en-us/agent-framework/overview/agent-framework-overview — agentes para tareas abiertas; workflows para procesos definidos.

**Fin.** Esta especificación define el objetivo, no afirma que el nuevo agente exista ni esté certificado.
