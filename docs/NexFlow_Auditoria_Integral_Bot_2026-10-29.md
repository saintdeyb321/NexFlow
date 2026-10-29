# NexFlow — Auditoría técnica transversal del bot de WhatsApp

**Fecha:** 29 de octubre de 2026  
**Fuente:** ZIP `NexFlow-main.zip` facilitado por el propietario, contrastado con el commit de GitHub `7b601bfe4a7cc1aea4cb4c8d8cd28e94580b59c5` (CORRECCIÓN CONVERSACIONAL).  
**Alcance:** inspección estática de la ruta de automatización, interpretación, módulos/licencias, lecturas de negocio, reservas, pedidos, salida, recuperación, proveedores, presupuesto y pruebas asociadas. **No** equivale a una revisión manual línea por línea de los 433 archivos C# ni a una prueba en tiempo real.

## 1. Identidad de la versión revisada

- Los tres commits recientes de GitHub al momento de la consulta son `7b601bfe4a7cc1aea4cb4c8d8cd28e94580b59c5`, `74de852f0249ead0ec6571a14754bd77dfebf627`, `a8062cb8f4f7da97150b91f5d478ffb0d2dce051`.
- El commit `7b601bf` contiene los cambios de B6.1 en interpretación, presentación, políticas, resolución de comercio, orquestación, recuperación, flujo y pruebas, además de documentación.
- Se comparó el hash Git de los archivos del ZIP `AiInterpreter.cs` (`09a362c…`), `CommerceTurnResolver.cs` (`08e61f5…`) y `VirtualSecretaryClosureTests.cs` (`fac8eb6…`) con sus blobs publicados para `7b601bf`: **los tres coinciden**. No se verificó el hash de cada archivo del ZIP.
- **Estado operacional distinto del código publicado:** la última API confirmada por el usuario en Oracle es `b581e58`, no `7b601bf`; no se efectuó inspección directa del servidor en esta auditoría.

## 2. Dictamen

**El parche B6.1 mejora deficiencias reales, pero NO está certificado como secretaria virtual conversacional completa ni preparado para clientes pagos.** Puede prepararse un **piloto DEMO controlado**, *después* de los bloqueadores de seguridad y certificaciones de imagen, y siempre distinguiendo pruebas reales de pruebas con interpretaciones simuladas. Las correcciones de catálogo global, edición y referencias tienen fundamento en el código; faltan respuestas contextuales naturales y aceptación con Gemini/Groq reales.

### Lo que está bien planteado y debe conservarse

- La identidad del workspace se resuelve a partir de la instancia recibida, se compara con el workspace declarado y se valida la vigencia de la licencia (`IncomingMessageGuard.cs:36–64`).
- `EntitlementService.cs:58–94,200–215` calcula módulos efectivos desde licencia y módulos activos; `AiIntentAccess.GetDeniedResponse` y los flujos hacen otras comprobaciones en ejecución (`AiInterpreter.cs:273–289`, `OrderFlow.cs:176–179`).
- `BusinessReadTools.cs:15–24,43–102` ofrece una fachada de **solo lectura**, vinculada al workspace y limitada a 3 lecturas/8 segundos, con verificación de permisos.
- Las reservas y los pedidos exigen comprobaciones del backend, no consentimientos propuestos por el LLM; los pedidos usan fingerprint/revisión vigente (`ConversationQuestion.cs:43–53`, `OrderFlow.cs:120–143,167–194`).
- Para el catálogo existe una capa que conserva datos verificados (`OfferingResponseFormatter.cs:7–73`), con separación de precio/duración y aclaración de disponibilidad por sede.
- La edición parcial y el flujo de reservas revalidan dependencias y tienen pruebas de regresión; la protección final debe medirse sobre respuestas y estado persistido, no solo sobre una directiva inyectada.
- `AiTelemetry.cs:11–79` registra latencia, intentos y uso de tokens cuando el proveedor lo informa.

## 3. Hallazgos priorizados

### H1 · ALTO — El bot no dispone de herramientas dinámicas por licencia en el modelo

**Evidencia:** `IAiProvider.cs:15–29` declara `AiTool` y `AiToolCall`, pero `GeminiAiProvider.cs:45–50,89–104,138–145` recibe un parámetro `tools` sin incluirlo en el payload ni devolver una llamada de herramienta; `GroqAiProvider.cs:89–93` lanza `NotImplementedException` para el chat con herramientas. `AiRouter.cs:30–56` usa `GenerateTextAsync`. `BusinessReadTools` sí existe, pero es llamado directamente desde rutas de `ChatFlow` y no mediante elección semántica del modelo (`ConversationFlows.cs:840–979`).

**Impacto:** no existe hoy una herramienta genérica que el modelo elija de un menú generado a partir de la licencia. Las capacidades de catálogo/FAQ/sedes/horarios/reservas/pedidos se implementan como rutas de la aplicación. Esto limita la adaptación contextual y no cumple literalmente la expectativa de “herramientas de todos los módulos según licencia”. **No significa** que las operaciones de reserva/pedido no funcionen.

**Recomendación mínima:** inventariar capacidades efectivas y crear un contrato de herramientas de negocio **permitidas por workspace**, con entradas/salidas tipadas, validación servidor, límite de lecturas, evidencia factual y separación estricta `read` frente a `write`. No abrir operaciones mutantes a ejecución autónoma: creación/cancelación requiere confirmación transaccional y rutas existentes. Puede iniciarse con enrutamiento semántico **validado** hacia los servicios actuales, sin introducir otro framework de agentes.

### H2 · ALTO — La redacción contextual por IA no está operativa de forma general

**Evidencia:** `ResponseComposer.cs:14–21` declara `EnableAi` en falso por defecto. La única composición mediante proveedor reorganiza ids/bloques de hechos para comparaciones específicas (`ResponseComposer.cs:44–69`); otra ruta añade prefijos fijos (`:75–86`). Los catálogos, preguntas y sedes siguen en formateadores/plantillas (`SecretaryPresentation.cs:6–25`, `OfferingResponseFormatter.cs:47–71`). `ChatFlow` entrega hechos literalmente (`ConversationFlows.cs:965–973`).

**Impacto:** incluso con intención correcta, el usuario recibe un flujo parcialmente rígido; los tests de estado no demuestran naturalidad. **No habilitar simplemente generación libre** sobre datos comerciales.

**Corrección:** introducir una composición contextual **acotada y optativa**, que use un sobre factual inmutable (intención, restricciones, hechos citados del backend, pregunta útil, estado, módulos). Dejar hechos/precios/enlaces/horarios/operaciones renderizados por backend o validados token a token; permitir al modelo variar únicamente explicación y transición. Comparar versión nueva y fallback en una conversación DEMO; activar gradualmente solo si hay control de costos y auditoría de salida.

### H3 · ALTO — Interpretación híbrida con múltiples gramáticas de frases

**Evidencia:** antes de consultar el LLM, `TurnUnderstandingPipeline.cs:44–66,94–161` clasifica y cortocircuita rutas; `DialoguePolicy.cs:21–51` usa condiciones por vocabulario; `CommerceTurnResolver.cs:106–170` usa conjuntos amplios de palabras; `ConversationRecoveryService.cs:75–84,198–242` incluye entradas especiales y regex. Parte es necesaria para consentimiento y seguridad, pero parte mezcla semántica con flujo.

**Impacto:** los cortocircuitos pueden ganar prioridad a una intención conversacional novedosa y los patrones crecen indefinidamente. **No hay prueba de que cada condición sea un fallo**, pero la distribución de lógica aumenta riesgo de conflictos y mantenimiento.

**Corrección:** clasificar reglas por autoridad: (A) seguridad/confirmación/idempotencia, (B) datos y referencias verificadas, (C) lingüística heurística. Mantener A y B; para C definir una vía semántica única cuando la regla no sea inequívoca. Medir qué rutas realmente ahorran IA sin romper conversaciones. Eliminar redundancias solo después de comparar el comportamiento.

### H4 · MEDIO-ALTO — Presupuesto por llamadas no equivale a límite de tokens o gasto

**Evidencia:** `AssistantRecoveryOptions.cs:3–10` fija `ClientAiCallsPerMinute=6` y `WorkspaceAiCallsPerMinute=60`; `FirestoreConversationStateRepository.cs:61–90` contabiliza **identidades de mensajes**, no tokens HTTP. `AiTelemetry.cs:58–79` mide tokens informados, pero no los usa para impedir gastos. `AiRouter.cs:34–56` puede hacer fallback y `GeminiAiProvider.cs:108–116` también cambia modelo: una entrada admitida puede consumir múltiples intentos HTTP, y la cuota contabiliza una admisión, no cada intento.

**Corrección:** métricas por workspace de costo estimado, `prompt/completion/total` por intento, límite de costo diario/mensual, tope de reintentos/latencia global y circuit breaker básico por proveedor. No trasladar el costo de un fallback a una cuota artificial por mensaje si existe control financiero independiente; conservar el control actual de tasa.

### H5 · MEDIO-ALTO — Prompt de interpretación grande, cambiante y con ejemplos literales

**Evidencia:** `AiInterpreter.cs:114–179` genera un prompt de extensa lógica en español, con lista de directivas, múltiples casos y contexto de hasta 12 pistas de sedes, servicios y productos y 10 ítems de borrador (`:167–177`). El fragmento fuente de construcción anterior al contexto ocupa ~11.133 caracteres, **no 11.133 tokens**. `GroqAiProvider.cs:43–55` y `GeminiAiProvider.cs:95–103` limitan salida a 1024 tokens, sin que se vea una política adaptativa por complejidad.

**Riesgo:** coste por mensaje, mantenimiento difícil y respuestas JSON truncadas en entradas compuestas; no está demostrado que suceda en DEMO.

**Corrección:** separar contrato JSON invariable de ejemplos; enviar únicamente opciones/entidades y resumen de contexto relevantes; agrupar instrucciones generales sin fraseología repetitiva. Registrar longitud de prompt y tasa de `InvalidResponse` por versión. Ajustar presupuesto de salida con criterio y controles, no incrementarlo indiscriminadamente.

### H6 · MEDIO-ALTO — Confirmaciones afirmativas demasiado amplias para el objetivo de consentimiento inequívoco

**Evidencia:** `ConversationConfirmation.cs:8–9` acepta `"de acuerdo"` además de `"si"`, `"si confirmo"`, `"confirmo"`, `"confirmar"`; `ConversationQuestion.cs:50–53` requiere la pregunta de confirmación vigente, y `OrderFlow.cs:124–143` también exige una revisión vigente. **Esos controles existentes reducen mucho el riesgo**.

**Riesgo:** responder «de acuerdo» a un resumen o una explicación puede resultar menos inequívoco que «Confirmar». Evaluar con el usuario si «de acuerdo» debe considerarse consentimiento transaccional; no afirmar que ya crea registros erróneos sin reproducción.

**Corrección:** asegurar política de consentimiento *explícito para efecto comercial* con la menor ambigüedad posible y preservar la distinción entre aceptar información, seleccionar datos y autorizar escritura.

### H7 · MEDIO — Licenciamiento de módulo ≠ verificación de capacidades finas del módulo

**Evidencia:** `EntitlementService.cs:100–169` implementa matriz de capacidades para usuarios autenticados, mientras `AiIntentAccess.cs` (`AiInterpreter.cs:273–289`) y `BusinessReadTools.cs:58–68` aplican permisos mediante **códigos de módulos**. Para el webhook esto puede ser exactamente lo deseado, porque las capacidades se diseñaron para roles humanos; requiere especificación explícita.

**Recomendación:** documentar una matriz `módulo de licencia → capacidades de secretaria virtual → lectura/efecto → servicio responsable → restricción de canal`. No dar por hecho que activar `RESERVATIONS` autoriza automáticamente cancelar reservas existentes sin pasar por el contrato de ese flujo. Agregar un pequeño test de matriz por licencia, no cientos de pruebas de frases.

### H8 · MEDIO — Algunas rutas funcionales de conocimiento no tienen cobertura de composición contextual

**Evidencia:** `ChatFlow.ProcessAsync` (`ConversationFlows.cs:840–979`) ofrece PDF, FAQ, perfil, sedes, servicios y horarios mediante lectura factual, y puede responder con mensajes propios, mientras `ResponseComposer` (`:44–69`) limita su composición de proveedor a consultas de productos/servicios con `VerifiedResponseLayout` acotado. La interacción entre las rutas debe verificarse en DEMO para no crear resúmenes redundantes ni perder referencia de menú.

**Corrección:** una interfaz de respuesta única de tipo `Facts + Scope + NextAction + PresentationPolicy` para **mensajes informativos**; conservar los canales especiales de envío de PDF, handoff y confirmación y evitar una segunda generación por defecto.

### H9 · MEDIO — Ausencia de prueba semántica real en la evidencia recibida

**Evidencia:** existe `VirtualSecretaryClosureTests.cs` y otros tests; los tests de regresión de B6.1 reportados por Codex validan rutas y se apoyan en interpretaciones controladas. No se facilitó un registro con el proveedor real reconociendo de punta a punta las frases del 27/10, ni el resultado de WhatsApp DEMO con `7b601bf`.

**Corrección:** validación real corta con una sola conversación controlada y checklist de 8–10 turnos, observando texto visible, borrador y efecto persistido. No construir otro staging ni ejecutar cientos de simulaciones: trabajar sobre el DEMO existente una vez certificado el candidato.

## 4. Matriz de módulos: qué hay y qué falta

| Módulo/capacidad | Ruta visible | Estado de evidencia |
|---|---|---|
| `CATALOG`, `SERVICES` | `ChatFlow`, `BusinessReadTools`, `OfferingResponseFormatter` | Lectura por licencia y formato verificado existen; falta composición contextual amplia. |
| `LOCATIONS`, `BUSINESS_HOURS`, `FAQ`, perfil | `BusinessReadTools`, `ChatFlow` | Consultas autorizadas por rutas; no existen herramientas dinámicas elegidas por el modelo. |
| `RESERVATIONS` + `SERVICES` | `ConversationFlows`, `ReservationEngine`, `ExistingReservationFlow` | Comprobaciones de módulo y consentimiento; validar frases, edición, recursos, cancelación/gestión según licencia. |
| `ORDERS` + `CATALOG` | `CommerceTurnResolver`, `OrderFlow` | Borrador, revisión, confirmación y revalidación; validar interpretación coloquial con proveedor real. |
| `REQUESTS` | `AiResponseOrchestrator`, `RequestFlow` | Ruta activada por código de módulo; verificar consentimiento y escalada con mensajes reales. |
| `CONVERSATIONS`/humano | `ConversationStateService`, `SupportFlow`, `OutgoingMessageService` | Se observan controles de takeover; probar que un mensaje durante takeover no emita IA. |
| Capacidades futuras y subcapacidades | `EntitlementService.GetEffectiveCapabilitiesAsync` | No existe un manifiesto dinámico de herramientas consumido por Gemini/Groq. |

## 5. Correcciones recomendadas por fases (sin reescritura masiva)

**Fase A — Cerrar bloqueadores (Codex, código):** (1) revisar consentimiento `"de acuerdo"` versus `"Confirmar"` con test focalizado; (2) revisar precedencias de heurísticas para preguntas durante reservas y pedidos; (3) registrar el motivo de cada ruta sin guardar texto sensible. Mantener contratos transaccionales e idempotencia.

**Fase B — IA conversacional (Codex, código):** (1) unificar contexto compacto de propósito, pregunta, referencias válidas y hechos; (2) permitir redacción contextual acotada para mensajes informativos; (3) medir el coste y establecer fallback legible; (4) reducir redundancia del prompt y heurísticas tras evidencia.

**Fase C — Herramientas y licencias (Codex, incremental):** crear una matriz de capacidades permitidas por workspace y adaptadores tipados para lectura. Aprovechar `BusinessReadTools` y los flujos existentes. No exponer escrituras a ejecuciones arbitrarias del modelo. Conservar vigencia de licencia, validaciones por sede y auditoría de efectos.

**Fase D — Certificación y DEMO:** pruebas focalizadas + los workflows necesarios para el SHA final + imagen ARM64; desplegar **solo la API** después de preflight y respaldo; verificar una conversación real con el proveedor configurado (sede, catálogo 5/4, hora cercana, editar sede, ordinal «La 1», pedido coloquial, consentimiento y transferencia humana). Inspeccionar la persistencia, no solo capturas del chat.

## 6. Criterios de NO-GO / limitaciones

- No declarar el bot “perfecto” ni listo para clientes pagos antes de una prueba real con el proveedor instalado y el canal WhatsApp.
- No habilitar generación libre de precios/horarios/operaciones o función de escritura sin consentimiento.
- No borrar guardas de seguridad ni eliminar regex específicas de protocolos/confirmaciones con el argumento de quitar “todas las reglas”.
- No actualizar producción desde GitHub `main` directamente: usar SHA e imagen ARM64 verificados y un procedimiento de cambio controlado, sin tocar infraestructura compartida.
- No confundir “módulo activo” con un derecho genérico a todas las acciones sobre ese módulo: verificar cada flujo y restricciones.

## 7. Evidencia ejecutada y no ejecutada

**Ejecutado:** lectura directa del ZIP; inspección de clases y vínculos principales; búsqueda de invocaciones de IA/herramientas; comprobación de hash Git de tres archivos contra el commit GitHub.  
**Reportado por Codex, pero no reejecutado aquí:** 347/347 tests relacionados y compilación `dotnet build` aprobada.  
**NO EJECUTADO:** compilación/pruebas .NET en este entorno (no tiene `dotnet`), llamadas a Gemini/Groq reales, conexión a Oracle, simulación WhatsApp, auditoría de tráfico o coste real.  
**Confianza:** alta en la constatación de rutas y clases indicadas; media en sus consecuencias operacionales, que requieren reproducción.
