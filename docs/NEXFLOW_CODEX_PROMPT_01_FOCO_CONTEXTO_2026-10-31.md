# PROMPT 01 PARA CODEX — NexFlow CTA: foco conversacional + continuidad segura

> **Pegar este prompt en Codex desde la raíz del proyecto.** Es la primera orden de ejecución de la nueva estrategia CTA. No pide implementar toda la arquitectura.

---

Eres el agente de ingeniería que trabaja sobre **NexFlow** (.NET backend, WhatsApp/Evolution, estado Redis/Firestore, PostgreSQL y motores de pedidos/reservas). Debes evolucionar el sistema hacia el **Contextual Transactional Agent (CTA)**, **reutilizando los componentes actuales**.

## 0. Documentos obligatorios y estado de partida

Primero lee íntegramente:

1. `docs/NEXFLOW_CTA_ARQUITECTURA_OBJETIVO_Y_BRECHAS_2026-10-31.md` (el usuario pondrá el archivo descargado en `docs/`).
2. Si está disponible, `docs/NEXFLOW_PLAN_MEJORA_CONVERSACIONAL_WHATSAPP_2026-10-30.md` y sus pruebas/artefactos relacionados, como contexto histórico, **no como verdad del HEAD actual**.
3. Si está disponible, la fuente `# NexFlow AI — Visión, arquitectura.txt`, como visión de producto, **no como una instrucción para crear servicios nuevos**.

Último `main` observado durante la elaboración de este encargo:

`1ac887186d49ade0d6bcde6d117dfc9487557420` (*Correcion BOT pedidos*).

**No asumas que ese sigue siendo HEAD local o remoto.** Verifícalo; si cambió, trabaja sobre el árbol que encuentres y documenta diferencias. **No hagas `git reset`, `git clean`, checkout destructivo, ni sobrescribas staging/cambios locales.** Si existen cambios ajenos a esta tarea, consérvalos.

La API que se había desplegado en Oracle era `aca4d739...`; Codex **no debe tocar Oracle, Docker remoto ni WhatsApp real** en esta fase.

## 1. Objetivo de negocio: por qué estamos cambiando de enfoque

NexFlow debe comprender expresiones naturales, conservar contexto y ofrecer próximos pasos útiles. La demostración de Axion Labs es una referencia de **experiencia de usuario**, no de arquitectura conocida. No copiar su stack ni asumir que su bot es transaccionalmente seguro.

No quiero resolver esto con un prompt gigante ni con más `if`, regex o listas de frases para cada error. Quiero un **agente conversacional transaccional híbrido**:

- IA interpreta lenguaje, referencias y posibles acciones, con evidencias tipadas.
- Contexto guarda focos, preguntas, referencias y borradores autorizados.
- Resolutor determinista decide qué puede consultarse, modificarse, pausarse, retomarse o confirmarse.
- Los motores de dominio siguen siendo los únicos que ejecutan reservas/pedidos.
- `ResponsePlan` entrega hechos verificados, opciones reales y una pregunta útil.
- El costo se mide por conversación completada, no solo por tokens evitados.

**Esta primera tarea solo cubre la primera brecha funcional reproducible: el bloqueo al cambiar entre pedido y reserva.** No implementes aún un intérprete genérico multiagente ni dos commits en un mismo turno.

## 2. Conducta que queremos lograr en la primera vertical

Escenario de Sonrisa Andina Dental DEMO (datos sintéticos; adaptar a fixtures reales):

```text
Cliente: Quiero hacer un pedido.
Bot: Pregunta por productos y cantidades, con un ejemplo útil.
Cliente: 2 hilos dentales.
Bot: Prepara un borrador verificado. No registra el pedido.
Cliente: También quiero hacer una reserva.
Bot: Conserva el pedido en estado suspendido o propone pausarlo
     con una elección inequívoca, sin obligar a descartarlo.
Cliente: En Concepción, limpieza dental.
Bot: Inicia/continúa la preparación de reserva, sin confirmar.
Cliente: Volvamos al pedido.
Bot: Recupera exactamente los 2 hilos dentales y orienta el siguiente paso.
Cliente: Envía la solicitud.
Bot: Muestra una revisión NUEVA y solicita consentimiento ligado a esa revisión.
Cliente: De acuerdo.
Bot: No ejecuta automáticamente por el mero acuse.
Cliente: Confirmar.
Bot: A lo sumo un pedido PendingReview, SOLO si el contrato vigente de
     consentimiento está satisfecho. No crea ninguna reserva.
```

**La meta no es forzar esos textos literales:** importa el estado, la causa de cada acción y los efectos reales.

### Política mínima de la primera versión

- Una conversación mantiene **un foco** y, inicialmente, **como máximo un borrador suspendido**.
- Si no hay modo seguro de mantener pedido y reserva sin cruzar campos globales, pide una decisión explícita y suspende antes de cambiar el foco; **jamás borres el borrador antiguo sin consentimiento para descartarlo**.
- No crees dos borradores del mismo tipo ni dos escrituras de negocio por turno.
- No permite reusar confirmaciones/revisiones de un borrador suspendido cuando cambia de foco; revalidación obligatoria.
- Si las transiciones serían inseguras con el contexto existente, **no implementes un bypass**: documenta NO-GO de F1 con prueba roja y alternativa mínima.

## 3. Fase A — reconocimiento focalizado (no auditoría general)

1. Ejecuta `git status --short`, `git rev-parse HEAD` y revisa diferencias locales sin modificar nada.
2. Lee solo las rutas relevantes primero:
   - `backend/NexFlow.Application/Abstractions/Cache/IConversationCache.cs`
   - `backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/ContextRecoveryService.cs`
   - `backend/NexFlow.Infrastructure/Persistence/Firestore/FirestoreConversationStateRepository.cs`
   - `backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/AiResponseOrchestrator.cs`
   - `backend/NexFlow.Application/Features/Automation/Dialogue/Understanding/TurnUnderstandingPipeline.cs`
   - `backend/NexFlow.Application/Features/Automation/Dialogue/Contracts/TurnUnderstanding.cs`
   - `backend/NexFlow.Application/Features/AI/Interpretation/AiInterpreter.cs`
   - `backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/Flows/OrderFlow.cs`
   - BookingFlow en `ConversationFlows.cs`
   - `ConversationRecoveryService.cs`, `ResponsePlan.cs` y pruebas de concierge relacionadas.
3. **Demuestra** el bloqueo de cambio de operación. En el SHA observado, `AiResponseOrchestrator` tiene una rama que devuelve «Para iniciar otra operación, primero completa la actual o escribe cancelar...» cuando ORDER y RESERVATION se cruzan. Confirma si sigue siendo la ruta efectiva.
4. Identifica los campos **compartidos peligrosos**: `CurrentGoal`, `CurrentStep`, `CurrentQuestion`, `LastQuestion`, `SelectedLocationId`, `StateVersion`, `PendingRecoveryAction`, `OrderDraftItems`, slots de reserva, referencias, resumen y consentimiento. Traza quién los lee/escribe/limpia.
5. Identifica el formato y el mecanismo real del `contextJson` y CAS; prueba el comportamiento al deserializar contextos legacy sin campos nuevos.
6. Comprueba que los 28 tests del parche anterior son fixtures con interpretación controlada y no equivalen a Gemini/Groq/WhatsApp real.
7. Revisa el riesgo P0 de respuesta de reserva inesperada **solo para no debilitar garantías**. No bloquees la investigación de esta nueva brecha con una auditoría general ni afirmes una causa sin traza.
8. Construye una **reproducción multi-turno fallida** con los servicios actuales y estado persistido/recuperado, no solo un test de formateo. Registra el estado inicial/final y el punto exacto de rechazo.

**Artefacto de Fase A:** `docs/arquitectura/CTA_F1_DIAGNOSTICO_REAL.md` con máximo foco: rutas, evidencia, riesgo de compatibilidad, propuesta mínima y línea base. No redactes 20 documentos ni una auditoría integral innecesaria.

## 4. Fase B — implementar UNA vertical segura, condicionada por Fase A

**Continúa a implementación solo si Fase A demuestra una ruta de compatibilidad clara.** Si no, detente y entrega pruebas rojas más un plan de adaptación precisa; no improvises.

### Diseño esperado, sin imponer clases nuevas

- Separar conceptualmente **foco de conversación** de **borrador operativo**.
- Reutilizar el `ConversationContextDto`, la persistencia durable y `StateVersion` con CAS; si se requiere un campo nuevo, aditivo/opcional/versionado dentro del mismo `contextJson`.
- Guardar solo el estado mínimo específico del borrador suspendido; no hacer copia ciega de todo el contexto (especialmente `StateVersion`, datos de otra operación, consentimiento y preguntas de un turno antiguo).
- Definir una política tipada para `Pause` / `Resume` / `SwitchFocus` o equivalente **sin una enorme colección de heurísticas léxicas**. El lenguaje natural no inequívoco pasa por la interpretación actual; botones/opciones exactos pueden resolverse localmente.
- Crear o reutilizar **identidad y versión de operación**. Preguntas pendientes y decisiones deben estar asociadas a la operación correcta; un «sí» fuera de contexto no puede confirmar el pedido ni la reserva.
- Solo rutas autorizadas en el workspace; no permitir que el modelo invente IDs de catálogos, precios o acciones directas.
- Revalidar módulos/licencias, catálogo/precios y disponibilidad al retomar. Una revisión vieja de pedido es inválida para autorizar su envío.
- Preservar preguntas informativas ya soportadas mientras otro borrador está activo.
- Adaptar el texto final mediante `ResponsePlan`/`SecretaryPresentation` existentes. No crear un segundo compositor, un modelo por industria o un framework de agentes.
- Si necesitas un feature flag, comprobar primero si ya hay un mecanismo adecuado. **Desactivado por defecto** hasta las pruebas de compatibilidad; no activar en Oracle.

### Cambios que NO debes hacer en este sprint

- No implementar multi-intent general de operaciones compuestas ni transacciones atómicas entre Firestore y PostgreSQL.
- No reescribir `ReservationEngine`, `OrderFlow` o la lógica de consentimiento para acomodar una nueva interpretación.
- No dar al LLM `ConfirmOperation` o herramientas de escritura directa.
- No migrar PostgreSQL, reestructurar colecciones Firestore ni añadir Redis keys sin evidencias y aprobación.
- No reentrenar modelos ni instalar LangChain/AutoGen/CrewAI, vector DB, RAG, embeddings o nuevos proveedores.
- No refactorizar módulos enteros por estética ni crear docenas de nuevas abstracciones.

## 5. Pruebas obligatorias de esta vertical

Crea primero tests de comportamiento que FALLEN con el código previo y PASEN con la modificación. Al menos:

1. `order → reservation`: estado de pedido persistido/suspendido, con cantidades y producto intactos; cero `CreateOrder` y cero reserva confirmada.
2. `reservation → order`: recuperación exacta del pedido sin contaminar campos de reserva; pregunta siguiente correcta.
3. `switch → process restart → resume`: el borrador recupera desde persistencia real/fixture equivalente, no solo variable en memoria.
4. `switch → TTL expirado`: no reanudar estado caducado ni reutilizar opciones viejas.
5. `confirmación antigua de pedido tras cambio de foco`: cero writes. Solicitar revisión nueva.
6. `confirmación antigua de reserva tras cambio de foco`: cero writes. Respetar H06.
7. `cambio de catálogo/precio/licencia`: no consumir el resumen viejo; revalidar según el motor actual.
8. `dos mensajes concurrentes / CAS conflict`: no pisar ni perder borradores.
9. `replay del mismo MessageId`: cero efectos duplicados, salida vinculada al mensaje correcto.
10. `workspace B` intenta referencia de `workspace A`: denegar, sin fuga.
11. `human handoff`: ninguna transición de bot tras takeover.
12. `UnknownDelivery`: no reenviar automáticamente ni volver a ejecutar una transacción.
13. `legacy contextJson`: leer y continuar sin cambiar su semántica de estado por defecto.
14. `IA cae`: conservación de borradores y ruta de recuperación útil sin loops.
15. `suspender cuando hay un pending recovery/confirm`: pregunta correcta; no se heredan permisos de confirmación de otro foco.
16. `«Quiero reservar»` con pedido abierto pero intención semántica insuficiente: aclarar sin descartarlo ni inventar operación.

Si la política final decide requerir confirmación para *pausar* el borrador, prueba esa decisión por separado de las autorizaciones de negocio.

**Prueba de regresión:** las pruebas recientes de pedidos/CTA, H06/G8, reservas, licencias, Outbox y Redis relevantes. Nunca cambiar las expectativas históricas de seguridad para hacer pasar un test.

## 6. Métricas: sin introducir un nuevo sistema de observabilidad

Aprovecha `AiTelemetry`/`ConversationDiagnostics` para reportar, en fixtures medibles:

- Interpretaciones IA por turno y por conversación (incluido fallback).
- Tamaño del contexto enviado a IA; no guardar payloads sensibles.
- Latencia por etapa donde esté disponible.
- Número de aclaraciones/repeticiones para completar la vertical.
- Escrituras de negocio y operaciones indebidas: **0** salvo el único consentimiento válido del caso positivo.

No inventes costes. Si no tienes uso/coste del proveedor, informa «no medido» y deja mecanismo de medición sin bloqueos.

## 7. Validación y eficiencia de ejecución

1. Ejecuta **primero pruebas dirigidas** de la vertical y seguridad. Conserva pruebas rojas iniciales para demostrar la reparación.
2. Compila backend y ejecuta las regresiones funcionales pertinentes.
3. Ejecuta **la suite completa una sola vez al cerrar el cambio**, si el entorno lo permite. Si existía un fallo previo, distingue fallo de baseline, regresión y fallo independiente; no declares suite verde mientras exista FAIL.
4. `git diff --check`; revisa cambios finales, `git status`, staging y ausencia de credenciales/datos de WhatsApp.
5. No gastes tiempo en un segundo ciclo de auditoría amplia ni en reescribir tests históricos.
6. Si alguna validación es imposible (p. ej., proveedor real, emulador o CI), dilo con precisión.

## 8. Restricciones críticas

**Sin commit, push, despliegue, migraciones, Docker, acciones en Oracle, cambios de Evolution o mensajes reales a WhatsApp.** Todo local hasta autorización posterior del usuario.

Preservar en todas las transiciones:

- Consentimiento ligado a revisión/fingerprint, operación, conversación, estado y mensaje fuente.
- Idempotencia y recibos; ningún doble pedido ni doble reserva.
- Estado `UnknownDelivery` sin reenvío automático.
- CAS y control de concurrencia, recuperación persistente, TTL de referencias.
- Licencias y módulos por workspace, aislamiento multiempresa.
- Takeover humano prioritario.
- Integridad de respuestas: hechos empresariales solo desde fuentes verificadas.
- H06 y G8 histórico 130/101/29, sin degradar el gate.

No usar información real de clientes ni copiar secretos de `.env`/appsettings a reportes. Datos sintéticos en pruebas.

## 9. Formato final de entrega que debes devolver

Responde con estas secciones, sin relleno:

**A. Árbol y versión:** SHA, cambios/staging previos conservados.

**B. Brecha demostrada:** ruta concreta, por qué bloqueaba la experiencia, prueba roja previa y resultado posterior.

**C. Decisión de arquitectura:** cómo separaste o propusiste separar foco/operación, dónde persiste, cómo funciona legacy/CAS/TTL, por qué no necesitó otro framework.

**D. Cambios exactos:** archivos editados/creados, razones y alcance; indicar si F1 quedó implementada o es NO-GO razonado.

**E. Pruebas reales:** cantidad y comandos dirigidos, resultados de regresión, build, suite completa, fallos pendientes, resultados sin simular aprobación.

**F. Seguridad:** evidencia de cero efectos en cambios de foco, confirmaciones cruzadas, duplicados, fuga de tenants y `UnknownDelivery`.

**G. Eficiencia:** llamadas de IA por vertical, coste si medido, latencia si medida, repreguntas.

**H. Próximo sprint recomendado:** solo la dependencia siguiente demostrada: F2 operaciones independientes o corrección que haya quedado bloqueando F1.

**I. Git:** `git status --short`, confirmación de **sin commit, sin push, sin despliegue**.

### Regla de decisión final

**No quiero que Codex «implemente CTA entero» en una sola respuesta.** Quiero una primera vertical pequeña, segura, basada en evidencia, con experiencia conversacional mejorada y compatibilidad con el backend existente. Si F1 no puede hacerse sin perder garantías, una reproducción roja y un diseño preciso con NO-GO es un resultado correcto; un parche inseguro no lo es.
