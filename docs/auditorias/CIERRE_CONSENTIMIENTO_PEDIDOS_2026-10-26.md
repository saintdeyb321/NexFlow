# Auditoría focalizada: consentimiento de pedidos

Fecha: 2026-10-26. Referencia local, `origin/main` y `main` remoto comprobado mediante `git ls-remote`: `4840b3b0258da47834dd990a73e2fc3ab9e33dc5`. Árbol inicialmente limpio. Sin cambios de producción, ramas, commits, migraciones, proveedores reales u Oracle.

## Actualización: cierre funcional del MVP sobre fba564d

Esta sección sustituye el dictamen funcional anterior. Se conservan debajo las evidencias de la auditoría original; sus limitaciones del flujo MVP ya no describen el parche actual.

- Reproducción previa: 27 pruebas de consentimiento, 9 fallidas y 18 aprobadas. Se confirmó que `finalizar` desde `ORDER_REVIEW` creaba un pedido, que faltaba renovar referencias inválidas y que se repetía el resumen sin mostrar la sede.
- `ORDER_REVIEW` ya no permite un nuevo efecto. El backend exige `ORDER_CONFIRM` con fingerprint comercial actual, pregunta `ConfirmOrder/Order` vigente y de la misma conversación/workspace, fuente posterior, ausencia de recuperación pendiente y consentimiento textual explícito. El claim CAS y la revalidación del catálogo/licencia siguen precediendo a la creación `PendingReview` y `NotifyOnce`.
- El `Submit` semántico no autoriza. Con revisión válida devuelve un recordatorio corto con `Confirmar / Editar / Descartar`, conservando pregunta y vencimiento. Una respuesta explícita válida completa el pedido sin consultar IA; se probó también tras indisponibilidad y rate limit del proveedor.
- Referencias vencidas, ausentes, futuras, ajenas o con intención/fingerprint incorrectos generan una nueva revisión y requieren otro mensaje. Los recordatorios no renuevan la ventana original de diez minutos.
- La sede se obtiene del repositorio autorizado del workspace, se muestra por nombre y se vincula al fingerprint. Sedes inexistentes/ajenas y productos fuera de su alcance no pueden confirmarse. `LocationDto` no tiene un campo de desactivación: no se inventó ese contrato. Sin sede seleccionada no se fabrica ninguna.
- Los checkpoints sin sede conservan su formato de hash; la recuperación de un efecto ya persistido o de `ORDER_COMMIT` del mismo inbound conserva su identidad. Contextos antiguos sin vinculación de conversación deben presentar una nueva revisión antes de un efecto nuevo.
- Evolution convierte determinadas respuestas interactivas en texto (`EvolutionWebhookController.cs:195`); no se atribuye autenticidad criptográfica a IDs o etiquetas de botones. No se implementó la alternativa interactiva propuesta en la auditoría inicial.
- Pruebas no congeladas que antes enviaban directamente desde revisión ahora presentan la confirmación y usan un mensaje explícito posterior. Se mantienen sus aserciones de CAS, efectos únicos, recuperación de catálogo, persistencia y notificaciones. No se cambiaron corpus congelados ni workflows.

Validación real del parche final:

| Validación | Ejecutadas | Aprobadas | Fallidas | Omitidas |
|---|---:|---:|---:|---:|
| Consentimiento, dentro de las focalizadas | 32 | 32 | 0 | 0 |
| Suites focalizadas de pedidos, reservas y recuperación | 258 | 258 | 0 | 0 |
| Gate oficial `g8_certification.py run current` | 1609 | 1609 | 0 | 0 |
| Históricos congelados | 130 | 102 | 28 | 0 |

`dotnet build backend/backend.slnx --no-restore`: 0 errores; tres advertencias preexistentes en `refactor`/designer y `FirestoreCatalogRepository.cs:228`. `git diff --check`: sin errores. TRX, logs y reportes del gate: `backend/NexFlow.Tests/TestResults/order-final/`.

**Bloqueo de certificación CI:** los 12 fallos históricos anteriores conservan identidad y motivo. Se añaden `*-03`, `*-04`, `*-19` de los cinco sectores de `CommercialCertificationCorpus.json` y `H26` de `QualityHoldout61.json`: 16 casos `OrderEffects` que esperaban crear un pedido sin la nueva confirmación separada. Los hashes de corpus/evaluadores permanecen intactos. El verificador histórico exige exactamente 118/12 y devuelve **NO-GO** con 102/28. No se amplió su allowlist ni se falseó un PASS. Hace falta una decisión independiente y explícita sobre la compatibilidad de ese contrato congelado con la nueva política de seguridad; no corresponde relajar el flujo productivo para satisfacerlo.

**Estado:** cierre funcional local del MVP explícito aprobado; consentimiento libre arbitrario sigue sin certificarse. Build, focalizadas y gate vigente cumplen la condición de publicación del encargo, pero los cuatro workflows no pueden darse por listos/aprobados debido al gate histórico. No se ejecutaron CI remoto, integraciones reales, proveedores, migraciones ni operaciones de Oracle. El despliegue sigue sin estar autorizado. Las secciones siguientes son el informe histórico de la auditoría anterior.

## Dictamen de la auditoría inicial (anterior al parche)

**P1 confirmado: la autorización libre del resumen vigente continúa PENDIENTE.** Esta intervención añade pruebas y documenta la limitación permitida por el encargo; no habilita consentimiento inferido por IA. Pruebas verdes de caracterización y seguridad **no significan que se haya corregido el bucle**.

Se reprodujeron tres paráfrasis nuevas mediante el handler, pipeline, resolver y flujo reales con los fixtures sintéticos existentes. Ejemplo: «Doy mi visto bueno para que tramiten esa solicitud tal cual». Con un resumen vigente, interpretación `Order/Resume/Submit`, evidencia literal, catálogo y licencia válidos, cada nuevo mensaje produce otra revisión, conserva el fingerprint y crea una nueva pregunta; no se crea ningún pedido. El control positivo sobre el mismo borrador usando `Confirmar` sí registra un único `PendingReview`.

## Causas verificadas

| Componente | Evidencia | Implicación |
|---|---|---|
| `CommerceTurnResolver.ResolveOrderActionAsync` | `backend/NexFlow.Application/Features/Automation/Dialogue/Understanding/CommerceTurnResolver.cs:249`, `:253`, `:258` | Todo `Submit` semántico exige revisión. El acto `Resume` representa tanto acabar la selección como pedir el envío; no es una prueba diferenciada de autorización final. |
| `OrderFlow.ProcessPlannedAsync` | `backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/Flows/OrderFlow.cs:111`, `:114`, `:116` | Comprueba la cotización vigente y consentimiento acotado; `RequireConfirmation` fuerza revisión. Ponerlo a `false` por sí solo tampoco resuelve el problema: la expresión libre sigue sin satisfacer `consent`. |
| `AiInterpreter` | `backend/NexFlow.Application/Features/AI/Interpretation/AiInterpreter.cs:24`, `:156`, `:188`, `:211` | `Confirmed` es del servidor y se rechaza en JSON del modelo; `Confirmation` no es un acto aceptado. El prompt prohíbe ambos. La prueba del intérprete real reproduce el rechazo para pedidos y reservas. |
| Validación de evidencia | `CommerceTurnResolver.cs:253` | Comprueba que el fragmento existe en el mensaje, no que sea una autorización afirmativa e incondicional. Una frase negativa real también puede acompañar un etiquetado semántico erróneo. |
| Referencia y confirmación | `backend/NexFlow.Application/Features/Automation/Dialogue/Contracts/ConversationQuestion.cs:25`, `:43`; `.../Policy/ConversationConfirmation.cs:8` | La pregunta vigente permite verificar workspace, vencimiento y fingerprint; el consentimiento actual es formal/acotado. Ninguno demuestra por sí mismo el significado de una frase libre. |

No se acepta convertir `OrderAction=Submit`, un booleano del modelo, una puntuación de confianza o la mera existencia de `Evidence.Text` en consentimiento. Tampoco se propone añadir sinónimos, regex de intención o un segundo LLM como supuesta prueba independiente.

Observación del mismo criterio de resumen: `OrderDraftMerger.Price` (`.../Dialogue/Policy/OrderDraftMerger.cs:65`) muestra artículos e importes; la revisión de `OrderFlow` (`:123`) no añade la sede seleccionada al texto. La sede sí está vinculada al fingerprint y a `RequestedLocationId`. Falta hacerla visible con su nombre autorizado antes de certificar íntegramente ese requisito. No se alteraron datos ni se amplió este encargo a otra UX.

## Regresiones añadidas

Archivo: `backend/NexFlow.Tests/Concierge/OrderConfirmationAuditTests.cs`.

| Caso solicitado | Evidencia local y alcance |
|---|---|
| Finalizar sin confirmar | Revisión y cantidades intactas, cero escrituras de pedido. |
| Autorización natural posterior | Tres casos `Known_limitation_*` reproducen la repetición; **criterio funcional PENDIENTE**, no simulan éxito. Cada uno incluye un control positivo del mismo borrador con consentimiento existente. |
| Pregunta, negativa, condición y ambigüedad | Aun con una propuesta semántica excesivamente afirmativa, cero pedidos y cantidades intactas. |
| Corrección de cantidad | Cambia la cantidad validada, invalida el hash anterior y no envía la solicitud. |
| Cambio de precio, licencia o sede | No se envía con la confirmación previa. Se usa también consentimiento existente que, sin el cambio, sí puede registrar: no son controles vacuos basados sólo en bloquear todo `Submit` semántico. |
| Cambio de contexto durante IA | Rechazo por versión/CAS antes del efecto. |
| Replay y texto repetido con otro ID | Un pedido `PendingReview` y una invocación de creación. |
| IA indisponible o limitada | Cero pedidos, borrador intacto, sin reintento automático del proveedor. |
| Reservas | El intérprete sigue rechazando confirmación del modelo; el consentimiento existente sobre una pregunta vigente registra una sola reserva, sin crear pedidos. |

Se reutilizan además las pruebas de `CommerceSafetyTests`: concurrencia con un ganador CAS, identidades workspace/source/version, cambios de catálogo, recuperación después de fallo de notificación y `NotifyOnce`. Las reservas cuentan también con las regresiones de `RuntimeNaturalBookingTests`, `DialogueIntegrationTests` y `AiResilienceTests`. No se modificaron expectativas ni corpus existentes.

## Validaciones reales

1. `dotnet build backend/backend.slnx --no-restore`: correcto, 0 errores, 0 advertencias en la ejecución final incremental.
2. `dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --filter FullyQualifiedName~OrderConfirmationAuditTests`: 21 ejecutadas, 21 aprobadas, 0 fallidas, 0 omitidas. La primera compilación emitió tres advertencias preexistentes de infraestructura y cuatro del nuevo test; estas últimas se corrigieron usando los overloads de aserción recomendados.
3. Suites afectadas: `OrderConfirmationAuditTests`, `RuntimeOrderRequestTests`, `CommerceSafetyTests`, `RuntimeNaturalBookingTests`, `DialogueIntegrationTests`, `AiResilienceTests`: 242 ejecutadas, 242 aprobadas, 0 fallidas, 0 omitidas, sin advertencias nuevas.
4. `python -B .github/workflows/g8_certification.py run current --trx backend/NexFlow.Tests/TestResults/order-confirmation-audit/current.trx`: **PASS local**, 1598 ejecutadas, 1598 aprobadas, 0 fallidas, 0 omitidas. Incluye las 21 pruebas nuevas. Se verificaron los hashes de corpus/evaluadores congelados; no se ejecutó la batería histórica en este encargo.
5. `git diff --check` y revisión `--no-index --check` de ambos archivos nuevos: sin errores de whitespace. Los únicos cambios pendientes son este informe y el archivo nuevo de pruebas.

TRX y logs locales: `backend/NexFlow.Tests/TestResults/order-confirmation-audit/`. El gate usa el filtro y controles existentes de `.github/workflows/g8_certification.py`; no se modificó el harness. No se ejecutaron integraciones externas, CI remoto, Gemini/Groq reales ni WhatsApp. Las interpretaciones de pruebas son propuestas sintéticas controladas, no evidencia de comprensión del proveedor real.

## Alternativa segura propuesta, sin implementar

La opción de seguridad estricta es una acción de confirmación estructurada del usuario, autenticada por el canal y vinculada al resumen. Puede reutilizar el contexto, la pregunta vigente, el pipeline durable y `OrderFlow`, sin un segundo estado transaccional:

1. El backend emite una referencia opaca de un solo uso para confirmar ese resumen. La vincula al workspace resuelto por el canal, conversación/contacto, `QuestionId`, versión, fingerprint comercial, acción y vencimiento. No la genera el LLM ni se envían huellas internas al proveedor IA.
2. El usuario activa una acción interactiva del canal. El adaptador valida su autenticidad y la referencia al mensaje de revisión; el texto mostrado o copiado no equivale a esa acción. Su soporte real en el contrato del gateway/webhook debe verificarse antes de implementarlo.
3. El backend verifica `ORDER_CONFIRM`, referencia vigente, identidad, versión, ausencia de takeover/recuperación pendiente, licencia, sede y catálogo actuales. Una modificación invalida la referencia y exige otro resumen con nombre de sede autorizado e importes verificados.
4. Reclama CAS antes del efecto, reutiliza `ORDER_COMMIT`, el identificador idempotente del inbound, la creación `PendingReview`, la validación transaccional de catálogo y `NotifyOnce`. Replays recuperan el resultado existente; una referencia consumida no abre otro pedido.
5. La IA puede interpretar una petición natural como candidata a solicitar esa confirmación; no fabrica el evento de consentimiento ni ejecuta escrituras. Ante fallo o ambigüedad, el borrador permanece intacto.

**Esta alternativa no equivale a aceptar texto libre arbitrario como consentimiento final.** Si el producto exige que la frase libre complete directamente el pedido, hace falta aprobar explícitamente otro contrato de confianza semántica y su riesgo de clasificación, sustentado en el mensaje completo y la referencia del resumen, con evaluación adversarial independiente. El contrato actual no ofrece esa evidencia; añadir etiquetas del mismo LLM no la crea. No se declara esa ampliación diseñada, implementada ni certificada aquí.

## Gate de cierre

- **OK:** identidad del checkout/remoto, reproducción del bucle, regresiones de seguridad, confirmaciones existentes y separación de reservas.
- **PENDIENTE / NO-GO para anunciar consentimiento libre:** autorización natural final y presentación de sede en el resumen.
- **Sin parche productivo:** se conserva el fallo seguro en lugar de debilitar la confirmación. Detenido para revisión del contrato propuesto; sin commit, push ni despliegue.
