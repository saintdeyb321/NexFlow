# NEXFLOW — CIERRE DE ARQUITECTURA Y COMPORTAMIENTO DEL BOT

Trabaja sobre `C:\Proyectos\NexFlow`, partiendo del HEAD actual y preservando todos los cambios locales. Lee primero `docs/NexFlow_Auditoria_Integral_Bot_2026-10-29.md` (copiado desde el documento de auditoría adjunto) y el transcript de WhatsApp del 27/10.

OBJETIVO: corregir los defectos reales del asistente WhatsApp para que interprete y comunique naturalmente con hechos verificados y herramientas por licencia, sin armar un árbol creciente de expresiones ni poner en riesgo transacciones.

## Restricciones
- No crear staging, otro entorno aislado ni Docker local. No tocar Oracle ni bases reales.
- No commit/push/pull/fetch/workflows/deploy; usar comandos git **locales de lectura** si hacen falta para verificar el árbol.
- No refactor masivo, ni añadir un framework de agentes.
- No eliminar controles de autorización, estado, consentimiento, idempotencia, errores o takeover humano.
- No escribir una gramática de frases: preferir significado contextual usando IA e interpretación validada; conservar atajos solo cuando inequívocos.
- No ejecutar una batería de tests completa por cada edición. Usar tests focalizados y un build final; declarar por separado simulación e IA real.

## Tareas en orden

**A. Diagnóstico verificado:** comprobar cada hallazgo H1–H9 del MD, refutar los que no se sostengan con el código actual y establecer prioridad. No copiar hallazgos sin inspeccionarlos. Proteger trabajo local.

**B. Seguridad de consentimiento:** confirmar la semántica de «de acuerdo» ante una reserva/pedido en revisión. Si existe riesgo, introducir una distinción contextual mínima entre acuse de recibo y autorización inequívoca; añadir una regresión para ello. Mantener el respaldo del resumen vigente y su fingerprint.

**C. Contexto e intención:** centralizar o acotar la prioridad de comprensión semántica frente a heurísticas, sin sustituir los estados deterministas. Corregir rutas que confundan «quiero cambiar la sede» con solicitud de dirección, o preguntas de catálogo con selección de servicio. Conservar interrupciones y borradores.

**D. Respuestas de secretaria virtual:** implementar una capa ligera de redacción contextual optativa para *explicaciones informativas* a partir de hechos verificados y restricciones. El modelo no puede crear ni alterar nombres, precios, duraciones, sedes, horas, permisos, mensajes de consentimiento ni enlaces. Si no se puede verificar la salida, usar el fallback legible actual. No habilitar generación libre sobre transacciones. Si no hay una verificación segura sencilla, documentar bloqueo en lugar de inventar garantías.

**E. Herramientas por licencia:** realizar inventario real de capacidades y asociar herramientas de lectura tipadas a los módulos permitidos, reaprovechando `BusinessReadTools` y el backend. Separar consulta de efecto; no habilitar operaciones mutantes por `ToolCall` directo ni conceder permisos por inferencia. Entregar primero la matriz de acceso y el adaptador mínimo; si implica reescribir proveedores, parar y justificarlo antes de extender alcance.

**F. Tokens y latencia:** contabilizar las llamadas HTTP reales con fallback, registrar input/output tokens sin PII, reducir contexto e instrucciones redundantes de `AiInterpreter`, y proponer/implementar un límite de gasto por workspace solo si es compatible con persistencia y arquitectura existente. No sobrecargar cada turno con segunda llamada a IA si el beneficio no está demostrado.

**G. Verificación focalizada:** 8–12 casos sobre conversación real: «En la de conce»; «¿qué servicios tienen?»; explicación 5/4; «Editar → La sede → Sanjeronimo»; «La 1» después de interrupción; «Hoy 8pm» / «¿y a las 7?»; «me separas dos cajas»; consentimiento ambiguo; repetición de confirmar; takeover humano. Separar tests con interpretación inyectada y evaluación de proveedor real. Si no hay credenciales de proveedor, marcar NO EJECUTADO. Ejecutar build y tests pertinentes, no generar otro laboratorio.

## Entrega
Devolver tabla para H1–H9 (confirmado, descartado, pendiente, solución y riesgo), diff por archivo, pruebas realmente ejecutadas, ejemplos de mensajes WhatsApp mejorados, métricas de tokens si existen, y criterio GO/NO-GO para piloto. No hacer commit, push ni despliegue; esperar autorización.
