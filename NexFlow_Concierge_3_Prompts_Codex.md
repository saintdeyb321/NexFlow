# NEXFLOW CONCIERGE 3.0 — PROMPTS PARA CODEX

**Documento acompañante:** `NexFlow_Concierge_3_Plan_Maestro.md`  
**Rama de trabajo:** `main`, pero comprobar identidad efectiva al inicio.  
**Base inspeccionada al redactar:** `a79e998fb56ac4100408d88b0c432b635b077b0b`.  
**Norma:** No avanzar de etapa sin una nueva aprobación. No modificar ni desplegar producción. El Sprint 0 es exclusivamente diagnóstico.

---

## PROMPT 1 — SPRINT 0: AUDITORÍA PROFESIONAL DE CÓDIGO Y FLUJOS (EJECUTAR AHORA)

> **Codex, actúa como arquitecto/auditor senior de backend .NET, SaaS multi-tenant, diálogo transaccional y sistemas distribuidos.**
>
> Trabaja en `C:\Proyectos\NexFlow`. Lee `AGENTS.md` y `NexFlow_Concierge_3_Plan_Maestro.md` (te lo proporcionaré en la raíz). **Este encargo explícitamente autoriza una auditoría amplia de los flujos conversacionales, sus fuentes y sus dependencias, pero no una auditoría general indiscriminada de todo el frontend/SaaS.**
>
> **Objetivo:** diagnosticar por qué NexFlow no se comporta como una recepcionista profesional durante conversaciones reales, a pesar de completar algunas operaciones guiadas. Identificar límites de información, errores de enrutamiento, uso incorrecto de datos, memoria, consultas, coste y diseños de refactorización posibles. **No escribir código productivo en este sprint.**
>
> ### 0. Identidad y alcance
>
> - Documenta `git rev-parse HEAD`, `git status --short`, rama y cambios locales. Nunca assumes que el checkout corresponde a `a79e998`; si difiere, registra diferencias y sigue con la revisión del código local efectivo sin sobrescribir nada.
> - Revisar de extremo a extremo solo las clases realmente participantes en: webhook inbound, FIFO, contexto/caché, clasificación local/LLM, política, conocimiento, horarios, reservas nuevas/existentes, pedidos, ResponsePlan/Composer y outbound.
> - Seguir referencias y llamadas reales. `rg`, inspección localizada de código, interfaces, implementaciones y DI. No basarse solo en nombres de clases o en documentos antiguos.
>
> ### 1. Análisis forense de conversación real
>
> Reconstruir cada ruta que probablemente generó estas respuestas, distinguiendo **verificado por código**, **hipótesis** y **requiere traza**:
>
> - «quiero informacion» → menú numerado.
> - «quiero informacion de la clinica» → aclaración genérica.
> - «qué productos tienen» → cinco productos y publicidad de otros módulos.
> - «cuáles son sus horarios» → «No hay horarios registrados para esta sede» sin sede seleccionada.
> - «cuál sede, si ni me preguntaste» → se listan direcciones.
> - «yo no te pedí eso» → otra aclaración genérica.
> - creación de cita exitosa → «sí confirmos» no reconocido al primer intento.
> - «quiero editar mi reserva» / «solo quiero cambiar el horario» → inicia otra reserva nueva.
>
> Para cada evento: métodos exactos, ramas, datos de entrada, estado de conversación antes/después, origen de la respuesta, consulta al repositorio, decisiones de permiso, intervención de IA, respuesta producida y dónde se perdió la intención real. No inventar la salida de Gemini/Groq ni registros de Oracle que no están presentes.
>
> ### 2. Mapa de información del asistente
>
> Construir una matriz por `Intent / DialogueAct / CurrentGoal / CurrentStep` con:
>
> - Qué parte resuelve .NET localmente y con qué prioridades.
> - **Qué texto exacto de instrucciones/contexto recibe Gemini o Groq** y qué NO recibe.
> - Qué se consulta de perfil, FAQ, sedes, horarios, catálogo, servicios, disponibilidad, pedidos y reservas; en qué momento.
> - Fuente de verdad por tipo de dato; si la consulta usa workspace, sede, estado, reloj y permisos.
> - Máximo de ofertas por respuesta, paginación, longitud de mensajes, presupuesto IA, timeouts, fallbacks, límites del contexto, reglas de retención.
> - Cómo distingue una consulta informativa de una operación, modificación, confirmación, cancelación y rectificación.
>
> **Investiga específicamente** `hours/global/schedule` frente a `hours/{locationId}/schedule`, y por qué `null` se convierte en `global`, no en todas las sedes. Analizar la salida de `BusinessHoursFormatter`, y proponer cómo condensar días contiguos con apertura y cierre realmente idénticos. Diferenciar horarios del negocio y disponibilidad de citas.
>
> ### 3. Datos, memoria y eficiencia
>
> Identificar dónde reside cada dato y qué ocurre cuando un cliente regresa al cabo de cinco días:
>
> - PostgreSQL: reservas, estados, origen y transacciones.
> - Firestore: perfil, sedes, horarios, catálogo, estado de diálogo versionado.
> - Redis: cache de 30 minutos y claves; qué se invalida y cuándo.
> - Memoria de proceso, FIFOs, outbound durable y deduplicación.
>
> Revisar `ContextRecoveryService` y explicar el coste de leer Firestore incluso ante cache hit. Comprobar retención y limpieza de `conversationStates`, expiración lógica de contexto (24 h) frente a expiración Redis (30 min), lectura y escritura por turno, tamaño de serialización, índices PostgreSQL, accesos repetidos, fan-out de FAQ/catálogos y contención del documento de presupuesto IA por workspace.
>
> **No proponer mantener todas las reservas en Redis.** Proponer lista breve de citas futuras consultadas en PostgreSQL con fuente y ámbito verificados, selección por referencia vigente y nuevo contexto temporal mínimo. Considerar cero, una o varias reservas, números compartidos, protección de datos y autorización real del remitente.
>
> ### 4. Diseño mínimo de gestión de reservas existentes
>
> Sin implementar, examina cómo usar `IReservationRepository`, `GetActiveReservationByPhoneAsync`, `GetByIdAsync`, `ReservationEngine.EditReservationAsync`, `CancelReservationAsync`, CAS, filas SQL y Outbox. Comparar edición web con WhatsApp. Identificar qué falta para:
>
> - Consultar citas futuras del contacto sin pedir todos sus datos otra vez.
> - Mostrar máximo 3–5 opciones y permitir «ver más».
> - Conservar ReservationId, sede y servicio, y cambiar solamente fecha/hora.
> - Pedir confirmación explícita y revalidar disponibilidad.
> - Garantizar idempotencia de edición y evento Outbox ante reintentos/ACK incierto.
> - Evitar crear una nueva reserva cuando se solicita modificar una existente.
>
> Señala si alguna mejora requeriría migración PostgreSQL o campo Firestore y justifica alternativas sin migración. Ninguna migración autorizada en Sprint 0.
>
> ### 5. Mejoras conversacionales y eficiencia de IA
>
> Auditar primero: recuperación, menús, `LastQuestion`, respuestas de «no era eso», cómo se reconoce una confirmación imperfecta, gestión de un cliente que cambia de intención, contexto de turno y respuestas de `ResponseComposer`. Distinguir fallos de comprensión de fallos de acceso a datos o de política.
>
> Proponer resolución local para hechos claros y acceso a datos; IA estructurada solo para la ambigüedad que lo justifique, con contexto mínimo autorizado. No atribuir al modelo hechos comerciales o permisos. No enviar historial completo, PII innecesaria ni secretos. Diseñar un registro de decisión por turno sin guardar texto sensible completo, de forma que podamos comprender por qué respondió algo erróneo.
>
> ### 6. Priorización y entregables
>
> Produce solo cuatro documentos Markdown:
>
> 1. `docs/concierge-3/00_AUDITORIA_DIAGNOSTICO.md` — mapa de causas, con rutas y líneas precisas, evidencia y grado de certeza.
> 2. `docs/concierge-3/01_MAPA_DATOS_Y_ESTADO.md` — secuencias reales, fuentes, retención, límites de IA/información, coste de lecturas y alternativas.
> 3. `docs/concierge-3/02_DECISIONES_PROPUESTAS.md` — máximo diez decisiones, priorizadas por impacto/seguridad/coste, con comparación entre corrección localizada y refactor.
> 4. `docs/concierge-3/03_INVENTARIO_DOCUMENTAL.md` — inventario de `.md` por vigencia, referencias cruzadas, conservar/archivar/candidato de eliminación, sin tocar ni borrar archivos.
>
> Incluye una lista corta de las **cinco mayores causas raíz**, acciones que proponen mejor experiencia y cómo verificar cada cambio mediante simulaciones supervisadas de WhatsApp, sin ampliar la batería sintética innecesariamente.
>
> ### RESTRICCIONES ABSOLUTAS
>
> - No modificar `.cs`, `.csproj`, frontend, SQL, migraciones, .env, secrets, Docker, Compose, reglas Firestore, Evolution, n8n o infraestructura.
> - No correr migrador, no tocar Oracle ni servicios productivos, no hacer llamadas pagadas, no reinyectar mensajes históricos.
> - No crear commits, push ni ramas, no borrar/archivar aún ningún archivo. Los cuatro `.md` de diagnóstico son la única escritura autorizada.
> - No corregir código antes del dictamen. Si encuentras un P0, explica el riesgo y detente; no lo parches.
> - Las pruebas existentes pueden leerse; no es necesario ejecutarlas todas ni fabricar otro corpus de 100 casos.
> - Al finalizar, presenta archivos creados, cinco causas raíz y bloqueos; **detente para auditoría antes del Sprint 1**.

---

## PROMPT 2 — SPRINT 1: PRIMER PARCHE CONTROLADO (GUARDAR; NO EJECUTAR AÚN)

> **IMPORTANTE: este prompt no autoriza desarrollo todavía.** Solo usar tras entregar y auditar los cuatro documentos de Sprint 0 y recibir aprobación explícita del propietario.
>
> Lee `AGENTS.md`, `NexFlow_Concierge_3_Plan_Maestro.md` y los cuatro informes de `docs/concierge-3/`. Considera el código local actual, no un SHA asumido.
>
> Implementa **únicamente** las decisiones aprobadas expresamente en `02_DECISIONES_PROPUESTAS.md` para corregir consultas informativas y reparación de contexto, priorizando:
>
> 1. Horarios reales: distinguir global, sede seleccionada y múltiples sedes; agrupar días con horarios iguales y reales, no confundir agenda de citas con apertura; responder correctamente cuando falta sede.
> 2. «Información de la clínica»: consultar perfil comercial autorizado, no saltar a menú general.
> 3. Rectificaciones: ante «no te pedí eso» recuperar la pregunta anterior y ofrecer una aclaración pertinente sin anunciar una operación nueva.
> 4. Confirmaciones semánticamente seguras en el paso vigente, sin aceptar negaciones o mensajes indirectos como consentimiento.
>
> No implementar reagendamiento de reservas confirmadas en este Sprint 1, excepto si el dueño cambia expresamente el orden después de la auditoría. No introducir un nuevo servicio/almacenamiento global para «memoria». Reutiliza contexto existente, SourceMessageId, CAS, licencias y renderer autorizado. No hacer llamadas IA para una pregunta cuya respuesta se puede recuperar determinísticamente.
>
> Presenta un diff pequeño, explicación antes/después y un conjunto mínimo de regresiones basadas en el incidente real y las invariantes afectadas. Después solicita permiso para probar con el número de WhatsApp de prueba; no modificar producción sin autorización separada. No commit, push, desplegar ni limpiar datos por tu cuenta. **Detenerse para auditoría.**

---

## Orden de trabajo

1. Entregar al agente el archivo maestro y **solo el Prompt 1**.
2. Auditar los cuatro informes, confirmar decisiones y dependencias.
3. Ajustar el Prompt 2 en función de hechos reales; autorizarlo en un nuevo turno.
4. La gestión de reservas existentes se autorizará en un siguiente sprint con contrato propio de identidad, edición idempotente y confirmación. No improvisar esa operación dentro de una refactorización de tono.
