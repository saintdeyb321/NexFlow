# PROMPT PARA CODEX — Auditoría del MVP NexFlow WhatsApp (Sprint 0)

Actúa como **auditor principal de arquitectura SaaS, ASP.NET Core/.NET 10, PostgreSQL, Firestore, Redis, WhatsApp/Evolution y flujos conversacionales con IA**. Trabajamos sobre `C:\Proyectos\NexFlow`, repositorio `saintdeyb321/NexFlow`.

**Objetivo de ESTA ejecución: AUDITORÍA EXHAUSTIVA EN SOLO LECTURA. NO IMPLEMENTAR CAMBIOS AÚN.**

Contexto del servidor: el checkout certificado `306d7c8af41aacfd14c71d949da039f628beed95` pasó CI y smoke ARM64; la API Oracle está activa con la imagen local `nexflow-api:306d7c8-perms1` porque la imagen original tenía `/app/appsettings.json` modo 600, propietario `root:root`, inaccesible para UID 1654. El endpoint `/health/ready` ya reporta PostgreSQL, Redis y Firestore Healthy. Esa corrección de permisos no está implementada en el Dockerfile de la fuente base. **No cambies servidor/Compose ni borres el hotfix.** Verifica el estado y SHA local antes de basar conclusiones en ese commit.

## Casos REALES que deben guiar la auditoría (anonimiza cualquier persona o contacto)

- «Hola, busco información» y «¿de qué trata el negocio?» provocaron respuestas genéricas repetidas; después otra formulación sí devolvió el perfil de la clínica DEMO.
- El catálogo general/PDF tiene 5 servicios; la lista de servicios reservables para una sede mostró 4. **Evaluación odontológica SÍ estaba en posición 2**; el quinto aparentemente omitido fue «Restauración con resina simple». Distingue `IsActive`, `RequiresReservation`, duración, `LocationScope`, `LocationIds`, licencia, versión PDF y cachés. No presupongas que son mocks.
- «Quiero una evaluación odontologixa» no seleccionó un servicio; «la 4» acabó seleccionando «Limpieza dental», aunque el turno anterior hacía referencia a evaluación. Estudiar interpretación/alias/corrección/contradicción.
- «Pasado mañana a las 10 am» obtuvo confirmación **solo de disponibilidad**, pero «Vale» provocó repetir fecha y «a las 10» inició un loop de aclaración; «dije a las 10 am» finalmente avanzó.
- «¿Se puede hacer un pedido?» no arrancó el flujo. Revisar `ORDERS+CATALOG`, productos disponibles, rutas de intención `Order`, `Request` y fallback.
- El bot afirmó haber confirmado la reserva ficticia tras resumen y confirmación; verificar la persistencia real y unicidad en PostgreSQL antes de dar la operación por probada. Al preguntar por su estado, el bot derivó a asesor; esta restricción **es deliberada** en `ExistingReservationFlow` mientras no exista verificación de titularidad. NO eliminar ni sortear el control. Tras transferencia humana no hubo respuesta automática: verificar la cola y mecanismo del operador.

## Inspección obligatoria de arquitectura real

1. Leer `AGENTS.md`, documentos `docs/NEXFLOW_AGENT_RUNTIME_4_PLAN_MAESTRO.md`, `docs/agent-runtime/04_PEDIDOS_SOLICITUDES.md`, `07_PILOTO_RELEASE.md`, `08_CIERRE_AGENT_RUNTIME_4.md`, runbooks pertinentes y tests actuales.
2. Seguir flujo E2E `EvolutionWebhookController` -> Inbound -> Worker -> `AiResponseOrchestrator` -> `TurnUnderstandingPipeline`/`CommerceTurnResolver`/`AiInterpreter` -> `ConversationRecoveryService` -> `IBookingFlow`/`IOrderFlow`/`IChatFlow` -> `ReservationEngine`/repositorios -> Outbound, handoff y PDF.
3. Auditar `OfferingService`, `KnowledgeService`, `BusinessReadTools`, lectores de Firestore, repositorios SQL, contexto Redis/Firestore, DTOs, licencia `EntitlementService`, sedes, horarios y PDF `Current`.
4. Inventariar cada **respuesta fija** y clasificarla: respuesta determinista legítima, fallback honesto, mock/test-only, dato sintético del tenant, seed, hardcode indebido o conflicto real con la fuente de verdad. No concluir «mock» por ver un texto fijo. Separar ambiente local/demo de producción.
5. Verificar qué parte se delega a la IA: intención, entidades, herramientas propuestas, composición, límites y fallbacks; qué parte ejecuta operaciones transaccionales; dónde se pierde continuidad. No proponer un LLM que escriba bases de datos ni ejecute acciones sin autorización/consentimiento.
6. Revisar estado de reserva `COLLECT_LOCATION/SERVICE/DATE/TIME/NAME/CONFIRM`, selección numérica, tiempo referenciado `AvailabilityReference`, Date/Time, `ReadOnly`, guardias CAS, TTL de contexto, cambio de objetivo, reintentos, duplicación y `Human` takeover.
7. Revisar pedidos: catálogo PRODUCT y no SERVICE, licencias, borrador, cambios de cantidad, precio y sede, recotización, firma/fingerprint F7-01, `PendingReview`, notificación única y recuperación durable.
8. Auditar conectividad/permisos del Dockerfile para solucionar reproduciblemente el bug `root:root`/600/UID 1654 sin root runtime, `chmod 777`, ni hotfix manual permanente.
9. Revisar riesgo comercial: aislamiento multi-tenant, permisos y módulos, titularidad de reservas existentes, filtrado por sede, fuentes de verdad, falla de proveedor, cuotas IA, límites Firestore Spark, respaldo, delivery `UnknownDelivery`, mensajes históricos DeadLetter/outbox y observabilidad.

## Entregables (crear SOLO documentos nuevos bajo `docs/auditorias/`)

- `AUDITORIA_WHATSAPP_MVP_2026-10-26.md`: resumen ejecutivo, diagrama real, matriz por cada mensaje del caso, evidencia `archivo:línea`, diagnóstico confirmado vs hipótesis, riesgos P0/P1/P2, tests faltantes, defectos reproducibles, plan de corrección mínimo y bloqueadores de comercialización.
- `MATRIZ_DATOS_MODULOS_MVP.md`: origen (Firestore/PostgreSQL/Redis/configuración) de perfil, servicios, productos, `RequiresReservation`, sedes, horarios, disponibilidad, PDF, reservas, pedidos, licencias y conversaciones; permisos, filtros, invalidación y versión/cache.
- `PLAN_SPRINTS_CORRECCION_MVP.md`: sprints cortos ordenados por dependencias, con definición de terminado, pruebas obligatorias y despliegue seguro; S1 Dockerfile, S2 coherencia de catálogo, S3 continuidad/IA, S4 reservas/gestión privada, S5 pedidos, S6 canal y piloto. Puedes proponer otro orden si lo justificas con evidencia.
- `CASOS_REGRESION_WHATSAPP_MVP.md`: tests ROJOS propuestos, escenarios multitur, input, estado previo, esperado y riesgo. Separar unit/integración con Firestore Emulator + PG/Redis de E2E WhatsApp real. No simular las integraciones para proclamar GO comercial.

## Restricciones absolutas

- No modificar archivos productivos, tests existentes, migraciones, secretos, variables locales ni datos. Solo crear los cuatro documentos nuevos.
- No hacer commit, push, PR, deploy, Docker, migrator, migraciones, reinicios, borrados, colas, cambio de licencias, solicitudes reales a Gemini/Groq ni operaciones externas. Si ya existen documentos con esos nombres, no sobrescribirlos: reportar y usar sufijo.
- No usar datos reales de contacto del transcript en documentos o logs; anonimiza workspace, teléfono, nombre, IDs. No imprimir secretos ni contenido de `.env`.
- Si no tienes acceso a datos reales del workspace, indica **NO VERIFICADO** y describe una consulta read-only segura que realizará el operador; NO inventes el valor de `RequiresReservation`, ni el estado PDF.
- Preservar F7-01, CAS, idempotencia, transacciones, validación de evidencia, licencia y autorización de titularidad. No pedir que los elimine para simplificar UX.
- Distingue **infraestructura Healthy**, **función E2E WhatsApp probada parcialmente** y **MVP comercial aún sin certificar**.

## Formato de respuesta final

1. Commit y árbol local auditados (`git status` resumido), diferencias frente al SHA productivo.
2. Lista priorizada P0/P1/P2, con prueba de cada afirmación y exacta ubicación de código.
3. Explicación especialmente rigurosa de las 5 vs 4 ofertas y el bucle de fecha/hora, con observaciones verificadas y pendientes.
4. Arquitectura de herramientas actual vs mejoras mínimas: ¿qué usa IA, qué usa repositorios, qué es determinista, dónde se pierde el contexto?
5. Ruta crítica para llegar a un MVP vendible, con GO/NO-GO verificables.
6. Archivos Markdown creados y comandos de pruebas que ejecutarías **después de autorización**, sin ejecutarlos ahora.

Al terminar, detente y solicita autorización explícita para empezar **Sprint 1**, no implementes nada más.
