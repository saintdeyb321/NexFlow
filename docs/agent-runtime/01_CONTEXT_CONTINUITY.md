# Agent Runtime 4.0 — F1: continuidad y precedencia

**Fecha:** 25/10/2026. **Alcance:** BLOQUE COMÚN + F1, G0 aprobado expresamente por el propietario. **Resultado local:** G1 propuesto para auditoría; no autoriza F2 ni producción.

## 1. Identidad y evidencia anterior a los cambios

La referencia local leída directamente en `.git/HEAD` y `.git/refs/heads/main` es `0da524d6e840ce6aeec77950d8e1f82b42c53e16`, rama referenciada `main`. No se ejecutó Git: no se certifica limpieza del árbol, contenido del commit ni identidad remota. Se conservaron en memoria las versiones iniciales de los archivos intervenidos para revisar exclusivamente el cambio propio. El informe F0 permanece intacto, SHA-256 `FC706CE45B3EFE4C3CEA02DDD3F25BC4D59980FAA18DE472CE101F14176E82F4`.

Se leyeron los cuatro documentos obligatorios. El bloque común y los antecedentes pertinentes de Concierge 3.0 ya examinados en F0 se usan como contexto; las decisiones se contrastaron con el código local. No se modificaron motores, almacenamiento, infraestructura, frontend ni corpus JSON.

Artefactos de esta ejecución: `backend/NexFlow.Tests/TestResults/agent-runtime-f1/`. Son pruebas sintéticas locales, no conversaciones ni evidencias de producción.

| Reproducción antes de su corrección | Resultado real / evidencia |
|---|---|
| Handler real → ChatFlow → KnowledgeService → IFaqRepository sin FAQ y después de revocarlo | `f1-faq-reproduced.trx`: 2 ejecutadas, 2 fallidas. Ambas registran una llamada indebida a `GetFaqsAsync`. |
| Invitación → sí; menú antiguo → nueva invitación → número; precio informativo → sí; disponibilidad → «¿Qué?» | `f1-continuity-before.trx`: 4 ejecutadas, 4 fallidas. Respectivamente: no inicia preparación, lee productos del menú anterior, crea una reserva, consume otra interpretación IA. |
| Solicitud semántica que menciona un servicio | `f1-precedence-before.trx`: 1 ejecutada, 1 fallida: `Expected Request / Actual Reservation`. No se ejecutó un motor comercial en esa reproducción. |
| Repetir resumen con «¿Qué?» y confirmar | `f1-repeat-before.trx`: 1 fallida. Regresión detectada durante F1: se conservaba el tipo de pregunta, pero se perdía su intención. Corregida antes de cerrar. |
| Contexto legacy con paso CONFIRM sin referencia verificable | `f1-legacy-before.trx`: 1 fallida: el consentimiento creaba directamente. Se retiró el puente de compatibilidad que lo permitía. |
| Menú previo seguido de una pregunta nueva de fecha | `f1-date-reference-before.trx`: 1 aprobada. La preparación existente ya limpia esa lista; se conserva como regresión, sin parche adicional. |

La primera ejecución exploratoria `f1-faq-before.trx` también falló, pero una rama tenía una FAQ sintética que no coincidía con la búsqueda. Se corrigió únicamente ese dato de prueba y se repitió **antes** del parche productivo; la evidencia válida del defecto es `f1-faq-reproduced.trx`.

## 2. Causas y cambios localizados

Rutas de esta sección relativas a `backend/NexFlow.Application/`:

| Causa confirmada | Cambio / evidencia actual |
|---|---|
| F0-15: faltaba Faq/FAQ en el permiso compartido y General podía consultar FAQ indirectamente | `Features/AI/Interpretation/AiInterpreter.cs:248`: guarda FAQ. `Features/Automation/ProcessMessage/Services/Flows/ConversationFlows.cs:790`: General conserva la consulta pública de perfil sin leer FAQ no licenciada. ChatFlow y el orquestador mantienen sus revalidaciones. |
| F0-02: invitación y resumen dependían de texto/paso, sin antecedente tipado | `Abstractions/Cache/IConversationCache.cs:7`; nuevo `Features/Automation/Dialogue/Contracts/ConversationQuestion.cs:10`: referencia aditiva dentro del mismo contexto. |
| F0-01: la comprensión no recibía la pregunta vigente | `Features/Automation/Dialogue/Contracts/TurnUnderstanding.cs:56`; `Features/AI/Interpretation/AiInterpreter.cs:144`: proyección mínima comprobada y filtrada por módulos, sin historial ni resumen personal. |
| F0-03: una segunda puerta léxica podía contradecir la interpretación | `Features/Automation/ProcessMessage/Services/AiResponseOrchestrator.cs:148`: exige acto/propuesta tipados en lugar de tres regex de inicio. Se mantiene la aclaración segura si falta evidencia. |
| Un StartOperation semántico con servicio podía transformarse en reserva aunque la intención fuera Request | `Features/Automation/Dialogue/Understanding/TurnUnderstandingPipeline.cs:169`: rama reservada a intención Reservation; `:187`: Request con acto/evidencia válidos conserva su intención. No cambia RequestFlow ni los motores. |

La revocación FAQ se comprueba con el handler y KnowledgeService reales sobre el fixture existente: tras retirarla no hay llamada a IFaqRepository. Otra regresión cubre el fallback General directamente y demuestra que el perfil público sigue disponible. No se conceden módulos productivos ni se exige FAQ para reservar o consultar otros módulos autorizados.

## 3. Contrato de pregunta vigente

`CurrentQuestion` es opcional dentro del `contextJson` actual. Contiene:

- `QuestionId` generado por backend, `WorkspaceId`, `SourceMessageId`, intención y `ExpectedReply`.
- Huella SHA-256 del objetivo/paso, sede, servicio, fecha, hora, preferencia, nombre, datos pendientes, ítems y decisión pendiente. La huella de selección incluye además las opciones y su orden.
- `IssuedAt` y `ExpiresAt`: vigencia de **10 minutos**, independiente de la caducidad de conversación/borrador. No introduce borrado ni TTL de almacenamiento.

`ExpectedReply` distingue Information, Selection, RequestedData, StartReservation, ConfirmReservation, RecoveryDecision y la compatibilidad de ConfirmOrder. No es un comando del LLM ni acredita por sí solo consentimiento.

La referencia es válida sólo con workspace coincidente, modo automático, enums válidos, vigencia y huellas iguales. Se guarda por el CAS existente antes de componer/entregar. Después de IA/composición se conservan las comprobaciones de StateVersion, takeover y licencia. StateVersion no se incorpora a la huella porque cada Save genera una nueva versión: la exclusión sigue dependiendo del CAS, no de una huella que se invalidaría a sí misma al persistir.

Precedencia efectiva:

1. Guardas existentes: identidad, modo humano, replay, privacidad de citas registradas, entradas hostiles, cancelación y decisiones pendientes.
2. Referencia informativa o de aclaración vigente; una invitación permite **preparar**, nunca crear. Una consulta informativa no concede consentimiento al resumen anterior.
3. Confirmación de reserva: afirmación inequívoca según ConversationConfirmation + resumen tipado vigente + paso CONFIRM + permisos y validación final del flujo/motor.
4. Opciones sólo bajo referencia Selection válida, con revalidación de datos/licencia. Los datos absolutos, como `16:00`, pueden resolverse y validarse normalmente, sin convertirse en índices de una lista vieja.
5. Pipeline existente para datos locales inequívocos o una interpretación contextual presupuestada por AiRouter cuando falta comprensión. Una intención transaccional aislada no sustituye un acto/propuesta respaldado.

«¿Qué?» conserva tipo e intención del antecedente autorizado; repetir el resumen no confirma nada ni pierde la posibilidad de consentir después. «Ya te lo dije» usa la reparación existente sin borrar slots. Las interrupciones conservan borradores. Se preserva `LastQuestion` de OrderFlow para no reemplazar su continuación por el texto de una cotización; no se modifica el flujo de pedidos.

La proyección al intérprete añade únicamente respuesta esperada, intención, pregunta lingüística derivada del backend y tema informativo tipado. Mantiene los límites anteriores de hints de sedes/servicios y fecha. No envía QuestionId, workspace, SourceMessageId, nombre persistido, respuesta completa, resumen ni historial. La pregunta de la proyección no puede autorizar operaciones. El mensaje actual continúa siendo dato no confiable y podría contener PII aportada por el consumidor.

## 4. Consolidación, sin árboles nuevos

- Eliminadas las tres regex redundantes de inicio de reserva/pedido/solicitud en el orquestador. Su barrera de seguridad usa acto/propuestas; la evidencia se valida en el pipeline.
- Eliminadas las dos clasificaciones finales independientes de horarios/ubicaciones de Recovery. Handler y recuperación reutilizan `InformationalRequest.ShortcutTopic` y una normalización común para los mismos atajos completos existentes.
- DialoguePolicy deja de interpretar indiscriminadamente un sí informativo como reanudación de reserva. Recovery y orquestador usan el mismo `CanConfirmReservation`.
- Menú, invitación y pregunta informativa dejan de compartir la interpretación implícita de una afirmación. No se añadieron diccionarios de sinónimos, erratas ni reglas por rubro.
- No se eliminaron comprobaciones de autorización, replay, consentimiento, CAS o handoff. Las comprobaciones a ambos lados de una espera siguen siendo necesarias.

Persisten heurísticas anteriores en Recovery/Policy/Commerce: F1 no pretende completar F2–F7 ni rediseñar todos los flujos.

## 5. Archivos intervenidos

Código backend, bajo `backend/NexFlow.Application/`:

1. `Abstractions/Cache/IConversationCache.cs`
2. `Features/AI/Interpretation/AiInterpreter.cs`
3. `Features/Automation/Dialogue/Contracts/ConversationQuestion.cs` — nuevo.
4. `Features/Automation/Dialogue/Contracts/TurnUnderstanding.cs`
5. `Features/Automation/Dialogue/Policy/DialoguePolicy.cs`
6. `Features/Automation/Dialogue/Understanding/InformationalRequest.cs`
7. `Features/Automation/Dialogue/Understanding/TurnUnderstandingPipeline.cs`
8. `Features/Automation/ProcessMessage/ProcessIncomingMessageCommandHandler.cs`
9. `Features/Automation/ProcessMessage/Services/AiResponseOrchestrator.cs`
10. `Features/Automation/ProcessMessage/Services/ConversationRecoveryService.cs`
11. `Features/Automation/ProcessMessage/Services/Flows/ConversationFlows.cs` — sólo el fallback FAQ de ChatFlow; BookingFlow no modificado.

Pruebas, bajo `backend/NexFlow.Tests/`:

- `AssistantLicenseBudgetTests.cs`
- `AssistantConversationTests.cs`
- `Concierge/DialogueIntegrationTests.cs`
- `Concierge/InformationalRecoverySprint1Tests.cs`
- `Concierge/Sprint51ConversationTests.cs`

Documentación: este informe. Frontend: ningún archivo. Sin dependencias, configuración de despliegue, migraciones, cambios de AiRouter/proveedores, motores, Outbox ni repositorios productivos.

## 6. Pruebas y compilación reales

Comandos finales ejecutados desde la raíz:

```powershell
dotnet build backend/backend.slnx --no-restore
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --filter '(FullyQualifiedName~Concierge|FullyQualifiedName~Assistant|FullyQualifiedName~ZeroTokenKnowledgeAuthorizationTests|FullyQualifiedName~AiResilienceTests)&Category!=Integration&Category!=RedisIntegration&Category!=ProviderIntegration' --logger 'trx;LogFileName=f1-g1.trx' --results-directory backend/NexFlow.Tests/TestResults/agent-runtime-f1
```

**Build:** salida 0; 0 errores y 0 advertencias en la compilación incremental final (`f1-build.log`). Durante recompilaciones de pruebas aparecieron tres advertencias previas: dos CS8981 de la migración histórica `refactor` y una CS8602 en FirestoreCatalogRepository:228. No se modificaron esos archivos.

**Suite afectada:** 981 ejecutadas, **981 aprobadas**, 0 fallidas, 0 omitidas; 27 segundos de ejecución reportados. `f1-g1.trx` contiene los contadores. Incluye 19 casos añadidos de F1 sobre las clases/fixtures existentes; no se crearon fixtures ni infraestructura nuevos. Mock de IFaqRepository y router existentes como patrón, con KnowledgeService real, permiten observar lecturas/autorización y el prompt sin servicios externos.

Cobertura adicional conservada: presupuestos, negación/consentimiento, nombres, fechas y disponibilidad, recuperación, ámbitos comerciales, handoff concurrente, CAS, replays antiguos, efecto único y UnknownDelivery. Las categorías externas se **excluyeron**, no se presentan como aprobadas ni como parte de esas cero omisiones.

Resultados intermedios conservados: primera regresión 947/968 aprobadas (21 fallos), segunda 967/968 (1 fallo), tercera 975/975, ampliaciones 976/976 y 979/979. La revisión de compatibilidad legacy produjo 976/980 con cuatro preparaciones de tests pendientes; la ejecución final 981/981 las resuelve. No se ocultan estos intentos ni se suman ejecuciones repetidas como casos únicos.

Los fallos intermedios revelaron dos ajustes propios necesarios: no interpretar horas absolutas como etiquetas de menú obsoleto y conservar ConfirmOrder. Se revalidaron nombres ante referencias desactualizadas. Las FAQ positivas del fixture de licencia completa ahora incluyen explícitamente FAQ; las pruebas de denegación la retiran.

Se mantuvieron todas las aserciones de seguridad existentes. Cambios de preparación explícitos:

- Antes, `Booking_multiple_turns...` consultaba ubicación y el siguiente sí creaba. Ahora se añade `information-reply` con `Assert.Empty(Bookings)` y `continuemos`; se conserva la confirmación final, replay y los asserts de la reserva exacta.
- `Exact_consent_commits_current_summary...` y dos métodos `Audit511` partían de un draft inyectado como CONFIRM sin una pregunta verificable. Ahora presentan el resumen con `continuemos` antes de sus mismos asserts de consentimiento, nombre, tenant y catálogo no licenciado. La nueva regresión legacy comprueba separadamente que el primer sí no crea.
- No se modificó ningún corpus JSON, incluyendo QualityHoldout61, ni se relajaron aserciones de CAS, hechos, titularidad o efectos únicos.

El SDK falló inicialmente dentro del sandbox por acceso denegado al Service Control Manager. Las ejecuciones posteriores se realizaron con la elevación local aprobada del comando .NET; sin restore, instalación de dependencias ni conexiones productivas.

## 7. Compatibilidad y costes

Los documentos antiguos sin CurrentQuestion siguen deserializándose y conservan slots, estado y capacidades. El próximo mensaje emitido crea la referencia dentro del mismo CAS. Una reserva legacy en CONFIRM **vuelve a validar y mostrar el resumen** antes de admitir un nuevo consentimiento. No se borra ni migra el contexto. Una referencia vencida o desactualizada tampoco permite crear; se recupera con pregunta/resumen vigente.

No hay segundo almacén ni memoria de historial para proveedores. La persistencia de la referencia en el límite de respuesta añade un Save de contexto a los recorridos orquestados que ya habían guardado: implica CAS Firestore y actualización de caché existentes. No se eliminaron saves previos por intuición. El atajo del handler incorpora la referencia en su Save existente.

Invitación, reparación exacta y respuestas locales probadas consumen 0 interpretaciones IA. En la ruta ambigua se mantiene una interpretación presupuestada y los fallbacks internos de AiRouter existentes. La proyección agrega texto acotado; no se midieron tokens facturados, p95 SaaS ni ahorro económico. La latencia y coste externos quedan pendientes de evaluación autorizada.

## 8. G1 y riesgos restantes

| Criterio | Estado local |
|---|---|
| Sí a invitación vigente prepara sólo la reserva correspondiente, sin crear | OK |
| Sí informativo no confirma una transacción anterior | OK |
| Consentimiento inequívoco exige resumen vigente; repetición y legacy seguros | OK |
| «¿Qué?» y reparación conservan antecedente/datos; menú antiguo no prevalece | OK |
| FAQ ausente/revocada no consulta el repositorio; perfil y otros módulos no bloqueados | OK |
| Interrupciones, replay, CAS, módulos, workspace y takeover | OK en regresiones simuladas seleccionadas |
| Fallos IA conservan borrador y no inventan operaciones | OK en regresiones simuladas |
| AiRouter, motores, UX de horas, flujo de pedidos e infraestructura preservados | OK; sin implementación F2–F7 |
| Auditoría independiente G1 | PENDIENTE |

Riesgos pendientes: calidad semántica con Gemini/Groq reales, anáforas no inequívocas que todavía requieren interpretación/aclaración, coste de la escritura adicional y comportamiento bajo carga real. Las integraciones con almacenes reales y el canal WhatsApp no se ejecutaron aquí. El guard de acceso a citas registradas sigue cerrado sin acreditación independiente. Una operación de transporte ya iniciada no se puede retirar físicamente con un takeover posterior; se conservan las barreras previas existentes.

Revertir código local antes de publicar no revierte operaciones comerciales. El nuevo campo es aditivo; un binario anterior puede ignorarlo, pero perdería las garantías nuevas de referencia: no se certifica compatibilidad de convivencia de escritores antiguos/nuevos ni rollback productivo. No se realizó despliegue.

**Dictamen:** G1 técnicamente satisfecho en la verificación local, listo para auditoría independiente. **Sin autorización de F2 ni GO comercial/producción. Detención en F1.**
