# Concierge 3.0 — Sprint 5.1: comprensión y disponibilidad

## Alcance y evidencia

Parche local del backend. Base desplegada declarada por el propietario: `8104f05ef765dce4dc2cd51059fb51abf9d801dc`; no se verificó mediante Git, por prohibición expresa del encargo. Se conservaron los archivos existentes tomando una copia de su contenido en memoria antes de editar. No se ejecutaron Git, proveedores externos, WhatsApp, Oracle, migraciones ni despliegues. Los resultados siguientes corresponden a fixtures sintéticas existentes, no al servidor.

## Causas confirmadas y correcciones

1. `EntityMentionMatcher.Resolve`: las coincidencias dependían de límites de palabras. Ahora contrasta también fragmentos contiguos compactados con nombres y alias autorizados; conserva todas las colisiones como ambigüedad. No selecciona IDs inventados por el modelo.
2. `NameSlotResolver.Resolve`: una respuesta nominal podía incluir una presentación imperfecta, y los datos separados por delimitadores dependían de su orden. Se separan candidatos nominales con el mismo resolutor y se deriva incertidumbre al intérprete. `AiEvidenceValidator.Validate` exige un fragmento literal; no corrige nombres propios ni completa apellidos.
3. `DialoguePolicy.Reads` omitía los candidatos comerciales al recibir una cadena con forma de nombre. Una reproducción guardó el nombre de un servicio como persona. En el paso de nombre se cargan candidatos autorizados y se excluyen sedes, servicios y alias, también después de una extracción semántica. La validación anterior al consentimiento impide confirmar presentaciones inválidas heredadas.
4. `TurnUnderstandingPipeline.PrepareCoreAsync`: resultados locales parciales podían impedir la comprensión semántica; las preguntas horarias llegaban a captura de datos o a respuestas generales. Se distinguen propuestas de consulta sin mutación comercial y propuestas de selección. Se reutilizan `BookingDraftProposal`, `ResponsePlan`, CAS y el estado informativo existente.
5. `BookingFlow.AnswerAvailabilityAsync`: responde primero por la hora solicitada; distingue día cerrado, horario ausente, fuera de jornada, indisponibilidad y fallo de consulta. Las alternativas proceden del motor. No sustituye nombre, sede, servicio, fecha ni hora del borrador por los de una pregunta. Un «sí» a esa respuesta no confirma una reserva anterior; seleccionar la hora requiere después el consentimiento habitual. Cancelación explícita y toma humana conservan prioridad.
6. `ReservationEngine.GetAvailabilityCoreAsync`: se confundía una cuadrícula de sugerencias con el conjunto de inicios válidos. Decisión explícita del propietario: **conservar inicios libres y verificar la hora puntual**. `GetAvailabilityAtAsync` reutiliza las validaciones del motor y comprueba el instante solicitado, duración real, jornada y ocupación; la confirmación conversacional utiliza esa consulta. Crear sigue revalidando conflictos dentro de las protecciones transaccionales existentes.

La disponibilidad consultada es una instantánea, no una retención de recursos. No se modificaron Serializable, coordinación de recursos, fencing, idempotencia, Outbox ni las operaciones sobre reservas existentes.

## Comprensión, expresión y fronteras

| Capa | Responsabilidad y límites |
|---|---|
| Resolución local | Nombres literales inequívocos, entidades autorizadas, fechas, relojes y preferencias. Palabras unidas se contrastan sin distancia difusa local. Una franja nunca genera una hora inventada. |
| Intérprete existente | Una interpretación estructurada ante incertidumbre relevante, no una llamada por campo. Recibe reloj del negocio, fecha del borrador y hasta 12 nombres de sede y 12 de servicio, cada uno limitado a 120 caracteres. No recibe historial completo, precios, reservas privadas ni nombre persistido del cliente como contexto adicional. |
| Evidencia semántica | El fragmento debe existir en el mensaje original. Una referencia a entidad admite compactación y una diferencia ortográfica acotada, conservando ambigüedades. La expansión opcional `CanonicalText` de una fecha exige equivalencia superficial verificable y resolución determinista con el reloj del negocio. Una extracción no respaldada se aclara. No cubre cualquier falta ortográfica. |
| Composición | Conserva el cuerpo factual literalmente. La opción existente `EnableAi` permite únicamente una apertura no factual, validada con un vocabulario pequeño de enlace/cortesía. No permite al modelo reescribir disponibilidad, alternativas, consentimiento ni resultados. No constituye redacción libre integral. |
| Seguridad | Licencias y módulos se comprueban de nuevo; el orquestador mantiene las barreras de modo humano y versión antes de entregar. Confirmación, cancelación, denegaciones, estados de operaciones, errores y fallback permanecen deterministas. |

El intérprete conserva su presupuesto, router Groq/Gemini, JSON limitado y deadline de 20 segundos. La composición opcional usa el mismo presupuesto con identificador estable `response-compose:v1:<SourceMessageId>`, entrada mínima de dos hechos, salida de hasta 512 caracteres y deadline de proveedor entre 100 y 2000 ms (800 ms por defecto). Fallos, cuota agotada o salida no validable conservan el cuerpo seguro; la cancelación del llamador se propaga. No se cambió configuración ni se activó esta opción en un servidor.

## Consolidación y coste

- **Una composición duplicada eliminada del recorrido principal:** `AvailabilityOptionsAsync` entrega opciones estructuradas y `ConversationRecoveryService` presenta una sola lista. La continuación textual sin plan conserva su respaldo existente.
- **Una lectura repetida del motor eliminada** al decorar esa lista; la prueba verifica exactamente una llamada para preparar los horarios. Se reutiliza también la zona del plan en ese recorrido, evitando su segunda lectura para decorar.
- **Cero familias completas de reglas lingüísticas eliminadas.** Se corrigieron precedencias y se ampliaron dos expresiones regulares existentes para estructura nominal y gramática horaria; no se añadieron métodos regex ni un diccionario de errores por frase. No se presenta un aumento de código como reducción de reglas.
- La validación del nombre en su paso puede necesitar leer sedes y servicios autorizados; es un coste de comprobación deliberado frente al defecto reproducido. Los candidatos adicionales sólo se recargan cuando una interpretación los necesita y el intento local no los había leído.
- En las pruebas, preguntas horarias claras y formatos estructurados usan **cero interpretaciones IA**; una presentación imperfecta o la respuesta compuesta con abreviatura usan **una interpretación** mediante la cola ya existente. La composición opcional añade como máximo una solicitud al router por invocación elegible y respeta la deduplicación presupuestaria existente.
- **Cero llamadas reales a Gemini o Groq.** No se midieron tokens, precio, latencia SaaS ni ahorro monetario. Las invocaciones simuladas no certifican calidad lingüística de un proveedor.

## Archivos de esta entrega

Todos los archivos de código siguientes están bajo `backend/NexFlow.Application/`:

- `Features/AI/Interpretation/AiInterpreter.cs`.
- `Features/Automation/Dialogue/Contracts/PreparedDialogueTurn.cs`, `TurnUnderstanding.cs`.
- `Features/Automation/Dialogue/Understanding/AiEvidenceValidator.cs`, `EntityMentionResolvers.cs`, `NameSlotResolver.cs`, `TimePreferenceResolver.cs`, `TurnUnderstandingPipeline.cs`, `WorkspaceSlotResolver.cs`.
- `Features/Automation/Dialogue/Policy/DialoguePolicy.cs`.
- `Features/Automation/Dialogue/Composition/ResponseComposer.cs`.
- `Features/Automation/ProcessMessage/Services/AiResponseOrchestrator.cs`, `ConversationRecoveryService.cs`, `Flows/ConversationFlows.cs`.
- `Features/Reservations/IReservationEngine.cs`, `Engines/Reservation/ReservationEngine.cs`.

Pruebas: `backend/NexFlow.Tests/AssistantConversationTests.cs` (adapta el mock existente al contrato puntual), `ReservationsPeruTimeTests.cs`, `Concierge/ResponseComposerTests.cs` y nuevo `Concierge/Sprint51ConversationTests.cs`. Se reutilizaron fixtures, mocks, reloj y cola de interpretaciones existentes; no se crearon simuladores, bases ni mocks nuevos. Ningún corpus JSON ni expectativa anterior fue modificado. Frontend sin cambios.

Este informe es el único documento añadido. El diff de la entrega se compara contra el contenido previo conservado en memoria, sin ejecutar Git.

## Reproducciones y validación

- Reproducción inicial: `sprint51-reproduction.trx`, **7 fallidas de 7**, sobre palabras unidas, presentación imperfecta y preguntas horarias. Corregidas.
- Reproducción adicional: `sprint51-name-label-reproduction.trx`, **1 fallida y 1 aprobada**, al recibir etiquetas comerciales como nombre. Corregida.
- La regresión detectó dos llamadas innecesarias al intérprete al corregir una fecha recuperada y, después, una al segmentar una respuesta con cortesía. Se corrigieron las precedencias conservando las expectativas y los corpus.
- Se comprueban mensajes con campos en distinto orden, abreviatura con evidencia literal, ambigüedad de sedes, nombre no inventado, consulta sin mutación/consentimiento, selección y confirmación posterior, cancelación, fallo del motor, toma humana, una sola lista y ausencia de lectura duplicada.
- El motor real con los repositorios sintéticos existentes verifica duración de 45 minutos, inicio libre a las 16:00 ausente de la cuadrícula, equivalencia UTC/local, ocupación y límites de jornada; la creación persiste el mismo intervalo y su evento.
- El compositor verifica texto factual intacto, exclusión de contexto privado, salidas adversariales, timeout, fallo, acceso y presupuesto. Las regresiones existentes incluyen aislamiento, cambios de sede/fecha, replays, reservas/pedidos, handoff, entrega incierta y recuperación durable.

Comandos ejecutados (PowerShell, desde la raíz):

```powershell
dotnet build backend/backend.slnx --no-restore --disable-build-servers -m:1 --verbosity quiet
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --disable-build-servers -m:1 --filter '(FullyQualifiedName~NexFlow.Tests.Concierge|FullyQualifiedName~AssistantConversationTests|FullyQualifiedName~ZeroTokenKnowledgeAuthorizationTests|FullyQualifiedName~AssistantDurabilityTests|FullyQualifiedName~AssistantRedisRecoveryTests|FullyQualifiedName~AssistantRc1|FullyQualifiedName~AiResilienceTests|FullyQualifiedName~ReservationsPeruTimeTests)&Category!=ProviderIntegration' --logger 'trx;LogFileName=sprint51-complete.trx' --results-directory backend/NexFlow.Tests/TestResults/concierge-sprint51 --verbosity quiet
```

Resultado final: **949 ejecutadas, 949 aprobadas, cero fallidas y cero omitidas**, TRX `backend/NexFlow.Tests/TestResults/concierge-sprint51/sprint51-complete.trx`. Incluye 60 casos adicionales de esta entrega, reutilizando la infraestructura existente. La compilación terminó con **cero errores y cero advertencias en ejecución incremental**. Al recompilar infraestructura durante las pruebas aparecieron tres advertencias preexistentes: dos CS8981 de la migración `refactor` y una CS8602 de `FirestoreCatalogRepository.cs:228`; no se modificaron esos archivos.

## Límites y gate

Pendientes: validación supervisada de variaciones inéditas con Gemini/Groq autorizados; calidad humana de las respuestas y conveniencia/coste de la apertura opcional; conversación WhatsApp real en workspace aislado; confirmar comportamiento con datos y proveedores desplegados. No se certificaron servicios reales ni concurrencia PostgreSQL/Firestore/Redis mediante nuevas integraciones en este encargo.

Las horas fuera de jornada y las franjas sin huecos no se convierten en cierres del negocio. La composición integral del texto por un modelo permanece restringida deliberadamente: sólo se publica el cuerpo autorizado, con apertura opcional acotada. Debe revisarse esta limitación durante la auditoría de calidad; no se declara una certificación comercial ni ciega.

Estado: parche local para auditoría; sin despliegue y sin avance de fase.

## Auditoría 5.1.1

Fecha: 22/10/2026. Referencia publicada declarada: `15814b553ee8c1e3fa0a560af963c48f1ac4a73e`. Se trabajó sobre el código local; no se ejecutó Git para verificar esa identidad. Esta sección complementa la evidencia histórica del Sprint 5.1 anterior.

### A. Validación final de nombres

**Defecto reproducido.** Cuatro casos llegaron indebidamente a `CreateReservationAsync`: nombre heredado idéntico al de otra sede (`San Jeronimo`, sin prefijo «Sede»), otro servicio, alias de otro servicio y alias de producto licenciado. El caso inicial con prefijo «Sede» ya era rechazado por la validación de estructura; no se contabiliza como reproducción del defecto comercial. La segunda reproducción aislada falló en los cuatro casos pertinentes.

**Causa.** `BookingFlow.ProcessTurnAsync` sólo contrastaba el nombre heredado con el servicio seleccionado; no podía confiarse en las validaciones de extracción de turnos anteriores. Un contexto persistido puede precederlas.

**Parche.** En `backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/Flows/ConversationFlows.cs`, `IsCustomerNameValidAsync` conserva la validación estructural y compara etiquetas completas normalizadas (mayúsculas, acentos y separadores) con las sedes actuales del workspace, servicios activos y sus alias. Incluye productos activos y alias únicamente cuando está autorizado `CATALOG`. Reutiliza `VerifiedOffering.Capture` para leer alias, incluidos los representados como JSON. No utiliza distancia de edición, palabras sueltas, eliminación de palabras funcionales ni semejanza parcial para invalidar un nombre.

El control se aplica antes del resumen y de la llamada de creación. Una coincidencia comercial devuelve al paso de nombre sin alterar sede, servicio, fecha ni hora. Si no se puede leer la fuente autorizada, el error se propaga al procesamiento durable existente y no se crea la reserva; no hay autorización por defecto ni consumo adicional de IA.

**Consultas.** El borrador válido consulta una vez los servicios del workspace y, si corresponde, una vez los productos para comprobar otras etiquetas/alias. Son lecturas adicionales necesarias para esta cobertura final; `OfferingService` lee el catálogo por tipo. Se reutiliza la lectura de sedes de este mismo turno para el resumen, eliminando su segunda consulta. No hay caché nueva, segundo estado ni cambios de contrato o persistencia.

Regresiones: otro servicio/sede, alias en lista y JSON, alias de producto, nombres humanos que comparten palabras o contienen un alias más un apellido, productos no licenciados, aislamiento entre fixtures de dos workspaces, fuente indisponible y resumen correcto con todos los slots intactos. Se verifica explícitamente que los casos inválidos no invocan creación. Un homónimo completo de una etiqueta comercial exige aclaración; no se infiere identidad personal real a partir de una coincidencia ni se inventa acreditación.

### B. Alternativas próximas

**Defecto reproducido.** Una consulta puntual de mañana o tarde podía recibir las primeras horas del motor en lugar de las más próximas. Una consulta de mañana tampoco priorizaba el centro de la franja. Los casos con preferencia de tarde y después de una hora ya filtraban la franja, y se conservaron como regresión.

**Parche.** `BookingFlow.AnswerAvailabilityAsync` conserva el filtrado de disponibilidad y preferencias. Entre los slots reales resultantes ordena por distancia en hora civil del negocio a:

- La hora exacta consultada, cuando es inequívoca.
- El centro de una franja; los límites abiertos se acotan al día únicamente para ordenar, sin crear un inicio disponible.
- El límite solicitado en preferencias «después» o «antes», respetando previamente el filtro direccional existente.

Los empates se resuelven por instante UTC ascendente, se eliminan instantes duplicados y se muestran como máximo tres alternativas. Sin ancla se conserva un orden cronológico estable. Una preferencia ambigua sin hora exacta requiere aclaración antes de listar alternativas: no se supone AM/PM. No se cambia ningún `StartTime`, `EndTime`, duración, jornada o regla del motor; tampoco las protecciones transaccionales. Las preguntas siguen dejando el borrador intacto.

### D. Evidencia ejecutada

Se ampliaron los tests existentes de `Concierge/Sprint51ConversationTests.cs`; no hay nuevos mocks, simuladores ni infraestructura. `AssistantRc11Tests.cs` adapta exclusivamente un `Setup` del mock estricto existente para devolver su mismo servicio autorizado al nuevo control final. Las seis primeras regresiones RC-1.1 fallaron por ese contrato de fixture incompleto, no por un cambio de zona horaria; no se alteraron sus expectativas, el motor ni los corpus.

Artefactos bajo `backend/NexFlow.Tests/TestResults/concierge-sprint511/`:

| Ejecución | Ejecutadas | Aprobadas | Fallidas | Omitidas |
|---|---:|---:|---:|---:|
| `audit511-reproduction.trx` | 12 | 5 | 7 | 0 |
| `audit511-legacy-reproduction.trx` | 4 | 0 | 4 | 0 |
| `audit511-directed.trx` | 12 | 12 | 0 | 0 |
| `audit511-regression.trx` (fixture RC-1.1 anterior) | 965 | 959 | 6 | 0 |
| `audit511-complete.trx` | 965 | 965 | 0 | 0 |
| `audit511-final.trx` | **966** | **966** | **0** | **0** |

La última ejecución incluye 17 casos adicionales de 5.1.1 y conserva los 949 anteriores. Los comandos conservan el filtro de suites afectadas del Sprint 5.1 anterior y excluyen `Category=ProviderIntegration`; cambian únicamente el logger a `trx;LogFileName=audit511-final.trx` y el directorio de resultados a `backend/NexFlow.Tests/TestResults/concierge-sprint511`. No se ejecutaron integraciones que creen o migren bases, ni Gemini/Groq reales.

Build ejecutado: `dotnet build backend/backend.slnx --no-restore --disable-build-servers -m:1 --verbosity quiet`: **0 errores, 0 advertencias en la compilación incremental**. Durante la recompilación de pruebas aparecieron las tres advertencias históricas ya descritas en este informe; no se modificaron esos archivos.

Archivos de 5.1.1: `ConversationFlows.cs`, `Concierge/Sprint51ConversationTests.cs`, `AssistantRc11Tests.cs` y este informe. Frontend, `ResponseComposer.cs`, contratos, motor, esquema, configuración e infraestructura sin cambios en esta corrección.

### C. Propuesta separada: redacción informativa con IA verificable — NO IMPLEMENTADA

**Diagnóstico.** `ResponseComposer.ComposeAsync` sólo permite aperturas de un vocabulario acotado y preserva el cuerpo. Esto garantiza esa frontera, pero no satisface una reformulación informativa contextual completa. `ResponsePlan` tiene colecciones inmutables, aunque sus hechos siguen siendo pares de clave/valor textual; comprobar que una respuesta contiene las mismas cifras o citas no demuestra que conserve sus relaciones, negaciones, unidades o condiciones. Otro LLM como juez tampoco sería validación independiente.

**Contrato propuesto dentro del compositor existente:** proyección efímera y tipada de los hechos de `ResponsePlan`, sin otra historia ni persistencia. Cada hecho tiene un identificador opaco del turno, tipo, valor canónico, formato local autorizado y obligatoriedad. Precios: minor units y moneda; fechas/horas: instante, zona y fecha civil; cantidades: valor y unidad; disponibilidad: `Available`, `Unavailable` o `Unknown`, vinculada a servicio/sede/intervalo. Alternativas: referencias a `Choices` verificadas. El texto obligatorio de seguridad y la siguiente pregunta permanecen del backend.

La respuesta del modelo sería un **árbol estructurado de realización**, sin un campo de prosa publicable:

```json
{
  "Version": 1,
  "PlanFingerprint": "huella-del-plan-del-turno",
  "Clauses": [
    { "Kind": "Availability", "FactRefs": ["F1", "F2"], "Realization": "Direct" },
    { "Kind": "Alternatives", "ChoiceRefs": ["C1", "C2"], "Realization": "Compact" },
    { "Kind": "NextQuestion", "Ref": "Q1" }
  ]
}
```

El fingerprint vincula la propuesta al snapshot; no es una credencial ni sustituye WorkspaceId, SourceMessageId, CAS o autorización. `Realization` sería una enumeración de construcciones gramaticales revisadas por humanos, nunca texto libre ni prompts por rubro. El modelo puede escoger organización y forma entre realizaciones equivalentes; no puede aportar valores, cambiar polaridad, inventar un verbo comercial ni omitir aclaraciones obligatorias. El renderizador incorpora los valores originales y deriva la polaridad del hecho tipado, no de una palabra del modelo. Esta restricción tiene un coste: ofrece variación gramatical controlada, no paráfrasis libre ilimitada; requiere validar que esa variación aporte utilidad comercial antes de implementarla.

**Validador determinista independiente propuesto:** JSON estricto con tamaño/profundidad/número de cláusulas acotados; rechazo de propiedades duplicadas/desconocidas y texto bruto; versión y huella iguales al plan; referencias existentes y tipos compatibles por nodo; cobertura de hechos y calificadores obligatorios; relaciones de disponibilidad correctas; alternativas únicamente autorizadas y con el orden del backend; una sola siguiente pregunta; ninguna cláusula transaccional o de consentimiento. Cualquier incompatibilidad devuelve `FallbackText` íntegro. Sólo planes informativos elegibles; consentimiento, éxito comercial y acciones quedan fuera de la gramática. Las comprobaciones de licencia, estado y toma humana antes de entregar permanecen en `AiResponseOrchestrator`.

**Presupuesto, privacidad e injection:** reutilizar la adquisición durable existente, una solicitud opcional por composición y sin reintentos no acotados; deadline total y salida limitados, con cancelación del llamador propagada y fallback al agotar presupuesto/plazo. Enviar sólo hechos informativos necesarios, IDs opacos por turno y datos como JSON no ejecutable; ningún historial, reserva privada, secreto o herramienta de operación. Etiquetas del negocio y salida del modelo se tratan como datos no confiables. Ninguna cadena propuesta por el modelo se publica sin pasar por la gramática y el renderizador.

**Regresiones necesarias antes de autorizar código de C:** mutar por separado importes/monedas, cantidades/unidades, fechas/zonas y relaciones servicio/sede/hora; intentar invertir disponibilidad, afirmar éxito o pedir consentimiento; quitar calificadores, duplicar preguntas o referenciar hechos/opciones de otro plan/tenant; propiedades JSON extra/duplicadas, tamaños extremos, texto o instrucciones inyectadas en etiquetas; cuotas, timeout y cancelación; CAS/handoff/revocación durante composición; equivalencia factual para cada realización gramatical permitida. Incluir casos de inventario/pago no soportados y comprobar que no puedan afirmarse. Estas pruebas son una propuesta, **no se ejecutaron ni certifican este contrato**.

No se implementa C en este parche: falta formalizar los hechos tipados, la gramática revisada y demostrar su validador. Un filtro de cifras más un prompt no ofrece la garantía requerida. El compositor actual no se amplió ni se habilitó en producción.

### Riesgos pendientes y cierre

Validación semántica y naturalidad real de Gemini/Groq, evaluación humana y WhatsApp autorizado siguen pendientes. Las nuevas lecturas finales pueden afectar la latencia en catálogos grandes; falta medición real, sin atribuir ahorro monetario o de tokens a estas pruebas. Disponibilidad es una instantánea; la creación conserva la revalidación de conflictos del motor. El rechazo de homónimos comerciales exactos necesita aclaración humana si corresponde.

A/B: regresión local aprobada y diff propio revisado. C: análisis/contrato propuesto, implementación pendiente de demostrar la validación independiente descrita. Sin bloqueos de compilación o pruebas locales. No se declara validación real de proveedores ni certificación comercial. No se ejecutaron Git, migraciones, Oracle ni despliegues, ni se avanzó de fase.
