# NexFlow CTA — Prompt para Codex: Fase 4, validación real de IA y preparación de piloto DEMO

## CONTEXTO Y OBJETIVO

Trabaja en `C:\Proyectos\NexFlow` sobre el estado real del repositorio. La referencia GitHub publicada tras S2/S3 es `b8338c598815c658b8dab1f33f057627192fb4fa`; si HEAD local o remoto difieren, informa antes de ejecutar cambios, sin hacer reset, checkout destructivo ni sobrescribir staging.

Lee estos documentos:

1. `docs/NEXFLOW_CTA_ARQUITECTURA_OBJETIVO_Y_BRECHAS_2026-10-31.md`.
2. `docs/arquitectura/CTA_F1_DIAGNOSTICO_REAL.md`.
3. `docs/arquitectura/CTA_S2_S3_RESULTADOS.md`.
4. `docs/NEXFLOW_CTA_AUDITORIA_S2_S3_2026-10-31.md` (auditoría independiente, cuando esté disponible).

**Misión:** dejar de medir solamente respuestas de IA inyectadas. Evaluar **Groq/Gemini reales** y la integración conversacional CTA sin escribir datos de producción, determinar brechas concretas y preparar un gate de piloto en el negocio DEMO. No crear otro gran refactor. La funcionalidad F1–F3 debe preservarse.

## GATE 1 — ESTADO Y CERTIFICACIÓN DEL SHA

- Registrar `git rev-parse HEAD`, `git status --short`, `git diff --cached --name-only` y árbol de archivos CTA. Conservar todo sin modificar.
- Corroborar que `AssistantRecovery.EnableContextualFocus` está apagado por defecto en configuración productiva.
- Revisar si existen checks GitHub Actions **del SHA exacto**, no de un SHA anterior. Si no existen, reportarlo y proporcionar los workflows que el propietario debe ejecutar; no afirmar certificación por la suite local.
- Revisar que ningún entorno productivo haya sido activado con CTA por accidente. No conectarse a Oracle ni cambiar servicios.

## GATE 2 — INTERPRETACIÓN REAL, ECONÓMICA Y SIN EFECTOS

Crear o reutilizar una prueba/CLI/harness **solo de interpretación** que invoca la infraestructura actual `IAiRouter`/`AiInterpreter` con credenciales existentes en el entorno local, sin imprimirlas ni persistir mensajes privados. **No usar números telefónicos reales, nombres de pacientes, precios internos, datos de empresa ajenos ni historiales.** Usar ejemplos sintéticos y ámbito de prueba autorizado.

- No inventar credenciales ni agregarlas al repositorio. Si faltan, marcar `BLOCKED_PROVIDER_ENV` y generar instrucciones locales seguras, sin fabricar resultados.
- Presupuesto máximo para la primera muestra: **12 peticiones facturables al proveedor en total**, salvo autorización posterior. Evitar reintentos en cascada, múltiples modelos por turno o repetición de la suite con IA real. Usar el mismo router principal/fallback configurado, registrando el proveedor efectivo cuando sea posible.
- Seleccionar escenarios de máximo valor: «Dije si está disponible el hilo dental» con referencia autorizada; «2 hilos» → «solo uno de ese»; «quita el segundo» con dos líneas; «reduce uno, no lo quites»; «envía solicitud» sin consentimiento; «solo estoy preguntando»; pedido → reserva → pedido; «cancelar» ambiguo; «descarta solo la reserva»; texto con errores; dos órdenes mutables incompatibles; «confirmar» sin revisión vigente.
- En cada escenario, controlar contexto, `ExpectedReply`, referencias, tipo de operación, `Evidence` y precondiciones. Diferenciar **acierto semántico**, JSON inválido, fallo de proveedor, referencia irresoluble y denegación correcta del backend.
- Medir p50/p95 solo con muestras suficientes; con muestra pequeña reportar latencias individuales y promedio sin pretender p95 robusto. Registrar tokens de entrada/salida si el proveedor los proporciona y costo solo si existe tarifa configurada verificable. Sin métricas inventadas.
- La prueba NO debe invocar `ConfirmOperation`, `OrderFlow.Submit`, `ReservationEngine.Create` ni enviar WhatsApp; no hay efectos transaccionales. Una propuesta de la IA jamás sirve como consentimiento.

## GATE 3 — E2E LOCAL CON CTA HABILITADO SOLO EN FIXTURES

- Reutilizar los tests de F1/S2/S3; ejecutar solo filtros dirigidos pertinentes a cualquier fallo real. No repetir a ciegas la suite completa.
- Probar conversación unificada: catálogo → producto con referencia → pedido de dos unidades → cambio de foco a reserva → fecha/hora informativa → regreso al pedido → disminución exacta → revisión → consentimiento explícito independiente. En este gate los motores deben ser mocks/fakes y persistencia de prueba: **cero escrituras en negocio DEMO productivo**.
- Afirmar estado después de cada turno, identidad de pregunta, `ActivationRevision`, ausencia de efectos antes de consentimiento, ausencia de efectos duplicados tras replay y salida segura ante fallo de proveedor.
- Revisar únicamente si se reproduce el texto confuso del escenario `catalogo-vacio`: no debe contestar con opciones no registradas cuando el usuario preguntó por productos. Si amerita fix, implementarlo con prueba dirigida y sin cambios generales.

## GATE 4 — INCIDENTE P0 Y PILOTO

- El incidente de reserva previamente confirmada sigue **NO DETERMINADO**. Referencia del registro a investigar: `b5f1ef63-21b0-454e-bd12-44a9bef14c71`.
- No acceder a Oracle, Firestore, Redis, Evolution ni WhatsApp real desde Codex en este encargo. En su lugar, producir una **guía mínima de consultas de solo lectura, sin PII ni secretos**, para que el propietario pueda recuperar una traza correlacionada (Id, SourceMessageId, timestamp, servicio, revisión/fingerprint, confirmación, outbound), o identificar que faltan datos disponibles.
- Proponer **gates separados** para (a) consultas y guía sin mutaciones, (b) borradores no confirmados, (c) pedidos reales DEMO y (d) reservas reales DEMO. Nunca declarar autorizadas (c) o (d) mientras falten sus controles.
- Preparar un plan reversible de activación aislada del flag CTA, que preserve los escritores compatibles y no dependa de revertir a un binario que perdería snapshots. **Solo preparar, no activar.**

## RESTRICCIONES DURAS

Sin commit, push, deploy, migración, cambios en Docker/Oracle, cambios de configuración de producción ni llamadas de escritura a sistemas externos. No desactivar G8/H06 ni relajar consentimiento, CAS, idempotencia, tenant, módulo/licencia, `UnknownDelivery` o takeover humano. No crear gramáticas por palabra, no frameworks multiagente y no aumentar tokens sin medir. No ocultar errores ni presentar simulaciones como pruebas de modelos reales.

## ENTREGA

Crear `docs/arquitectura/CTA_F4_VALIDACION_REAL_Y_PILOTO.md` con:

- HEAD y árbol realmente inspeccionado.
- Evidencia y resultados de los cuatro gates, marcados PASS/FAIL/BLOCKED/NOT_RUN con razones.
- Tabla de casos de proveedor con respuesta **estructurada saneada**, criterio y validez, sin secretos ni PII.
- Llamadas y tokens reales medidos, si están disponibles, y costo no determinado si no se puede calcular.
- Defectos reproducidos y cambios mínimos, con tests dirigidos, sin forzar corrección especulativa.
- Guía P0 de consultas de lectura para el propietario y plan de activación DEMO por capacidades.
- `git status` final, archivos cambiados y GO/NO-GO específico por capacidad.

Avanza autónomamente dentro de este alcance. Si faltan credenciales para la prueba real, **no simules que pasó**; entrega el harness y su evidencia local sin inventar, más la instrucción exacta que desbloquea la validación.
