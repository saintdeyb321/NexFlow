# Resultados

## Dictamen

**GO_TO_CODE_REVIEW** del árbol local basado en `1bba6bbb1655ce9ce0ab4f4c8eb346b09920b460`, con el parche Gemini y pruebas pendientes de publicar. **NO_GO_EXTERNAL_MODEL**, **NO_GO_LIVE_RESERVATIONS**, **NO_GO_DEPLOY**. No hay Run ID remoto para este diff ni certificación de proveedores/WhatsApp. P0 histórico no resuelto.

Fases locales 5 y 6 cerradas: gate vigente1936/1936, histórico esperado130/101/29 y build PASS. Las actividades de aceptación humana/proveedor/capacidad efectiva siguen BLOCKED o NOT_RUN; no declarar completa la aceptación externa del producto.

## Evidencia leída, sin sumar ejecuciones solapadas

Todos los TRX están bajo `backend/TestResults/NaturalFreeTier20261102/` (ignorados por Git). Se conservan rojos; no se reescribieron ni eliminaron.

| Artefacto | Ejecutadas | PASS | FAIL | Omitidas | Interpretación |
|---|---:|---:|---:|---:|---|
| baseline-red.trx | 33 | 28 | 5 | 0 | Baseline con seis reproducciones nuevas y27 previas; defectos de nombre, menú, sugerencia e inicio |
| ux-routing-green.trx | 52 | 52 | 0 | 0 | Comprensión/control simulados |
| pilot-green.trx | 81 | 81 | 0 | 0 | Gate por workspace |
| global-admission.trx | 95 | 95 | 0 | 0 | Admisión + resiliencia, lote previo |
| directed-stable.trx | 325 | 325 | 0 | 0 | Lote dirigido previo |
| current-final.trx | 1923 | 1919 | 4 | 0 | Perfil y tres estímulos de picker; explicación en CAMBIOS |
| stable-before-full.trx | 232 | 232 | 0 | 0 | Lote dirigido previo |
| compatibility-green.trx | 153 | 153 | 0 | 0 | Corrección de perfil/picker y sugerencia ambigua |
| current-verified.trx | 1924 | 1924 | 0 | 0 | **Existe, XML válido, gate PASS**. Evidencia anterior; no se atribuye al nuevo parche Gemini |
| quota-final.trx | 96 | 95 | 1 | 0 | Mensaje de recuperación no explicaba que no se interpretó; preservado |
| quota-closed.trx | 96 | 96 | 0 | 0 | Clasificación UNKNOWN/mensaje corregidos antes de publicación |
| quota-resume-red.trx | 24 | 22 | 2 | 0 | Gemini text/chat → UnboundedTokenEnvelope. Prueba antes del parche |
| resume-directed-green.trx | 197 | 197 | 0 | 0 | NaturalExperience62 + AgentStabilization27 + admisión24 + resiliencia84 |
| **current-closed.trx** | **1936** | **1936** | **0** | **0** | **Gate consolidado final vigente, PASS** |
| **historical-closed.trx** | **130** | **101** | **29** | **0** | **G8 esperado, no 130 verdes** |

`current-closed.g8.json`: decision PASS, test_exit_code0. `historical-closed.g8.json`: decision EXPECTED_HISTORICAL_INCOMPATIBILITIES, test_exit_code1; el **validador** devuelve0 al exigir exactamente identidades/razones esperadas. H06 sigue `H06/4/A: ReservationEffects`; las otras28 incompatibilidades y los cuatro hashes congelados permanecen iguales. No hay omisiones ni fallos vigentes tolerados.

## Comandos de esta reanudación y resultados

```powershell
# Rojo antes del parche: 24/22/2/0, exit1
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --filter "FullyQualifiedName~AiGlobalAdmissionTests" --logger "trx;LogFileName=quota-resume-red.trx" --results-directory backend/TestResults/NaturalFreeTier20261102
# Dirigido posterior: 197/197/0/0, exit0
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --filter "FullyQualifiedName~AiGlobalAdmissionTests|FullyQualifiedName~AiResilienceTests|FullyQualifiedName~NaturalExperienceTests|FullyQualifiedName~AgentStabilizationTests" --logger "trx;LogFileName=resume-directed-green.trx" --results-directory backend/TestResults/NaturalFreeTier20261102
# Un único current al cierre de esta reanudación: 1936/1936, exit0
python -B .github/workflows/g8_certification.py run current --trx backend/TestResults/NaturalFreeTier20261102/current-closed.trx
# Histórico final: 130/101/29; validador exit0
python -B .github/workflows/g8_certification.py run historical --trx backend/TestResults/NaturalFreeTier20261102/historical-closed.trx
dotnet build backend/backend.slnx --no-restore
python -B -m unittest discover -s tools/ai -p test_capacity_simulator.py -v
python -B tools/ai/capacity_simulator.py --config tools/ai/capacity_assumptions.example.json --output backend/TestResults/NaturalFreeTier20261102/capacity
git diff --check
```

Build final incremental: exit0, **0 errores/0 advertencias**, 4.12s. Compilación de tests previa emitió tres advertencias preexistentes: dos CS8981 en migración refactor y CS8602 en FirestoreCatalogRepository:228; no se modificaron. Frontend build/lint: **NOT_RUN**, frontend sin cambios. Integraciones contra almacenes/proveedores reales: **NOT_RUN**. Simulador:7/7, 144 UNKNOWN, 0 llamadas reales. Diff check final: PASS (ver ESTADO).

G8 usa su filtro oficial, que excluye Integration/RedisIntegration/ProviderIntegration y separa dos clases históricas. No confundir el current con pruebas reales de almacenamiento. El comando del validador usa --no-build: se compiló el árbol con el dirigido antes de ejecutarlo.

## Mejoras comprobadas y límites de métricas

- Las seis reproducciones iniciales pasaron tras el parche; el set congelado de35 casos está35/35 en la consolidada. Es éxito de mecanismo con interpretación inyectada, **no exactitud NLU ni evaluación ciega**.
- Menú raíz conserva borrador; necesidad dental obtiene hechos del catálogo sin elegir tratamiento; preparación natural no escribe; nombre descartado no reaparece en nueva reserva; agenda de doctor/gestión de cita tienen explicación útil sin inventar capacidades.
- Ejemplo sintético antes/después: «Quiero hacerme una consulta odontologixa» antes no resolvía la entidad; ahora, dada una propuesta IA válida, presenta «Evaluación odontológica», su precio verificado S/60.00 y pregunta si desea información o ayuda para reservar. No elige el servicio para una transacción.
- «Menú» en reserva antes devolvía servicios del paso; ahora muestra acciones principales y conserva el borrador. «Para mañana en la mañana» pregunta la hora dentro de esa franja, sin ejemplo de tarde ni TargetTime implícito.
- Contrato informativo8121→6656 caracteres (~18 % menos); tokens reales UNKNOWN. No nuevas llamadas de composición ni ampliación de cuotas internas. NLU/prompts reales, latencia P50/P95, satisfacción, aclaraciones/100 mensajes y abandono son NOT_RUN, no métricas inferidas de los tests.
- En admisión: sin HTTP al rechazar; fallback sólo financiado; intentos inciertos conservan consumo; cuota/configuración ausente falla cerrada. Cuota pequeña puede bloquear toda llamada: **no activar** hasta calibrar límites, reserva y share.

## Próxima acción autorizable

Auditar este diff y los seis informes. Antes de evaluar con modelos reales: propietario verifica cuotas/modelos/política de datos y autoriza presupuesto/entorno sintético; evaluador independiente mide diez conversaciones y set no expuesto. No activar CTA ni cuotas globales en producción. Resolver o aislar operacionalmente el P0 antes de reservas reales. No se realizó commit, push, workflow, migración, despliegue ni conexión a servicios externos.
