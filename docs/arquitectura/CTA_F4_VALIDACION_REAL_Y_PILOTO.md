# CTA F4 — Validación real y piloto DEMO

Evidencia local del 31/10/2026; cierre de comprobaciones a las 19:19 UTC aproximadamente. **Fase parcialmente verificada; NO-GO para piloto real.** No se realizaron llamadas a Groq/Gemini, WhatsApp, Oracle, Firestore/Redis/PostgreSQL reales, ni despliegues. No se atribuye comprensión real a interpretaciones inyectadas.

## 1. Identidad y gates

HEAD local y `main` remoto: **`b8338c598815c658b8dab1f33f057627192fb4fa`**. El informe S2/S3 relata el árbol anterior a publicar ese commit; su base `17ab2cd` no es el HEAD actual. Índice vacío. Árbol inicial: solamente los dos documentos del propietario F4 sin seguimiento; conservados sin editar.

`git ls-remote` falló por DNS del sandbox. La consulta de solo lectura mediante el conector GitHub verificó la rama y la colección completa de runs filtrada por `head_sha` (sin limitar a pull requests): **0 runs**. No se hizo fetch, dispatch ni otra modificación remota.

Resumen sanitizado de esa consulta: `backend/TestResults/CTAF4/ci-preflight.json`; no contiene datos de autor, correos ni credenciales.

| Gate | Estado | Evidencia / bloqueo |
|---|---|---|
| G1 identidad | PASS | HEAD local/remoto coincidente; sin diferencias iniciales de código ni staging preparado. |
| G1 certificación externa | BLOCKED | No hay runs/checks certificados de los workflows para el SHA. Los nuevos parches locales tampoco tienen SHA publicado. |
| G1 activación | PASS estático / NOT_RUN efectivo | `AssistantRecoveryOptions.cs:5`: false por defecto. Sin declaración del flag en appsettings versionados, ni variable de proceso/usuario local presente. **No se verificó el valor efectivo en Oracle**, por prohibición de acceso; no se afirma que nunca se activó allí. |
| G2 interpretación real | **BLOCKED_PROVIDER_ENV** | Ausentes claves/modelos de prueba de ambos proveedores y autorización opt-in en entorno de proceso/usuario. Cero solicitudes externas. Harness preparado, compilado y probado offline. |
| G3 E2E local | PASS | **117 ejecutadas / 117 aprobadas / 0 fallidas / 0 omitidas**, con dos defectos reproducidos y reparados. Proveedores/engines de negocio simulados; SDK/repository Firestore con transporte RPC fake existente. |
| G4 P0 / piloto | BLOCKED | Incidente `b5f1ef63-21b0-454e-bd12-44a9bef14c71` **NO DETERMINADO**. Guía de extracción y gates por capacidad preparados abajo; ninguna extracción real ejecutada. |

Los gates se revisaron en orden. Al faltar el entorno G2 se continuó con el trabajo local independiente de G3/G4, tal como permite el contrato; no se interpretó el bloqueo como aprobación del proveedor.

## 2. G2: mecanismo listo, sin fabricar resultados

Nuevo `backend/NexFlow.Tests/Concierge/CtaRealInterpretationTests.cs`, categoría **ProviderIntegration**. Reutiliza `AuthorizedProviderFactAttribute`, `AiInterpreter`, **AiRouter real**, `GroqAiProvider`, `GeminiAiProvider` y las métricas `AiTelemetry`. No inicia la API ni instancia OrderFlow, ReservationEngine, stores o servicios outbound. No recibe teléfonos, nombres de pacientes, precios ni contexto real.

Controles:

- Solo lee `NEXFLOW_TEST_GROQ_API_KEY`, `NEXFLOW_TEST_GROQ_MODEL`, `NEXFLOW_TEST_GEMINI_API_KEY`, `NEXFLOW_TEST_GEMINI_MODEL`; **sin fallback a appsettings, .env ni secretos de la API**. Las variables `Groq__ApiKey` / `Gemini__ApiKey` tampoco estaban presentes; no se inspeccionaron archivos de secretos.
- Requiere `NEXFLOW_CONCIERGE_REAL_AI_AUTHORIZED=1`. Ejecutado sin autorización: descubrimiento de una prueba, **0 PASS, 0 FAIL, 1 omitida**, no una certificación verde del proveedor. Artefacto: `backend/TestResults/CTAF4/provider-blocked.trx`.
- Presupuesto compartido de **12 intentos HTTP como máximo**, incluyendo errores, timeouts y fallback. No reembolsa intentos ni reintenta por JSON incorrecto. Si se gasta el cupo antes del caso 12, registra los restantes como NOT_RUN_BUDGET y el gate falla.
- Usa exactamente la ruta de producción para IntentExtraction: **Groq principal → Gemini únicamente ante fallo transitorio**. No provoca fallos artificiales para forzar Gemini, no cambia de tarea/modelo ni habilita el fallback interno de otro modelo Gemini. La muestra podría no llamar a Gemini si Groq funciona: en ese caso **Gemini permanece NOT_RUN**, incluso si los casos Groq pasan. Una evaluación directa adicional requiere presupuesto/plan autorizado; no se esconde dentro de estos 12 intentos.
- HTTPS solamente a los dos hosts de proveedor que usan los adaptadores actuales, sin query string, redirecciones ni destinos configurables. Timeout por adaptador 8 s, deadline por caso 18 s. Sin una llamada de composición.
- Reserva `backend/TestResults/CTAF4/provider-sample.started` con CreateNew antes de gastar; una repetición accidental no reinicia el presupuesto. No se borra automáticamente ese marcador. Una nueva muestra después de un intento necesita presupuesto autorizado separado.
- Registra SHA local obtenido con Git, marca de working tree local, caso, salida estructurada saneada, proveedor HTTP efectivo, estado, latencias individuales, caracteres y tokens reportados por AiTelemetry. Conserva evidencia por caso y al terminar, incluidos los casos no ejecutados. No registra prompts, claves, cabeceras, URI completas ni texto arbitrario del modelo. Los únicos productos exportables son las dos etiquetas sintéticas autorizadas.
- JSON bruto válido se distingue de rechazo del contrato por AiInterpreter; el acierto semántico usa criterios independientes del proveedor. Una referencia comercial o denegación de negocio se valida en G3, no se declara autorizada por la salida del modelo. `Confirmed` o propuestas del servidor enviados por el LLM siguen rechazándose.

### Matriz congelada de esta muestra

En todos los casos siguientes: estado **NOT_RUN / BLOCKED_PROVIDER_ENV**, respuesta estructurada **no disponible**, proveedor/modelo efectivo **no invocado**. La columna de criterio es una expectativa, **no una respuesta obtenida**.

| Caso | Texto sintético / contexto | Criterio independiente | Resultado estructurado real |
|---|---|---|---|
| C01 | «Dije si está disponible el hilo dental»; referente único | ProductQuery, Stock/Availability, no edición | — NOT_RUN |
| C02 | «solo uno de ese»; dos unidades de un producto | Set(1), referencia vigente | — NOT_RUN |
| C03 | «quita el segundo»; dos líneas vigentes | Remove, ReferenceIndex=2, sin elegir otro producto | — NOT_RUN |
| C04 | «reduce uno, no lo quites» | Decrease(1), no Remove | — NOT_RUN |
| C05 | «envía solicitud» sin consentimiento | Submit/Resume solicita revisión, no escritura | — NOT_RUN |
| C06 | «solo estoy preguntando» | Act informativo, sin edición/directiva | — NOT_RUN |
| C07 | «quieor preparar una reseva»; pedido activo | StartOperation Reservation, sin commit | — NOT_RUN |
| C08 | «regresemos a lo de la compra»; reserva activa/pedido suspendido | Resume Order, sin escritura | — NOT_RUN |
| C09 | «cancelar»; dos preparaciones | No inventar DiscardScope | — NOT_RUN |
| C10 | «descarta solo la reserva»; pedido activo | Scope Suspended; backend exige otra decisión | — NOT_RUN |
| C11 | «baja uno de la compra y cambia también la fecha de la reserva» | Aclarar mutaciones cruzadas | — NOT_RUN |
| C12 | «confirmar» sin revisión/pregunta | Aclarar, nunca producir consentimiento | — NOT_RUN |

**Métricas reales de proveedor:** solicitudes enviadas 0; tokens NO MEDIDOS; latencia NO MEDIDA; coste NO DETERMINADO. No se publican percentiles ni se extrapola inteligencia, naturalidad o economía. Cuando haya muestra, el JSON contiene latencias individuales y tokens disponibles; no calcula precio sin tarifa verificable ni p95 robusto con 12 casos.

### Acción exacta que desbloquea G2

En una PowerShell nueva, cargar las cuatro variables de prueba mediante el gestor local autorizado. Como alternativa, entrada enmascarada, **sin compartir claves en el chat ni escribirlas en archivos versionados**:

```powershell
$env:NEXFLOW_TEST_GROQ_API_KEY = [Net.NetworkCredential]::new('', (Read-Host 'Clave Groq de prueba' -AsSecureString)).Password
$env:NEXFLOW_TEST_GROQ_MODEL = Read-Host 'Modelo Groq autorizado'
$env:NEXFLOW_TEST_GEMINI_API_KEY = [Net.NetworkCredential]::new('', (Read-Host 'Clave Gemini de prueba' -AsSecureString)).Password
$env:NEXFLOW_TEST_GEMINI_MODEL = Read-Host 'Modelo Gemini autorizado'
$env:NEXFLOW_CONCIERGE_REAL_AI_AUTHORIZED = '1'
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --filter 'FullyQualifiedName~CtaRealInterpretationTests.Twelve_synthetic_cases_through_the_production_router_with_a_shared_http_budget' --logger 'trx;LogFileName=provider-real.trx' --results-directory backend/TestResults/CTAF4
```

Ejecutar **solo ese filtro**, una vez: la clase histórica ProviderCertificationTests tiene otra muestra y no forma parte del presupuesto F4. No ejecutar toda la categoría ProviderIntegration. Revisar `provider-sample.json` y el TRX; no interpretar exit code 0 de una prueba omitida como PASS. Cerrar la terminal al finalizar. Si existe el marcador de intento anterior, detenerse y revisar la evidencia/presupuesto, sin borrarlo para reintentar automáticamente. Ninguno de estos pasos reales se ejecutó en este encargo.

## 3. Defectos demostrados y cambios mínimos

### F4-01 — catálogo vacío, P2 UX

Reproducción real del handler, con reserva activa y pedido suspendido: «¿Qué productos tienen?» respondía «No tengo una opción activa registrada que coincida con tu consulta». Causa: `OfferingResponseFormatter.Format:50` utilizaba el mismo texto para búsqueda específica sin coincidencia y catálogo amplio vacío.

Corrección: cuatro líneas para catálogo **PRODUCT/Broad vacío**, diferenciando negocio de sede. Ahora responde «En este momento no hay productos activos publicados en el catálogo del negocio» y después continúa preguntando la fecha pendiente. No promete stock, no cambia permisos, consultas, precios ni borradores. La búsqueda específica conserva su mensaje anterior.

Evidencia: `empty-before.trx` **1 ejecutada / 0 PASS / 1 FAIL**; prueba posterior de mensaje completo, borrador y snapshot inmutables, cero efectos y cero llamadas IA adicionales; dos variantes de alcance. No se cambió ninguna expectativa histórica.

### F4-02 — inicio referencial pierde cantidad, P1 continuidad

Reproducción nueva de G3:

1. Catálogo y consulta de precio «¿Cuánto cuesta Hilo dental?» producen un referente único y pregunta vigente comprobados.
2. «Me apuntas dos de ese» abre ORDER_COLLECT_ITEMS vacío, **0 llamadas IA**, en vez de conservar el referente/cantidad. La interpretación controlada preparada en el fixture ni siquiera era invocada.

Causa: el resolutor local no resolvía ese ítem; `CommerceTurnResolver.ResolveAsync:179` solo cedía sus errores a IA si ya había pedido activo. Con CurrentGoal vacío caía después en Start, perdiendo los datos aportados.

Corrección: reutilizar la referencia vigente ya validada y ceder a la ruta semántica existente si un ítem no se resolvió y existe esa referencia, aunque sea el primer turno del pedido. No se añaden palabras, regex, menús ni escrituras; no se toma el nombre propuesto por el modelo como ID. Una incertidumbre antes silenciada cuesta ahora **una interpretación necesaria**; los casos locales inequívocos siguen sin ella.

Evidencia: `unified-before.trx` **1/0/1**, referencia registrada y CurrentQuestionValid=true. Tras el parche, la misma entrada, con interpretación inyectada, conserva dos unidades y termina la secuencia segura. Esto demuestra el backend posterior a interpretación y el enrutamiento local; **no demuestra que Groq/Gemini entiendan la frase**.

## 4. G3 — conversación y seguridad local

Pruebas nuevas: cuatro casos `CtaF4LocalTests` (catálogo global, específico, sede y secuencia unificada); tres casos `CtaProbeSafetyTests` (presupuesto/fallback HTTP, bloqueo de endpoints y saneamiento/inventario). Se reutilizaron fixtures existentes; sin infraestructura nueva.

El test unificado afirma después de cada turno WorkspaceId, ConversationId, identidad/revisión de pregunta cuando hay Focus, ningún pedido antes de consentimiento y ninguna reserva. Tras retomar comprueba el mismo OperationId y ActivationRevision=2; edición/fallo conservan el suspendido; un replay de Confirmar no repite interpretación ni efecto.

| Turno sintético | Resultado de backend observado | IA del fixture acumulada | Pedidos / reservas |
|---|---|---:|---|
| Catálogo | Productos verificados del fixture | 0 | 0 / 0 |
| Precio de Hilo dental | Referente único/pregunta vigente | 0 | 0 / 0 |
| «Me apuntas dos de ese» | ORDER, dos unidades | 1 | 0 / 0 |
| «Preparemos también una reserva» | RESERVATION, pedido suspendido | 2 | 0 / 0 |
| Sede Concepción, limpieza dental | Sede/servicio del fixture | 2 | 0 / 0 |
| Disponibilidad mañana 10:00 | Consulta puntual; borrador sin mutaciones; SelectReservationTime | 2 | 0 / 0 |
| Retomar pedido | Cantidad 2, mismo OperationId, revisión 2 | 3 | 0 / 0 |
| «Reduce uno, no lo quites» | Cantidad 1; reserva suspendida intacta | 4 | 0 / 0 |
| Fallo de interpretación inyectado | Cantidad y snapshot conservados | 5 | 0 / 0 |
| «Envía solicitud» | Revisión vigente, ConfirmOrder | 6 | 0 / 0 |
| «De acuerdo.» | H06: no consiente | 6 | 0 / 0 |
| Nueva petición de envío | Revisión vigente, sin efecto | 6 | 0 / 0 |
| «Confirmar», después replay del mismo ID | Un PendingReview, sin duplicado | 6 | 1 / 0 |

El stock, disponibilidad y escrituras de esta tabla pertenecen exclusivamente al fixture: no son datos del negocio DEMO ni una medición comercial. Los tokens/caracteres reales no se midieron por falta de proveedor. La reparación de catálogo no añade llamadas; no se afirma reducción de repreguntas. El harness guarda caracteres/tokens/latencias cuando haya respuestas reales; el test de transporte comprobó contabilidad de tokens con **HTTP sintético**, no consumo real.

Gate final dirigido: **117/117, 0 fallos, 0 omisiones**, `backend/TestResults/CTAF4/gate3.trx`. Incluye F1, ciclo de descarte, continuidad, confirmaciones de pedidos, G8FocusedRegression, concurrencia y recuperación Redis con fake. Dos fallos intermedios del harness fueron de configuración Moq Strict de Dispose; se añadió su setup, sin debilitar las aserciones de red/presupuesto. Se conserva `local-second.trx` con ese resultado 5 PASS / 2 FAIL.

Después de completar la captura dinámica de identidad Git y promedio de latencias del harness se compiló nuevamente con su filtro offline: **3/3 PASS**, sin fallos ni omisiones (`harness-final.trx`). El marcador `provider-sample.started` no existe: no se inició ninguna muestra facturable. No se repitió el gate conversacional ni la suite completa por esos metadatos.

Comandos finales ejecutados:

```powershell
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --filter 'FullyQualifiedName~CtaF4LocalTests|FullyQualifiedName~CtaProbeSafetyTests|FullyQualifiedName~ContextualFocusTests|FullyQualifiedName~CtaContinuityTests|FullyQualifiedName~CtaGate0Tests|FullyQualifiedName~OrderConfirmationAuditTests|FullyQualifiedName~AssistantRc1ConcurrencyTests|FullyQualifiedName~AssistantRedisRecoveryTests|FullyQualifiedName~G8FocusedRegressionTests' --logger 'trx;LogFileName=gate3.trx' --results-directory backend/TestResults/CTAF4
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~CtaRealInterpretationTests' --logger 'trx;LogFileName=provider-blocked.trx' --results-directory backend/TestResults/CTAF4
dotnet build backend/backend.slnx --no-restore
git diff --check
```

Build **PASS, 0 errores, 0 advertencias** en la ejecución incremental final. La compilación inicial mostró los tres avisos antiguos CS8981 (2) / CS8602; no se tocaron. No se repitió suite completa, frontend ni históricos. Los **1806/1806** y G8 **130/101/29** pertenecen a la evidencia anterior S2/S3, no a una nueva ejecución sobre este diff. G8, H06, corpus, motores, CAS y outbox no se editaron.

## 5. G4 — guía P0 de lectura para el propietario, NO ejecutada

Objetivo: correlacionar la reserva conocida con el inbound que la originó, la revisión/consentimiento y la entrega. Tener un registro Confirmed **no demuestra por sí solo consentimiento válido**. Hora reservada no es timestamp de creación. No reproducir ni reenviar eventos para investigar.

Operador autorizado: obtener WorkspaceId del DEMO por su canal privado y verificar que corresponde a la reserva `b5f1ef63-21b0-454e-bd12-44a9bef14c71`. Usar sesión PostgreSQL ya autorizada con rol de solo lectura; no publicar DSN. Parámetros psql: `workspace_id` UUID, `reservation_id` UUID de arriba; ventana UTC `from_utc` / `to_utc` acotada al incidente por el operador, sin inventar hora. Ejecutar estas consultas, no SELECT *:

```sql
BEGIN TRANSACTION READ ONLY;
SET LOCAL statement_timeout = '5s';
SELECT "Id", "WorkspaceId", "ServiceId", "LocationId", "Status", "StartTime", "EndTime",
       md5(coalesce("SourceMessageId", '')) AS source_key
FROM "Reservations"
WHERE "WorkspaceId" = :'workspace_id'::uuid AND "Id" = :'reservation_id'::uuid;

SELECT i."Id", md5(i."ExternalMessageId") AS source_key, i."ReceivedAt",
       i."ProcessingStartedAt", i."ProcessedAt", i."Status", i."Attempts"
FROM "InboundMessages" i
JOIN "Reservations" r ON r."WorkspaceId" = i."WorkspaceId"
 AND r."SourceMessageId" = i."ExternalMessageId"
WHERE r."WorkspaceId" = :'workspace_id'::uuid AND r."Id" = :'reservation_id'::uuid;

SELECT "Id", "EventType", "Status", "CreatedAt", "ProcessedAt", "RetryCount",
       "PayloadJson"->>'CorrelationId' AS correlation_id
FROM "OutboxMessages"
WHERE "WorkspaceId" = :'workspace_id'::uuid
 AND "CreatedAt" BETWEEN :'from_utc'::timestamptz AND :'to_utc'::timestamptz
 AND "PayloadJson"->>'IdempotencyKey' = 'res_' || :'reservation_id'
ORDER BY "CreatedAt" LIMIT 20;
ROLLBACK;
```

Campos contrastados con `ReservationConfiguration`, `NexFlowDbContext`, InboundMessage y ReservationEngine. El status de inbound/outbox puede ser numérico según EF: cotejar sus enums; no inferir Processed de un número sin esa correspondencia. `md5` aquí es solo una etiqueta correlacional privada, no autorización/criptografía de seguridad. No consultar/exportar Phone, CustomerIdentifier, CustomerName, payload, Error ni LastError. Si timeout, detenerse; no crear índices ni cambiar el servidor.

### Correlación outbound y preguntas, lectura privada minimizada

1. Solo el operador puede leer internamente SourceMessageId de esa fila para calcular el identificador outbound; no exportar el ID de proveedor sin sanear. El contrato actual de `OutboundMessageService.cs:37,39` es:

   ```text
   key = workspaceGuidEnFormatoN + ":AI:" + sourceMessageId + ":response:1"
   outboundId = SHA256(UTF8(key)) como HEX mayúsculas
   ```

2. En Firestore, lectura directa **únicamente** de `workspaces/{workspace}/outboundMessages/{outboundId}` con proyección `id`, `conversationId`, `direction`, `sender`, `kind`, `status`, `origin`, `timestamp`, `transportStartedAt`, `transportLeaseUntil`. Para confirmar entrega, el operador puede clasificar la existencia de providerConfirmationId y exportar solo su hash. No exportar content, phone, mediaUrl, idempotencyKey ni identificadores externos sin sanear.
3. Contrastar logs `OutboundStage` por WorkspaceId/OutboundId y ventana UTC: ResponsePersisted, intento, confirmación durable o UnknownDelivery. Una entrega tardía con source anterior es una hipótesis a contrastar, no causa demostrada. No repetir Failed/UnknownDelivery, reabrir leases ni reprocesar DeadLetters.
4. Para el mismo chat y ventana, el operador puede obtener metadatos de `workspaces/{workspace}/conversations/{conversation}/messages` (sin content) para ordenar inbounds/outbounds; correlacionar source_key mediante hash en su herramienta privada. No barrer otros workspaces ni conversaciones.
5. El estado está en `workspaces/{workspace}/conversationStates/{phone}`: phone se resuelve privadamente por el operador, no se pide por chat ni se exporta la ruta. `contextJson` es un string que puede incluir PII; no es posible proyectar sus miembros con Select de Firestore. **Solo si está autorizado** leerlo en proceso privado, deserializar y exportar whitelist: StateVersion, LastUpdated, CurrentGoal/CurrentStep sin datos personales, QuestionId/Expected/IssuedAt/ExpiresAt/OperationId/ActivationRevision y hash de SourceMessageId/DraftFingerprint. No exportar el JSON, slots personales, texto o snapshots completos.
6. Comparar revisión emitida antes de la respuesta con consentimiento inequívoco sobre **ese** resumen, mismo servicio, workspace y source. La interpretación de IA no es consentimiento. El operador puede informar una clasificación anonimizada y sus hashes/timestamps; sin evidencia anterior no se completa esta comparación.

**Límite confirmado de evidencia disponible:** `FirestoreConversationStateRepository` sobrescribe el contextJson actual; no es un historial de revisiones. `ConversationDiagnostics` emite fase/resultado y `DialogueDecision` intención/acto/operación, pero no garantiza un registro histórico por turno de QuestionId/fingerprint/source. Si el contexto ya cambió y no se conservaron trazas suficientes, esos datos son **NO DISPONIBLES**, no se reconstruyen de memoria ni se inventan. El código actual tampoco prueba qué binario/estado causó el incidente histórico. No se añadieron nuevas infraestructuras o logging de PII para intentar resolverlo en esta fase.

## 6. Gates DEMO separados y activación reversible (solo propuesta)

| Capacidad | Dictamen actual | Condiciones necesarias antes de habilitar |
|---|---|---|
| Consultas y guía sin mutaciones | **GO en fixtures / NO-GO piloto real** | Proveedor real válido, CI mismo SHA, workspace/instancia/conversación de prueba autorizados, capacidades de lectura y permisos reales; ningún outbound histórico pendiente que pueda confundirse. |
| Borradores sin confirmar | **GO en fixtures / NO-GO piloto real** | Todo lo anterior, escritores CTA compatibles, TTL/CAS/revisión y recuperación verificadas. **No existe un modo productivo draft-only demostrado**: otorgar ORDERS/RESERVATIONS no bloquea por sí solo commits. Debe existir un límite de ejecución aislado y autorizado antes de prometer cero escrituras reales. |
| Pedidos reales DEMO | **NO-GO** | Interpretación real + CI, licencia CATALOG/ORDERS, catálogo/precios actuales, revisión/fingerprint/consentimiento independiente, un PendingReview por source, entrega incierta protegida, autorización explícita de prueba. P0 de reservas resuelto o formalmente aislado sin habilitar reservas. |
| Reservas reales DEMO | **NO-GO** | Además: correlación suficiente/cierre o aislamiento explícito del P0 aprobado, disponibilidad/motor/persistencia certificados, consentimiento ligado a revisión, ventana y workspace exactos autorizados. |

El flag actual **es global**, no se encontró un toggle por workspace para CTA. No se debe activar en una API compartida afirmando que afecta solo DEMO. Para un ensayo aislado se necesita un proceso/ruteo de test ya autorizado que atienda exclusivamente ese ámbito, sin datos de clientes; no se creó ni desplegó uno aquí.

Procedimiento preparado:

1. Congelar el diff revisado en un nuevo SHA por el propietario; este encargo no autoriza commit/push. Ejecutar manualmente los cuatro workflows sobre **ese mismo SHA**, comprobar Run IDs, attempts, identity/TRX y ARM64. No atribuirles el SHA b8338c5 si los parches se publican después.
2. Completar G2 sin ampliar el presupuesto; separar proveedores realmente usados de no evaluados, errores JSON de errores semánticos y datos del backend. Evaluación humana independiente de naturalidad todavía pendiente.
3. Operador acredita aislamiento del DEMO, compatibilidad de todos los escritores, configuración efectiva, permisos/módulos y ausencia de pendientes conflictivos. Resolver los gates de la tabla y autorizar expresamente cada capacidad. No cambiar licencias de clientes ni procesar históricos para despejar la cola.
4. Habilitar `AssistantRecovery__EnableContextualFocus` solamente en el host aislado autorizado y ventana aprobada, usando binario compatible. No habilitar composición libre ni cambiar límites de IA. Observar source/outbound, operación/revisión y efectos por separado.
5. Para detener el piloto, cerrar su admisión por el mecanismo operativo autorizado y drenar turnos en vuelo, manteniendo binarios que preserven Focus/PendingDiscard. Terminar o descartar preparaciones con consentimiento, o esperar su expiración y resolver su descarte; verificar por lectura que no quedan snapshots/decisiones pendientes. No borrar conversaciones/recibos.
6. Solo entonces apagar el flag con escritores compatibles. **No revertir a un binario que pierda Focus**, ni apagarlo suponiendo que las decisiones pendientes pueden seguir resolviéndose. Ante incidente, mantener control humano y evidencia, sin reenviar UnknownDelivery ni reiniciar servicios compartidos.

Workflows a ejecutar por el propietario tras publicar una candidata autorizada, **sin dispatch en este encargo**:

- [RC3 Release Checks](https://github.com/saintdeyb321/NexFlow/actions/workflows/rc3-release-checks.yml).
- [PostgreSQL + Firestore](https://github.com/saintdeyb321/NexFlow/actions/workflows/rc3-reservation-integration.yml).
- [Redis](https://github.com/saintdeyb321/NexFlow/actions/workflows/concierge-redis-integration.yml).
- [ARM64](https://github.com/saintdeyb321/NexFlow/actions/workflows/agent-runtime-arm64-certification.yml).

## 7. Archivos, alcance y cierre

Backend productivo (7 inserciones / 2 eliminaciones): `OfferingResponseFormatter.cs`, `CommerceTurnResolver.cs`. Nuevos tests/harness: `CtaF4LocalTests.cs`, `CtaProbeSafetyTests.cs`, `CtaRealInterpretationTests.cs`. Documento nuevo: este informe. Frontend, configuración, secretos, staging, motores transaccionales, migraciones, workflows, corpus y contratos públicos no modificados. Sin dependencias nuevas.

`git diff --check` PASS; build PASS. Índice vacío; HEAD sigue b8338c5. Los documentos iniciales del propietario permanecen sin seguimiento y sin editar. No se ejecutaron suites completas ni pruebas pagadas. Las evidencias son locales del working tree: no certifican el SHA remoto sin este diff.

**Dictamen: GO para revisar los parches y ejecutar el mecanismo aislado de interpretación cuando se configure el entorno autorizado. NO-GO para activar CTA, piloto DEMO real o transacciones.** Bloqueos precisos: claves/modelos de prueba y opt-in ausentes, CI del nuevo SHA pendiente, estado efectivo/aislamiento operacional no verificados, evaluación humana pendiente y P0 histórico no determinado. Se detiene F4 en estos gates; no se inicia otra fase.

### Git status final

```text
 M backend/NexFlow.Application/Features/Automation/Dialogue/Composition/OfferingResponseFormatter.cs
 M backend/NexFlow.Application/Features/Automation/Dialogue/Understanding/CommerceTurnResolver.cs
?? backend/NexFlow.Tests/Concierge/CtaF4LocalTests.cs
?? backend/NexFlow.Tests/Concierge/CtaProbeSafetyTests.cs
?? backend/NexFlow.Tests/Concierge/CtaRealInterpretationTests.cs
?? docs/NEXFLOW_CODEX_PROMPT_04_VALIDACION_REAL_CTA_2026-10-31.md
?? docs/NEXFLOW_CTA_AUDITORIA_S2_S3_2026-10-31.md
?? docs/arquitectura/CTA_F4_VALIDACION_REAL_Y_PILOTO.md
```
