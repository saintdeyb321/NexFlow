# Estado de ejecución

## Estado vigente: fases locales 5/6 cerradas, detener para revisión

HEAD/main de reanudación y final: `1bba6bbb1655ce9ce0ab4f4c8eb346b09920b460`. Árbol e índice limpios al entrar; el propietario ya había publicado la implementación anterior. No reiniciar fases0–4.

Se leyó `current-verified.trx`: XML válido, 1924 ejecutadas/aprobadas, cero fallos/omisiones. Existía también quota-closed.trx96/96. No se atribuyen esos resultados a cambios posteriores.

Revisión focalizada solicitada encontró Gemini texto/chat sin límite de salida con AiQuota habilitado. Rojo `quota-resume-red.trx`:24 ejecutadas,22 PASS/2 FAIL/0 omitidas (UnboundedTokenEnvelope). Parche mínimo: maxOutputTokens1024 también fuera de JSON; no forzar MIME JSON ni activar redacción IA. Configuración parcial, cuotas pequeñas, fallback financiado429/timeout/agotamiento y consumo se verifican con doce nuevos casos. `resume-directed-green.trx`:197/197 PASS.

**Gate final vigente `current-closed.trx`:1936 ejecutadas,1936 PASS,0 FAIL,0 omitidas; validador G8 PASS/exit0.** Histórico final `historical-closed.trx`:130 ejecutadas,101 PASS,29 FAIL esperados,0 omitidas; validador exit0 exige identidades/razones y cuatro hashes intactos. H06 exacto `H06/4/A: ReservationEffects`. Build `dotnet build backend/backend.slnx --no-restore`: PASS,0 errores/0 advertencias. Compilación de tests previa mostró tres warnings existentes documentados en RESULTADOS.

Simulador Python7/7 PASS;144 escenarios UNKNOWN y0 llamadas reales. 35/35 casos congelados pasan como mecanismo con interpretación controlada, **no NLU real ni corpus ciego**. Prompt8121/6656 caracteres; tokens reales UNKNOWN.

Se completaron los seis informes. Diferencia de esta reanudación: sólo GeminiAiProvider.cs, AiGlobalAdmissionTests.cs y esos seis Markdown. Índice Git sigue vacío, sin archivos ajenos sobrescritos. Revisión de diff y `git diff --check`: PASS. Frontend, configuración productiva, corpus/evaluadores G8, migraciones, motores, AiRouter y defaults CTA/cuotas sin cambios.

Evidencia local ignorada por Git: `backend/TestResults/NaturalFreeTier20261102/`. `identity-local.json` vincula HEAD, hashes SHA256 de ambos archivos de código, `resume-source.diff` y seis artefactos TRX/G8; no es identidad de un release publicado. Comandos exactos en RESULTADOS. Los rojos anteriores permanecen conservados.

**GO_TO_CODE_REVIEW**. **NO_GO_EXTERNAL_MODEL / NO_GO_LIVE_RESERVATIONS / NO_GO_DEPLOY**. P0 histórico pendiente de trazas. Cuotas/modelos/política de datos, calibración de reserva/share, capacidad Firestore real, aceptación humana y Groq/Gemini/WhatsApp reales BLOCKED/NOT_RUN. Una cuota pequeña puede admitir cero llamadas; no activar sin calibración. No hubo servicios externos, secretos, commits, push, workflows ni despliegues.

Siguiente acción: revisión del diff y evidencia por el propietario/auditor. Antes de cualquier ensayo externo se requiere autorización de presupuesto/entorno sintético y cuotas verificadas; antes de reservas reales, resolver o aislar el P0. No iniciar otra fase ni desplegar automáticamente.

## Registro histórico conservado (anterior a la reanudación)

Base inicial: `39432d7bf870bf5e0ba370738b05e3bd750bc9ab`. Índice vacío; inicialmente dos documentos del propietario sin seguimiento, conservados. Baseline y reproducciones con fixtures existentes. No hubo llamadas externas, configuraciones productivas ni migraciones.

Skills existentes systematic-debugging/code-testing aplicadas inline; test-engineer de Copilot no disponible. Cuotas efectivas Groq/Gemini UNKNOWN: no se abrió panel privado ni secretos.

Fase 0/1: baseline-red.trx 33 ejecutadas, 28 PASS/5 FAIL/0 omitidas; 27 eran AgentStabilization originales. Rojos: dos modos de nombre descartado, menú global, orientación de servicio y variante semántica de inicio. Se congeló NaturalExperienceEvaluation.json (35 entradas sintéticas separadas de desarrollo; SHA256 141744B0EBDF3A4EC3A42CE4C8C1EBEB44D59BB4C62B89C30E6274ED4FBF1EF7). No es corpus ciego ni prueba de NLU real.

Fases 2/3: se corrigieron lifetime de nombre al terminar/descartar, menú global y directivas semánticas tipadas, orientación informativa a servicios autorizados sin seleccionar tratamiento, manejo informativo de cita existente, horario sin ejemplo contradictorio. ux-routing-green.trx 52/52; pilot-green.trx 81/81. Los dos defectos adicionales de precedencia del set congelado se corrigieron sin tocar sus entradas. Pedidos/solicitudes se pausan sólo por workspace opt-in, sin tocar licencias ni históricos; motores tienen bloqueo adicional. Continuación de pedido descartado a reserva mantiene sólo fecha/hora validadas en el mismo CAS y exige respuesta posterior ligada a pregunta/workspace/conversación. CTA usa su descarte existente, sin estado paralelo.

Fase 4: admisión global duradera implementada por cuenta/proyecto/modelos compartidos en Firestore existente; opt-in off por defecto. RPM/TPM móviles, RPD/TPD por zona de reset configurada, margen y share por workspace, sin devoluciones tras fallos. global-admission.trx 95/95 (84 resiliencia existente + 11 nuevos casos); local-complete.trx 70/70, incluido menú sin IA con cuota UNKNOWN. Simulador offline: 6/6 Python, 144 escenarios UNKNOWN con cuotas efectivas no proporcionadas, cero llamadas reales. Corregida precisión decimal del ceil tras test rojo (968 vs 969). Prompt completo 8121 caracteres y piloto 6656 en la medición previa al ajuste final; tokens de proveedor UNKNOWN.

Registro intermedio de Fase 5: directed-stable.trx325/325. Se retiró un resguardo de texto vacío innecesario: ResponsePlan ya rechaza el vacío antes de salida; test verifica el contrato sin debilitarlo. Negativos detectaron invitación de reserva sin capacidad del servicio/licencia y se corrigió. En ese corte faltaban nuevas pruebas de abreviatura/colisión, franja, request directo, cancelación General, suite vigente completa y build; se completaron posteriormente.

Fase 5, continuación: stable-before-full.trx 232/232; final-new-cases.trx 73/73. La primera suite completa current-final.trx dio 1923 ejecutadas, 1919 PASS/4 FAIL/0 omitidas: tres estímulos de tests de picker asumían «menú» contextual, contrario al nuevo menú principal global, y una regresión de lectura de perfil al rechazar una reserva. Se conserva TRX rojo. Se acotó la exclusión de referencias de cita al atajo de horarios, preservando lectura de perfil. Tres pruebas actuales (no corpus/evaluadores congelados) mantienen todas sus aserciones de paginación/IDs/replay y piden «continuemos» para el picker; fixtures manuales de servicio pendiente ahora no tienen servicio ya seleccionado. Nueva prueba negativa encontró que una sugerencia informativa podía romper una coincidencia ambigua; se impide. Ningún corpus G8 fue alterado.

Simulador definitivo:7/7 pruebas Python y144 escenarios UNKNOWN, cero llamadas externas; CSV/Markdown regenerados con Decimal. `current-verified.trx`, dejado pendiente de lectura al interrumpirse, ya fue verificado como1924/1924. Los resultados finales posteriores están al inicio de este registro. P0 histórico continúa pendiente; no hay autorización de servicios reales.
