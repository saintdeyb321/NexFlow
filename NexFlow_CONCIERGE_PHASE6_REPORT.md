# Concierge 2.0 — Fase 6: hardening y certificación comercial local

## Dictamen

**NO-GO para iniciar el piloto WhatsApp.** Correcciones y regresión local aprobadas; faltan las integraciones críticas reales, contratos de cuentas IA autorizadas y la revisión humana/WhatsApp de un entorno aislado. No existe evidencia de producción en esta entrega. El runbook está preparado, **no ejecutado**.

Referencia local: `f43f00dc32e86aa8f207cd09c51cd89d429a7dfe`, coincidió con la base solicitada; árbol limpio al comenzar. Se conservó HEAD y todo el trabajo quedó sin commit/push. No se reiniciaron fases anteriores ni se modificaron frontend, migraciones PostgreSQL, Docker/Compose, Oracle, Evolution, n8n, Gotenberg, secretos, `.env` o reglas Firebase. Firestore recibe únicamente el campo opcional expresamente autorizado en el ticket, mediante código de persistencia; no se conectó a datos reales ni hubo backfill.

## Hallazgos reproducidos y correcciones

| Severidad | Evidencia anterior | Resultado aplicado |
|---|---|---|
| P1 | Tres reproducciones de precio/moneda/sede terminaban en `ConcurrencyException`; el estado conservaba `ORDER_COMMIT` sin respuesta conversacional. `phase6-reproduction.trx` conserva los fallos reales. | `OrderCatalogChangedException` identifica exclusivamente rechazos definidos de catálogo anteriores al commit. Se conserva la familia de concurrencia/HTTP 409. CAS, identidad reutilizada, control humano y transporte incierto siguen propagándose sin conversión genérica. |
| P1 | `RequestedLocationId` servía para validar pero no figuraba en escritura/mapeo del documento. | Campo Firestore opcional, persistido y recuperado; lectura legacy sin campo devuelve null. La sede se lee dentro del workspace y transacción del pedido; replay no puede cambiarla. La propiedad sigue fuera del JSON público como antes. |
| P1 | Tras eliminar una sede elegida, el segundo envío creaba un pedido sin sede si los productos tenían alcance ALL. Prueba fallida en `phase6-location-reproduction.trx`. | `ORDER_LOCATION` conserva la obligación de una elección nueva, incluso al editar cantidades o volver a enviar. Sólo una sede explícita y validada permite continuar. |
| P2 | Una cortesía separada por coma se interpretaba como una línea de producto vacía y descartaba la interpretación multiartículo. | Se ignoran únicamente cláusulas completas de cortesía conocidas; ninguna cantidad o línea desconocida se acepta por ese motivo. |
| P2 | Preguntar precio de un servicio llamado «Reserva de mesa» evitaba la resolución comercial. «Orientación general» activaba instrucciones en lugar de una reserva explícita. | Se distinguen consultas fácticas de inicio de operación, y la petición de orientación usa su construcción informativa, sin confundir ese sustantivo en un nombre de catálogo. |
| P2 | Una pregunta independiente de horario/ubicación leía productos y servicios. «Hola, buenas tardes» gastaba una interpretación. | Descarte temprano conservador en IsCandidate para información independiente, manteniendo referencias comerciales y preguntas mixtas. El saludo compuesto se resuelve localmente con las opciones autorizadas existentes. No se añadió caché global. |

En conflictos recuperables, OrderFlow comprueba primero la identidad original. Si el pedido ya existe, recupera su efecto/notificación; no crea una segunda solicitud. Si no existe, vuelve a comprobar licencia, relee catálogo, muestra diferencias verificadas y una cotización nueva, y guarda una confirmación vinculada a su huella mediante el CAS existente. Productos/sede no disponibles exigen corregir el borrador. Una falla de lectura o un conflicto CAS no se disfraza como recuperación comercial exitosa.

Se conservaron precios enteros minoritarios, monedas independientes, PendingReview, límites de IA, SourceMessageId, FIFO, persistencia durable y prohibición de reenvío de UnknownDelivery. Las solicitudes no cotizadas con productos inválidos conservan su validación de entrada anterior. No se cambió Reservation Engine, OutboundMessageService, InboundMessageWorker ni ContextRecoveryService; se inspeccionaron sus contratos y se conservaron las regresiones existentes.

## Archivos

Backend productivo:

- `backend/NexFlow.Domain/Exceptions/ConcurrencyException.cs` y nuevo `OrderCatalogChangedException.cs`.
- `backend/NexFlow.Infrastructure/Persistence/Firestore/FirestoreOrderRepository.cs`.
- `backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/Flows/OrderFlow.cs`.
- `backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/ConversationRecoveryService.cs`.
- `backend/NexFlow.Application/Features/Automation/Dialogue/Understanding/OrderMentionResolver.cs`.
- `backend/NexFlow.Application/Features/Automation/Dialogue/Policy/DialoguePolicy.cs`.

Pruebas/medición:

- Nuevos en `backend/NexFlow.Tests/Concierge/`: `CommerceCertificationFixture.cs`, `CommerceHardeningTests.cs`, `CommerceLocationRecoveryTests.cs`, `CommerceReadMetricsTests.cs`, `CommercialCertificationCorpus.json`, `CommercialCertificationTests.cs`, `ProviderCertificationTests.cs`.
- Nuevos en `backend/NexFlow.Tests/Integration/`: `CommerceCertificationIntegrationTests.cs`, `CommerceRedisIntegrationTests.cs`.
- Ajustados: `AssistantConversationTests.cs` (inyección controlada del intérprete real), `Concierge/CommerceSafetyTests.cs` y `Rc3Sprint4CommercialTests.cs` (excepción de catálogo específica, sin eliminar las comprobaciones posteriores), `Integration/IntegrationSafetyTests.cs` (rechazo de endpoints Redis de aplicación), `NexFlow.Tests.csproj` (corpus embebido).
- `backend/tools/ConciergeCertification/collect_evidence.py`: extracción reproducible de metadatos de evaluación, sin dependencias ni copia de StdOut completo.

CI/documentación:

- `.github/workflows/rc3-release-checks.yml` y `rc3-reservation-integration.yml`: separar contratos externos optativos del gate local, conservando cero omisiones/fallos; el gate de integración exige al menos 37 casos. No se habilitan proveedores ni se instala Redis en los workflows.
- Este informe y `NexFlow_CONCIERGE_PHASE6_PILOT.md`.
- Frontend: **sin cambios**.

Diff completo y lista absoluta: `phase6.patch` y `phase6-files.txt`, en el directorio de evidencias indicado abajo.

## Pruebas y build reales

| Comprobación | Resultado |
|---|---|
| `dotnet build backend/backend.slnx --no-restore --disable-build-servers -m:1` | **0 errores, 3 advertencias históricas**: dos CS8981 de las migraciones refactor y CS8602 en FirestoreCatalogRepository:228. No se modificaron esos archivos. |
| Suite explícita local con filtro `Category!=Integration&Category!=RedisIntegration&Category!=ProviderIntegration` | **1.195 aprobadas, 0 fallidas, 0 omitidas**. `phase6-certification-final.trx`. Se conservan las 1.065 aprobadas de la base y se añaden 130 casos locales. |
| Corpus nuevo | **100/100 conversaciones aprobadas, 375 turnos**, cinco perfiles: clínica, hotel, restaurante, panadería/comercio y licencia limitada. Intención, datos, continuidad, siguiente paso, permisos, hechos, ausencia de menú global en el borrador y efectos comprobados automáticamente. |
| Carreras críticas | SDK Firestore real sobre RPC en memoria: cambio tras lectura dentro de transacción provoca aborto/reintento y confirmación nueva; CAS y control humano impiden sobrescribir cambios nuevos. No equivale a Emulator. |
| Integraciones PostgreSQL + Emulator | **37 descubiertas, NO EJECUTADAS**: las 25 existentes y 12 nuevas. No hay servicios locales de prueba habilitados. |
| Redis real | **1 descubierta, NO EJECUTADA**. Cuenta de prueba local separada en puerto 16379 y DB 15; prueba invalidación fallida, copia vieja, reinicio, expiración y cancelación. |
| Groq/Gemini reales | **2 baterías descubiertas, NO EJECUTADAS**, ocho entradas sintéticas por proveedor. Sin autorización/credenciales de prueba; no hubo llamadas pagadas. |
| YAML | Sintaxis de ambos workflows validada con PyYAML ya disponible; no se instalaron dependencias. CI remoto no se ejecutó ni se consultó. |
| Diff | `git diff --check` y comprobación inversa del parche completo. No incluye cambios ajenos ni archivos generados de build. |

Se conservan las evidencias de fallos. La primera evaluación del corpus produjo **82/100**: 13 conversaciones expusieron los tres defectos lingüísticos anteriores; otras cinco expusieron una aserción incorrecta del harness que equiparaba carrito vacío con objetivo cerrado. Se corrigió esa aserción para comprobar por separado ambas condiciones: retirar todas las líneas no equivale a cancelar el objetivo. No se eliminaron escenarios ni se relajaron requisitos de hechos, permisos, cantidades, confirmación o ausencia de duplicados. Luego se añadieron comprobaciones explícitas de intención, licencia, consentimiento, efectos y respuestas informativas, y todos los diálogos pasaron.

Los handlers, orquestadores, pipeline, flujos, merger, compositor y repositorio de contexto son reales en el corpus. Catálogo/entitlements, motor de reservas/pedidos y transporte son dependencias controladas; las pruebas de seguridad de pedidos añaden su repositorio Firestore real sobre el SDK/RPC en memoria. La interrupción de horarios declara explícitamente el resultado semántico simulado; timeout/JSON inválido pasan por AiInterpreter real con router simulado. Esto certifica el contrato y continuidad, **no la calidad de Gemini/Groq en vivo**.

Las 12 integraciones nuevas preparan persistencia/legacy/aislamiento, precio, moneda y sede entre snapshot y transacción, producto desactivado, sede eliminada, monedas mixtas, notificación real en PostgreSQL tras commit Firestore y reinicio, doble origen, revocación/control humano, outbound durable ante timeout/fallo y FIFO real detrás de un head fallido. La modificación dentro de una transacción se ejecutó localmente en el transporte RPC; su evidencia real en Emulator sigue pendiente. En las integraciones de PRODUCT el coordinador de recursos de reserva es un guard estricto no invocado, no un coordinador PostgreSQL simulado que se presente como prueba de locks. Las 25 integraciones previas siguen certificando esos locks cuando se ejecuten en su entorno real.

## Medición de consultas y latencia

Comparación del mismo harness antes/después. P/S = llamadas GetProductsAsync/GetServicesAsync por turno medido. Son llamadas de aplicación, no lecturas facturables de Firestore.

| Caso | P/S antes → después | Intérprete antes → después | ms local antes → después |
|---|---|---|---|
| Saludo compuesto | 0/1 → 1/1 | 1 → 0 | 15,03 → 11,65 |
| Ubicación | 1/1 → 0/0 | 1 → 1 | 3,72 → 3,69 |
| Horarios | 1/1 → 0/0 | 1 → 1 | 4,45 → 9,82 |
| Producto | 1/1 → 1/1 | 0 → 0 | 3,12 → 3,10 |
| Servicio | 0/1 → 0/1 | 0 → 0 | 31,91 → 2,64 |
| Reserva activa | 0/0 → 0/0 | 0 → 0 | 34,45 → 79,75 |
| Pedido activo | 2/0 → 2/0 | 0 → 0 | 4,86 → 3,65 |
| Interrupción informativa | 0/0 → 0/0 | 0 → 0 | 6,29 → 2,67 |
| Ambigua | 1/1 → 1/1 | 1 → 1 | 25,40 → 6,18 |

El saludo nuevo construye el menú real autorizado, por eso consulta ambos tipos de offering; no se oculta esa lectura adicional. Las cuatro consultas evitadas de ubicación/horario y esa lectura necesaria dejan **12 → 9 llamadas de catálogo** en los nueve casos, y **4 → 3 invocaciones semánticas simuladas**. No se suprimieron las lecturas contextuales necesarias. Son muestras únicas locales, sensibles a JIT/contención: **no prueban una mejora de latencia de producción** ni un SLA. La reserva resultó más lenta en esta muestra; se debe medir en entorno representativo antes de fijar un presupuesto.

Corpus final: **15 invocaciones del intérprete simulado**, cero de redacción/cortesía. **2.140 RPC de lectura y 520 commits** del transporte Firestore en memoria durante los turnos (no los accesos de aserción); p50 **3,08 ms**, p95 **11,15 ms**. Los contadores por método, caso y turno están en `phase6-metrics.json` y `phase6-telemetry.ndjson`. No se atribuyen latencias de SaaS a estas cifras.

Métricas de infraestructura real, consumo CPU/RAM, cola/DeadLetters real, facturación Firestore, tokens/coste real de IA, disponibilidad/cuota/fallback de cuentas proveedor: **NO MEDIDO**. Los contratos optativos registran tokens reportados cuando existen, distinguen ausencia de uso reportado, cuota/timeout/configuración/JSON e incluyen un límite HTTP/turno. No agotan cuotas artificialmente ni contabilizan fallos de proveedor como errores del cliente. Fallback real entre proveedores sigue pendiente; se conserva su cobertura simulada existente.

## Criterios y riesgos pendientes

| Criterio | Estado |
|---|---|
| Operaciones sin licencia, duplicadas o sin confirmación válida | **0 observadas**; aserciones por turno y número de llamadas al motor en el corpus, más regresiones críticas locales. No es una afirmación sobre producción. |
| Precios/horas inventados; pérdida silenciosa del borrador; handoff no solicitado | **0 observados en los escenarios comprobados**. Stock físico no es una capacidad de NexFlow. |
| Resolución canónica ≥95% | **100% en el corpus automatizado nuevo**. No es un benchmark ciego ni revisión humana independiente. |
| Persistencia/idempotencia crítica 100% real | **PENDIENTE**: las integraciones optativas están compiladas/descubiertas, no aprobadas por ejecución. |
| Naturalidad, ausencia de repeticiones y utilidad para clientes reales | Comprobaciones estructurales locales OK; evaluación humana independiente **PENDIENTE**. No se declara esa evaluación como medida. |
| Proveedores reales y WhatsApp E2E | **PENDIENTE**, requieren autorización y cuentas sintéticas. |

- **P1 / bloqueo del piloto:** ejecutar sin omisiones las 37 integraciones PostgreSQL/Emulator y el contrato Redis real; confirmar especialmente nuevo campo de sede, reintentos, casos transaccionales y recovery. La disponibilidad local insuficiente no se convirtió en una prueba verde.
- **P1 / operación:** probar con cuentas autorizadas inbound→comprensión→efecto→outbound, revocación, handoff, PDF y UnknownDelivery; revisar los diálogos con responsable de cada negocio. Un health verde no sustituye estas evidencias.
- **P2:** medir tokens/coste, p95, lecturas y carga reales. El corpus usa 20 trayectorias distintas con cinco juegos de datos, no cien trayectorias universales ni un LLM entrenado/certificado con ellas. Mantener un holdout humano nuevo para el piloto.
- **P2 / recuperación:** FIFO y ORDER_COMMIT mantienen la identidad de origen. Una caída parcial/DeadLetter puede exigir recuperación supervisada del mismo origen; no liberar el borrador creando una compra nueva. No se prometió atomicidad distribuida PostgreSQL/Firestore ni autorización simultánea con una revocación posterior al último check.
- **P2 / despliegue:** esta entrega no incorpora un interruptor de diálogo nuevo por workspace. El piloto deberá usar un entorno aislado; no instalar globalmente la candidata en un despliegue compartido suponiendo un canary inexistente. Rollback conserva lectura de documentos, pero la versión anterior carece de las correcciones nuevas.

No queda un defecto productivo reproducido sin corrección dentro del alcance evaluado. Esto no cierra las condiciones operativas anteriores.

## Evidencia y reproducción

Directorio local de evidencias:
`C:\Users\deyvi\.codex\visualizations\2026\10\02\01a0fa60-4061-7571-be5e-8c8e0adbefd8\concierge-phase6`.

- `phase6-reproduction.trx`, `phase6-location-reproduction.trx`: fallos anteriores.
- `phase6-corpus-first.trx`, `phase6-corpus-certified.trx`: corpus antes/después.
- `phase6-reads-before.trx`, `phase6-certification-final.trx`: comparación y regresión final.
- `phase6-external-discovery.log`: descubrimiento de 40 contratos externos, **no ejecución**.
- `phase6-metrics.json`, `phase6-telemetry.ndjson`: únicamente metadatos de evaluación sin texto, teléfonos, nombres ni secretos. Workspaces y mensajes están representados por hashes. El corpus fuente es estático y sintético, separado de estas trazas.
- `phase6.patch`, `phase6-files.txt`: diff completo y rutas de todos los archivos.

Para regenerar métricas con Python estándar:

```powershell
python backend/tools/ConciergeCertification/collect_evidence.py --results <TRX-final> --baseline <TRX-lecturas-antes> --output <directorio-evidencias>
```

No se copiaron secretos ni se accedió a SaaS/Oracle. Continuar únicamente mediante revisión de esta entrega y el procedimiento de `NexFlow_CONCIERGE_PHASE6_PILOT.md`, una vez autorizadas y aprobadas sus condiciones. **Detención para auditoría; piloto y despliegue no autorizados.**
