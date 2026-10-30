# PROMPT PARA CODEX — NEXFLOW / REPARACIÓN CONVERSACIONAL WHATSAPP

Trabaja en `C:\Proyectos\NexFlow`. Candidato publicado/desplegado inicial: `aca4d7397d09a522c529d039fa21bb9c655cc158`. El negocio de simulación es «Sonrisa Andina Dental — DEMO», pero la solución debe ser SaaS, independiente de sector y workspace. **No hagas commit, push, workflows, operaciones remotas, cambios en Oracle, despliegue ni migraciones.** No tocar producción ni contactar números de WhatsApp. Conserva modificaciones locales previas sin sobrescribirlas.

**Lee primero** `docs/NEXFLOW_PLAN_MEJORA_CONVERSACIONAL_WHATSAPP_2026-10-30.md` (si lo coloqué allí; de lo contrario, el archivo adjunto con ese nombre). Es el contrato de trabajo y contiene la transcripción real, el diagnóstico inicial, matriz de aceptación e invariantes. Antes de implementar, confirma HEAD y estado Git. Si el plan no está disponible, pídelo como archivo; no ejecutes cambios a ciegas.

## Objetivo

El asistente debe entender lenguaje natural de pedidos igual o mejor que reservas, contestar dudas concretas del catálogo, orientar paso a paso y redactar mensajes WhatsApp claros. La IA interpreta semántica/contexto y **el backend determinista** autoriza entidades, cotizaciones, reservas/pedidos, idempotencia, privacidad, consentimiento y entrega. Reducir fallbacks repetitivos; no sustituirlos por reglas léxicas adicionales ni permitir que un LLM ejecute acciones.

## Prioridad P0: posible reserva inesperada

La transcripción termina con «Limpieza dental?» → pregunta por fecha → mensaje «reserva confirmada» con **Aplicación de flúor**, fecha/hora/nombre no aportados en la parte visible. No afirmar que fue un commit ilegítimo sin trazas: puede ser salida asíncrona, estado previo o error de correlación. Revisa el código de correlación inbox → contexto → reserva → Outbox → Evolution y construye una prueba multi-turno con estado inicial aislado y otra con mensaje saliente tardío. Si no tienes logs reales autorizados, reporta `P0 NO DETERMINADO CON TRAZA REAL PENDIENTE`; no inventes causa ni afirmes que reservas está resuelto. Si reproduces creación sin confirmación válida, corrige antes que el resto. No elimines reservas, no reproceses `UnknownDelivery` ni relajes el consentimiento. Preserva H06: `«De acuerdo.»` no confirma una reserva final.

## Prioridad P1: pedidos y consultas

Reproduce con fixture completo esta secuencia (separando consultas informativas de mutaciones):

- «¿Está disponible el hilo dental?» y «Dije si está disponible»: devolver solo el producto referenciado, diferenciar publicación en catálogo y stock físico no verificable; no prometer unidades ni listar enjuague.
- «Quiero hacer un pedido»: orientación concreta en pasos y ejemplos de cantidades.
- «Quiero 2 hilos dentales»: exactamente 2 unidades, sin agregados involuntarios, precio verificado.
- «Envía la solicitud»: interpretarlo como intención de enviar para revisión; mostrar revisión y **pedir consentimiento** sobre cotización vigente sin persistir pedido todavía.
- «Dije que solo quiero eso» / «Solo esos dos, nada más»: conservar exactamente las 2 unidades y guiar al siguiente paso; no duplicar.
- «Quiero cancelar el pedido», «No conserves los productos quiero eliminarlos»: detectar descarte/limpieza del borrador, resolver ambigüedad con una sola pregunta específica y confirmar antes del descarte; distinguir pedido ya registrado.
- Fallback en estado ORDER: no devolver indefinidamente «No pude interpretar ese cambio». Proponer una acción concreta coherente con el estado y una vía humana si no progresa.

Inspecciona **primero** `OrderMentionResolver.IsSubmit`, `CommerceTurnResolver`, `TurnUnderstandingPipeline`, `AiResponseOrchestrator`, `OrderFlow`, `OrderDraftMerger`, `ConversationRecoveryService` y `ConversationQuestion`. La detección textual estricta de envío y el fallback del orquestador son evidencia de código; verifica su ruta exacta en los tests. Reutiliza contratos/DTOs y la interpretación contextual existente en vez de construir un segundo motor o ampliar listas de keywords. Verifica que IA producida y la intención realmente procesada sean observables en tests.

## Prioridad P2: redacción, guía y reservas

Mejora saludos, información general y preguntas de seguimiento con respuestas cortas, útiles y correctas para WhatsApp; marca comandos o ejemplos mediante `«...»` (y `*negritas*` solo cuando tenga valor). En pedidos guiar elección → resumen → solicitud de envío → consentimiento explícito → solicitud en revisión. En reservas guiar sede → servicio → fecha → hora disponible → nombre → revisión/consentimiento. Ejemplos de fecha **no obligatorios**: `«03/11»`, `«3 de noviembre»`, `«mañana por la tarde»`; resolver según reloj, zona horaria del negocio y horarios verificados; no elegir hora implícita, no prometer turnos. `«En conepcion»` debe poder resolver sede autorizada por similitud solo si inequívoca; de lo contrario preguntar. `«Limpieza dental?»` jamás confirma una reserva.

`ResponseComposer.ResponseCompositionOptions.EnableAi` empieza apagado y su capacidad está restringida a layout/inicio, **no arregla las interpretaciones**. Diseña una realización conversacional limitada sobre `ResponsePlan`/hechos/opciones verificados, con guardas de licencia, presupuesto, latencia, validación estricta de salida y fallback específico. No active composición IA general en producción sin pruebas ni aprobación; no permitir que reformule ni omita textos de confirmación, importes, negaciones o condiciones transaccionales. No hacer hardcodes odontológicos.

## Reglas inviolables

- Conservar fingerprint/resumen vigente, consentimiento fuerte, transacciones idempotentes, `UnknownDelivery` sin reenvío, aislamiento workspace/sede, permisos/entitlements, validación de catálogo/horarios y precios, recuperación persistente y transferencia humana.
- Nunca afirmar stock/pago/aprobación/horarios si la fuente del negocio no lo prueba.
- Sin rediseño masivo, sin nueva base de regex/if, sin nuevas migraciones ni configuración sensible ni nuevos contenedores.
- No tocar corpus/hashes/históricos/G8/workflows para obtener verde. G8 congelado vigente: `130/101/29`.
- No modificar frontend salvo fallo reproducible que lo requiera, y en ese caso reportar antes de ampliarlo.

## Tests y definición de terminado

Crea primero pruebas de regresión multi-turno para la transcripción y variantes inéditas, con `sourceMessageId` distinto por turno, retries de mismo ID, contexto persistido e inventario de efectos. Reutiliza fixtures del repositorio, prueba tanto interpretación mock y fallback sin proveedor. Verifica: ninguna reserva sin consentimiento, ni cambio de servicio, ni mensaje de confirmación fuera de correlación; 0 pedidos al pedir envío inicial y **1 solo** tras confirmar resumen vigente; cancelación diferenciada; consultas de stock sin inventario y sin editar el borrador; referencia singular, typo inequívoco, fechas y ayudas comprensibles. Ejercita licencias inactivas, comparación de sedes, cambios de precio, `UnknownDelivery`, takeover humano y proveedor IA fallido.

Ejecuta **tests dirigidos**, luego tests focales asociados, build y una vez la suite actual completa al finalizar. El histórico solo cuando sea necesario verificar no alteración. Si una prueba falla, informa la causa; no relajes las aserciones. No gastes tokens en otra auditoría global ni pruebas repetitivas.

## Entrega

Resumen de causas **demostradas**, hipótesis por falta de trazas, P0 `RESUELTO/NO DETERMINADO`, archivos modificados, nuevas pruebas, número de PASS/FAIL, diff final, ejemplos de respuestas de pedidos/reservas y `git status`. Si queda P0 incierto, no autorices operaciones reales nuevas; identifica exactamente la evidencia mínima que necesita aportar el operador. **Detente sin commit, push ni desplegar**. El propietario revisará antes de publicar el nuevo SHA.
