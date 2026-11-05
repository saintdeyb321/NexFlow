# NexFlow — Pivote real a un agente autónomo por workspace (Agent V2)

**Fecha:** 2026-11-04 · **Rama auditada:** `feature/llm-first-agent` · **SHA:** `652a277cccce40a9fc18eec5da3d04c66b899888`.

## 1. Diagnóstico técnico, separando hechos de hipótesis

### Evidencia del chat real

- «Me duele el diente» recibió «No pude interpretar tu consulta en este momento…». Esa respuesta está codificada como fallback de `ProviderUnavailable` en `AiResponseOrchestrator`; no demuestra por sí misma si el problema fue timeout, proveedor, contrato JSON o configuración. Necesita telemetría por turno.
- «Me duele el diente» volvió a recibir «Quiero asegurarme de entenderte…»; el texto existe como respuesta fija para `RequiresClarification` en `TurnUnderstandingPipeline`.
- Ante errores ortográficos o preguntas sobre qué servicio elegir, repitió pantallas y preguntas. `DialoguePolicy.Classify` usa comprobaciones de términos y estado; el flujo legacy domina el siguiente paso.
- El chat terminó en una reserva confirmada con referencia. El nuevo Agent DEMO no tiene herramientas de creación y sus guardas bloquean la escritura comercial para el tenant protegido. Esto es fuerte indicio de **enrutamiento por Legacy**. No se puede certificar la ruta exacta sin correlación de mensaje/log; «Sí» se dio tras una confirmación explícita, así que esta muestra no prueba por sí sola falta de consentimiento. El P0 histórico previo permanece independiente.
- Se publicó una **imagen** con Agent DEMO, no se activó el nuevo motor como dueño de los turnos del negocio. La versión del binario no equivale a la ruta conversacional efectiva.

### Hallazgos de código en el SHA anterior

1. `ProcessIncomingMessageCommandHandler`: desvío a DEMO solo si `IAgentDemoPolicy.Protects(workspace)`; en caso contrario usa los flujos tradicionales.
2. `AgentHostProtection`: rol predeterminado `Legacy` si no se suministra señal DEMO; el nuevo agente requiere atestación de workspace/manifest.
3. `TurnUnderstandingPipeline`, `DialoguePolicy`, `AiResponseOrchestrator`: clasificador, slot resolvers, menús, recuperación y textos fijos toman decisiones por encima del modelo; la IA no es dueña de la conversación.
4. `LlmFirstShadowAgent`: modelo propone JSON; llamadas a `IAiRouter.ExecuteTaskAsync(IntentExtraction, ...)`, no un bucle general de herramientas nativas; máximo tres lecturas por turno y segundo llamado tras la lectura.
5. `AgentReadTools`: cinco lecturas `Services`, `Products`, `Locations`, `Profile`, `Hours`; no incluye de forma genérica inventario hotelero, políticas de check-in, preguntas particulares, órdenes, reservas o registro de acciones.
6. `AgentResponseAuthority.Prepare`: ignora `CandidateResponse` y genera `VerifiedText` mediante frases predefinidas. Es seguro en un prototipo, pero impide que el modelo converse con fluidez.
7. `AgentBookingDrafts`: únicamente prepara un borrador; el piloto no tiene comando para confirmar reservas; la transacción mostrada procede de otro camino.
8. `GroqAiProvider`: timeout predeterminado de 5 segundos, salida máxima de 1024 tokens, temperature=0 y contrato de extracción extenso; un 429/timeout/JSON incompatible puede activar fallback. El error real no ha sido observado en Oracle.

**Conclusión:** no basta con poner más texto en el prompt. Hace falta convertir a la IA en el dueño real de la decisión y la respuesta, y exponerle contexto verificable y herramientas relevantes. La autoridad comercial y la seguridad deben permanecer fuera del modelo.

## 2. Objetivo del producto

Una secretaria multitenant que interpreta español cotidiano, elipsis, errores de escritura y cambios de tema; identifica el workspace y su especialidad; consulta sus datos reales y relevantes; responde con prosa útil y natural; recuerda borradores y decisiones; ejecuta herramientas autorizadas para alcanzar objetivos; nunca inventa servicios/precios/cupos o acciones realizadas. Debe comportarse con criterio de negocio, no exigir palabras clave ni mostrar un formulario por defecto.

El core no codifica «si duele muela, entonces evaluación» como regla; el modelo puede **orientar** con opciones verificadas del catálogo, sin diagnosticar. La selección final y cualquier transacción requieren acción y consentimiento del cliente.

## 3. Arquitectura propuesta: un orquestador LLM dueño del turno

```text
Evolution webhook
  -> Verificar autenticidad / idempotencia / identidad workspace+remitente
  -> TenantRuntimeConfig (versionado: perfil, catálogo, módulos, políticas, lenguaje, tools)
  -> ConversationMemory (objetivo activo, resumen, últimas interacciones, borradores,
                         pregunta pendiente, referencias verificadas, estado de consentimiento)
  -> ContextBuilder (instrucciones estables + perfil relevante + retrieval por consulta
                     + lista de herramientas permitidas + hechos recientes con procedencia)
  -> ConversationalAgent (razona y decide; primer modelo competente como baseline)
       -> tool call(s) tipados -> autorización server-side -> repositorios/motores actuales
       -> resultados tipados y verificables -> modelo compone respuesta natural
  -> AnswerVerifier (hechos, permisos, promesas, contrato, límites)
  -> Journal CAS / outbound idempotente / respuesta WhatsApp
```

**Un agente principal**, no una red costosa de agentes sin necesidad. Bucle acotado (p. ej. 0–3 operaciones de lectura por turno; escrituras siempre mediante comando seguro). El número definitivo se calibra con pruebas reales. Modo observabilidad con trazas tipadas sin PII ni claves.

### 3.1 Contexto dinámico real por negocio

- `BusinessIdentity`: nombre, rubro, idioma, zona horaria, tono y alcance.
- `BusinessPolicies`: horarios, reglas de reservas/cancelación, disponibilidad de atención, condiciones, limitaciones, FAQs.
- `Capabilities`: catálogo de herramientas habilitadas según licencia, configuración y tipo de negocio.
- `RelevantKnowledge`: búsqueda por significado sobre servicios/productos/habitaciones/procedimientos/políticas, con versión, origen, estado de vigencia y workspace.
- `ConversationMemory`: resumen corto y últimos 4–8 intercambios, objetivos, slots confirmados, preguntas vigentes y entidades referenciadas.
- `Facts`: solo evidencia tipada devuelta por herramientas/lecturas actuales; no añadir fichas de otros tenants.

No enviar el catálogo completo en cada turno; recuperación semántica + caché por workspace/versión. Las instrucciones no autorizan acceso cruzado. Un negocio nuevo debería poder configurarse desde el panel sin editar prompts del código.

### 3.2 Capacidades por sector sin hardcodear conversación

Un núcleo común (`get_business_profile`, `search_knowledge`, `list_offerings`, `list_locations`, `get_hours`, `handoff`) y **capability packs** tipados:

- Clínica: `list_services`, `check_appointment_availability`, `prepare_appointment`, `get_booking_terms`.
- Hotel: `search_room_types`, `check_room_inventory`, `get_stay_rules`, `prepare_stay`.
- Tienda: `search_products`, `get_product_details`, `get_stock_if_authoritative`, `prepare_order`.

Los *adaptadores de dominio* sí deben existir y validar reglas reales del sector. Lo que se evita es programar cien expresiones para adivinar qué frase significa cada intención. Sin motor de inventario de habitaciones no se promete disponibilidad de habitaciones.

### 3.3 Respuesta realmente natural sin alucinaciones

El modelo redacta en su idioma/tono usando hechos citados internamente mediante IDs. El backend permite prosa libre **solo cuando** las afirmaciones verificables están fundamentadas; no inserta siempre una plantilla de menú. Rechazar o regenerar una vez si inventa precio, servicio, stock, horario, disponibilidad, cita confirmada o dato personal; si no puede verificarse, pedir aclaración pertinente o derivar a humano. La respuesta transaccional final se compone del recibo real del motor.

No exigir que el texto del cliente coincida literalmente con la denominación comercial: resolver entidad por semántica + candidatos del workspace, conservando aclaración si hay varias opciones plausibles. Los hechos vienen del repositorio, no del modelo.

### 3.4 Herramientas de escritura con seguridad de verdad

`prepare_*` crea/edita únicamente borradores reversibles. `confirm_*` ejecuta después de validar autorización por tenant, estado/version, disponibilidad, precio/alcance, identidad y consentimiento explícito referido al resumen vigente. El modelo no elige el workspaceId, no firma consentimiento ni crea un recibo ficticio. Conservar idempotencia, CAS, outbox, handoff, UnknownDelivery, auditoría y P0 como puerta independiente. Primero habilitar escrituras en entornos sintéticos aislados; jamás reutilizar la API legacy del workspace comercial como prueba inicial.

### 3.5 Manejo del proveedor y fallos

Registrar por mensaje: motor seleccionado (Legacy/AgentV2), modelo/proveedor real, task, HTTP attempts, errores (`Timeout`, `429`, `401`, `InvalidJson`, `ModelUnavailable`, `ReadUnavailable`), herramientas propuestas/ejecutadas, duración, motivo de validación y si hubo fallback. No registrar contenido, nombres, teléfonos ni API keys en métricas públicas.

Para el modelo nuevo, usar soporte real de tool/function calls si el proveedor lo permite; si no, contratos JSON estrictos y feedback de herramientas equivalente. No responder «no pude interpretar» por un fallo de transporte como si el cliente se hubiera expresado mal. Un reintento de proveedor o fallback solo dentro de un presupuesto y sin repetir acciones externas inciertas.

## 4. Migración desde el código actual

- Conservar **Legacy** para tenants no migrados; nunca mezclar ambos motores para un mismo turno.
- Introducir selector versionado `ConversationEngine=Legacy|AgentV2` por workspace, revisado y protegido en ingreso, con aislamiento de cola/instancia. Migración solo en tenant sintético; no activar sobre clientes reales ni reaprovechar el workspace con P0.
- Reutilizar `IBusinessReadTools`, `IEntitlementService`, motores de reservas/pedidos, `FirestoreAgentTurnJournal`, `OutboundMessageService`, `AgentDemoPolicy`, guards, recovery solo de datos; retirar `DialoguePolicy.Classify` y los flujos de plantillas de la ruta AgentV2.
- Reemplazar `AgentResponseAuthority` para AgentV2 por composiciones naturales verificadas, preservando confirmaciones transaccionales respaldadas por recibos.
- Sustituir `AgentReadKind` fijo por un `ToolRegistry` con definiciones JSON Schema por capability pack, autorización y ejecución servidas por backend; no permitir tools inventadas.
- Promover AgentV2 de «piloto read-only» a runtime comercial **solo cuando** exista motor transaccional seguro certificado aparte. No confundir piloto con venta.

## 5. Experimentos con valor (no 2000 mocks que eluden NLU)

| Fase | Criterio de salida |
|---|---|
| A — Ruteo | Demostrar por trazas que cada turno del workspace sintético pasa por AgentV2 y nunca vuelve silenciosamente a Legacy; guardar SHA e identidad por turno. |
| B — Contexto | Dos workspaces sintéticos distintos, odontología/hotel: misma IA usa herramientas distintas, nunca mezcla hechos, no supone datos no disponibles. |
| C — Modelo real | Prueba controlada con proveedor real autorizado: 10–20 conversaciones cotidianas, errores ortográficos, selección semántica, pregunta lateral, corrección y handoff. Medir resultado, tokens, latencia, fallos. |
| D — Operaciones | Simulación completa de borradores, consentimientos y ejecución de herramientas comerciales sobre datos aislados; cero escritura sin consentimiento, replay y concurrencia protegidos. |
| E — WhatsApp | Smoke de extremo a extremo: Evolution -> AgentV2 -> lectura -> respuesta verificable -> salida. Ningún cliente real, nada de P0 comercial. |

No valorar éxito por `test passed` exclusivamente: evaluar transcripciones observadas, groundedness, recuperación de contexto y criterio humano. Baseline inicial con modelo suficientemente capaz; optimizar coste después de demostrar calidad.

### Escenario de aceptación odontológico

Usuario: «Me duele el diente».
Agente: reconoce necesidad, consulta catálogo sin diagnosticar, informa que existe «Evaluación odontológica» y que puede orientar la atención; precio/sede solo si herramienta los devuelve; pregunta si desea más detalles o preparar una cita.
Usuario: «Estoy en Concepción, ¿qué elijo?».
Agente: sabe que es la sede, usa catálogo de esa sede, sugiere evaluación como atención inicial, sin adjudicar diagnóstico ni reservar.
Usuario: «Quieor la evaluación ofontologixa».
Agente: vincula de manera robusta con entidad verificada, sin repetir cinco servicios si es inequívoco; si hay dos, pregunta cuál.
Usuario: «Antes dime cuánto cuesta».
Agente: consulta precio de esa oferta para esa sede, responde brevemente sin perder selección.
Usuario: «Mañana por la tarde».
Agente: interpreta fecha/franja en zona horaria del negocio, comprueba slots con motor real, no asegura disponibilidad sin respuesta.

### Escenario hotel

Usuario: «Llegamos el viernes dos adultos y un niño, ¿qué habitaciones hay?».
Agente: consulta tipos y disponibilidad si existe adaptador de hotel, pregunta edad del niño si política lo requiere, explica políticas verificadas sin inventar habitaciones, tarifas ni check-in.

## 6. Restricciones para implementación

- No modificar `main`, no reescribir el histórico del repositorio.
- No modificar la configuración del workspace comercial de Oracle mientras sea Legacy ni reutilizarlo para probar AgentV2.
- No confundir build/CI con NLU real.
- No añadir reglas «diente => evaluación» en C# para superar esta conversación.
- No tratar un «Sí» aislado como consentimiento salvo que esté vinculado a una operación, resumen, importe/fecha y versión vigentes.
- Preservar `G8` y tests históricos, pero añadir evaluación real de calidad, trazas y seguridad.
- No despliegues ni gasto externo sin aprobación explícita del propietario por entorno/destino/presupuesto.

## 7. Resultado mínimo que aceptaríamos como «secretaria inteligente»

Un workspace odontológico y uno hotelero sintéticos hacen conversaciones de extremo a extremo con datos diferentes usando el mismo orquestador. El agente recibe contexto pertinente, elige herramientas autorizadas, responde con naturalidad, maneja lenguaje imperfecto y cambios de tema, conserva memoria, jamás inventa hechos comerciales y no escribe sin consentimiento versionado. El observador puede señalar qué modelo se llamó y qué herramientas ejecutó. Cero rutas al legacy dentro de AgentV2.

**Esta es una nueva dirección de producto, no la promesa de que el piloto read-only existente ya cumple lo anterior.**
