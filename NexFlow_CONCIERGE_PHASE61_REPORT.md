# Concierge 2.0 — Fase 6.1: evidencia y dictamen

**Infraestructura: NO-GO (evidencia pendiente). Calidad conversacional: NO-GO. Piloto aislado: NO-GO.** No se autoriza producción. No hubo commit, push, despliegue, acceso a Oracle/SaaS ni llamadas pagadas.

## Identidad y alcance

HEAD inicial y final: `1ebf1e786edaf5b457d80698ca922381c356b0c3`, rama `main`, árbol inicialmente limpio. La candidata es ese SHA **más el diff local**, no un SHA nuevo publicado. Se leyeron AGENTS, informe/runbook de Fase 6 y las secciones aplicables del maestro Concierge. No se reimplementó la Fase 6.

Se modificó sólo la entrada conversacional de bienvenida y su certificación. Reservation Engine, Order Engine, persistencia productiva, contratos externos, esquemas, frontend, proveedores, Evolution, infraestructura productiva y secretos permanecen intactos.

## Infraestructura real y CI

La consulta autenticada de la colección de runs para el SHA base devolvió `total_count: 0`, incluyendo los workflows manuales. Evidencia: `ci-base-query.json`. **CI PENDIENTE; no hay Run ID, conclusión ni TRX remoto que aprobar para esta candidata.** No se confundió la consulta específica de runs de PR con la colección general.

- Workflow existente: https://github.com/saintdeyb321/NexFlow/actions/workflows/rc3-reservation-integration.yml
- Workflow Redis preparado localmente: https://github.com/saintdeyb321/NexFlow/actions/workflows/concierge-redis-integration.yml (no publicado; puede no existir aún en GitHub).

Las **37 integraciones existentes se descubrieron**, pero no se ejecutaron: 25 RC3 + 12 comerciales. El fixture real usa PostgreSQL temporal UUID y Firestore Emulator demo; no se sustituyó por mocks. El workflow mantiene el gate mínimo de 37, sin fallos ni omisiones. Se añadió únicamente identidad SHA/Run ID/attempt/job a los artefactos, sin exportar configuración.

| Cobertura existente revisada | Evidencia de código; ejecución real PENDIENTE |
|---|---|
| Persistencia/legacy/aislamiento | Structured_site_survives_restart_legacy_documents_and_workspace_isolation |
| Precio/moneda/sede y nueva confirmación | Price_changes / Currency_changes / Location_scope_changes_between_snapshot_and_transaction; Deleted_site_is_not_reused |
| Recuperación tras commit | Lost_notification_after_firestore_commit_recovers_after_restart_once_in_postgresql |
| Concurrencia/idempotencia | Same_source_and_concurrent_sources_cannot_duplicate_an_order; contratos RC3 de locks, xmin, Serializable y fencing |
| Revocación/handoff | Real_postgresql_module_revocation_and_human_takeover_do_not_create_orders |
| FIFO/DeadLetter | Real_postgresql_fifo_keeps_a_pending_turn_behind_failure_until_dead_letter |
| Entrega incierta/fallida | Unknown_delivery_and_failed_delivery_do_not_resend_after_service_restart |

El **nuevo job Redis separado** prepara Redis 7.4.2 desechable, endpoint exclusivo `127.0.0.1:16379`, DB 15 impuesta por el contrato existente, workspace generado, PostgreSQL/Emulator aislados y las dos variables explícitas de opt-in. Exige exactamente **1 ejecutada/aprobada, cero omisiones/fallos**, conserva TRX e identidad y no hace flush ni borrado masivo. El test ya comprueba invalidación fallida, copia antigua, recuperación, expiración y cancelación. No se modificó ese contrato.

No hay Redis, PostgreSQL, Java ni Docker de prueba disponibles en PATH local; no se instalaron ni arrancaron servicios. Los YAML no publicados no pueden ejecutarse remotamente conservando la prohibición de commit/push. Redis real: **PENDIENTE, no aprobado**. YAML y Python embebido validados localmente; eso no sustituye la ejecución del job.

## Bienvenida: reproducción y cambio localizado

Antes: **3/10 casos aprobados, 7 fallidos** (`phase61-welcome-before.trx`). Después: **10/10 requeridos**, más cuatro regresiones de repetición/replay, borradores y licencia: **14/14**. Cero interpretaciones IA en los diez primeros contactos.

`GreetingRecognizer` separa únicamente una cortesía inicial y conserva el contenido original restante para resolver entidades y evidencias. No cambia origen/idempotencia. Recovery da una bienvenida breve, pregunta qué se necesita y ofrece menú/asesor como alternativas. Una intención explícita continúa por el pipeline actual; ayuda sobre un pedido no inicia una compra. Un saludo durante un borrador continúa el paso y conserva los datos; un saludo repetido no vuelve a presentar la bienvenida. El backend conserva la autorización vigente y las opciones deterministas.

Las pruebas anteriores de menús ahora **piden menú expresamente**, manteniendo todas sus aserciones de licencia, paginación, recuperación y efectos. La secuencia saludo → consulta natural → selección añade esa petición explícita, sin debilitar presupuesto ni idempotencia. Dos pruebas anteriores fechadas el 20/10 dependían del reloj real; se fijó su reloj sintético antes de la fecha, conservando resultados UTC y todas las aserciones.

Transcripciones sintéticas completas de los diez casos requeridos, antes/después: `first-contact-before-after.md` en evidencias. Son ejemplos controlados solicitados, separados de telemetría operacional; no contienen teléfonos ni nombres de clientes.

## HOLDOUT congelado e independiente

Nuevo `QualityHoldout61.json`: **30 conversaciones / 84 turnos**, congelado antes del parche; SHA256 `3dfe02ac4cc1d6a067d1a875917615a4a3b3b73ab6d72c47ef2d63fe1f7e96e0`, comprobado sin cambios. Ningún primer turno repite literalmente los tres corpus anteriores. Hay cuatro expresiones breves compartidas de continuación/sede/franja/referencia, no conversaciones reutilizadas.

El handler, pipeline, política, flujos y compositor son reales; catálogo, entitlements, motores y transporte son sintéticos. Firestore usa SDK sobre RPC en memoria, **no Emulator**. Cinco turnos declaran semántica simulada; timeout/JSON inválido usan AiInterpreter real y router simulado. El resto de llamadas semánticas devuelve la respuesta controlada del fixture, no predicciones de Groq/Gemini. Por tanto esto mide cobertura y seguridad del contrato local, **no precisión de modelos reales**.

Resultado bruto: **24/30 aprobadas (80%)**, 77/86 invocaciones cumplen sus criterios (incluye dos turnos adicionales del segundo workspace y replays). Los seis fallos quedan visibles en la suite, sin Skip, relajación de aserciones ni entrenamiento contra el holdout:

| Caso | Hallazgo de evaluación |
|---|---|
| H04 | Cantidad de producto con cortesía informal al final no alcanza el borrador esperado. Deriva a semántica simulada; cobertura local insuficiente. |
| H12 | Mismo límite lingüístico en pedido con licencia CATALOG+ORDERS; consulta inicial y rechazo de reserva permanecen seguros. |
| H22 | Pedido informal inicial no queda como se esperaba; el subtotal posterior tampoco. JSON inválido sí se clasifica y no crea operaciones. |
| H16 | Petición informal de folleto no satisface los hechos esperados; la ruta local no consume la semántica preparada. No se certifica esa expresión como resuelta. |
| H27 | Petición natural de opciones no prepara la selección esperada; la ruta semántica es simulada. La palabra explícita menú sí está cubierta por regresiones aprobadas. |
| H06 | **Problema de expectativa del holdout**: exige una fecha única para «el jueves», pero el resolutor exige aclarar la semana. No se debe inventar fecha ni forzar la reserva. La expectativa congelada queda pendiente de revisión independiente, no se presenta como defecto del motor ni se cambia para obtener verde. |

No se aplicaron correcciones derivadas del holdout: dejó de ser evidencia independiente si se utiliza para ajustar esta candidata. Los fallos no demuestran pérdida durable, fuga entre tenants ni operaciones prematuras; tampoco autorizan GO de calidad.

Rúbrica preparada en `backend/tools/ConciergeCertification/quality61-human-review.csv`, 30 filas **sin puntuaciones**. Una persona ajena al parche debe evaluar: naturalidad, claridad y utilidad, de 1 (deficiente) a 3 (aceptable con fricción) a 5 (natural, inequívoca y útil); 2/4 son intermedios. Revisar intención, continuidad, pregunta pertinente, repetición, hechos, paso siguiente y ausencia de operación prematura. Usar código de evaluador y notas sin PII/cuerpos de mensajes. Cualquier problema de seguridad bloquea aprobación, independientemente de promedios. Umbral humano propuesto para auditoría: al menos 4 en cada dimensión y cero problemas críticos; **ninguna puntuación humana ha sido medida**.

## IA: contrato directo frente a router

`ProviderCertificationTests` sigue correcto y no se modifica: opt-in expreso, claves/modelos sólo `NEXFLOW_TEST_*`, sin fallback a appsettings; datos sintéticos, loggers nulos, timeout HTTP 8 s y deadline total 12 s; ocho entradas por proveedor, metadatos de JSON/intención/entidades/estado/tokens, sin imprimir mensajes, secretos ni cuerpos del proveedor. Los dos contratos reales quedaron **NO EJECUTADOS**, sin autorización/credenciales de prueba.

Comandos preparados, desde la raíz (no habilitar opt-in con credenciales productivas):

```powershell
# Contratos simulados: HTTP, cuota, timeout, JSON, entidades, cancelación y AiRouter real con proveedores simulados.
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --filter FullyQualifiedName~AiResilienceTests --logger 'trx;LogFileName=ai-simulated.trx'
# Sólo después de autorización adicional y claves/modelos de prueba por canal privado:
$env:NEXFLOW_CONCIERGE_REAL_AI_AUTHORIZED='1'
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --filter Category=ProviderIntegration --logger 'trx;LogFileName=ai-direct.trx'
```

Se ejecutaron **63/63 AiResilienceTests**: JSON/modelo/presupuesto de salida, HTTP 400/401/403/404/429/5xx, timeout HTTP sin reintento, esquema y evidencia de entidades, clasificación de errores y cancelación. AiRouter real con proveedores simulados hace un solo fallback ante 408/429/5xx/red, ninguno ante rechazo/configuración; cancelación del consumidor se propaga. Los tests de contratos directos **no usan AiRouter real** ni demuestran fallback real entre cuentas. Cuota agotada/timeout/HTTP/fallback en proveedores vivos: PENDIENTE de incidencia controlada autorizada, sin agotar cuota deliberadamente ni llamadas de esta ejecución.

## Métricas reproducibles

`collect_quality_evidence.py` lee únicamente metadatos permitidos del TRX final; no copia StdOut ni cuerpos. Su prueba de allowlist rechaza texto, teléfono, nombre y respuesta inyectados. La exportación humana y la telemetría son distintas.

```powershell
python backend/tools/ConciergeCertification/collect_quality_evidence.py --results '<evidencias>/phase61-full-final.trx' --output '<evidencias>'
```

Medición sintética final: **86 invocaciones procesadas**, 0 sin respuesta inesperada, 0 excepciones de handler; 2 supresiones humanas y dos replays sin nueva entrega. 75 turnos locales, 11 llamadas a intérprete simulado, 0 de composición. Latencia de handler local p50 **3.56 ms**, p95 **16.77 ms** (no SLO de servidor). 885 invocaciones de repositorios/dependencias observadas; SDK Firestore en memoria: **473 lecturas / 108 commits**, excluyendo lecturas diagnósticas del evaluador. 2 solicitudes humanas, 2 reservas y 1 pedido completados en dobles de motor; **82 entregas Sent sintéticas**, cuatro invocaciones sin entrega nueva. Desglose por método en `quality61-metrics.json`; no son documentos facturables ni cifras de infraestructura real.

DeadLetters/cola de workers: **NO MEDIDO en este fixture**. PostgreSQL/Emulator/Redis reales: PENDIENTE. Entregas WhatsApp, latencia de infraestructura, llamadas/tokens/coste de proveedores reales y notas humanas: **NO MEDIDO/PENDIENTE**, no cero inferido. Las marcas Sent son del transporte sintético y no confirmaciones de WhatsApp.

## Validación y artefactos

- `dotnet build backend/backend.slnx --no-restore --disable-build-servers -m:1`: **0 errores, 0 advertencias en build incremental final**. La compilación inicial de tests mostró tres advertencias históricas sin modificar: CS8981 x2 de refactor y CS8602 de FirestoreCatalogRepository:228.
- Suite completa explícita del csproj, sin filtros: **1.279 descubiertas, 1.239 ejecutadas, 1.233 aprobadas, 6 fallidas, 40 omitidas**. Las omitidas son 37 PostgreSQL/Emulator + 1 Redis + 2 baterías de proveedores. No se declararon aprobadas. Los seis fallos son sólo el holdout nuevo; las **1.195 anteriores pasan**, junto con los 14 primeros contactos y 24 holdout.
- Corpus anterior: **100/100**; primer contacto: **14/14**; IA simulada: **63/63**; regresiones dirigidas de bienvenida/menú/licencia/durabilidad: **90/90**. Son subconjuntos de la suite, no se suman entre sí.
- Se conservan TRX iniciales fallidos y finales, transcripciones, CI base, métricas, lista de archivos y diff completo. YAML/Python embebido y `git diff --check`: OK; parche completo comprobado en reversa.

Evidencias locales: `C:/Users/deyvi/.codex/visualizations/2026/10/02/01a0fa60-4061-7571-be5e-8c8e0adbefd8/concierge-phase61/`. El build fue ejecutado, no se infiere de un test verde. Frontend no cambió y no se recompiló.

## Archivos cambiados

Backend productivo (sólo bienvenida):
- `C:/Proyectos/NexFlow/backend/NexFlow.Application/Features/Automation/Dialogue/Understanding/GreetingRecognizer.cs` (nuevo).
- `C:/Proyectos/NexFlow/backend/NexFlow.Application/Features/Automation/Dialogue/Understanding/TurnUnderstandingPipeline.cs`.
- `C:/Proyectos/NexFlow/backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/ConversationRecoveryService.cs`.

Pruebas/medición:
- `C:/Proyectos/NexFlow/backend/NexFlow.Tests/AssistantConversationTests.cs`, `AssistantLicenseBudgetTests.cs`, `AssistantDurabilityTests.cs`, `AssistantRc1Tests.cs` (mismo directorio).
- `C:/Proyectos/NexFlow/backend/NexFlow.Tests/NexFlow.Tests.csproj`.
- Nuevos `C:/Proyectos/NexFlow/backend/NexFlow.Tests/Concierge/FirstContactTests.cs`, `QualityHoldout61.json`, `QualityHoldout61Tests.cs` (mismo directorio).
- Nuevos `C:/Proyectos/NexFlow/backend/tools/ConciergeCertification/collect_quality_evidence.py` y `quality61-human-review.csv` (mismo directorio).

CI/documentación:
- `C:/Proyectos/NexFlow/.github/workflows/rc3-reservation-integration.yml` (identidad del run en artefactos).
- `C:/Proyectos/NexFlow/.github/workflows/concierge-redis-integration.yml` (nuevo).
- `C:/Proyectos/NexFlow/NexFlow_CONCIERGE_PHASE61_REPORT.md` (este informe).
- Frontend: ninguno.

## Riesgos y decisión de cierre

No se demostró una violación crítica nueva de autorización, idempotencia, confirmación, aislamiento o UnknownDelivery. Sigue sin evidencia real la infraestructura crítica. La cobertura de lenguaje informal presenta brechas y el holdout necesita arbitraje de expectativas más revisión humana; la suite queda roja explícitamente y no se puede homologar todavía.

**NO-GO infraestructura / NO-GO calidad / NO-GO piloto aislado.** Antes de avanzar: auditar este diff, arbitrar los seis resultados; cualquier parche derivado de ellos requiere otro holdout inédito y no permite presentar éste como evaluación independiente; ejecutar ambos workflows publicados sobre el SHA candidato con TRX y cero omisiones, obtener revisión humana independiente y los contratos de cuentas IA autorizadas. Se detiene para auditoría, sin iniciar otra fase ni ejecutar el piloto.
