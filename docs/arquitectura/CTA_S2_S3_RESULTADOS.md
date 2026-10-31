# CTA — Sprint 2 y Sprint 3: resultado local

31/10/2026. HEAD de referencia `17ab2cd0211fbbfd420bbabce477acdc0ac6a6b9`. Cambios **sin publicar**; índice vacío. Árbol inicial: únicamente el documento de instrucciones S2/S3 sin seguimiento; se conservó intacto. Código local, AGENTS y documentos obligatorios leídos completos. Sin cambios de frontend, infraestructura, configuración, corpus, migraciones ni staging. Sin servicios reales ni proveedores externos.

## Decisiones de gates

| Gate | Resultado | Evidencia real |
|---|---|---|
| G0 | PASS local | Reproducción inicial 4 casos: 1 PASS, 3 FAIL (H1 y dos H2). Gate corregido 48/48; cuatro comprobaciones adicionales de CAS/identidad incluidas después y en la suite final. |
| Sprint 2 | PASS local | Reproducción 11 casos: 7 PASS, 4 FAIL (referencias y cantidades). Gate final 216/216, 0 FAIL, 0 omitidas. |
| Sprint 3 | PASS local | 12 conversaciones antes/después, 95 turnos por batería. Antes: 12 fallos del criterio nuevo de orientación; sus efectos esperados ya eran correctos. Después: gate 196/196, 0 FAIL, 0 omitidas. |
| Build | PASS | `dotnet build backend/backend.slnx --no-restore`: 0 errores, 0 advertencias en esta ejecución incremental. Compilaciones previas de pruebas emitieron los avisos antiguos CS8981 (2) y CS8602 en FirestoreCatalogRepository:228; no se modificaron. |
| Suite vigente final | PASS | Una sola ejecución completa mediante G8: **1806 ejecutadas, 1806 PASS, 0 FAIL, 0 omitidas**. Base F1 1764 + 42 casos nuevos. |
| G8 histórico | EVIDENCIA REUTILIZADA | TRX F1 copiado, sin tocar original, y revalidado por el validador vigente: **130/101/29**, inventario y motivos exactos, H06/4/A: ReservationEffects. No ejecutado de nuevo sobre el diff actual. Corpus, evaluadores y contrato sin cambios, hashes fijados comprobados. |
| Proveedores / WhatsApp / stores reales / CI | NO EJECUTADO | Interpretaciones inyectadas y router simulado. Repositorios y serialización SDK reales contra el fake RPC Firestore existente. No equivalen a emulador ni certificación distribuida real. |

Un primer filtro dirigido demasiado amplio seleccionó también 13 integraciones no configuradas: 170 PASS, 0 FAIL, 13 omitidas. Se conservó `sprint2.trx`; el gate dirigido posterior usa categorías locales explícitas, sin esas omisiones. No se añadieron Skip ni se excluyeron nuevos casos del gate vigente.

## G0: causas y reparación

- **H1 CONFIRMADO y REPRODUCIDO.** El TTL rechazaba restaurar el suspendido y el inicio con `CurrentGoal=null` lo bloqueaba permanentemente. `ConversationFocusService.Discard.cs:20,46` añade una decisión de descarte acotada, en el mismo Focus/contextJson. `AiResponseOrchestrator.cs:207` ofrece descartar el suspendido vencido al solicitar un nuevo inicio. No lo borra automáticamente ni inicia la operación después de un simple sí: primero descarta con prueba vigente y luego se puede iniciar de nuevo.
- Alcances Active/Suspended/Both explícitos; «Cancelar» sin alcance pregunta, y un sí sin alcance no elimina nada. Tres elecciones publicadas son protocolo opcional, no un diccionario semántico. El lenguaje libre usa el intérprete con evidencia y `DiscardScope` tipado. Cada alcance exige otra respuesta y pregunta propia de 10 minutos, SourceMessageId, fingerprint de ambas proyecciones, WorkspaceId, ConversationId, OperationId, ActivationRevision y CAS. Permite descartar un borrador cuyo TTL venció; **no permite usarlo para confirmar una transacción**. `ConversationQuestion.Current()` y los comprobadores de consentimiento siguen intactos.
- Cancelación negativa, expiración, replay, CAS concurrente, otro workspace/conversación y control humano probados. No borra pedidos/reservas registrados, recibos durables ni mensajes. Descartar el activo conserva el suspendido y viceversa. La decisión temporal no renueva el TTL del borrador.
- **H2 CONFIRMADO y REPRODUCIDO.** `ProcessIncomingMessageCommandHandler.cs:142` ahora guarda la pregunta con `conversation.Id`; el reloj inyectado permite validar la vigencia usando el mismo instante. Con un descarte pendiente, la ruta rápida vuelve a la orquestación para no sustituir esa pregunta. Pruebas multi-turno de sedes y horarios, con activo terminado y suspendido vigente; legacy cubierto por regresiones.
- **H3 SIN INCIDENCIA REPRODUCIDA.** Se preservan `OrderLastSourceMessageId`/`OrderLastResponse`. `Flows/OrderFlow.cs:32,74,166,179` distingue recuperación de commit/ACK, source y respuesta de turno. La reanudación genera revisión con precio actual; un replay anterior no muta ni ejecuta efectos. No se alteraron sus recibos ni marcadores.

## Sprint 2: contrato mínimo

- Se reutilizan `DialogueTurnContext`, `OfferingReference`, la pregunta vigente y Focus. Sin nuevo historial, catálogo completo, repositorio o framework.
- `AiOrderItem.ReferenceText` copia el fragmento real; `ReferenceIndex` es un ordinal explícito (base 1), comprobado mediante el resolutor ordinal existente contra las líneas del pedido y la pregunta de su revisión activa. Los IDs provienen del estado y catálogo autorizado del workspace, nunca del modelo. Un pronombre sin ordinal solo usa el referente único vigente. Producto inexistente, módulo revocado, referencia vencida, índice fabricado o varios posibles productos => aclaración y borradores intactos.
- `CommerceTurnResolver.cs:329` comprueba fragmento, pregunta, revisión, candidatos y catálogo. `Set` expresa cantidad final; `Decrease` expresa reducción literal positiva. `OrderDraftMerger.cs:28` resta de la cantidad actual, elimina una línea al llegar a cero y rechaza valores negativos. No modifica precios, stock ni duración.
- `AiInterpreter` amplía solamente spans semánticos opcionales y valida JSON. `Confirmed`, IDs/propuestas del servidor y consentimiento del modelo continúan prohibidos. Una interpretación inválida/timeout no ejecuta cambios. Dos intenciones mutables o referente ambiguo piden aclaración; no hay ejecución múltiple ni efectos parciales. La combinación existente de consulta de horarios + preparación de reserva se conserva y prueba con VirtualSecretaryTests.
- Después de pausar y volver, la edición no afecta la reserva suspendida. Replays no duplican cantidades. Se conservan los controles ya probados de CAS, takeover, licencias, aislamiento, precios, revisión nueva, H06 y notificaciones/UnknownDelivery. No se cambió ReservationEngine ni OrderFlow.

## Sprint 3: presentación y límites

`SecretaryPresentation.FocusTransition/FocusReminder` y su integración en `AiResponseOrchestrator.cs:160,371` sustituyen la mención técnica del foco por la operación activa/guardada y un modo de retomarla. Ante incertidumbre/fallo se conserva la orientación existente más el estado de ambas preparaciones. No se ofrece retomar con TTL vencido o permiso revocado. No se añade una pregunta transaccional, consulta, CAS o llamada IA para decorar. Se reutiliza ResponsePlan/ResponseComposer; composición determinista por defecto.

La orientación de productos/cantidades, subtotal, revisión separada, PendingReview, catálogo frente a stock, fecha libre y hora comprobada ya estaba implementada: se conserva y se ejercita, no se reescribe. Los productos, precios, sedes, servicios y horarios que siguen son **sintéticos de fixtures**; no representan DEMO ni clientes reales. El caso dental ejercita hilo dental, dos unidades, corrección a una, envío, sede Concepción, limpieza, fecha/hora y confirmaciones independientes; las secuencias de descarte están en tienda/salón/restaurante y la conversación ambigua.

## Pruebas y comandos ejecutados

Artefactos locales no versionados: `backend/TestResults/CTAS2S3/`.

```powershell
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --filter "FullyQualifiedName~CtaGate0Tests|FullyQualifiedName~ContextualFocusTests|FullyQualifiedName~AssistantRc1ConcurrencyTests" --logger "trx;LogFileName=gate0.trx" --results-directory backend/TestResults/CTAS2S3
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --filter "(FullyQualifiedName~CtaContinuityTests|FullyQualifiedName~CtaGate0Tests|FullyQualifiedName~ContextualFocusTests|FullyQualifiedName~Commerce|FullyQualifiedName~AssistantRc1ConcurrencyTests|FullyQualifiedName~VirtualSecretaryTests)&Category!=Integration&Category!=RedisIntegration&Category!=ProviderIntegration" --logger "trx;LogFileName=sprint2-final.trx" --results-directory backend/TestResults/CTAS2S3
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --filter "(FullyQualifiedName~CtaGuidanceTests|FullyQualifiedName~CtaGate0Tests|FullyQualifiedName~CtaContinuityTests|FullyQualifiedName~ContextualFocusTests|FullyQualifiedName~ResponseComposerTests|FullyQualifiedName~VirtualSecretaryTests)&Category!=Integration&Category!=RedisIntegration&Category!=ProviderIntegration" --logger "trx;LogFileName=sprint3-after.trx" --results-directory backend/TestResults/CTAS2S3
dotnet build backend/backend.slnx --no-restore
python -B .github/workflows/g8_certification.py run current --trx backend/TestResults/CTAS2S3/current.trx
python -B .github/workflows/g8_certification.py verify historical --trx backend/TestResults/CTAS2S3/historical-reused.trx --test-exit-code 1
git diff --check
```

También se ejecutaron las reproducciones `gate0-before.trx`, `sprint2-before.trx` y `sprint3-before.trx`; fallos conservados. Los dos tests dirigidos de antes/después usan el mismo código de prueba y entradas; solo la expectativa del nuevo texto tiene la corrección de redacción para afirmar que **el cambio** no envía operaciones, en vez de negar operaciones ya realizadas.

## Métricas medidas


| Batería | Turnos | Invocaciones intérprete simulado | RPC Commit (incluye presupuesto/contexto) | RPC lecturas | Aclaraciones contadas | ms fixture total |
|---|---:|---:|---:|---:|---:|---:|
| Antes | 95 | 44 | 262 | 832 | 2 | 1192 |
| Después | 95 | 44 | 262 | 832 | 2 | 1169 |

La tasa de aclaraciones es incrementos del contador ClarificationAttempts / turnos, no una puntuación humana de naturalidad: 2/95 (2,1 %) antes y después; no se afirma reducción de repreguntas. Latencias sintéticas incluyen JIT, mocks, serialización y carga local; no constituyen comparación de rendimiento ni latencia de Gemini/Groq. RPC Commit cuenta también presupuesto IA: no representa exclusivamente CAS del contexto. Sprint 3 no añade commits ni lecturas en estas secuencias. El descarte usa la persistencia CAS existente de la respuesta, sin store ni commit separado.

La prueba del prompt mide 7250 caracteres con CTA desactivado y 7801 con CTA habilitado: delta **551**, dentro del límite F1 de 600; sin IDs de operación, teléfono ni detalles del borrador suspendido. No se midieron tokens facturados ni costos reales. Cada turno semántico conserva como máximo una interpretación; composición IA 0/95 turnos; no hay una segunda llamada obligatoria. Campos ReferenceText <=120 caracteres y ReferenceIndex 1..10; ordinal no reconocido por el resolutor existente => aclaración, sin ampliar diccionarios.

## Archivos y revisión de alcance


- `backend/NexFlow.Application/Features/AI/Interpretation/AiInterpreter.cs`
- `backend/NexFlow.Application/Features/Automation/Dialogue/Composition/SecretaryPresentation.cs`
- `backend/NexFlow.Application/Features/Automation/Dialogue/Contracts/CommerceTurn.cs`
- `backend/NexFlow.Application/Features/Automation/Dialogue/Contracts/ConversationFocusState.cs`
- `backend/NexFlow.Application/Features/Automation/Dialogue/Contracts/ConversationQuestion.cs`
- `backend/NexFlow.Application/Features/Automation/Dialogue/Policy/OrderDraftMerger.cs`
- `backend/NexFlow.Application/Features/Automation/Dialogue/Understanding/CommerceTurnResolver.cs`
- `backend/NexFlow.Application/Features/Automation/ProcessMessage/ProcessIncomingMessageCommandHandler.cs`
- `backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/AiResponseOrchestrator.cs`
- `backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/ConversationFocusService.cs`
- `backend/NexFlow.Tests/AssistantConversationTests.cs`
- `backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/ConversationFocusService.Discard.cs`
- `backend/NexFlow.Tests/Concierge/CtaGate0Tests.cs`
- `backend/NexFlow.Tests/Concierge/CtaContinuityTests.cs`
- `backend/NexFlow.Tests/Concierge/CtaGuidanceTests.cs`
- `docs/arquitectura/CTA_S2_S3_RESULTADOS.md`

El documento de instrucciones del propietario `docs/NEXFLOW_CTA_AUDITORIA_F1_Y_PROMPT_SPRINTS_2_3_2026-10-31.md` ya estaba sin seguimiento; no se editó ni se preparó para commit. Ninguna expectativa o corpus congelado modificado. Revisión del diff de los archivos cambiados y `git diff --check` sin errores; Git solo advierte normalización LF/CRLF en algunos archivos. Índice vacío; HEAD sin cambios. Sin cambios fuera de backend/pruebas y este informe.

## Riesgos y activación futura (no ejecutada)

- **P0 histórico de reserva inesperada: PENDIENTE, causa no determinada.** Las nuevas pruebas no sustituyen trazas reales correlacionadas.
- Sin ensayo de IA real, evaluación ciega/humana, WhatsApp ni concurrencia PostgreSQL/Firestore/Redis reales en esta intervención. No se declaran certificados.
- CTA **apagado por defecto**, `AssistantRecovery.EnableContextualFocus` sigue false. No activarlo con escritores antiguos capaces de perder Focus o PendingDiscard al reescribir contextJson. Antes de cualquier publicación: nueva revisión/SHA autorizado, gates CI del mismo SHA, todos los escritores compatibles y drenaje; piloto aislado autorizado, observar transición/revisión/efectos y fallos. No revertir a escritores que ignoren Focus con preparaciones aún abiertas; antes, terminar/descartar con consentimiento o esperar expiración según procedimiento aprobado.
- Un solo suspendido, sin historial adicional ni ejecución paralela. Una tercera preparación o mutaciones simultáneas se aclaran/rechazan. Referencias no inequívocas no se adivinan. Cambiar de operación invalida la pregunta/consentimiento anterior.
- **GO solo para revisión del desarrollo local S2/S3. NO-GO para activar CTA/desplegar o declarar resuelto el P0.** Siguiente gate recomendado: auditoría independiente del diff y después pruebas autorizadas con proveedor real en workspace sintético aislado. No se inicia otro sprint.

## Doce conversaciones antes/después (capturadas del handler)

Cada respuesta se conserva completa. Cuando es idéntica, «Después = Antes» referencia el texto completo inmediatamente anterior. Los códigos generados son sintéticos; pueden diferir entre ejecuciones. Los motores de prueba registran efectos y usan repositorios controlados, no servicios productivos. Las etiquetas tienda/salón/restaurante varían el producto y comprueban los mismos contratos base; reutilizan las sedes y servicios clínicos del fixture existente. No constituyen homologación comercial de cada sector.


### 1. atencion-humana

**1. Cliente:** ¿Qué productos tienen?

**Antes:**

> *Productos del catálogo*
>
> • *Pan blanco*
>   S/ 2.00
>
> • *Pan integral*
>   S/ 2.50
>
> • *Queque*
>   S/ 10.00

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":null,"Suspended":null,"Question":"Information","Items":[],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**2. Cliente:** Quiero hacer un pedido

**Antes:**

> Te ayudo a preparar una solicitud para que el negocio la revise. Elegimos productos y cantidades, revisamos el resumen y después te pediré confirmar el envío.
>
> ¿Qué productos y cantidades deseas solicitar? Por ejemplo, «2 unidades de Pan integral». También puedes consultar «productos».

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":"ORDER","Suspended":null,"Question":"RequestedData","Items":[],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**3. Cliente:** Solicito dos unidades de Pan integral

**Antes:**

> 🛍️ *Tu solicitud para revisión*
>
> Así va tu pedido:
>
> • 2 × Pan integral
>   PEN 5.00
>
> Subtotal parcial: S/ 5.00.
>
> Importes del catálogo; el negocio revisará la solicitud. No se confirma stock, pago ni aceptación del negocio. No incluyen cargos adicionales no registrados.
>
> ¿Deseas agregar o cambiar algo, o revisar el envío? Puedes indicar un cambio o decir «Enviar la solicitud»; antes de registrarla te pediré confirmar el resumen.

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":"ORDER","Suspended":null,"Question":"RequestedData","Items":[{"ProductName":"Pan integral","Quantity":2}],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**4. Cliente:** Preparemos también una cita

**Antes:**

> He cambiado el foco sin enviar ni confirmar nada. La otra preparación queda guardada para retomarla.
>
> Podemos atenderte en estas sedes:
>
> 1. Sede Concepcion
> 2. Sede San Jeronimo
>
> ¿En cuál sede deseas atenderte? Puedes indicar el nombre o una opción como «la primera».

**Después:**

> Tu pedido sigue guardado; continuamos con tu reserva. Puedes pedir «Retomar el pedido» cuando quieras volver. Este cambio no envía ni confirma operaciones.
>
> Podemos atenderte en estas sedes:
>
> 1. Sede Concepcion
> 2. Sede San Jeronimo
>
> ¿En cuál sede deseas atenderte? Puedes indicar el nombre o una opción como «la primera».

Estado/efectos antes = después: `{"Active":"RESERVATION","Suspended":"Order","Question":"Selection","Items":[],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**5. Cliente:** En Concepción, limpieza dental

**Antes:**

> Busquemos un día que te acomode.
>
> ¿Para qué fecha deseas reservar? Puedes indicar día y mes, por ejemplo «03/11» o «3 de noviembre», o decir «mañana por la tarde»; comprobaré los horarios de la sede.

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":"RESERVATION","Suspended":"Order","Question":"RequestedData","Items":[],"SelectedLocationId":"sede-a","SelectedServiceId":"service-a","TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**6. Cliente:** Retomemos mi selección de productos

**Antes:**

> Retomamos tu preparación sin confirmar ninguna operación. La otra preparación queda guardada para retomarla.
>
> 🛍️ *Tu solicitud para revisión*
>
> Así va tu pedido:
>
> • 2 × Pan integral
>   PEN 5.00
>
> Subtotal parcial: S/ 5.00.
>
> Importes del catálogo; el negocio revisará la solicitud. No se confirma stock, pago ni aceptación del negocio. No incluyen cargos adicionales no registrados.
>
> ¿Deseas agregar o cambiar algo, o revisar el envío? Puedes indicar un cambio o decir «Enviar la solicitud»; antes de registrarla te pediré confirmar el resumen.

**Después:**

> Retomamos tu pedido. Tu reserva sigue guardada. Puedes pedir «Retomar la reserva» cuando quieras volver. Este cambio no envía ni confirma operaciones.
>
> 🛍️ *Tu solicitud para revisión*
>
> Así va tu pedido:
>
> • 2 × Pan integral
>   PEN 5.00
>
> Subtotal parcial: S/ 5.00.
>
> Importes del catálogo; el negocio revisará la solicitud. No se confirma stock, pago ni aceptación del negocio. No incluyen cargos adicionales no registrados.
>
> ¿Deseas agregar o cambiar algo, o revisar el envío? Puedes indicar un cambio o decir «Enviar la solicitud»; antes de registrarla te pediré confirmar el resumen.

Estado/efectos antes = después: `{"Active":"ORDER","Suspended":"Reservation","Question":"RequestedData","Items":[{"ProductName":"Pan integral","Quantity":2}],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**7. Cliente:** asesor

**Antes:**

> Un asesor continuará la conversación.

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":"ORDER","Suspended":"Reservation","Question":"RequestedData","Items":[{"ProductName":"Pan integral","Quantity":2}],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`


### 2. catalogo-vacio

**1. Cliente:** ¿Qué productos tienen?

**Antes:**

> *Productos del catálogo*
>
> • *Pan blanco*
>   S/ 2.00
>
> • *Pan integral*
>   S/ 2.50
>
> • *Queque*
>   S/ 10.00

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":null,"Suspended":null,"Question":"Information","Items":[],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**2. Cliente:** Quiero hacer un pedido

**Antes:**

> Te ayudo a preparar una solicitud para que el negocio la revise. Elegimos productos y cantidades, revisamos el resumen y después te pediré confirmar el envío.
>
> ¿Qué productos y cantidades deseas solicitar? Por ejemplo, «2 unidades de Pan integral». También puedes consultar «productos».

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":"ORDER","Suspended":null,"Question":"RequestedData","Items":[],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**3. Cliente:** Solicito dos unidades de Pan integral

**Antes:**

> 🛍️ *Tu solicitud para revisión*
>
> Así va tu pedido:
>
> • 2 × Pan integral
>   PEN 5.00
>
> Subtotal parcial: S/ 5.00.
>
> Importes del catálogo; el negocio revisará la solicitud. No se confirma stock, pago ni aceptación del negocio. No incluyen cargos adicionales no registrados.
>
> ¿Deseas agregar o cambiar algo, o revisar el envío? Puedes indicar un cambio o decir «Enviar la solicitud»; antes de registrarla te pediré confirmar el resumen.

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":"ORDER","Suspended":null,"Question":"RequestedData","Items":[{"ProductName":"Pan integral","Quantity":2}],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**4. Cliente:** Preparemos también una cita

**Antes:**

> He cambiado el foco sin enviar ni confirmar nada. La otra preparación queda guardada para retomarla.
>
> Podemos atenderte en estas sedes:
>
> 1. Sede Concepcion
> 2. Sede San Jeronimo
>
> ¿En cuál sede deseas atenderte? Puedes indicar el nombre o una opción como «la primera».

**Después:**

> Tu pedido sigue guardado; continuamos con tu reserva. Puedes pedir «Retomar el pedido» cuando quieras volver. Este cambio no envía ni confirma operaciones.
>
> Podemos atenderte en estas sedes:
>
> 1. Sede Concepcion
> 2. Sede San Jeronimo
>
> ¿En cuál sede deseas atenderte? Puedes indicar el nombre o una opción como «la primera».

Estado/efectos antes = después: `{"Active":"RESERVATION","Suspended":"Order","Question":"Selection","Items":[],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**5. Cliente:** En Concepción, limpieza dental

**Antes:**

> Busquemos un día que te acomode.
>
> ¿Para qué fecha deseas reservar? Puedes indicar día y mes, por ejemplo «03/11» o «3 de noviembre», o decir «mañana por la tarde»; comprobaré los horarios de la sede.

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":"RESERVATION","Suspended":"Order","Question":"RequestedData","Items":[],"SelectedLocationId":"sede-a","SelectedServiceId":"service-a","TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**6. Cliente:** ¿Qué productos tienen?

**Antes:**

> No tengo una opción activa registrada que coincida con tu consulta.
>
> Busquemos un día que te acomode.
>
> ¿Para qué fecha deseas reservar? Puedes indicar día y mes, por ejemplo «03/11» o «3 de noviembre», o decir «mañana por la tarde»; comprobaré los horarios de la sede.

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":"RESERVATION","Suspended":"Order","Question":"Information","Items":[],"SelectedLocationId":"sede-a","SelectedServiceId":"service-a","TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**7. Cliente:** Retomemos mi selección de productos

**Antes:**

> Retomamos tu preparación sin confirmar ninguna operación. La otra preparación queda guardada para retomarla.
>
> 🛍️ *Tu solicitud para revisión*
>
> Así va tu pedido:
>
> • 2 × Pan integral: no está disponible con un precio vigente.
>
> Importes del catálogo; el negocio revisará la solicitud. No se confirma stock, pago ni aceptación del negocio. No incluyen cargos adicionales no registrados.
>
> ¿Deseas agregar o cambiar algo, o revisar el envío? Puedes indicar un cambio o decir «Enviar la solicitud»; antes de registrarla te pediré confirmar el resumen.

**Después:**

> Retomamos tu pedido. Tu reserva sigue guardada. Puedes pedir «Retomar la reserva» cuando quieras volver. Este cambio no envía ni confirma operaciones.
>
> 🛍️ *Tu solicitud para revisión*
>
> Así va tu pedido:
>
> • 2 × Pan integral: no está disponible con un precio vigente.
>
> Importes del catálogo; el negocio revisará la solicitud. No se confirma stock, pago ni aceptación del negocio. No incluyen cargos adicionales no registrados.
>
> ¿Deseas agregar o cambiar algo, o revisar el envío? Puedes indicar un cambio o decir «Enviar la solicitud»; antes de registrarla te pediré confirmar el resumen.

Estado/efectos antes = después: `{"Active":"ORDER","Suspended":"Reservation","Question":"RequestedData","Items":[{"ProductName":"Pan integral","Quantity":2}],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`


### 3. dental-demo

**1. Cliente:** ¿Qué productos tienen?

**Antes:**

> *Productos del catálogo*
>
> • *Hilo dental*
>   S/ 2.50
>
> • *Pan blanco*
>   S/ 2.00
>
> • *Queque*
>   S/ 10.00

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":null,"Suspended":null,"Question":"Information","Items":[],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**2. Cliente:** Quiero hacer un pedido

**Antes:**

> Te ayudo a preparar una solicitud para que el negocio la revise. Elegimos productos y cantidades, revisamos el resumen y después te pediré confirmar el envío.
>
> ¿Qué productos y cantidades deseas solicitar? Por ejemplo, «2 unidades de Hilo dental». También puedes consultar «productos».

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":"ORDER","Suspended":null,"Question":"RequestedData","Items":[],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**3. Cliente:** Solicito dos unidades de Hilo dental

**Antes:**

> 🛍️ *Tu solicitud para revisión*
>
> Así va tu pedido:
>
> • 2 × Hilo dental
>   PEN 5.00
>
> Subtotal parcial: S/ 5.00.
>
> Importes del catálogo; el negocio revisará la solicitud. No se confirma stock, pago ni aceptación del negocio. No incluyen cargos adicionales no registrados.
>
> ¿Deseas agregar o cambiar algo, o revisar el envío? Puedes indicar un cambio o decir «Enviar la solicitud»; antes de registrarla te pediré confirmar el resumen.

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":"ORDER","Suspended":null,"Question":"RequestedData","Items":[{"ProductName":"Hilo dental","Quantity":2}],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**4. Cliente:** Preparemos también una cita

**Antes:**

> He cambiado el foco sin enviar ni confirmar nada. La otra preparación queda guardada para retomarla.
>
> Podemos atenderte en estas sedes:
>
> 1. Sede Concepcion
> 2. Sede San Jeronimo
>
> ¿En cuál sede deseas atenderte? Puedes indicar el nombre o una opción como «la primera».

**Después:**

> Tu pedido sigue guardado; continuamos con tu reserva. Puedes pedir «Retomar el pedido» cuando quieras volver. Este cambio no envía ni confirma operaciones.
>
> Podemos atenderte en estas sedes:
>
> 1. Sede Concepcion
> 2. Sede San Jeronimo
>
> ¿En cuál sede deseas atenderte? Puedes indicar el nombre o una opción como «la primera».

Estado/efectos antes = después: `{"Active":"RESERVATION","Suspended":"Order","Question":"Selection","Items":[],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**5. Cliente:** En Concepción, limpieza dental

**Antes:**

> Busquemos un día que te acomode.
>
> ¿Para qué fecha deseas reservar? Puedes indicar día y mes, por ejemplo «03/11» o «3 de noviembre», o decir «mañana por la tarde»; comprobaré los horarios de la sede.

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":"RESERVATION","Suspended":"Order","Question":"RequestedData","Items":[],"SelectedLocationId":"sede-a","SelectedServiceId":"service-a","TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**6. Cliente:** mañana a las 10:00

**Antes:**

> Para completar tu reserva,
>
> ¿cuál es tu nombre y apellido?

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":"RESERVATION","Suspended":"Order","Question":"RequestedData","Items":[],"SelectedLocationId":"sede-a","SelectedServiceId":"service-a","TargetDate":"2026-11-01","TargetTime":"10:00","RealCustomerName":null,"Orders":0,"Reservations":0}`

**7. Cliente:** Elena Flores

**Antes:**

> 📋 *Reserva por confirmar*
>
> Sede: Sede Concepcion
> Servicio: Limpieza dental
> Fecha: 01/11/2026
> Hora: 10:00
> Paciente: Elena Flores
> Precio: S/ 0.00
>
> ¿Confirmas estos datos? Puedes responder «Confirmar» o «Sí» para registrar la cita.
> Para cambiar un dato, dime cuál o escribe «Editar». Para abandonar el borrador, escribe «Descartar».

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":"RESERVATION","Suspended":"Order","Question":"ConfirmReservation","Items":[],"SelectedLocationId":"sede-a","SelectedServiceId":"service-a","TargetDate":"2026-11-01","TargetTime":"10:00","RealCustomerName":"Elena Flores","Orders":0,"Reservations":0}`

**8. Cliente:** Retomemos mi selección de productos

**Antes:**

> Retomamos tu preparación sin confirmar ninguna operación. La otra preparación queda guardada para retomarla.
>
> 🛍️ *Tu solicitud para revisión*
>
> Así va tu pedido:
>
> • 2 × Hilo dental
>   PEN 5.00
>
> Subtotal parcial: S/ 5.00.
>
> Importes del catálogo; el negocio revisará la solicitud. No se confirma stock, pago ni aceptación del negocio. No incluyen cargos adicionales no registrados.
>
> ¿Deseas agregar o cambiar algo, o revisar el envío? Puedes indicar un cambio o decir «Enviar la solicitud»; antes de registrarla te pediré confirmar el resumen.

**Después:**

> Retomamos tu pedido. Tu reserva sigue guardada. Puedes pedir «Retomar la reserva» cuando quieras volver. Este cambio no envía ni confirma operaciones.
>
> 🛍️ *Tu solicitud para revisión*
>
> Así va tu pedido:
>
> • 2 × Hilo dental
>   PEN 5.00
>
> Subtotal parcial: S/ 5.00.
>
> Importes del catálogo; el negocio revisará la solicitud. No se confirma stock, pago ni aceptación del negocio. No incluyen cargos adicionales no registrados.
>
> ¿Deseas agregar o cambiar algo, o revisar el envío? Puedes indicar un cambio o decir «Enviar la solicitud»; antes de registrarla te pediré confirmar el resumen.

Estado/efectos antes = después: `{"Active":"ORDER","Suspended":"Reservation","Question":"RequestedData","Items":[{"ProductName":"Hilo dental","Quantity":2}],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**9. Cliente:** Ajusta Hilo dental a uno

**Antes:**

> 🛍️ *Tu solicitud para revisión*
>
> Así va tu pedido:
>
> • 1 × Hilo dental
>   PEN 2.50
>
> Subtotal parcial: S/ 2.50.
>
> Importes del catálogo; el negocio revisará la solicitud. No se confirma stock, pago ni aceptación del negocio. No incluyen cargos adicionales no registrados.
>
> ¿Deseas agregar o cambiar algo, o revisar el envío? Puedes indicar un cambio o decir «Enviar la solicitud»; antes de registrarla te pediré confirmar el resumen.

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":"ORDER","Suspended":"Reservation","Question":"RequestedData","Items":[{"ProductName":"Hilo dental","Quantity":1}],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**10. Cliente:** Enviar la solicitud

**Antes:**

> Revisa el resumen vigente de tu solicitud.
>
> 🛍️ *Tu solicitud para revisión*
>
> Así va tu pedido:
>
> • 1 × Hilo dental
>   PEN 2.50
>
> Subtotal parcial: S/ 2.50.
>
> Importes del catálogo; el negocio revisará la solicitud. No se confirma stock, pago ni aceptación del negocio. No incluyen cargos adicionales no registrados.
>
> ¿Confirmas que la enviemos al negocio con estos importes?
> Puedes responder «*Confirmar*» o «Sí» para enviar la solicitud, indicar un cambio o escribir «*Descartar*» para abandonar el borrador.

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":"ORDER","Suspended":"Reservation","Question":"ConfirmOrder","Items":[{"ProductName":"Hilo dental","Quantity":1}],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**11. Cliente:** Confirmar

**Antes:**

> Tu solicitud de pedido fue enviada. El negocio la revisará. Código: 0DFC32.

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":null,"Suspended":"Reservation","Question":"Information","Items":[],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":1,"Reservations":0}`

**12. Cliente:** Retomemos la cita que dejamos preparada

**Antes:**

> Retomamos tu preparación sin confirmar ninguna operación.
>
> Tu reserva sigue en preparación, sin confirmar. Puedes indicar qué dato cambiar o decir «Continuemos» para comprobar el horario y ver un resumen nuevo.

**Después:**

> Retomamos tu reserva. Este cambio no envía ni confirma operaciones.
>
> Tu reserva sigue en preparación, sin confirmar. Puedes indicar qué dato cambiar o decir «Continuemos» para comprobar el horario y ver un resumen nuevo.

Estado/efectos antes = después: `{"Active":"RESERVATION","Suspended":null,"Question":"RequestedData","Items":[],"SelectedLocationId":"sede-a","SelectedServiceId":"service-a","TargetDate":"2026-11-01","TargetTime":"10:00","RealCustomerName":"Elena Flores","Orders":1,"Reservations":0}`

**13. Cliente:** Continuemos

**Antes:**

> 📋 *Reserva por confirmar*
>
> Sede: Sede Concepcion
> Servicio: Limpieza dental
> Fecha: 01/11/2026
> Hora: 10:00
> Paciente: Elena Flores
> Precio: S/ 0.00
>
> ¿Confirmas estos datos? Puedes responder «Confirmar» o «Sí» para registrar la cita.
> Para cambiar un dato, dime cuál o escribe «Editar». Para abandonar el borrador, escribe «Descartar».

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":"RESERVATION","Suspended":null,"Question":"ConfirmReservation","Items":[],"SelectedLocationId":"sede-a","SelectedServiceId":"service-a","TargetDate":"2026-11-01","TargetTime":"10:00","RealCustomerName":"Elena Flores","Orders":1,"Reservations":0}`

**14. Cliente:** Confirmar

**Antes:**

> ✅ ¡Todo listo, Elena Flores! Tu reserva está confirmada:
> • Sede: Sede Concepcion
> • Servicio: Limpieza dental
> • Fecha: 01/11/2026
> • Hora: 10:00
> • Referencia: 11f161b3-67ce-4f12-8b4a-5af292ae6bc4
>
> ¡Te esperamos!

**Después:**

> ✅ ¡Todo listo, Elena Flores! Tu reserva está confirmada:
> • Sede: Sede Concepcion
> • Servicio: Limpieza dental
> • Fecha: 01/11/2026
> • Hora: 10:00
> • Referencia: 474b4046-0578-4ffe-9830-d0402e29e7a1
>
> ¡Te esperamos!

Estado/efectos antes = después: `{"Active":null,"Suspended":null,"Question":"Information","Items":[],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":"Elena Flores","Orders":1,"Reservations":1}`


### 4. ia-indisponible

**1. Cliente:** ¿Qué productos tienen?

**Antes:**

> *Productos del catálogo*
>
> • *Pan blanco*
>   S/ 2.00
>
> • *Pan integral*
>   S/ 2.50
>
> • *Queque*
>   S/ 10.00

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":null,"Suspended":null,"Question":"Information","Items":[],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**2. Cliente:** Quiero hacer un pedido

**Antes:**

> Te ayudo a preparar una solicitud para que el negocio la revise. Elegimos productos y cantidades, revisamos el resumen y después te pediré confirmar el envío.
>
> ¿Qué productos y cantidades deseas solicitar? Por ejemplo, «2 unidades de Pan integral». También puedes consultar «productos».

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":"ORDER","Suspended":null,"Question":"RequestedData","Items":[],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**3. Cliente:** Solicito dos unidades de Pan integral

**Antes:**

> 🛍️ *Tu solicitud para revisión*
>
> Así va tu pedido:
>
> • 2 × Pan integral
>   PEN 5.00
>
> Subtotal parcial: S/ 5.00.
>
> Importes del catálogo; el negocio revisará la solicitud. No se confirma stock, pago ni aceptación del negocio. No incluyen cargos adicionales no registrados.
>
> ¿Deseas agregar o cambiar algo, o revisar el envío? Puedes indicar un cambio o decir «Enviar la solicitud»; antes de registrarla te pediré confirmar el resumen.

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":"ORDER","Suspended":null,"Question":"RequestedData","Items":[{"ProductName":"Pan integral","Quantity":2}],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**4. Cliente:** Preparemos también una cita

**Antes:**

> He cambiado el foco sin enviar ni confirmar nada. La otra preparación queda guardada para retomarla.
>
> Podemos atenderte en estas sedes:
>
> 1. Sede Concepcion
> 2. Sede San Jeronimo
>
> ¿En cuál sede deseas atenderte? Puedes indicar el nombre o una opción como «la primera».

**Después:**

> Tu pedido sigue guardado; continuamos con tu reserva. Puedes pedir «Retomar el pedido» cuando quieras volver. Este cambio no envía ni confirma operaciones.
>
> Podemos atenderte en estas sedes:
>
> 1. Sede Concepcion
> 2. Sede San Jeronimo
>
> ¿En cuál sede deseas atenderte? Puedes indicar el nombre o una opción como «la primera».

Estado/efectos antes = después: `{"Active":"RESERVATION","Suspended":"Order","Question":"Selection","Items":[],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**5. Cliente:** En Concepción, limpieza dental

**Antes:**

> Busquemos un día que te acomode.
>
> ¿Para qué fecha deseas reservar? Puedes indicar día y mes, por ejemplo «03/11» o «3 de noviembre», o decir «mañana por la tarde»; comprobaré los horarios de la sede.

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":"RESERVATION","Suspended":"Order","Question":"RequestedData","Items":[],"SelectedLocationId":"sede-a","SelectedServiceId":"service-a","TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**6. Cliente:** Retomemos mi selección de productos

**Antes:**

> Retomamos tu preparación sin confirmar ninguna operación. La otra preparación queda guardada para retomarla.
>
> 🛍️ *Tu solicitud para revisión*
>
> Así va tu pedido:
>
> • 2 × Pan integral
>   PEN 5.00
>
> Subtotal parcial: S/ 5.00.
>
> Importes del catálogo; el negocio revisará la solicitud. No se confirma stock, pago ni aceptación del negocio. No incluyen cargos adicionales no registrados.
>
> ¿Deseas agregar o cambiar algo, o revisar el envío? Puedes indicar un cambio o decir «Enviar la solicitud»; antes de registrarla te pediré confirmar el resumen.

**Después:**

> Retomamos tu pedido. Tu reserva sigue guardada. Puedes pedir «Retomar la reserva» cuando quieras volver. Este cambio no envía ni confirma operaciones.
>
> 🛍️ *Tu solicitud para revisión*
>
> Así va tu pedido:
>
> • 2 × Pan integral
>   PEN 5.00
>
> Subtotal parcial: S/ 5.00.
>
> Importes del catálogo; el negocio revisará la solicitud. No se confirma stock, pago ni aceptación del negocio. No incluyen cargos adicionales no registrados.
>
> ¿Deseas agregar o cambiar algo, o revisar el envío? Puedes indicar un cambio o decir «Enviar la solicitud»; antes de registrarla te pediré confirmar el resumen.

Estado/efectos antes = después: `{"Active":"ORDER","Suspended":"Reservation","Question":"RequestedData","Items":[{"ProductName":"Pan integral","Quantity":2}],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**7. Cliente:** Retoca lo que habíamos visto

**Antes:**

> No he cambiado tu solicitud ni la he enviado. Conservamos tus productos y cantidades.
>
> Puedes pedir «Ver pedido», indicar una cantidad nueva o decir «Enviar la solicitud» para revisar el resumen. Si necesitas ayuda de una persona, escribe «asesor».

**Después:**

> No he cambiado tu solicitud ni la he enviado. Conservamos tus productos y cantidades.
>
> Puedes pedir «Ver pedido», indicar una cantidad nueva o decir «Enviar la solicitud» para revisar el resumen. Si necesitas ayuda de una persona, escribe «asesor».
>
> Tu pedido está en preparación y tu reserva sigue guardada. Puedes pedir «Retomar la reserva»; al volver comprobaremos de nuevo los datos.

Estado/efectos antes = después: `{"Active":"ORDER","Suspended":"Reservation","Question":"Information","Items":[{"ProductName":"Pan integral","Quantity":2}],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`


### 5. licencia-revocada

**1. Cliente:** ¿Qué productos tienen?

**Antes:**

> *Productos del catálogo*
>
> • *Pan blanco*
>   S/ 2.00
>
> • *Pan integral*
>   S/ 2.50
>
> • *Queque*
>   S/ 10.00

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":null,"Suspended":null,"Question":"Information","Items":[],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**2. Cliente:** Quiero hacer un pedido

**Antes:**

> Te ayudo a preparar una solicitud para que el negocio la revise. Elegimos productos y cantidades, revisamos el resumen y después te pediré confirmar el envío.
>
> ¿Qué productos y cantidades deseas solicitar? Por ejemplo, «2 unidades de Pan integral». También puedes consultar «productos».

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":"ORDER","Suspended":null,"Question":"RequestedData","Items":[],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**3. Cliente:** Solicito dos unidades de Pan integral

**Antes:**

> 🛍️ *Tu solicitud para revisión*
>
> Así va tu pedido:
>
> • 2 × Pan integral
>   PEN 5.00
>
> Subtotal parcial: S/ 5.00.
>
> Importes del catálogo; el negocio revisará la solicitud. No se confirma stock, pago ni aceptación del negocio. No incluyen cargos adicionales no registrados.
>
> ¿Deseas agregar o cambiar algo, o revisar el envío? Puedes indicar un cambio o decir «Enviar la solicitud»; antes de registrarla te pediré confirmar el resumen.

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":"ORDER","Suspended":null,"Question":"RequestedData","Items":[{"ProductName":"Pan integral","Quantity":2}],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**4. Cliente:** Preparemos también una cita

**Antes:**

> He cambiado el foco sin enviar ni confirmar nada. La otra preparación queda guardada para retomarla.
>
> Podemos atenderte en estas sedes:
>
> 1. Sede Concepcion
> 2. Sede San Jeronimo
>
> ¿En cuál sede deseas atenderte? Puedes indicar el nombre o una opción como «la primera».

**Después:**

> Tu pedido sigue guardado; continuamos con tu reserva. Puedes pedir «Retomar el pedido» cuando quieras volver. Este cambio no envía ni confirma operaciones.
>
> Podemos atenderte en estas sedes:
>
> 1. Sede Concepcion
> 2. Sede San Jeronimo
>
> ¿En cuál sede deseas atenderte? Puedes indicar el nombre o una opción como «la primera».

Estado/efectos antes = después: `{"Active":"RESERVATION","Suspended":"Order","Question":"Selection","Items":[],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**5. Cliente:** En Concepción, limpieza dental

**Antes:**

> Busquemos un día que te acomode.
>
> ¿Para qué fecha deseas reservar? Puedes indicar día y mes, por ejemplo «03/11» o «3 de noviembre», o decir «mañana por la tarde»; comprobaré los horarios de la sede.

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":"RESERVATION","Suspended":"Order","Question":"RequestedData","Items":[],"SelectedLocationId":"sede-a","SelectedServiceId":"service-a","TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**6. Cliente:** Retomemos mi selección de productos

**Antes:**

> Este negocio no tiene habilitados los pedidos.

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":"RESERVATION","Suspended":"Order","Question":"Information","Items":[],"SelectedLocationId":"sede-a","SelectedServiceId":"service-a","TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`


### 6. precio-nuevo

**1. Cliente:** ¿Qué productos tienen?

**Antes:**

> *Productos del catálogo*
>
> • *Pan blanco*
>   S/ 2.00
>
> • *Pan integral*
>   S/ 2.50
>
> • *Queque*
>   S/ 10.00

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":null,"Suspended":null,"Question":"Information","Items":[],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**2. Cliente:** Quiero hacer un pedido

**Antes:**

> Te ayudo a preparar una solicitud para que el negocio la revise. Elegimos productos y cantidades, revisamos el resumen y después te pediré confirmar el envío.
>
> ¿Qué productos y cantidades deseas solicitar? Por ejemplo, «2 unidades de Pan integral». También puedes consultar «productos».

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":"ORDER","Suspended":null,"Question":"RequestedData","Items":[],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**3. Cliente:** Solicito dos unidades de Pan integral

**Antes:**

> 🛍️ *Tu solicitud para revisión*
>
> Así va tu pedido:
>
> • 2 × Pan integral
>   PEN 5.00
>
> Subtotal parcial: S/ 5.00.
>
> Importes del catálogo; el negocio revisará la solicitud. No se confirma stock, pago ni aceptación del negocio. No incluyen cargos adicionales no registrados.
>
> ¿Deseas agregar o cambiar algo, o revisar el envío? Puedes indicar un cambio o decir «Enviar la solicitud»; antes de registrarla te pediré confirmar el resumen.

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":"ORDER","Suspended":null,"Question":"RequestedData","Items":[{"ProductName":"Pan integral","Quantity":2}],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**4. Cliente:** Preparemos también una cita

**Antes:**

> He cambiado el foco sin enviar ni confirmar nada. La otra preparación queda guardada para retomarla.
>
> Podemos atenderte en estas sedes:
>
> 1. Sede Concepcion
> 2. Sede San Jeronimo
>
> ¿En cuál sede deseas atenderte? Puedes indicar el nombre o una opción como «la primera».

**Después:**

> Tu pedido sigue guardado; continuamos con tu reserva. Puedes pedir «Retomar el pedido» cuando quieras volver. Este cambio no envía ni confirma operaciones.
>
> Podemos atenderte en estas sedes:
>
> 1. Sede Concepcion
> 2. Sede San Jeronimo
>
> ¿En cuál sede deseas atenderte? Puedes indicar el nombre o una opción como «la primera».

Estado/efectos antes = después: `{"Active":"RESERVATION","Suspended":"Order","Question":"Selection","Items":[],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**5. Cliente:** En Concepción, limpieza dental

**Antes:**

> Busquemos un día que te acomode.
>
> ¿Para qué fecha deseas reservar? Puedes indicar día y mes, por ejemplo «03/11» o «3 de noviembre», o decir «mañana por la tarde»; comprobaré los horarios de la sede.

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":"RESERVATION","Suspended":"Order","Question":"RequestedData","Items":[],"SelectedLocationId":"sede-a","SelectedServiceId":"service-a","TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**6. Cliente:** Retomemos mi selección de productos

**Antes:**

> Retomamos tu preparación sin confirmar ninguna operación. La otra preparación queda guardada para retomarla.
>
> 🛍️ *Tu solicitud para revisión*
>
> Así va tu pedido:
>
> • 2 × Pan integral
>   PEN 7.00
>
> Subtotal parcial: S/ 7.00.
>
> Importes del catálogo; el negocio revisará la solicitud. No se confirma stock, pago ni aceptación del negocio. No incluyen cargos adicionales no registrados.
>
> ¿Deseas agregar o cambiar algo, o revisar el envío? Puedes indicar un cambio o decir «Enviar la solicitud»; antes de registrarla te pediré confirmar el resumen.

**Después:**

> Retomamos tu pedido. Tu reserva sigue guardada. Puedes pedir «Retomar la reserva» cuando quieras volver. Este cambio no envía ni confirma operaciones.
>
> 🛍️ *Tu solicitud para revisión*
>
> Así va tu pedido:
>
> • 2 × Pan integral
>   PEN 7.00
>
> Subtotal parcial: S/ 7.00.
>
> Importes del catálogo; el negocio revisará la solicitud. No se confirma stock, pago ni aceptación del negocio. No incluyen cargos adicionales no registrados.
>
> ¿Deseas agregar o cambiar algo, o revisar el envío? Puedes indicar un cambio o decir «Enviar la solicitud»; antes de registrarla te pediré confirmar el resumen.

Estado/efectos antes = después: `{"Active":"ORDER","Suspended":"Reservation","Question":"RequestedData","Items":[{"ProductName":"Pan integral","Quantity":2}],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`


### 7. presupuesto-agotado

**1. Cliente:** ¿Qué productos tienen?

**Antes:**

> *Productos del catálogo*
>
> • *Pan blanco*
>   S/ 2.00
>
> • *Pan integral*
>   S/ 2.50
>
> • *Queque*
>   S/ 10.00

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":null,"Suspended":null,"Question":"Information","Items":[],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**2. Cliente:** Quiero hacer un pedido

**Antes:**

> Te ayudo a preparar una solicitud para que el negocio la revise. Elegimos productos y cantidades, revisamos el resumen y después te pediré confirmar el envío.
>
> ¿Qué productos y cantidades deseas solicitar? Por ejemplo, «2 unidades de Pan integral». También puedes consultar «productos».

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":"ORDER","Suspended":null,"Question":"RequestedData","Items":[],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**3. Cliente:** Solicito dos unidades de Pan integral

**Antes:**

> 🛍️ *Tu solicitud para revisión*
>
> Así va tu pedido:
>
> • 2 × Pan integral
>   PEN 5.00
>
> Subtotal parcial: S/ 5.00.
>
> Importes del catálogo; el negocio revisará la solicitud. No se confirma stock, pago ni aceptación del negocio. No incluyen cargos adicionales no registrados.
>
> ¿Deseas agregar o cambiar algo, o revisar el envío? Puedes indicar un cambio o decir «Enviar la solicitud»; antes de registrarla te pediré confirmar el resumen.

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":"ORDER","Suspended":null,"Question":"RequestedData","Items":[{"ProductName":"Pan integral","Quantity":2}],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**4. Cliente:** Preparemos también una cita

**Antes:**

> He cambiado el foco sin enviar ni confirmar nada. La otra preparación queda guardada para retomarla.
>
> Podemos atenderte en estas sedes:
>
> 1. Sede Concepcion
> 2. Sede San Jeronimo
>
> ¿En cuál sede deseas atenderte? Puedes indicar el nombre o una opción como «la primera».

**Después:**

> Tu pedido sigue guardado; continuamos con tu reserva. Puedes pedir «Retomar el pedido» cuando quieras volver. Este cambio no envía ni confirma operaciones.
>
> Podemos atenderte en estas sedes:
>
> 1. Sede Concepcion
> 2. Sede San Jeronimo
>
> ¿En cuál sede deseas atenderte? Puedes indicar el nombre o una opción como «la primera».

Estado/efectos antes = después: `{"Active":"RESERVATION","Suspended":"Order","Question":"Selection","Items":[],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**5. Cliente:** En Concepción, limpieza dental

**Antes:**

> Busquemos un día que te acomode.
>
> ¿Para qué fecha deseas reservar? Puedes indicar día y mes, por ejemplo «03/11» o «3 de noviembre», o decir «mañana por la tarde»; comprobaré los horarios de la sede.

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":"RESERVATION","Suspended":"Order","Question":"RequestedData","Items":[],"SelectedLocationId":"sede-a","SelectedServiceId":"service-a","TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**6. Cliente:** Retomemos mi selección de productos

**Antes:**

> Retomamos tu preparación sin confirmar ninguna operación. La otra preparación queda guardada para retomarla.
>
> 🛍️ *Tu solicitud para revisión*
>
> Así va tu pedido:
>
> • 2 × Pan integral
>   PEN 5.00
>
> Subtotal parcial: S/ 5.00.
>
> Importes del catálogo; el negocio revisará la solicitud. No se confirma stock, pago ni aceptación del negocio. No incluyen cargos adicionales no registrados.
>
> ¿Deseas agregar o cambiar algo, o revisar el envío? Puedes indicar un cambio o decir «Enviar la solicitud»; antes de registrarla te pediré confirmar el resumen.

**Después:**

> Retomamos tu pedido. Tu reserva sigue guardada. Puedes pedir «Retomar la reserva» cuando quieras volver. Este cambio no envía ni confirma operaciones.
>
> 🛍️ *Tu solicitud para revisión*
>
> Así va tu pedido:
>
> • 2 × Pan integral
>   PEN 5.00
>
> Subtotal parcial: S/ 5.00.
>
> Importes del catálogo; el negocio revisará la solicitud. No se confirma stock, pago ni aceptación del negocio. No incluyen cargos adicionales no registrados.
>
> ¿Deseas agregar o cambiar algo, o revisar el envío? Puedes indicar un cambio o decir «Enviar la solicitud»; antes de registrarla te pediré confirmar el resumen.

Estado/efectos antes = después: `{"Active":"ORDER","Suspended":"Reservation","Question":"RequestedData","Items":[{"ProductName":"Pan integral","Quantity":2}],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**7. Cliente:** Retoca lo que habíamos visto

**Antes:**

> No he cambiado tu solicitud ni la he enviado. Conservamos tus productos y cantidades.
>
> Puedes pedir «Ver pedido», indicar una cantidad nueva o decir «Enviar la solicitud» para revisar el resumen. Si necesitas ayuda de una persona, escribe «asesor».

**Después:**

> No he cambiado tu solicitud ni la he enviado. Conservamos tus productos y cantidades.
>
> Puedes pedir «Ver pedido», indicar una cantidad nueva o decir «Enviar la solicitud» para revisar el resumen. Si necesitas ayuda de una persona, escribe «asesor».
>
> Tu pedido está en preparación y tu reserva sigue guardada. Puedes pedir «Retomar la reserva»; al volver comprobaremos de nuevo los datos.

Estado/efectos antes = después: `{"Active":"ORDER","Suspended":"Reservation","Question":"Information","Items":[{"ProductName":"Pan integral","Quantity":2}],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`


### 8. referencia-ambigua

**1. Cliente:** ¿Qué productos tienen?

**Antes:**

> *Productos del catálogo*
>
> • *Pan blanco*
>   S/ 2.00
>
> • *Pan integral*
>   S/ 2.50
>
> • *Queque*
>   S/ 10.00

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":null,"Suspended":null,"Question":"Information","Items":[],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**2. Cliente:** Quiero hacer un pedido

**Antes:**

> Te ayudo a preparar una solicitud para que el negocio la revise. Elegimos productos y cantidades, revisamos el resumen y después te pediré confirmar el envío.
>
> ¿Qué productos y cantidades deseas solicitar? Por ejemplo, «2 unidades de Pan integral». También puedes consultar «productos».

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":"ORDER","Suspended":null,"Question":"RequestedData","Items":[],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**3. Cliente:** Solicito dos unidades de Pan integral

**Antes:**

> 🛍️ *Tu solicitud para revisión*
>
> Así va tu pedido:
>
> • 2 × Pan integral
>   PEN 5.00
>
> Subtotal parcial: S/ 5.00.
>
> Importes del catálogo; el negocio revisará la solicitud. No se confirma stock, pago ni aceptación del negocio. No incluyen cargos adicionales no registrados.
>
> ¿Deseas agregar o cambiar algo, o revisar el envío? Puedes indicar un cambio o decir «Enviar la solicitud»; antes de registrarla te pediré confirmar el resumen.

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":"ORDER","Suspended":null,"Question":"RequestedData","Items":[{"ProductName":"Pan integral","Quantity":2}],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**4. Cliente:** Preparemos también una cita

**Antes:**

> He cambiado el foco sin enviar ni confirmar nada. La otra preparación queda guardada para retomarla.
>
> Podemos atenderte en estas sedes:
>
> 1. Sede Concepcion
> 2. Sede San Jeronimo
>
> ¿En cuál sede deseas atenderte? Puedes indicar el nombre o una opción como «la primera».

**Después:**

> Tu pedido sigue guardado; continuamos con tu reserva. Puedes pedir «Retomar el pedido» cuando quieras volver. Este cambio no envía ni confirma operaciones.
>
> Podemos atenderte en estas sedes:
>
> 1. Sede Concepcion
> 2. Sede San Jeronimo
>
> ¿En cuál sede deseas atenderte? Puedes indicar el nombre o una opción como «la primera».

Estado/efectos antes = después: `{"Active":"RESERVATION","Suspended":"Order","Question":"Selection","Items":[],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**5. Cliente:** En Concepción, limpieza dental

**Antes:**

> Busquemos un día que te acomode.
>
> ¿Para qué fecha deseas reservar? Puedes indicar día y mes, por ejemplo «03/11» o «3 de noviembre», o decir «mañana por la tarde»; comprobaré los horarios de la sede.

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":"RESERVATION","Suspended":"Order","Question":"RequestedData","Items":[],"SelectedLocationId":"sede-a","SelectedServiceId":"service-a","TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**6. Cliente:** Retomemos mi selección de productos

**Antes:**

> Retomamos tu preparación sin confirmar ninguna operación. La otra preparación queda guardada para retomarla.
>
> 🛍️ *Tu solicitud para revisión*
>
> Así va tu pedido:
>
> • 2 × Pan integral
>   PEN 5.00
>
> Subtotal parcial: S/ 5.00.
>
> Importes del catálogo; el negocio revisará la solicitud. No se confirma stock, pago ni aceptación del negocio. No incluyen cargos adicionales no registrados.
>
> ¿Deseas agregar o cambiar algo, o revisar el envío? Puedes indicar un cambio o decir «Enviar la solicitud»; antes de registrarla te pediré confirmar el resumen.

**Después:**

> Retomamos tu pedido. Tu reserva sigue guardada. Puedes pedir «Retomar la reserva» cuando quieras volver. Este cambio no envía ni confirma operaciones.
>
> 🛍️ *Tu solicitud para revisión*
>
> Así va tu pedido:
>
> • 2 × Pan integral
>   PEN 5.00
>
> Subtotal parcial: S/ 5.00.
>
> Importes del catálogo; el negocio revisará la solicitud. No se confirma stock, pago ni aceptación del negocio. No incluyen cargos adicionales no registrados.
>
> ¿Deseas agregar o cambiar algo, o revisar el envío? Puedes indicar un cambio o decir «Enviar la solicitud»; antes de registrarla te pediré confirmar el resumen.

Estado/efectos antes = después: `{"Active":"ORDER","Suspended":"Reservation","Question":"RequestedData","Items":[{"ProductName":"Pan integral","Quantity":2}],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**7. Cliente:** Quiero añadir un queque

**Antes:**

> No he cambiado tu solicitud ni la he enviado. Conservamos tus productos y cantidades.
>
> Puedes pedir «Ver pedido», indicar una cantidad nueva o decir «Enviar la solicitud» para revisar el resumen. Si necesitas ayuda de una persona, escribe «asesor».

**Después:**

> No he cambiado tu solicitud ni la he enviado. Conservamos tus productos y cantidades.
>
> Puedes pedir «Ver pedido», indicar una cantidad nueva o decir «Enviar la solicitud» para revisar el resumen. Si necesitas ayuda de una persona, escribe «asesor».
>
> Tu pedido está en preparación y tu reserva sigue guardada. Puedes pedir «Retomar la reserva»; al volver comprobaremos de nuevo los datos.

Estado/efectos antes = después: `{"Active":"ORDER","Suspended":"Reservation","Question":"Information","Items":[{"ProductName":"Pan integral","Quantity":2}],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**8. Cliente:** Deja uno de esos

**Antes:**

> No he cambiado tu solicitud ni la he enviado. Conservamos tus productos y cantidades.
>
> Puedes pedir «Ver pedido», indicar una cantidad nueva o decir «Enviar la solicitud» para revisar el resumen. Si necesitas ayuda de una persona, escribe «asesor».

**Después:**

> No he cambiado tu solicitud ni la he enviado. Conservamos tus productos y cantidades.
>
> Puedes pedir «Ver pedido», indicar una cantidad nueva o decir «Enviar la solicitud» para revisar el resumen. Si necesitas ayuda de una persona, escribe «asesor».
>
> Tu pedido está en preparación y tu reserva sigue guardada. Puedes pedir «Retomar la reserva»; al volver comprobaremos de nuevo los datos.

Estado/efectos antes = después: `{"Active":"ORDER","Suspended":"Reservation","Question":"Information","Items":[{"ProductName":"Pan integral","Quantity":2}],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**9. Cliente:** Descartar suspendido

**Antes:**

> ¿Confirmas que descartemos solo el borrador suspendido, aunque haya vencido? La preparación activa no cambiará. Responde «Sí» o «No».

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":"ORDER","Suspended":"Reservation","Question":"DiscardDrafts","Items":[{"ProductName":"Pan integral","Quantity":2}],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**10. Cliente:** No

**Antes:**

> No he descartado ninguna preparación. Puedes continuar o retomar el otro borrador.

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":"ORDER","Suspended":"Reservation","Question":"Information","Items":[{"ProductName":"Pan integral","Quantity":2}],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`


### 9. referencia-vencida

**1. Cliente:** ¿Qué productos tienen?

**Antes:**

> *Productos del catálogo*
>
> • *Pan blanco*
>   S/ 2.00
>
> • *Pan integral*
>   S/ 2.50
>
> • *Queque*
>   S/ 10.00

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":null,"Suspended":null,"Question":"Information","Items":[],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**2. Cliente:** Quiero hacer un pedido

**Antes:**

> Te ayudo a preparar una solicitud para que el negocio la revise. Elegimos productos y cantidades, revisamos el resumen y después te pediré confirmar el envío.
>
> ¿Qué productos y cantidades deseas solicitar? Por ejemplo, «2 unidades de Pan integral». También puedes consultar «productos».

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":"ORDER","Suspended":null,"Question":"RequestedData","Items":[],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**3. Cliente:** Solicito dos unidades de Pan integral

**Antes:**

> 🛍️ *Tu solicitud para revisión*
>
> Así va tu pedido:
>
> • 2 × Pan integral
>   PEN 5.00
>
> Subtotal parcial: S/ 5.00.
>
> Importes del catálogo; el negocio revisará la solicitud. No se confirma stock, pago ni aceptación del negocio. No incluyen cargos adicionales no registrados.
>
> ¿Deseas agregar o cambiar algo, o revisar el envío? Puedes indicar un cambio o decir «Enviar la solicitud»; antes de registrarla te pediré confirmar el resumen.

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":"ORDER","Suspended":null,"Question":"RequestedData","Items":[{"ProductName":"Pan integral","Quantity":2}],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**4. Cliente:** Preparemos también una cita

**Antes:**

> He cambiado el foco sin enviar ni confirmar nada. La otra preparación queda guardada para retomarla.
>
> Podemos atenderte en estas sedes:
>
> 1. Sede Concepcion
> 2. Sede San Jeronimo
>
> ¿En cuál sede deseas atenderte? Puedes indicar el nombre o una opción como «la primera».

**Después:**

> Tu pedido sigue guardado; continuamos con tu reserva. Puedes pedir «Retomar el pedido» cuando quieras volver. Este cambio no envía ni confirma operaciones.
>
> Podemos atenderte en estas sedes:
>
> 1. Sede Concepcion
> 2. Sede San Jeronimo
>
> ¿En cuál sede deseas atenderte? Puedes indicar el nombre o una opción como «la primera».

Estado/efectos antes = después: `{"Active":"RESERVATION","Suspended":"Order","Question":"Selection","Items":[],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**5. Cliente:** En Concepción, limpieza dental

**Antes:**

> Busquemos un día que te acomode.
>
> ¿Para qué fecha deseas reservar? Puedes indicar día y mes, por ejemplo «03/11» o «3 de noviembre», o decir «mañana por la tarde»; comprobaré los horarios de la sede.

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":"RESERVATION","Suspended":"Order","Question":"RequestedData","Items":[],"SelectedLocationId":"sede-a","SelectedServiceId":"service-a","TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**6. Cliente:** Retomemos mi selección de productos

**Antes:**

> Retomamos tu preparación sin confirmar ninguna operación. La otra preparación queda guardada para retomarla.
>
> 🛍️ *Tu solicitud para revisión*
>
> Así va tu pedido:
>
> • 2 × Pan integral
>   PEN 5.00
>
> Subtotal parcial: S/ 5.00.
>
> Importes del catálogo; el negocio revisará la solicitud. No se confirma stock, pago ni aceptación del negocio. No incluyen cargos adicionales no registrados.
>
> ¿Deseas agregar o cambiar algo, o revisar el envío? Puedes indicar un cambio o decir «Enviar la solicitud»; antes de registrarla te pediré confirmar el resumen.

**Después:**

> Retomamos tu pedido. Tu reserva sigue guardada. Puedes pedir «Retomar la reserva» cuando quieras volver. Este cambio no envía ni confirma operaciones.
>
> 🛍️ *Tu solicitud para revisión*
>
> Así va tu pedido:
>
> • 2 × Pan integral
>   PEN 5.00
>
> Subtotal parcial: S/ 5.00.
>
> Importes del catálogo; el negocio revisará la solicitud. No se confirma stock, pago ni aceptación del negocio. No incluyen cargos adicionales no registrados.
>
> ¿Deseas agregar o cambiar algo, o revisar el envío? Puedes indicar un cambio o decir «Enviar la solicitud»; antes de registrarla te pediré confirmar el resumen.

Estado/efectos antes = después: `{"Active":"ORDER","Suspended":"Reservation","Question":"RequestedData","Items":[{"ProductName":"Pan integral","Quantity":2}],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**7. Cliente:** Rectifica uno de ese

**Antes:**

> ¿Cuál producto activo deseas cambiar? Conservamos el pedido.

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":"ORDER","Suspended":"Reservation","Question":"RequestedData","Items":[{"ProductName":"Pan integral","Quantity":2}],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`


### 10. restaurante

**1. Cliente:** ¿Qué productos tienen?

**Antes:**

> *Productos del catálogo*
>
> • *Menú vegetal*
>   S/ 2.50
>
> • *Pan blanco*
>   S/ 2.00
>
> • *Queque*
>   S/ 10.00

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":null,"Suspended":null,"Question":"Information","Items":[],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**2. Cliente:** Quiero hacer un pedido

**Antes:**

> Te ayudo a preparar una solicitud para que el negocio la revise. Elegimos productos y cantidades, revisamos el resumen y después te pediré confirmar el envío.
>
> ¿Qué productos y cantidades deseas solicitar? Por ejemplo, «2 unidades de Menú vegetal». También puedes consultar «productos».

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":"ORDER","Suspended":null,"Question":"RequestedData","Items":[],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**3. Cliente:** Solicito dos unidades de Menú vegetal

**Antes:**

> 🛍️ *Tu solicitud para revisión*
>
> Así va tu pedido:
>
> • 2 × Menú vegetal
>   PEN 5.00
>
> Subtotal parcial: S/ 5.00.
>
> Importes del catálogo; el negocio revisará la solicitud. No se confirma stock, pago ni aceptación del negocio. No incluyen cargos adicionales no registrados.
>
> ¿Deseas agregar o cambiar algo, o revisar el envío? Puedes indicar un cambio o decir «Enviar la solicitud»; antes de registrarla te pediré confirmar el resumen.

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":"ORDER","Suspended":null,"Question":"RequestedData","Items":[{"ProductName":"Menú vegetal","Quantity":2}],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**4. Cliente:** Preparemos también una cita

**Antes:**

> He cambiado el foco sin enviar ni confirmar nada. La otra preparación queda guardada para retomarla.
>
> Podemos atenderte en estas sedes:
>
> 1. Sede Concepcion
> 2. Sede San Jeronimo
>
> ¿En cuál sede deseas atenderte? Puedes indicar el nombre o una opción como «la primera».

**Después:**

> Tu pedido sigue guardado; continuamos con tu reserva. Puedes pedir «Retomar el pedido» cuando quieras volver. Este cambio no envía ni confirma operaciones.
>
> Podemos atenderte en estas sedes:
>
> 1. Sede Concepcion
> 2. Sede San Jeronimo
>
> ¿En cuál sede deseas atenderte? Puedes indicar el nombre o una opción como «la primera».

Estado/efectos antes = después: `{"Active":"RESERVATION","Suspended":"Order","Question":"Selection","Items":[],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**5. Cliente:** En Concepción, limpieza dental

**Antes:**

> Busquemos un día que te acomode.
>
> ¿Para qué fecha deseas reservar? Puedes indicar día y mes, por ejemplo «03/11» o «3 de noviembre», o decir «mañana por la tarde»; comprobaré los horarios de la sede.

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":"RESERVATION","Suspended":"Order","Question":"RequestedData","Items":[],"SelectedLocationId":"sede-a","SelectedServiceId":"service-a","TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**6. Cliente:** Retomemos mi selección de productos

**Antes:**

> Retomamos tu preparación sin confirmar ninguna operación. La otra preparación queda guardada para retomarla.
>
> 🛍️ *Tu solicitud para revisión*
>
> Así va tu pedido:
>
> • 2 × Menú vegetal
>   PEN 5.00
>
> Subtotal parcial: S/ 5.00.
>
> Importes del catálogo; el negocio revisará la solicitud. No se confirma stock, pago ni aceptación del negocio. No incluyen cargos adicionales no registrados.
>
> ¿Deseas agregar o cambiar algo, o revisar el envío? Puedes indicar un cambio o decir «Enviar la solicitud»; antes de registrarla te pediré confirmar el resumen.

**Después:**

> Retomamos tu pedido. Tu reserva sigue guardada. Puedes pedir «Retomar la reserva» cuando quieras volver. Este cambio no envía ni confirma operaciones.
>
> 🛍️ *Tu solicitud para revisión*
>
> Así va tu pedido:
>
> • 2 × Menú vegetal
>   PEN 5.00
>
> Subtotal parcial: S/ 5.00.
>
> Importes del catálogo; el negocio revisará la solicitud. No se confirma stock, pago ni aceptación del negocio. No incluyen cargos adicionales no registrados.
>
> ¿Deseas agregar o cambiar algo, o revisar el envío? Puedes indicar un cambio o decir «Enviar la solicitud»; antes de registrarla te pediré confirmar el resumen.

Estado/efectos antes = después: `{"Active":"ORDER","Suspended":"Reservation","Question":"RequestedData","Items":[{"ProductName":"Menú vegetal","Quantity":2}],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**7. Cliente:** Descartar activo

**Antes:**

> ¿Confirmas que descartemos solo el borrador activo? El suspendido seguirá guardado. Responde «Sí» o «No».

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":"ORDER","Suspended":"Reservation","Question":"DiscardDrafts","Items":[{"ProductName":"Menú vegetal","Quantity":2}],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**8. Cliente:** Sí

**Antes:**

> He descartado solo el borrador activo. El suspendido sigue guardado para retomarlo.

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":null,"Suspended":"Reservation","Question":"Information","Items":[],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`


### 11. salon

**1. Cliente:** ¿Qué productos tienen?

**Antes:**

> *Productos del catálogo*
>
> • *Champú*
>   S/ 2.50
>
> • *Pan blanco*
>   S/ 2.00
>
> • *Queque*
>   S/ 10.00

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":null,"Suspended":null,"Question":"Information","Items":[],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**2. Cliente:** Quiero hacer un pedido

**Antes:**

> Te ayudo a preparar una solicitud para que el negocio la revise. Elegimos productos y cantidades, revisamos el resumen y después te pediré confirmar el envío.
>
> ¿Qué productos y cantidades deseas solicitar? Por ejemplo, «2 unidades de Champú». También puedes consultar «productos».

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":"ORDER","Suspended":null,"Question":"RequestedData","Items":[],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**3. Cliente:** Solicito dos unidades de Champú

**Antes:**

> 🛍️ *Tu solicitud para revisión*
>
> Así va tu pedido:
>
> • 2 × Champú
>   PEN 5.00
>
> Subtotal parcial: S/ 5.00.
>
> Importes del catálogo; el negocio revisará la solicitud. No se confirma stock, pago ni aceptación del negocio. No incluyen cargos adicionales no registrados.
>
> ¿Deseas agregar o cambiar algo, o revisar el envío? Puedes indicar un cambio o decir «Enviar la solicitud»; antes de registrarla te pediré confirmar el resumen.

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":"ORDER","Suspended":null,"Question":"RequestedData","Items":[{"ProductName":"Champú","Quantity":2}],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**4. Cliente:** Preparemos también una cita

**Antes:**

> He cambiado el foco sin enviar ni confirmar nada. La otra preparación queda guardada para retomarla.
>
> Podemos atenderte en estas sedes:
>
> 1. Sede Concepcion
> 2. Sede San Jeronimo
>
> ¿En cuál sede deseas atenderte? Puedes indicar el nombre o una opción como «la primera».

**Después:**

> Tu pedido sigue guardado; continuamos con tu reserva. Puedes pedir «Retomar el pedido» cuando quieras volver. Este cambio no envía ni confirma operaciones.
>
> Podemos atenderte en estas sedes:
>
> 1. Sede Concepcion
> 2. Sede San Jeronimo
>
> ¿En cuál sede deseas atenderte? Puedes indicar el nombre o una opción como «la primera».

Estado/efectos antes = después: `{"Active":"RESERVATION","Suspended":"Order","Question":"Selection","Items":[],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**5. Cliente:** En Concepción, limpieza dental

**Antes:**

> Busquemos un día que te acomode.
>
> ¿Para qué fecha deseas reservar? Puedes indicar día y mes, por ejemplo «03/11» o «3 de noviembre», o decir «mañana por la tarde»; comprobaré los horarios de la sede.

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":"RESERVATION","Suspended":"Order","Question":"RequestedData","Items":[],"SelectedLocationId":"sede-a","SelectedServiceId":"service-a","TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**6. Cliente:** Retomemos mi selección de productos

**Antes:**

> Retomamos tu preparación sin confirmar ninguna operación. La otra preparación queda guardada para retomarla.
>
> 🛍️ *Tu solicitud para revisión*
>
> Así va tu pedido:
>
> • 2 × Champú
>   PEN 5.00
>
> Subtotal parcial: S/ 5.00.
>
> Importes del catálogo; el negocio revisará la solicitud. No se confirma stock, pago ni aceptación del negocio. No incluyen cargos adicionales no registrados.
>
> ¿Deseas agregar o cambiar algo, o revisar el envío? Puedes indicar un cambio o decir «Enviar la solicitud»; antes de registrarla te pediré confirmar el resumen.

**Después:**

> Retomamos tu pedido. Tu reserva sigue guardada. Puedes pedir «Retomar la reserva» cuando quieras volver. Este cambio no envía ni confirma operaciones.
>
> 🛍️ *Tu solicitud para revisión*
>
> Así va tu pedido:
>
> • 2 × Champú
>   PEN 5.00
>
> Subtotal parcial: S/ 5.00.
>
> Importes del catálogo; el negocio revisará la solicitud. No se confirma stock, pago ni aceptación del negocio. No incluyen cargos adicionales no registrados.
>
> ¿Deseas agregar o cambiar algo, o revisar el envío? Puedes indicar un cambio o decir «Enviar la solicitud»; antes de registrarla te pediré confirmar el resumen.

Estado/efectos antes = después: `{"Active":"ORDER","Suspended":"Reservation","Question":"RequestedData","Items":[{"ProductName":"Champú","Quantity":2}],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**7. Cliente:** Descartar activo

**Antes:**

> ¿Confirmas que descartemos solo el borrador activo? El suspendido seguirá guardado. Responde «Sí» o «No».

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":"ORDER","Suspended":"Reservation","Question":"DiscardDrafts","Items":[{"ProductName":"Champú","Quantity":2}],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**8. Cliente:** Sí

**Antes:**

> He descartado solo el borrador activo. El suspendido sigue guardado para retomarlo.

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":null,"Suspended":"Reservation","Question":"Information","Items":[],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`


### 12. tienda

**1. Cliente:** ¿Qué productos tienen?

**Antes:**

> *Productos del catálogo*
>
> • *Pan blanco*
>   S/ 2.00
>
> • *Pan integral*
>   S/ 2.50
>
> • *Queque*
>   S/ 10.00

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":null,"Suspended":null,"Question":"Information","Items":[],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**2. Cliente:** Quiero hacer un pedido

**Antes:**

> Te ayudo a preparar una solicitud para que el negocio la revise. Elegimos productos y cantidades, revisamos el resumen y después te pediré confirmar el envío.
>
> ¿Qué productos y cantidades deseas solicitar? Por ejemplo, «2 unidades de Pan integral». También puedes consultar «productos».

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":"ORDER","Suspended":null,"Question":"RequestedData","Items":[],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**3. Cliente:** Solicito dos unidades de Pan integral

**Antes:**

> 🛍️ *Tu solicitud para revisión*
>
> Así va tu pedido:
>
> • 2 × Pan integral
>   PEN 5.00
>
> Subtotal parcial: S/ 5.00.
>
> Importes del catálogo; el negocio revisará la solicitud. No se confirma stock, pago ni aceptación del negocio. No incluyen cargos adicionales no registrados.
>
> ¿Deseas agregar o cambiar algo, o revisar el envío? Puedes indicar un cambio o decir «Enviar la solicitud»; antes de registrarla te pediré confirmar el resumen.

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":"ORDER","Suspended":null,"Question":"RequestedData","Items":[{"ProductName":"Pan integral","Quantity":2}],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**4. Cliente:** Preparemos también una cita

**Antes:**

> He cambiado el foco sin enviar ni confirmar nada. La otra preparación queda guardada para retomarla.
>
> Podemos atenderte en estas sedes:
>
> 1. Sede Concepcion
> 2. Sede San Jeronimo
>
> ¿En cuál sede deseas atenderte? Puedes indicar el nombre o una opción como «la primera».

**Después:**

> Tu pedido sigue guardado; continuamos con tu reserva. Puedes pedir «Retomar el pedido» cuando quieras volver. Este cambio no envía ni confirma operaciones.
>
> Podemos atenderte en estas sedes:
>
> 1. Sede Concepcion
> 2. Sede San Jeronimo
>
> ¿En cuál sede deseas atenderte? Puedes indicar el nombre o una opción como «la primera».

Estado/efectos antes = después: `{"Active":"RESERVATION","Suspended":"Order","Question":"Selection","Items":[],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**5. Cliente:** En Concepción, limpieza dental

**Antes:**

> Busquemos un día que te acomode.
>
> ¿Para qué fecha deseas reservar? Puedes indicar día y mes, por ejemplo «03/11» o «3 de noviembre», o decir «mañana por la tarde»; comprobaré los horarios de la sede.

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":"RESERVATION","Suspended":"Order","Question":"RequestedData","Items":[],"SelectedLocationId":"sede-a","SelectedServiceId":"service-a","TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**6. Cliente:** Retomemos mi selección de productos

**Antes:**

> Retomamos tu preparación sin confirmar ninguna operación. La otra preparación queda guardada para retomarla.
>
> 🛍️ *Tu solicitud para revisión*
>
> Así va tu pedido:
>
> • 2 × Pan integral
>   PEN 5.00
>
> Subtotal parcial: S/ 5.00.
>
> Importes del catálogo; el negocio revisará la solicitud. No se confirma stock, pago ni aceptación del negocio. No incluyen cargos adicionales no registrados.
>
> ¿Deseas agregar o cambiar algo, o revisar el envío? Puedes indicar un cambio o decir «Enviar la solicitud»; antes de registrarla te pediré confirmar el resumen.

**Después:**

> Retomamos tu pedido. Tu reserva sigue guardada. Puedes pedir «Retomar la reserva» cuando quieras volver. Este cambio no envía ni confirma operaciones.
>
> 🛍️ *Tu solicitud para revisión*
>
> Así va tu pedido:
>
> • 2 × Pan integral
>   PEN 5.00
>
> Subtotal parcial: S/ 5.00.
>
> Importes del catálogo; el negocio revisará la solicitud. No se confirma stock, pago ni aceptación del negocio. No incluyen cargos adicionales no registrados.
>
> ¿Deseas agregar o cambiar algo, o revisar el envío? Puedes indicar un cambio o decir «Enviar la solicitud»; antes de registrarla te pediré confirmar el resumen.

Estado/efectos antes = después: `{"Active":"ORDER","Suspended":"Reservation","Question":"RequestedData","Items":[{"ProductName":"Pan integral","Quantity":2}],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**7. Cliente:** Descartar activo

**Antes:**

> ¿Confirmas que descartemos solo el borrador activo? El suspendido seguirá guardado. Responde «Sí» o «No».

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":"ORDER","Suspended":"Reservation","Question":"DiscardDrafts","Items":[{"ProductName":"Pan integral","Quantity":2}],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`

**8. Cliente:** Sí

**Antes:**

> He descartado solo el borrador activo. El suspendido sigue guardado para retomarlo.

**Después = Antes** (respuesta completa anterior).

Estado/efectos antes = después: `{"Active":null,"Suspended":"Reservation","Question":"Information","Items":[],"SelectedLocationId":null,"SelectedServiceId":null,"TargetDate":null,"TargetTime":null,"RealCustomerName":null,"Orders":0,"Reservations":0}`


## Estado final del árbol

```text
 M backend/NexFlow.Application/Features/AI/Interpretation/AiInterpreter.cs
 M backend/NexFlow.Application/Features/Automation/Dialogue/Composition/SecretaryPresentation.cs
 M backend/NexFlow.Application/Features/Automation/Dialogue/Contracts/CommerceTurn.cs
 M backend/NexFlow.Application/Features/Automation/Dialogue/Contracts/ConversationFocusState.cs
 M backend/NexFlow.Application/Features/Automation/Dialogue/Contracts/ConversationQuestion.cs
 M backend/NexFlow.Application/Features/Automation/Dialogue/Policy/OrderDraftMerger.cs
 M backend/NexFlow.Application/Features/Automation/Dialogue/Understanding/CommerceTurnResolver.cs
 M backend/NexFlow.Application/Features/Automation/ProcessMessage/ProcessIncomingMessageCommandHandler.cs
 M backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/AiResponseOrchestrator.cs
 M backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/ConversationFocusService.cs
 M backend/NexFlow.Tests/AssistantConversationTests.cs
?? backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/ConversationFocusService.Discard.cs
?? backend/NexFlow.Tests/Concierge/CtaContinuityTests.cs
?? backend/NexFlow.Tests/Concierge/CtaGate0Tests.cs
?? backend/NexFlow.Tests/Concierge/CtaGuidanceTests.cs
?? docs/NEXFLOW_CTA_AUDITORIA_F1_Y_PROMPT_SPRINTS_2_3_2026-10-31.md
?? docs/arquitectura/CTA_S2_S3_RESULTADOS.md
```

