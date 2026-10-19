# RC-3 / Sprint 0 — diagnóstico local del incidente

Fecha: 2026-10-19 (America/Lima). HEAD local: `67016bdb37c054711e57e0ac3c048b9999eedae6`.
Alcance: reproducción y diagnóstico; **ningún parche de aplicación**. Resultado: **NO-GO para despliegue / producción**.

## Alcance y procedencia de la evidencia

El incidente referido por el plan ocurrió el 18/10/2026 23:07–23:10 Perú, equivalente al 19/10/2026 04:07–04:10 UTC.
No se consultaron Oracle, Evolution, IA ni bases reales. No se recibieron aquí la imagen ejecutada, filas SQL, logs originales ni respuestas HTTP del incidente.
Los health previos y cuatro eventos PDF fallidos son antecedentes **referidos por el plan**, no evidencia operacional verificada en esta ejecución.
No hay ZIP en la raíz para comparar; no se consultó GitHub ni se presupone sincronización remota. El HEAD coincide con la referencia local esperada por el plan.
El único archivo local previo era el plan maestro, no versionado; se conservó intacto. No había diff staged ni unstaged de archivos versionados.

## Hallazgos y causa demostrable

| ID / prioridad | Clasificación | Evidencia local y límite de la conclusión |
|---|---|---|
| NF-01 / P1 | CONFIRMADO y REPRODUCIDO, con límite de motor | `InboundMessageRepository.cs:59`: una entrada Failed anterior impide seleccionar el siguiente turno del mismo workspace/consumidor. La prueba ejecuta el predicado del repositorio con SQLite temporal, función regexp_replace y sin el sufijo de locks PostgreSQL; otro tenant progresa y el turno posterior avanza después de completar el predecesor. **No certifica SKIP LOCKED ni concurrencia PostgreSQL**. |
| NF-02 / P1 | REPRODUCIDO | `AiInterpreter.cs:57`: un RpcException Unavailable leyendo el perfil escapa antes del try de interpretación. No se llama al router. El worker captura la excepción y registra Failed/reintento; combinarlo con NF-01 es un mecanismo de silencio temporal, no una causa probada del servidor. |
| NF-03 / P1 | REPRODUCIDO | `AiResponseOrchestrator.cs:72`: dos inbounds limitados distintos comparten source de respuesta por minuto. La prueba obtiene 7 respuestas lógicas únicas para 8 turnos: seis consultas admitidas y un único aviso compartido. El contador no supera seis llamadas IA. |
| NF-04 / P1 | REPRODUCIDO en Chromium | `useConversations.ts:165` combina errores; `InboxPage.tsx:40` sustituye toda la vista. Ante 503 simulado de messages o conversations, ambos caches retienen un elemento y el panel sano desaparece. **Se demuestra ocultamiento de UI, no borrado de datos**. |
| NF-05 / P2 | REPRODUCIDO | `ConversationRecoveryService.cs:105`: la frase exacta «Escojo la opción 1» no produce selección directa bajo un menú vigente; requiere IA/aclaración. No explica por sí sola el silencio de «Hola?». |
| NF-06 / observabilidad | CONFIRMADO EN CÓDIGO | `ProcessIncomingMessageCommandHandler.cs:124` acepta Sent, Failed y UnknownDelivery como resultados durables de turno. SQL Processed no acredita entrega. Ya existen avisos de fallo de outbound y protección contra reenvío; no duplicarlos ni cambiar Processed por una falsa confirmación. |
| Incidente real | PENDIENTE DE EVIDENCIA | No se demuestra cuál de estos mecanismos ocurrió ni una causa raíz única en Oracle. También falta descartar ausencia/rechazo de webhook, licencia, modo Human, excepciones de almacenamiento, transporte y error HTTP de la bandeja. |

Hay logging de errores y reintentos durables en el worker; no se afirma ausencia total de observabilidad.
El backoff inspeccionado espera 1, 2, 4 y 8 minutos antes de agotar cinco intentos y pasar a DeadLetter. No equivale a bloqueo infinito.

## Secuencia exacta, con dependencias simuladas sanas

Se usa el recuperador, orquestador y flujos reales de la fixture existente, estado Firestore simulado e interpretaciones/salidas simuladas; no un LLM real ni transporte WhatsApp real.

| Paso del caso de prueba | Resultado |
|---|---|
| Quiero informacion | Menú real de datos ficticios autorizados, numerado. |
| Como puedo hacer una reserva? | Con interpretación Reservation preparada: inicia borrador y solicita nombre. Ninguna reserva creada. |
| Hola? | Respuesta determinista, sin incrementar llamadas IA; conserva borrador. |
| Que productos tienen | Con interpretación ProductQuery preparada: información factual y continuación; conserva borrador. |
| Escojo la opción 1 | No selecciona directamente; con interpretación General preparada responde aclaración. |

Resultado del control: cinco respuestas simuladas, tres llamadas al intérprete simulado, cero reservas/pedidos.
**El silencio de cuatro mensajes no se reproduce con estas dependencias sanas.** La prueba no acredita las interpretaciones del modelo en producción.

## Validación ejecutada

| Comando | Resultado |
|---|---|
| `dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --verbosity minimal` antes de añadir reproducciones | 500 aprobadas, 0 fallidas, 0 omitidas; 9 s de ejecución de tests. |
| Mismo comando, con reproducciones | 505 totales: 502 aprobadas, 3 fallidas (NF-02/03/05), 0 omitidas; 9 s. |
| `dotnet build backend/backend.slnx --no-restore` | Correcto, 0 errores/advertencias en la última ejecución incremental. En la recompilación inicial: dos CS8981 de migraciones históricas y un CS8602 de FirestoreCatalogRepository:198, preexistentes. |
| `node --test tests/rc3-inbox-browser.test.mjs` en frontend | 3 totales: 1 control aprobado, 2 fallos de aceptación NF-04; 7,49 s. Red externa bloqueada. |
| `npm run build` en frontend | Correcto; advertencia de chunk JS de 709,26 kB (>500 kB). |
| `npm run lint` en frontend | Correcto, exit 0. |

Las cinco expectativas rojas se conservan deliberadamente como evidencia, **sin skips ni modificación de expectativas para ocultarlas**. Las 500 pruebas anteriores siguen pasando.
No se ejecutó npm ci: se utilizaron dependencias instaladas, sin cambios de versiones/lockfile.

## Evidencia operacional pendiente (sin ejecutar comandos)

Identificar imagen/digest y Compose efectivo que estaban activos en el intervalo; verificar si era este código.
Obtener salida agregada del script de solo lectura citado en el plan para 03:55–04:35 UTC: estados inbox, edad/retries/errores y dead letters.
Correlacionar en entorno privado los webhooks/HTTP, inbound, contexto/modo/licencia, outbound y evidencia Evolution del consumidor autorizado, sin exportar PII.
Obtener estados HTTP/errores de consultas de bandeja. Healthy antes del incidente no acredita workers, canal, permisos ni salud durante el incidente.
Los cuatro eventos de Outbox PDF no son la cola de inbounds ni los outbound Firestore; no atribuirles el silencio ni reinyectarlos sin evidencia.

## Propuesta exacta para Sprint 1 (NO implementada, NO autorizada aún)

1. `AiResponseOrchestrator`: usar request.MessageId como source del aviso limitado; mantener presupuesto atómico y replay idempotente separado. Convertir NF-03 en verde.
2. `AiInterpreter`: cubrir la lectura inicial del perfil con clasificación de fallos transitorios y resultado seguro; propagar cancelación del solicitante y errores de autorización/tenant. No inventar perfil, zona ni datos comerciales cuando Firestore está caído. Convertir NF-02 en verde y probar permisos/cancelación.
3. `InboundMessageWorker`/repositorio: conservar FIFO, claim y retries; hacer observable edad del predecesor y transición Failed/DeadLetter, aprovechando logging/notificaciones existentes y sin spam. No saltar operaciones transaccionales ni reenviar mensajes inciertos.
4. Handler/outbound: correlacionar resultado de turno e intento por WorkspaceId + inbound.MessageId sin datos sensibles; distinguir Processed de entrega. Reutilizar alertas/reconciliación existentes y probar que UnknownDelivery no vuelve a transportar.

NF-04 (UI) y NF-05 (parser) corresponden a **Sprint 2**, no al parche backend de Sprint 1.
Las pruebas están etiquetadas por sprint cuando aplica. No declarar G1/global verde mientras permanezcan reproducciones pendientes.
G0 permanece parcial por falta de evidencia del servidor; G1 no pasa por las expectativas nuevas rojas. GO solamente para continuar diagnóstico y proponer revisión; no desplegar.

## Adenda — Sprint 1 autorizado, 2026-10-19

La propuesta anterior describe el cierre histórico del Sprint 0. El usuario aprobó Sprint 0 y autorizó exclusivamente NF-01/02/03/06 sobre HEAD local `7c7f6d071378d7c38f224208c6172abf1892a7f5` (árbol inicialmente limpio).

| Hallazgo | Evidencia nueva / resultado |
|---|---|
| NF-03 CORREGIDO | Reproducción original verde: ocho IDs distintos producen ocho respuestas con seis llamadas IA; replay no duplica. La prueba histórica de cuota ahora exige diez respuestas a diez inbounds, sin elevar las seis llamadas IA. |
| NF-02 CORREGIDO | Fallo RPC del repositorio real simulado → clasificación tipada → ProviderUnavailable, sin IA. Unavailable/ResourceExhausted y DeadlineExceeded cubiertos, recuperación posterior con hora real de Lima/UTC. Permisos/conflictos/configuración crítica no se convierten en indisponibilidad; cancelación del solicitante se conserva. Borrador y opciones licenciadas permanecen. Dos pruebas aseguran conservar HTTP 503 al normalizar excepciones. |
| NF-01 OBSERVABLE, FIFO CONSERVADO | Diagnóstico read-only muestra cabeza demorada y siguiente inbound bloqueado por tenant, sin teléfono/payload. Muestreo 50/min, timeout 3 s, umbral 5 min; alerta con cooldown SQL existente de 1 h, fuera de la transacción principal. Pruebas verifican Failed→DeadLetter al quinto fallo, avance del siguiente, lease expirado/reinicio y rechazo de finalización con lease antiguo. Logging/alertas fallidos no alteran estado ni claims. |
| NF-06 TRAZABLE LOCALMENTE | Worker/handler/outbound correlacionados por WorkspaceId e InboundId interno mediante scopes. Pruebas con handler, repositorio Firestore y outbound reales, límite SQL simulado: preparación, intento, aceptación (no entrega), Sent/Failed/UnknownDelivery y modo Human; no PII/secretos en logs. Replay de resultado terminal conserva un solo transporte. Fallo de logging no altera aceptación durable. |

Se añadió solo una excepción de frontera de persistencia para evitar dependencia gRPC en Application, una proyección diagnóstica sin tabla y logging sobre los componentes existentes. No se sustituyeron mecanismos estables. Lista completa de archivos en `NexFlow_RC3_PROGRESS.md`.

Validación real:

- Build requerido `dotnet build backend/backend.slnx --no-restore`: correcto, 0 errores; último incremental sin advertencias. Recompilación previa con tres advertencias conocidas, fuera de alcance.
- Suite explícita `dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore`: **530 aprobadas / 1 fallida / 0 omitidas, total 531**. Fallo exclusivamente NF-05 (`Assert.NotNull` en la selección por frase). Exit 1 esperado y reportado.
- Regresión dirigida: **354 aprobadas / 0 fallidas / 0 omitidas**, filtro `FullyQualifiedName~Rc3Sprint1|FullyQualifiedName~AssistantRc1|FullyQualifiedName~AssistantConversationTests|FullyQualifiedName~AssistantDurabilityTests|FullyQualifiedName~AiResilienceTests|FullyQualifiedName~AssistantRedisRecoveryTests|FullyQualifiedName~Evolution|FullyQualifiedName~NF01|FullyQualifiedName~NF02|FullyQualifiedName~NF03|FullyQualifiedName~Incident_sequence` sobre el mismo proyecto, `--no-restore`.
- Navegador Sprint 0, `node --test tests/rc3-inbox-browser.test.mjs` en frontend: **1 aprobada / 2 fallidas NF-04 / 0 omitidas**. Caches mantienen una conversación/un mensaje; vista sana aún se oculta tras fallo parcial. Sin cambios de frontend ni parser; expectativas Sprint 2 intactas.

**Límites:** SQLite no valida locks PostgreSQL ni dos workers reales. El test de trazabilidad simula la frontera SQL; el test de lease recrea el repositorio sobre almacenamiento SQLite conservado. No equivale a detener procesos reales. La observabilidad es best effort y la notificación usa su fila SQL como cooldown durable; no es una nueva garantía de entrega de alertas. Verificar scopes en el colector y costo del diagnóstico bajo carga real.

**SPRINT 1 APROBABLE localmente.** La evidencia operacional de Oracle sigue pendiente y no existe causa única demostrada del incidente. NF-04/05 permanecen para Sprint 2, todavía NO autorizado. No se ejecutaron servicios externos, commit, push, despliegue, migraciones ni operaciones sobre datos reales.

## Adenda — Sprint 2 autorizado, 2026-10-19

HEAD local `785a4f3c481b23033b76da0fc7f58aba2965aafc`, inicialmente limpio. Sprint 1 aprobado; únicamente NF-04/05/07 autorizados.

- **NF-04 CORREGIDO:** las dos reproducciones originales pasan conservando sus aserciones. Un fallo parcial mantiene panel sano e historial cacheado autorizado, muestra aviso de desactualización y permite reintento específico. 401/403/READ revocado ocultan caché; recuperación conjunta espera ambas respuestas. Sin acciones con estado incierto ni reenvío automático. Nuevas pruebas verifican también móvil, respuesta tardía de otro workspace y ausencia de polling durante 16 segundos con pestaña oculta.
- **NF-05 CORREGIDO:** la reproducción original pasa sin modificar expectativas. Selección determinista estricta, menú vigente, fingerprint/página y datos/licencias comprobados. Frases numéricas ajenas no se extraen como índices. El control de cinco mensajes conserva sus cinco respuestas y cero operaciones comerciales; ahora requiere dos llamadas IA (antes tres), porque la selección inválida/sin picker recibe aclaración determinista. Solo ese conteo del control se actualizó, justificadamente.
- **NF-07 CONFIRMADO Y CORREGIDO LOCALMENTE:** AddMessage escribía actividad sin comparación. Ahora mensaje/máximo se confirman juntos bajo transacción, sin alterar timestamp ni TTL; confirmaciones y reescritura de conversación tampoco pueden retroceder actividad. Pruebas con SDK Firestore y transporte RPC simulado comprueban conflicto concurrente/reintento, mensaje atrasado, rechazo atómico del commit, aislamiento, eco/confirmación, normalización UTC y cursores con timestamps iguales. El fake se amplió con filtros/orden/cursor para no simular paginación con resultados prefabricados.

Resultados finales: backend **565/565**, Inbox Chromium **18/18**, regresión frontend de navegación/WhatsApp **41/41**; cero fallidas y omitidas. Builds backend/frontend y lint correctos; tres advertencias backend preexistentes y warning conocido de bundle (710,65 kB). Comandos e inventario completo en `NexFlow_RC3_PROGRESS.md`.

**SPRINT 2 APROBABLE localmente.** No prueba causa única del incidente de Oracle ni valida servicios reales. Comparación transaccional agrega una lectura de conversación por AddMessage (más reintentos en contención), sin nuevas estructuras; costo real Spark/índices/latencia pendiente. Límites de SQLite/Firestore simulado se conservan. No hubo llamadas a Oracle, Firestore real, Evolution ni IA; sin commit, push, migraciones o despliegue. **Sprint 3 NO autorizado.**
