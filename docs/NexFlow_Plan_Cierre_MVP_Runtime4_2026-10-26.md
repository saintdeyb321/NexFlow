# NexFlow — Plan de cierre del MVP del asistente WhatsApp

**Base de auditoría:** 26 de octubre de 2026  
**Repositorio:** `saintdeyb321/NexFlow` (`main`, `306d7c8af41aacfd14c71d949da039f628beed95`)  
**Estado del servidor:** `nexflow-api:306d7c8-perms1` en Oracle ARM64; `/health/ready` confirmó PostgreSQL, Redis y Firestore saludables tras corregir permisos del paquete publicado.  
**Alcance:** auditoría y cierre funcional de conversaciones, conocimiento por negocio, servicios, reservas, productos, pedidos, PDF, licencias, integraciones y operación comercial.  
**Decisión actual:** infraestructura habilitada para **piloto controlado**; **NO-GO comercial** hasta completar pruebas de negocio, seguridad, desempeño y recuperación de todos los almacenes relevantes.

> **Regla de trabajo:** Codex audita primero, implementa después y sólo por sprint autorizado. El servidor de Oracle no debe cambiarse directamente para ocultar defectos del repositorio. No reintroducir el riesgo F7-01. No usar datos reales de clientes ni reprocesar mensajes históricos.

## 1. Qué demuestra la conversación real (y qué no)

| Hallazgo | Evidencia observada | Interpretación y verificación exigida | Severidad provisional |
|---|---|---|---|
| Respuesta inicial poco útil | Consultas sobre el negocio recibieron preguntas genéricas antes de obtener el perfil | Trazar intención/act/ruta local/IA/fallback. Distinguir error de comprensión, limitación de proveedor, presupuesto y aclaración | P1 UX |
| PDF vs. opciones reservables | Cinco servicios enumerados en catálogo; cuatro al entrar a reservar en una sede | **No prueba mock**. `OfferingService` lee Firestore; la reserva filtra por sede, `IsActive`, `RequiresReservation` y duración mínima. Identificar exactamente el quinto servicio, sus flags, alcance por sede y fecha del PDF | P1 si hay inconsistencia no explicada |
| Selección de servicio con error ortográfico | Texto similar a «evaluación odontologixa» reiteró la selección | Reproducir extracción por IA/alias y resolución autorizada; aclaración dirigida si no existe coincidencia inequívoca | P1 UX |
| Menú numérico y selección | «Evaluación odontológica» estaba en posición 2; «La 4» seleccionó «Limpieza dental» | El motor respetó la opción numérica; mejorar detección de contradicción cuando el usuario había mencionado otro servicio, sin adivinar | P1 UX |
| Disponibilidad vs. selección | El bot dijo «10:00 disponible», luego «Vale» retornó a preguntar fecha y repitió aclaraciones de hora | Separar consulta `ReadOnly` de selección explícita; conservar referencia segura `AvailabilityReference`, fecha, hora y paso; probar «esa hora», «sí, la de 10», «vale» contextual | P1 funcional |
| Hora natural | «10 am», «a las 10», «1p» obtuvieron aclaraciones repetidas | `TimePreferenceResolver` puede considerar «10» ambiguo (10:00/22:00) y no reconocer ciertas abreviaturas; no elegir mañana/tarde sin evidencia. Pedir una sola pregunta útil cuando no pueda resolverse | P1 UX |
| Pedido desde clínica | «¿Se puede hacer un pedido?» no inició flujo | Comprobar licencia `CATALOG+ORDERS`, existencia de PRODUCT activos y clasificación. Si no hay módulo, explicar restricción con claridad; no fabricar productos desde servicios | P1 si se omitió una operación autorizada |
| Gestión de cita existente | «Estado de mi reserva» escaló a un asesor | **Comportamiento intencional**: `ExistingReservationFlow` exige verificación de titularidad externa. No habilitar edición/cancelación sólo por coincidir número o nombre | Control de seguridad vigente |
| Tras transferencia humana | No se observa respuesta posterior al intento de editar | Confirmar modo humano persistente, notificación al operador y retorno controlado; silencio automático posterior puede ser correcto si el caso está escalado | P1 experiencia operativa |
| Contenido presuntamente mock | Datos de clínica DEMO y mensajes fijos | No se halló nombre de clínica hardcodeado en el código revisado. Un workspace DEMO puede contener datos **reales del repositorio Firestore**. Distinguir datos de prueba, respuestas deterministas y doubles usados únicamente en tests | Auditoría pendiente |

**Nota factual:** en el registro compartido «Evaluación odontológica» sí aparece como opción 2 del menú. El servicio aparentemente ausente es «Restauración con resina simple», que aparece en el catálogo general. Su estado real por sede y por tipo de reserva sigue sin verificarse.

## 2. Arquitectura que debemos conservar

```text
WhatsApp (contacto autorizado)
  -> Evolution API / webhook autenticado por instancia
  -> Inbound PostgreSQL (deduplicación y estado)
  -> Orquestador + estado conversacional por workspace/cliente
  -> Comprensión local / IA semántica con evidencia verificable
  -> Política de autorización (licencia + módulos + sede + titularidad)
  -> Herramientas de lectura (KnowledgeService / OfferingService / datos de negocio)
  -> Motores de transacción (ReservationEngine / OrderFlow / etc.)
  -> Resultado verificable + respuesta controlada / handoff
  -> Outbound WhatsApp con reconciliación de entrega
```

**Principio:** la IA interpreta solicitudes y propone una operación tipada; el backend comprueba contexto, permisos, identidad, disponibilidad, precios, consentimiento e idempotencia. La IA **no** escribe PostgreSQL/Firestore ni llama endpoints privilegiados arbitrariamente. Respuestas deterministas para precios, importes, fechas confirmadas y fallos de seguridad son deseables; lo incorrecto es repetirlas fuera de contexto o sustituir resultados reales por datos estáticos.

### Evidencia de código revisada

- `AiResponseOrchestrator.cs`: enruta a `IBookingFlow`, `IOrderFlow`, `IChatFlow`, recuperación y handoff; hay mensajes fallback explícitos y filtros por módulos.
- `TurnUnderstandingPipeline.cs` y `AiInterpreter.cs`: ruta local + interpretación semántica por proveedor; evidencia y límites de IA.
- `OfferingService.cs`: PRODUCT y SERVICE recuperados de Firestore, filtrados por `IsActive`/`LocationScope`; `ResolveReservationServiceAsync` agrega `RequiresReservation` y duración mínima.
- `ConversationFlows.cs` (`BookingFlow`, `ChatFlow`): lectura de sedes, servicios, horarios, disponibilidad, resumen y confirmación; entrega de PDF si el artefacto está `Current`.
- `OrderFlow.cs`: productos activos por workspace, borrador, cotización, confirmación, persistencia y notificación una sola vez; no convertir servicios en pedidos.
- `KnowledgeService.cs` / `BusinessReadTools.cs`: perfil, horarios, FAQ, servicios y productos sujetos a workspace y límites.
- `ExistingReservationFlow.cs`: bloqueo preventivo de consultas/cambios privados hasta tener verificación de titularidad externa.
- `ResponseComposer.cs`: composición informativa acotada y fallback determinista; no redacción libre de hechos/transacciones.
- `backend/Dockerfile`: en `306d7c8` la imagen original heredó `/app/appsettings.json` como `root:root` modo `600`; el hotfix de imagen `perms1` es operativo, **no está incorporado aún al repositorio**.

## 3. Orden de trabajo propuesto

### Sprint 0 — Auditoría de trazabilidad, solo lectura (obligatorio)

**Objetivo:** señalar para cada mensaje si se ejecutó una ruta local, IA, recuperación o fallback y qué fuente de datos respondió, antes de modificar la arquitectura.

1. Congelar fuente base y confirmar `git status`, commit, configuración relevante y diferencias entre imagen operativa `perms1` y Dockerfile del repo. No volcar secretos.
2. Dibujar recorrido por turno: webhook -> persistencia -> orquestador -> `TurnUnderstandingPipeline` -> acceso a módulos -> lectura/motor -> estado -> envío. Registrar rutas reales y fallback, con archivo y línea.
3. Auditar ausencia de mocks/datos hardcodeados en código productivo, entornos y loaders; distinguir tests, demo seed, plantillas, fallback de seguridad y un workspace DEMO legítimo. No borrar textos de seguridad.
4. Crear matriz **fuente de verdad** para perfil, sedes, horarios, FAQ, PRODUCT, SERVICE, `RequiresReservation`, `LocationScope`, duración, precio, PDF `Current`, pedidos, reservas, licencias y conversaciones; indicar Firestore/Postgres/Redis y refresco de cache.
5. Revisar por qué cuatro servicios reservables frente a cinco del PDF: inspección de filtros en código y propuesta de comprobación **read-only** sobre los datos reales autorizados. No inventar los flags del quinto servicio.
6. Clasificar los problemas de continuidad (disponibilidad consultada vs hora seleccionada; «vale»; «la 4»; errores ortográficos), pedido y handoff, con tests rojos propuestos.
7. Inventariar riesgos P0/P1 y priorizar por bloqueadores de piloto/comercialización.

**Salida exigida:** `docs/auditorias/AUDITORIA_WHATSAPP_MVP_2026-10-26.md`: hallazgo, evidencia `archivo:línea`, input, contexto, expected/actual, severidad, origen y propuesta mínima. `docs/auditorias/MATRIZ_DATOS_MODULOS_MVP.md`: campos, almacenamiento, acceso, límites de licencia y consistencia. Sin cambios productivos ni commits.

**Gate:** ninguna refactorización grande hasta tener diagnóstico reproducible de los turnos prioritarios.

### Sprint 1 — Hotfix reproducible de imagen y observabilidad mínima

- Arreglar **en el Dockerfile del repositorio** los permisos de archivos publicados al usuario no root, sin `chmod 777`; build/test ARM64 y smoke de lectura de `appsettings.json` como UID 1654.
- Mantener imagen/override `perms1` operativos hasta verificar una candidata nueva; no sobrescribirla ni regenerar tags a ciegas.
- Añadir telemetría sanitizada por turno: `workspaceHash`, `messageCorrelationId`, `route=Local|Semantic|Recovery|Fallback`, intent, act, goal/step, provider status, herramienta de lectura/operación, outcome y delivery status, sin teléfonos, nombres ni payloads.
- Verificar health de API y dependencias + Evolution / n8n / Gotenberg aparte (readiness no valida WhatsApp).

**Aceptación:** imagen nueva nonroot lee todos los recursos requeridos, CI/smoke pasan y la instrumentación explica exactamente por qué se disparó un fallback.

### Sprint 2 — Una sola verdad comercial para cada negocio

- Definir contratos tipados de lectura por workspace: perfil, sedes, horarios, FAQ, `Services`, `Products`, precio/moneda, `RequiresReservation`, `Duration`, `LocationScope`, `IsActive`, stock/disponibilidad donde aplique y procedencia.
- Identificar por qué un servicio está en PDF pero no es reservable; responder de modo explícito y amable, por ejemplo «ofrecemos X, pero no está habilitado para reserva en esta sede» **solo cuando ese estado esté probado**.
- Revisar PDF por alcance (`SERVICE`/`PRODUCT`), workspace y versión/hash o fecha de generación; evitar que se ofrezca como vigente si ha cambiado el catálogo, y no regenerarlo automáticamente.
- Conservar límites de licencia; sin permiso devolver información pública autorizada o instrucción útil, nunca datos de otro negocio.
- Validar filtrado y paginación de la consulta; una lista de 5 no debe afirmarse como catálogo completo si hay más.

**Aceptación:** módulo, panel, bot y PDF ofrecen resultados compatibles o explican diferencias legítimas; no existe información inventada ni cruce entre tenants.

### Sprint 3 — IA, intención y continuidad de conversación

- Auditar primero la precedencia `CurrentGoal`, intent, actos y preguntas pendientes; evitar que «Vale» o «esa hora» reinicien fecha/hora si hay referencia inequívoca vigente.
- Mantener `AvailabilityReference` contextual, con TTL y vínculo de workspace/conversación/sede/servicio/fecha/hora; consulta de disponibilidad **no** confirma ni cambia la reserva.
- Resolver correcciones, negaciones, errores ortográficos y abreviaciones españolas frecuentes; usar alias definidos por negocio, y aclaración cuando hay ambigüedad; no escoger servicios por fuzzy guess silencioso.
- Si el usuario dice «quiero evaluación» y luego «la 4» que corresponde a limpieza, mostrar la discrepancia y pedir una aclaración **antes** de sobrescribir el servicio.
- Para preguntas genéricas («a qué se dedica el negocio») intentar `Profile`/datos reales antes de una aclaración genérica, si la intención y permisos lo permiten.
- Preservar borradores al interrumpir con consultas informativas o cambiar de tema bajo confirmación; limitar loops de aclaración, proponer asesor cuando no hay progreso.
- La IA propone herramientas **tipadas** y evidencia validable; el orquestador resuelve, autoriza y ejecuta de forma determinista. Nada de SQL, API calls o comandos arbitrarios desde texto del LLM.

**Aceptación:** pruebas multi-turn reproducen exactamente las transiciones fallidas y pasan sin perder ni inventar fecha, servicio, sede o consentimiento.

### Sprint 4 — Reservas completas y seguras

- Crear: elegir sede, servicio real habilitado, fecha/hora real, verificación de disponibilidad, nombre, resumen vigente, confirmación explícita y creación idempotente.
- Consultar/reprogramar/cancelar **reservas existentes**: diseñar autorización independiente (p. ej., flujo de verificación por canal seguro/OTP o verificación humana formal), caducidad y auditoría; nunca desbloquear solo por número/alias/nombre.
- Handoff: verificar aviso al operador, conversación `Human` durable, SLA/cola de atención y mecanismo autorizado de devolver control al bot.
- Pruebas de concurrencia, reintentos, conflictos de slot, timezone America/Lima, cambio de precios/horarios, mensajes duplicados y rollback compatible.

**Aceptación:** reserva única y consistente en PostgreSQL; gestión privada no expone datos sin verificación; no modifica la cita al informar disponibilidad.

### Sprint 5 — Pedidos reales, no simulados

- Distinguir `Order` de `Request`, consulta de precios y `Reservation`; si el negocio no tiene `CATALOG + ORDERS` o no tiene PRODUCT activos, responder por qué no se puede realizar un pedido.
- Permitir iniciar borrador sin artículos, agregar/quitar/cambiar cantidades y sede, hacer resumen exacto con moneda y precio actuales, confirmar expresamente, crear solicitud `PendingReview` y notificar una sola vez.
- No crear pedidos de PRODUCT desde SERVICE (si el negocio es una clínica sin PRODUCT, la intención puede requerir consulta o una solicitud, con licencia habilitada, no una orden fantasma).
- Mantener mitigación F7-01: invalidar resumen por revocación/restauración de licencia o recotización; no confirmar precio cambiado sin nueva revisión.
- Reconciliar reintentos de inbound/outbound, `UnknownDelivery` y outbox sin reenviar ciegamente.

**Aceptación:** operación comercial trazable, exactamente una solicitud por consentimiento/origen, importes confirmados y permisos válidos.

### Sprint 6 — Calidad del canal, resiliencia y UX del negocio

- Verificar Evolution con instancia autenticada por workspace; estado de conexión, webhooks, deduplicación, mensajes interactivos, multimedia no interpretado, 429/timeouts y reconexión.
- Confirmar entrega real al teléfono, no solamente Inbound `Processed` ni HTTP 200.
- Revisar estados `Pending`, `Processing`, `Processed`, `Failed`, `DeadLetter`, Outbox, head bloqueado, notificaciones, horarios de operadores y takeover.
- Panel: dejar al negocio cargar datos, alias, servicios reservables por sede, productos, horarios, documentos PDF, licencia y contacto humano con validaciones simples; ofrecer simulador de pruebas y diagnóstico sanitizado.
- Costos y límites por workspace: llamadas Gemini/Groq, fallback y presupuesto. No deshabilitar rate limiting para esconder fallos.
- Revisar Firestore Spark: plan verificable de backup/exportación antes de clientes reales; `pg_dump` **no** cubre perfiles, catálogo, pedidos y estado que viva en Firestore.

**Aceptación:** E2E real y medible, flujo estable para más de un negocio de prueba, soporte y recuperación documentados.

### Sprint 7 — Piloto y puerta de salida a ventas

1. Crear dos o más workspaces sintéticos con licencias/módulos distintos, sin contactos reales de clientes.
2. Ejecutar escenarios clínica multisede, tienda con productos y hotel/servicios si está habilitado, con números de WhatsApp de prueba consentidos.
3. Al menos 30 conversaciones multitur nuevas e independientes con revisión humana; para umbral comercial seguir el runbook existente: 100 conversaciones con al menos 95% de resolución y **cero violaciones críticas**, si ese es el criterio formal aprobado.
4. Registrar resultados reales de acciones: reservas/pedidos únicos, notificaciones, transferencia humana, entrega y costos; no dar por aprobado un proveedor no ejercitado.
5. Pruebas adversariales autorizadas: aislamiento por workspace, licencias revocadas, mensajes fuera de orden, doble confirmación, prompt injection en catálogo, secretos, fallos de red, Firestore/Redis degradados y colas atascadas.
6. Tener respaldo y restauración aislada de **todos** los almacenes con datos de clientes, plan de incidentes, soporte, términos de servicio/privacidad y monitoreo antes de iniciar ventas reales.

**GO para venta:** cero P0/P1 críticos pendientes, resultados de E2E en WhatsApp, recuperación integral probada, protección de datos, canales de soporte, costos y capacidad operativa real. De lo contrario: piloto controlado, no comercial.

## 4. Matriz mínima de pruebas de aceptación

| ID | Conversación o condición | Resultado verificable |
|---|---|---|
| T01 | «Hola, ¿a qué se dedica este negocio?» | Perfil del **workspace correcto**; no respuesta genérica si el perfil existe y es accesible |
| T02 | «¿Qué servicios ofrecen?» | Lista verificada y paginación/completitud correctas; sin servicios de otro negocio |
| T03 | PDF enumera cinco y sede permite reservar cuatro | Explicación real de la diferencia con bandera/sede/versión, no falsa equivalencia |
| T04 | «Quiero evaluación odontologixa» | Resolución mediante evidencia/alias autorizado o pregunta breve, nunca selección arbitraria |
| T05 | «Quiero evaluación» seguido de «la 4» donde 4 = limpieza | Clarifica discrepancia antes de cambiar servicio |
| T06 | «Pasado mañana a las 10 am», consulta disponible, «esa hora» | Conserva fecha/hora y pide solo lo faltante; no confirma automáticamente |
| T07 | «Vale», «a las 10», «1p» | No entra en bucle ni cambia fecha/hora inadvertidamente; aclara ambigüedades reales |
| T08 | «Quiero reservar X en sede Y» con todos los datos | Valida disponibilidad, confirma con resumen vigente y crea una reserva única |
| T09 | Consulta de estado de cita existente | Exige verificación titularidad y pasa a humano si no se puede verificar |
| T10 | «Se puede hacer un pedido» en clínica sin producto o módulo | Explica capacidad real y alternativa lícita; sin orden inventada |
| T11 | Pedido 2 productos + cambio de cantidad y sede | Subtotal actual y `PendingReview` solo tras consentimiento vigente |
| T12 | Precio cambia tras resumen, o licencia se revoca/restaura | Invalida consentimiento antiguo y recotiza antes de enviar (F7-01) |
| T13 | Mismo webhook y confirmación repetida | Una transacción comercial y una notificación durable |
| T14 | Human takeover seguido de «editar mi reserva» | Sin auto-respuesta que suplante al asesor; canal humano atiende y devuelve control mediante flujo válido |
| T15 | Dos workspaces con nombre de servicio idéntico | Aislamiento total de IDs, precio, sedes, PDF, licencias y borradores |
| T16 | PDF `Failed` o `UnknownDelivery` | Alternativa textual y reconciliación sin reenvío ciego |
| T17 | Deploy con archivos `root:root` modo 600 | CI/smoke detecta el fallo **antes** de producción; usuario 1654 lee configuración |
| T18 | Proveedor IA agotado o falla Redis | Fallback honesto, contexto durable y límites de costo; no operación comercial inventada |

Para cada prueba: capturar ID sintético/hash de correlación, estado anterior/posterior, ruta elegida, respuestas sanitizadas, source version, persistencia y entrega. Guardar transcripciones sensibles sólo en entorno y política autorizados, no en logs públicos ni commits.

## 5. Condiciones de parada y controles

- **P0:** cruce de workspaces, exposición de datos privados, operación no autorizada, pérdida de integridad o credenciales comprometidas.
- **P1:** confirmación sin resumen vigente, duplicado de reserva/pedido, precios o sedes inventados, falso estado de entrega, gestión de cita sin verificar titularidad, pérdida sistemática del borrador.
- Ante P0/P1: suspender nuevas pruebas comerciales, conservar IDs durables y corregir con pruebas de regresión antes de reanudar.
- Nunca restaurar backup sobre producción para ensayar; no borrar mensajes `DeadLetter`, no reprocesar eventos PDF históricos ni reenviar `UnknownDelivery` automáticamente.
- No ejecutar `docker compose down`, migraciones ni `prune` por comodidad. No escribir secretos en documentos ni pedir al usuario compartirlos.

## 6. Cómo coordinaremos Codex y la auditoría

1. **Codex Sprint 0:** abre el repositorio local `C:\Proyectos\NexFlow`, lee `AGENTS.md`, los documentos maestros, inspecciona source y tests, y entrega una **auditoría sin modificar código**.
2. **Revisión del auditor:** compara hallazgos con esta matriz, separa evidencia de hipótesis y elige un sprint mínimo por vez.
3. **Codex sprint autorizado:** escribe pruebas que reproduzcan la falla, corrige código, ejecuta tests específicos + regresiones, informa archivos/diff y no hace push o deploy.
4. **Verificación:** auditar cambios, CI de SHA congelado, build ARM64 en Oracle cuando corresponda y despliegue controlado con respaldo/rollback compatible.
5. **Piloto:** mediciones en canal real. Nada se declara listo para ventas basándose solo en tests de mocks o en `/health/ready`.

**Regla final:** no sustituir una arquitectura determinista y auditable por un «agente» que invente acciones. El MVP estará listo cuando la interpretación natural y los módulos del negocio funcionen integrados, demostrablemente y con seguridad.
