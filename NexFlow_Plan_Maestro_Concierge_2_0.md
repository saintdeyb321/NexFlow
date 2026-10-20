# NexFlow Concierge 2.0 — Plan maestro de refactorización conversacional

**Fecha:** 20/10/2026  
**Base técnica:** ZIP `NexFlow-main(5).zip` (inspeccionado directamente: 772 entradas, 594 archivos).  
**Escenario real:** conversación de WhatsApp del 20/10/2026, de «Hola» a transferencia humana después de no reconocer nombre, sede ni fecha.  
**Alcance:** backend de interpretación, diálogo, conocimiento y respuestas; **no** reescritura de motores transaccionales ni infraestructura de producción.

## 1. Visión de producto y criterio arquitectónico

**Propuesta de valor:** atender como una recepcionista virtual profesional, disponible y confiable. El cliente puede usar lenguaje cotidiano, completar varios datos a la vez, cambiar de opinión, preguntar otra cosa en medio de una reserva y recibir respuestas claras basadas en información real del negocio. Debe saber que interactúa con un asistente virtual cuando sea relevante; no suplantar una persona.

No existe un modelo infalible ni el número de pruebas unitarias garantiza una conversación útil. El éxito se medirá por *tareas completas, corrección factual, satisfacción, transferencia apropiada, coste y latencia*, además de integridad transaccional.

**Principio:** la IA interpreta y opcionalmente redacta; las políticas y motores .NET autorizan y ejecutan. **La determinación de acciones permanece rígida, la comprensión del lenguaje es flexible.** No usar un LLM autónomo con escritura libre en BD/Firestore/Evolution.

## 2. Evidencias verificadas contra el código del ZIP

| ID | Archivo/área | Evidencia | Impacto |
|---|---|---|---|
| E01 | `.../Business/Locations/LocationResolverService.cs`, 20–34 | `ToLowerInvariant()` + igualdad/`Contains`, sin eliminar tildes ni frases como «en la sede ...» | «En la sede concepción» no garantiza reconocer `Sede Concepcion` |
| E02 | `.../ProcessMessage/Services/Flows/ConversationFlows.cs`, 81–90 | `UpdateStep()` ordena **nombre → sede → servicio → fecha → hora** | Interroga por el nombre antes de mostrar servicios/disponibilidad |
| E03 | `.../Features/AI/Interpretation/AiInterpreter.cs`, 55–117 | Prompt recibe `CurrentGoal`, `CurrentStep`, módulo y fecha; solicita JSON, pero no entrega catálogo/opciones reales ni slots completos | Ambigüedad y dependencia del proveedor para respuestas simples |
| E04 | `.../AiResponseOrchestrator.cs`, 69–109 | Interpretación LLM cuando no hay recovery; `General`, fallo, aclaración => `ClarifyAsync` | Menú repetido ante preguntas humanas y fallos de proveedor |
| E05 | `.../ConversationRecoveryService.cs`, 164–169 | Incrementa `ClarificationAttempts` y al máximo devuelve `Support` | Caídas externas pueden terminar en handoff sin ambigüedad humana real |
| E06 | `.../ConversationRecoveryService.cs`, 178–211 | Añade opciones numeradas y «Escribe su número» incluso en casos de recuperación | Sensación de IVR, no de atención personalizada |
| E07 | `.../ConversationFlows.cs`, 193–243 y 351–388 | Fecha en el draft depende de `AiInterpretation.Date`, en `yyyy-MM-dd`; disponibilidad real solo tras reunir slots | «Pasado mañana» falla si el LLM no extrae bien; respuesta no usa contexto |
| E08 | `.../AI/Router/AiRouter.cs`, 6–49 | Define `IntentExtraction` y `ComplexChat`; el recorrido principal encontrado llama solo extracción | No existe compositor natural conectado al cliente |
| E09 | `.../Engines/AI/GroqAiProvider.cs`, 19–27, 86–90 | Timeout por defecto 5 s; método de chat complejo no implementado | Uso conversacional requiere un adaptador nuevo o Gemini acotado |
| E10 | `.../ConversationFlows.cs`, 496–547 | Consulta amplia de productos prioriza PDF vigente y una leyenda fija, sin conversar sobre productos | «¿Qué productos tienen?» puede recibir «Te comparto...» en vez de datos concretos |
| E11 | `.../Flows/OrderFlow.cs`, 48–150 | Pedidos utilizan comandos/frases rígidas y exactitud del nombre del producto | Con lenguaje natural faltan composición de pedidos y correcciones fluidas |
| E12 | `.../AiInterpreter.cs`, 15–36, 88–110 y `BookingFlow.ProcessTurnAsync` | **Ya existe extracción de múltiples campos en `AiInterpretation` y aplicación de slots** | No rehacer este mecanismo; robustecerlo y hacerlo atómico/independiente del orden |

**No demostrado:** qué proveedor, modelo, HTTP code o excepción causó cada «No pude interpretar» observado. Solo el código de fallback es evidente; hay que añadir y consultar telemetría sin secretos ni PII. Tampoco el ZIP prueba cómo resolvió cada JSON el LLM de producción.

## 3. Arquitectura objetivo — compatible con Clean Architecture actual

```text
Evolution API → webhook existente → InboundMessageRepository / FIFO / worker
        → Guard + detección modo Human + licencia actual del workspace
        → DialogueContextLoader
           (borrador validado, etapa, última pregunta, opciones autorizadas, reloj local)
        → TurnUnderstandingPipeline
            (1) safety/interruptions/commands explícitos
            (2) deterministic slot/entity resolvers basados en etapa y catálogo real
            (3) LLM NLU estructurado SOLO si queda ambigüedad/idioma complejo
            (4) validation + entity resolution + merge atómico contra estado actual
        → DialoguePolicy
           (answer/info | collect next slot | disambiguate | confirm | change | handoff)
        → ActionExecutor
           (BookingFlow/ReservationEngine | OrderFlow | KnowledgeService | SupportFlow)
        → ResponsePlan tipado + hechos/resultado/nextQuestion autorizados
        → ResponseComposer (español natural determinista + LLM opcional acotado)
        → OutboundMessageService durable → Evolution API
```

### Contratos sugeridos (de diseño, no nombres obligatorios)

```csharp
record DialogueTurnContext(
    Guid WorkspaceId, string SourceMessageId, string CurrentGoal,
    string? CurrentStep, IReadOnlyDictionary<string, ValidatedSlot> Slots,
    IReadOnlyList<AllowedOption> AuthorizedChoices, DateTime LocalBusinessNow,
    IReadOnlySet<string> EnabledModules, string? LastQuestion,
    long StateVersion // adaptar al tipo real; no migrar por esta representación
);

record TurnUnderstanding(
    DialogueAct Act, IReadOnlyList<SlotCandidate> Candidates,
    ResolutionStatus Status, string? AmbiguityKind, EvidenceSource Source
);

record SlotCandidate(string Name, string RawText, object? NormalizedValue,
                     EvidenceSource Source, string? MatchedBusinessEntityId);

record ResponsePlan(string Goal, IReadOnlyList<VerifiedFact> Facts,
                    string? NextQuestion, IReadOnlyList<Choice> Choices,
                    BusinessOperationResult? Result, bool AllowCreativeParaphrase);
```

**Reglas:** `WorkspaceId` nunca procede del LLM; entidades `LocationId`, `ServiceId`, `ProductId` salen de repositorios autorizados; `EvidenceSource` = literal validado / menú vigente / resolutor local / LLM validado; `ResolutionStatus` = determinado / ambiguo / inválido / insuficiente / proveedor no disponible. Nunca mutar el estado del draft con candidatos no validados. Mantener `StateVersion` tal como existe; el contrato anterior es conceptual, no una migración.

### Políticas específicas de interpretación

- **No es «todo regex»**: reglas locales para fechas, números, respuestas directas a una pregunta y nombres de entidades del catálogo; LLM para intención compuesta, corrección semántica y referencias complejas. Cada ruta entrega la **misma salida estructurada validada**.
- **No es «todo LLM»**: precio, disponibilidad, identidad de sedes, permisos y acciones provienen de la fuente de verdad del workspace.
- **Multi-slot en cualquier orden**: extraer todos los datos dados aunque no correspondan al siguiente paso. Conservar candidatos aún no aplicables (p. ej. servicio mencionado antes de elegir sede), validarlos cuando exista contexto suficiente, y confirmar si hay empate entre entidades.
- **Interrupciones**: preguntas de precio/ubicación/horarios en mitad de una reserva responden sin destruir el draft; una corrección explícita actualiza únicamente el slot afectado e invalida dependencias (cambio de sede puede invalidar servicio, disponibilidad y hora).
- **Seguridad**: un texto agresivo o confuso no es por sí solo solicitud de atención humana; tratar con respeto, mantener estado, ofrecer handoff cuando se pide o tras fallos semánticos persistentes.

## 4. Resolutores locales y NLU avanzado

**Nombre:** solo interpretar nombre cuando la pregunta vigente lo solicita o el usuario se presenta inequívocamente; aceptar nombre + apellido de dos o más términos, evitar usar arbitrariamente el `pushName`; validar longitud, caracteres y referencias no nominales. No exigir tres apellidos ni inferir DNI. Si duda, una aclaración breve.

**Sede:** Unicode NFD + diacríticos + puntuación, tokens de función («en», «la», «sede», «de»), alias configurables y coincidencia contra sedes habilitadas del workspace. Priorizar ID de opción vigente y alias exacto; si hay dos coincidencias, mostrar ambas sin elegir. Normalización no convierte nombres distintos en equivalentes sin revisión.

**Servicio/producto:** comparador reutilizable de términos/alias sobre catálogo activo del workspace y sede si aplica; no resolver por nombre aproximado con baja confianza y no listar categorías no licenciadas. Permitir nombres parciales solo cuando hay un único resultado, sin elegir sobre dos similares.

**Fechas:** `BusinessDateResolver` con `TimeProvider`/`IClock`, `TimeZoneInfo` de negocio, español Perú, «hoy», «mañana», «pasado mañana», «22 de octubre», «22/10», «22 de oct», «este jueves», «el próximo viernes»; meses/años límite, fechas vencidas, cambio de año, día de semana, zona y DST de la zona configurada. Si «el viernes» es ambiguo respecto a esta/próxima semana, aclarar. Guardar `DateOnly`; nunca inventar hora.

**Hora/franja:** reconocer «a las 4», «16:00», «por la mañana/tarde», «después de las 5» como horario exacto o **preferencia**. Una franja no se transforma arbitrariamente en 16:00; filtrar slots que entregue ReservationEngine y ofrecer opciones reales.

**Intención vs. consulta instructiva:** «no sé cómo reservar» debe explicar el proceso y preguntar si desea ayuda, **no** crear una reserva. «Quiero reservar» inicia flujo. «¿Puedo reservar?» es consulta ambigua suave: contestar y ofrecer empezar.

## 5. Política de reserva — cliente primero, nombre al final

**Orden de preguntas recomendado:** información comercial mínima → sede + servicio (o se deduce lo ya dicho) → fecha → disponibilidad y hora → nombre para formalizar → resumen claro → confirmación explícita → commit idempotente.

Esto no impide recibir nombre de forma adelantada: el sistema debe aprovechar todos los slots que el cliente envíe. Reservas transaccionales continúan en `ReservationEngine`; el diálogo no decide disponibilidad ni crea la reserva por sí mismo.

Ejemplo de una sola intervención: «Hola, soy Ana, quiero limpieza dental en Concepción pasado mañana por la tarde». El parser devuelve objetivo reservar, nombre Ana (aclarar apellido antes de confirmar, si negocio lo exige), servicio candidato, sede candidata, fecha local resuelta y franja tarde. Solo queda consultar **disponibilidad real** y completar los datos necesarios. No formular cuatro preguntas que ya quedaron respondidas.

Cuando no hay cupo: proponer alternativas verificadas de fecha/sede/horario, sin inventar. Si cambia sede, recomprobar servicio y horas. Una respuesta «sí» fuera de una etapa de confirmación vigente jamás crea reserva.

## 6. Recuperación y proveedores IA

- Separar contadores `SemanticAmbiguityCount`, `ProviderOutageCount` y `TransportFailureCount` (preferible sin migración si el DTO actual admite extensión compatible). Un fallo del proveedor **no incrementa** la frustración atribuida al cliente.
- Antes de llamar IA, intentar slot local seguro y menú contextual solo si el cliente lo pide. Al faltar un dato preguntar *ese dato* y no listar todas las capacidades del negocio.
- Contrato de extracción JSON estricto con campos whitelist, sin `WorkspaceId` ni operaciones; validación de enumeraciones, fechas, cantidades, IDs y fuentes. El modelo propone candidatos; el software verifica.
- Controlar `Timeout`, `RateLimit`, `BadJson`, `AuthOrModelConfiguration`, `Unavailable` y latencias separadamente; no tratar todos como «General». Reintento único y presupuestado en casos seguros; evaluar fallback Groq→Gemini para JSON inválido bajo política explícita, no bucles ni gasto infinito.
- Medir en el servidor real qué proveedor/modelo se llama, resultado `failure-kind`, latencia y razones del JSON rechazado, **sin registrar prompt completo, texto del usuario, teléfono, token ni credenciales**.
- Presupuestos de IA existentes continúan atómicos en Firestore (`ai-budget`), workspace y cliente; el camino sin IA debe respetar licencia y aislamiento.

## 7. ResponsePlan y calidad del lenguaje

Dos componentes distintos:

1. **DialoguePolicy / Domain:** construye hechos confirmados y una próxima pregunta. Ejemplo `{service:'Limpieza dental', location:'Concepción', requestedDate:'2026-10-22', availableTimes:[...], nextQuestion:'¿Qué hora prefieres?'}` con fuentes y permiso.
2. **ResponseComposer:** formula 1–3 frases, tono cortés y natural, un siguiente paso, evitando «no pude interpretar», «tu borrador sigue guardado» o menús globales salvo necesidad. Plantillas naturales son respaldo determinista confiable; generación LLM opcional solo después de contar con `ResponsePlan` validado y verificación de que su salida no introduce hechos no permitidos.

**Reglas de copy:**
- No suplantar a una persona («soy el asistente virtual de [negocio]» cuando proceda).
- No repetir saludo/misma lista al segundo turno.
- No preguntar lo que ya se respondió.
- Ante falla IA, seguir si el dato local es suficiente: «¿Qué día prefieres?» en vez de exponer errores internos.
- No decir «te envié el catálogo» si no se inicia envío real; si estado `UnknownDelivery`, no repetir a ciegas.
- «Quiero una cita» => orientación breve y pregunta relevante; ante malestar => reconocer dificultad, continuar con el dato útil y ofrecer humano sin forzarlo.
- Estilo adaptable por sector (clínica/tienda/hotel), pero **misma garantía funcional y de licencia**. Si es salud, no inventar diagnósticos ni recomendaciones médicas.

## 8. Motor de pedidos, servicios, preguntas frecuentes y otras licencias

**Pedidos:** en la misma representación de turno soportar «dos panes y un jugo», «mejor tres panes», «quita el jugo», «¿cuánto sería?», «sí, confirmar». El motor existente sigue validando catálogo, cantidad, precio confirmado y commit idempotente. No convertir «quiero comprar» en pedido confirmado.

**Productos/servicios:** responder la pregunta concreta con 3–5 ejemplos reales, precio/condiciones si existen y sus fuentes; ofrecer PDF como complemento, no esconder los hechos detrás de archivo adjunto. Para consulta específica, respuesta específica; disponibilidad verificada por sede.

**FAQ y horarios:** conocimiento recuperado por workspace y permisos; separar «cómo reservo» (explicación) de «reserva para mí» (acción). Si no hay datos, admitirlo y dar opción de hablar con alguien.

**Licencias:** menú, NLU, conocimiento, acciones y API verifican el mismo `EntitlementService` por workspace. Reservas pueden depender internamente de sedes y horarios sin habilitar consultas públicas independientes de esos módulos. Revocaciones se comprueban antes del commit/lecturas sensibles.

## 9. Plan de implementación, en entregas integrables

| Fase | Código y entregables | Gate de aceptación |
|---|---|---|
| **0. Baseline y contrato** | Congelar versión actual, grabar 60–100 diálogos sintéticos *golden*, separar fallas proveedor/semántica/negocio/transporte, telemetría sin PII; diseñar DTO `TurnUnderstanding` y `ResponsePlan` | Pruebas actuales inalteradas; el diálogo observado se reproduce automáticamente como fallo antes de corregir |
| **1. Resolución local de slots** | `SpanishTextNormalizer`, `NameSlotResolver`, `LocationMentionResolver`, `BusinessDateResolver`, `TimePreferenceResolver`, `OfferingMentionResolver`; reloj y catálogo real | 100% de expresiones inequívocas del incidente resueltas sin LLM; cero selección arbitraria |
| **2. Dialogue Engine y estado** | `DialogueTurnContext`, `TurnUnderstandingPipeline`, `DialoguePolicy`, merge validado multi-slot; preguntas por estado y recuperación, distinguiendo outages | «No sé cómo reservar» y multi-slot correctos; interrupciones preservan draft; no menú global ante slot inequívoco |
| **3. Reserva natural** | Adaptar `BookingFlow`/`UpdateStep` a agenda primero, nombre hacia el final, revalidación de disponibilidad y confirmación | Conversación completa reserva 1 vez, preferencia horaria no se convierte en hora inventada, cambios de sede invalidan datos dependientes |
| **4. Compositor conversacional** | `ResponsePlan`/`ResponseComposer` con verificación factual y fallback breve; FAQ/catálogo con respuesta informativa real, PDF opcional | Ninguna afirmación de hechos inexistentes ni de entrega no confirmada; tono natural sin exponer lógica interna |
| **5. Pedidos y perturbaciones** | Orden, correcciones, múltiples productos, cambio de intención, handoff real, proveedor caído, mensajes en ráfagas, revocación de licencia | Cero operaciones no confirmadas, sin duplicados y respuestas útiles aun si LLM falla |
| **6. Ensayo y despliegue gradual** | CI suites unitarias y 25+ integraciones en Firestore Emulator/PostgreSQL, golden dialogs, staging/workspace de prueba, canary Oracle con feature flag | Resultado de calidad y seguridad aprobado, health + WhatsApp reales; posibilidad de desactivar el nuevo diálogo sin revertir migración |

Cada fase se entrega con **(a)** diff, **(b)** pruebas, **(c)** incompatibilidades y **(d)** prompt de Codex limitado a esa fase. No autorizar refactorización masiva de seis subsistemas en un único PR.

### Ubicación sugerida del código (adaptar a la estructura existente)

- `NexFlow.Application/Features/Automation/Dialogue/Contracts/` — act, slots, estado de comprensión, plan de respuesta.
- `.../Dialogue/Understanding/` — pipeline y resolutores inyectados, interfaces a catálogos/zonas.
- `.../Dialogue/Policy/` — siguiente acción/slot sin depender de texto de presentación.
- `.../Dialogue/Presentation/` — compositor de WhatsApp, factual guard y tono por rubro.
- Reusar `AiInterpreter`, `AiRouter`, `ContextRecoveryService`, `ConversationStateService` y flujos existentes; migrar gradualmente `ConversationRecoveryService` y `AiResponseOrchestrator` a delegar estas funciones.
- Los protocolos externos, tablas PostgreSQL, `InboundMessageWorker`, `OutboundMessageService`, `ReservationEngine` y `OrderFlow` se preservan salvo cambios necesarios con pruebas de contrato.

## 10. Batería de conversación de aceptación

Reloj de prueba fijo 2026-10-20 16:00 **America/Lima**, sedes sintéticas «Sede Concepcion» y «Sede san jeronimo», servicios sintéticos odontológicos, políticas de negocio simuladas y disponibilidad controlada por fixture.

**Casos de la conversación original:**

1. `Hola` → saludo claro sin exigir elegir menú.
2. `No sé cómo hacer una reserva` → explicar el proceso y preguntar si quiere ayuda, sin iniciar operación.
3. `Quiero reservar una limpieza dental en Concepción pasado mañana` → slots ya presentes, pregunta el siguiente faltante.
4. `Deyvid Pariona` → nombre válido en el paso apropiado, sin exigir tercera palabra.
5. `Deyvid Pariona Ramos` → también válido.
6. `En la sede concepción` / `sede Concepcion` / `en San Jerónimo` → sede única autorizada.
7. `pasado mañana`, `el 22 de octubre`, `22/10`, `este jueves` → 2026-10-22 o aclaración si término ambiguo.
8. `por la tarde`, `a las 4`, `después de las cinco` → franja/preferencia/hora interpretada correctamente.
9. `22 de octubre estúpido` → resolver fecha sin transferir por insulto, manteniendo trato digno.
10. `¿Cuánto cuesta la limpieza?` durante reserva → precio verificado + reanudación de la reserva.
11. `No tienen más horarios?` → preguntar/mostrar alternativas disponibles, no inventar.
12. `Mejor sede San Jerónimo` → conservar lo independiente e invalidar disponibilidad/hora.
13. `Sí` antes de confirmación → no reserva.
14. `Sí` tras resumen completo → exactamente una reserva aunque se repita inbound.
15. `Quiero dos de X y uno de Y` → borrador único, precios validados, sin pedido definitivo sin confirmar.
16. `Asesor` → transferencia humana y no segunda respuesta IA tardía.
17. Licencia solo SERVICES+RESERVATIONS → no ofrecer pedidos, sí usar sedes necesarias internamente.
18. Caída Groq/Gemini, timeout, JSON no válido → continuidad específica del paso y sin bucle de menú.
19. Dos negocios con productos de igual nombre → no cruzar datos.
20. Reintento de webhook, respuestas UnknownDelivery, ráfaga de turnos y concurrencia humana → no duplicados ni reenvío inseguro.

**Matriz ampliada:** 60–100 conversaciones completas en 4 sectores (clínica, hotel, tienda/panadería, servicios), formulaciones regionales, pausas/reanudaciones, entidades con alias, horarios cerrados, feriados si el negocio los configura, errores de proveedor, revocaciones y entradas maliciosas. Usar datos sintéticos antes de prueba real.

## 11. Criterios de GO/NO-GO para vender licencias

**Bloqueantes absolutos:**
- 0 operaciones comerciales sin permiso de licencia.
- 0 reservas/pedidos duplicados frente a reintentos.
- 0 creación de reservas sin confirmación explícita.
- 0 precios, sedes, servicios u horarios inventados en casos evaluados.
- 0 respuestas del bot en conversaciones bajo control humano; 0 reenvíos de UnknownDelivery.
- 0 filtraciones de otro workspace o PII en prompts/logs.
- 0 silencios inexplicados en la batería; todo fallo tiene resultado diagnosticable y seguimiento seguro.

**Objetivos de producto (metas propuestas, no estándares externos):**
- ≥95% de tareas canónicas de reserva/consulta completadas en la batería golden.
- ≥90% de turnos con respuesta apropiada/contextual en evaluación humana independiente.
- ≥90% de consultas simples comprendidas sin llamar a IA si son inequívocas.
- ≥95% de respuestas fácticas sustentadas cuando existen datos autorizados; **cero invenciones críticas**.
- Registrar p50/p95 de latencia, tasa de handoff, tasa de caída LLM, porcentaje de prompts invalidados y costo por conversación por workspace; fijar umbrales solo con mediciones del servidor.
- Ejecutar 50+ conversaciones con mensajes naturales y múltiples giros, sin depender de un único guion.

**Rúbrica humana de 0–4 por turno:** comprensión, factualidad, continuidad, utilidad, tono, economía de preguntas y seguridad. Revisar falsos positivos (el bot actuó sin querer) con prioridad sobre falsos negativos.

## 12. Despliegue y protección de producción

- El código del ZIP no autoriza modificar el Oracle actual; establecer feature flag de nuevo diálogo por workspace, off por defecto.
- Respetar la arquitectura real: **Docker solo en Oracle Ubuntu ARM64**; desarrollo .NET en Windows, CI de emuladores en GitHub. No instalar Docker o Java local innecesariamente.
- Empezar con pruebas locales unitarias, CI controlada en PostgreSQL + Firestore Emulator, compilar imagen ARM64 en Oracle y activar en workspace de prueba.
- No ejecutar nuevas migraciones mientras no estén justificadas ni borrar conversaciones/Inbound históricos para «limpiar» métricas.
- `health/ready` valida infraestructura, **no** experiencia de diálogo; comparar trazas de inbound → comprensión → política → backend → outbound.
- No declarar ventas comerciales listas hasta asegurar recuperación Firestore (Spark: respaldo lógico privado verificado), revisar retención/privacidad y superar batería E2E.
- Mantener deshabilitado el borrado nuclear de tenants; preservar epochs/fences, FIFO, `UnknownDelivery`, control humano y reglas de licencia.

## 13. Protocolo de trabajo con Codex y auditor

1. Esta documentación es fuente de verdad del objetivo y las invariantes.
2. Codex toma **una fase** y produce PR pequeño con contratos, migración progresiva y pruebas.
3. El auditor revisa diff contra ZIP/HEAD real, pruebas fallidas y oportunidades de bypass.
4. CI prueba regresiones y diálogos golden; no desplegar un «verde» que no cumple las conversaciones.
5. En Oracle se activa canary solo para workspace autorizado; si degrada, apagar el feature flag del nuevo diálogo sin revertir protocolos comerciales.
6. Tras aprobar fase 6, comenzar piloto con negocios voluntarios y mensajes reales, medir calidad y revisar licencias comerciales.

**Primera orden de Codex recomendada:** desarrollar solo Fase 0+Fase 1 (contratos mínimos y slot resolvers + dataset reproducible) y detenerse para auditoría. No reescribir a la vez `BookingFlow`, `AiResponseOrchestrator`, proveedores IA, `OrderFlow` y transporte.

---

### Nota sobre certeza
Este plan surge de la inspección estática real del ZIP y del chat aportado, no de una prueba en vivo de Gemini/Groq. Las menciones a fallas concretas de proveedor son hipótesis a validar con trazas por turno. Las metas de calidad son objetivos de producto sujetos a evaluación, no promesas de perfección.
