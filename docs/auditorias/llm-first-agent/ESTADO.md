# LLM-first — Estado de continuidad

## Estado vigente — continuación de certificación sobre cb14cb6

2026-11-04, America/Lima. HEAD local/origin/remoto feature **cb14cb65d485220af73a5ab1a500387d6fab7e53**; main **5f5ff850e31df3fa73cb2c7f02f65ba6081173c7** intacta,6 commits adelante. Árbol/índice/untracked iniciales vacíos. REST Actions:0 runs para cb14. El parche de manifiesto/DI/validador YA está publicado; no se repitió ni se modificó código, tests o workflows.

Informe vigente: **CERTIFICACION_SHA_CB14_CONTINUACION.md**. Los16 archivos de código/tooling del parche y el índice completo de evidencias anteriores coinciden por SHA256. TRX previos revalidados (sin nueva ejecución .NET):95 dirigidas,2154/2154 vigente, histórico130/101/29 exacto, H06/4/A: ReservationEffects; build previo íntegro. No atribuir esos tests/build a CI de cb14. Ahora se ejecutaron33 pruebas Python existentes (20 runner/custodia+9 G8+4 evidencia journal),4 YAML y11 segmentos Python parseados; PowerShell syntaxPASS. Nuevos metadatos/logs en `backend/TestResults/LlmFirstCb14Continuation_20261104/`, anteriores intactos. Build y suites C# NOT_RUN: auditoría sin parche de código.

Holdout/rúbrica siguen rastreados y EXPOSED, confirmados sólo por metadatos; NO abiertos, leídos, copiados, hasheados ni retirados. Procedimiento owner-only: respaldo con script existente, confirmar integridad/ACL y autorización, luego **git rm --cached** de las dos rutas para mantener copias locales. Sustituye el git rm sin --cached de la documentación histórica; no reescribir historia. No ejecutado. Reemplazo nuevo privado por evaluador independiente PENDIENTE.

No bypass nuevo encontrado en las rutas del encargo. Riesgo de pérdida de TODAS las fuentes de identidad y rollback antiguo requiere admisión externa; constancias O01–O10 preparadas y todas **BLOCKED_OPERATOR_ATTESTATION**. Ningún acceso al destino. Cuatro workflows deben certificar el mismo SHA final, journal con EmulatorOnly y método exacto PASS; ARM64 sin retención de imagen salvo autorización de cuota/coste.

Ensayo real BLOCKED: sin nuevo holdout/autorización/modelos/cuotas. Limitación de evidencia del arnés señalada: live report registra acto/texto/booleanos/HTTP pero no herramientas/hechos/snapshots completos (`AgentLiveEvaluationTests.cs:54–67`). Cerrar captura aislada verificable antes del primer gasto; no fingir que existe, no reescribir el agente.0 llamadas IA/WhatsApp. P0 histórico ABIERTO.

OFFLINE_GO=GO sólo revisión/congelación; PROVIDER_EVAL_GO=NO_GO/BLOCKED; DEMO_DEPLOY_GO=NO_GO/BLOCKED_OPERATOR_ATTESTATION; COMMERCIAL_GO=NO_GO. Cambios de esta sesión:3 documentos únicamente. Diff checkPASS, índice sin cambios; no commit/push/dispatch/despliegue/migración/Oracle. Próximo paso exacto del propietario: revisar paquete, autorizar custodia y retirada del índice, publicar sólo lo aprobado y congelar SHA; autorizar/iniciar cuatro CI de ese SHA. Atestación y ensayo son aprobaciones separadas. **Detenido, sin nueva fase.**

## Registro anterior — guardia de reinicio sobre5d26b7, antes de publicar cb14cb6

2026-11-03. HEAD local/origin/remoto feature **5d26b7c73e7ed46823bf49f45e3bbeecb5b9bdc5**; main **5f5ff850e31df3fa73cb2c7f02f65ba6081173c7** intacta,5 commits de diferencia. REST Actions filtrado por5d26b7: **0 runs**. Índice inicial vacío; especificación del propietario untracked conservada. El singleton de la revisión anterior YA estaba publicado; no se repitió ese parche.

Se reprodujeron2 fallos entre reinicios/DI nuevos: identidad dinámica omitida libera tenant; identidad cambiada no aborta. Ahora manifiesto persistente independiente+SHA256, `NexFlowHost__Role=Demo`, validación síncrona al inicio de AddInfrastructure, antes de SDK/workers, y AgentProtectionIdentity inmutable. Sin AgentDemo dinámico sigue protegido; identidad discordante/manifiesto ausente/pin inválido abortan. Escritores de reservas, pedidos y EF sync/async bloqueados tras recrear host; ingreso real del handler no cae a legacy. Otros tenants mantienen motores. No nuevo store ni schema.

**Límite operativo aún NO_GO:** eliminar simultáneamente marca DEMO, manifiesto/pin y toda configuración DEMO es indistinguible de un host Legacy válido. Exigir atestación independiente obligatoria del despliegue y montaje read-only en cada réplica; rollback antiguo sólo con ingreso/consumidores DEMO aislados. No certificado en servidor. No equiparar pruebas DI con arranque Linux o rollback real.

Evidencia nueva `backend/TestResults/LlmFirstDurableGuard_20261103/`: restart-red.trx2/0/2; final dirigidas95/95; G8 vigente **2154/2154**,0 omitidas; histórico **130/101/29** con inventario/motivos/hashes exactos, H06/4/A: ReservationEffects;9 tests G8 Python+17 runner+3 custodia+4 integración=33 PASS. Build `dotnet build backend/backend.slnx --no-restore` exit0,0 errores/0 advertencias incremental;3 warnings antiguos en recompilación previa sin cambios ajenos. YAML4/4 y11 segmentos Python, script PowerShell parseado, diff checkPASS. No se suman dirigidos al vigente. Todos los TRX anteriores preservados; no nuevo CI aprobado.

Los dos archivos holdout/rúbrica están **publicados y EXPOSED**, confirmados sólo por nombres del árbol Git, NO abiertos/leídos/hasheados/copiados/borrados. No son benchmark ciego. `.gitignore` y `Backup-ExposedEvaluation.ps1` preparan custodia/retiro normal por propietario; originales intactos. Reemplazo externo privado aún PENDIENTE; no lo crea Codex. Runner live ahora exige corpus externo nuevo+hash, rechaza digest expuesto y ruta del repo antes de leer, aprobación independiente, una conversación y presupuestos explícitos. Sus4 pruebas nuevas fueron rojas antes del cambio; ninguna llamada real.

Sólo workflow PostgreSQL/Firestore modificado: exige PASS único del método completo AgentJournalEmulatorTests; antes podía aceptar37 PASS sin ese caso, reproducido con TRX sintético. Otros3 workflows sin cambios, todos preparados para el MISMO SHA futuro autorizado. Journal con emulador real, Redis/PG, ARM64, montaje/rollback, proveedores y WhatsApp: NOT_RUN/BLOCKED.

Informe vigente: **CIERRE_GUARDIA_REINICIO_Y_RELEASE.md**; RELEASE_CANDIDATE actualizado. Candidata **5d26b7+diff local sin commit**, sin nuevo SHA/digest. OFFLINE_GO=GO local condicionado a sus límites; PROVIDER_EVAL_GO/DEMO_DEPLOY_GO/COMMERCIAL_GO=NO_GO. P0 histórico ABIERTO. Detenido para auditoría, sin commit/push/dispatch/Oracle/migraciones/proveedores. Próximo: propietario aprueba custodia y retiro normal de expuestos, revisa/publica archivos explícitos y autoriza cuatro CI del nuevo SHA; luego atestación del entorno y evaluación privada autorizada. No repetir fases A–K.

## Registro anterior — certificación pre-DEMO sobre6bba4c4, antes de publicación5d26b7

Lo siguiente conserva la evidencia histórica. Sus afirmaciones de corpus privado/untracked y HEAD6bba4c4 quedaron superadas por la publicación5d26b7; no son el estado vigente.

HEAD/origin/remoto feature6bba4c4d86ac5bd0a5dbb3c6476916fbb5b238dd; main5f5ff850e31df3fa73cb2c7f02f65ba6081173c7 intacta. F–K fue publicado por el propietario después del checkpoint de abajo. Inicio rastreado limpio; untracked del propietario: corpus independiente12, rúbrica y auditoría de certificación. Corpus/rúbrica **no abiertos ni leídos**; hash comunicado A64059637C90E802F7A4692BF3B4BED38CF9070D16BC0F0F7E8C8EFEA33549B5, no calculado por Codex. Propietario debe custodiarlos fuera del repo antes de publicar.

Informe actual: `CERTIFICACION_PRE_DEMO.md`. P1 reproducido2 FAIL: omitir/cambiar WorkspaceId liberaba protección entre scopes. Corregido con política singleton/identidades monotónicas por host y bloqueo ante deriva; tres archivos backend y tres tests cambiados, no prompts ni motores. No conserva identidades entre reinicios: requiere configuración invariable e ingreso aislado verificados por operador. Remoto6bba4c4 no incluye este parche, falta nuevo SHA.

Evidencia nueva LlmFirstPreDemo_20261103: dirigidos69/69; vigente2128/2128; histórico130/101/29 exacto, cero omisiones; build0 errores/0 advertencias incremental, diff checkPASS; YAML4/4 y12 scripts Python embebidos válidos. GitHub REST devuelve0 runs para6bba4c4; no dispatch. Firestore Emulator no configurado; no IA/proveedores/WhatsApp/Oracle/Docker/migraciones. OFFLINE_GO=GO sólo árbol parcheado; PROVIDER_EVAL_GO/DEMO_DEPLOY_GO/COMMERCIAL_GO=NO_GO por gates externos/autoridad/P0. No repetir A–K. Detenido para revisión; próximo: propietario aprueba/publica nuevo SHA y autoriza cuatro workflows del mismo commit; ensayo sellado sólo después con permiso específico.

## Registro anterior — cierre F–K antes de publicar6bba4c4

Reanudación publicada por el propietario: HEAD/ref feature `f537ceea9dae400d6e04f2da1916f0747a4beea0`; main local/ref `5f5ff850e31df3fa73cb2c7f02f65ba6081173c7`. Inicio sin staged/unstaged; sólo plan maestro F–K untracked, preservado. El bloqueo anterior de revisión automática fue de ejecución, no un test fallido; la ejecución volvió a estar disponible. No commit/push/merge ni cambios de main.

**G:** `backend/TestResults/LlmFirstFinal_20261103/g-completed.trx`34/34 PASS. Se mantuvo la aserción de hora no disponible configurando correctamente el fallo del fixture, y se añadió el caso positivo. `g-filtered-reference-red.trx` reprodujo opciones no presentadas tras filtrar hechos; corregido: selección y referencias sólo sobre entidades visibles, sin lista duplicada. Captura múltiple/edición/franjas/referencias actuales y vencidas verificadas con interpretación inyectada, no NLU real. Motores sin escrituras comerciales.

**H offline:** `h-isolation-retention.trx`18/18 PASS. DemoReservationEngine protege7 entradas de mutación, DemoOrderRepository3 escrituras y DemoReservationWriteInterceptor bloquea EF Added/Modified/Deleted antes de SQL. Protección por workspace independiente del LLM y de Enabled. Compactación física por lote conserva identidad/fence/tombstone, no borra claims ni readmite IA; sólo scope sintético, sin worker/ejecución sobre stores reales. Handoff=Request, sin afirmar notificación/completado. P0 histórico ABIERTO, AISLADO_SOLO_DEMO offline tras cerrar I. Emulador no disponible: nueva integración EmulatorOnly preparada, NO EJECUTADA (dos clientes, no dos procesos). CI actual acepta >=37 integraciones; no se cambia workflow ni se simula aprobado.

**I:** i-completed.trx67/67 PASS,0 omisiones. Canal DEMO independiente del scope experimental, allowlist backend por workspace/instancia/teléfonos y autorización explícita; salida por OutboundMessageService existente, sin otro emisor. Se probaron ACK incierto, fallos antes de outbox/transporte, duplicados, rechazo de Pending legacy sin journal, revocación/toma humana, persistencia de handoff antes del ACK, resolución DI y circuito de diez turnos. Se reprodujeron y corrigieron dos defectos del nuevo apagado: IOptions congelado (i-kill-reload-red.trx) y caída a legacy si Enabled cambiaba entre ingreso y orquestación (i-kill-boundary-red.trx). IOptionsMonitor observa recargas; workspace protegido no cae a legacy. Configuración vacía conserva legacy; apagar Enabled de un workspace DEMO configurado suspende su respuesta y mantiene el bloqueo de operaciones.

**J:** BLOCKED_EXTERNAL_EVALUATION. El propietario confirmó que NO hay corpus independiente ni rúbrica; no volver a pedirlos durante este trabajo. Runner externo por ruta/hash preparado; ningún proveedor ni WhatsApp real ejecutado. Python13/13 revalidado en f-runner-revalidated-green.log; primera ejecución sandbox falló por permisos TEMP, no por lógica (log preservado). Preflight f-preflight.json0 HTTP, modelos UNKNOWN. No repetir A–I ni los gates completados. Ver `CIERRE_FINAL_LLM_FIRST.md` y `RELEASE_CANDIDATE.md`.

**K final:** una ejecución G8 current2123/2123 PASS,0 fallos/omitidas; una histórica130 ejecutadas/101 PASS/29 incompatibilidades exactas autorizadas/0 omitidas, validador exit0 y dotnet exit1 conservado. G8 Python9/9, build solución exit0/0 errores/0 advertencias en compilación incremental final. Tres advertencias antiguas aparecieron al recompilar tests/Infrastructure (CS8981×2 y CS8602 catálogo); no corregidas fuera de alcance. `git diff --check`PASS. TRX/JSON/logs y hashes: backend/TestResults/LlmFirstFinal_20261103/evidence-index.json (incluye29 TRX anteriores y nuevos). Walkthrough10 turnos:13 llamadas lógicas simuladas,5 herramientas,0 HTTP real, tokens/costos UNKNOWN.

Identidad final revalidada local/remota: feature f537cee, main5f5ff85 sin cambios; índice vacío. Candidata es f537cee+diff local, **sin SHA nuevo ni imagen certificada**. Untracked propios: AgentDemoChannel.cs, AgentDemoPolicy.cs, DemoCommercialGuards.cs, DemoReservationWriteInterceptor.cs, AgentDemoChannelTests.cs, AgentDemoSafetyTests.cs, AgentJournalRetentionTests.cs, AgentRegistrationTests.cs, Integration/AgentJournalEmulatorTests.cs, CIERRE_FINAL_LLM_FIRST.md y RELEASE_CANDIDATE.md; además plan F–K del propietario intacto. Inventario final completo/hashes/diff en el directorio de evidencia, no stageado. Sin cambios frontend, migraciones, configuración privada, Oracle, IA real ni WhatsApp.

**Decisiones:** OFFLINE_GO=GO; PROVIDER_EVAL_GO=NO_GO/BLOCKED; DEMO_DEPLOY_GO=NO_GO/WAITING_FOR_OWNER (publicación/SHA final, cuatro CI, emulador/ARM64 y destino autorizado); COMMERCIAL_GO=NO_GO. P0 histórico ABIERTO; AISLADO_SOLO_DEMO mediante barreras verificadas offline, no causa resuelta. `RELEASE_CANDIDATE.md` prepara configuración, health, smoke y rollback; no se ejecutó despliegue. **DETENIDO PARA AUDITORÍA. Próximo paso exacto:** revisión del diff/inventario; sólo el propietario autoriza publicación de nuevo SHA y los cuatro workflows en la rama. No volver a ejecutar suites ni proveedores por esta sesión.

## Registro anterior — Iteración 4 F y G parcial (antes de publicar f537cee)

2026-11-03. feature/llm-first-agent local y remoto verificados vía ls-remote: `e054223c1c105529f6779177e10c799e42ba6724`; main local/remoto `5f5ff850e31df3fa73cb2c7f02f65ba6081173c7`. Árbol inicial sólo especificación Iteración4 sin seguimiento, preservada. El cierre Iteración3 anterior describe el diff antes de su publicación posterior; no se reescribe como CI de e054223. Índice vacío; sin commit/push/merge/dispatch ni cambios de main.

**F:** reproducidos reporte live omitido, PASS sin reporte, marcador global de corpus, bloqueo prematuro de preflight y excepción sin TRX. `backend/TestResults/LlmFirstIteration4_20261103/f-runner-red.log`:10 pruebas,4 fallos/2 errores (incluye subtests), preservado. Runner ahora exige live-<caso>.json, observaciones completas, TRX sin omisiones, correlación de corpus/caso/revisión/modelos y métricas HTTP/tokens respaldadas. Distingue ProviderHttp de InterceptedHttp. Marcador v2 por corpus+caso+SHA+huella de fuentes+modelos/ruta, sin claves; presupuesto no reabre intento. Se crea atómicamente en el guard C# inmediatamente antes del primer HTTP, no durante compilación/preflight. Terminal/Uncertain nunca se reabren; marcadores antiguos se conservan y bloquean su caso o todo el corpus si son ilegibles. Sin HTTP no se consume marcador. Dry-run sin efectos.

`f-runner-green.log`:13/13 PASS (subprocess/observaciones fabricados,0 HTTP); `f-attempt-budget.trx`:5/5 PASS con archivos sintéticos y HTTP interceptado; `f-dry-run.json`:Preflight,0 HTTP. Comandos: `python -B scripts/llm-first/test_evaluate.py`; `dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --disable-build-servers --tl:off -m:1 --filter "FullyQualifiedName~AgentHttpAttemptTests|FullyQualifiedName~AgentEvaluationBudgetTests" --logger "trx;LogFileName=f-attempt-budget.trx" --results-directory backend/TestResults/LlmFirstIteration4_20261103`; runner --live --dry-run con hash congelado, D01 y salida nueva. No ejecutar live sin autorización nueva/cuotas/modelos/presupuesto. Keys no leídas de appsettings/privados ni publicadas.

Firestore Emulator **BLOCKED**, host no configurado. Conjunto independiente10–15 y rúbrica humana **PENDING**, propietario confirmó que no existen todavía. No se crea un corpus propio fingiendo independencia. NLU real **NOT_RUN**; cuotas/tokens realesUNKNOWN. Gate F offline PASS; sus gates externos se mantienen pendientes y permiten continuar G/H offline según la especificación. Sin fase I.

## Registro histórico — Iteración 3, C/D/E completadas localmente

**Checkpoint E/final:** `e-directed-final.trx`126/126 PASS; runner `offline-evaluation-final/evaluation.trx`40/40 PASS con399 turnos observados; gate vigente `current-iteration3.trx`2078/2078 PASS (una ejecución completa); histórico `historical-iteration3.trx`130/101/29,0 omisiones, validador oficial EXPECTED_HISTORICAL_INCOMPATIBILITIES exit0. Motivos y cuatro hashes congelados intactos; H06 exacto `H06/4/A: ReservationEffects`. Python preflight4/4 PASS. `dotnet build backend/backend.slnx --no-restore`:PASS,0 errores/0 warnings,5.23s. Sin frontend cambiado, no build frontend. Diff check PASS; índice vacío, main/HEAD sin mover.

E reprodujo/cerró el rechazo de status de journal desconocido, una regresión de proyección de sedes en el contrato shadow anterior, la pregunta repetida al reanudar datos conocidos y un hook que ignoraba ShouldAiRespond=false. Rojos preservados (`e-schema-repro`, `e-directed`, `e-resume-red`, `e-state-guard-red`). Los fallos de Dispose del mock HTTP se separan como harness. Se conserva pregunta/consentimiento/tenant/CAS/UnknownDelivery y se observan0 efectos comerciales. Contadores sólo por enums; modelo libre no publicable. No se cambió ninguna expectativa histórica ni corpus de desarrollo para obtener verde.

Métricas offline observadas (NO NLU): legacy/shadow/agente133 turnos cadauno, coincidencias de política66/70/133, llamadas lógicas89/196/192, repeticiones textuales23/60/12, efectos0, HTTP real0, tokens realesUNKNOWN. Agente P50/P95 local6.09/11.99ms; no latencia real de proveedor. Reporte completo, matriz de evidencia y comandos en `ITERACION_3_CIERRE.md`. La muestra live fue bloqueada correctamente en preflight por falta de autorización/cuotas; no se buscaron claves ni se ejecutaron proveedores.

**GO sólo revisión/evaluación experimental aislada. NO_GO_LIVE.** Corpus independiente sellado, calidad humana, Groq/Gemini reales, cuotas/costos y Firestore Emulator real permanecen BLOCKED/NOT_RUN; FIRESTORE_EMULATOR_HOST no configurado. P0 histórico sigue pendiente. Retención24h es lógica/reconciliada al observar: payload no observado puede persistir y tombstones crecen; aprobar limpieza/privacidad/costo antes de datos reales, sin liberar claims. Sin API de consumidor ni envío experimental. No conectar a WhatsApp/Oracle ni confirmar operaciones. No hay nueva candidata publicada/CI remoto. Iteración cerrada; no iniciar F.

**Checkpoint D:** corpus de desarrollo NO ciego `AgentDevelopmentConversations.json`,40 conversaciones/133 turnos, SHA256 `98AB7CD025D0A3C6378507179FC5AEC06073AFED270BB18DDBB5F178FFE729EE`, congelado antes de la ejecución. El evaluador ejecuta legacy, shadow y agente en fixtures independientes equivalentes (399 turnos observados), con interpretaciones preparadas explícitas, no NLU. Permite un corpus externo sellado por hash y no pasa expectativas al modelo. `scripts/llm-first/evaluate.py` offline por defecto; runner live opt-in con autorización/cuota, credenciales sintéticas sólo de entorno, marcador de intento, máximo12 HTTP compartidos y reserva de tokens, sin ejecuciones reales autorizadas aquí.

`d-development-first.trx`:71 ejecutadas/69 PASS/2 FAIL/0 omitidas. D15 reprodujo que el nuevo handoff conservaba su marcador de descarte pendiente: ahora se invalida ese marcador propio al pasar a humano, preservando el borrador. D35 reveló un defecto del harness: el mock existente ignoraba Search en servicios; la extensión del fixture sintético ahora respeta búsqueda/ámbito. Corpus/expectativas intactos. `d-development-green.trx`:71/71 PASS,0 fallos/omisiones. Incluye las40 conversaciones y las31 regresiones C. Comando: mismo proyecto/opciones C, filtro `FullyQualifiedName~AgentConversationEvaluationTests|FullyQualifiedName~AgentDialogueTests|FullyQualifiedName~AgentDialogueResilienceTests|FullyQualifiedName~AgentTurnJournalTests`, TRX nuevo. Naturalidad humana, benchmark ciego y semántica Groq/Gemini BLOCKED; comparaciones son de política/integración, no porcentajes de comprensión. E autorizado a continuar, sin F.

2026-11-03. Identidad reconciliada: feature/llm-first-agent local/remota `920fcaebe78fa236f94c5888c58e112dab9008fa`; main local/remota `5f5ff850e31df3fa73cb2c7f02f65ba6081173c7`. El relato «sin commits/push» de abajo es histórico, anterior a la publicación de Iteración 2. Índice vacío; documento Iteración 3 del propietario preservado. No commit/push/dispatch ni servicios externos.

**C0/C implementadas:** journal de resultados separado de los claims shadow anteriores; clave canónica workspace/conversación/source, huella, fencing, Started/Prepared/Failed, deadline 30s, payload 24h por defecto y tombstone sin nueva admisión. Preparación de respuesta y contexto existentes se confirman en la misma transacción CAS Firestore. Pérdida de ACK recupera el resultado; Started incierto no repite IA, al observar después del plazo ofrece explicación terminal segura. No se promete exactly-once de entrega. Sin segundo outbox ni envío.

Entrada experimental en handler/orchestrator después de guards/recovery de efectos y antes de clasificadores lingüísticos legacy. Requiere flag apagado por defecto + workspace explícito + scope backend exclusivo de harness. La API/worker no abre ese scope. El mismo planificador IA usa AiRouter y herramientas de lectura autorizadas. CandidateResponse libre nunca se publica ni persiste: autoridad backend realiza actos tipados y segmentos de hechos verificados. Sólo preparación/descarte reversible en contexto, sin motores comerciales. Pregunta y descarte ligados a workspace/conversación/source/TTL/fingerprint/CAS. Consultas laterales conservan la referencia de cancelación.

**Evidencia C:** `backend/TestResults/LlmFirstIteration3_20261103/c0-c-repro.trx`:16 ejecutadas/14 PASS/2 FAIL/0 omitidas. Defectos nuevos reproducidos: clave con separadores podía colisionar; paso COLLECT_LOCATION no coincidía con pregunta de servicio. Corregidos con clave JSON canónica y metadata coherente. `c-reviewed-local.trx`:31/31 PASS,0 fallos/omisiones. Incluye circuito handler/orchestrator de ocho turnos, crash boundaries, ACK perdido, reinicio, concurrencia, tombstone, revocación, humano, JSON/429/timeout/presupuesto, hechos/monedas y ausencia de efectos. El archivo `c0-owner-red.trx` contiene en realidad4 PASS; su nombre no lo convierte en evidencia roja.

Comando C: `dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --no-build --tl:off -m:1 --filter "FullyQualifiedName~AgentTurnJournalTests|FullyQualifiedName~AgentDialogueTests|FullyQualifiedName~AgentDialogueResilienceTests" --logger "trx;LogFileName=c-reviewed-local.trx" --results-directory backend/TestResults/LlmFirstIteration3_20261103`. Compilación previa con `--disable-build-servers -m:1`; el sandbox impedía iniciar VSTest (intentos sin TRX no declarados aprobados); ejecución local autorizada fuera del sandbox pasó. Error transitorio de test CS1061 por propiedad CurrencyCode corregido a Currency existente; sin aserciones de negocio debilitadas.

**Límites:** habla pública acotada por actos/segmentos, no prosa generativa libre; no edición arbitraria de slots ni disponibilidad nueva. SDK Firestore sobre transporte fake, no emulador real. NLU Groq/Gemini, cuotas efectivas, benchmark ciego, evaluación humana y P0 histórico continúan pendientes. NO_GO_LIVE. D autorizado a continuar offline; no F.

## Registro histórico — Iteración 2 detenida por el gate de auditoría

Rama feature/llm-first-agent. HEAD/main local y ambas refs remotas verificadas: `5f5ff850e31df3fa73cb2c7f02f65ba6081173c7`. Trabajo local de Iteración1 y documentos del propietario preservados; índice vacío. No iniciar C/D/E ni una fase F automáticamente.

Se leyeron especificaciones y todos los archivos del shadow, incluyendo no rastreados, contratos, DI y pruebas. Los siete hashes de archivos de Iteración1 coincidían antes de editar; se verificaron XML y SHA256 de sus TRX39/39,1975/1975 y130/101/29. Son resultados locales, no CI remoto. Los artefactos anteriores siguen intactos.

**P1 reproducido:** repetir el mismo workspace/mensaje ejecutaba otra vez el modelo, incluso concurrentemente o recreando el servicio. El GUID aleatorio de cada evaluación evadía una deduplicación propia. `iteration2-audit-red.trx`:2/0/2/0, duplicados Accepted=true. No hubo duplicación comercial ni outbound; P0 histórico sigue pendiente.

Se aplica la regla expresa del §0 de `docs/NEXFLOW_LLM_FIRST_ITERACION_2_FASES_C_D_E_2026-11-03.md`: ante omisión de deduplicación, corregir/probar y **detener el avance**. Por tanto C/D/E no se implementaron; no es un cierre conversacional ni aceptación de piloto.

**Corrección:** IAgentEvaluationClaims + FirestoreAgentEvaluationClaims, admisión atómica exists=false por workspace/sourceMessageId, con huella del texto/cliente/conversación. Sólo metadatos pseudonimizados bajo el presupuesto IA existente; no contexto/borrador paralelo, motores, migración ni outbound. Duplicate/Conflict se rechazan antes de historial/presupuesto/modelo, con0 llamadas. No se libera el marcador ante timeout, pérdida de ACK o cambio de día. El servicio no falla abierto si el almacén está caído.

Tradeoff explícito: at-most-once sin guardar resultado; una caída después del claim puede impedir recuperar una propuesta. No se presenta el replay como éxito. Recuperación exactly-once de respuesta, retención/limpieza/costo y cualquier vía de consumidor requieren revisión antes de C. Shadow sigue apagado por defecto y todo texto no publicable.

La antigua prueba que repetía seis veces el mismo ID se versionó a IDs distintos conservando las aserciones de cuota/efectos. Replays ahora tienen casos específicos secuenciales/concurrentes. No se tocaron aserciones/corpus históricos.

| Evidencia nueva en backend/TestResults/LlmFirstIteration2_20261103/ | Resultado |
|---|---|
| iteration2-audit-red.trx | 2 ejecutadas,0 PASS/2 FAIL/0 omitidas, preservado |
| iteration2-audit-first-green.trx | 41/41 PASS |
| iteration2-audit-reviewed.trx | 53/53 PASS |
| iteration2-audit-final.trx | 61/61 PASS |
| current-audit-fix.trx / .g8.json | 1997/1997,0 fallos/omisiones; validador PASS |
| historical-audit-fix.trx / .g8.json | 130/101/29,0 omisiones; validador exit0 exige motivos/hashes exactos; H06=H06/4/A: ReservationEffects |
| Backend build --no-restore | PASS,0 errores/0 advertencias;3.00s |
| git diff --check | PASS; main intacta, sin staged |

Todos los tests usan modelos simulados/SDK Firestore con fake; no equivalen a almacenes reales, comprensión de Groq/Gemini o WhatsApp. LLM externo0 HTTP, WhatsApp0 envíos, Oracle0 operaciones. No commits/push/migraciones/despliegues. Advertencias antiguas de una compilación previa documentadas en ITERACION_2_CIERRE.

Archivos de esta intervención: AgentContracts.cs, LlmFirstShadowAgent.cs, DI Infrastructure y LlmFirstShadowTests.cs; nuevos FirestoreAgentEvaluationClaims.cs y AgentEvaluationClaimsTests.cs. DI Application/AgentReadTools originales no alterados. Tres informes ITERACION_2 añadidos; estado Git completo y comandos en ITERACION_2_CIERRE. No cambios frontend ni secretos/configuraciones reales.

**NO_GO_PILOT; C/D/E BLOCKED.** Siguiente acción: revisar el arreglo de replay y este gate antes de autorizar reanudación de C. No preparar corpus supuestamente ciego desde el mismo implementador ni declarar aceptación NLU real. P0 histórico, causa ProviderUnavailable, cuotas efectivas y evaluación humana siguen pendientes. No ejecutar proveedores sin autorización específica.

## Registro histórico — Iteración 1 (conservado)

Base/main local y remoto verificados: `5f5ff850e31df3fa73cb2c7f02f65ba6081173c7`. Rama creada: `feature/llm-first-agent`; main no movida. Único archivo previo sin seguimiento: especificación `docs/NEXFLOW_PIVOTE_AGENTE_CONVERSACIONAL_LLM_FIRST_2026-11-03.md`, conservada sin editar.

## Alcance y estado

Fase A reproducida y prototipo mínimo de Fase B implementado. **Detener para revisión, no iniciar C/D.** Pruebas shadow dirigidas:39/39 PASS; gate vigente1975/1975, histórico esperado130/101/29 y build PASS. Las propuestas no se envían a WhatsApp ni se aplican al estado del negocio. P0 histórico sigue pendiente. No hay una nueva candidata publicada ni CI remoto de este diff.

Se aplicaron skills systematic-debugging/code-testing. El agente especializado test-engineer y catálogo de extensiones no están disponibles; investigación/plan/revisión se hicieron inline, con scratch fuera del repositorio. No se sustituyó por un subagente genérico.

## Fase A: causas y evidencia

| Componente | Evidencia | Conclusión |
|---|---|---|
| AiResponseOrchestrator | `RespondCoreAsync`, líneas101–111 | Recovery se ejecuta antes de PrepareAsync y su Response puede terminar el turno sin interpretación semántica |
| ConversationRecoveryService | `RecoverAsync`, líneas96–125 | RESERVATION_SCOPE detecta Draft, borra la referencia y devuelve únicamente Intent=Reservation; pierde el acto que motivó la aclaración |
| ReservationOperationClassifier | `ReferenceScope`, líneas9–17 | «preparando» identifica Draft correctamente; no es un fallo de reconocimiento de esa palabra |
| DialoguePolicy | `Classify`, línea51 | En reserva, el fallback es RequestedData; una consulta abierta puede convertirse en continuación del formulario |
| TurnUnderstandingPipeline | `PrepareCoreAsync`, rutas locales previas al intérprete | Recovery/slots/resume pueden decidir antes de IA. En el incidente reproducido «Quiero hacerme una consulta» acaba solicitando sede |
| CommerceTurnResolver | `ResolveAsync`, líneas58–110 | SearchTerm literal sin entidad determinada ni sugerencia autorizada termina en aclaración genérica; validar una operación no equivale a orientar una necesidad |
| AiInterpreter | `InterpretCoreAsync`, líneas291–310 | Diferencia timeout/rate-limit/JSON/configuración. **ProviderUnavailable real del incidente: UNKNOWN** sin trazas; se simula JSON inválido sólo como ejemplo diagnóstico |
| ResponseComposer | `ComposeAsync`, líneas44–92 | La opción actual no es reescritura conversacional general: agrupación verificada o apertura local. No se activa ni se modifica |

Rutas: `backend/NexFlow.Application/Features/Automation/` para orquestación/diálogo; intérprete en `Features/AI/Interpretation`. Las fuentes originales no se alteraron. `Legacy_scope_reference_loses_the_cancel_act_and_resumes_location_collection` prueba el defecto antiguo y ausencia de reservas/pedidos, sin convertirlo en consentimiento.

No se inspeccionó Oracle ni Docker. El SHA realmente desplegado y las trazas del segundo mensaje quedan pendientes de operador autorizado. No atribuir un JSON inválido simulado a Groq/Gemini reales.

## Prototipo implementado

Entrada interna `IConversationalAgent.EvaluateAsync(AgentTurnRequest)` → gate de workspace sintético/humano/replay/UnknownDelivery/licencia → snapshot autorizado → AiRouter → tools de lectura opcionales → propuesta tipada → revalidación de estado/pregunta/licencia → resultado **shadow no publicable**.

- `AgentShadow.Enabled=false` por defecto y un único `SyntheticWorkspaceId` explícito. Configuración sólo vinculada como contrato; no se editó appsettings ni se habilitó ningún negocio. El ID debe corresponder a datos realmente sintéticos; el flag no convierte datos reales en sintéticos.
- No hay hook en inbound, endpoint HTTP, worker, envío ni aplicación automática de resultados. La ruta se invoca desde el fixture aislado. Habilitar un setting no reemplaza el bot de WhatsApp. Esto mantiene la ruta antigua intacta mientras se evalúa la arquitectura.
- `AgentTurnContext` es una proyección inmutable de ConversationContextDto/ConversationQuestion existentes; no otro almacén. Incluye slots de preparación (no disponibilidad verificada), pregunta válida/TTL, acción pendiente y seis turnos como máximo,400 caracteres por turno. Omite ClientKey/teléfono del request, nombre del paciente y resumen completo del estado. Historial sólo sintético en estas pruebas: no es un anonimizador general de texto privado.
- El agente recibe todos los mensajes admitidos. No llama a DialoguePolicy ni a Recovery.RecoverAsync. Usa Recovery únicamente para presupuesto, no para decidir intención. La IA propone acto y herramientas; backend valida estructura, referencias, licencias y procedencia, sin confiar en el significado como consentimiento.
- `AgentReadTools` adapta IBusinessReadTools existente: Services, Products, Locations, Profile y Hours. Máximo3 lecturas, resultados autorizados y permisos comprobados antes/después por la fachada existente. Rechaza otro workspace, fuente o hechos sensibles. No existen tools para confirmar, cancelar, escribir, cobrar ni consultar información privada de citas.
- Máximo2 llamadas lógicas a AiRouter: una decisión; otra sólo tras solicitar lecturas. Sin rondas de herramientas adicionales. Deadline20s, entrada24000 caracteres, salida8000, propuesta1800; límites de lectura internos existentes. El router/proveedores mantienen fallback y admisión global existentes, sin cambiar modelos ni cuotas.
- Cada evaluación manual tiene presupuesto propio, sin reutilizar el recibo idempotente de otra evaluación del mismo mensaje. Repetirla consume presupuesto acotado; no repite ningún efecto comercial. Las únicas escrituras del prototipo son contadores de presupuesto existentes en el almacenamiento **simulado**; no guarda/modifica el contexto ni una reserva.
- `ProposeDiscardDraft` exige reserva activa, pregunta vigente ligada al contexto y PendingAction RESERVATION_SCOPE/CANCEL. Es propuesta de pedir descarte, nunca autorización ni ejecución. Un cambio de versión, pregunta, permisos, modo humano o entrega incierta invalida el resultado.
- `Accepted=true` significa propuesta estructuralmente admisible, **no redacción factual certificada**. `Publishable=false` y `ResponseValidation=UnverifiedShadowProposal` son incondicionales. CandidateResponse no tiene validador independiente suficiente para publicarse; se conserva sólo para comparación en TRX. Ningún LLM valida a otro LLM.

## Comparación del incidente (modelos simulados)

Las seis primeras entradas son las aportadas; se añaden «Sí» y consulta de servicios para observar continuidad sin transacción. El fixture usa negocio/catálogo sintéticos. Las respuestas IA son JSON preparados: se prueba integración, no comprensión real.

| Entrada | Ruta antigua observada | Propuesta shadow observada |
|---|---|---|
| Hola quiero sacarme el diente | No identifiqué un producto o servicio inequívoco… | Ofrece revisar servicios sin asumir tratamiento; Information |
| Quiero extraerme el diente | Se fuerza JSON inválido para observar ProviderUnavailable; no se conoce causa real | Orientación informativa; no diagnóstico |
| Quiero reservar | Pide sede | PrepareReservation, sin crear ni confirmar |
| Quiero hacerme una consulta | Insiste en sede | Information; preserva la preparación |
| Quiero cancelar la reserva | Pide alcance | ClarifyCancellationScope |
| A la que estamos preparando | Pierde cancelación y vuelve a sede | ProposeDiscardDraft con QuestionId exacto, pide decisión de descarte |
| Sí | Solicita elegir sede | Propuesta de aclaración; no ejecuta descarte ni operación |
| ¿Qué servicios ofrecen? | Catálogo y retorno al formulario | Propuesta informativa; ninguna mutación |

El shadow lee las mismas instantáneas de la ruta antigua **antes** de cada turno; no mantiene una conversación autónoma persistente. La propiedad durable de nuevas preguntas y la aplicación de propuestas pertenecen a una Fase C autorizada, no se simulan como implementadas.

## Pruebas y medición

Todas bajo `NexFlow.Tests.Concierge.LlmFirstShadowTests`. Evidencias ignoradas por Git en `backend/TestResults/LlmFirst20261103/`.

| Requirement | Evidence |
|---|---|
| Causa antigua y ocho turnos | `Legacy_scope_reference_loses_the_cancel_act_and_resumes_location_collection`; `Eight_turn_incident_compares_old_and_shadow_routes_without_real_nlu_or_business_effects` |
| Cancelación ligada a pregunta e interrupción sin pérdida | `Shadow_keeps_cancel_reference_and_information_without_mutating_the_durable_draft` |
| Necesidad no literal y servicio autorizado | `A_nonliteral_need_can_read_real_services_but_never_selects_or_confirms_a_treatment` |
| Off por defecto/tenant/humano/replay/entrega incierta | `Disabled_foreign_human_replay_and_unknown_delivery_never_reach_the_model` |
| JSON/herramienta/facts/pregunta/workspace/commit falsos | `Untrusted_json_tools_facts_questions_and_commit_commands_are_rejected` |
| CAS/pregunta caducada/revocación/toma humana | `Stale_snapshot_handoff_revocation_and_expired_questions_cannot_validate_a_proposal` |
| Cinco tipos de lectura autorizados | `Authorized_read_tools_return_only_the_requested_business_source` |
| Módulos revocados, sin consultar repositorio | `Read_tools_recheck_workspace_licenses_before_consulting_restricted_repositories` |
| Revocación durante lectura | `Revocation_during_a_read_removes_facts_before_the_agent_receives_them` |
| Procedencia y privacidad de facts | `Read_tool_provenance_cannot_be_substituted_by_another_workspace_or_private_data` |
| Loop, exceso de tools, timeout y cancelación | `Tools_and_model_failures_have_a_bounded_safe_exit_without_effects` |
| Repeticiones sin concesión gratuita ni efectos | `Repeated_shadow_evaluations_charge_their_own_budget_without_business_effects` |
| Snapshot sin nombre/resumen ni módulos ajenos | `Context_snapshot_uses_the_current_question_without_patient_identity_or_unrelated_modules` |

Rojo inicial `iteration1-red.trx`:21 ejecutadas,19 PASS/2 FAIL; dos contratos positivos del prototipo no implementado, junto a caracterización legacy. No se presenta ese rojo como prueba de fallo de proveedor real. Después hubo un error de compilación CS0103 por namespace de MessageKind; corregido. `iteration1-prototype.trx`:21/20/1 por aserción de texto JSON escapado, corregida parseando JSON. `iteration1-expanded.trx`:34/32/2 por comparar LastUpdated de contextos vacíos efímeros; fixtures ahora persisten primero el contexto sintético. No se debilitaron aserciones del negocio ni tests anteriores.

`iteration1-stable.trx`:36/36. `iteration1-reviewed.trx`:39/39,0 fallos/omisiones, incluyendo aserciones de motivos exactos para rechazos. En los ocho turnos hay8 llamadas lógicas,9217 caracteres de payload de entrada acumulados (excluye el contrato system repetido) y0 HTTP a proveedores. La consulta con herramienta separada usa2 llamadas. Tokens/fallbacks/latencia real de proveedor UNKNOWN/NOT_RUN. El resultado incluye duración local medida y llamadas; AiTelemetry existente conserva telemetría HTTP/modelo/uso si un proveedor se ejecuta en el futuro; nuevos contadores sólo usan outcomes/failure kinds, sin cuerpos ni PII.

### Cierre verificable

| Validación | Ejecutadas | PASS | FAIL | Omitidas | Resultado |
|---|---:|---:|---:|---:|---|
| Shadow dirigido, iteration1-reviewed.trx | 39 | 39 | 0 | 0 | PASS |
| Gate current, current-iteration1.trx | 1975 | 1975 | 0 | 0 | PASS, validador exit0 |
| G8 histórico, historical-iteration1.trx | 130 | 101 | 29 | 0 | EXPECTED_HISTORICAL_INCOMPATIBILITIES, validador exit0 |

Los29 rojos históricos no se vuelven verdes artificialmente: identidades/motivos exactos y hashes de los cuatro corpus/evaluadores congelados comprobados por el validador oficial. H06 sigue `H06/4/A: ReservationEffects`. El filtro vigente excluye integraciones reales y separa las dos clases históricas; no afirmar certificación de PostgreSQL/Firestore/Redis reales con este gate.

```powershell
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --filter "FullyQualifiedName~LlmFirstShadowTests" --logger "trx;LogFileName=iteration1-reviewed.trx" --results-directory backend/TestResults/LlmFirst20261103
python -B .github/workflows/g8_certification.py run current --trx backend/TestResults/LlmFirst20261103/current-iteration1.trx
python -B .github/workflows/g8_certification.py run historical --trx backend/TestResults/LlmFirst20261103/historical-iteration1.trx
dotnet build backend/backend.slnx --no-restore
git diff --check
```

Build final incremental: exit0,0 errores/0 advertencias,3.51s. La compilación de tests previa mostró tres advertencias preexistentes: dos CS8981 de la migración refactor y CS8602 de FirestoreCatalogRepository:228; sin cambios en esos archivos. Ninguna advertencia nueva pendiente. Frontend build/lint NOT_RUN: sin cambios frontend. Integraciones externas/proveedores/WhatsApp NOT_RUN. Todos los TRX rojos se conservan; no se repitió la consolidada tras pasar.

HEAD y main finales siguen en `5f5ff850e31df3fa73cb2c7f02f65ba6081173c7`, rama activa feature/llm-first-agent, índice vacío y diff check PASS. Cambios de código: sólo los seis archivos listados (dos DI, tres clases/contratos y un test); más este checkpoint nuevo. Especificación original sin seguimiento conservada. No se movió main ni se modificaron sus refs/commits.

## Archivos de esta iteración

- Nuevos: `backend/NexFlow.Application/Features/Automation/Agent/{AgentContracts,AgentReadTools,LlmFirstShadowAgent}.cs`.
- Nuevo: `backend/NexFlow.Tests/Concierge/LlmFirstShadowTests.cs`.
- Modificados: `backend/NexFlow.Application/DependencyInjection/DependencyInjection.cs` (dos registros), `backend/NexFlow.Infrastructure/Persistence/DependencyInjection/DependencyInjection.cs` (binding de opciones apagadas).
- Este checkpoint. La especificación previa del propietario continúa sin seguimiento y sin modificaciones.

Frontend, motores, contexto durable/DTO existente, guards de H06/G8, corpus/evaluadores, proveedores, PostgreSQL, Firestore, Redis, Evolution, migraciones y defaults anteriores sin cambios. No hay commit, push, merge ni despliegue.

## Límites y siguiente gate

GO sólo para revisión de este prototipo shadow sintético. NO-GO para activación DEMO/WhatsApp, NLU real, publicación de texto libre, confirmaciones o despliegue. P0 histórico pendiente. Sin prueba ciega ni acceso a trazas sanitizadas no hay causa determinada de ProviderUnavailable ni métricas de comprensión real.

Antes de C: aprobar contrato de respuesta factual independientemente verificable, retención/privacidad de historial real, propiedad durable de pregunta/acto en el mismo CAS, aplicación segura de preparación/descarte y replay de evaluaciones. Las lecturas se limitan a5 resultados por consulta con la fachada existente; el prototipo no implementa navegación exhaustiva ni debe usarse para afirmar que son todos los servicios. Disponibilidad, gestión de citas registradas y cualquier escritura siguen en motores/controles existentes. Modelos reales requieren autorización, cuota/presupuesto verificados y datos sintéticos. No se ejecuta ninguna de estas tareas en esta iteración.
