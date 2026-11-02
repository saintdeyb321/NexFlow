# NEXFLOW — Plan ejecutable de auditoría, depuración y estabilización del agente conversacional

**Destino:** agente de desarrollo Codex con acceso al repositorio local de NexFlow  
**Fecha de elaboración:** 2026-11-02  
**Estado:** especificación de trabajo; **NO** es constancia de cambios ni de pruebas ejecutadas.  
**Modo:** ejecutar, investigar, escribir pruebas, corregir, volver a probar y documentar; realizar los cambios en el *working tree* local.  
**Alcance por defecto:** backend C#/.NET, pruebas y documentación técnica. Frontend solo si un hallazgo demuestra que es imprescindible y se deja documentado; no ampliar alcance sin necesidad.

---

## 0. Misión y resultado exigido

NexFlow cuenta con un asistente para WhatsApp basado en Evolution API, con interpretación de lenguaje natural, catálogo de productos y servicios, pedidos, reservas, selección de sedes, persistencia y recuperación de contexto. El objetivo **no** es sustituirlo por un nuevo framework ni reescribir el sistema: es **hacer fiable la comprensión y continuidad de conversaciones**, corregir problemas de estado y enrutamiento, garantizar la seguridad de las operaciones y contener el costo de IA.

**Codex debe ejecutar realmente el trabajo**, no limitarse a proponer una arquitectura o producir un informe. Debe:

1. Instalar o habilitar de forma segura las skills necesarias en una carpeta verificable del proyecto.
2. Inspeccionar el estado Git, la arquitectura y los tests existentes sin alterar trabajo pendiente.
3. Reproducir los defectos con evidencia de entrada, decisión interna, contexto, herramienta elegida, persistencia y respuesta final.
4. Crear tests de regresión que fallen antes de la corrección; si no es posible, documentar el impedimento técnico concreto.
5. Corregir la **causa raíz** mediante modificaciones mínimas y coherentes en el backend.
6. Ejecutar pruebas focalizadas y generales, medir el resultado antes/después y repetir los ciclos hasta cumplir criterios o registrar impedimentos verificables.
7. Entregar una matriz de requisitos frente a tests exactos, un registro de cambios y un informe de pendientes honestos.

No afirmar que una función está corregida si solo pasó una prueba superficial del texto de salida. Comprobar también **estado, acción, consulta real, idempotencia, aislamiento de workspace y continuidad multiturno**.

## 1. Restricciones no negociables

- **No realizar** `git reset --hard`, `git clean -fd`, `git stash`, restauraciones masivas, ni sobrescribir modificaciones preexistentes. Antes de modificar, inventariar `git status --short`, rama, `HEAD`, `git diff` y archivos nuevos; distinguir cambios propios y anteriores.
- **No hacer commit, push, merge, rebase, migración aplicada, despliegue, reinicio de servicios remotos o publicación** sin autorización posterior y explícita. No modificar producción. No ejecutar comandos contra la instancia pública ni una base con datos reales.
- No enviar mensajes reales de WhatsApp, no generar pagos, no abrir ni confirmar pedidos/reservas reales, no hacer llamadas a proveedores de IA con costos no controlados. Simular endpoints externos; para pruebas opcionales de integración usar exclusivamente credenciales y tenant de prueba, si ya están disponibles y su uso está autorizado.
- Nunca imprimir tokens, claves, contraseñas, números personales, conversaciones privadas completas ni datos de pacientes en logs o reportes. Usar fixtures sintéticos, anonimizar y respetar reglas existentes de redacción.
- Preservar autorización transaccional: **el mensaje semántico `Submit` no equivale a consentimiento libre e incondicional**. Para pedidos y reservas debe existir un resumen vigente y un acto de confirmación inequívoco asociado a la versión correcta del borrador, según contratos existentes. No saltar revisión, no convertir `RequireConfirmation=false` en consentimiento automático, ni introducir duplicaciones.
- No inventar stock, disponibilidad de agenda, precio, sede, horarios ni confirmaciones. Catálogo ≠ inventario; búsqueda de turnos ≠ reserva registrada.
- Preservar la separación por workspace/tenant, sedes, políticas, huso horario, límite de proveedores y almacenamiento.
- Mantener la arquitectura actual salvo que las pruebas demuestren una modificación necesaria. Preferir reglas deterministas para selecciones explícitas, edición de borradores, validación, idempotencia y operaciones; IA estructurada para lenguaje ambiguo.
- No cambiar proveedor/modelo, no instalar frameworks de agentes ni añadir grandes dependencias sin probar el beneficio real.
- Evitar ciclos infinitos: si hay un bloqueo (SDK, dependencias, Internet, fallo preexistente, infraestructura), **seguir con los otros escenarios factibles** y clasificarlo claramente; no inventar resultados.
- Ejecutar cambios en etapas, con pruebas y reporte por etapa. Nada de refactorización global no solicitada.

## 2. Instalar, revisar y guardar las skills (paso obligatorio)

### Skills elegidas

| Prioridad | Skill | Origen | Propósito |
|---|---|---|---|
| P0 | `systematic-debugging` | `https://github.com/obra/superpowers/tree/main/skills/systematic-debugging` | Investigar causa raíz y trazar fallos entre componentes antes de proponer cambios |
| P0 | `code-testing` | `https://github.com/dotnet/skills/tree/main/plugins/dotnet-test/skills/code-testing` | Crear/reforzar pruebas C# y regresiones |
| Opcional | `eval-triage-and-improvement` o `eval-result-interpreter` | `https://github.com/microsoft/eval-guide` | Clasificar fallos de una batería de evaluación; la herramienta está orientada a Copilot Studio, adaptar criterios a NexFlow sin suponer compatibilidad directa |
| Opcional, posterior | `promptfoo` | `https://github.com/promptfoo/promptfoo` | Evaluación de conversaciones end-to-end si existe una interfaz de pruebas estable y aporta valor sobre xUnit |

**Ubicación requerida para skills de trabajo en el repositorio:**

```text
<NEXFLOW_REPO>/.agents/skills/systematic-debugging/SKILL.md
<NEXFLOW_REPO>/.agents/skills/code-testing/SKILL.md
<NEXFLOW_REPO>/docs/auditorias/agente-conversacional/2026-11-02/
```

Mantener cada skill con **todos sus archivos de apoyo necesarios**, no copiar solo el `SKILL.md` cuando existan documentos, scripts o referencias relativos. Preservar copyright/licencias aplicables y guardar la procedencia en `docs/auditorias/agente-conversacional/2026-11-02/SKILLS_ORIGEN.md` (URL, SHA del commit origen, licencia, archivos copiados, fecha, verificaciones). No sobrescribir skills ya presentes ni hacer actualizaciones remotas silenciosas.

**Proceso seguro y multiplataforma:**

1. Averiguar la raíz real del repo (`git rev-parse --show-toplevel`) y el sistema operativo. No asumir que siempre es `C:\Proyectos\NexFlow`, aunque puede serlo.
2. Inspeccionar las skills existentes, el `AGENTS.md` y las instrucciones de proyecto. Comprobar si Codex ya descubre `systematic-debugging` y `code-testing`; si existen, **validarlas y reutilizarlas**, sin reinstalar a ciegas.
3. Para las ausentes: clonar **solo fuentes oficiales** en una carpeta temporal fuera del repositorio; inspeccionar los `SKILL.md`, scripts, licencias, referencias y SHA antes de copiar. No ejecutar scripts descargados automáticamente.
4. Copiar directorios de skills íntegros, sin pisar contenido preexistente, a `.agents/skills/`. Registrar versión exacta y licencias. Como opción, si `code-testing` requiere funciones de agentes específicas del host y no funciona de forma autónoma en Codex, usar sus reglas como referencia de pruebas, y anotar esa limitación. **No afirmar que el plugin de Copilot funciona íntegramente en Codex.**
5. Confirmar que ambos `SKILL.md` existen, tienen frontmatter correcto y sus referencias locales se resuelven. La activación por descubrimiento puede exigir **reiniciar la sesión de Codex**: si ocurre, dejar un punto de continuación claro en `ESTADO_EJECUCION.md` y continuar en la nueva sesión. Si Codex ya permite leer las instrucciones localmente sin reinicio, aplicarlas explícitamente en esta sesión.
6. Si falla Internet o no es posible instalar, dejar evidencia y ejecutar la metodología equivalente de causa raíz + pruebas xUnit. No bloquear la corrección del bot por una instalación auxiliar.

**Ejemplo orientativo de Windows PowerShell**, adaptar rutas, validar origen y omitir la copia si ya existen destinos:

```powershell
$root = (git rev-parse --show-toplevel).Trim()
$skills = Join-Path $root '.agents\skills'
New-Item -ItemType Directory -Force -Path $skills | Out-Null
$temp = Join-Path $env:TEMP ('nexflow-skills-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
try {
  git clone --depth 1 https://github.com/obra/superpowers.git (Join-Path $temp 'superpowers')
  git clone --depth 1 https://github.com/dotnet/skills.git (Join-Path $temp 'dotnet-skills')
  git -C (Join-Path $temp 'superpowers') rev-parse HEAD
  git -C (Join-Path $temp 'dotnet-skills') rev-parse HEAD
  $s1 = Join-Path $temp 'superpowers\skills\systematic-debugging'
  $s2 = Join-Path $temp 'dotnet-skills\plugins\dotnet-test\skills\code-testing'
  # Leer SKILL.md, recursos referidos y licencias ANTES de instalar.
  if (!(Test-Path (Join-Path $skills 'systematic-debugging'))) {
    Copy-Item -Recurse $s1 (Join-Path $skills 'systematic-debugging')
  }
  if (!(Test-Path (Join-Path $skills 'code-testing'))) {
    Copy-Item -Recurse $s2 (Join-Path $skills 'code-testing')
  }
  Test-Path (Join-Path $skills 'systematic-debugging\SKILL.md')
  Test-Path (Join-Path $skills 'code-testing\SKILL.md')
} finally {
  # Eliminar únicamente $temp creado en esta operación, tras verificar su ruta.
}
```

El bloque es **referencia**, no licencia para ejecutar comandos sin inspección. Si el entorno Codex proporciona un instalador seguro, también se puede usar, pero **debe quedar la copia local solicitada y el registro de versiones**. Para el marketplace de Codex, consultar `https://github.com/dotnet/skills#codex-cli`; no confundir instrucciones de Copilot CLI (`/plugin`) con Codex (`/plugins`).

## 3. Contexto técnico que debe comprobarse contra código real

Se han mencionado los componentes siguientes en conversaciones técnicas anteriores. **Son pistas de localización, no garantía de que el HEAD actual los conserve sin cambios**:

- Entrada: webhook Evolution API, gateway, manejo de botones/listas y mensajería saliente.
- Procesamiento: `ProcessMessage`, `AiInterpreter`, `AiRouter`, `AiResponseOrchestrator`, `ConversationRecoveryService`, `ConversationStateService`, `ContextRecoveryService`.
- Negocio: flujos de catálogos, `ReservationEngine`, `ExistingReservationFlow`, pedido, borradores, verificación/confirmación de reserva, catálogo/sedes/servicios, políticas por workspace.
- Persistencia: PostgreSQL, Redis, Firestore, repositorios de reservas y Outbox.
- IA: proveedores como Groq/Gemini, respuestas estructuradas JSON, fallbacks y límites por workspace.

**Inspeccionar** `.sln`/`.slnx`, `*.csproj`, version de SDK en `global.json` si existe, `AGENTS.md`, notas de auditoría recientes y los tests existentes. Evitar duplicar servicios o rediseñar flujos cuyo contrato ya funciona.

Regresiones históricas que requieren especial cuidado:

- Consentimiento de pedidos, confirmaciones explícitas y bucles de revisión; no debilitar invariantes transaccionales.
- Preservar estados `Failed` de PDF rechazados y fallback textual único; los estados `UnknownDelivery` no autorizan reenvíos.
- Reconocimiento de listas/botones sin inventar texto de multimedia.
- Límites de IA, validación estricta de JSON y respuestas seguras ante error del proveedor.
- Recuperación de borradores y continuidad de conversación, especialmente cuando el usuario cambia de tema o regresa.

## 4. Evidencia real del bug (caso de aceptación obligatorio)

Conversación observada el **1 de noviembre de 2026**. Reproducir con fixtures de conversación/estado equivalentes, sin mensajes reales:

| Turno | Entrada | Observado | Esperado |
|---|---|---|---|
| A | `Hola` | Presentación normal | Respuesta inicial correcta |
| B | `Menu` | Muestra opciones | Menú disponible, selección ordenada |
| C | `Ver mas` | Muestra otras opciones | Navegación, sin pérdida del contexto válido |
| D | `Que productos tienen` | Lista: cepillo S/14.90, enjuague bucal sin alcohol 250 ml S/24.00, hilo dental S/12.90, pasta S/18.50 | Respuesta basada en catálogo real |
| E | `Esta disponible el enjuague bocal?` | `No tengo información registrada...` | Reconocer entidad por nombre aproximado y **consultar inventario real**, o distinguir existencia en catálogo de stock desconocido |
| F | `Dije si tiene disponible el enjuague bocal sin slcohol` | Respuesta genérica | Resolver referencia del producto y autocorrección razonable en el contexto de catálogo |
| G | `Quieor hacer una reserva` | Aclaración genérica sobre selección previa | Detectar intención razonablemente clara de reservar o aclarar específicamente si la señal es realmente ambigua; no bloquearse en catálogo |
| H | `Quiero reservar` | Inicia selección de sede | Flujo de reserva empieza coherentemente también con variantes equivalentes |
| I | `En concepcion` | Elige sede Concepción | Selección coherente |
| J | `La segunda` | Elige Evaluación odontológica | Opción ligada a **la lista vigente de servicios**, no a una lista antigua |
| K | `El 2 de noviembre` | Fecha 02/11/2026 | Fecha local acorde a instante de la conversación, no fecha del reloj actual |
| L | `Las 3 pm` | Hora 15:00 | Validar disponibilidad real con zona de sede |
| M | `Deyvid`, luego `Deyvida alexander` | Solicita apellido y luego crea resumen | Manejar nombre incompleto sin perder el borrador |
| N | `Si` tras resumen | Reserva confirmada con referencia | Confirmar solo si el resumen vigente sigue válido, con escritura persistida e idempotencia |

**Precisión:** los precios y nombres arriba son datos de un ejemplo capturado; no convertirlos en valores globales. Las pruebas deben usar fixtures por tenant. No afirmar stock positivo por el solo hecho de que un producto esté listado.

## 5. Hipótesis a contrastar (NO asumir como diagnóstico)

- Coincidencia literal, normalización insuficiente, falta de acentos, errores ortográficos o alias de producto.
- Búsqueda sobre una fuente distinta a la lista renderizada, filtro de sede/workspace, inventario ausente o stock `unknown` tratado como `not found`.
- Contexto reciente no disponible en `ConversationRecoveryService`, TTL, claves Redis, persistencia Firestore, expiración, reintento o orden de webhook.
- Enrutamiento con prioridad del estado anterior sobre intención global nueva; diferencias entre `Quieor hacer una reserva`, `Quiero hacer una reserva` y `Quiero reservar`.
- Selecciones posicionales (`la segunda`) ligadas a opciones obsoletas al cambiar de contexto.
- Respuesta del proveedor de IA mal estructurada, validación fallida o fallback excesivamente genérico.
- Desfase entre lectura de horarios, confirmación y persistencia, o consumo de confirmación equivocada.

Para cada hipótesis indicar: evidencia observada, fichero/método/línea, prueba de reproducción, causa establecida o refutada. **No introducir nuevos sinónimos o regex hasta determinar qué capa falla.**

## 6. Plan de ejecución secuencial y entregables por fase

### Fase 0 — Inventario, skills y baseline

- Registrar rama, HEAD, Git status, diff, proyectos de backend, framework y tests relevantes.
- Instalar/verificar skills conforme a sección 2.
- Crear carpeta `docs/auditorias/agente-conversacional/2026-11-02/` con: `ESTADO_EJECUCION.md`, `SKILLS_ORIGEN.md`, `BASELINE.md`, `MATRIZ_PRUEBAS.md`, `HALLAZGOS_Y_CAUSAS.md`, `CAMBIOS.md`, `RESULTADOS_FINALES.md`.
- Ejecutar compilación y un conjunto focalizado de tests existentes para establecer baseline. Capturar comandos, duración aproximada, número de pruebas, fallas, entorno de ejecución y limitaciones. No confundir fallos preexistentes con regresiones nuevas.
- Establecer `ESTADO_EJECUCION.md` como bitácora de reanudación entre sesiones de Codex. Actualizarla al cerrar **cada** fase: `fase`, `commit_base`, `archivos tocados`, `tests ejecutados`, `resultado`, `pendientes`, `siguiente paso`.

### Fase 1 — Traza de punta a punta y tests en rojo

- Localizar el recorrido: mensaje recibido -> deduplicación -> contexto actual -> opciones visibles -> interpretación (determinista/IA) -> selección de acción -> lectura de datos -> modificación de borrador -> respuesta -> persistencia -> Outbox.
- Añadir diagnóstico **solo donde haga falta** y sin PII: correlation id, message id hash, workspace id ofuscado, intención candidata/efectiva, versión de estado, consulta de catálogo e inventario, decisión y motivo del fallback.
- Reproducir secuencias A–N en tests aislados. Conservar el mismo `conversationId` y un reloj inyectable anclado a 2026-11-01 en zona America/Lima para esa traza.
- Crear tests que fallen en los puntos problemáticos y verificar que fallan por la **razón correcta** (no por configuración del runner ni un mock incompleto). Guardar salida resumida del estado inicial en `BASELINE.md`.
- Para dudas sobre expectativa, inspeccionar contratos/requisitos del sistema y explicitar la decisión; evitar convertir una suposición en requisito.

### Fase 2 — Corrección de producto y stock

- La búsqueda debe usar el catálogo real del tenant/sede y la fuente de inventario correcta. Diferenciar claramente: no encontrado, encontrado sin registro de inventario, stock cero, stock positivo, no consultable por error.
- Normalizar de modo apropiado errores frecuentes (`bocal` vs `bucal`, `slcohol` vs `alcohol`), tildes, mayúsculas, espacios y referencias anafóricas (`ese`, `el enjuague`) **sin false positives**. Priorizar coincidencia exacta/código, alias conocidos, contexto explícitamente mostrado, coincidencia aproximada con umbral y aclaración ante empate/ambigüedad; no usar la IA para inventar artículos.
- Reutilizar el producto seleccionado/mencionado cuando el usuario pregunta `¿y hay disponible?`, salvo cambio explícito.
- Evitar mostrar precio/stock de un workspace o sede diferente. Nunca confirmar la disponibilidad si no hubo verificación.
- Verificar errores de infraestructura y mensajes de fallback útiles, sin entrar en bucle.

### Fase 3 — Enrutamiento de intenciones y continuidad multiturno

- Revisar si una intención global explícita (`reservar`, `hacer pedido`, `consultar productos`, `asesor`, `cancelar`) puede cambiar de flujo aun estando dentro de otro. Prioridades deben estar definidas en código/contrato, no emergentes del prompt de IA.
- Evitar que el estado `awaiting_selection` de productos domine para siempre. Dar respuesta a `Quiero hacer una reserva` en el primer mensaje; tolerar una errata razonable si la señal es inequívoca. Ante ambigüedad real, preguntar **qué intención** se quiere, no qué dato antiguo se desea editar.
- Preservar borradores existentes por ámbito/operación; no sustituir o descartar datos silenciosamente. Definir qué ocurre con un borrador interrumpido, su recuperación y cuándo requiere confirmación para descartarlo.
- Selecciones ordinales ancladas a `lastPresentedOptions` de **la pregunta vigente** y su versión/timestamp; evitar elección de una lista anterior.
- Mensajes `menu`, `ver mas`, `volver`, `editar`, `descartar`, `asesor`, `sí`, `no`, `otra sede`, `mismo producto` deben tener reglas de contexto comprobables.
- Evitar reset de contexto en reintentos, mensajes duplicados, entrega desordenada o falla temporal de Redis/Firestore.

### Fase 4 — Pedidos y reservas: contratos de seguridad

- Seguir flujo de pedido hasta borrador, líneas, variantes/cantidades, precio, total, disponibilidad cuando aplique, resumen y confirmación.
- Comprobar que la edición parcial (`dos enjuagues` -> `solo uno`) modifica **la línea correspondiente**, no genera un segundo pedido ni borra otros datos.
- Para reserva comprobar sede, servicio de esa sede, fecha local, hora, horario real, ocupación, datos del cliente y persistencia; revalidar antes de confirmar según el contrato actual.
- Cambios después del resumen invalidan el consentimiento anterior y exigen nuevo resumen/confirmación cuando corresponda.
- Verificar atomicidad y deduplicación del ingreso de webhooks, Outbox, comandos y confirmaciones; reintento de mensajes no debe crear dos pedidos/reservas ni enviar confirmaciones duplicadas.
- Si operación queda `UnknownDelivery`, no asumir fallo y reenviar por defecto. No relajar reglas previas de confirmación libre.
- Las transferencias humanas tienen prioridad, sin suprimir una operación ya confirmada. Respuestas con errores de proveedor o de infraestructura deben distinguir «no pude consultar» de «no existe».

### Fase 5 — Robustez, eficiencia y pruebas de regresión

- Tests parametrizados de redacción natural en español peruano, errores tipográficos, tildes, abreviaturas, mensajes cortos, interrupciones, opciones ordinales y contexto largo.
- Tests de concurrency/race, aislamiento por workspace, saturación/timeout del proveedor IA, JSON inválido, Redis/Firestore indisponibles, límites de tokens y fallback.
- Medir llamadas IA por conversación e impacto en latencia con fixtures reproducibles. **No inventar ahorros**: documentar baseline y comparación. Preferir resolver reglas explícitas sin llamada IA cuando ya hay información suficiente.
- Ejecutar tests focalizados después de cada grupo de modificaciones, luego build completo y suite de backend que sea practicable. Tests de DB/integración solo con infraestructura efímera o de pruebas; si está ausente registrar `BLOCKED`.
- Analizar escenarios que fallan, corregir causa raíz y repetir. Evitar arreglar un caso a costa de otros. Inspeccionar `git diff --check` y cambios no solicitados.

### Fase 6 — Revisión final y entrega

- Completar archivos de la carpeta de auditoría.
- Reportar `PASS`, `FAIL`, `BLOCKED` y `NOT RUN` separados; incluir nombres exactos de tests, comandos, evidencia y motivo de cada bloqueo. No usar `100%` si hay tests omitidos.
- Resumir archivos nuevos/modificados, hallazgos, correcciones, efectos sobre flujos anteriores y riesgos restantes; especificar si hay cambios al contrato público.
- Incluir tabla final `| Requisito | Evidencia | Estado |` con una o más pruebas específicas por requisito.
- Dejar trabajo local listo para revisión; **no commit ni deploy**.

## 7. Matriz mínima de escenarios obligatorios

Crear un inventario en `MATRIZ_PRUEBAS.md`, con `ID`, precondiciones, mensajes en orden, intención/estado esperado, llamadas a repositorio/herramienta, respuesta permitida, assertions, nombre exacto de test, resultado antes y después.

### Catálogo e inventario

| ID | Caso | Validación decisiva |
|---|---|---|
| CAT-01 | Listar productos por tenant | Datos reales del catálogo sin mezclar workspaces |
| CAT-02 | `enjuague bucal` exacto | Resuelve producto correcto |
| CAT-03 | `enjuague bocal` | Coincidencia tolerante en contexto inequívoco |
| CAT-04 | `enjuague bocal sin slcohol` | Resuelve entidad sin inventarla |
| CAT-05 | `¿Está disponible?` tras listar | Referencia contextual al producto vigente |
| CAT-06 | Stock = positivo | Consulta fuente real y comunica disponibilidad acotada |
| CAT-07 | Stock = 0 | No afirma disponibilidad |
| CAT-08 | Sin inventario registrado / fallo de consulta | No confunde con inexistencia, respuesta útil |
| CAT-09 | Dos artículos de nombre parecido | Pide aclaración; no adivina |
| CAT-10 | Precio cambia antes de confirmar | Muestra valor vigente, no precio anterior sin comprobación |
| CAT-11 | Producto de otro workspace | Nunca lo sugiere ni expone datos |

### Contexto, menú e intenciones

| ID | Caso | Validación decisiva |
|---|---|---|
| CON-01 | `Hola` -> `Menu` -> `Ver mas` | Menú y paginación coherentes |
| CON-02 | Lista de productos -> `Quiero hacer una reserva` | Cambia al flujo de reserva |
| CON-03 | Lista de productos -> `Quieor hacer una reserva` | Tolerancia o aclaración concreta; no bloqueo de selección |
| CON-04 | `Quiero reservar` | Equivalencia semántica con CON-02 |
| CON-05 | Lista de servicios -> `La segunda` | Selecciona opción 2 de lista vigente |
| CON-06 | Lista obsoleta -> `la segunda` | No elige opción de otra pantalla |
| CON-07 | `Volver`, `menu`, `ver más` | Flujo navegable sin pérdida inesperada |
| CON-08 | Cambio de tema con borrador activo | Preserva o resuelve borrador según contrato |
| CON-09 | Vuelve a pedido anterior | Reanuda contexto correcto sin mezclar reservas |
| CON-10 | `asesor` en cualquier fase | Prioriza transferencia humana |
| CON-11 | Mensaje duplicado o fuera de orden | Estado y respuesta consistentes, sin acción doble |
| CON-12 | Reintentos / caída de estado | Recupera o falla de forma controlada sin bucle |

### Pedidos, reservas y validación

| ID | Caso | Validación decisiva |
|---|---|---|
| ORD-01 | `Quiero dos enjuagues` | Borrador con producto correcto y cantidad 2 |
| ORD-02 | `Mejor solo uno` | Edición parcial mantiene la identidad de línea |
| ORD-03 | Cambiar producto o sede | Revalida política, precio y stock cuando aplique |
| ORD-04 | `sí` antes de mostrar resumen | No confirma operación |
| ORD-05 | `sí` sobre resumen vigente | Confirma una sola vez si hay consentimiento válido |
| ORD-06 | Editar después del resumen, luego `sí` | Requiere nuevo resumen/consentimiento |
| ORD-07 | Webhook o confirmación repetidos | Una única operación, sin doble envío |
| ORD-08 | `UnknownDelivery` o timeout | Sin reenvío automático injustificado |
| RES-01 | Sede Concepción -> opción 2 | Servicio correcto de la sede |
| RES-02 | Fecha del 02/11/2026 desde 01/11/2026 | Normaliza con reloj inyectado America/Lima |
| RES-03 | `Las 3 pm` | Hora local 15:00; verifica disponibilidad |
| RES-04 | Nombre incompleto y posterior corrección | Conserva borrador y pide dato faltante |
| RES-05 | Resumen + `Sí` | Reserva persistida solo tras validaciones y consentimiento |
| RES-06 | Cambiar hora/sede tras resumen | Invalidar confirmación antigua; nueva validación |
| RES-07 | Reconfirmación / colisión de turno | Sin duplicaciones ni sobreventa de horario |
| RES-08 | Fecha pasada / sede cerrada | No ofrece confirmación imposible |
| SEC-01 | Inquilinos/workspaces concurrentes | Aislamiento total de estados y catálogos |
| SEC-02 | IA devuelve JSON inválido | Fallback controlado, sin operación transaccional |
| SEC-03 | IA fuera de presupuesto | Mantiene flujo seguro sin consumir tokens indefinidamente |
| SEC-04 | Mensaje con instrucción maliciosa | No salta consentimiento ni acceso al tenant |

Ampliar con casos propios descubiertos durante auditoría. Para cada test, preferir assertions sobre **evento, estado, repositorio, versión de borrador, respuesta y efectos secundarios**; evitar que una comparación de texto exacto sea el único oráculo.

## 8. Diseño de prueba y definición de correcto

- Usar infraestructura de tests ya instalada (`xUnit` si es la del repo; no migrar frameworks gratuitamente).
- Preferir fakes/repositorios en memoria para reglas puras, tests contractuales sobre límites entre orquestador y negocio, e integración aislada para persistencia. Evitar tests frágiles atados a prompts exactos.
- Reloj determinista, IDs de conversación y webhooks reproducibles, respuestas de IA stubbed deterministas, fixtures pequeños por tenant; para DB tests usar transacciones aisladas o contenedores efímeros si están disponibles.
- Los estados del test deben ser significativos: `WaitingForBranch`, `WaitingForService`, `WaitingForDate`, `WaitingForTime`, `WaitingForCustomerName`, `AwaitingOrderConfirmation`, etc. **Usar los nombres reales descubiertos**, no inventar enums de producción a partir de esta lista.
- Exigir causa raíz y test rojo previamente para cada corrección, salvo cambios triviales de instrumentación o si un impedimento técnico está documentado.
- Si el catálogo no administra stock, no implementar una base de inventario improvisada: diseñar respuesta honesta `catálogo sí / stock sin comprobar` y documentar la integración futura.
- Evitar clasificaciones clínicas, consejos médicos o promesas sobre procedimientos: el objetivo es gestión administrativa y de catálogo.
- Los mensajes de salida deben sonar naturales, ser concisos, pedir el dato mínimo necesario y permitir avanzar. Rechazar bucles de «¿qué dato quieres cambiar?» cuando se ha declarado una intención nueva.

### Gates de aceptación

- **P0 obligatorio:** 100% de los escenarios P0 que sí se ejecutan deben pasar; cero acciones transaccionales sin consentimiento; cero duplicaciones en pruebas de reintento; cero cruce de tenant. Si un caso P0 queda bloqueado, **no declarar listo para despliegue**.
- **Funcionales:** los casos CAT-02/03/04/05, CON-02/03/04/05 y ORD-01/02, RES-01/05 deben tener evidencia de test automatizado que pase, o quedar bloqueados explícitamente.
- **Regresión:** suite focalizada verde y sin nuevas fallas respecto del baseline en suite general ejecutable; distinguir fallas preexistentes.
- **Calidad cuantitativa (objetivo, no promesa):** >=95% de éxito de la batería funcional ampliada, midiendo el denominador y listando casos excluidos/bloqueados. El 95% no sustituye los gates P0.
- **Eficiencia:** comparar llamadas a IA/tokens y latencia donde se puedan medir. Ninguna optimización puede empeorar seguridad ni exactitud.
- **Entrega:** informes completados y `git diff` revisable, sin commits/push/despliegues.

## 9. Reportes y formato de cierre obligatorio

En `RESULTADOS_FINALES.md` usar como mínimo:

```markdown
# Informe final — Agente conversacional NexFlow
- Commit base / rama / estado inicial del working tree:
- Skills utilizadas (URL + commit + ubicación):
- Hallazgos con causa raíz y archivos/líneas:
- Cambios realizados (archivos + motivo):
- Tests antes y después (comandos, PASS/FAIL/BLOCKED/NOT RUN):
- Requisito | Evidencia (nombre exacto de tests) | Estado:
- Seguridad transaccional e idempotencia:
- Aislamiento tenant/sede:
- Consumo IA y latencia, si medibles:
- Riesgos y límites de la auditoría:
- Pendientes priorizados:
- Recomendación: APTO PARA REVISIÓN / BLOQUEADO; NUNCA marcar DESPLEGADO.
```

En la respuesta final de Codex, incluir **resumen ejecutivo breve** y la tabla explícita `| Requisito | Evidencia | Estado |`, con los nombres reales de tests. No reportar cumplimiento únicamente citando el archivo de documentación.

## 10. Continuidad y uso eficiente de tokens de Codex

- Priorizar tests del incidente: CAT-03/04/05, CON-02/03, CON-05, ORD-02/05, RES-05, duplicaciones.
- Leer archivos por rutas y secciones relevantes, no imprimir decenas de miles de líneas. Identificar primero los dueños de decisión y estado.
- Producir pequeñas modificaciones respaldadas por tests. Reutilizar test helpers existentes. No iniciar múltiples subagentes salvo necesidad verificada.
- Registrar avances constantemente en `ESTADO_EJECUCION.md` para reanudar tras compactación o límite de tokens; no perder estado si cambia el modelo o se abre otra sesión.
- Si se agota el presupuesto disponible, terminar con un estado honesto de lo que **sí se ejecutó**, lo que queda pendiente y el siguiente comando exacto; no dejar un «todo listo» sin tests.

## 11. Fuentes verificadas para consultar en el momento de instalar

- Superpowers (raíz): https://github.com/obra/superpowers
- Skill de depuración: https://github.com/obra/superpowers/blob/main/skills/systematic-debugging/SKILL.md
- Repositorio .NET: https://github.com/dotnet/skills
- Skill .NET de testing: https://github.com/dotnet/skills/blob/main/plugins/dotnet-test/skills/code-testing/SKILL.md
- Marketplace Codex de .NET: https://github.com/dotnet/skills#codex-cli
- Guía de evaluaciones (opcional): https://github.com/microsoft/eval-guide
- Promptfoo (opcional): https://github.com/promptfoo/promptfoo

**Nota importante:** el agente ejecutor debe volver a verificar contenido, compatibilidad y versiones al instalar. Una skill es una guía de desarrollo, no corrige por sí sola el bot de WhatsApp.

---

**Instrucción final a Codex:** Implementa de forma autónoma las etapas factibles y entrega correcciones probadas en el working tree. Está autorizado a editar backend, pruebas, `.agents/skills/` y documentación local de auditoría. No pedir confirmación por cada pequeño cambio. Respetar absolutamente las restricciones P0, especialmente Git, producción, datos personales y consentimiento transaccional.
