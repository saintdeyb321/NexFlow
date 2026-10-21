# NEXFLOW CONCIERGE 3.0
## Plan maestro de diagnóstico, rediseño conversacional y eficiencia operativa

**Fecha de referencia:** 21 de octubre de 2026  
**Repositorio:** `https://github.com/saintdeyb321/NexFlow`  
**Base inspeccionada:** `a79e998fb56ac4100408d88b0c432b635b077b0b`  
**Entorno de desarrollo:** `C:\Proyectos\NexFlow`  
**Producción:** Oracle Cloud ARM64; no intervenir sin autorización expresa.  
**Estado:** PLAN DE TRABAJO. **Sprint 0 = solo auditoría y diagnóstico**. Ningún cambio de código, esquema, infra, Git ni despliegue queda autorizado por este documento.

---

## 1. Propósito y problema real

NexFlow es un SaaS multi-tenant de atención comercial por WhatsApp. Su asistente debe resolver consultas, reservas, solicitudes y pedidos con lenguaje cotidiano, sin inventar información, sin convertir cada mensaje en un menú y sin usar modelos de IA para tareas que resuelve mejor el software.

La conversación real del 21/10/2026 evidenció errores que sobrevivieron a cientos de pruebas locales: «quiero información» activó un menú genérico; «información de la clínica» devolvió una aclaración vacía; «horarios» consultó un alcance incorrecto; una protesta contra esa respuesta se interpretó como solicitud de direcciones; una confirmación coloquial no se reconoció; **«quiero modificar mi reserva» abrió una reserva nueva en vez de localizar la existente**.

**Nueva regla de trabajo:** antes de corregir respuestas, trazar los componentes y las fuentes de datos que intervinieron. No parchear frases aisladas. La mejora debe encontrarse en la decisión conversacional, el acceso al conocimiento, la gestión del estado o el contrato de operación que provocó el error.

### Resultado de producto buscado

Un cliente debe poder escribir después de varios días, sin mantener una sesión abierta, para consultar o reagendar una reserva propia. El bot debe recuperar la reserva desde la fuente de verdad, conservar los datos no modificados, pedir una sola aclaración útil y confirmar el cambio **sobre el mismo ReservationId**. Para información del negocio, debe responder con los datos oficiales correctos y distinguir horarios comerciales, horarios por sede y disponibilidad de citas. Las rectificaciones del cliente deben corregir el rumbo sin reiniciar el diálogo.

---

## 2. Principios y restricciones

1. **El backend ejecuta; la IA interpreta.** La IA no crea, cancela, modifica ni confirma transacciones directamente, ni inventa horarios, disponibilidad, precios o sedes.
2. **Una operación duradera no es memoria de conversación.** Reservas y transacciones viven en PostgreSQL; contexto conversacional en Firestore; Redis es caché efímera y prescindible.
3. **Una reserva de hace cuatro días no requiere cuatro días de caché.** Se consulta PostgreSQL por workspace y contacto autorizado, usando identidad estable e índices apropiados.
4. **Seguridad multi-tenant y mínima divulgación:** workspace autorizado, origen WhatsApp autenticado, número normalizado y revisión de titularidad. Un código corto o el número de teléfono por sí solos no equivalen a prueba absoluta de identidad.
5. **No crear otra máquina de estados** si pueden evolucionar `ConversationContextDto`, `DialoguePolicy`, `TurnUnderstandingPipeline` y los flujos actuales.
6. **No introducir un ERP/POS.** Pedidos siguen siendo solicitudes comerciales, no pagos o stock garantizado.
7. **No comprometer durabilidad:** conservar FIFO, CAS/StateVersion tipo `string`, idempotencia, confirmaciones explícitas, handoff humano prioritario y `UnknownDelivery` sin reenvío automático.
8. **Optimizar por impacto observado:** reducir vueltas innecesarias, lecturas Firestore, llamadas IA y tiempos, sin añadir caché distribuida compleja antes de medir.
9. **Los mensajes reales supervisados son evidencia de UX**; las pruebas automáticas se reservan para garantizar invariantes y reproducir defectos. No se exige crear cientos de casos por sprint.
10. **Sin cambios a Oracle, Docker, Caddy, Evolution, n8n, Redis, Firestore real, datos productivos, secretos, despliegues, commits o push** salvo autorización separada y específica.

---

## 3. Primeros hallazgos verificados en código (no sustituyen Sprint 0)

Estas observaciones se contrastaron con el commit indicado. Hay que confirmar su vigencia en el checkout local antes de usar las rutas para modificar código.

| ID | Evidencia de implementación | Problema o decisión que exige auditoría |
|---|---|---|
| H01 | `RedisConversationCache.SetContextAsync` usa `StringSetAsync(..., TimeSpan.FromMinutes(30))`. | La caché caduca; **no** necesita guardar reservas durante meses. Medir tamaño medio y número de contextos activos, no asumir crecimiento ilimitado. |
| H02 | `ContextRecoveryService.GetOrRecoverContextAsync` lee Redis **y también Firestore**, y compara `StateVersion`. | Garantiza autoridad durable, pero un hit de Redis no ahorra necesariamente la lectura Firestore. Cuantificar coste/latencia; optimizar solo con un diseño seguro de versiones. |
| H03 | `FirestoreConversationStateRepository` almacena `contextJson` por workspace/teléfono; no se observó allí una expiración automática del documento. | Auditar ciclo de vida, retención, privacidad, coste, documentos huérfanos y limpiezas existentes. No crear TTL destructivo sin análisis. |
| H04 | `ConversationStateService` utiliza sesión de 24 h y puede borrar contexto al cerrar una conversación inactiva sin objetivo. | Distinguir sesión conversacional de reserva PostgreSQL. Auditar qué pasa con borradores antiguos y recuperación días después. |
| H05 | `IReservationRepository.GetActiveReservationByPhoneAsync` / `ReservationRepository` buscan por workspace y contacto, pero devuelven solo la **primera** cita futura confirmada. | Para reagendar, auditar selección paginada si existen varias reservas y cómo se verifica la propiedad. |
| H06 | `IReservationEngine.EditReservationAsync` existe y actualiza la reserva por ID; el controlador web usa `PUT /api/reservations/{id}`. | La capacidad transaccional ya existe; revisar por qué no está conectada a WhatsApp, su idempotencia y sus eventos Outbox al reintentar. |
| H07 | `BookingFlow.ClearDraft` limpia `CurrentGoal`, sede, servicio, fecha, hora y paso al confirmar. | Es correcto cerrar un borrador; es incorrecto inferir de ello que una reserva confirmada dejó de existir. Las solicitudes posteriores requieren nueva consulta a PostgreSQL. |
| H08 | `ConversationIntent` ofrece `Reservation` pero no separa crear/consultar/reagendar/cancelar una reserva registrada; `ConversationDirective.CancelCurrentFlow` se refiere a borradores. | Auditar precedencia y contratos antes de introducir cualquier nuevo acto de gestión postreserva. |
| H09 | `FirestoreBusinessHoursRepository.GetBusinessHoursAsync(workspaceId, null)` consulta `hours/global/schedule`; con `locationId` consulta `hours/{locationId}/schedule`. | **`null` no agrega los horarios de todas las sedes.** Esto explica de forma plausible la respuesta vacía en la consulta general aun habiendo horarios por sede. |
| H10 | `BusinessHoursFormatter.Format` devuelve «No hay horarios de atención registrados para esta sede» cuando el conjunto está vacío. | Mensaje engañoso si no se eligió sede. Separar estado «falta sede» de «la sede no tiene horarios». |
| H11 | `ReservationEngine.GetAvailabilityAsync` lee horarios de la sede concreta y la agenda; `KnowledgeService` lee horarios de negocio por otra ruta. | Dos flujos con alcance distinto. No confundir **horario de apertura/cierre** con **slots de cita**. |
| H12 | `ConversationRecoveryService` tiene coincidencia literal de «quiero informacion» que activa menú; `DialoguePolicy` y el orquestador contienen fallback genérico. | Auditar precedencia y reacciones a correcciones: «no te pedí eso», «cuál sede», «dije mi reserva actual». |
| H13 | `AiInterpreter` transmite al LLM módulos, fecha, hora, objetivo y paso, junto con el mensaje; no le pasa automáticamente todo el perfil, FAQ, sedes y horarios. | Dibujar los límites exactos de información: qué ve el modelo, qué obtiene después el software, qué se omite y cómo se validan las respuestas. |
| H14 | `ResponseComposer` es determinista y, opcionalmente, antepone cortesías. No genera diálogo semántico libre. | El tono es responsabilidad secundaria. Primero mejorar decisiones y respuestas estructuradas con hechos reales. |
| H15 | `OfferingResponseFormatter.Format` presenta hasta cinco ofertas y `ConversationRecoveryService` pagina opciones. | Distinguir top 5 por mensaje de límite del conocimiento del negocio. Diseñar resumen y continuación sin inundar WhatsApp. |
| H16 | `AssistantRecoveryOptions`: 3 aclaraciones, 2.000 caracteres de entrada, 6 llamadas IA por cliente/min, 60 por workspace/min, contexto con caducidad lógica de 24 h. | Son límites configurados, no métricas. Auditar su impacto sobre usuarios indecisos, errores de proveedor, coste y respuestas repetitivas. |
| H17 | `FirestoreConversationStateRepository.TryConsumeAiBudgetAsync` utiliza un documento de presupuesto `ai-budget` por workspace y ventana minuto. | Auditar contención del documento si escala la concurrencia, lecturas/escrituras asociadas y corrección de límites; no migrar presupuestos sin evidencia. |
| H18 | `AiInterpreter` restringe el JSON de interpretación a 8.192 caracteres y máximo seis evidencias; el router prioriza Groq para extracción, Gemini para otro tipo de tarea. | Documentar qué activa cada proveedor, cómo ocurre fallback y cuáles son los costes y timeouts realmente configurados. |

**Naturaleza de los hallazgos:** H01–H18 son hechos de lectura estática cuando describen código. Las consecuencias operativas, el coste real, la causa exacta de cada mensaje de WhatsApp y el posible efecto de concurrencia son **hipótesis que deben verificarse**, no conclusiones de producción.

---

## 4. Modelo objetivo: operaciones durables y diálogo breve

### 4.1. Reagendar una reserva hecha días atrás

1. Cliente: «Quiero modificar mi cita». Primero reconocer `ManageExistingReservation`, **no** iniciar `CreateReservation`.
2. Validar workspace, webhook/identidad de canal, licencia y modo humano.
3. Consultar en **PostgreSQL** las reservas futuras confirmadas de ese contacto, en ese workspace, con límite de resultados e índices revisados. No consultar Redis para intentar recordar una cita antigua.
4. **Cero:** informar que no se localizó una cita vigente; no crear otra por defecto. **Una:** proponer la cita encontrada de forma discreta y pedir qué desea cambiar. **Varias:** mostrar un conjunto pequeño, ordenado por fecha, con opciones numeradas o identificadores breves verificados y opción «ver más».
5. Un número de la lista tiene significado solo dentro de una lista **persistida y vigente**, vinculada al workspace, identidad y a IDs completos del servidor. Un código corto no es credencial, y los sufijos de GUID no deben utilizarse como clave única sin verificar colisiones.
6. Conservar `ReservationId` y datos existentes; solicitar solo fecha/hora nueva si es eso lo que cambia.
7. Consultar horarios y disponibilidad de la sede/servicio ya registrados; distinguir preferencias («en la tarde») de horas libres verificadas.
8. Mostrar **resumen antiguo → nuevo** y confirmar explícitamente antes de mutar.
9. Ejecutar la modificación sobre el **mismo ReservationId**. Revisar/implementar idempotencia durable de operación por `(WorkspaceId, ReservationId, acción, SourceMessageId o clave de operación confirmada)` con restricción efectiva de unicidad/reintentos. No basta con `Guid.NewGuid()` en Outbox.
10. Tras confirmación, comunicar los datos registrados y actualizar solo el estado conversacional necesario. Si la notificación falla, no repetir el cambio comercial.

> **Atención:** el teléfono puede ser compartido, transferido o mal asociado. La selección/listado exige revisar autenticidad del webhook y un nivel de comprobación proporcionado. No mostrar listas privadas de terceros ni confiar en IDs suministrados por el LLM.

### 4.2. ¿Redis se volverá inmenso?

No por conservar una reserva que se consultará dentro de un mes: la reserva **no debería residir en Redis**. El código actual fija TTL de 30 minutos en la caché de conversación. El volumen de Redis lo determinan contactos simultáneamente activos, tamaño por contexto, otras claves y políticas de memoria; el verdadero análisis pendiente es cuánto crecen los **documentos de Firestore** y la cantidad de lecturas, no estirar el TTL. Evaluar métricas reales de `used_memory`, tamaño de claves por muestreo seguro, `keyspace_hits/misses` y lecturas del repositorio, **sin publicar teléfonos ni claves**. Prohibido FLUSH o barridos globales de Redis.

### 4.3. Horarios verificables, expresados como los diría una secretaria

Si se seleccionó una sede, leer **su** calendario configurado. Si la pregunta es general y hay varias sedes, identificar si existe realmente un horario global autorizado o si hay que resumir horarios por sede o pedir cuál. Nunca interpretar `hours/global` vacío como prueba de que todas las sedes están cerradas o no configuradas.

Si el calendario real de una sede tiene lunes–viernes 08:00–17:00, sábado cerrado y domingo cerrado, condensarlo de forma determinista:

> «En la sede Concepción atendemos de lunes a viernes, de 8:00 a. m. a 5:00 p. m.; sábados y domingos no atendemos.»

El ejemplo es **ilustrativo**, no una afirmación de los horarios actuales del negocio. Solo agrupar días con horarios **idénticos y comprobados**; respetar excepciones, días sin configuración, festivos si existen datos autorizados y sede. No inventar horarios. «Horario de atención» y «espacios libres para reservar» son conceptos distintos.

### 4.4. Memoria de trabajo compacta, no historial infinito

Conservar únicamente estado activo útil: intención/acto anterior, pregunta pendiente, entidad referenciada, candidatos verificables, paso, operación comercial y versión. Guardar IDs y no copiar catálogos enteros ni conversaciones completas a Redis. Firestore es autoridad del estado; su ciclo de vida debe revisarse. Al reabrir una operación registrada, recuperar datos de PostgreSQL y volver a validar titularidad, licencia y estado actual.

---

## 5. Sprint 0 — auditoría profesional de extremo a extremo (OBLIGATORIO)

**Única tarea autorizada al principio:** inspección estática y, opcionalmente, métricas de entornos locales de prueba ya existentes. No corregir código; no desplegar, borrar datos, cambiar configuraciones, ejecutar llamadas pagadas ni tocar Oracle. Entregar un **diagnóstico ejecutable**: suficiente detalle para que un segundo desarrollador pueda seguir un mensaje desde Evolution hasta WhatsApp y encontrar el origen de la decisión.

### A. Trazabilidad de mensajes y límites de información

Dibujar las rutas reales: `EvolutionWebhookController` → persistencia de inbound → worker/FIFO → identidad, permiso y contexto → recovery/policy → resolutores locales → IA cuando aplica → consulta de conocimiento/Booking/Order → ResponsePlan/Composer → Outbound durable → Evolution. Para cada salto documentar contrato, ID correlacionador, plazo, repositorio, cantidad de lecturas previstas, ruta de error y alternativas.

**Matriz por intención:** información general, perfil de empresa, FAQ, horarios, sedes, precios, catálogo/PDF, disponibilidad, nueva reserva, consultar reserva existente, reagendar/cancelar existente, pedido, reparación de respuesta, handoff. Columnas obligatorias: frase del usuario, clasificador local, cuándo llama IA, **qué información exacta ve la IA**, fuente autoritativa, opciones disponibles, límites/paginación y respuesta/fallback. Marcar explícitamente rutas **NO IMPLEMENTADAS**.

### B. Auditoría de conocimiento y horarios

Seguir perfil, FAQ, ubicaciones, horarios `global` y por sede, catálogos, servicios reservables, días cerrados, horarios de apertura, duración del servicio, ocupación y PDF. Identificar fuentes, esquema Firestore, filtros y prioridad entre horario global/sede. Explicar qué ruta produjo «No hay horarios... para esta sede» sin sede. Proponer formatos informativos compactos que no oculten excepciones.

### C. Auditoría de operaciones registradas

Identificar alcance de `GetActiveReservationByPhoneAsync`, `GetByIdAsync`, `EditReservationAsync`, `Cancel...`, estados, uso de UTC/zona de negocio, concurrencia, recursos, Outbox y reintentos. Identificar qué falta para administrar reservas ya confirmadas por WhatsApp. No suponer que el endpoint web confiere automáticamente autorización al remitente WhatsApp.

### D. Almacenamiento, caché, retención y costes

Mapa explícito de PostgreSQL, Firestore, Redis y memoria local. Campos por contexto, serialización estimada, TTL efectivo, documento versionado, ciclo de vida, cuántas lecturas se producen por turno aun con cache hit, coste potencial por workspace, índices de consultas de reservas y puntos de contención. Separar lectura de código de medición real. Proponer reducción de lecturas y documentos antes de proponer otras infraestructuras.

### E. Diagnóstico de conversación y uso de IA

Analizar precedencia de `ConversationRecoveryService`, `DialoguePolicy`, `TurnUnderstandingPipeline`, `AiInterpreter`, `AiRouter`, `BookingFlow`, `OrderFlow`, `ChatFlow`, `ResponseComposer`. Explicar los saltos de intención y la incapacidad para reconocer protestas y modificaciones. Inspeccionar límites de tokens, fallback, JSON, presupuesto y punto en que se pierde contexto. Proponer arquitectura incremental; no un agente autónomo ni un chatbot que fabrique respuestas.

### F. Jerarquía de riesgos y propuestas de cambio

Documentar cada causa con: ID; prueba textual del código `ruta:líneas`; qué ocurrió en WhatsApp; hipótesis vs demostrado; severidad; intervención mínima posible; dependencias; riesgos de regresión; coste/beneficio; necesidad de migración; seguridad; aceptación observable en conversación real. Clasificar **corrección localizada**, **refactor razonable**, **cambio de contrato**, **fuera de alcance**. Evitar marcar como «completo» lo que está solo parcialmente implementado.

### Entregables exclusivos de Sprint 0

- `docs/concierge-3/00_AUDITORIA_DIAGNOSTICO.md`: hallazgos con evidencias de código.
- `docs/concierge-3/01_MAPA_DATOS_Y_ESTADO.md`: diagrama de flujos + fuentes de verdad, TTL, IA, límites y estimación de consultas.
- `docs/concierge-3/02_DECISIONES_PROPUESTAS.md`: máximo 10 decisiones para aprobación, ordenadas por impacto, incluida gestión de reservas existentes y horarios por sede.
- `docs/concierge-3/03_INVENTARIO_DOCUMENTAL.md`: qué documentos son vigentes, históricos, reemplazados, duplicados o críticos para rollback.

**Gate de salida:** auditar conjuntamente esos cuatro documentos y aprobar **qué se corrige**, antes de que Codex modifique una sola clase productiva. No se pide una suite de 100 pruebas para completar un diagnóstico.

---

## 6. Ruta de ejecución posterior (Sprints guiados por el diagnóstico)

| Etapa | Objetivo | Cambio permitido solo tras aprobar Sprint 0 | Criterio funcional en WhatsApp |
|---|---|---|---|
| Sprint 1 | Información correcta y contextual | Perfil, horarios multi-sede, aclaraciones útiles, respuestas concisas; sin reescribir motores. | «Horarios» muestra sede válida y horas agrupadas; «no pedí eso» repara la consulta anterior. |
| Sprint 2 | Reservas existentes | Búsqueda segura de reservas, lista limitada cuando haya varias, selección por ID completo y reagendamiento del mismo registro con confirmación e idempotencia. | Volver cuatro días después, elegir cita y modificar **sin iniciar una nueva**. |
| Sprint 3 | Política conversacional | Prioridad entre intención nueva, corrección, consulta, recuperación de operación y handoff; reconocimiento contextual de confirmaciones. | Rectificar después de una respuesta equivocada sin menús ni pérdida de operación. |
| Sprint 4 | Rendimiento y gastos | Medir/optimizar lecturas Firestore, queries PostgreSQL, presupuestos IA y caché, sin comprometer consistencia. | Las consultas fáciles consumen cero tokens; un usuario no necesita explicar datos ya verificables. |
| Sprint 5 | Operación supervisada | Revisión de trazas anonimizadas, feedback real del número de prueba, fixes de incidencias verificadas. | Varias conversaciones continuas completan tareas sin duplicación, filtración ni confusión persistente. |

**Orden flexible según hallazgos:** no avanzar automáticamente; si Sprint 0 muestra que un defecto crítico nace en otra capa, reordenar con justificación y aprobación. **No hacer «sprint de tests» independiente:** reproducir un defecto, proteger su invariante y comprobarlo en conversaciones reales. La tasa de éxito informada nunca sustituye observar qué contestó el bot.

---

## 7. Indicadores de éxito y presupuesto

Medir sobre escenarios consentidos y datos sintéticos: tasa de tareas completas sin asesor, turnos hasta resolver, preguntas repetidas, «no era eso»/reparaciones, menús no solicitados, modificaciones de la reserva correcta, respuestas sin evidencia, lecturas Firestore por turno, consultas PostgreSQL por flujo, IA invocada por categoría, tokens estimados o facturados, p50/p95 por fase, fallo/reintento/UnknownDelivery y duplicaciones. No registrar cuerpos completos del cliente ni números de teléfono en telemetría compartida.

**Criterios de seguridad innegociables:** cero mutaciones sin autorización, cero cambios de reserva equivocada, cero cruces de workspace, cero nuevas reservas al solicitar un reagendamiento, cero envíos ciegos tras UnknownDelivery. Los umbrales conversacionales y de coste se fijarán **después de medir una línea base**, no inventados para aparentar éxito.

---

## 8. Política documental: qué conservar, archivar o eventualmente retirar

**No borrar ningún `.md` de inmediato.** Los informes de fases contienen evidencia histórica y algunos runbooks documentan protocolos, migraciones y rollback. La limpieza se hará con `git mv` a un archivo histórico solo **después** de que Codex revise referencias cruzadas, documentación contractual y uso operacional. Para Git, eliminar significa perder el acceso sencillo a esos documentos en el árbol actual; el historial permanece, pero no es razón para borrar sin inventario.

### Mantener vigentes, nunca eliminar por esta iniciativa

- `AGENTS.md`: contrato de trabajo e invariantes; este plan debe respetarlo.
- `NexFlow_RC3_DEPLOY_RUNBOOK.md`, `NexFlow_RC3_RELEASE_CHECKLIST.md`: operación, migraciones, rollback; **mantener**, aunque algunos valores tengan carácter histórico y requieran revisión posterior.
- `docs/NEXFLOW_PRODUCT.md`, `docs/UX_UI_ROADMAP.md`, `frontend/README.md`: documentación funcional/frontend relevante.
- `NexFlow_CONCIERGE_PHASE6_PILOT.md`: conservar como referencia operacional de WhatsApp hasta disponer de un runbook sucesor auditado.
- `backend/EVOLUTION-local-audit.md` y `backend/PDF-1.1-recovery.md`: referencias especializadas; auditar vigencia, no borrar ahora.

### Candidatos a archivar (no eliminar aún)

Mover en una **tarea documental posterior**, tras la auditoría de dependencias, a `docs/archive/2026-10-concierge-v2/`:

- `NexFlow_Plan_Maestro_Concierge_2_0.md`
- `NexFlow_CONCIERGE_PHASE6_REPORT.md`
- `NexFlow_CONCIERGE_PHASE61_REPORT.md`
- `NexFlow_CONCIERGE_PHASE62_REPORT.md`
- `NexFlow_CONCIERGE_PHASE62_CLOSURE_REPORT.md`
- `NexFlow_CONCIERGE_CANDIDATE_CERTIFICATION_REPORT.md`

Se consideran **históricos**, no inútiles. También evaluar archivado de `NexFlow_RC3_PROGRESS.md`, `NexFlow_PLAN_MAESTRO_CORRECCION_MVP_RC3.md`, `INCIDENTE_CAUSA_RAIZ.md`, `NexFlow_RC3_SPRINT4_1_CONSISTENCIA.md` y `NexFlow_RC3_SPRINT4_2_INTEGRACION.md` **solo cuando se haya conciliado su relación con el runbook actual y las incidencias**. Los informes `docs/UX_UI_SPRINT_*.md` y `docs/UX_UI_*REPORT.md` pertenecen al historial de frontend y quedan fuera de esta limpieza.

### Único documento canónico nuevo

`NexFlow_Concierge_3_Plan_Maestro.md` (este archivo) gobierna el diagnóstico y la evolución de la experiencia. Los resultados específicos viven en `docs/concierge-3/`. Evitar crear un «plan maestro» nuevo cada semana o duplicar documentos con instrucciones contradictorias.

---

## 9. Contrato entre propietario, auditor y Codex

**Propietario:** decide política de producto, alcance, permisos y cuándo desplegar.  
**Auditor:** contrasta el informe con código/WhatsApp, aprueba prioridades y señala riesgos.  
**Codex:** diagnostica o implementa **únicamente la etapa autorizada**, trabaja en `C:\Proyectos\NexFlow`, deja diff legible, registra resultados sin inventarlos y se detiene.  

Sprint 0 **no autoriza** tocar código. Una nueva etapa requiere una solicitud distinta, citando el informe de Sprint 0 y decisiones aprobadas. Ante dudas entre naturalidad y seguridad, prevalece seguridad, pero se responde con una aclaración útil, no con un menú genérico.
