# Prompt para Codex / IA con acceso al repositorio del bot

Quiero corregir el bot de **reservas odontológicas por WhatsApp**. Te adjunto `PLAN_BOT_WHATSAPP_SECRETARIA_VIRTUAL_V2.md` y el transcript real del 27/10/2026. **No confundas este bot con MediPOS**: inspecciona primero el repositorio o ZIP que te proporcione y determina su arquitectura real.

## Objetivo

Que funcione como secretaria virtual: natural, cordial, concisa, contextual, capaz de responder preguntas laterales, entender lenguaje cotidiano y **editar solo el dato que el paciente solicita** sin reiniciar la reserva. El backend sigue siendo la autoridad sobre sedes, servicios, horarios, precios y citas. No basta con reescribir mensajes: corrige flujo y estado.

## Orden de implementación — avanzar sin pausas innecesarias

**B1 — Contexto e intenciones.** Traza el flujo real del transcript. Identifica por qué «en la de conce» no se registra, por qué preguntar servicios reinicia sede, por qué «la 1» pierde referencia y por qué «editar la sede» reinicia servicio/fecha/hora/nombre. Corrige router, memoria y edición diferencial en la arquitectura existente. Acepta variaciones ortográficas, frases naturales, interrupciones y consultas informativas sin perder el borrador.

**B2 — Verdad de negocio y transacciones.** Distingue catálogo general (5) de servicios por sede (Concepción 4 en el ejemplo), y explícalo claramente. La oferta y el precio vienen de APIs reales. Buscar «hoy 8pm» significa comprobar 20:00; si falla, ofrecer alternativas cercanas verificadas, no primeras horas de la mañana. Cambiar sede revalida **solo** servicio, precio y horario dependientes; conserva todo lo demás mientras siga válido. Confirma únicamente después de consentimiento inequívoco, validación final y escritura idempotente. Nunca ejecutar borrado de DB por WhatsApp.

**B3 — Redacción WhatsApp profesional.** Elimina salidas crudas de JSON/tools, «Mostrando 1–5 de 5» sin valor para el cliente y preguntas repetitivas. Usa estilo cálido, párrafos breves, títulos destacados, un servicio por bloque, precios `S/` y mensajes escaneables. Diferencia disponibilidad por sede. Ejemplifica respuestas con «Sí», «No» o «Confirmar», **sin exigir coincidencia literal**. Muestra PDF solo si existe enlace real. Conserva claridad al confirmar o rechazar reservas.

**B4 — Cierre dirigido.** Reproduce el transcript y las pruebas T01–T16 del plan con mocks/staging, sin enviar mensajes reales ni crear citas de pacientes. Corrige fallos detectados, suprime código redundante y ramas rígidas solo cuando no sean controles de negocio. No hagas auditorías genéricas ni suites masivas.

## Reglas

- **No reemplazar IA por un bosque de palabras clave**; usar comprensión contextual + estados estructurados mínimos + reglas deterministas solo para acciones críticas.
- No alucinar sedes, horarios, precios, duración, enlaces, estados ni reservas.
- No eliminar autorización, privacidad, trazabilidad, concurrencia, idempotencia ni confirmación explícita.
- No destruir datos, migrar ni desplegar sin aprobación.
- Conservar cambios existentes; no reset, no push, no despliegue.
- Ante ambigüedad real, pedir una sola aclaración concreta; si el bot falló, reconocerlo brevemente y continuar.
- Si una API no ofrece la funcionalidad necesaria, especificar exactamente el contrato mínimo requerido antes de implementarlo.

## Entregables concisos

1. **Causa raíz comprobada** (archivos/funciones) para C01–C12; distinguir hallazgos de hipótesis.
2. **Lista de cambios** por B1–B4 (máximo 8 líneas por sprint).
3. **Contratos/tools afectados** y cómo se preservan estado, revalidación e idempotencia.
4. **Tabla PASS/FAIL** T01–T16, indicando lo no ejecutado; no declarar funcionamiento a partir de una compilación.
5. **Tres conversaciones antes/después** con datos ficticios; una debe editar solo la sede y otra explicar catálogo 5 vs 4.
6. Pasos mínimos para probarlo manualmente, bloqueos y autorizaciones requeridas.

**Empieza inspeccionando el código y el transcript. Avanza por B1–B4 sin detenerte por decisiones ordinarias. Prioriza correcciones comprobables de contexto y negocio, no solo un prompt más largo.**
