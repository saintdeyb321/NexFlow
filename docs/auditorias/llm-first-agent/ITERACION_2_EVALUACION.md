# Iteración 2 — Estado de evaluación

**Fase D: BLOCKED / NOT_RUN.** No se inició porque la auditoría previa reprodujo un defecto de replay que activa la regla de parada del §0 de la especificación principal. No se fabricó un corpus ciego ni se presentaron 40–60 conversaciones del implementador como independientes.

## Evidencia disponible

| Nivel | Resultado real |
|---|---|
| 1: contratos con modelos simulados | PASS del arreglo de replay y pruebas heredadas; comandos/TRX nuevos en ITERACION_2_CIERRE |
| 2: runtime propietario + almacenes aislados/emulados | NOT_RUN; Fase C no iniciada. El SDK con fake no es un emulador ni infraestructura real |
| 3: Groq/Gemini reales | BLOCKED / NOT_RUN; no autorización de llamadas,0 HTTP reales |
| WhatsApp real | NOT_RUN;0 envíos |
| Corpus ciego independiente40–60 conversaciones | NOT_RUN; no corpus externo aportado ni construido como falsa independencia |
| Claridad humana≥4/5, exactitud≥90 %, referencias≥95 % | NOT_RUN; ningún porcentaje se infiere de mocks |
| Tokens/latencia P50/P95 reales, cuota gratuita efectiva | UNKNOWN; no mediciones externas ni paneles consultados |

## Comparación antes/después comprobada

- **Antes:** dos invocaciones del mismo workspace/mensaje, incluso recreando el servicio o mientras el primero estaba en el modelo, podían generar dos evaluaciones aceptadas. Los tests nuevos fallaron con Accepted=true para el duplicado.
- **Después:** una sola admisión atómica. El duplicado devuelve EvaluationReplay con0 llamadas; cambiar su texto/cliente/conversación devuelve conflicto sin otra interpretación. Un commit de admisión con ACK perdido no se reabre.
- Sin cambios en la redacción del bot legacy ni nuevas capacidades conversacionales. Shadow continúa generando propuestas no publicables. La comparación de ocho turnos de Iteración1 se conserva y vuelve a pasar como **prueba con JSON controlado**, no como evaluación ciega o NLU real.

No hay comparación nueva legacy/shadow/agente propietario: esa vía aún no se construyó debido al gate. La causa del ProviderUnavailable histórico y el P0 de reserva inesperada siguen UNKNOWN/PENDIENTE.

Antes de reanudar D debe aprobarse el cierre del arreglo y completarse C. La evaluación independiente necesitará entradas/criterios congelados por un evaluador distinto, hash previo, almacenamiento separado y autorización/presupuesto para cualquier proveedor real. Nada de ello se declara implementado en esta intervención.
