# NexFlow CTA — Auditoría independiente de Sprints 2 y 3

**Fecha:** 31 de octubre de 2026  
**Repositorio:** `saintdeyb321/NexFlow`  
**SHA inspeccionado:** `b8338c598815c658b8dab1f33f057627192fb4fa` (`Sprint 2-3`), posterior a `17ab2cd` (F1).  
**Alcance de evidencia:** lectura del commit y de fuentes en GitHub; informe `docs/arquitectura/CTA_S2_S3_RESULTADOS.md`; **no** acceso al árbol local ni ejecución independiente de .NET, TRX, Groq, Gemini, Redis, PostgreSQL o WhatsApp.

## Dictamen

**GO para avanzar a validación real acotada, no para activar CTA en producción.** El diff está bien focalizado y contiene medidas verificables de seguridad. No se ha acreditado la calidad del entendimiento de proveedores reales ni se ha resuelto el P0 histórico de reserva inesperada. No se recomienda otro sprint grande de refactorización antes de medir esas dos incógnitas.

### Hallazgos confirmados por lectura de código

1. **G0 — descarte acotado:** `ConversationFocusService.Discard.cs` introduce `PendingDiscard` con `Scope` (Active/Suspended/Both), fingerprint de estado y suspendido, TTL de 10 min, identidad de operación, revisión y pregunta vigente; no ejecuta cancelación de un registro empresarial. `AiResponseOrchestrator` integra la consulta de alcance y evita que una respuesta afirmativa huérfana elimine borradores.
2. **G0 — pregunta de ruta rápida:** `ProcessIncomingMessageCommandHandler` ahora pasa `conversation.Id` a `ConversationQuestion.Remember`, conservando el vínculo que F1 valida. La ruta omite la derivación rápida si existe un descarte pendiente.
3. **Sprint 2 — referencias:** `AiInterpreter` admite `ReferenceText` / `ReferenceIndex` con límites y validaciones; `CommerceTurnResolver` coteja referencia/ordinal, borrador actual, pregunta vigente y catálogo autorizado. `OrderDraftMerger` incorpora `Decrease` y rechaza decrementos superiores a la cantidad.
4. **Sprint 3 — orientación:** `SecretaryPresentation.FocusTransition/FocusReminder` da al cliente nombres concretos de las operaciones que conserva y cómo retomarlas. No añade una segunda llamada obligatoria al modelo ni modifica las funciones transaccionales de pedidos/reservas.
5. **Control de rollout:** `AssistantRecovery.EnableContextualFocus` permanece apagado por defecto según código previo y el reporte; el diff no altera los motores transaccionales, migraciones ni infraestructura.

### Evidencia reportada (no reejecutada aquí)

- Gates dirigidos: **48/48**, **216/216** y **196/196**.
- Suite vigente final: **1806/1806**, sin omisiones.
- Build: PASS; `git diff --check`: PASS.
- G8: **130/101/29**, **evidencia histórica reutilizada y verificada**, no suite histórica nuevamente ejecutada sobre S2/S3.
- 12 conversaciones sintéticas del handler; 95 turnos por batería, 44 invocaciones simuladas del intérprete antes/después, dos aclaraciones antes/después. Sin mejora medida de comprensión real, tasa de repreguntas o costo.
- Ningún workflow de GitHub Actions asociado al SHA inspeccionado apareció al consultar sus runs. Verificar antes de promover.

### Riesgos y brechas restantes

- **P0:** la confirmación histórica inesperada de reserva sigue sin traza causal; no se considera resuelta. La referencia conocida del incidente es `b5f1ef63-21b0-454e-bd12-44a9bef14c71`, sin exponer nombres/teléfonos. Investigar con `SourceMessageId`, revisión de pregunta, consentimientos y outbound, siempre lectura y datos anonimizados.
- **Proveedores:** todas las pruebas conversacionales nuevas siguen empleando inyección de semántica/router simulado. No sabemos cómo Groq y Gemini interpretarán expresiones naturales ni si cumplirán el formato JSON en el entorno real.
- **UX demostrada:** en la conversación sintética `catalogo-vacio`, al preguntar «¿Qué productos tienen?» durante una reserva, la respuesta fue «No tengo una opción activa registrada que coincida...» y retomó la fecha. No inventa productos, pero **no explica directamente que no hay productos disponibles en el catálogo**. Señal concreta de respuesta confusa: corregir solo si se reproduce y cabe en el gate dirigido; no abrir un nuevo refactor general.
- **Métricas:** la guía mejoró en texto, pero la batería mantuvo las mismas 44 invocaciones, 262 RPC de commit y 832 lecturas. No se puede afirmar mejora de eficiencia ni naturalidad sin trazas/proveedor real.
- **Compatibilidad de rollback:** no activar CTA mezclando escritores antiguos que no preserven el campo `Focus`; el proyecto ya advierte de pérdida del suspendido al volver a binarios previos.

## Próximo hito: CTA F4, evidencia real y preparación del piloto

1. Confirmar HEAD/árbol local y SHA que realmente se va a probar; no pisar cambios.
2. Verificar la certificación CI del SHA exacto `b8338c5` (si no existe, reportarlo; no confundir resultados locales con CI).
3. Ejecutar una muestra **pequeña, de solo interpretación** con Groq y Gemini configurados en entorno aislado y datos ficticios. Medir JSON válido, intención, operación, referencias, falso consentimiento, costo/tokens si el proveedor lo devuelve y latencia.
4. Reproducir end-to-end **local** con flag CTA habilitado solamente en test host/fixtures, sin enviar pedidos ni reservas al entorno productivo.
5. Documentar el resultado real del P0 mediante una guía de extracción correlacionada; **no** modificar Oracle sin una orden posterior del propietario.
6. Presentar un GO/NO-GO por capacidades: lectura/guía, borradores, pedidos transaccionales y reservas transaccionales. No desplegar ni activar automáticamente.

**Criterio de avance:** evidencia real sobre comprensión + diagnóstico suficiente del riesgo de mensajes/reservas; sin regresiones de consentimiento/idempotencia/CAS. Los problemas detectados deberán corregirse de forma puntual, con reproducción y prueba dirigida.
