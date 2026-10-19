# NEXFLOW — Plan maestro de estabilización del MVP (RC-3)

**Estado:** PLAN DE TRABAJO, no certificación de producción  
**Fecha:** 18–19 de octubre de 2026 (Perú)  
**Origen:** auditoría técnica del incidente (`NexFlow_auditoria_incidente_2026-10-18.md`), auditoría externa (`auditoria nexflow.txt`), revisión directa del código extraído del ZIP `NexFlow-main(4).zip`, y diagnósticos de Oracle proporcionados por el propietario.  
**Código base:** la versión enviada en ZIP contiene correcciones RC-1.2; **el ZIP no incluye metadatos Git y no certifica por sí solo el SHA**. Último commit RC-1.2 previamente verificado en GitHub: `67016bdb37c054711e57e0ac3c048b9999eedae6`. Verificar `git rev-parse HEAD` en `C:\Proyectos\NexFlow` **antes de modificar nada**.  
**Decisión:** **NO-GO para producción comercial** hasta esclarecer el silencio por WhatsApp y las fallas de bandeja. **GO para análisis forense, parches acotados y staging aislado**. No afirmar causa única sin información del servidor.

---

## 0. Contrato innegociable de ingeniería

1. **NexFlow es un SaaS multi-tenant de comunicación por WhatsApp, no un POS, ERP, sistema de inventario, contabilidad ni cobro.** Productos y pedidos representan solicitudes comerciales; no añadir stock, checkout ni pagos.
2. **El backend determinista decide y persiste; la IA interpreta texto.** No inventar precio, horario, servicio, sede, cupo, disponibilidad o estado.
3. Preservar separación: **PostgreSQL** (transacciones, reservas, eventos durables), **Firestore** (documentos de negocio, conversaciones, pedidos y estado), **Redis** (caché prescindible), **Evolution** (transporte), **n8n/Gotenberg** (PDF).
4. Preservar `WorkspaceId` en todas las rutas; roles, licencias y capacidades verificadas en backend. No usar permisos del frontend como barrera única.
5. Preservar garantías de **idempotencia** de inbox, reservas, pedidos, documentos y outbound. `UnknownDelivery` **nunca se reenvía automáticamente** sin prueba del proveedor.
6. **No sustituir** el sistema actual de reintentos, locks o transacciones con refactors globales. Reparar un defecto comprobado, acompañarlo con pruebas y detenerse.
7. Repositorio de trabajo: `C:\Proyectos\NexFlow`. Codex opera localmente; **no debe hacer commit, push, cambiar secretos, actualizar dependencias ni desplegar** sin una orden separada. GitHub es respaldo y puede estar detrás del árbol local.
8. Los servicios de Oracle son **compartidos**: no reiniciar/recrear PostgreSQL, Redis, Evolution, n8n ni Caddy por un cambio de API. Nada de `docker compose down`, `docker prune`, reinicio global o migración automática.
9. La evidencia privada del incidente se consulta de forma **solo lectura** y agregada. No imprimir/exportar textos de clientes, teléfonos, IDs, webhooks, API keys, `.env` o logs crudos.
10. **No prometer respuesta universal en cualquier caída**: si Evolution no entrega el webhook o el canal está indisponible, el sistema debe evidenciarlo, reintentar de modo seguro o alertar al operador; no inventar una entrega.

### Etiquetas de evidencia

- **CONFIRMADO EN CÓDIGO:** ruta y comportamiento inspeccionados directamente en el ZIP.
- **CONDICIONAL/ESCENARIO REPRODUCIBLE:** mecanismo existente que *puede* originar el incidente, sin causalidad demostrada.
- **VERIFICAR:** preocupación razonable; primero escribir una prueba o localizar el código relevante.
- **YA EXISTE:** control presente; no implementarlo por segunda vez.
- **OPERACIONAL:** se confirma en Oracle, Evolution, n8n o navegador real, no en un test fake.

## 1. Contexto del incidente y qué sabemos

Secuencia observada el **18/10/2026 23:07–23:10 America/Lima** (= **19/10/2026 04:07–04:10 UTC**):

1. `Quiero informacion` → recibió menú real numerado y opción «ver más».
2. `Como puedo hacer una reserva?` → sin respuesta visible.
3. `Hola?` → sin respuesta visible.
4. `Que productos tienen` → sin respuesta visible.
5. `Escojo la opción 1` → sin respuesta visible.
6. El propietario no veía normalmente las respuestas en el módulo web de chats.

**Inferencia fuerte, no diagnóstico final:** `Hola?` debería entrar en el recuperador determinista tras normalización. Por tanto, la explicación «la IA no supo contestar» es insuficiente. Revisar **webhook → inbox SQL → worker FIFO → contexto Firestore → outbound Firestore → Evolution → bandeja React**.

**Evidencias operacionales previas:** Oracle Ubuntu ARM64 (2 vCPU visibles, ~11 GiB RAM, 97 GB disco); PostgreSQL 15, Redis, Caddy, Evolution 2.3.7, n8n, Gotenberg y API estaban levantados; `/health/ready` respondió Healthy para Redis/PostgreSQL/Firestore **antes** del incidente. Estos health checks no acreditan disponibilidad del canal ni de workers. En un inventario previo había **4 eventos `CATALOG_GENERATION_REQUESTED` fallidos**. Se construyó la imagen ARM64 `nexflow-api:67016bd`, pero construir no significa desplegar; **se desconoce qué imagen estaba ejecutando el servidor durante el incidente**.

### Mapa breve de estados / almacenamiento (NO confundir colas)

| Objeto | Almacén | Estados relevantes | Significado |
|---|---|---|---|
| `InboundMessages` | PostgreSQL | `0 Pending`, `1 Processing`, `2 Processed`, `3 Failed`, `4 DeadLetter` | Recepción y procesamiento del turno, **no entrega al teléfono** |
| `OutboxMessages` | PostgreSQL | `0 Pending`, `1 Processed`, `2 Failed`, `3 Processing` | Eventos para integraciones, especialmente n8n/PDF |
| `outboundMessages` | Firestore, por workspace | `Pending`, `Attempting`, `Sent`, `Failed`, `UnknownDelivery` | Envío de texto/documento por Evolution; `UnknownDelivery` requiere conciliación, **no reenvío ciego** |
| `conversations/.../messages` | Firestore | Historial reflejado y estado de transporte | Fuente del módulo de chats; comprobar consistencia con outbound |

### Árbol de diagnóstico obligatorio (FASE 0)

- **No ingresaron filas SQL en el intervalo:** Evolution (entrega de webhook, HTTP, 401/429), Caddy, identidad de instancia.
- **Ingresaron filas `Pending/Processing/Failed`:** lease, worker, excepciones Firestore/IA/SQL y bloqueo FIFO por conversación.
- **`DeadLetter`:** error persistente, manejo terminal y visibilidad operativa.
- **`Processed` sin mensaje visible en el móvil:** revisar Firestore outbound y Evolution, no solo SQL.
- **Outbound `Sent`, pero chat web desaparecido:** revisar consultas HTTP, caché React y Firestore; no asumir pérdida de datos.
- **`UnknownDelivery`:** mantener sin reenvío hasta evidencia de Evolution.
- **Resultados agregados ambiguos:** correlacionar de forma autorizada y **local** el hilo del incidente; no exportar PII.

**Primer artefacto operativo:** ejecutar en Oracle el script de solo lectura existente `nexflow_incidente_20261018_readonly.sh`, que reporta estados y categorías de errores entre `2026-10-19T03:55Z` y `04:35Z`. Preservar su salida antes de introducir cambios.

---

## 2. Conciliación crítica de ambas auditorías (decisiones basadas en código)

### 2.1. Defectos confirmados y candidatos a parche

| ID | Prio. | Hallazgo | Evidencia de código | Decisión |
|---|---|---|---|---|
| NF-01 | P1 | Una entrada `Failed` bloquea turnos más recientes del mismo consumidor mientras espera reintento | `InboundMessageRepository.GetAndLockNextMessagesAsync`, `NOT EXISTS` sobre `Status IN (0,1,3)` y cronograma de retry | **Diseño confirmado / causalidad por investigar**. Garantizar orden y agregar detección/alerta + recuperación sin saltar operaciones |
| NF-02 | P1 | Carga de perfil antes del `try` que protege errores de proveedor IA | `AiInterpreter.InterpretAsync`: `GetProfileAsync` antes de `try` | Reproducir fallo transitorio y clasificar recuperación; no esconder fallos críticos de infraestructura |
| NF-03 | P1 | Distintos mensajes limitados por cuota comparten clave de envío por minuto | `AiResponseOrchestrator.RespondAsync`: `assistant-limit:{conversation.Id}:{phone}:{yyyyMMddHHmm}` | Sustituir por identidad por `request.MessageId`, separar presupuesto de idempotencia |
| NF-04 | P1 | Error parcial oculta toda la pantalla de chats | `useConversations`: `isErrorConversations || isErrorMessages`; `InboxPage` retorna `ErrorState` global | Fallos por panel, retener datos válidos previos, reintentar sin vaciar la vista |
| NF-05 | P2 | Texto «Escojo la opción 1» no es parseado como selección directa | `ConversationRecoveryService.RecoverAsync` acepta número literal, ordinal o etiqueta exacta | Añadir gramática determinista estricta para menú activo con anticolisiones |
| NF-06 | P1/P2 | SQL `Processed` puede coexistir con outbound `Failed/UnknownDelivery` | `ProcessIncomingMessageCommandHandler` / `OutboundMessageService` | **No** tratar como duplicación de envío: añadir trazabilidad de entrega e incidencia visible; conservar seguridad de `UnknownDelivery` |
| NF-07 | P2 | Fecha `lastMessageAt` puede retroceder | `FirestoreConversationRepository` actualiza timestamp desde `message.Timestamp` sin comparación | Semántica monotónica transaccional y test de mensajes tardíos |
| NF-08 | P1 | La cola **volátil** está **en uso real** para invalidar PDFs tras cambios | `GenericTaskQueue` (`Channel`, `DropWrite`) + `CatalogController.QueueArtifactInvalidation` + `ServicesController.QueueArtifactInvalidation` | Sustituir esa operación crítica por invalidación durable/idempotente. No eliminar la cola hasta dejarla sin consumidores |
| NF-09 | P1/P2 | Borrado de **servicio** no comprueba reservas futuras, mientras sede sí | `ServicesController.DeleteService` vs `BusinessController.DeleteLocation` con `HasFutureConfirmedAtLocationAsync` | Añadir guarda de reservas futuras / desactivación segura con política funcional explícita |
| NF-10 | P2 | Upload de imágenes solo valida longitud y MIME declarados | `StorageController.UploadImage` | Añadir validación binaria real (JPG, PNG, WebP), extensión segura, tamaños y límites de streaming |
| NF-11 | P2 | La limpieza de tenant del worker solo muestra purge Firestore + borrado SQL | `TenantDeletionWorker` → `ITenantCleanupService` → `FirestoreTenantCleanupService` | Verificar otros callers/cleanup y añadir estado durable de limpieza Evolution/Cloudinary si realmente falta; no borrar raíz ni referencias antes de poder recuperar |
| NF-12 | P1 operacional | Hay cuatro `CATALOG_GENERATION_REQUESTED` fallidos históricos | Reportes reales Oracle; la causa de esos fallos no está demostrada | Clasificar la causa y test de PDF nuevo, sin reinyectar eventos antiguos a ciegas |
| NF-13 | P2 | Webhook legacy por defecto habilitado | `EvolutionWebhookController` acepta shared key si `AllowLegacyWebhookKey` no está definida | Verificar por instancia en staging, entonces fijar `Evolution__AllowLegacyWebhookKey=false` de forma coordinada |
| NF-14 | P2 operacional | Falta validar cuotas, latencia y contención de Firestore/IA con cargas reales | Presupuesto Firestore por workspace/minuto y test fake de carga RC-1 | Instrumentar y medir; optimizar **solo** ante presión real / evidencia |

### 2.2. Puntos de la auditoría externa que YA están implementados: **no duplicar**

1. **Handoff vs IA:** `AiResponseOrchestrator` comprueba modo/versión tras interpretar, y `FirestoreConversationRepository.StartOutboundAsync` comprueba `Human` en la transacción antes de iniciar el transporte. Hay que **probar la carrera**, no crear otro lock sin demostrar fallo. Lo ya transmitido a Evolution no puede deshacerse.
2. **Autenticación criptográfica:** `EvolutionWebhookController.KeyEquals` usa `CryptographicOperations.FixedTimeEquals` y clave derivada por instancia HMAC. `N8nWebhookController` verifica **HMAC + timestamp** y estado/generación de artefacto. Mantener; probar configuración, replay, aislamiento y transiciones.
3. **KPIs de Firestore:** `DashboardService` llama a repositorios cuyos conteos de catálogo, solicitudes y conversaciones ya emplean **`Count().GetSnapshotAsync`**. No cambiar a `Count` algo que ya es `Count`; buscar N+1 o carga real si existe.
4. **Polling de inbox en segundo plano:** `useConversations` ya usa `usePageVisible()` y `refetchInterval: visible ? 15000 : false`. No «añadir» esa protección; medir costo y revisar comportamiento de reanudación si es necesario.
5. **QR y estado Evolution:** `EvolutionConnectionService.GetStatusAsync(workspaceId, refresh, ...)` y `BusinessController` admiten refrescar estado. Verificar que la UI use `refresh`, timeouts y límites antes de declarar problema de «ghost connection».
6. **Categorías con hijos:** `CatalogController.DeleteCategory` impide eliminar una categoría con ítems. No introducir un `WriteBatch` para una eliminación que el negocio bloquea; revisar únicamente reasignaciones reales.
7. **Borrado de sedes:** el controlador ya impide borrar sedes con reservas futuras confirmadas. El hueco comprobado está en **servicios**.
8. **Precios históricos de pedidos:** `OrderItemRecord` ya tiene `ProductName`, `UnitPriceMinorUnits`, `Currency`; `FirestoreOrderRepository` conserva esos campos. Verificar integridad y cálculos, pero no rediseñar por afirmación genérica.
9. **Rate limiting HTTP:** `Program.cs` ya configura `PartitionedRateLimiter`; ajustar límites por evidencia, no añadir una segunda capa indiscriminada.
10. **Separación del frontend por tenant:** `queryPersistence.ts` ya usa namespaces por usuario/workspace/permisos y borra query cache al cambiar de identidad. Investigar **revocación asíncrona** específica, no declarar fuga sin prueba.

### 2.3. Riesgos plausibles que requieren prueba específica, NO migración masiva

- **Consistencia PostgreSQL ↔ Firestore:** no hay transacción distribuida (esperable en arquitectura híbrida). No «solucionar» con 2PC improvisado ni migrar todas las entidades. Trazar workflows críticos que cruzan ambos almacenes; elegir outbox durable, idempotencia y reconciliación por caso.
- **Provisión de nuevos tenants:** verificar inicialización idempotente de documentos de perfil/horarios y comportamiento ante ausencia. No crear dos fuentes de verdad.
- **Reservas manuales vs bot:** comparar validadores de horario/servicio/sede y escenarios de concurrencia `xmin`. No introducir `allowOutsideBusinessHours` sin requisito de negocio.
- **Reconciliación Evolution/QR:** probar desconexión móvil sin callback, límites de polling, estado stale; ajustar solo si hay fallo.
- **Notificaciones de pedidos/solicitudes:** confirmar que un fallo tras escritura Firestore se concilia sin duplicación ni pérdida de alerta.
- **Costo de conocimiento e IA:** medir lecturas por turno y cache hit; planear cache por workspace/versionado + invalidación si el costo realmente lo amerita.
- **Entidades/migraciones de catálogo históricas:** las clases y las migraciones NO demuestran una segunda fuente activa. Verificar mapeos EF realmente registrados antes de borrar tablas o editar migraciones.
- **Archivos Firebase:** `frontend/firebase.json` y `firebase.nexflow.json` contienen configuración de hosting equivalente en el ZIP (aunque uno tiene BOM); la existencia doble no demuestra configuración funcional contradictoria. Documentar fuente oficial de despliegue.
- **Caché `.firebase/hosting.ZGlzdA.cache`:** aparece en el ZIP; comprobar si está trackeada en Git (`git ls-files`) antes de retirarla del índice. No borrar activos de producción.

**Nota sobre la auditoría externa:** declara que no pudo descomprimir los cuerpos de muchos `.cs`/`.tsx`. Por tanto, sus propuestas «P0» basadas exclusivamente en nombres de archivos **no prueban** fallos. La revisión actual **sí inspeccionó cuerpos de código extraídos**, por lo que prevalecen las verificaciones concretas de esta tabla.

---

## 3. Plan de ejecución por sprints — evitar otra cadena interminable de auditorías

**Regla:** un sprint = defectos delimitados + prueba de reproducción que falla antes + cambio mínimo + suite completa + reporte GO/NO-GO. No avanzar si quedan P1 sin resolver; los hallazgos P3 no bloquean staging. Codex trabaja sobre código **local**, no sobre documentos de roadmap viejos.

### SPRINT 0 — Congelamiento, incidente y reproducción (PRIMERO; sin desplegar)

**Objetivo:** decidir qué pasó realmente a las 23:07–23:10 y fijar línea base reproducible.

Acciones:

- [ ] En local: `git status --short`, `git rev-parse HEAD`, `git diff --stat`, verificar que el ZIP representa el mismo árbol o explicar divergencias.
- [ ] En Oracle: `docker inspect nexflow_api --format '{{.Config.Image}} {{.Image}} {{.State.StartedAt}}'` para saber si se ejecutaba `e1cb6b0`, `67016bd` u otra imagen.
- [ ] Ejecutar `nexflow_incidente_20261018_readonly.sh`; conservar diagnósticos agregados SQL + contenedor en las horas UTC exactas.
- [ ] Si es necesario, correlacionar autorizadamente inbound/outbound del **cliente de pruebas** solo en entorno privado; nunca copiar conversaciones ni credenciales a este plan.
- [ ] Determinar si hubo 401/429 webhook, errores del worker, `Failed/DeadLetter`, Firestore, `UnknownDelivery`, handoff o fallo de UI.
- [ ] Redactar **1 prueba de regresión del incidente** en backend y **1 frontend** que hagan visible la falla antes de modificar código.
- [ ] Registrar causa como CONFIRMADA, PROBABLE o NO DEMOSTRADA; no convertir conjetura en hecho.

**Salida:** `INCIDENTE_CAUSA_RAIZ.md` breve y anonimizado, matriz paso→evidencia→resultado, versión real instalada. **No repetir una auditoría de todo el repo.**

### SPRINT 1 — WhatsApp sin silencios inexplicados (NF-01, 02, 03, 06)

**Objetivo:** que cada inbound tenga un resultado **observable** (respuesta persistida/intentada, transferencia humana, rechazo de negocio explícito, retry o dead letter con alerta) y que fallos transitorios no bloqueen indefinidamente a ese consumidor.

**Cambios acotados:**

1. `AiResponseOrchestrator`: eliminar identidad `assistant-limit` por ventana para envíos; usar `request.MessageId`, mantener el contador separado y replay idempotente del mismo evento.
2. `AiInterpreter`: analizar fallos de perfil/zonas/configuración **antes del `try`**. Convertir indisponibilidades recuperables en `ProviderUnavailable`/fallback verificable donde sea seguro; cancelar correctamente; propagar violaciones de tenant/autorización y fallos persistentes sin producir respuestas inventadas.
3. Inbox SQL / worker: preservar FIFO, single-claim y semántica de retry. Añadir alerta y seguimiento a **edad del mensaje bloqueante**, `Failed`, `DeadLetter` y respuestas inexistentes. Si hay mecanismo seguro de avance tras dead letter, especificar transición y pruebas; **no saltar automáticamente una orden/reserva transaccional incompleta**.
4. Guardar por `WorkspaceId + inbound.MessageId`: resultado de turno, intento outbound y causa de terminación en observabilidad (sin contenido sensible). Relacionar SQL `Processed` con outbound Firestore, nunca interpretarlo como confirmación de entrega.
5. Revisar tratamiento de `Failed` 4xx y `UnknownDelivery`; alertas/reconciliación no deben producir doble POST a Evolution.
6. Asegurar límites de timeout y errores de dependencias que no engullan `OperationCanceledException` del solicitante.

**Pruebas obligatorias:**

- [ ] La secuencia exacta de cinco mensajes del incidente funciona (incluye `Hola?` sin gasto IA).
- [ ] Dos IDs de inbound distintos limitados en el mismo minuto producen dos outbounds distintos; replay de ID antiguo no duplica.
- [ ] Perfil Firestore inaccesible antes de interpretar → resultado claro, retry o fallback seguro; no cola silenciosa eterna.
- [ ] Una entrada fallida seguida de `Hola?` genera alerta identificable y política de desbloqueo documentada sin invertir efectos de negocio.
- [ ] `UnknownDelivery` nunca genera segundo envío automático.
- [ ] Concurrencia de dos workers y dos workspaces con el mismo número, sin cruce de datos ni duplicaciones.
- [ ] Handoff ocurre mientras el LLM responde: la IA se suprime antes de transportar si el cambio ganó la carrera.

**DoD:** sin P1 pendiente de este tramo; mensajes de prueba tienen trazabilidad de `inbound→outbound`; pruebas nuevas y suite previa pasan.

### SPRINT 2 — Menús y bandeja confiables (NF-04, 05, 07)

**Objetivo:** un consumidor no debe adivinar comandos; el panel no debe simular pérdida total de mensajes ante un error parcial.

**Backend:**

- `ConversationRecoveryService`: identificar expresiones inequívocas (`opción 1`, `escojo la opción 1`, `elijo 1`, `número uno`, `la primera`), con **menú activo**, misma versión/fingerprint, rango válido, módulos licenciados y opción aún disponible. No interpretar números de DNI, fechas, precios o cantidades como selección aislada.
- `FirestoreConversationRepository`: hacer `lastMessageAt` **monotónico** al escribir mensajes fuera de orden, sin cambiar `message.Timestamp`, paginación ni cursores.

**Frontend:**

- Separar `isErrorConversations` e `isErrorMessages`; conservar el panel no afectado y la última data válida. Señal de «datos desactualizados» cuando se reutiliza caché; reintento **local** a la consulta fallida.
- Diferenciar mensajes `Pending`, `Attempting`, `Sent`, `Failed`, `UnknownDelivery`; no mostrar un indicador de «entregado» inexistente.
- Mantener sesión, `WorkspaceId`, permisos y URL del chat; recuperación en navegador tras 500/timeout/403.

**Pruebas obligatorias:**

- [ ] Menú → «Escojo la opción 1» con producto realmente autorizado.
- [ ] Menú viejo y acción 1 tras cambio de sede/servicio → refrescar lista, no transaccionar con opción obsoleta.
- [ ] «Reservar a las 10» con fecha activa → no interpretar 10 como menú si no es respuesta inequívoca.
- [ ] `/messages` 500 y `/conversations` 200 → listado sigue disponible.
- [ ] `/conversations` 500 y `/messages` 200 → historial anterior no desaparece si existe.
- [ ] Mensaje antiguo o eco tardío → orden cronológico del chat no retrocede.

**DoD:** la reproducción del incidente es navegable por un usuario, la bandeja representa errores sin borrar datos, y el polling continúa pausándose en pestaña oculta.

### SPRINT 3 — Consistencia de catálogos/PDF y tareas durables (NF-08, 12)

**Objetivo:** cambiar un producto/servicio nunca debe dejar por accidente un PDF «vigente» cuando el catálogo cambió, aunque la API se reinicie; distinguir los cuatro fallos históricos de una generación nueva.

- Auditar específicamente `CatalogController.QueueArtifactInvalidation`, `ServicesController.QueueArtifactInvalidation` y `GenericTaskQueue`. Hoy esa operación se encola en `Channel` in-memory con `DropWrite` y no se espera la operación en los controladores.
- Reemplazar esa ruta **crítica** por un mecanismo durable e idempotente compatible con el diseño: transacción/sellado Firestore cuando aplique, evento PostgreSQL outbox con reconciliación o invalidación síncrona si se justifica. **Elegir una**, no duplicar tres sistemas.
- Si la invalidación es síncrona, definir qué responde la API cuando el guardado del ítem funciona pero la invalidación falla; nunca declarar consistencia falsa.
- Mantener la prevención existente de PDFs duplicados por hash y recuperación de `CATALOG_GENERATION_REQUESTED`.
- Diagnosticar los **4 eventos Outbox fallidos** por causa/código de error **sin reejecutarlos**. Verificar alcance de permisos HMAC, disponibilidad n8n/Gotenberg/Cloudinary.
- Confirmar que el artefacto final no queda `Ready` si fue rechazado o cambió la fuente; preservar fallback textual de documento fallido y `UnknownDelivery`.

**Pruebas:**

- [ ] Mutación de producto + caída de API inmediatamente → artefacto antiguo no puede consumirse como fresco.
- [ ] Mutación servicio + Firestore transitorio → reparación/reconciliación sin duplicados.
- [ ] 2 callbacks n8n iguales + callback tardío de generación antigua → una sola publicación vigente.
- [ ] PDF productos y PDF servicios por separado en staging, tamaños/datos correctos y no contaminación tenant.
- [ ] Error Gotenberg/Cloudinary/timeout → estado durable observable, sin generación infinita.

**DoD:** sin invalidación crítica dependiente de una cola volátil; historial histórico no se reenvía sin autorización.

### SPRINT 4 — Integridad cruzada y reglas de negocio (NF-09; mejoras condicionadas)

**Objetivo:** proteger reservas y solicitudes comerciales a través de PostgreSQL y Firestore, sin una reescritura de persistencia híbrida.

- **Servicios:** impedir el borrado físico de un servicio referenciado por reservas futuras vigentes, o acordar un modelo de `IsActive=false` / soft-delete. Añadir repositorio/consulta por `WorkspaceId + ServiceId` y transacciones/rechecks según concurrencia. Revisar políticas de cancelación.
- **Sedes:** preservar el bloqueo existente para futuras reservas; validar el caso concurrente borrar/reservar, que puede necesitar lock de ciclo de vida / estrategia transaccional coordinada. No duplicar código de sede sin razón.
- **Reservas:** validar historial incluso si cambian nombres de servicios/sedes. Snapshot de etiquetas en `Reservation` es una **mejora de modelo**, con migración y DTOs sólo si hay fallo funcional de historial; no exigirla como parche urgente del chat.
- **Pedidos:** `OrderItemRecord` ya guarda producto/precio/currency; probar si el repositorio asigna precios correctos al confirmar, y que modificaciones futuras no alteren pedidos guardados.
- **Solicitudes y notificaciones:** inducir fallo posterior a escribir Firestore, validar retries/idempotencia de notificadores y definir reconciliación. No adoptar 2PC; escoger saga/outbox según operación concreta.
- **Reserva web/bot:** equivalencia de validación de disponibilidad, zona horaria, `xmin`, 409 y refresco UI.

**DoD:** sin borrado que invalide una reserva futura vigente; integridad y datos históricos respaldados con pruebas. **Migraciones exclusivamente si son necesarias, reversibles y aprobadas en otro paso.**

### SPRINT 5 — Seguridad e integración (NF-10, 11, 13)

**Objetivo:** cerrar brechas reales antes de habilitar múltiples clientes.

1. **Evolution:** ya existe HMAC por instancia y comparación constante. Probar webhook válido en cada instancia y rechazo de firma de otra, replay, cambio de propietario, origen y `fromMe`; **después** desactivar clave legacy en ENV **mediante operación separada** (`Evolution__AllowLegacyWebhookKey=false`) y verificar recepción real. Nunca cambiar la bandera antes de corregir configuraciones del proveedor.
2. **n8n:** ya existe HMAC+timestamp. Prueba de replay, scope/workspace incorrecto, callback duplicado, generación obsoleta y fuente alterada. Solo corregir si falla una de ellas.
3. **Uploads:** validar *magic bytes* reales JPG/PNG/WebP, tamaño y tipo, no confiar en `Content-Type`/extensión; proteger memoria y streaming; tests archivo poliglota/corrupto.
4. **Borrado de tenant:** verificar si `ITenantCleanupService` ya abarca recursos externos mediante algún otro orquestador. Si no, diseñar job durable con pasos idempotentes de Evolution, Cloudinary, Firestore, caché y eliminación SQL (orden preciso según dependencias); conservar tracking de fallos/leases y no borrar a ciegas recursos compartidos.
5. **Pérdida de permisos:** escenarios de workspace suspendido o módulo revocado en dos pestañas; 401/403/402 correctos, revalidación `/me` y purga de datos del tenant previo según el caso. No limpiar caché en todo 403 genérico sin distinguir semántica.
6. **Rate limiting existente:** validar con llamadas reales bajo límite; no reimplementar si funciona.

**DoD:** claves heredadas desactivadas cuando corresponda, uploads seguros y recursos por workspace aislados. Si el borrado de tenants se habilita en MVP, la limpieza externa es obligatoria antes de usarlo.

### SPRINT 6 — Medición, rendimiento y operabilidad (NF-14)

**Objetivo:** cuantificar en lugar de reestructurar por especulación.

- Medir latencias p50/p95/p99 (webhook ack, worker, Firestore contexto, interpretación Groq/Gemini, envío Evolution y UI).
- Métricas agregadas `Pending/Processing/Failed/DeadLetter` por workspace (sin teléfono/texto), edad máxima de primer evento bloqueante y reintentos. Alertas mínimas cuando crece `oldest pending` o `UnknownDelivery`.
- Medir lecturas y escrituras Firestore por turno y costo por workspace; observar documento caliente del presupuesto por minuto. **No** afirmar tolerancia a cientos de usuarios por test `InMemoryFirestore`.
- Verificar que agregados dashboard siguen usando Count nativo; cachear sólo si el valor/costo lo requiere.
- Medir snapshot de negocio; cache versionado por workspace solo si es cuello de botella y con invalidación por cambios.
- Probar Evolution disconnected, n8n/gotenberg down, Redis down, Firestore lento, proveedor IA caído; el worker vuelve a progresar y las alertas aparecen.
- En Oracle: observar 2 vCPU, consumo de contenedores, memoria 11 GiB y saturación PDF/n8n con tráfico de prueba autorizado.

**DoD:** presupuesto claro de latencia, errores y límites por cliente; funcionamiento degradado medido, no asumido.

### SPRINT 7 — Higiene del repositorio y automatización (no bloquear el hotfix)

- Validar `git ls-files frontend/.firebase/hosting.ZGlzdA.cache` y añadir a `.gitignore` si está trackeado; retirarlo del índice en cambio separado, sin borrar despliegue.
- Documentar el comando único de Firebase Hosting; `firebase.json` y `firebase.nexflow.json` son equivalentes en el ZIP y no requieren reescritura urgente.
- No eliminar entidades ni migraciones SQL históricas porque parezcan «duplicadas»: inspeccionar mapeos activos; preservar datos y upgrades.
- Agregar CI que **ejecute explícitamente `NexFlow.Tests.csproj`**, no solo `backend.slnx`.
- Añadir test de regresión permanente del incidente y smoke tests backend/frontend en pipeline.
- Crear runbook de imagen ARM64, versión exacta, Compose efectivo, health, rollback y correlación de eventos.

**DoD:** pasos reproducibles por otra persona sin depender de copiar comandos incompletos de conversaciones.

---

## 4. Matriz de escenarios de aceptación (obligatoria antes de producción)

**WhatsApp y conversación**

- [ ] W01. Menú → reserva explicada → `Hola?` → productos → `Escojo la opción 1`, manteniendo coherencia del contexto.
- [ ] W02. Menú → `ver más` → `menú`, sin duplicación, límites de 5 opciones ni selección obsoleta.
- [ ] W03. Mensajes en ráfaga (1–5 s) y dos workers, orden por `WorkspaceId + ConsumerPhone` sin perder drafts.
- [ ] W04. Reservar hasta confirmación; doble webhook mismo ID → una reserva y una respuesta lógica.
- [ ] W05. Pedido sin inventar precios ni stock; reenvío webhook → un pedido.
- [ ] W06. Interrupción informativa con borrador activo, vuelve al paso correcto sin borrar estado.
- [ ] W07. Handoff simultáneo, bot no invade el canal humano después de takeover.
- [ ] W08. Groq falla, Gemini falla, timeout, límite por usuario y límite por workspace: respuesta controlada/alerta o espera transparente.
- [ ] W09. Redis cae y retorna, Firestore manda, sin resurrección de borradores eliminados.
- [ ] W10. Evolution 4xx vs timeout ambiguo, `Failed` vs `UnknownDelivery`, **sin auto-reenvío incierto**.
- [ ] W11. Webhook firma de otra instancia, `fromMe`, duplicados, payload no soportado, sin efectos cruzados.
- [ ] W12. Dos workspaces con mismo consumidor; ni contexto, ni catálogo, ni chat, ni respuesta cruzados.

**Bandeja y UI**

- [ ] U01. Error de `/messages` conserva lista; error de `/conversations` conserva mensajes previos permitidos.
- [ ] U02. Estado `UnknownDelivery` es visible, claro y no declarado entregado.
- [ ] U03. Mensaje tardío no cambia orden de bandeja en forma errónea.
- [ ] U04. Cambio de rol/licencia/tenant invalida solo datos apropiados; no filtra información.
- [ ] U05. Error 409 por reserva concurrente refresca datos y explica conflicto.

**PDF y almacenamiento**

- [ ] P01. PDF PRODUCT y SERVICE separado con datos reales; imagen/estilo y archivo accesible.
- [ ] P02. Editar productos/servicios invalida versión anterior incluso con reinicio API.
- [ ] P03. Callback firmado repetido/obsoleto no publica documentos equivocados.
- [ ] P04. Outbox falla y se recupera sin reprocesar cuatro eventos antiguos inadvertidamente.
- [ ] P05. Rechazo Cloudinary, n8n y Gotenberg mantiene `Failed`, alerta y fallback coherente.

**Datos/seguridad/despliegue**

- [ ] D01. No se puede borrar servicio con reserva futura activa.
- [ ] D02. Precio/nombre histórico del pedido no cambia al editar producto.
- [ ] D03. Datos entre Firestore y PostgreSQL se reparan en reintentos con idempotencia.
- [ ] D04. Evolution clave derivada por instancia y legacy deshabilitada tras validación.
- [ ] D05. Health completo y verificación de workflows reales, sin confundir `Healthy` con entrega.
- [ ] D06. Deploy/rollback específico de `api` ensayado; bases, Redis, Evolution, n8n y Caddy intactos.
- [ ] D07. No quedan P1 sin corregir ni fallos silenciosos sin alertas durante ventana de prueba acordada.

## 5. Puertas de control (GO / NO-GO)

| Puerta | Requisitos | Decisión |
|---|---|---|
| G0 — Diagnóstico | Imagen ejecutada durante incidente identificada; SQL/logs agregados; causa raíz o incertidumbre documentada | Si no, seguir recolectando evidencia, no desplegar |
| G1 — Compilación/tests | Build backend; test project explícito (baseline reportada 500; nuevo total deberá crecer o mantenerse si refactor justificable); frontend build+lint y tests; 0 fallos | Si no, NO-GO |
| G2 — Reglas de negocio | Inbox, outbound, reserva, pedidos, handoff, licencias, aislamiento y PDF pasan pruebas adversariales | Si no, NO-GO |
| G3 — Staging Oracle | Tag SHA exacto ARM64, Compose efectivo verificado, health Healthy, Evolution/n8n/Groq/Gemini reales controlados, ningún envío duplicado | Si no, rollback API |
| G4 — Producto MVP | Bandeja web y canal WhatsApp correctos para dos workspaces; alertas, cuotas y tiempos aceptables; seguridad de webhook; cambios de rol/licencia | Si no, no habilitar clientes |
| G5 — Producción | Evidencia firmada del checklist, respaldo/restauración, rollback probado, sin silencios sin observabilidad | GO solo si todas las anteriores cumplen |

## 6. Comandos de validación en la máquina local de Codex

No inventar resultados; `dotnet` debe estar disponible en el Windows del propietario.

```powershell
cd C:\Proyectos\NexFlow
git status --short
git rev-parse HEAD
dotnet build backend/backend.slnx --no-restore
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore
cd frontend
npm ci
npm run build
npm run lint
```

- Si las suites adicionales del frontend requieren `node --test`, ejecutar los comandos **existentes y compatibles con su configuración real**; no inferir que el `npm test` inexistente funcionará.
- Mantener un conteo explícito de pruebas reales, tiempos y errores. Históricamente Codex reportó **500/500**, no auditado independientemente.
- Si hay tres advertencias preexistentes, documentarlas; no asegurar «0 warnings» sin salida real.

## 7. Preparación de Oracle y rollback (no ejecutar durante el parche)

**Observado:** `nexflow_api` y `nexflow_gotenberg` pertenecen al proyecto Compose `nexflow`; API creada a partir de `compose.yml` + varios overrides históricos. El Compose **base** apunta a imagen diferente de la API activa. `saas_postgres`, `saas_redis`, `saas_caddy`, `saas_evolution_api` y `saas_n8n` pertenecen a otros proyectos y son compartidos.

**Prohibiciones:** no usar Compose base sin validar overrides efectivos, no cambiar tags en ejecución sin versión comprobada, no reponer contenedor de base compartida, no ejecutar migración automáticamente, no `prune` ni borrar volúmenes.

**Preflight de despliegue (lectura):**

1. `docker inspect nexflow_api --format '{{.Config.Image}} {{.Image}} {{.State.Status}} {{.State.StartedAt}}'`.
2. Recuperar `com.docker.compose.project.config_files` y comprobar que **todos** los archivos siguen presentes.
3. Comparar `docker compose -f ... config --format json` **sin imprimir variables/env/secretos**.
4. Conservar ID de imagen y config anteriores, comprobando tag de rollback existente.
5. Construir API con SHA exacto de código aprobado, `linux/arm64`, sin publicar/activar aún.
6. **Con autorización independiente**, cambiar exclusivamente `api`; validar health HTTPS a través de Caddy (HTTP interno redirige 307 por `UseHttpsRedirection`; no tomar 307 como caída).
7. Repetir tests de WhatsApp autorizados, PDF y panel. Si falla, rollback de **API solamente** con Compose completo original; verificar salud y datos. Sin reenviar `UnknownDelivery` ni eventos de PDF viejos.
8. Después del deploy, corregir `AllowLegacyWebhookKey` **solo** cuando instancias reales confirmen autenticación derivada.

**No insertar comandos automáticos de rollback aquí:** los overrides efectivos y estado de producción deben recuperarse de Oracle justo antes de ejecutarlos, para no restaurar una configuración equivocada.

## 8. Instrucciones listas para Codex — ejecutar UNA fase por vez

### Prompt maestro de arranque (SPRINT 0)

> Eres el ingeniero principal de NexFlow. Lee `AGENTS.md` y el archivo `NexFlow_PLAN_MAESTRO_CORRECCION_MVP_RC3.md` que colocaré en la raíz del repositorio. El objetivo inmediato es **SPRINT 0**, no implementar todos los sprints en una sola ejecución. Trabaja en `C:\Proyectos\NexFlow`, usa el estado real de Git local como fuente de verdad y compara la auditoría con código compilable. Confirma diferencias entre ZIP, local y GitHub; no presupongas que `main` está sincronizada. Reproduce primero el incidente de WhatsApp del 18/10/2026: «Quiero informacion» → menú → «Como puedo hacer una reserva?» → «Hola?» → «Que productos tienen» → «Escojo la opción 1», junto con el error del panel Inbox. Clasifica hallazgos CONFIRMADO/REPRODUCIDO/PENDIENTE; las recomendaciones externas ya implementadas no deben reimplementarse. Si no tienes evidencia de Oracle, marca la causa real como PENDIENTE. Puedes añadir pruebas de reproducción, pero no refactorices todavía. Mantén intactos los invariantes A-H, seguridad, licencias, documentos, reservas e idempotencia. **No** hagas push/commit, despliegue, cambios de infra, migraciones, `.env` ni secretos. Entrega informe corto de causa/evidencia/archivos/pruebas/GO o NO-GO. Si descubres un bloqueo real, propón solo el parche mínimo para SPRINT 1.

### Prompt SPRINT 1 — backend crítico WhatsApp

> Continúa `NexFlow_PLAN_MAESTRO_CORRECCION_MVP_RC3.md`, **solo SPRINT 1 (NF-01, NF-02, NF-03 y NF-06)**. Inspecciona el código actual, crea primero pruebas que reproduzcan cada defecto y corrige únicamente lo demostrado. En particular: usar MessageId de inbound distinto para cada respuesta limitada, sin cambiar la lógica del presupuesto; distinguir fallo recuperable de perfil Firestore antes de `AiInterpreter` y propagar cancelaciones/autorización; mantener FIFO/idempotencia pero visibilizar entrada bloqueante/dead letter; correlacionar finalización inbound con salida Firestore y estado Evolution sin reenviar `UnknownDelivery`. No agregar locks redistribuidos si SQL y transacciones actuales ya resuelven la concurrencia; no borrar o recrear dominios correctos. Ejecuta `dotnet build backend/backend.slnx --no-restore` y `dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore`. Reporta pruebas nuevas, total, diff y hallazgos pendientes. Sin git push/commit ni deploy.

### Prompt SPRINT 2 — UX y frontend

> Sigue exclusivamente SPRINT 2 (NF-04, NF-05, NF-07). Implementa parser seguro de «Escojo la opción 1» bajo menú vigente; mantén ambigüedad, estado/versionado y módulos autorizados. Haz que un error de `/messages` no esconda `/conversations` ni borre el chat anterior permitido, y viceversa; comunica `Failed` y `UnknownDelivery` sin afirmar entrega. Corrige timestamp `lastMessageAt` monotónico ante llegada fuera de orden. Mantén React Query, separación por workspace, polling condicionado a visibilidad y guards existentes; no duplicar estado en Zustand. Pruebas de navegador/API y regresiones de mensajes, sin cambios de despliegue. Build/test backend y frontend realmente ejecutados. Sin commit/push.

### Prompt SPRINT 3 — PDF y cola volátil

> Sigue exclusivamente SPRINT 3 (NF-08 y NF-12). Inspecciona el uso real de `IBackgroundTaskQueue` en `CatalogController` y `ServicesController`. Demuestra el fallo de invalidación al reiniciar entre edición y ejecución, y sustituye solo esas tareas críticas por una estrategia durable idempotente compatible con Firestore/PostgreSQL existentes. No elimines GenericTaskQueue antes de demostrar que está sin consumidores. Preserva hashes, dos tipos PDF, cuotas, callback n8n HMAC, controles de artefacto y `UnknownDelivery`. Prueba caídas simuladas y reinicios. Los cuatro Outbox fallidos históricos son diagnóstico operacional: no ejecutarlos ni inventar su causa. No migraciones ni infraestructura sin autorización adicional. Validar y reportar.

### Prompt SPRINT 4 — integridad de negocio

> Ejecuta exclusivamente SPRINT 4. Prioridad: `ServicesController.DeleteService` debe preservar reservas futuras de su workspace; usa el patrón real que ya aplica `BusinessController.DeleteLocation`, cuidando concurrencia/soft-delete. Confirma precios históricos en `OrderItemRecord` y efectos duplicados en pedidos/notificaciones, sin rediseñar órdenes ni añadir stock/pagos. Prueba 409 y validación equivalente de reservas manuales/bot. Si un cambio requiere nuevas columnas/migración, **no la ejecutes**, explica motivación, compatibilidad y plan reversible para aprobación. Mantener tests y contratos frontend. Sin commit/push/deploy.

### Prompt SPRINT 5–7 — seguridad, observabilidad e higiene

> Ejecuta **solo la fase específica que te indique**, no las tres de una vez. Usa las secciones SPRINT 5–7 del plan y evita rehacer HMAC, doble-chequeo de handoff, Count Firestore, polling de pestaña oculta, precios históricos o rate limiting que ya existen. Prioriza fallos probados, tests de regresión, instrumentación que no exponga datos de clientes, y resultados reproducibles. No modificar secretos ni desplegar. Documenta toda dependencia de datos reales de Oracle como PENDIENTE OPERACIONAL.

## 9. Plantilla de entrega obligatoria de cada sprint Codex

```text
SPRINT / HASH LOCAL:
OBJETIVO:
HALLAZGO REPRODUCIDO / EVIDENCIA:
ARCHIVOS MODIFICADOS:
INVARIANTES PROTEGIDOS:
TESTS AGREGADOS:
BUILD RESULTADO:
BACKEND TESTS: pasaron / fallaron / omitidos:
FRONTEND BUILD / TESTS:
DIFF FUERA DE ALCANCE: ninguno / detalle:
BLOQUEADORES Y RIESGOS RESIDUALES:
OPERACIONES EXTERNAS EJECUTADAS: ninguna (o detalle autorizado):
VEREDICTO: GO A SIGUIENTE SPRINT / NO-GO:
```

## 10. Política contra regresiones y sobreingeniería

- No introducir Kafka, RabbitMQ, nueva BD, service bus ni 2PC por defecto: primero explotar inbox/outbox existentes.
- No agregar `Task.Run` ni colas en memoria para tareas que requieren durabilidad.
- No cambiar rutas de negocio consolidadas A-H sin fallo reproducido o prueba que lo justifique.
- No persistir observabilidad con contenido sensible (teléfono/mensaje/payload/keys). Preferir correlación por IDs técnicos protegidos y agregación operacional.
- No convertir un fallo de dependencia en «éxito»: diferenciar fallback seguro, reintento, dead letter, handoff y entrega incierta.
- No ejecutar muchos sprints en un solo prompt: controlar el diff, ejecutar tests, revisar y seguir.
- Ningún test fake sustituye la prueba E2E en Oracle y WhatsApp real.

---

**Conclusión:** mantener arquitectura base; reparar **silencio/cola+IA**, **identidad outbound**, **visibilidad Inbox**, **parser**, **timestamp**, **invalidación volátil de PDFs** y **borrado de servicios**. Verificar cuidadosamente integraciones, tenant cleanup y seguridad con pruebas específicas. El MVP podrá aspirar a producción **después**, no antes, de correlacionar el incidente real, aprobar los sprints críticos y completar la matriz de staging.
