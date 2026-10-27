# PROMPT PARA CODEX — NexFlow: corregir fallos conversacionales observados en WhatsApp DEMO

> **Pega este texto íntegro a Codex con acceso al repositorio local.** Adjunta también `NexFlow_Diagnostico_WhatsApp_MVP_2026-10-26.md`. No hace falta pegar teléfonos ni nombres del chat real.

## Rol, base y límites

Actúa como ingeniero principal de backend .NET 10 y responsable de regresiones del asistente multitenant NexFlow. **Tu misión es corregir defectos demostrados por una conversación WhatsApp real del negocio DEMO, no rehacer toda la arquitectura.** El usuario quiere un MVP útil, natural y seguro, sin ciclos interminables.

**Repositorio local principal:** `C:\Proyectos\NexFlow`. GitHub: `saintdeyb321/NexFlow`, `main`. **Versión actualmente desplegada en Oracle:** `cedd19469ca715616eb53d2cfd4d47732870b94e`; `main` visto posteriormente en `20416e2e7608f072b083f0b97c6a26c563fc371c`, que añade custodia CI/documentación. **Primero** inspecciona `git status`, `git rev-parse HEAD`, rama, diferencias y existencia de cambios ajenos; no supongas que el working tree es igual al despliegue. Conservar todo cambio del usuario.

**Restricciones innegociables:**

**Restricción arquitectónica prioritaria — NO CREAR UN BOT BASADO EN REGLAS DE FRASES:**

- **PROHIBIDO** crear o ampliar árboles de intención por palabras/frases, cascadas de `if/else`, switches de expresiones de usuario, diccionarios de sinónimos por caso, listas de expresiones permitidas, `regex` crecientes, menús obligatorios o bibliotecas de respuestas fijas como sustituto de comprensión del lenguaje natural. **No "arreglar" cada caso agregando la frase exacta del test a otra lista.** No introducir un nuevo intent router heurístico paralelo al existente.
- Los mensajes de la conversación son **ejemplos de fallos para tests**, no patrones literales que el sistema deba buscar. Probar paráfrasis nuevas y no vistas, faltas ortográficas, autorreparaciones, negaciones, varias entidades, referencias y cambios de tema. La solución debe generalizar sin modificar código por cada forma nueva de hablar.
- Utilizar la **IA semántica contextual existente** para proponer una representación estructurada acotada del acto conversacional (por ejemplo: `FinishOrder`, `CorrectDraftField`, `AddItems`, `AskStock`, `AskInformation`, `CancelDraft`, `HumanHelp`), objetivo, entidades mencionadas, negadas/corregidas, evidencia literal y grado de ambigüedad. **Estos son actos de negocio, no frases predefinidas.** Antes de añadir un contrato nuevo, evaluar la ampliación mínima de los contratos actuales de `AiInterpretation` / `TurnUnderstanding` / `OrderDraftProposal` / `BookingDraftProposal`. No delegar confirmaciones, identidad ni efectos a la IA.
- El backend **sigue siendo determinista para la seguridad y las transacciones**: valida cada propuesta con `WorkspaceId`, `SourceMessageId`, `StateVersion`, licencias, catálogo autorizado de la sede, cantidades, precios, agenda, fingerprint, consentimiento e idempotencia. Propuesta inválida, desconocida, contradicción o IA no disponible ⇒ **pregunta de aclaración relevante sin mutación**; nunca fallback que incremente artículos o ejecute operaciones.
- Se permiten validadores formales para **datos estructurados** (IDs, fechas, horarios, números, estados y botones/CTA explícitos como `Confirmar`, `Editar`, `Descartar`) y **formatos de salida** para resúmenes transaccionales. Estas piezas **no deben decidir el significado de una oración arbitraria por coincidencias léxicas**. Los textos visibles se construyen con datos verificados y una estructura clara, no con un árbol de respuestas por escenario.
- Las correcciones de escritura y selección de entidades deben basarse en los **nombres/aliases del catálogo o sedes autorizados en tiempo de ejecución**, con puntuación de coincidencia y rechazo por ambigüedad; **no** agregar a mano una tabla `bocal→bucal`, `conce→Concepción`, etc. por cada prueba. Ningún fuzzy match entre tenants ni selección insegura por mera similitud.
- **Criterio de revisión:** si la corrección principal consiste en "sumar más palabras al whitelist", "añadir otro regex de intención" o "crear un árbol de respuestas", RECHAZAR esa implementación y explicar el rediseño mínimo de interpretación contextual requerido. Mantener sin cambios los controles operacionales de seguridad.


- Solo trabajar en **código y tests del repositorio local**. **No** SSH, Oracle, Docker productivo, Evolution real, n8n, Firestore productivo, `dotnet ef database update`, nuevas claves, cambios de billing, migraciones, despliegues, pushes ni commits sin orden posterior expresa.
- **No** desactivar mecanismos de seguridad para permitir cambios de reservas confirmadas. `ExistingReservationFlow` exige verificación humana por diseño; conservarla. Se permite corregir **únicamente borradores aún no confirmados** sin autenticación adicional.
- La IA **interpreta/redacta bajo límites**; backend verifica licencia, workspace, autorización, catálogo, disponibilidad, estado, consentimiento, precio, huella de cotización, idempotencia y escritura. No permitir que Gemini/Groq invente productos, stock, fechas o autorice commits.
- No romper aislamiento tenant; `SourceMessageId`, CAS/StateVersion, Outbox/Inbound, `UnknownDelivery` (no reenviar), takeover humano, F7-01, recuperación de borradores, licencias revocadas, órdenes `PendingReview`, integridad de reserva ni formato de precios.
- No implementar funcionalidades ficticias o depender de IA pagada para las pruebas. Usar proveedores falsos deterministas en tests y datos sintéticos. Sin secretos/PII en logs, tests o reportes.

## Síntomas a reproducir (crear tests fallidos antes de modificar lógica)

**P0-1 Pedidos bloqueados:** con un borrador que contiene 1× hilo dental + 1× enjuague, sede válida y subtotal, «Solo eso», «Envía la solicitud», «Envía solicitud para revisión», «Que envíes la solicitud» provocan «No identifiqué un producto activo inequívoco»; repitiendo «El hilo dental» se suma otra unidad sin pedirla. Esperado: identificar intención de *finalizar* contextual; mostrar resumen actual, pedir confirmación final si aún no existe; `Confirmar`/`Sí` en paso de confirmación y con fingerprint vigente crea **exactamente un** registro `PendingReview` y una notificación idempotente, jamás venta/pago confirmado. `Solo eso` por sí solo no debe actuar como consentimiento final.

**P0-2 Corrección de servicio durante reserva sin confirmar:** cuando el borrador eligió Limpieza dental y espera fecha, «Espera, me confundí, no quiero limpieza dental, quiero extracción dental» debe sustituir el `ServiceId` por Extracción dental **si está disponible en esa sede**, invalidar tiempo/elecciones incompatibles, conservar sede y datos compatibles y solicitar fecha/horario pertinente. Segundo mensaje «Que no quiero limpieza, quiero extracción» nunca debe repetir la ficha de Limpieza. Un cambio exige nueva confirmación antes del commit.

**P1-3 Matching coloquial:** «Hilo dental y enjuague bocal» debe reconocer ambos si las coincidencias del catálogo son inequívocas; «sanj eronimo» y «En conce» deben resolverse semánticamente contra entidades/aliases **dinámicos** del negocio sin tabla de erratas codificada a mano y solo si no hay colisión. Si no está claro, preguntar solo la entidad ambigua preservando borrador, sin inventar IDs.

**P1-4 Disponibilidad y reparación:** «¿Está disponible el hilo dental?» debe referirse exclusivamente a ese producto. Si no existe inventario real, responder: catalogado/activo ≠ existencias confirmadas. Ante «Dije si está disponible o no» usar la referencia actual y aclarar, sin reset a un menú genérico. No confundir disponibilidad de servicio por sede con hueco de agenda.

**P1-5 Catálogo completo:** San Jerónimo devolvió 5 servicios reservables; Concepción devolvió 4. **No asumir defecto hasta leer fixtures/filtrado real.** Comparar `IsActive`, `RequiresReservation`, `DurationInMinutes`, `LocationScope/LocationIds` por ID en datos autorizados. Corregir omisiones **solo si** los cinco cumplen criterios en Concepción; cuando hay >5, mostrar paginación `Ver más` y total, manteniendo orden/IDs estables. No manipular flags ni listar servicios no reservables.

**P1-6 UX secretaria virtual:** resultados de negocio redactados con párrafos, línea en blanco entre secciones, emojis discretos, listas, encabezado específico y **una sola pregunta/CTA procesable**; **no** plantillas de respuesta vinculadas a frases específicas de entrada. En reservas usar `Confirmar` / `Editar` / `Descartar` sobre **el borrador**, conservar «Sí» como alias bajo confirmación válida y distinguir cancelación del borrador de cancelar reserva existente. En pedidos usar `Confirmar` / `Editar` / `Descartar`; informar siempre que los artículos se envían para revisión, no se confirma stock ni pago. Verificar serialización real de `\n\n` desde composición a gateway (con test, no API real).

**P1-7 Fechas y handoff:** «tras pasado mañana» es ambiguo; pedir fecha absoluta antes de prometer 28/29 de octubre. Una reserva **ya confirmada** que pide editarla conserva gestión humana segura; no cambiarla silenciosamente. Tras «Asesor», probar que takeover activo suprime respuestas del bot hasta liberación explícita.

## Código de partida a examinar (no modificar a ciegas)

- `backend/NexFlow.Application/Features/Automation/Dialogue/Understanding/OrderMentionResolver.cs` → `IsSubmit`, `Resolve`, `Match`, `Split` y expresiones compuestas. La gramática `IsSubmit` tiene un allowlist de todas las palabras demasiado estrecho para «envía solicitud para revisión».
- `.../Dialogue/Understanding/CommerceTurnResolver.cs` → precedencia `submit`, `summary`, `order`, `readOnly`, correferencia y elección de producto; no confundir finalización con selección de artículo.
- `.../ProcessMessage/Services/Flows/OrderFlow.cs` → `ORDER_REVIEW`, `ORDER_CONFIRM`, `ORDER_COMMIT`, huella, licencia y `TryResumeAsync`. Preservar F7-01, estado durable y `PendingReview`.
- `.../Dialogue/Policy/OrderDraftMerger.cs` → semántica `Add`/`Set`, quote e invalidación de consentimiento después de editar.
- `.../Dialogue/Policy/ConversationConfirmation.cs` y `.../Dialogue/Policy/DialoguePolicy.cs` → afirmaciones, negación/corrección, instrucciones interrumpidas.
- `.../Dialogue/Understanding/TurnUnderstandingPipeline.cs`, `.../Dialogue/Understanding/AiEvidenceValidator.cs`, `.../AI/Interpretation/AiInterpreter.cs` → `ChangeService`, evidencia y fallback. El validador actual de `ChangeService` restringe los mensajes sin `cambi...` u «otro servicio»; ampliar con hechos/estructuras **validados** sin aceptar texto inventado por el modelo.
- `.../Dialogue/Policy/BookingDraftMerger.cs` y `.../ProcessMessage/Services/Flows/ConversationFlows.cs` → sustitución segura de servicio y limpieza de hora vinculada.
- `.../ProcessMessage/Services/Flows/ExistingReservationFlow.cs` → **no** quitar control de identidad ni habilitar gestión de reservas registradas.
- `.../Business/Offerings/OfferingService.cs`, `.../Dialogue/Understanding/WorkspaceSlotResolver.cs`, `.../Dialogue/Understanding/EntityMentionResolvers.cs`, `.../Dialogue/Understanding/SpanishTextNormalizer.cs` → filtros por sede, aliases y menú reservable.
- `.../Dialogue/Composition/ResponseComposer.cs`, `.../Dialogue/Composition/OfferingResponseFormatter.cs`, `.../Dialogue/Contracts/ResponsePlan.cs`, `.../ProcessMessage/Services/AiResponseOrchestrator.cs`, `.../ProcessMessage/Services/OutboundMessageService.cs` → plantilla final de mensajes, respeto de saltos y hechos.
- Tests existentes en `backend/NexFlow.Tests/Concierge/RuntimeOrderRequestTests.cs`, `RuntimeNaturalBookingTests.cs`, `ResponseComposerTests.cs`, `CommerceConversationTests.cs`, `ExistingReservationSprint2Tests.cs`, `RuntimeReadToolsTests.cs`, `LinguisticRegression62Tests.cs`, `RuntimeF3ContractV2Tests.cs`; ampliarlos sin reducir cobertura.

## Orden de ejecución requerido

**Paso A — Reproducción y trazas mínimas.** Leer el diagnóstico adjunto y el árbol actual. Identificar para cada escenario `CurrentGoal/CurrentStep`, `Intent`, `Act`, `Directive`, `OrderTurnKind`, propuesta/slot local, `Outcome` y texto final. Crear tests de la transcripción **con identidades ficticias**. Identificar en cada caso qué detector tomó la decisión. No usar el stack productivo.

**Paso B — Arreglos P0 acotados.** Reparar la interpretación del acto de pedido con IA semántica contextual y propuesta estructurada validada contra el estado; **sin añadir gramáticas de variaciones, whitelists, regex ni un segundo árbol de intención**. Evitar incremento al re-mencionar artículo en revisión. Mantener presupuesto/fingerprint/consentimiento/idempotencia con pruebas de reintentos y revocación. Reparar interpretación/merger de cambio de servicio usando evidencia semántica contrastable del mensaje (antiguo negado, nuevo elegido), sin depender de palabras disparadoras, y matcher autorizado; corregir el defecto de precedencia que convierte la corrección en consulta informativa de Limpieza dental.

**Paso C — Formato verificable y comprensión P1.** Crear formatos visuales deterministas de **resultados de negocio verificados** con `\n\n`, títulos, secciones y 1 CTA; **no crear respuestas fijas para posibles frases de entrada**. Verificar en test el texto final enviado al gateway; permitir en UI WhatsApp texto plano + sintaxis `*negrita*` si el canal la conserva. Sin modificar facts sensibles ni delegar el cuerpo de una reserva a redacción libre de la IA. Resolver abreviaturas y typos usando exclusivamente los nombres/aliases dinámicos del catálogo y sedes del workspace, con confianza conservadora y desambiguación; revalidar en cada turno. Nada de listas manuales de erratas del corpus. Reparar respuestas referenciales de stock. Paginación cuando >5, con IDs estables y razón por sede.

**Paso D — Regresión y calidad.** Añadir tests de generalización con paráfrasis inéditas fuera del corpus, además de tests nuevos con casos por tabla; compilar .NET; suite actual no histórica; suites de integración desechables PostgreSQL/Firestore emulador/Redis si están disponibles sin credenciales productivas. Conservar históricos congelados y reportar separadamente su resultado real. Si hubo frontend modificado, ejecutar TS/browser pertinentes. No maquillar PASS si hubo omisiones, timeouts o tests saltados.

## Casos de aceptación obligatorios (mínimo)

1. Estado pedido con 2 ítems: `Solo eso` → revisar/confirmar, ítems intactos, `orders.Count == 0`.
2. `Envía la solicitud` / `Envía solicitud para revisión` → **nunca** «No identifiqué un producto»; antes de enviar, resumen vigente y confirmación segura si falta.
3. `Confirmar` en paso de confirmación actual → **una** orden `PendingReview`, no duplicados por mismo `sourceMessageId` ni doble notificación.
4. Confirmar un hash viejo después de editar cantidad/precio/sede → `NO ORDER`, nueva revisión.
5. Repetir `El hilo dental` tras intento de envío → no pasa de cantidad 1 a 2 sin comando inequívoco; `agrega otro hilo dental` sí aumenta cuando corresponde.
6. «Hilo dental y enjuague bocal» / «También un enjuague bocal» → IDs correctos o clarificación específica; nunca uno inventado.
7. «Me confundí; no quiero limpieza, quiero extracción» → `SelectedServiceId` correcto para sede, hora invalidada si dependiente, no se crea reserva hasta confirmar.
8. «Que no quiero limpieza...» → el resultado no vuelve a ofrecer descripción/precio de Limpieza como respuesta principal.
9. «Quiero editar mi reserva» con cita registrada → asistencia/verificación humana, **cero** cambios automáticos.
10. «¿Está disponible el hilo dental?» / «Dije si está disponible» → solo hilo dental, no promesa de stock, referencia preservada.
11. Sede `En conce` y `sanj eronimo` → resolver únicamente con alias inequívoco; ante duplicidad pedir precisión.
12. Catálogo por sede: 5/4 explicados por flags reales o se descubre y corrige filtro; >5 páginas sin pérdida ni inversión de IDs.
13. «Tras pasado mañana» → clarificar fecha en `America/Lima`; nunca commit basándose en tiempo ambiguo.
14. Mensaje de reserva/pedido con formato: saltos de línea y `*títulos*`, sumas exactas, 1 CTA final, sin tokens arbitrarios/HTML.
15. Human takeover, revocación licencia, Outbox, UnknownDelivery, CAS y multi-tenant mantienen todas sus invariantes sin degradación.

## Diseño deseado de estados y mensajes

**Reserva pendiente:**

```text
🦷 *Revisa tu reserva*

🏥 Sede: {sede_validada}
🩺 Servicio: {servicio_validado}
📅 Fecha: {fecha_validada}
🕕 Hora: {hora_verificada}
👤 Paciente: {nombre_validado}

¿Está correcto?
✅ *Confirmar* — registrar en la agenda
✏️ *Editar* — cambiar un dato
❌ *Descartar* — abandonar este borrador
```

**Pedido pendiente:**

```text
🛍️ *Tu solicitud para revisión*

{items_y_precios_autorizados_en_lineas}

📍 Sede: {sede_validada}
💰 Subtotal: {total_calculado_por_backend}
ℹ️ Sujeto a revisión del negocio; no se confirma stock ni pago.

✅ *Confirmar* — enviar solicitud
✏️ *Editar* — cambiar artículos/cantidades
❌ *Descartar* — borrar borrador
```

Los comandos deben funcionar **solo cuando el estado correspondiente los espera**. «Editar» sobre reserva confirmada no autoriza mutación sin verificación humana. «Descartar» nunca cancela un pedido ya registrado ni una reserva confirmada.

## Qué necesito en tu respuesta final de Codex

1. Diagnóstico confirmado por **causa y archivo/línea** para cada D-01…D-13; diferenciar reproducido, hipótesis descartada y requiere verificación de datos reales. Aclarar particularmente por qué hubo cinco servicios en una sede y cuatro en otra **sin inventar** los flags del negocio.
2. Cambios implementados: lista de archivos y explicación de cómo el estado/acto previene el bucle, conserva borradores y evita duplicaciones.
3. Evidencias de test **ejecutadas de verdad**: comando, total/passed/failed/skipped, suites involucradas, fallos no resueltos y si hubo dependencia externa faltante. No declarar 1538 PASS por recuerdo.
4. Ejemplos de salida de chat para 4 escenarios (pedido completo, corrección reserva, disponibilidad, listado por sede) generados desde el código y fixtures.
5. Riesgos residuales y límites de autoservicio para citas confirmadas, existencias de inventario y fechas ambiguas.
6. SHA actual del árbol y diff de cambios **sin hacer commit, push ni deploy**. Si descubres un P0 de seguridad, detente y repórtalo antes de extender el alcance.

**Definición de terminado:** reproducción de fallos → solución acotada → pruebas verdes/limitaciones explícitas → propuesta lista para auditoría antes de un nuevo despliegue. No empezar más “sprints” ni introducir nueva infraestructura si no es imprescindible.
