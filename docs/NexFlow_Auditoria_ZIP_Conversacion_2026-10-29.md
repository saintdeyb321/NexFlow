# NexFlow — Auditoría del ZIP y conversación WhatsApp del 27/10/2026

**Fecha de revisión:** 29/10/2026  
**Fuentes:** `NexFlow-main.zip` y `Texto pegado.txt` aportados por el propietario.  
**Alcance:** lógica conversacional, lectura de catálogo, edición de reservas, propuestas y presentación. **No** se modificó el código de NexFlow.

## 1. Veredicto

**NO-GO para considerar resueltos los errores conversacionales.** El ZIP contiene mejoras reales, pero la evidencia disponible no acredita que el asistente ya interprete de forma correcta los casos difíciles del diálogo ni que responda de forma natural en WhatsApp. Existen rutas concretas con riesgo de reproducir el problema de alcance del catálogo.

**Límite de evidencia importante:** el ZIP es una copia del árbol de archivos `NexFlow-main` y **no incluye `.git`**. Se auditó el estado contenido en el ZIP, **no** se pudo reconstruir ni verificar independientemente el orden, autoría o delta exacto de los últimos tres commits. Los tres commits previamente mencionados en el proyecto (`d0fd6df`, `a8062cb`, `74de852`) deben confirmarse en la copia Git local con `git log -3` y `git show`. No se ejecutaron compilaciones, pruebas de .NET, servicios externos ni simulaciones en WhatsApp; no se dispone de `dotnet` en este entorno.

La conversación observada el 27/10 **no es una prueba de la versión del ZIP**, dado que los cambios recientes pudieron ocurrir después de esa simulación. Sirve como caso de regresión de aceptación, no como demostración de que el ZIP siga enviando esos mismos textos.

## 2. Hallazgos confirmados en el ZIP

| ID | Estado | Evidencia concreta en código | Implicación |
|---|---|---|---|
| H01 — Catálogo general vs sede | **Riesgo funcional reproducible en ruta estática** | `CommerceTurnResolver.cs:35–44` construye una consulta **Broad** usando `context.SelectedLocationId`. `AiResponseOrchestrator.cs:210–224` enruta interrupciones informativas a `ChatFlow`; `OfferingResponseFormatter.cs:31–33` describe los servicios como de la sede si `LocationId != null`. | Una consulta «¿Qué servicios tienen?» durante una reserva puede devolver solo servicios de la sede, sin mostrar el catálogo global ni explicar la diferencia 5/4. |
| H02 — Consulta amplia interceptada en reserva | **Ruta no alineada con intención general** | `ConversationFlows.cs:260–272` interpreta `ShowOptions` o `ServiceQuery` broad como opciones **reservables en sede**, e incluso vuelve a pedir sede si aún no existe. | La consulta de catálogo puede convertirse en un paso de reserva, no una respuesta informativa. |
| H03 — Explicación 5 vs 4 | **Implementada parcialmente** | `ConversationFlows.cs:641–659` obtiene total activo, reservables por sede e identifica sedes de servicios excluidos; sin embargo, necesita que llegue `ConversationDirective.ExplainServiceScope`. | El cálculo existe; la entrada natural que lo activa no está probada. Tampoco debe asumir que toda diferencia equivale siempre a «exclusivo de otra sede». |
| H04 — Edición diferencial | **Mejora real, validación parcial** | `BookingDraftMerger.cs:19–40,52–65` conserva campos y revalida servicio al cambiar de sede. `ConversationFlows.cs:244–259,308–365,374–384` gestiona slots pendientes y comprueba la nueva hora. | La arquitectura sí permite corregir solo la sede. Falta demostrar con el intérprete real «Editar → La sede → Sanjeronimo» y que no se perdieron menú, pregunta vigente o confirmación. |
| H05 — Referencia ordinal «La 1» | **Prueba de flujo parcial** | `VirtualSecretaryTests.cs:136–145` prueba una lista actual y la elección ordinal, pero no exactamente después de la interrupción y cambio de sede de la transcripción. | Puede estar bien en el escenario sintético y fallar con la secuencia real de pregunta/menu sobreescrito. |
| H06 — Hora de 20:00 | **Mejora real** | `ConversationFlows.cs:374–385` verifica la hora solicitada y `:718–733` prioriza hasta tres alternativas dentro de ±2 horas; `VirtualSecretaryTests.cs:~87–106` verifica que no ofrezca mañanas cuando hay alternativas nocturnas. | El fallo anterior de enumerar 08:30–10:30 frente a «8pm» está mejor abordado. Falta comprobar respuesta y opciones enviadas por el gateway real. |
| H07 — Textos de secretaria | **Mejora cosmética, no redacción contextual** | `SecretaryPresentation.cs:6–25` contiene mensajes más cordiales y ejemplos entre comillas. `OfferingResponseFormatter.cs:13–33` pone nombre, precio y duración en líneas separadas y advierte sobre sedes. Pero `ResponseComposer.cs:17,44–86` tiene IA desactivada por defecto y, cuando se habilita, fundamentalmente reagrupa bloques o añade un saludo fijo. | No se puede considerar resuelto el tono o la capacidad de explicar contextualmente. Activar `EnableAi` por sí solo no concede redacción libre y contextual. |
| H08 — Pedidos sin decir «pedido» | **Pruebas semánticas insuficientes** | `VirtualSecretaryB5Tests.cs:15–30,154–173` encola manualmente `AiInterpretation` de Order/StartOperation. `CommerceTurnResolver.cs:63–87` contiene una ruta de inicio semántico seguro. | Si el proveedor interpreta correctamente la petición, el backend la puede procesar; no está probado que el proveedor lo haga en WhatsApp. |
| H09 — Pruebas que simulan al modelo | **Limitación confirmada** | `VirtualSecretaryTests.cs:~35–45,~55–67,~310–324` y `VirtualSecretaryB5Tests.cs:~21–26,~147–149,~158–169` usan `f.Interpretations.Enqueue(...)`. | Una prueba verde de esas suites no certifica comprensión del modelo real. |
| H10 — Respuesta final y consentimiento | **Preservar** | `ConversationFlows.cs:414–437` presenta resumen y exige confirmación; la creación se hace mediante motor de reservas y mensaje fuente. | No debilitar autorización, validación de disponibilidad ni idempotencia para mejorar la naturalidad. |
| H11 — Staging separado | **Fuera del problema conversacional** | `docs/STAGING_LOCAL.md` explica un proyecto Compose `nexflow-staging` independiente. | No es una corrección semántica. No crearlo ni desplegarlo para este encargo; tampoco es necesario revertirlo sin causa. |

## 3. Comparación con el diálogo aportado

| Mensaje/problema del 27/10 | Lo que el ZIP sí resuelve | Lo no demostrado / defecto restante |
|---|---|---|
| «En la de conce» → pregunta de sede repetida | Hay reconocimiento de entidades y tests con evidencia literal de «conce». | El test carga interpretación ya correcta: falta una conversación real del proveedor. |
| «¿Qué servicios tiene?» → catálogo, luego sede repetida | Existe formato de catálogo más claro. | Las consultas broad pueden pasar por el filtro de sede y continuar el draft; la distinción informativa no es uniforme. |
| «¿Por qué hay 4 si el catálogo muestra 5?» | `ExplainServiceScopeAsync` calcula los números y ofrece explicación por otras sedes. | Depende de activar la directiva semántica adecuada; no garantiza explicación espontánea. |
| «Hoy a las 8pm» → horarios de la mañana | Se comprueba 20:00 y se priorizan alternativas cercanas. | No hay evidencia real de mensaje/menú de Evolution con esa preferencia. |
| «Editar» → «La sede» → «Sanjeronimo» | El borrador preserva los demás campos y revalida los dependientes. | No está demostrada la interpretación natural ni toda la cadena de preguntas/menús. |
| «La 1» y menú no vigente | Existe un test de referencia ordinal sobre el menú vigente. | No reproduce la secuencia exacta de interrupciones de la conversación. |
| Tono seco y listados apretados | Plantillas mejoradas y separaciones de línea. | Compositor no ofrece redacción contextual general. |
| Pedir productos sin la palabra «pedido» | Backend posee ruta para semántica estructurada. | Los tests no llaman al proveedor real de IA. |

## 4. Principio de solución

No «eliminar todas las reglas»: eliminar reconocimiento lingüístico duplicado y plantillas innecesariamente rígidas **solo cuando haya reemplazo verificable**; conservar reglas deterministas de consentimiento, estado, licencias, autorización, tiempo, disponibilidad, catálogos, CAS, idempotencia y control humano.

**Arquitectura deseada:** IA interpreta intención y relaciones contextuales → backend valida y actualiza únicamente campos autorizados → backend aporta hechos verificados → presentación contextual clara en WhatsApp. Para la composición, la IA no puede reescribir datos comerciales sensibles ni inventar horarios; si se desea texto natural, usar estructura de bloques factuales inmutables y slots de lenguaje controlados. Dejar fallback determinista.

## 5. Plan de cierre mínimo (sin nuevos entornos aislados)

1. **Verificación Git local (rápida):** comprobar `git status`, `git log -3 --oneline --decorate` y `git show --stat` de cada uno; preservar cambios locales/staging ya existentes. Aclarar si el ZIP representa el último HEAD.
2. **Corregir enrutamiento:** diferenciar `GlobalCatalog` (todos los servicios activos) de `ReservableAtLocation` (solo servicios reservables allí) en la intención y en `CommerceTurnResolver`, `ConversationFlows` y `AiResponseOrchestrator`. Consulta informativa no debe mutar ni reiniciar borrador.
3. **Edición parcial real:** asegurar `Editar → La sede → [sede]` sin malinterpretar «La sede» como dirección ni reiniciar servicio/fecha/hora/nombre; revalidar solo las dependencias. Revisar vida útil de `ConversationQuestion` y `RecoveryOptions` para ordinales.
4. **Presentación:** introducciones y explicaciones humanas, nombres claros, 1 servicio por bloque, «S/» y duración; mostrar hasta 5 por página con continuidad y PDF **solo si existe**. Presentar franjas próximas verificadas y aclarar que pueden indicar otra hora. Una sola pregunta por mensaje. No prometer stock/agenda.
5. **Pedidos naturales:** usar la interpretación semántica existente para reconocer solicitudes sin palabra «pedido» y distinguir preguntas de órdenes. No permitir envíos hasta revisión y confirmación explícita.
6. **Pruebas indispensables y económicas:** 6–10 tests focalizados para invariantes y transcript; `dotnet build backend/backend.slnx --no-restore`; suites afectadas. No recrear staging ni correr suites masivas iterativamente. Mantener los gates oficiales para el release definitivo.
7. **Prueba real:** tras revisar diff y aprobar gates del SHA, desplegar en la infraestructura DEMO existente mediante la sustitución controlada solo de `api`. Simular por WhatsApp los casos concretos de la transcripción con el negocio ficticio y comprobar el estado persistido. No usar pacientes/clientes reales. Corregir únicamente fallos observados; no declarar GO por tests con `Enqueue`.

## 6. Aceptación real mínima

- A1: «En la de conce» selecciona la sede válida y no insiste en preguntarla.
- A2: «¿Qué servicios tienen?» devuelve catálogo general aunque haya reserva en preparación; explica el alcance.
- A3: «¿Por qué 5 y aquí 4?» explica diferencia dinámica desde datos actuales sin inventar exclusividades.
- A4: «Editar → La sede → Sanjeronimo» cambia solo sede y preserva datos compatibles; revalida hora/servicio.
- A5: «La 1» refiere al último menú válido o pide aclaración adecuada, sin actuar sobre menú antiguo.
- A6: «Hoy 8pm» comprueba 20:00; alternativas próximas reales, no horas lejanas arbitrarias.
- A7: Solicitud coloquial de productos sin «pedido» crea borrador, no pedido confirmado.
- A8: «Confirmar» solo sobre revisión vigente produce como máximo un efecto comercial.
- A9: Mensajes de catálogo, horarios y corrección son legibles, informativos y no repiten preguntas respondidas.
- A10: takeover y rechazo de petición administrativa peligrosa no cambian datos.

## 7. Qué NO certifica esta auditoría

- No se confirmó SHA/head exacto del ZIP ni los últimos tres diffs por ausencia de `.git`.
- No se ejecutaron tests ni compilación en este entorno.
- No se llamó al proveedor real ni al webhook de Evolution.
- No se verificó el estado de Oracle posterior al último reporte del usuario (imagen `b581e58`).
- No se autoriza desplegar este ZIP directamente: requiere un commit verificable y los gates exigidos para ese SHA.

**Decisión:** implementar una corrección dirigida a H01–H09, con aceptación manual sobre WhatsApp DEMO. Evitar otra infraestructura y otra refactorización global.
