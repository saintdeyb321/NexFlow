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
