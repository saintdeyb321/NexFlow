# Estado de ejecución

Base: `7c4782b35bd811cc26a502f2ae2b013e554f0238`, rama main. Índice vacío; inicialmente solo los dos planes del propietario sin seguimiento, conservados sin editar.

Fases 0–6: trabajo local finalizado para revisión, con gates externos y consolidación global pendientes explícitos. Alcance: backend, pruebas y esta evidencia; sin servicios externos, migraciones ni cambios productivos.

Skills oficiales inspeccionadas e instaladas completas con licencia; aplicación explícita en esta sesión. El instalador .NET encontró una ruta larga Windows; se descargaron únicamente los dos archivos del directorio oficial. No hay agente Copilot test-engineer: metodología Research → Plan → Implement inline. Scratch no versionable: `%TEMP%/nexflow-agent-testing-20261102`.

Baseline: build PASS (0 errores, 3 advertencias preexistentes), 42/42 pruebas existentes. Reproducción inicial: 11 ejecutadas, 1 PASS, 10 FAIL, 0 omitidas. TRX: `backend/TestResults/AgentStabilization20261102/reproduction.trx`.

Fase 2: coincidencia comercial acotada sobre nombres/alias autorizados; ruta semántica específica conserva Question=Stock en el plan determinista. 5/5 verdes en catalog-after.trx. No existe contrato de inventario: INVENTARIO_NO_DISPONIBLE; no se crearon módulos ni stock.

Fase 3: intención StartOperation con evidencia puede preparar reserva vacía; no confirma. transcript-after.trx: 15/15 verdes, sin omisiones. Incluye ambos modos, A–N, revisión vigente, replay y seis llamadas/minuto como límite sin elevarlo. Se corrigió el fixture nuevo para que la opción 2 realmente mostrada sea Evaluación (orden alfabético existente); no se alteró orden productivo.

Archivos propios productivos: CommerceTurnResolver.cs, EntityMentionResolvers.cs, TurnUnderstandingPipeline.cs. Pruebas: AgentStabilizationTests.cs nuevo, Speak en WhatsAppConversationalRepairTests.cs reutilizable dentro del ensamblado.

Fases 3–4: directed-final.trx **339/339 PASS**, 0 FAIL, 0 omitidas, ~27 s. Incluye CTA TTL/CAS/replay/consentimientos separados, durabilidad, control humano, errores de proveedor y reservas naturales. Negativos previos guards-before.trx 5 PASS/6 FAIL de 11: la referencia local no verificaba TTL/intención de pregunta; se corrigió, y se corrigió el ámbito explícito de sede de la ruta específica nueva. Dos expectativas de la prueba ordinal nueva no correspondían a un reemplazo real: menú e interrupción reanudan legítimamente el selector. Test final persiste explícitamente otra pregunta manteniendo la lista vieja, comprobando que se rechaza; producción de ordinales intacta.

Precedencia semántica: reference-semantic-before.trx 4 PASS/1 FAIL detectó que el SearchTerm redundante podía interceptar UseOfferingReference. Corregido: referencia explícita tiene prioridad. directed-stable.trx **340/340 PASS**, 0 omitidas, ~26 s.

Fase 5: única ejecución completa mediante validador vigente G8: current.trx **1850 ejecutadas, 1847 PASS, 3 FAIL, 0 omitidas**, ~42 s. Se conservan TRX y gate NO-GO de esa ejecución. Tres regresiones del parche: referencias legacy sin CurrentQuestion para elegir tipo de PDF (2) y referencia factual a la línea vigente de un pedido tras aclaración General (1).

Corrección mínima de compatibilidad: hint legacy solo para catálogo amplio, IDs revalidados; nunca selección individual ni escritura. Las consultas sobre líneas del borrador pueden conservar referencia tras aclaración vigente, pero CurrentPurchaseReference sigue restringida a Order/ProductQuery. **compatibility-final.trx: 253/253 PASS**, incluidos los tres fallos y nuevos negativos, sin omisiones. No se repitió la suite completa ni se fabricó un TRX agregado verde.

Fase 6 cerrada: build final PASS (0 errores, 0 advertencias incremental, 3.97 s); histórico independiente **130/101/29**, cero omitidas, validador exit 0 y hashes/casos/motivos intactos, incluido H06. Matriz CAT/CON/ORD/RES/SEC y siete informes completados. git diff --check PASS; HEAD sin cambios, índice vacío. 27 casos nuevos; ningún corpus, validador o aserción histórica editado.

Estado final: APTO PARA REVISIÓN LOCAL DEL DIFF; gate vigente consolidado PENDING (no se repitió la suite completa); NO_GO_PROVIDER_REAL, NO_GO_RESERVATIONS_REAL y NO_GO_DEPLOY. INVENTARIO_NO_DISPONIBLE; sin promesas de stock. P0 histórico NO DETERMINADO.

Siguiente acción, después de revisión del propietario: ejecutar gate consolidado del futuro SHA autorizado y los workflows oficiales si decide publicarlo. Para una repetición local explícitamente autorizada, compilar primero pruebas actuales y usar `python -B .github/workflows/g8_certification.py run current --trx backend/TestResults/AgentStabilization20261102/current-reviewed.trx`, conservando current.trx rojo. No rehacer baseline ni invocar proveedores. No se autoriza commit, push, dispatch ni despliegue por este registro.
