# F6 — Resiliencia, costos y calidad multiempresa

Referencia local efectiva: `166a1773a02aae4771467ed8fbc73a000d78add8`, comprobada leyendo HEAD/ref, sin Git. La evidencia de esta entrega corresponde al árbol local modificado, no a un commit publicado nuevo ni a CI. A y G5 aprobados localmente según 04_1/05. Sin servicios reales, secretos, migraciones, infraestructura o frontend.

## Auditoría del recorrido IA vigente

| Tramo | Comportamiento comprobado en código/pruebas |
|---|---|
| AiRouter | IntentExtraction→Groq, otros tipos→Gemini. Se conserva IAiRouter. ResponseComposition se añade al final del enum para distinguir tarea, sin cambiar los valores anteriores. |
| Fallback router | Sólo uno al otro proveedor por timeout, red, HTTP 408/429/5xx. No por 400/401/403/404, configuración o JSON inválido. Cancelación del token conjunto se propaga sin nuevo proveedor. |
| Gemini | Como máximo un modelo alternativo configurado por 429/503; puede ocurrir dentro de una sola invocación del router. |
| HTTP | Groq default 5 s, Gemini default 12 s; configuración validada de 1–60 s. Respuesta buffer ≤65.536 bytes, salida 1.024 tokens. No se cambia configuración. |
| Turno semántico | Deadline compartido de 20 s existente. Un router puede originar hasta 3 POST al combinar proveedores/modelos; no equivale a una sola petición facturada. |
| Composición | Opt-in existente, 800 ms default y máximo 2 s para todo el tramo opcional; un router, schema cerrado y fallback exacto. No llamadas IA para cortesías/datos faltantes/disponibilidad suficiente. |
| Herramientas | Tres admisiones y ocho segundos por BusinessReadTurn; límite de salidas/página. No equivalen a tres documentos Firestore ni a un límite de lecturas internas. |
| Presupuesto | Se conserva transacción durable por cliente/workspace, fuente idempotente y ventana monotónica. No se cambian límites ni se omiten revalidaciones. Una admisión/replay no acredita un HTTP ni un cargo monetario. |
| JSON/evidencia | AiInterpreter distingue InvalidResponse, Configuration, RateLimit, Timeout, Rejected, Unavailable. AiEvidenceValidator continúa validando el dato contra texto/entidades autorizadas; se cuenta Unsubstantiated sin registrar el contenido. |

## Telemetría implementada

`AiTelemetry` es instrumentación efímera, no autoridad ni estado conversacional. Usa AsyncLocal sólo para llevar workspace confiable/tarea durante la llamada; restaura el scope y no decide presupuesto, licencia o efectos. Dos scopes concurrentes se prueban sin mezcla de workspace.

| Señal | Instrumento/sitio | Alcance y cautela |
|---|---|---|
| Ruta local/semántica | NexFlow.Conversation / UnderstandingRoute | Ruta de comprensión, no POST ni facturación |
| Admisión IA | BudgetAdmission, Admitted/Denied | Fuente durable existente; no uso efectivo del proveedor |
| Invocación del proveedor | ConversationPhase / ProviderInvocations | Antes llamado AiInvocations: se renombra para no confundirlo con HTTP |
| HTTP iniciado | NexFlow.Ai / nexflow.ai.http.attempts | Un evento por invocación PostAsync del proveedor, primario/alternativo y categoría; transporte simulado en pruebas; intento, no garantía de recepción/cobro |
| Tiempo HTTP | nexflow.ai.http.duration (ms) | Hasta respuesta HTTP buffered o excepción; no tiempo de negocio extremo a extremo |
| Uso informado | nexflow.ai.tokens con kind Input/Output/Total; usage.reports | Sólo campos enteros no negativos del proveedor. No sumar Total otra vez a Input/Output. Datos ausentes/malformados quedan desconocidos, no cero |
| Fallos | HTTP outcome/status, InterpretationFailure, SemanticEvidence | Categorías, sin texto privado de excepción/respuesta |
| Fases/repositorios | conversation.events + phase.duration | OfferingService/KnowledgeQuery existentes, ContextStateRead/Write añadidos. Operaciones de repositorio, no documentos cobrados |
| Composición | Composition outcome | VerifiedLayout, determinista, rechazo, timeout, fallback, presupuesto/permiso denegados |
| Aclaraciones | Clarification First/Repeated | Solicitudes ejecutadas, no indicador automático de error del cliente |
| Resultado comercial | BusinessResult / OperationStatus | Resultado del plan; **no** contador de filas nuevas. Replay/éxitos recuperados no se deben convertir en nuevas ventas |

Los Meters estándar no añaden paquetes, contenedores ni exporter. La recolección/retención operativa depende de un observador configurado fuera de esta entrega. No se promete historial durable de métricas. Las fases anidadas no se suman como latencia total.

Logs nuevos AiHttpAttempt/AiUsage: proveedor, tarea, rol de modelo, status/categoría, duración y tokens numéricos disponibles. Correlación de workspace mediante SHA-256 truncado a 16 caracteres; no se registran GUID raw, teléfono, nombre de consumidor, source, prompt, cuerpo, respuesta o claves. Es seudonimización del workspace, no autorización ni anonimización universal. Los tags de métricas excluyen workspace para evitar cardinalidad por tenant; los logs restringidos permiten correlación de uso. No se cambian las políticas de logging ajenas al recorrido inspeccionado ni se afirma haber auditado todos los logs del SaaS.

Groq lee usage.prompt_tokens/completion_tokens/total_tokens; Gemini lee usageMetadata.promptTokenCount/candidatesTokenCount/totalTokenCount. Sólo se conserva lo informado: no se inventa el total por suma ni se estiman tokens del prompt. Gemini podría informar tokens adicionales de razonamiento; Total y sus componentes no necesariamente coinciden. No se calcula costo monetario sin tarifas/modelo efectivo/período verificables.

## Optimización medida, sin retirar seguridad

`AiResponseOrchestrator.SendChat`, línea 313: cuando hay Information y no Choices, no guarda inmediatamente para volver a guardar la pregunta en Compose. Esa llamada siguiente persiste información y pregunta juntas mediante el mismo CAS **antes** de cualquier composición/red opcional. Se conserva el guardado previo si existe aclaración. Se mantienen las lecturas durables tras composición, StateVersion, modo humano, licencia y permisos; ContextRecovery sigue contrastando Redis con Firestore en cada lectura.

Experimento controlado RuntimeEfficiencyTests: mismo turno de perfil público en tres fixtures/workspaces sintéticos. Las etiquetas de sector no constituyen medición productiva por rubro. Se reutilizan los fixtures, InMemoryFirestore y repositorios actuales. No se incluyen lecturas diagnósticas de estado en el delta.

| Fixture | RPC reads antes→después | RPC commits antes→después | IA semántica | Composición IA | Reservas/pedidos |
|---|---:|---:|---:|---:|---:|
| clinic | 7→6 | 3→2 | 1→1 | 0→0 | 0/0 |
| hotel | 7→6 | 3→2 | 1→1 | 0→0 | 0/0 |
| store | 7→6 | 3→2 | 1→1 | 0→0 | 0/0 |

TRX baseline `c-measure-before.trx` (3/3); después `c-first.trx` y la batería conjunta. Ahorro observado por este turno/configuración: **una lectura RPC del CAS y un commit**, no una estimación global de costos. RuntimeEfficiencyTests exige no superar 6/2, pregunta coherente y replay sin segunda interpretación/efecto.

Latencias de baseline (ms): clinic 332,5209; hotel 4,7666; store 4,9803. Primer después: 232,7831; 3,4575; 4,4911. En la batería concurrente final: 38,6242; 13,0154; 22,352 respectivamente. Cold start, ejecución paralela y fake afectan fuertemente los valores: **no** se atribuye una mejora de latencia, p95 productivo ni SLO a estas muestras. Costos por 1.000 conversaciones, moneda/tarifa/período y calidad con Gemini/Groq real: PENDIENTES.

## Lecturas en origen y límites pendientes

OfferingService y repositorios actuales filtran algunas colecciones después de recuperarlas. Offset/Limit de KnowledgeService limitan respuesta, no una query Firestore paginada. Horarios pueden leer calendario general, sedes y un calendario local. Se conserva esta distinción del informe F2; no se añade caché con revocación insegura ni se afirma que cinco opciones cuesten cinco documentos. Sólo se elimina la escritura consecutiva demostrada; no se cambian las revalidaciones de nombre/catálogo al confirmar, ni la autoridad de Firestore/SQL.

El conteo de efectos **únicos** se verifica en los repositorios/fakes y las pruebas de replay/CAS/commit perdido; BusinessResult no permite inferirlo por sí solo. Una medición productiva exige contrastar registros durables y fuentes idempotentes mediante procedimientos de lectura autorizados, sin emitir otra operación. No se añade un contador monetario aproximado.

## Pruebas y resultados reales

Artefactos: `backend/NexFlow.Tests/TestResults/agent-runtime-f56/`.

| Ejecución | Ejecutadas | Aprobadas | Fallidas | Omitidas |
|---|---:|---:|---:|---:|
| a-historical-before.trx | 130 | 118 | 12 | 0 |
| a-denied-before.trx (reproducción) | 1 | 0 | 1 | 0 |
| a-versioned.trx | 38 | 38 | 0 | 0 |
| b-composition.trx | 139 | 139 | 0 | 0 |
| b-g5-regression.trx | 953 | 953 | 0 | 0 |
| c-measure-before.trx | 3 | 3 | 0 | 0 |
| c-first.trx | 156 | 156 | 0 | 0 |
| c-telemetry.trx | 162 | 162 | 0 | 0 |
| c-final-all-with-historical.trx | 1092 | 1080 | 12 | 0 |
| c-final-targeted.trx | 19 | 19 | 0 | 0 |
| c-reference-order-before.trx (defecto del prototipo) | 1 | 0 | 1 | 0 |
| c-reference-order-fixed.trx | 94 | 94 | 0 | 0 |
| c-final-v2-all-with-historical.trx (cierre definitivo) | 1096 | 1084 | 12 | 0 |

No se suman ejecuciones repetidas como cobertura única. La batería definitiva de 1096 incluye los 130 casos históricos originales: 118 aprobados/12 fallidos con **los mismos mensajes exactos de A**. Los otros 966 casos del mismo TRX están aprobados; esto es una partición del resultado, no una ejecución completa original verde. F3 v2: sus doce conversaciones están aprobadas. La ejecución anterior de 1092 no incluía la FAQ hostil ni la protección ordinal posterior; tras reproducir ese defecto, se fija schema v2 de agrupación visual con secuencia inmutable y se repite toda la batería. No se atribuyen los resultados del prototipo al verificador final.

Filtro conjunto reproducible (sin exclusión de evaluadores históricos):

```text
(FullyQualifiedName~Concierge|FullyQualifiedName~Assistant|FullyQualifiedName~Knowledge|FullyQualifiedName~AiResilienceTests|FullyQualifiedName~CatalogChatTests|FullyQualifiedName~ReservationsPeruTimeTests|FullyQualifiedName~ZeroTokenKnowledgeAuthorizationTests)&Category!=Integration&Category!=RedisIntegration&Category!=ProviderIntegration
```

Se conservan pruebas de consentimiento, referencias/contexto, correcciones, nombre, fecha/zona/DST, licencias/revocación, aislamiento, CAS, toma humana, replay, UnknownDelivery, commit y aviso recuperable. Nuevas: contrato F3 v2, Denied sin payload, realización inmutable, cambios/inyección/omisiones, rechazo de pregunta/operación, contexto obsoleto durante escritor, FAQ como dato y referencias ordinales coherentes; código HTTP de ambos proveedores con transporte simulado y tokens presentes/ausentes/inválidos, un router frente a dos POST de Gemini, scopes concurrentes y observador que lanza sin romper el resultado. Transport existente ampliado con secuencia/demora sintéticas, sin fixture/infraestructura nuevos. Pruebas comerciales anteriores ya usan cinco sectores/catálogos y permisos distintos; las nuevas comparaciones cambian nombres/datos de workspace manteniendo el mismo runtime.

Compilación requerida por bloque: `dotnet build backend/backend.slnx --no-restore`; `a-build.log`, `b-build.log`, `c-build.log` y cierre `c-final-v2-build.log`: correctos, 0 errores, 0 advertencias incrementales. Compilaciones de tests observaron tres avisos anteriores: CS8981 en las dos partes de 20260929030327_refactor; CS8602 FirestoreCatalogRepository:228. Sin restore, cambios de dependencias ni intentos de corregirlos.

## Relación exacta de archivos de esta entrega

### Backend — modificados

```text
backend/NexFlow.Application/Features/Knowledge/BusinessReadTools.cs
backend/NexFlow.Application/Features/Automation/Dialogue/Contracts/ResponsePlan.cs
backend/NexFlow.Application/Features/Automation/Dialogue/Composition/ResponseComposer.cs
backend/NexFlow.Application/Features/Automation/Dialogue/Composition/OfferingResponseFormatter.cs
backend/NexFlow.Application/Features/Automation/Dialogue/Understanding/TurnUnderstandingPipeline.cs
backend/NexFlow.Application/Features/Automation/Dialogue/Understanding/AiEvidenceValidator.cs
backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/Flows/ConversationFlows.cs
backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/AiResponseOrchestrator.cs
backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/ConversationDiagnostics.cs
backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/ConversationRecoveryService.cs
backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/ContextRecoveryService.cs
backend/NexFlow.Application/Features/AI/Interpretation/AiInterpreter.cs
backend/NexFlow.Application/Features/AI/Router/AiRouter.cs
backend/NexFlow.Infrastructure/Engines/AI/GroqAiProvider.cs
backend/NexFlow.Infrastructure/Engines/AI/GeminiAiProvider.cs
backend/NexFlow.Tests/Concierge/RuntimeReadToolsTests.cs
backend/NexFlow.Tests/Concierge/ResponseComposerTests.cs
backend/NexFlow.Tests/AiResilienceTests.cs
```

### Backend — nuevos

```text
backend/NexFlow.Application/Features/Automation/Dialogue/Contracts/VerifiedResponseLayout.cs
backend/NexFlow.Application/Features/AI/Router/AiTelemetry.cs
backend/NexFlow.Tests/Concierge/RuntimeF3ContractV2Tests.cs
backend/NexFlow.Tests/Concierge/RuntimeRealizationTests.cs
backend/NexFlow.Tests/Concierge/RuntimeEfficiencyTests.cs
```

Documentación nueva: 04_1_COMPATIBILIDAD_F3.md, 05_COMPOSICION_SEGURA.md y este informe. Los originales 00–04 y los corpus/evaluadores históricos no se modifican. Frontend: ninguno. Revisión contra capturas propias previas, sin Git; no se afirma ausencia de cambios ajenos anteriores. Sin cambios en la política primaria/fallback del router, motores, pagos, empleados, esquemas, secretos, infraestructura o Outbox.

## Controles operativos propuestos, no desplegados

Mantener EnableAi=false hasta revisión de su utilidad/latencia real; es el apagado del compositor existente, no de la interpretación necesaria. El runtime sigue con fallback determinista. Proponer luego un opt-in/kill switch por workspace aprobado sobre configuración existente, sin agentes/BD nuevos; hoy no se afirma que WorkspaceStyles sea una autorización de activación por tenant. Un rollback de código/flag no revierte reservas/pedidos ya confirmados. Un shadow futuro debe ser de lectura sin entrega, notificación ni commit comercial; no se activa aquí. Panel propuesto: HTTP por proveedor/tarea, rate limit, usage disponible/desconocido, presupuesto, fallbacks, fase, contexto, aclaraciones y comparación con efectos durables únicos. No inventar límite comercial nuevo ni eliminar FIFO/revalidaciones para subir throughput.

## Riesgos y gates

- **P0/P1 abiertos:** ninguno tras las correcciones y verificaciones locales descritas; no equivale a ausencia universal de defectos.
- **P1 corregido durante revisión:** se reprodujo la divergencia entre orden mostrado y OfferingReference del prototipo de realización. Schema v2 sólo agrupa párrafos conservando la secuencia; «el primero» sigue apuntando a la oferta mostrada y su precio. No se agregó otro estado o selector. Ver evidencia roja y posterior en 05.
- **P2:** paráfrasis informativa libre no verificable permanece deshabilitada; el valor UX de la agrupación tampoco se ha medido con personas. No declarar redacción natural plena.
- **P2:** suite histórica original conserva doce incompatibilidades conocidas; se evalúa v2 por separado sin ocultarlas. Hashes de ambos corpus iguales a los registrados en 04_1 al cierre.
- **P2:** colecciones sin paginación en origen, cargas/contención/límites globales, volumen/costo de logs y agregación/retención real de métricas, tarifas y resultados de proveedores/WhatsApp siguen pendientes de evidencia autorizada.
- **P2:** costos/SLO y calidad comercial requieren muestra independiente, rúbrica humana y proveedores reales autorizados; ninguna evaluación sintética los certifica.

**G5: GO local limitado al verificador y organización segura. G6: GO local para resiliencia, instrumentación y optimización demostradas. NO-GO comercial/de producción por evidencia real faltante.** No se afirma el gate maestro de costos/calidad sobre conversaciones reales: esta autorización sólo cubre el gate local. Se detiene después de F6 para auditoría conjunta; no F7, commit, push, despliegue, Oracle o pruebas pagadas.
