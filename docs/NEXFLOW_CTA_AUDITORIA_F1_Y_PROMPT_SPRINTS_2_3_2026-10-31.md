# NexFlow CTA — Auditoría focalizada F1 y encargo combinado de Sprint 2 + Sprint 3

**Fecha:** 31 de octubre de 2026  
**Repositorio:** `saintdeyb321/NexFlow`  
**Base F1 inspeccionada en GitHub:** `17ab2cd0211fbbfd420bbabce477acdc0ac6a6b9` («Sprint 1»), descendiente directo de `1ac887186d49ade0d6bcde6d117dfc9487557420`.

## Resultado de auditoría externa limitada

**GO condicionado para continuar el desarrollo local de Sprint 2 y Sprint 3; NO-GO para activar CTA o desplegar.**

Se revisó el diff y el código publicado de F1: `ConversationFocusService`, `ConversationFocusState`, `ConversationQuestion`, `AiResponseOrchestrator`, `TurnUnderstandingPipeline`, `AiInterpreter`, contexto y pruebas. **No** se ejecutaron localmente los TRX ni los 1764 tests: esos resultados son evidencia reportada por Codex, no una ejecución independiente. No existe en esta auditoría una validación real de proveedores ni de WhatsApp.

### Hallazgos verificables y condiciones de entrada

- **Aprobado conceptualmente:** almacenamiento de un solo borrador suspendido en `contextJson` con CAS; `FocusProposal` backend-only; cambio de foco sin efectos de negocio; revisión nueva por identidad de operación y revisión de activación; límites de snapshot y flag apagado por defecto. Las 25 pruebas nuevas cubren los escenarios centrales.
- **H1 — ciclo de vida incompleto:** `ConversationFocusService.ApplyAsync` impide restaurar un suspendido vencido, pero no define vía segura para descartarlo y continuar; en `AiResponseOrchestrator`, cuando `CurrentGoal == null` y queda `Focus.Suspended != null`, un nuevo inicio se bloquea. Un suspendido vencido podría dejar al cliente atascado. Reproducción y reparación obligatorias en el primer gate.
- **H2 — referencia de pregunta en ruta rápida:** `ProcessIncomingMessageCommandHandler` llama `ConversationQuestion.Remember(...)` para consultas directas de sedes/horarios **sin `conversationId`**. En F1 `ConversationQuestion.Current()` exige `question.ConversationId == state.Focus.ConversationId` cuando existe Focus. Si la consulta rápida se usa tras finalizar el foco activo pero con un suspendido vigente, la pregunta guardada queda inválida. Comprobar ruta y reparar sin debilitar el vínculo de consentimiento.
- **H3 — limpieza de campos operativos:** `ConversationFocusService.ClearProjection` limpia preguntas, referencias y slots, pero no `OrderLastSourceMessageId` ni `OrderLastResponse`. Verificar si estos campos interfieren entre focos/replays, y corregir solo si hay reproducción o prueba justificativa; nunca borrar recibos durables.
- **Compatibilidad operacional:** escritores antiguos deserializan y reescriben `contextJson` sin conocer `Focus`: podrían perder el suspendido. Mantener CTA apagado en despliegues no coordinados; no activar ni revertir binarios con contextos F1 sin procedimiento explícito.
- **P0 histórico:** reserva sospechosa del DEMO sin traza correlacionada. **No resuelto.** No usar la nueva arquitectura como prueba de que P0 desapareció.

## PROMPT ÚNICO PARA CODEX — Sprint 2 + Sprint 3, con gate previo obligatorio

Eres el ingeniero responsable de evolucionar NexFlow CTA. Trabaja en `C:\Proyectos\NexFlow` y usa el código local como autoridad. No presupongas que el árbol local esté limpio ni coincida con `origin/main`.

### 0. Contrato y contexto

Lee los archivos existentes:

1. `docs/NEXFLOW_CTA_ARQUITECTURA_OBJETIVO_Y_BRECHAS_2026-10-31.md`
2. `docs/NEXFLOW_CODEX_PROMPT_01_FOCO_CONTEXTO_2026-10-31.md`
3. `docs/arquitectura/CTA_F1_DIAGNOSTICO_REAL.md`
4. Este documento, `docs/NEXFLOW_CTA_AUDITORIA_F1_Y_PROMPT_SPRINTS_2_3_2026-10-31.md`, si está presente en el repositorio; si no, usa el prompt pegado por el usuario.

**Inspección ya realizada:** F1 está publicada en GitHub como `17ab2cd0211fbbfd420bbabce477acdc0ac6a6b9`, pero los resultados de test se reportaron localmente. Comprueba `git status`, HEAD y staging. No hagas pull, reset, checkout destructivo ni sobrescribas archivos pendientes. No repitas una auditoría general del repositorio: revisa únicamente los riesgos y contratos de este encargo.

### GATE 0 — Auditoría y cierre de bloqueadores de F1 (OBLIGATORIO)

Antes de Sprint 2/Sprint 3:

**G0.1 Suspendido vencido y operación activa finalizada.** Construye un caso reproducible:
- pedido A suspendido, reserva B en foco;
- B termina o se descarta;
- A vence por TTL;
- cliente desea iniciar una nueva operación o descartar A.

No puede quedar bloqueado indefinidamente por un suspendido irrecuperable. Diseña una salida acotada y explícita, con la semántica correcta para *descartar suspendido*, *descartar activo* y *descartar ambos*; ninguna intención ambigua debe eliminar datos. Probar continuación segura tras el descarte, sin reusar preguntas ni consentimientos.

**G0.2 Preguntas en la ruta directa.** Comprueba el caso `CurrentGoal == null` con `Focus.Suspended` vigente y consulta directa de horario/sede. `ProcessIncomingMessageCommandHandler` genera una `ConversationQuestion` sin conversationId, pero `Current()` lo exige en modo CTA. Añade test multi-turno realista y corrige usando identidad y conversación verificadas. No relajes `ConversationQuestion.Current()` para permitir consentimientos de otra operación. Verifica que el comportamiento legacy no cambie con el flag apagado.

**G0.3 Proyección de marcadores de pedido.** Comprueba `OrderLastSourceMessageId` y `OrderLastResponse` en cambios de foco, replay y reanudación: no deben provocar respuestas viejas ni mutaciones indebidas. Si no hay incidencia reproducible, documenta su inocuidad y no cambies código sin razón.

**G0.4 Integridad F1:** preguntas ligadas a `OperationId` y `ActivationRevision`; TTL no renovado por otro foco; CAS de un solo ganador; mensajes duplicados; distinto workspace/conversación; toma humana; licencia revocada; pedidos/reservas sin efectos al cambiar. Mantén `AssistantRecovery.EnableContextualFocus=false` por defecto. El P0 histórico sigue pendiente.

**Decisión GATE 0:** si hay fallo de seguridad, pérdida de borrador, consentimiento reutilizable, duplicado, incompatibilidad de escrituras o bloqueo no resuelto, **NO empieces Sprint 2 ni 3**. Corrige los bloqueadores estrictamente dentro de este gate, ejecuta pruebas dirigidas y vuelve a evaluarlo. Si no puedes corregirlo de forma pequeña y compatible, detente y entrega informe `NO-GO` concreto.

### SPRINT 2 — Comprensión contextual y continuidad (solo si GATE 0 = PASS)

Objetivo: dar al intérprete y al resolutor contexto suficiente para comprender referencias, cambios y continuaciones, **sin** convertir las propuestas semánticas en autoridad transaccional.

Alcance:

- Mejorar referencias al producto, servicio, sede, opción o borrador vigente después de consultar, pausar y retomar; utilizar únicamente identificadores del workspace comprobados, pregunta vigente y foco correcto. Si «ese», «el primero», «lo de antes» o «deja uno» no tienen referente inequívoco, preguntar de forma específica.
- Comprender correcciones absolutas frente a incrementales: «quiero solo dos», «quita uno», «mejor tres», «eso es todo»; no duplicar cantidades ni convertir preguntas en ediciones.
- Diferenciar consulta informativa, petición para revisar, cambio de foco, descarte, continuación y confirmación. Las interrupciones informativas no deben modificar borradores.
- Resolver expresiones con errores de escritura y variación semántica **sin** añadir listas masivas de frases, sinónimos o regex como sustituto de comprensión.
- Usar la proyección mínima de `DialogueTurnContext` y el `Focus` existente. Evitar historiales y catálogos completos en prompts; respetar módulo/licencia, TTL, identidad, límites y CAS.
- Si llega un turno con dos intenciones, discriminar lectura + preparación o dos cambios mutables. No implementar ejecución múltiple insegura ni ampliar contratos de forma masiva: identificar y resolver lo que sea inequívoco y pedir aclaración sobre el resto; no provocar efectos parciales inesperados.
- Mantener los motores actuales. No añadir framework de agentes, almacenamiento alternativo ni segunda llamada obligatoria de IA.

**Pruebas de Sprint 2:** secuencias multi-turno con pedidos/reservas alternados; referencias inequívocas y ambiguas; ediciones de cantidades; consulta cruzada; reintentos; cambio de precio, licencia o catálogo; timeout/JSON inválido IA; takeover; aislamiento de workspace; confirmación vieja; texto con errores y frases inéditas. Asegurar que `Confirmed` no venga del modelo.

### SPRINT 3 — Orientación y respuestas naturales (solo si Sprint 2 = PASS)

Objetivo: que NexFlow **guíe al usuario** como secretaria comercial competente sin sonar a formulario ni gastar tokens en cada texto.

- Aprovechar `ResponsePlan`, `SecretaryPresentation`, `ResponseComposer`, `ConversationRecoveryService` y las opciones validadas: evitar un compositor paralelo.
- Responder primero la pregunta real y después dar **un siguiente paso útil**, solo si está permitido y respaldado por capacidades/datos reales.
- Para pedidos: explicar elección de productos y cantidades, revisión de subtotal, edición, envío para aprobación del negocio y confirmación separada; catálogo no significa stock físico ni pedido aceptado.
- Para reservas: guiar por sede, servicio, fecha y horario verificado. Ejemplos como `«03/11»`, `«3 de noviembre»`, `«mañana por la tarde»` sin asumir hora disponible. Pedir solo el dato faltante.
- Para cambios de foco: «Tu pedido sigue guardado; continuamos con la reserva» y al volver presentar el estado verificado. Si dos borradores están abiertos, indicar cuál está activo y cómo retomar el otro, sin volcar datos privados.
- Respuestas cortas, útiles, WhatsApp-friendly; no plantillas fijas que ignoren el estado; comandos opcionales con `«...»`; no prometer stock, horarios, precios, pagos o reservas no verificados.
- Fallos de IA, ambigüedad, limitación de tokens y catálogo vacío: ofrecer pasos concretos basados en el estado, no bucles de «no pude interpretar».
- Mantener composición determinista por defecto; una redacción IA opcional solo si el contrato permite comprobar hechos exactos, presupuesto, tiempo, no consentimiento y ausencia de filtraciones. No habilitar generación de texto libre para confirmar operaciones.
- No prometer soporte de funcionalidades que los módulos licenciados no permiten; funcionará en clínica dental, tienda, salón o restaurante con los mismos contratos base.

**Pruebas de Sprint 3:** verificar mensajes completos, decisión y siguiente paso; no únicamente `Contains` o número de palabras. Comparar al menos 12 conversaciones sintéticas antes/después que incluyan el transcript dental DEMO (hilo dental, pedido de 2 unidades, envío, corrección, descarte, sede Concepción, limpieza dental, fecha y hora), sin datos inventados. Evaluar claridad y número de repreguntas; no confundir pruebas con proveedor falso y una medición real de Gemini/Groq.

### Reglas de ejecución y eficiencia

- **Orden obligatorio:** GATE 0 → Sprint 2 → gate dirigido → Sprint 3 → gate dirigido → build → **una suite completa final**. No ejecutar la suite completa después de cada ajuste.
- Si no pasa el gate de Sprint 2, no iniciar Sprint 3. Si no pasa Sprint 3, reportar bloqueador sin ocultarlo.
- Mantener H06, G8 histórico `130/101/29` aceptado, pruebas actuales, idempotencia, consentimientos por operación/fingerprint, CAS, permisos, licencias, transferencia humana, outbox, `UnknownDelivery` y fuentes verificadas. **No convertir las 29 incompatibilidades históricas en una licencia para aceptar nuevos fallos.**
- Si el resultado local antes de tocar código era `1764/1764` y agregas pruebas, el total cambiará; reporta ejecutadas, aprobadas, fallidas y omitidas, sin cifras inventadas. Preserva archivos existentes de pruebas.
- Registrar en pruebas/diagnóstico las llamadas IA, tamaño de contexto, CAS añadidos, latencias sintéticas, tasa de repreguntas y composición; no atribuirles costo real ni latencia de proveedor si no se midieron.
- Control de alcance: backend y pruebas principalmente; no migraciones, frontend, Docker, cambios de infraestructura, schema paralelo ni dependencias grandes sin permiso. No borrar archivos ni modificar staging ajeno.
- **Sin commit, push, migraciones, deploy, toggle en producción, reinicios, cambios Oracle ni pruebas de transacciones reales.** F1 es opt-in; no activar CTA mientras existan escritores antiguos que desconozcan `Focus`.
- **P0 histórico** de confirmación sospechosa sigue sin causa real; sin traza correlacionada, no declararlo resuelto. El GO de este encargo es solo para desarrollo local y revisión.

### Entrega final obligatoria

1. `Gate 0`: PASS / NO-GO con causas y casos reproducibles de H1, H2, H3.
2. `Sprint 2`: PASS / NO-GO y comportamiento conversacional demostrado.
3. `Sprint 3`: PASS / NO-GO y 12 conversaciones antes/después.
4. Arquitectura/archivos concretos modificados y motivo; `git status`, HEAD y staging.
5. Pruebas dirigidas, build, suite completa final y G8 (reutilizar la verificación histórica cuando no haya cambios en corpus/contrato; no repetir trabajo sin motivo).
6. Métricas medidas vs no medidas; costo de IA no inferido.
7. Límites, riesgos y plan de activación futura sin ejecutar ninguna publicación.

**Empieza por GATE 0. Si está en verde, avanza automáticamente a Sprint 2 y Sprint 3 en esta misma sesión. No pidas aprobación intermedia salvo que encuentres una decisión destructiva o fuera de alcance.**
