# NexFlow — Diagnóstico de conversación real y plan de corrección del MVP

**Fecha:** 26 de octubre de 2026  
**Base del análisis:** transcripción WhatsApp aportada por el propietario y lectura del código en el commit desplegado `cedd19469ca715616eb53d2cfd4d47732870b94e` de [`saintdeyb321/NexFlow`](https://github.com/saintdeyb321/NexFlow/tree/cedd19469ca715616eb53d2cfd4d47732870b94e).  
**Estado:** informe de diagnóstico y propuesta; **no** modifica código, datos, GitHub ni Oracle.  
**Entorno:** negocio ficticio odontológico y contacto autorizado de prueba. Datos de contacto y nombre del paciente deliberadamente omitidos.

## 1. Resumen ejecutivo

La nueva API se desplegó y respondió `Healthy` en PostgreSQL, Redis y Firestore; la transcripción demuestra que *infraestructura saludable no equivale a conversación funcional correcta*. El bot **sí** crea reservas, lee productos, conserva borradores y calcula precios; falla cuando el interlocutor se expresa de manera natural, corrige lo ya dicho o intenta finalizar un pedido. La respuesta tiene problemas de composición visual y a veces no contesta la pregunta exacta.

**Conclusión principal:** no atribuir estos defectos únicamente a “falta de prompt”, “amnesia” o “datos mal enviados”. El código posee una mezcla de gramáticas deterministas, detectores de actos, estados de flujo, consultas autorizadas y una IA que solo interpreta ciertos turnos. Algunas rutas fallidas ocurren **antes** de llegar a una respuesta generativa. Hay evidencia de restricciones concretas que explican varios fallos, mientras otros requieren trazas sanitizadas para localizar la capa exacta.

**Prioridad:** P0 para bloqueo de envío/cambio de pedido y correcciones ignoradas de un borrador; P1 para disponibilidad, comprensión de errores ortográficos, recuento por sede y presentación; P1 de seguridad/UX para aclarar gestión de una reserva ya registrada sin saltarse verificación de titularidad.

## 1-A. Principio de arquitectura obligatorio: comprensión generativa, ejecución determinista

**Prohibido resolver esta auditoría agregando otro árbol de reglas, más expresiones regulares, nuevas listas de palabras clave/sinónimos o respuestas predefinidas para cada frase.** Los 15 casos de prueba son muestras del fallo, no un catálogo cerrado de expresiones humanas. Una solución que solo reconoce los enunciados de esta transcripción reproduce el defecto original.

Diseño requerido:

1. **Comprender:** la IA semántica contextual interpreta el mensaje junto con el estado real del diálogo y emite un acto estructurado acotado: qué quiere hacer, sobre qué entidad, qué valor corrige/niega, qué queda ambiguo y qué evidencia del mensaje lo sustenta. Debe comprender frases no vistas y errores de escritura; el código no depende de una expresión literal.
2. **Validar:** el backend contrasta la propuesta con workspace/licencia, datos actuales del catálogo/sede/agenda, origen, versión CAS y autorización. La IA nunca elige un ID inexistente, confirma existencias no verificadas, registra sin consentimiento o edita una reserva confirmada sin verificación. Ante duda se solicita **una aclaración contextual**, sin efectos.
3. **Ejecutar:** el motor transaccional actual aplica únicamente cambios validados, con hash de revisión, confirmación explícita e idempotencia; no se crea un segundo árbol que replique los flujos.
4. **Responder:** presentar los hechos verificados en un mensaje WhatsApp legible. **Sí** se admiten formatos estructurados para resumen de reserva, pedido, disponibilidad y preguntas de confirmación, porque ordenan hechos; **no** se admiten respuestas prefabricadas que pretendan adivinar lo que significa cualquier conversación.

**Antipatrones rechazados:** añadir `"envia solicitud"` a un whitelist para dar T03 por verde; detectar correcciones exclusivamente por `"cambia"`; codificar `"bocal"→"bucal"` o `"conce"→"Concepción"` como excepciones privadas; asumir que toda mención de producto significa `Add`; crear un gigantesco switch `intención → frase de respuesta`. Los validadores de IDs, números, consentimiento explícito y demás invariantes de negocio **siguen siendo deterministas**, por razones de seguridad.

**Prueba de generalización:** además de T01–T15, Codex debe agregar un conjunto de paráfrasis inéditas (no copiadas de los ejemplos), negaciones y reparaciones en distintos momentos del flujo, y demostrar que el mismo mecanismo interpreta sin introducir condiciones especiales por cada expresión.

## 2. Evidencia observada y valoración

| ID | Turno o situación resumida | Observación | Severidad | Comportamiento correcto |
|---|---|---|---|---|
| D-01 | Pedido con dos artículos; «Solo eso», «Envía la solicitud», «Que envíes la solicitud», «Envía solicitud para revisión» | Repite «No identifiqué un producto activo inequívoco»; no registra solicitud | **P0** | Priorizar intención de *finalizar* sobre búsqueda de producto, mantener borrador, presentar resumen vigente y confirmación explícita; crear **una sola** solicitud `PendingReview` tras confirmación válida |
| D-02 | Ante el bloqueo de envío, el usuario repite «El hilo dental» | La cantidad sube de 1 a 2 sin que la persona pida añadir otra unidad | **P0** | No inferir incremento de cantidad en un estado de revisión/confirmación. Pedir cantidad si la intención es ambigua; agregar solo ante petición de añadir inequívoca |
| D-03 | «Hilo dental y enjuague bocal»; «También un enjuague bocal» | No reconoce el segundo artículo hasta que se escribe «bucal» | **P1** | Corrección ortográfica acotada/alias para catálogo autorizado; interpretar dos entidades en un turno sin perder lo válido ni inventar IDs |
| D-04 | «Espera, me confundí; no quiero limpieza dental, quiero extracción dental» | Bot sigue describiendo «Limpieza dental» y preguntando fecha | **P0** | Detectar corrección explícita; reemplazar servicio **en el borrador no confirmado**, invalidar hora/slot si el servicio cambia y continuar con extracción si es reservable en la misma sede |
| D-05 | «Que no quiero limpieza dental, quiero extracción» | Insiste en información sobre el servicio anterior | **P0** | La negación del valor anterior **no** debe bloquear la extracción del nuevo valor; tras segunda corrección, no insistir ni repetir una pregunta anterior |
| D-06 | «¿Está disponible el hilo dental?» | Responde detalles de dos productos y no dice claramente si hay existencias | **P1** | Contestar **sobre el producto pedido**: «Figura en el catálogo; no puedo confirmar existencias en tiempo real» si no hay inventario autorizado. No sugerir stock garantizado |
| D-07 | «¿Dije si está disponible o no?» / «¿Qué?» | Fallback genérico «¿Qué información necesitas?» | **P1** | Detectar reparación/queja referida a la respuesta anterior; aclarar el límite de stock sin borrar el tema ni iniciar una operación |
| D-08 | «En conce» para elegir sede | No reconoce abreviación; «Concepción» sí | **P1** | Admitir abreviaturas inequívocas registradas; si colisionan, enumerar candidatos y pedir precisión, sin seleccionar a ciegas |
| D-09 | Reserva confirmada; «Quiero editar mi reserva» | Transfiere a verificación humana | **No es defecto de seguridad** | Explicar claramente que es una reserva **ya registrada**; requiere prueba de titularidad. No habilitar mutación por mero teléfono o nombre |
| D-10 | Sede San Jerónimo lista 5; sede Concepción lista 4 | Diferencia entre sedes, no necesariamente omisión | **A verificar** | Comparar artículos reales por sede, `IsActive`, `RequiresReservation`, duración y `LocationScope/LocationIds`. Si son 5 elegibles en Concepción y se entregan 4, entonces registrar defecto de lectura/paginación |
| D-11 | Confirmaciones y catálogo con textos largos pegados | Lectura poco natural, opciones implícitas | **P1** | Mensajes segmentados con `\n\n`, títulos, lista numerada/viñetas, **una** pregunta final y comandos procesables («Confirmar», «Editar», «Descartar») |
| D-12 | «Tras pasado mañana a las 6 de la tarde» | Se fijó el 28/10/2026 sin aclaración | **P1 a evaluar** | La expresión no es inequívoca: aclarar fecha absoluta antes de prometer el horario y verificar agenda/horarios; no asumir automáticamente 2 o 3 días |
| D-13 | Tras «Asesor», vuelve a intervenir el bot en una nueva reserva | Falta evidencia para saber si se liberó correctamente el takeover | **Investigar** | Confirmar si pasó de modo `Human` a `AI` mediante acción autorizada; nunca responder autónomamente después de takeover activo |

> La transcripción muestra el síntoma, **no** revela directamente la salida del clasificador, la respuesta de Gemini/Groq ni los snapshots de Firestore. No se afirma haber inspeccionado esos datos productivos.

## 3. Hallazgos del código: hipótesis con respaldo preciso

### H-01 — Gramática de finalización demasiado restrictiva (**alta confianza**)

[`OrderMentionResolver.IsSubmit`](https://github.com/saintdeyb321/NexFlow/blob/cedd19469ca715616eb53d2cfd4d47732870b94e/backend/NexFlow.Application/Features/Automation/Dialogue/Understanding/OrderMentionResolver.cs#L22-L29) permite variantes de `envia/enviar/finaliza`, pero también obliga a que **todas** las palabras estén en una lista muy corta. Por eso «Envía la solicitud», «Envía solicitud para revisión» y «Que envíes la solicitud» pueden ser clasificadas como texto de producto y terminar en «No identifiqué un producto...». [`OrderMentionResolver.IsCandidate`](https://github.com/saintdeyb321/NexFlow/blob/cedd19469ca715616eb53d2cfd4d47732870b94e/backend/NexFlow.Application/Features/Automation/Dialogue/Understanding/OrderMentionResolver.cs#L30-L46) devuelve `true` para cualquier texto si `CurrentGoal == ORDER`.

[`CommerceTurnResolver`](https://github.com/saintdeyb321/NexFlow/blob/cedd19469ca715616eb53d2cfd4d47732870b94e/backend/NexFlow.Application/Features/Automation/Dialogue/Understanding/CommerceTurnResolver.cs#L42-L70) decide `submit` antes de buscar ítems, pero si no lo reconoce continúa a `orders.Resolve(...)` y produce `Clarify`. [`OrderFlow`](https://github.com/saintdeyb321/NexFlow/blob/cedd19469ca715616eb53d2cfd4d47732870b94e/backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/Flows/OrderFlow.cs#L77-L133) devuelve inmediatamente la aclaración, sin tramitar el envío. **La corrección no puede ser solo cambiar el prompt de IA.**

### H-02 — Semántica del borrador y riesgo de doble agregado (**alta confianza**)

`OrderMentionResolver.Resolve` usa `OrderEditKind.Add` como opción por defecto cuando la frase no parece quitar, reemplazar o fijar cantidad. [`OrderDraftMerger.Apply`](https://github.com/saintdeyb321/NexFlow/blob/cedd19469ca715616eb53d2cfd4d47732870b94e/backend/NexFlow.Application/Features/Automation/Dialogue/Policy/OrderDraftMerger.cs) aplica esas ediciones al borrador. Repetir el nombre de un artículo durante una revisión puede interpretarse como **agregar otra unidad**. Reforzar la precedencia de comandos, el estado y la intención de *corrección/confirmación*; **no** hacer que todos los nombres de artículo se conviertan globalmente en operaciones `Set`, porque rompería «agrega otra unidad».

También preservar el contrato F7-01: huella (`Fingerprint`) del presupuesto vigente, consentimiento con productos/precios/cantidades visibles, revalidación contra licencia y catálogo al persistir y unicidad/idempotencia por mensaje fuente; un intento reanudado no debe crear duplicados. [`OrderFlow`](https://github.com/saintdeyb321/NexFlow/blob/cedd19469ca715616eb53d2cfd4d47732870b94e/backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/Flows/OrderFlow.cs#L99-L205).

### H-03 — Corrección de servicio rechazada o despriorizada (**alta confianza de restricción; causa exacta por trazar**)

En [`AiInterpreter`](https://github.com/saintdeyb321/NexFlow/blob/cedd19469ca715616eb53d2cfd4d47732870b94e/backend/NexFlow.Application/Features/AI/Interpretation/AiInterpreter.cs) existe `ConversationDirective.ChangeService`, pero su validación de salida semántica exige evidencia de `cambi...` u «otro servicio». «Me confundí», «no quiero limpieza, quiero extracción» pueden expresar cambio sin esas palabras. Además, [`DialoguePolicy.Classify`](https://github.com/saintdeyb321/NexFlow/blob/cedd19469ca715616eb53d2cfd4d47732870b94e/backend/NexFlow.Application/Features/Automation/Dialogue/Policy/DialoguePolicy.cs#L21-L51) trata muchos turnos con `no` como ambigüedad; [`TurnUnderstandingPipeline`](https://github.com/saintdeyb321/NexFlow/blob/cedd19469ca715616eb53d2cfd4d47732870b94e/backend/NexFlow.Application/Features/Automation/Dialogue/Understanding/TurnUnderstandingPipeline.cs) combina gramática local, consulta de entidades y validación de la propuesta.

La escritura segura **ya existe** en [`BookingDraftMerger.ApplyAsync`](https://github.com/saintdeyb321/NexFlow/blob/cedd19469ca715616eb53d2cfd4d47732870b94e/backend/NexFlow.Application/Features/Automation/Dialogue/Policy/BookingDraftMerger.cs#L19-L73): sustituye el servicio validado y limpia la hora si cambia. Hay que conseguir que la intención llegue con evidencia válida a esa ruta, sin permitir mutaciones basadas en texto de la IA no autorizado.

### H-04 — Edición de reserva confirmada bloqueada intencionalmente (**confirmado; no quitar**)

[`ExistingReservationFlow.TryHandleAsync`](https://github.com/saintdeyb321/NexFlow/blob/cedd19469ca715616eb53d2cfd4d47732870b94e/backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/Flows/ExistingReservationFlow.cs#L28-L70) **transfiere a asesor por diseño** porque un teléfono, nombre o contexto previo no demuestra titularidad. El texto enviado en tu prueba coincide con ese comportamiento. **No es igual** corregir el borrador activo que modificar una reserva confirmada. Cualquier autoservicio de una reserva registrada requiere otro proyecto de verificación de identidad/propiedad explícitamente autorizado; no desactivar la puerta por conveniencia.

### H-05 — Catálogo por sede, filtro reservable y límite de listado (**código confirmado; datos reales pendientes**)

[`OfferingService.GetServicesAsync` y `FilterItems`](https://github.com/saintdeyb321/NexFlow/blob/cedd19469ca715616eb53d2cfd4d47732870b94e/backend/NexFlow.Application/Features/Business/Offerings/OfferingService.cs#L35-L92) filtran por `IsActive`, `LocationScope == ALL` o `SPECIFIC` y `LocationIds`. El flujo reservable exige además `RequiresReservation` y duración válida (>=5). El formateador de opciones usa [`services.Skip(offset).Take(5)`](https://github.com/saintdeyb321/NexFlow/blob/cedd19469ca715616eb53d2cfd4d47732870b94e/backend/NexFlow.Application/Features/Business/Offerings/OfferingService.cs#L164-L175). Para catálogo amplio, [`CommerceTurnResolver`](https://github.com/saintdeyb321/NexFlow/blob/cedd19469ca715616eb53d2cfd4d47732870b94e/backend/NexFlow.Application/Features/Automation/Dialogue/Understanding/CommerceTurnResolver.cs#L136-L140) también usa `Take(5)`.

En la transcripción, **San Jerónimo enumera cinco y Concepción cuatro**. No está probado que sea un error: podría cambiar la elegibilidad por sede. Para cada sede, cotejar **IDs, flags y motivos de exclusión** con el repositorio autorizado; no forzar que ambas listas tengan el mismo tamaño ni presentar un servicio no reservable como reservable. Si hay más de cinco, ofrecer paginación estable y respuesta completa por páginas (no omisión silenciosa).

### H-06 — Coincidencias ortográficas y preguntas de disponibilidad (**confianza media-alta**)

[`SpanishTextNormalizer`](https://github.com/saintdeyb321/NexFlow/blob/cedd19469ca715616eb53d2cfd4d47732870b94e/backend/NexFlow.Application/Features/Automation/Dialogue/Understanding/SpanishTextNormalizer.cs) quita tildes y signos, **no** convierte `bocal` en `bucal` ni `conce` en `Concepción`. [`OrderMentionResolver.Match`](https://github.com/saintdeyb321/NexFlow/blob/cedd19469ca715616eb53d2cfd4d47732870b94e/backend/NexFlow.Application/Features/Automation/Dialogue/Understanding/OrderMentionResolver.cs) depende del conjunto de opciones del catálogo. Implementar aliases auditables y fuzzy matching **con umbral y margen de desambiguación** solo sobre entidades autorizadas. Rechazar desconocidos en vez de inventarlos.

[`OfferingResponseFormatter.Format`](https://github.com/saintdeyb321/NexFlow/blob/cedd19469ca715616eb53d2cfd4d47732870b94e/backend/NexFlow.Application/Features/Automation/Dialogue/Composition/OfferingResponseFormatter.cs#L21-L36) ya dice que aparecer en el catálogo no confirma stock, pero el turno real exhibió dos productos y respondió tarde al sí/no; investigar `OfferingQueryPlan.Offerings`, antecedente/correferencia y selección en `CommerceTurnResolver`, no asumir que Firestore envió artículos erróneos.

### H-07 — Compositor demasiado conservador para buena UX (**alta confianza**)

[`ResponseComposer.ComposeAsync`](https://github.com/saintdeyb321/NexFlow/blob/cedd19469ca715616eb53d2cfd4d47732870b94e/backend/NexFlow.Application/Features/Automation/Dialogue/Composition/ResponseComposer.cs#L38-L86) **no pide a la IA redactar libremente las operaciones**: protege hechos y, si está habilitado, antepone frases como «Perfecto.» + texto determinista; la IA opcional solo puede reagrupar bloques informativos autorizados. El `OrderDraftMerger` y otros flujos ya generan saltos `\n`, de modo que **no está demostrado** que Evolution elimine los saltos. Verificar el texto en tres puntos sanitizados: composición en backend, `OutboundMessageService.PrepareTextAsync` y payload saliente a Evolution; comprobar formato en WhatsApp real. Mejorar mediante **plantillas deterministas por tipo de operación** con estructura legible y secciones, no mediante una IA libre que altere precios, consentimiento u operaciones.

## 4. Diseño conversacional propuesto (WhatsApp, humano y seguro)

### 4.1 Principios

1. **IA comprende expresiones → motor autoriza y ejecuta → compositor comunica.** Nunca IA sola escribe o “confirma” reservas, pedidos, inventario o disponibilidad.
2. **Una respuesta = estado actual reconocido + información necesaria + una acción/pregunta concreta.** Saltos de párrafo con `\n\n` y listas cortas, sin párrafos largos pegados.
3. **Opciones canónicas y sinónimos controlados.** Mostrar «Confirmar», «Editar», «Descartar», pero aceptar `sí`, `está bien`, `envía la solicitud`, `me equivoqué`, etc. según el *estado activo*. Una palabra de confirmación fuera de estado válido nunca genera un efecto.
4. **Corrección como acción de primer nivel.** Si la persona corrige una sede, servicio, fecha, hora o cantidad, volver a validar ese campo, invalidar los dependientes, mostrar resumen nuevo y exigir consentimiento nuevo.
5. **Priorizar el contexto vigente sin encerrar al usuario.** Una pregunta informativa puede interrumpir, responder y luego retomar; una reparación («dije disponible») debe volver a la pregunta previa. Nunca convertir una queja en una orden.
6. **No inventar stock, horarios, servicios, precio, cantidad, identidad ni estado de entrega.** Indicar explícitamente las limitaciones del dato disponible.
7. **Formato multiplataforma.** Emojis discretos, UTF-8, saltos de línea preservados, un límite de longitud acordado y sin HTML. No partir una transacción en mensajes independientes que induzcan confirmaciones incompletas.

### 4.2 Ejemplos de **texto de salida esperado** (no sustituyen reglas de negocio)

**Reserva — revisar borrador antes de registrar**

```text
🦷 *Revisa tu reserva*

🏥 Sede: San Jerónimo
🩺 Servicio: Evaluación odontológica
📅 Fecha: 28/10/2026
🕕 Hora: 6:00 p. m.
👤 Paciente: [NOMBRE DE PRUEBA]

¿Está todo correcto?
✅ *Confirmar* — registrar la reserva
✏️ *Editar* — cambiar sede, servicio, fecha, hora o nombre
❌ *Descartar* — eliminar este borrador

También puedes responder «Sí» o decir qué deseas cambiar.
```

**Reserva — corrección mientras se prepara**

```text
✏️ Entendido: cambiamos *Limpieza dental* por *Extracción dental simple*.

🏥 Sede: Concepción
🩺 Servicio: Extracción dental simple

La hora anterior debe comprobarse nuevamente para este servicio.
📅 ¿Qué fecha prefieres?
```

**Pedido — confirmación explícita**

```text
🛍️ *Tu solicitud para revisión*

• 1 × Hilo dental 50 m — S/ 12.90
• 1 × Enjuague bucal sin alcohol 250 ml — S/ 24.00

📍 Sede: Concepción
💰 Subtotal del catálogo: *S/ 36.90*
ℹ️ La tienda confirmará existencias y condiciones; todavía no es una venta.

✅ *Confirmar* — enviar esta solicitud
✏️ *Editar* — modificar productos o cantidades
❌ *Descartar* — eliminar el borrador
```

**Disponibilidad de producto sin inventario en tiempo real**

```text
🧵 *Hilo dental 50 m*

✅ Figura en el catálogo de productos.
⚠️ No tengo confirmación de existencias en tiempo real.

¿Deseas agregarlo a una solicitud para que el negocio lo verifique?
```

**IMPORTANTE:** fecha, sede, precios y productos de estos ejemplos son meramente los valores ficticios comunicados en la transcripción, no validaciones productivas. Los mensajes reales deben construirse de entidades y montos **revalidados** en el motor; no incrustar esos valores como constantes.

## 5. Plan de implementación por prioridad, sin reescribir arquitectura

### P0-A — Arreglar pedidos sin efectos accidentales

- Distinguir actos de negocio como `SubmitRequested`, `ConfirmationReceived`, `EditRequested`, `DiscardRequested`, `InformationRequested`, `Add/Set/RemoveItems`, aprovechando la interpretación semántica contextual existente; **no** crear un clasificador nuevo a base de palabras/frases. El backend comprueba qué actos son legales según el estado del borrador **antes** de aplicar el matcher de catálogo.
- Comprender semánticamente solicitudes de cierre, revisión, corrección y descarte expresadas de formas **no enumerables**; las frases «solo eso», «enviar solicitud», «envía para revisión», «confirmar», «no, cambia...» son **ejemplos de tests, no patrones codificados**. «Solo eso» no equivale a consentimiento final fuera de pantalla de revisión.
- Formalizar estados `ORDER_COLLECT_ITEMS → ORDER_LOCATION (si aplica) → ORDER_REVIEW:<hash> → ORDER_CONFIRM:<hash> → ORDER_COMMIT:<hash> → PendingReview`; reutilizar nombres existentes si dan el mismo contrato. No cambiar el modelo durable innecesariamente.
- Una confirmación solo aplica al resumen **visible, actual e idéntico** (`Fingerprint`, workspace, source, StateVersion, precios y cantidades); tras cambios de catálogo/licencia/sede, recalcular resumen y volver a pedir confirmación.
- Cuando el usuario ya ve un resumen y menciona un producto existente de forma ambigua, preguntar qué acción desea, **no sumar una unidad por defecto**. Cada `MessageId` debe producir como máximo un efecto durable; reintentos no duplican pedido/avisos.
- Resolver textos con dos ítems por coincidencia inequívoca; ante un ítem ambiguo mostrar el candidato y preservar correctamente los demás según estrategia transaccional del turno (no aplicar parcialmente sin explicación).

### P0-B — Corrección real de reservas en curso

- Distinguir `DraftReservationEdit` frente a `RegisteredReservationManagement` mediante interpretación contextual de la corrección y sus entidades; «me confundí», «no era X, quiero Y», «en vez de limpieza quiero extracción» son **muestras de prueba**, no una lista nueva de frases gatillo. La negación del valor anterior no debe destruir la evidencia del nuevo.
- Priorizar reemplazo explícito de servicio sobre consulta de precio/duración del servicio previamente elegido; si faltan datos, pedir **solo** el dato ambiguo.
- Resolver `Y` contra servicios `SERVICE`, activos, habilitados para reservar en la sede, duración y scope; no inventar entidades ni activar servicio excluido.
- Aplicar cambios mediante `BookingDraftMerger`/flujo real usando control CAS; invalidar horas y opciones no compatibles; confirmar resumen actualizado antes de crear. Si la solicitud es para reserva ya confirmada, **mantener verificación humana**.

### P1-C — Disponibilidad, reparación y selecciones coloquiales

- Respuestas específicas por tipo de `OfferingQuestion`: para `PRODUCT` con `Availability/Stock`, «en catálogo» ≠ «stock disponible»; para `SERVICE` ofrecido en sede ≠ hueco de agenda. Evitar retornar un segundo producto si el usuario solo preguntó por uno.
- Correferencias («¿está disponible?», «dije disponible», «ese mismo») deben usar referencia vigente y vencimiento/alcance; si no hay referencia inequívoca, preguntar el artículo sin repetir información falsa.
- Abreviaturas y typos («bocal»/«bucal», «sanj eronimo», «conce») resueltos **contra entidades y alias autorizados del workspace en ejecución**, con margen de confianza conservador y desambiguación; **sin** tablas manuales por frase ni coincidencias difusas entre tenants. Cache/lecturas mínimas.

### P1-D — Listado completo y razón por sede

- Comparar para **cada sede** los cinco servicios observados con datos autorizados. Reportar por ID: `IsActive`, `RequiresReservation`, `DurationInMinutes`, `LocationScope`, `LocationIds`, motivo de exclusión.
- Implementar paginación estable si hay >5 resultados. Texto «mostrando 1–5 de N; escribe Ver más». Evitar confundir índice de menú con índice de catálogo/PDF; conservar el ID del ítem real.
- No alterar flags reales para hacer coincidir cantidades. Las sedes pueden tener listas válidas diferentes.

### P1-E — Composición visual de resultados verificados, sin árbol de respuestas

- Formatos de **resultados estructurados y validados** `BookingReview`, `OrderReview`, `OrderConfirmed`, `OrderPendingReview`, `Availability`, `LocationList`, `ServiceList`, `ErrorRepair`, `Handoff` con separaciones `\n\n`, listas, emojis discretos, un CTA final y longitud máxima. **Son plantillas de presentación de datos, no respuestas predefinidas a frases del cliente.**
- Conservar textualmente montos/monedas/fechas/identidades/limitaciones y orden de hechos; escapes apropiados de `*`/`_` y caracteres aportados por clientes. No duplicar encabezados ni preguntas.
- Verificar si saltos de línea sobreviven en `ResponseComposer` → `OutboundMessageService` → gateway Evolution → WhatsApp. Corregir capa que realmente los pierde si ocurre; no culpar a Evolution sin evidencia.
- IA opcional solo para variación del tono/agrupación verificada, con fallback local, licencia/cuotas y protección anti-inyección. Priorizar plantillas deterministas en estados transaccionales.

### P2-F — Observabilidad y pruebas de aceptación

- Añadir trazas **sanitizadas** por turno: `workspace alias`, hash no reversible de `sourceMessageId`, `CurrentGoal/CurrentStep`, `Act`, `Intent`, detector que ganó, estado de resolución (`Determined/Ambiguous/Insufficient`), transición, operación (`no-op/draft/edit/commit`), `Image/SHA`, sin conversaciones o números de teléfono en logs. Instrumentar en todos los pasos importantes para responder *dónde* se pierde la intención.
- Tests de orquestación completos con proveedor IA falso y lectura real del catálogo fixture, y tests de regresión del harness existente. Ejecutar backend/CI vigente, integración con dependencias efímeras y frontend si resulta afectado. No declarar PASS por test que no ejecutó.

## 6. Matriz mínima de aceptación (redacción exacta de la prueba de usuario)

| Caso | Input | Aserción obligatoria |
|---|---|---|
| T01 | `Hilo dental y enjuague bocal` | Dos productos correctos si la coincidencia es única; si no, aclaración concreta sin mutación incorrecta |
| T02 | `Solo eso` en ORDER_REVIEW | Mantiene cantidades; pasa a confirmar el resumen, **0 escrituras de pedido** |
| T03 | `Envía la solicitud` y `Envía solicitud para revisión` | Comando entendido, no error de nombre de producto; solo envía tras consentimiento final correcto |
| T04 | `Confirmar` / `Sí` / `Confirmo` en ORDER_CONFIRM con hash vigente | Exactamente una solicitud `PendingReview`; retransmisión mismo source id = 0 duplicados |
| T05 | `El hilo dental` después de error de envío | No incrementa automáticamente de 1 a 2 |
| T06 | `Me confundí, quiero extracción dental en lugar de limpieza` | Borrador cambia a ID extracción, limpia hora incompatible, 0 reservas creadas antes de confirmar |
| T07 | `Que no quiero limpieza, quiero extracción` | No repite precio/descripción de limpieza ni mantiene la selección antigua |
| T08 | `Quiero editar mi reserva` tras reserva confirmada | **Handoff/verificación de titular**, 0 modificación sin autenticación |
| T09 | `¿Está disponible el hilo dental?` / `Dije si está disponible` | Solo producto solicitado y distinción explícita catálogo/stock; nunca sí a existencias no verificadas |
| T10 | `En conce` / `sanj eronimo` | Selecciona únicamente coincidencia inequívoca; si no, pide aclaración sin seleccionar |
| T11 | San Jerónimo (5) vs Concepción (4) | Recuento por sede coincide con fuente de verdad; todos los ítems elegibles listados/paginados |
| T12 | Reserva con «tras pasado mañana» | Si es ambiguo, pide fecha absoluta antes de reservar |
| T13 | `Asesor`, luego `Quiero hacer una nueva reserva` | Si modo Human sigue activo, bot no contesta ni persiste; tras liberación válida puede reanudar |
| T14 | Textos multilínea en WhatsApp | Saltos y una sola pregunta/CTA final sobreviven a render, gateway y proveedor |
| T15 | Revocar licencia/cambiar precio entre resumen y confirmación | `NO ORDER`, nueva cotización/denegación, sin consentimiento reutilizado; F7-01 no reaparece |

**Límites a preservar:** aislación multitenant, roles/licencia por módulo, validación del catálogo/sede, precios minor units, consentimiento previo, idempotencia inbound/outbox, CAS, `UnknownDelivery` sin reenvío ciego, takeover humano, validación de propietario de reserva registrada y órdenes `PendingReview` sin cobro/stock garantizado.

## 7. Entrega, implementación y go-live

1. **Codex en Windows:** trabajar en `C:\Proyectos\NexFlow`; inspeccionar `git status` y SHA real antes de tocar código (en GitHub `main` estaba en `20416e2…`, un commit después de la candidata por cambios de custodia, sin alteraciones del backend). Entregar diff acotado, tests y evidencia; no implementar lógica exclusivamente en n8n o prompts.
2. **QA local / CI:** reproducir primero fallos con tests rojos, corregir, ejecutar suite local completa y controles de regresión que existan. No desactivar históricas incompatibles ni cambiar expectativas para simular verde.
3. **Operador Oracle (fase posterior y separada):** nueva versión tendrá **otro SHA e Image ID**. No editar binarios en el servidor, no `git pull` sobre la release desplegada ni reemplazar imagen hasta aprobar el nuevo CI, conservar backup/recuperación, compatibilidad y probar en DEMO.
4. **Criterio de salida:** P0 resueltos sin efectos no autorizados; T01–T15 aprobados o bloqueados explícitamente con causa; estilo legible validado en WhatsApp; sin P0/P1 nuevos. Piloto DEMO ≠ comercialización y no acredita recuperación Firestore.

## 8. Qué no sabemos todavía

- Si la diferencia 5/4 se debe a `LocationScope`, flag de reserva, duración, datos de sede o paginación: requiere lectura sanitaria real por ID.
- En qué punto se pierde el formato (si se pierde): la transcripción copia mensajes, pero código y pruebas contienen `\n`; se necesita prueba end-to-end del payload real.
- Cuál fue `Intent/Act/Directive` para los mensajes de corrección, ni si Gemini/Groq tuvo fallback/corte por cuota: requiere trazas sin PII.
- Si el takeover humano se levantó antes de volver a escribir: verificar estado/reanudación autorizada; no deducirlo de mensajes aislados.
- Si «tras pasado mañana» era intención de 28/10 o 29/10: no existe aclaración del usuario en el registro.

**Dictamen:** refactorización instalada con infraestructura saludable, **no** apta todavía para declarar calidad conversacional aceptada. Recomiendo corrección enfocada de semántica contextual + estado de pedido y reserva + composición, no rediseño del SaaS.
