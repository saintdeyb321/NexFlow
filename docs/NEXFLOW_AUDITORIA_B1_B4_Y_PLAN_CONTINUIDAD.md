# NexFlow — Auditoría estática B1–B4 y continuación focalizada

Fecha: 2026-10-27. Repo: `saintdeyb321/NexFlow`; base `b581e58854e54d54cc515e0eb0dd620b7dbb89ba`; auditado `d0fd6df9bc835dee8e4cb997f0b55b5f129a6339` (`main`).

## Límite de evidencia

Se revisó código publicado por GitHub y el reporte de Codex. No se ejecutaron pruebas locales, TRX, llamadas a Gemini/Groq ni un webhook/WhatsApp real. GitHub no mostró check-runs para el commit auditado. «481 PASS» es un dato reportado por Codex, no reejecutado en esta revisión.

## Fortalezas verificables en código

- `BookingDraftMerger.cs`: el cambio de sede ya no elimina por defecto servicio y hora. `BookingFlow` revalida servicio y horario antes de emitir el resumen.
- `ConversationQuestion.cs`: referencia de pregunta con identidad de workspace, huella del borrador y expiración de 10 minutos; `CanConfirmReservation` requiere pregunta de confirmación vigente.
- `TurnUnderstandingPipeline.cs` / `ConversationRecoveryService.cs`: una hora consultada puede seleccionarse sin confundirse con consentimiento final; los datos de la consulta siguen en referencia acotada.
- `ConversationFlows.cs`: reconsulta disponibilidad con `GetAvailabilityAtAsync` antes de mostrar resumen y utiliza `CreateReservationAsync` solo tras consentimiento; el motor sigue siendo autoridad del alta.
- `OfferingResponseFormatter.cs`: servicios separados en dos líneas, formato `S/`, y aviso de catálogo general frente a sede.
- `VirtualSecretaryTests.cs`: escenarios de cambios parciales, incompatibilidad, vencimiento/referencia, conflicto, consentimiento, no duplicación y control humano.

## Hallazgos y priorización

### A01 — P1 para aceptación: IA externa no verificada

Tests como T01 y T03 proporcionan interpretaciones de IA preparadas mediante `f.Interpretations.Enqueue(...)`, por lo que verifican integración del flujo con esa salida, no la calidad semántica real de Gemini/Groq. Reproducir con los adaptadores configurados en un entorno aislado, en modo interpretación/lectura, y evaluar evidencia contra mensajes de pacientes variados. Ante una interpretación ambigua no debe alterarse un borrador.

Evidencia: `backend/NexFlow.Tests/Concierge/VirtualSecretaryTests.cs` (T01, T02, T03 y replay).

### A02 — P2: tono de secretaria virtual incompleto

Persisten textos poco naturales en `ConversationFlows.cs`: `LocationOptionsAsync` devuelve `Sedes disponibles:`, `PromptAsync` usa `¿Para qué fecha deseas reservar?`, `AvailabilityOptionsAsync` usa `¿Qué horario prefieres?`; `OfferingService.cs` sigue usando `Servicios reservables disponibles en esta sede:`. No es un fallo transaccional, pero contradice el objetivo del sprint B3. Centralizar mensajes de presentación breves según contexto; evitar prefijos repetidos y conservar preguntas concretas. No permitir al modelo cambiar hechos verificables ni las operaciones.

### A03 — P2: pruebas de UX textual incompletas

Hay aserciones de palabras/precios y estados, pero falta una batería de snapshots/estructuras para primera respuesta, listado de sedes, solicitud de fecha, alternativa de horario, cambio de sede y confirmación en WhatsApp (separadores, longitud, solo una pregunta principal, CTA entre comillas, salvedades por sede). Probar sin acoplar las aserciones a un texto exacto y único, validando estructura y hechos.

### A04 — P2: pedidos en lenguaje natural fuera de la cobertura demostrada B1–B4

El reporte/commit se concentra en reservas. No se demuestra que solicitudes de pedidos sin la palabra «pedido» funcionen, p. ej., `¿Tienen paracetamol? Ponme dos cajas para recoger en Concepción` o `¿Me separas dos?`. Auditar `ICommerceTurnResolver`, confirmación de pedidos, habilitación de módulos, desambiguación, cantidades, stock y consentimiento antes de proponer cambios. Es un objetivo de aceptación aparte, no un bug confirmado en código.

### A05 — P2: probar con canal real sin escrituras

No se verificó el transporte WhatsApp, el formato y la secuencia final que realmente recibe el cliente, ni condiciones de entrega repetida, mensajes consecutivos, humanos/robots o latencia de proveedores. Instrumentar modo de ensayo aislado con salida interceptada y datos sintéticos; no enviar mensajes a números reales ni crear citas productivas.

### A06 — P3: duplicidad de documentación

El plan aparece en `PLAN_BOT_WHATSAPP_SECRETARIA_VIRTUAL_V2.md` (raíz) y `docs/PLAN_BOT_WHATSAPP_SECRETARIA_VIRTUAL_V2.md`. Mantener una fuente canónica (`docs/`) y evitar copias divergentes en próximos cambios.

## Próximo trabajo propuesto: B5 (aceptación conversacional y estilo)

1. Congelar/respaldar el commit B1–B4 y revisar los cambios locales antes de tocar código.
2. Hacer una auditoría focalizada de prompts, rutas de interpretación y pruebas de Gemini/Groq, usando entrada sintética, sin efectos comerciales.
3. Corregir únicamente causas reproducidas de semántica/continuidad; añadir casos de órdenes y consultas sin palabras clave si los fallos son reales.
4. Extraer/copiar los mensajes principales en una capa de presentación conversacional (sin sobreescribir hechos de dominio): salutación no repetida, sedes, listado servicios, consulta de fecha/hora, resumen y edición parcial.
5. Validar restricciones WhatsApp: párrafos cortos, precios separados, comillas en ejemplos sugeridos, sin columnas de espacios, sin duplicar preguntas ni listar mañanas al consultar noche.
6. Realizar regresión focalizada de confirmación, CAS, idempotencia, edición de sede y handoff; conservar el resultado previo si falla la IA.
7. Entregar tabla breve: hallazgo, modificación, caso PASS/FAIL, no verificado. No desplegar ni escribir en DB real.

## Criterios de aceptación mínima

- «En la de conce» selecciona Concepción con alias documentado o pide una aclaración real, no repregunta sin motivo.
- «¿Qué servicios tiene?» contesta, distingue catálogo general y servicios por sede, y no cambia el borrador.
- «Cambia únicamente la sede a San Jerónimo» conserva servicio, paciente y fecha; revalida oferta y horario, sin confirmar automáticamente.
- «¿Y a las 7 pm?» consulta; «Sí» selecciona la hora en la referencia vigente; un segundo consentimiento explícito confirma, sin duplicar.
- «Elimina la base de datos» no acciona administración ni modifica la reserva.
- «¿Me separas dos cajas de X?» no exige la palabra «pedido» si el módulo de órdenes está habilitado; no persiste sin consentimiento.
- Toda afirmación de precio, disponibilidad o reserva procede del backend y se revalida antes de escribir.
- Si la IA no interpreta confiablemente, aclara una vez o deriva a humano; no inventa entidades.

## Archivos de referencia

- `backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/Flows/ConversationFlows.cs`
- `backend/NexFlow.Application/Features/Automation/Dialogue/Policy/BookingDraftMerger.cs`
- `backend/NexFlow.Application/Features/Automation/Dialogue/Understanding/TurnUnderstandingPipeline.cs`
- `backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/ConversationRecoveryService.cs`
- `backend/NexFlow.Application/Features/Automation/Dialogue/Composition/ResponseComposer.cs`
- `backend/NexFlow.Application/Features/Automation/Dialogue/Composition/OfferingResponseFormatter.cs`
- `backend/NexFlow.Application/Features/Business/Offerings/OfferingService.cs`
- `backend/NexFlow.Tests/Concierge/VirtualSecretaryTests.cs`
