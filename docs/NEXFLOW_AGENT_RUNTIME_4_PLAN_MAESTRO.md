# NexFlow — Plan maestro de evolución del agente conversacional

**Documento:** `NEXFLOW_AGENT_RUNTIME_4_PLAN_MAESTRO.md`  
**Versión documental:** 1.0 · 25/10/2026  
**Estado:** propuesta técnica para implementación incremental; no equivale a código desarrollado ni a autorización de despliegue.  
**Repositorio:** `https://github.com/saintdeyb321/NexFlow`  
**Ruta local habitual:** `C:\Proyectos\NexFlow`  
**Base de código verificada en GitHub al redactar:** `0da524d6e840ce6aeec77950d8e1f82b42c53e16` (`main`). **Antes de trabajar, comparar la identidad con el checkout local; no presuponer que este SHA sigue siendo HEAD.**

## 0. Decisión ejecutiva

**Evolucionar el asistente actual a un runtime de agente multiempresa compartido, con configuración lógica por workspace, comprensión semántica contextual y herramientas tipadas conectadas a los motores existentes.** No crear un bot compilado, contenedor o modelo entrenado por cliente. Mantener `AiRouter` y su abstracción de proveedores, el backend .NET y las garantías de persistencia, transacciones, autorización y entrega.

La interfaz debe adaptarse al lenguaje del cliente, incluidos errores de ortografía, referencias implícitas y cambios de idea. La seguridad comercial **no** depende de que un LLM acierte: el software verifica hechos, permisos, estado, consentimiento y efectos persistentes.

**No perseguimos infalibilidad lingüística absoluta.** Buscamos que el sistema no se rompa ante mensajes inusuales: debe preguntar, recuperarse o transferir a una persona, sin perder datos válidos, mezclar negocios ni ejecutar operaciones indebidas.

### Alcance de producto — definición innegociable

NexFlow **automatiza la comunicación** entre un negocio y sus clientes mediante WhatsApp y un panel web de conversaciones. Incluye:

- **Información:** perfil público autorizado, FAQ, sedes, horarios, servicios, productos, precios vigentes, catálogos y documentos publicados.
- **Reservas de servicios:** consulta de disponibilidad, preparación del borrador y creación con consentimiento explícito. Gestión de reservas **ya registradas** solo en la medida en que existan controles aprobados de identidad/titularidad; de lo contrario, transferencia a humano.
- **Solicitudes de pedido:** el cliente selecciona productos/cantidades y envía una **solicitud**; NexFlow registra la lista y notifica al dueño o encargado. El negocio verifica disponibilidad, responde y coordina la recogida/atención. **El bot nunca promete stock, precio final negociado, aprobación, preparación ni recogida confirmada sin datos/eventos que lo acrediten.**
- **Atención humana:** personal autorizado puede intervenir y devolver una conversación a modo automático mediante controles existentes.

**Fuera del alcance:** pasarela de pagos, cobro, conciliación, facturación, POS, promesas de stock, logística autónoma y gestión de empleados durante estas fases. No convertir «pedido enviado» en «pedido aceptado» o «pagado».

## 1. Diagnóstico y fronteras actuales

La base revisada ya separa funciones comerciales de la interpretación por lenguaje, pero varias capas toman decisiones lingüísticas sobre el mismo turno.

| Existente | Responsabilidad que se conserva | Deuda a resolver |
| --- | --- | --- |
| `AiRouter`, `IAiRouter`, Groq/Gemini | Selección y fallback de proveedores | Identificar fallos reales, JSON inválido, presupuesto, coste, observabilidad y límites de llamadas |
| `AiInterpreter`, `TurnUnderstandingPipeline` | Interpretación y evidencia semántica | Autoridad única sobre intención/acto y comprensión de referencias contextualizadas |
| `DialoguePolicy`, `ConversationRecoveryService` | Reglas de seguridad, recuperación y menús vigentes | Evitar que intercepten o contradigan interpretaciones seguras; reducir árboles de sinónimos y mensajes rígidos |
| `ConversationContextDto`, Redis/Firestore | Estado/versionado de conversación | Persistir referencias/pregunta pendiente sin duplicar estados o exponer memoria privada |
| `BookingFlow`, `IReservationEngine` | Disponibilidad, borrador, consentimiento, reserva durable | Preferencia horaria natural y ruta de herramientas sin el menú obligatorio |
| `OrderFlow`, `CommerceTurnResolver` | Lista de productos, solicitud comercial y notificación | Separar consulta de productos de iniciar un pedido y mantener coherencia entre turnos |
| `KnowledgeService`, perfiles, FAQ, catálogos | Fuentes oficiales del negocio | Consulta selectiva, contexto por workspace y campos públicos realmente autorizados |
| `ResponsePlan`, `ResponseComposer` | Hechos verificados y respuesta | Dar naturalidad sin inventar datos ni sustituir los hechos con una salida libre |
| Inbound/FIFO/Outbox/Evolution | Ingreso, orden e idempotencia de entrega | No modificar garantías por introducir llamadas a herramientas |

**Evidencia de código revisada:** `AiRouter.cs`, `ResponsePlan.cs`, `TurnUnderstanding.cs`, `ConversationFlows.cs`, `OrderFlow.cs`, `KnowledgeService.cs` y documentos existentes `NexFlow_Concierge_3_Plan_Maestro.md`, `docs/concierge-3/01_MAPA_DATOS_Y_ESTADO.md` y `05_1_INTELIGENCIA_CONVERSACIONAL.md`. Lo descrito como objetivo no se confunde con lo que ya está implementado.

### Principios de arquitectura

1. **Un agente lógico por workspace, un runtime compartido.** Sus datos, configuración, conversaciones, presupuestos y herramientas efectivas se aíslan por tenant.
2. **El LLM interpreta y propone; el backend valida y ejecuta.** Una salida del modelo jamás es un ID, precio, horario ni consentimiento autoritativo.
3. **Herramientas antes que prompts gigantes.** Consultas puntuales a fuentes oficiales; no colocar todos los catálogos, secretos o historiales en el prompt.
4. **No duplicar motores.** Las herramientas son fachadas sobre `KnowledgeService`, `IReservationEngine`, `BookingFlow`, `OrderFlow`, etc.; no implementan otra reserva o pedido.
5. **El contexto es un snapshot mínimo y versionado.** Una conversación no es memoria duradera de reservas; PostgreSQL mantiene las operaciones confirmadas, Firestore el contexto autoritativo y Redis una caché recuperable.
6. **La siguiente pregunta forma parte del plan de diálogo.** «Sí», «esa», «mañana», «la otra sede» y «ya te lo dije» se interpretan respecto a un antecedente comprobado; no a cualquier menú antiguo.
7. **Una consulta no es una acción.** Consultar disponibilidad no selecciona, seleccionar no confirma, confirmar no omite las verificaciones.
8. **Coste bajo observación.** La ruta local inequívoca usa 0 tokens; la ruta semántica consume un presupuesto acotado por turno; no se cobra coste LLM por cada slot o frase de cortesía.
9. **Fail closed + conversación útil.** Si falla IA, permiso o dato: no mentir, no repetir la misma pregunta indefinidamente, no comprometer transacciones; brindar alternativa o asesor.
10. **Migración gradual, reversible y medible.** No activar todos los workspaces ni eliminar las rutas existentes antes de medir el reemplazo.

## 2. Arquitectura objetivo y orden de procesamiento

```text
WhatsApp / Evolution
      |
Inbound autenticado + deduplicación + FIFO por conversación
      |
Workspace autorizado + licencias + modo humano + snapshot versionado
      |
Contexto compacto: objetivo, última pregunta, referencias, slots y operación
      |
+---- Comprensión (turno único) ------------------------------------+
| Ruta local inequívoca -> sin IA                                   |
| Si ambigüedad lingüística -> AiInterpreter mediante AiRouter      |
| Salida estructurada + evidencia literal + validación independiente|
+------------------------------------------------------------------+
      |
Coordinación de diálogo sobre el estado existente
      |
Plan tipado: consulta / aclaración / propuesta / confirmación / humano
      |
Herramientas permitidas por licencia, datos y capacidades
   |                |                   |
Knowledge         Reservation           Order
(perfil/FAQ/etc.) (agenda/servicios)     (solicitud, NO pago)
   +----------------+-------------------+
      |
Resultado factual/operacional validado -> ResponsePlan
      |
Realización conversacional con protección de hechos + fallback
      |
Revisión de state version, handoff, delivery durable, Evolution
```

### Contratos a especificar antes de cambiar código

**`AgentContext` efímero:** workspace de confianza, módulos vigentes, zona horaria del negocio, capacidad autorizada, objetivo/step, última pregunta + `QuestionId`, referencias verificadas, campos relevantes, estado del borrador y `StateVersion`. No duplicar `ConversationContextDto` como segunda fuente persistida; hacer proyección del estado existente.

**`TurnUnderstanding` tipado:** acto (`Ask`, `Answer`, `Select`, `Correct`, `Resume`, `Decline`, `ExplicitConfirm`, `RequestHuman`), intención comercial (`Information`, `Reservation`, `OrderRequest`, etc.), entidades y slots con evidencia literal, ambigüedad, incertidumbre y referencia contextual. La taxonomía final debe mapear a enums/contratos ya existentes cuando sea posible, sin introducir campos JSON libres que actúen como autoridad.

**`AgentDecision` interno:** siguiente acción autorizada: responder desde hechos, consultar herramienta de lectura, actualizar borrador con CAS, solicitar dato faltante, solicitar confirmación comercial o transferir a humano. `AiRouter` no decide autorización.

**`AgentToolResult` estructurado:** `WorkspaceId` validado, tipo/estado, hechos tipados, restricciones, opciones reales, posibles siguientes pasos y razón segura de indisponibilidad. No devolver al modelo DTOs internos completos ni datos sensibles innecesarios.

**`ResponsePlan` existente:** fuente de verdad para hechos y operaciones, con dato obligatorio, siguiente pregunta y fallback; evolucionarlo solo tras revisar compatibilidad.

### Invariantes transversales

- `WorkspaceId` se toma del inbound autenticado y contexto de autorización, **nunca** de un argumento que haya generado el LLM.
- Permisos efectivos = licencia/módulo del workspace ∩ capacidad autorizada del actor/canal ∩ política de herramienta. No asumir que un empleado o contacto tiene permisos del dueño.
- El agente no puede inventar entidades ni obtener datos de otro workspace por búsqueda semántica, caché o referencia antigua.
- Un LLM no puede emitir un comando final de confirmación: la confirmación es una autorización explícita interpretada y verificada por el backend contra un resumen vigente, fuente, versión y operación.
- Lecturas y operaciones deben preservar FIFO, CAS/StateVersion, idempotencia, Outbox, `UnknownDelivery` sin reenvío ciego, toma humana y compatibilidad de recuperación.
- Presupuesto LLM agotado, JSON inválido, timeout o proveedor caído no alteran el borrador ni generan efectos comerciales.
- Las citas registradas no son visibles ni modificables únicamente porque alguien escriba un nombre o teléfono; hasta aprobar acreditación independiente, usar handoff.

## 3. Fases de implementación y puertas de aprobación

**Regla operativa:** Codex realiza **una fase por vez**, entrega diff e informe, ejecuta pruebas locales pertinentes, se detiene y espera auditoría. No refactorizar todo a la vez. Los títulos no conceden autorización de despliegue, migración o cambios remotos.

### Fase 0 — Baseline, diagnóstico real y contrato de calidad

**Objetivo:** dejar de corregir conversaciones aisladas sin explicar su causa.

- Congelar referencia exacta y mapa de ruta desde webhook hasta respuesta, incluyendo rutas de error y handoff.
- Inventariar reglas lingüísticas solapadas y respuestas genéricas/repetidas; clasificar cuáles son seguridad y cuáles son heurísticas prescindibles.
- Analizar fallos reales del intérprete: configuración, HTTP, timeout, 429, evidencia invalidada, salida JSON y fallback. Logs agregados sin nombres, teléfonos, prompts, IDs sensibles ni secretos.
- Crear/usar una **suite de evaluación semántica** diversa y con variaciones inéditas por rubro: clínica, hotel y tienda. Aprovechar herramientas de tests existentes, sin fabricar un proveedor real.
- Establecer medición baseline: finalización, repeticiones, recuperación, llamadas IA, latencia, lecturas, invenciones y errores de transacción.

**Entregable:** `docs/agent-runtime/00_BASELINE_DIAGNOSTICO.md`, matriz causa→código→evidencia, propuesta precisa de migración. **Gate G0:** no codificar fases posteriores sin diagnóstico aceptado.

### Fase 1 — Turno semántico coherente y precedencia única

**Objetivo:** una interpretación y una decisión de diálogo por mensaje, evitando competencia entre recovery, policy, menús e intérprete.

- Construir/ajustar contrato efímero de turno usando tipos existentes y evidencia verificable.
- Resolver antes reglas de seguridad y canal (handoff, licencia, cancelación inequívoca, consentimiento), no catálogos de sinónimos.
- Si el texto es inequívoco, procesar localmente; si no, IA contextual mediante **`AiRouter` existente** con una llamada presupuestada; no una llamada por campo.
- Interpretar «sí» por su **pregunta actual**, no por el último menú histórico. Invalidar o aislar menús obsoletos.
- Normalizar referencias «ese servicio», «la otra sede» y respuestas de seguimiento mediante evidencia y datos del workspace; no decidir por coincidencias frágiles.
- Mantener intactas reglas durables y handoff; no crear otro estado autoritativo.

**Entregable:** informe F1, diff acotado, regresiones. **Gate G1:** «sí» y una corrección nunca inician/transaccionan un flujo distinto; un mensaje desconocido no reinicia el borrador.

### Fase 2 — Herramientas de lectura verificadas y perfil lógico de agente

**Objetivo:** consulta natural sobre cada negocio sin meter su base completa en el prompt.

- Definir fachadas de solo lectura sobre servicios actuales: `get_business_profile`, `search_offerings`, `get_location_hours`, `get_public_faq`, `check_reservation_availability`.
- Herramientas reciben `WorkspaceId` confiable y aplican permisos, límites, paginación, filtros, zona horaria, sensibilidad y proveniencia.
- Componer un `AgentProfile` **efímero o desde configuración existente** por workspace: marca, tono, reglas publicables, módulos y opciones; no migrar BD ni crear tabla/configuración persistente nueva sin justificar la necesidad.
- FAQ/documentos grandes: **RAG optativo** solo si está demostrado el problema de recuperación de textos largos y existe política de indexing/tenant; no usar vectores para consultar precios, horarios o disponibilidad estructurada.
- Poner límites a cadenas de herramientas por turno (por defecto presupuesto de diseño máximo 2–3 lecturas; fijar valor tras medir) y fallback útil.

**Entregable:** matriz herramienta→fuente→permiso→coste→salida; tests de aislamiento. **Gate G2:** una pregunta semánticamente equivalente obtiene el mismo hecho, sin usar el conocimiento de otro cliente.

### Fase 3 — Reservas naturales con motor existente

**Objetivo:** un cliente indica servicio, sede, fecha y hora en cualquier orden; el bot pide solo lo que falte.

- Conservar `BookingFlow`, `IReservationEngine`, validación de duración, ocupación, jornada, zona horaria, recursos, CAS y consentimiento.
- En `COLLECT_TIME` **preguntar primero la preferencia**, no mostrar cinco slots de apertura por defecto. El cliente puede decir «4 pm», «16:00», «por la tarde»; sólo listar opciones si lo solicita o el horario elegido no existe y se proponen alternativas reales cercanas.
- Separar `check_availability` (lectura) de selección de una hora y de confirmación final. «¿Hay a las 4?» no modifica borrador; «quiero esa» puede seleccionar; «sí confirmo» tras resumen vigente autoriza crear.
- Nombre personal sin requisito artificial de capitalización; si no hay evidencia suficiente, una aclaración concreta. Verificar contra entidades comerciales autorizadas sin rechazar nombres humanos por coincidencias parciales.
- Gestión de citas **registradas** queda en handoff salvo si existe identificación independiente aprobada.

**Entregable:** conversación clínica supervisada y tests de no creación inadvertida. **Gate G3:** ninguna hora inventada, consentimiento respetado, borrador estable y agenda transaccional idéntica a la ruta web.

### Fase 4 — Solicitudes de pedido conversacionales (sin pagos)

**Objetivo:** atender «quiero hacer un pedido» y construir una lista de productos de forma flexible sin asumir venta ni stock confirmado.

- Mantener `OrderFlow`, validaciones de catálogo/licencia y notificación durable existente.
- Permitir empezar un **borrador vacío**: si no hay ítems, preguntar qué productos/cantidades desea o mostrar catálogo autorizado si lo solicita.
- Interpretar agregados, cambios, cantidades y consultas sin convertir preguntas hipotéticas en mutaciones.
- Presentar resumen de **solicitud**, solicitar confirmación y entonces registrar/enviar aviso al dueño/encargado autorizado.
- No prometer «pedido aceptado/listo/disponible» ni hora de recogida sin respuesta o estado aportado por el negocio.
- Si `ORDERS`/`CATALOG` no están licenciados o no hay productos disponibles, explicar límites y ofrecer atención humana; no inventar ofertas.

**Entregable:** pruebas de solicitud única, replay, aviso único y diálogo no-robótico. **Gate G4:** la confirmación genera una solicitud comercial idempotente, no un pago ni aceptación automática.

### Fase 5 — Respuestas naturales con hechos y lenguaje seguro

**Objetivo:** respuestas concisas, contextualizadas y adaptadas al rubro sin árboles de texto fijo.

- Evaluar `ResponsePlan` y los hechos existentes antes de ampliar `ResponseComposer`.
- Mensajes transaccionales críticos, avisos legales/operativos, consentimiento y fallbacks siguen deterministas.
- Para hechos públicos, introducir **realización controlada y evaluable**: el LLM puede escoger estructura o tono, pero un verificador independiente conserva identificadores, negaciones, unidades, dinero, fechas, horas, condiciones y pregunta requerida. Si no puede validarse, conservar texto factual original.
- No dar al modelo libertad para emitir precios, disponibilidad, estado de reservas o promesas que el backend no proporcionó.
- Mantener personalización por workspace sin enviar historiales privados ni secretos al compositor.
- Eliminar menús no solicitados y redundancias; mantener menús como ayuda o fallback explícito.

**Entregable:** comparación UX antes/después, pruebas de inyección y precisión factual. **Gate G5:** cero hechos inventados en evaluación de aceptación; no gasto IA para cortesías inútiles.

### Fase 6 — Costos, rendimiento, robustez y evaluación multiempresa

**Objetivo:** operar muchas conversaciones sin ahogar los proveedores ni volver imprevisible la facturación.

- **Mantener `AiRouter`**; añadir telemetría de intentos por proveedor/tarea, respuestas inválidas, retries/fallback, límites, latencia y, cuando el proveedor lo suministre, usage/tokens por workspace. Nunca registrar texto privado ni claves.
- Medir diferencias reales entre ruta local, IA primaria, fallback y composición. Presupuesto por tenant y total, con límites de concurrencia y degradación útil.
- Reducir lecturas Firestore redundantes solo con prueba de consistencia de estado y aislamiento.
- Evaluar la misma arquitectura con clínica, hotel y tienda **sin código específico por rubro**; únicamente cambian datos, personalidad permitida y capacidades.
- Evaluaciones de robustez con mensajes desconocidos, desordenados, truncados, multitema y potencialmente hostiles; distinguir resultado semántico de resultado comercial.
- Diseñar flags por workspace, rollback de runtime (no de operaciones ya confirmadas), shadow testing sin respuestas duplicadas y apagado rápido seguro.

**Entregable:** informe de costo por 1.000 conversaciones **medido**, p50/p95, tasa de éxito, aislación, regresiones e incidentes. **Gate G6:** coste y calidad aprobados sobre muestras reales supervisadas, no solo tests unitarios.

### Fase 7 — Piloto, rollout y preparación comercial

**Objetivo:** desplegar el runtime revisado sin arriesgar clientes ni sistemas compartidos.

- Construcción reproducible ARM64 desde commit aprobado y revisión de diferencias respecto a imagen desplegada.
- Preflight de respaldo **restaurable**, reversión compatible y ventana de mantenimiento; no confundir copia YAML con backup de PostgreSQL/Firestore.
- Piloto con workspace y contacto autorizados: consultas → borradores → transacciones de prueba autorizadas → toma humana → reanudación → fallo controlado de proveedor.
- Verificación de correlación Evolution→API→Outbox y ausencia de respuestas bajo modo humano.
- Rollout gradual por workspace/flag, métricas y criterio de parada documentados.

**Gate G7:** aprobación comercial **solo** cuando calidad, costos, privacidad, aislamiento, durabilidad y operaciones comerciales hayan superado pruebas reales. El LLM no tiene garantía de acierto absoluto: el producto debe recuperarse con seguridad.

## 4. Indicadores de calidad y decisiones de producto

**Indicadores propuestos para establecer después de G0** (no son resultados actuales ni garantías):

| Indicador | Medición | Condición prioritaria |
| --- | --- | --- |
| Comprensión | % de turnos cuya intención y referencia coinciden con revisión humana | Mejorar respecto al baseline; analizar por tipo de negocio |
| Conversación | % de tareas completas sin reinicio innecesario | Definir umbral con muestra representativa; aspiración inicial ≥95 % en tareas de MVP delimitadas |
| Recuperación | % de ambigüedades resueltas en la siguiente aclaración | Mensaje útil y una sola pregunta concreta |
| Respuestas repetidas | Veces que se repite misma pregunta sin nuevo motivo | Reducción marcada frente a baseline |
| Información no respaldada | Alucinaciones en conjunto de aceptación | 0 en pruebas de liberación (no garantía global) |
| Transacciones indebidas | Sin consentimiento o duplicadas | 0 en pruebas y monitorización; stop inmediato ante hallazgo |
| Privacidad | Datos de otros tenants / datos privados divulgados | 0; stop inmediato ante hallazgo |
| Coste/latencia | Tokens, llamadas, lecturas, p50 y p95, coste por 1.000 conversaciones | Presupuesto y SLO comerciales definidos con medición real |
| Handoff | Efectividad de toma humana / supresión de bot | Ningún mensaje bot posterior en ventana bajo control humano |

**Gates son acumulativos:** aprobar G3 no permite omitir G0/G1/G2; un GO local no es GO para producción.

## 5. Política de cambios y entregables por fase

Cada fase debe producir: `Resumen`, `SHA base y estado del checkout`, `método/contrato cambiado`, `diff/archivos`, `pruebas exactas`, `mediciones disponibles`, `invariantes`, `deuda remanente`, `riesgo y rollback`, `GO/NO-GO propuesto`. No atribuir métricas simuladas a producción. No introducir un framework de agentes externo, BD vectorial, segundo orquestador, migraciones o workers nuevos salvo demostración de necesidad y aprobación explícita.

**Control de trabajo:** Codex modifica solo el repositorio local cuando el prompt de fase lo autorice; no Git/commit/push/despliegue. El usuario publica y despliega conscientemente tras auditoría. Las pruebas contra proveedores pagos o WhatsApp real requieren autorización y datos de prueba seguros.

## 6. Backlog independiente: módulo de empleados (NO IMPLEMENTAR AQUÍ)

Después de la estabilización del agente, abrir un proyecto separado para empleados/encargados. No incluirlo en prompts, conocimiento del LLM, datos de entrenamiento ni modelos de IA durante F0–F7.

**Idea funcional futura:** el dueño administra empleados, asigna capacidades por módulo y permite operar conversaciones, consultar solicitudes de pedido/reservas y asumir/liberar el modo humano. Los empleados actúan bajo la **licencia del workspace propietario**; no obtienen por herencia permisos ilimitados del dueño.

**Principio obligatorio:** permisos efectivos = capacidades de la licencia del negocio ∩ asignaciones expresas del dueño ∩ rol/permiso propio de la operación. Revocación inmediata, auditoría de acciones, aislamiento tenant, no acceso a secretos ni a configuración de facturación del dueño. El empleado que responde en modo humano no requiere que su identidad privada sea entregada al modelo. El diseño exige revisión de autenticación, autorización, UI, auditoría y ciclo de vida de invitaciones; **no asumir que es trivial por existir un panel de conversaciones**.

## 7. Cómo usar este documento

1. Entregar a Codex **solo el Prompt 0** del documento compañero `NEXFLOW_AGENT_RUNTIME_4_PROMPTS_CODEX.md`.
2. Auditar el diagnóstico y aprobar G0.
3. Autorizar el siguiente prompt uno por uno, con objetivos y alcance invariables.
4. Tras G6, preparar el piloto conforme a G7. **No desplegar automáticamente.**

**Resultado buscado:** incorporar un negocio nuevo mediante configuración, fuentes y herramientas autorizadas, sin escribir casos específicos para cómo hablan sus clientes.
