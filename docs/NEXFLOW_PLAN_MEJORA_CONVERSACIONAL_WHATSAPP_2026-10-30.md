# NexFlow — Plan de corrección conversacional basado en WhatsApp real

**Fecha:** 30 de octubre de 2026  
**Repositorio:** `https://github.com/saintdeyb321/NexFlow.git`  
**Base desplegada y certificada:** `aca4d7397d09a522c529d039fa21bb9c655cc158`  
**Servidor:** Oracle Linux ARM64, contenedor `nexflow_api` / Compose `nexflow`, servicio `api`.  
**Ámbito:** backend de asistente WhatsApp; producto multiempresa y negocio DEMO existente.  
**Origen de evidencia:** transcripción del propietario de una conversación WhatsApp del negocio «Sonrisa Andina Dental — DEMO» y lectura del código de GitHub en el SHA indicado. No se consultaron trazas privadas de Evolution, PostgreSQL ni Firestore de esa conversación; las causas exactas que dependen de esas trazas siguen sin determinarse.

## 1. Decisión operativa y prioridades

**P0 — Integridad transaccional de reservas:** en la transcripción, tras seleccionar «Limpieza dental?» el sistema preguntó la fecha y apareció un mensaje «✅ ¡Todo listo ... Tu reserva está confirmada», con **otro servicio** («Aplicación de flúor»), fecha/hora y **nombre no proporcionados en la secuencia visible**. Esto **no demuestra por sí solo** que se haya creado una reserva sin consentimiento: el mensaje puede ser tardío, reintentado, proceder de otro flujo o ser un fallo real de contexto/correlación. **Investigar primero** relacionando ID de mensaje de entrada, workspace, conversación, contexto persistido, efecto reserva, outbox y respuesta Evolution. Hasta aclararlo, no autorizar pruebas de confirmación que puedan causar efectos ni afirmar que reservas es seguro por estar más avanzado. Si se comprueba que hubo efecto no autorizado, bloquear ese camino y añadir regresión antes de continuar. No eliminar, corregir a mano ni reenviar efectos; no reprocesar `UnknownDelivery`.

**P1 — Pedidos inutilizables por interpretación:** el pedido de dos hilos se compuso y cotizó, pero «Envía la solicitud», «Dije que solo quiero eso», «Quiero cancelar el pedido» y «No conserves los productos quiero eliminarlos» cayeron en respuestas repetitivas que no progresan. El usuario únicamente pudo descartar el borrador mediante el comando aislado «Cancelar» + «Sí». Se debe equiparar el entendimiento conversacional de pedidos y reservas, sin equiparar sus efectos ni relajar consentimientos.

**P1 — Consultas factuales y referencias:** «¿Está disponible el hilo dental?» devolvió también un enjuague; «Dije si está disponible» generó una aclaración genérica. Diferenciar «producto publicado/activo en catálogo» de **stock real confirmado**. Si el dominio no tiene inventario verificado, afirmar solo que figura en el catálogo y explicar que la disponibilidad física la confirma el negocio. Resolver «el hilo dental», «ese», «solo ese» a una referencia de producto actual, sin agregar otro producto ni iniciar pedido accidentalmente.

**P2 — Conversación, orientación y redacción:** saludos y ayuda deben orientar por capacidades habilitadas; los pedidos requieren pasos claros similares a reservas; formatos explícitos «03/11», «3 de noviembre», «martes 3 de noviembre» con zona horaria y horarios del negocio verificados; ejemplos con comillas españolas `«...»`, y lenguaje natural que no obligue al cliente a acertar un comando exacto. Manejo de errores ortográficos («Quieor», «conepcion»), preguntas contextuales e interrupciones sin pérdida de estado.

## 2. Evidencia de WhatsApp aportada

| Turno (resumen literal) | Observado | Contrato esperado |
|---|---|---|
| «Hola» | Saludo fijo «escribe menú ... o asesor ...» | Bienvenida breve contextual, 2–3 ejemplos claros según módulos reales, opcional `«menú»` / `«asesor»`, sin pedir comandos obligatorios. |
| «Quieor informacion» | Perfil completo del negocio | Respuesta útil y breve, seguida de «¿Quieres saber sobre servicios, precios, horarios o sedes?»; adaptar si la petición es general. |
| «Que productos tiene?» | Lista de 4 productos, correcto en principio | Lista verificada y legible; orientar «puedes pedir, por ejemplo, `«Quiero 2 hilos dentales»`». |
| «Esta disponible el hilo dental?» | Hilo + enjuague; sin contestar disponibilidad | Responder **solo** sobre el hilo; separar existencia en catálogo de stock. No inventar inventario. |
| «Dije si esta disponible» | «Quiero asegurarme ... qué dato ...» | Recuperar referencia vigente y responder o preguntar solo por la dimensión no verificada. |
| «Quiero hacer un pedido» | Pregunta genérica por productos | Explicar preparación: elegir productos y cantidades, ver resumen, solicitar envío, confirmar explícitamente. |
| «Quiero 2 hilos dentales» | 2 × Hilo, PEN 25.80 | Correcto. Mantener el borrador y orientar: «Si está bien, di `«Enviar la solicitud»`; te mostraré el resumen final antes de confirmar». |
| «Envía la solicitud» | Error «No pude interpretar ese cambio» | Intención **solicitar revisión/envío**; mostrar presupuesto vigente y pregunta de confirmación, **sin registrar todavía pedido**. |
| «Dije que solo quiero eso» | Mismo error | Entender que no desea agregar productos; mantener exactamente los 2 hilos; ofrecer resumen y avance, sin requerir fórmula exacta. |
| «Estúpido dije ...» | Mismo error | Responder sin confrontación y resolver el pedido; no castigar insulto con bucle. |
| «Quiero cancelar el pedido» | Mismo error | Detectar intención de **descartar borrador**, distinguir de cancelación de pedido ya registrado; pedir confirmación de descarte si corresponde. |
| «No conserves los productos quiero eliminarlos» | Mismo error | Distinguir vaciar productos del borrador de descartar operación; una pregunta focal si la distinción es ambigua. Nunca borrar pedido persistido. |
| «Cancelar» + «Si» | Descarte seguro | Conservar el mecanismo de confirmación de descarte. Ampliar comprensión natural para llegar allí. |
| «Quiero hacer una reserva» | Ruta guiada por sede y servicio | Conservar validación determinista y mejorar redacción/pasos. |
| «En conepcion» | Listado general de dos sedes | Interpretar variante ortográfica contra **sedes autorizadas**; resolver solo si inequívoco; si no, preguntar entre opciones. |
| «En concepcion» | Lista de servicios de sede | Correcto, con alternativas factuales. |
| «Limpieza dental?» | Pregunta por fecha, seguida en la transcripción de confirmación inesperada con Aplicación de flúor | **P0 de investigación:** ni la selección ni el cambio de servicio, fecha, hora, nombre y efecto de reserva pueden aparecer sin datos/fuente y consentimiento vigente verificables. |

**Criterio:** un mensaje que parece confirmado en WhatsApp puede proceder de salida asíncrona de otra transacción; no diagnosticar automáticamente autorización indebida sin evidencia de correlación de cada mensaje.

## 3. Confirmaciones del código publicado

Evidencia de lectura del repositorio en `aca4d739` (rutas relativas):

- `backend/NexFlow.Application/Features/Automation/Dialogue/Understanding/OrderMentionResolver.cs`: `IsSubmit` filtra las palabras de envío mediante un conjunto restrictivo, sensible a términos adicionales. La estrategia actual no representa adecuadamente la semántica de «Envía la solicitud» en su contexto de revisión.
- `backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/Flows/OrderFlow.cs`: usa `OrderTurnKind` (`Start`, `Edit`, `Summary`, `Submit`, `Clarify`), calcula cotización y protege el commit mediante resumen/fingerprint, pregunta vigente, consentimiento y creación idempotente. Esta **protección debe preservarse**. Hay oportunidades de guía en `ORDER_COLLECT_ITEMS`, `ORDER_REVIEW` y `ORDER_CONFIRM`.
- `backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/AiResponseOrchestrator.cs`: existen rutas de fallback literales como «No pude interpretar ese cambio. Conservamos los productos y cantidades...». Se observa además interrupción informativa según goal y enrutamiento a `OrderFlow` y `BookingFlow`.
- `backend/NexFlow.Application/Features/Automation/Dialogue/Understanding/TurnUnderstandingPipeline.cs`: la interpretación con `RequiresClarification` produce una aclaración genérica; algunas consultas de orden pasan a `CommerceTurnResolver`, pero no está asegurado que el enrutamiento atienda todas las directivas de pedidos.
- `backend/NexFlow.Application/Features/Automation/Dialogue/Policy/DialoguePolicy.cs`: el modelo de diálogo clasifica patrones, negaciones y reservas; `ReservationProcess` ofrece guía básica. No copiar esta estrategia de frases cerradas para todos los pedidos.
- `backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/ConversationRecoveryService.cs`: `PendingRecoveryAction` y preguntas vigentes implementan cancelación/descarte, clarificación e invitaciones; preservar su seguridad, límite de intentos y estado durable.
- `backend/NexFlow.Application/Features/Automation/Dialogue/Composition/ResponseComposer.cs`: `ResponseCompositionOptions.EnableAi` **por defecto es falso**. La composición IA actual es muy limitada: agrupar párrafos verificados para ciertos tipos de consulta o añadir un encabezado local al inicio de una operación. **No es actualmente una capa general de redacción conversacional.** Habilitar un flag no soluciona por sí solo la comprensión del pedido ni valida las acciones.

El hecho de que exista una frase fija no es el error por sí mismo: lo erróneo es usarla como sustituto de una intención no resuelta, repetirla indefinidamente o no presentar la siguiente acción apropiada. Mantener respuestas deterministas para consentimientos, fallos, restricciones y hechos verificados.

## 4. Diseño objetivo: IA entiende; backend verifica; respuesta guía

### 4.1 Interpretación semántica común de turnos

Implementar o extender el contrato tipado de comprensión reutilizando `PreparedDialogueTurn`, `TurnUnderstanding`, `AiInterpretation`, `OrderTurnProposal` y demás tipos existentes. **No crear otro orquestador paralelo**. Para cada mensaje y estado vigente, distinguir de forma semántica:

- Consulta factual de producto/servicio, stock, sede, precio, horario o disponibilidad.
- Inicio de pedido o reserva; aportación de entidades (producto, cantidad, servicio, sede, fecha, hora).
- Corrección o sustitución parcial; «eso», «ese», «solo eso», cantidades en singular/plural y referencias validadas.
- Solicitud de enviar para revisión; revisión existente; **consentimiento final** como operación distinta y más estricta.
- Cancelar/descartar **borrador**, vaciar artículos del borrador, rechazar una propuesta, desistir de la conversación, gestionar una operación **ya registrada** (requiere identidad/autorización y flujo separado).
- Ayuda/instrucciones, interrupción informativa, respuesta negativa, contradicción y transferencia humana.

Usar extracción con evidencia textual y referencia a **entidades autorizadas** para el workspace/sede, y fallbacks deterministas cuando no sea posible. Las expresiones normalizadas pueden dar caminos rápidos para casos inequívocos, pero **no deben bloquear la interpretación semántica** si el texto contiene formas naturales no listadas. Evitar construir árboles de `if`, listas masivas de sinónimos o regex por oración.

### 4.2 Resolver productos y afirmar disponibilidad con precisión

La pregunta de stock no es un acto de agregar al carrito. `ProductQuery` debe mantener `OfferingReference` de una entidad concreta, reconocer pronombres en contexto y no mezclar fichas vecinas. Si solo consta `IsActive`/catálogo, responder «Está publicado en nuestro catálogo, pero no tengo confirmación del stock físico; puedo preparar una solicitud y el negocio lo revisará». Solo indicar «hay unidades» si existe inventario real autorizado y actualizado. No citar precios o condiciones sin fuente de repositorio.

### 4.3 Pedidos guiados como reservas, con contrato transaccional propio

Flujo sugerido:

1. **Inicio:** «Te ayudo con tu pedido. Dime qué productos y cuántos quieres; por ejemplo, `«2 hilos dentales y 1 cepillo»`. También puedes ver `«productos»`.»
2. **Captura/corrección:** aceptar frases naturales de cantidades, artículos adicionales, sustituciones y eliminación de ítems. «Solo 2 hilos» confirma selección sin sumar duplicados.
3. **Resumen:** enumerar artículos, cantidades, moneda, subtotales, sede cuando aplique, datos pendientes y aviso de que es **solicitud para revisión** y que no se ha confirmado stock/pago.
4. **Intención de envío:** al decir «Envía la solicitud», «Eso es todo, mándala», «Terminé», pasar a la **pregunta de consentimiento** vinculada al presupuesto vigente; todavía no crear pedido.
5. **Confirmación:** únicamente una aceptación explícita del resumen activo, posterior y válida, puede crear una solicitud; preservar firma/fingerprint, revalidación catálogo/licencia, un solo efecto y notificación idempotente. Una frase genérica «de acuerdo» sin referencia de confirmación final no basta.
6. **Descarte/edición:** «No lo quiero», «Cancela mi pedido», «Borra lo que agregué» deben tramitar intención concreta. Aclarar cuando `cancelar` pueda referirse a una operación persistida, nunca borrar datos por suposición.
7. **Fallo IA:** mantener borrador, ofrecer una acción útil y específica del estado (`«Agregar»`, `«Quitar hilo dental»`, `«Enviar solicitud»`, `«Descartar borrador»`, `«asesor»`) sin repetir indefinidamente el mismo mensaje.

Los ejemplos se generan a partir de nombres/fechas reales del workspace. Nunca exponer comandos administrativos internos al cliente.

### 4.4 Reservas: continuidad y guía práctica

Conservar `BookingFlow` / `ReservationEngine`. Tras sede, servicio, fecha y hora, el siguiente paso debe ser evidente, pero sin fijar horarios por inferencia ni saltarse la confirmación. Preguntas de fecha: «¿Qué día prefieres? Por ejemplo, `«03/11»` o `«3 de noviembre»`. Si quieres, dime `«mañana por la tarde»` y comprobaré los horarios de la sede». Para hora: mostrar opciones verificadas de la sede/servicio/fecha, no prometer disponibilidad futura sin consulta. Usar zona horaria configurada del negocio (America/Lima si corresponde al workspace) y resolución de ambigüedades de día/mes/año. No suponer que «martes 3» es 2026 sin reloj/configuración fiable. Si el usuario escribe «Limpieza dental?» en selección de servicio, resolver la entidad o confirmar sin crear reserva. «conepcion» puede resolverse por similitud **solo contra lista autorizada y sin colisión**.

### 4.5 Presentación natural, sin creatividad sobre hechos

Dos capas separadas:

- **Plan canónico (backend):** `Intent`, `Act`, referencia de entidad, datos necesarios, hechos citables, resumen y fingerprint, permisos/estado, siguiente pregunta y acciones permitidas. No requiere IA para concluir o persistir efectos.
- **Realizador de respuesta:** texto amable y corto para WhatsApp, sin inventar precios, sede, horarios, disponibilidad, diagnóstico médico, efecto comercial ni permisos. Puede emplear IA **acotada** para redactar orientación/información con hechos y opciones verificadas; validar la salida (JSON/estructura, límites, anchura de contexto, identidad de hechos, ausencia de órdenes extra, no cambios de resumen/consentimiento). Si hay error, timeout o límites por workspace, fallback **útil y específico**, no el bucle genérico.

No activar un LLM libre para firmar operaciones ni prometer disponibilidad; respetar límites de presupuesto y revalidación de licencias. Mantener acentos, signos `¿?`, puntuación, listas WhatsApp y ejemplos `«...»`. En saludo y navegación, mostrar 2–3 capacidades realmente habilitadas. No hardcodear estilo odontológico para otros sectores.

## 5. P0 — Investigación de confirmación inesperada

Antes de modificar la lógica comercial o aceptar el piloto:

1. Identificar el `workspaceId` y la conversación DEMO **sin compartir teléfono, claves o PII** en reporte. Cruzar timestamps UTC/local con cada `sourceMessageId` y envío Evolution, `ConversationRecord`, inbox/outbox/receipt y contexto Firestore (versiones y cambios).
2. Determinar si el mensaje de «reserva confirmada» corresponde a un efecto creado **después** de «Limpieza dental?» o a un mensaje demorado/duplicado de otro turno, y si está asociado a esta misma conversación y workspace.
3. Para la reserva identificada, inspeccionar de forma agregada y segura la secuencia: quién aportó sede, servicio, fecha, hora y nombre; resumen mostrado y fingerprint/`CurrentQuestion`; mensaje explícito que autorizó; fecha de commit; idempotency key y outbound source. No inventar datos faltantes. Distinguir «mensaje mostrado» de «reserva persistida».
4. Verificar aislamiento por tenant/phone y orden FIFO, recuperación de contexto, correlación de respuesta y `UnknownDelivery`. Revisión de cualquier memoria/borrador preexistente: la conversación no comienza necesariamente en estado limpio.
5. Reproducir la secuencia real con pruebas controladas: ningún `ReservationEngine` commit ni «confirmada» sin resumen vigente y consentimiento explícito; servicio no cambia de Limpieza a Flúor; sin fecha/hora/nombre nuevos por interpretación implícita. Si la traza resulta ser salida tardía, probar no atribuir ni reordenar confirmaciones de otros mensajes.
6. Si se confirma un efecto no autorizado: clasificar **P0**, corregir mínimo en la capa causal, mantener flujo de confirmación fuerte, no dar GO a nuevas operaciones reales hasta que la regresión pase.

## 6. Matriz de pruebas de aceptación obligatoria

**Reproducir el diálogo entero** como prueba multi-turno persistente con IDs únicos (no solo inputs aislados) y datos DEMO sintéticos. Añadir variantes fuera del texto exacto de la transcripción, incluyendo faltas ortográficas, negaciones y pronombres. Usar fixtures de IA simulada y del modo sin IA; verificar efectos y contexto persistido, no solo que la respuesta contenga una frase.

| ID | Escenario | Resultado mínimo |
|---|---|---|
| O01 | «¿Hay hilo dental?» / «Dije si está disponible» | Referencia única y respuesta diferenciando catálogo vs stock. Sin artículo extra ni pedido. |
| O02 | «Quiero hacer un pedido» | Explicación breve de pasos y pregunta por productos/cantidades. |
| O03 | «Quiero 2 hilos» | Borrador exacto de 2 unidades; resumen PEN 25.80 según fixture. |
| O04 | «Envía la solicitud» tras O03 | Paso a revisión/confirmación; **0 pedidos persistidos**. |
| O05 | «Confirmar» tras resumen vigente | **1 pedido PendingReview**, aviso idempotente; reenvío del mismo `sourceMessageId` = 0 duplicados. |
| O06 | «Eso es todo» / «solo quiero eso» | No suma unidades; deriva a resumen/enviar con guía. |
| O07 | «No quiero ese», «quita hilo», «elimina los productos» | Cambio de borrador seguro; si ambiguo, pregunta focal y no cambia datos. |
| O08 | «Cancela mi pedido» en borrador | Descarte con confirmación, separado de cancelación de pedido registrado. |
| O09 | Consulta mientras ORDER activo | Responder y volver a paso actual sin perder ni mutar pedido. |
| O10 | LLM falla / timeout / falta cuota | Respuesta operativa contextual; no loop ni efecto, no llamadas excesivas. |
| R01 | «Quiero reservar» / «En conepcion» | Resolver sede autorizada inequívoca o aclarar; no mostrar listado genérico si ya está determinada. |
| R02 | «Limpieza dental?» | Selección guiada, pedir fecha y ejemplos; **no crear reserva**. |
| R03 | Solo sede/servicio, sin fecha/hora/nombre/consentimiento | 0 reservas, 0 avisos de confirmación. |
| R04 | Mensaje confirmado demorado/de otra secuencia | Trazabilidad de `sourceMessageId`, destino, tenant y orden; no atribuir al turno actual. |
| R05 | Consentimiento exacto sobre resumen vigente y nuevo `sourceMessageId` | Un solo efecto, servicio/fecha/hora/nombre iguales a los del resumen; renovación si cambia campo. |
| R06 | «De acuerdo.» ante resumen final | Sin efecto de reserva; conservar protección histórica H06. |
| X01 | Módulo ORDERS/RESERVATIONS deshabilitado | No ejecutar ni ofrecer flujo prohibido; aclaración informativa permitida. |
| X02 | Multi-tenant, sede/producto ajeno, catálogo vacío o cambiante | Ninguna fuga ni hecho inventado; no confirmar cotización invalidada. |
| X03 | «Hola», «Quieor informacion», «¿Cómo hago un pedido?» | Guía específica, lenguaje sencillo y ejemplos `«...»`; sin iniciar operación por pregunta informativa. |
| X04 | 2 mensajes concurrentes, retry, outbox `UnknownDelivery` | Solo un efecto real; nunca reenvío automático de estado incierto. |

**Invariantes de regresión:** H06 y otros contratos congelados, G8 `130/101/29` (no modificar), suites actuales y tres pruebas de proveedores/stores según alcance. Evitar modificar tests para ajustar expectativas incorrectamente; cuando una presentación cambie legítimamente, conservar aserciones de hechos/origen/entidad/efectos.

## 7. Orden de implementación — foco y ahorro de tokens

1. **P0 trazabilidad** (solo lectura del incidente real si el propietario proporciona evidencia local/autorización; primero reproducir con fixture). Si no se dispone de producción, indicar «NO DETERMINADO» y añadir defensas de correlación y pruebas sin inventar logs.
2. **Interpretación de pedidos**: resolver submit, corrección, cierre, descarte y referencia factual mediante el contrato tipado compartido, eliminando bloqueos léxicos injustificados. Mantener núcleo `OrderFlow` de presupuesto/consentimiento y revalidación.
3. **Orientación y presentación**: reutilizar `ResponsePlan` y compositor para preguntas guiadas, no inventar stack paralelo. Rediseñar fallbacks en su lugar de origen; reglas críticas mínimas y verificadas.
4. **Reservas/fechas**: guía con formatos de fecha, horarios verificados, corrección de sede y tratamiento de preguntas, sin tocar `ReservationEngine` salvo defecto demostrado P0.
5. **Validar** pruebas dirigidas primero, suite focal y luego suite actual completa **una vez**. Histórico solo si hay impacto. Sin commit/deploy sin aprobación.

## 8. Límites explícitos

- No nueva base de reglas por palabras, regex o hardcodes del negocio DEMO. No sustitución completa del stack existente.
- No mutar entidades no autorizadas ni transformar preguntas en operaciones.
- No debilitar consentimiento final, revisión/fingerprint, idempotencia, FIFO, CAS/locks, licencia por workspace, tenant isolation, tratamiento `Failed`/`UnknownDelivery`, límites de IA ni transferencia humana.
- No afirmar stock, pago, aprobación del negocio, disponibilidad horaria o diagnóstico sin fuente verificada.
- No migraciones ni cambios de infraestructura, frontend, Oracle, contenedores, webhook Evolution o Firestore salvo que el defecto demostrado exija una autorización separada.
- No tocar ni relajar G8, corpus históricos, pruebas pinneadas o workflows. No commit, push ni deploy en esta intervención.
- Si hay evidencia de efecto no autorizado, no continuar pruebas comerciales sobre el negocio DEMO hasta cerrar P0.

## 9. Entrega exigida a Codex

- Diagnóstico breve con **ruta causal comprobada** por mensaje, decisión semántica, estado antes/después y si IA fue invocada; separar hechos de hipótesis.
- Lista de archivos de backend modificados y razón, diff focal y pruebas añadidas.
- Resultados cuantificados de tests y compilación; advertir si no pudo consultar los mensajes reales de Oracle.
- Guía WhatsApp de ejemplo para pedidos y reservas con frases naturales y hechos verificados, sin presentarlas como captura real.
- P0 reservado/confirmado/no determinado y criterio exacto para habilitar simulaciones.
- `git status`, sin commit/push/despliegue.

**Meta:** que el usuario pueda hablar naturalmente, orientarse y avanzar en pedidos/reservas, mientras el backend sigue siendo la única autoridad sobre lo que se confirma, se cobra, se reserva o se registra.
