# Continuación pre-DEMO y paquete de certificación — cb14cb6

2026-11-04, America/Lima. **Preparación local; ningún despliegue, dispatch, proveedor ni operación comercial ejecutados.**

## 1. Identidad y evidencia realmente comprobadas

| Comprobación | Resultado observado |
|---|---|
| Rama | `feature/llm-first-agent` |
| HEAD local, origin/feature y `git ls-remote` | **cb14cb65d485220af73a5ab1a500387d6fab7e53** |
| Main local/origin/remoto | **5f5ff850e31df3fa73cb2c7f02f65ba6081173c7**, intacta |
| Diferencia frente a main |6 commits |
| Árbol inicial / índice / untracked |limpios/vacíos; no trabajo posterior que sobrescribir |
| GitHub Actions REST filtrado por head_sha=cb14cb6 |**total_count=0**, consulta de sólo lectura; sin Run IDs |
| Parche de manifiesto, DI y validador |ya publicado en cb14cb6; no reimplementado |
| Corpus/rúbrica expuestos |ambos continúan en el árbol HEAD y rastreados; sólo metadatos/nombres consultados |

Se leyeron los cinco documentos obligatorios y las rutas acotadas del encargo. Los informes anteriores describen fotografías previas a publicación; sus números no son CI de cb14cb6.

Evidencia nueva ignorada por Git: `backend/TestResults/LlmFirstCb14Continuation_20261104/`. `verification.json` correlaciona **16/16 archivos de código/tooling** del parche anterior con los mismos SHA256 de `LlmFirstDurableGuard_20261103/revision.json`. Ningún hash del índice anterior de evidencias cambió. El diff de5d26b7→cb14cb6 contiene aquel parche/documentación y la especificación del propietario, sin otro cambio de runtime oculto.

| Evidencia | Naturaleza | Resultado |
|---|---|---|
| Python runner/custodia |**ejecutado ahora** sobre archivos publicados; ficheros opacos sintéticos, HTTP simulado |20/20 PASS |
| Python validador G8 |**ejecutado ahora** |9/9 PASS |
| Python validador de journal requerido |**ejecutado ahora**, TRX sintéticos |4/4 PASS |
| Cuatro YAML / Python embebido |parseado ahora, controles dispatch/permisos/timeouts inspeccionados |4/4 YAML y11 segmentos PASS |
| Backup-ExposedEvaluation.ps1 |parser PowerShell PASS; ejercitado por los3 tests de custodia incluidos en20 |originales NO abiertos/copiados/hasheados |
| restart-red.trx / guard-directed-final.trx |TRX **anteriores**, XML y hashes verificados ahora |2 FAIL originales /95 PASS posteriores |
| current-final.trx |TRX **anterior**, validador oficial reaplicado sin ejecutar dotnet |2154/2154,0 fallos/omisiones; PASS |
| historical-final.trx |TRX **anterior**, validador oficial reaplicado sin ejecutar dotnet |130 ejecutadas,101 PASS,29 FAIL conocidos,0 omisiones; inventario/motivos/hashes exactos |
| Build backend |**NO EJECUTADO de nuevo**: no se modificó código ni pruebas |build-final.log anterior íntegro,0 errores/0 advertencias incremental; no es un build nuevo de cb14 |
| Integraciones reales / ARM64 / proveedores / WhatsApp |**NOT_RUN/BLOCKED** |ningún resultado externo atribuido al SHA |

H06 permanece exactamente `H06/4/A: ReservationEffects`. Se preservaron los29 fallos históricos y el exit1 real de dotnet; el validador sólo acepta ese contrato exacto. No se reejecutaron las2154 pruebas ni se modificaron aserciones/corpus/validadores. Los checks nuevos guardan procedencia explícita; no sobreescriben TRX ni .g8.json anteriores.

Comandos ejecutados de tooling:

```powershell
python -B -m unittest discover -s scripts/llm-first -p 'test_*.py'
python -B -m unittest discover -s .github/workflows -p test_g8_certification.py
python -B -m unittest discover -s .github/workflows -p test_integration_evidence.py
git diff --check
```

## 2. Custodia: retirar del índice conservando las copias locales

Las dos rutas siguen **EXPOSED**, no son un benchmark ciego. No se abrieron, leyeron, copiaron ni calcularon sus hashes durante esta continuación. Confirmación: `git ls-tree -r --name-only HEAD -- <las dos rutas>` y metadatos del índice. La eliminación posterior no recupera privacidad histórica. No force-push, reset ni reescritura de historia.

`scripts/llm-first/Backup-ExposedEvaluation.ps1` se revisó completo: valida destino local absoluto fuera del repo, rechaza ancestros enlazados, verifica ambos orígenes, no sobrescribe copias diferentes y compara SHA256 sin imprimir contenido/hash. No elimina ni cambia Git. **No certifica las ACL del destino**: el propietario debe usar una carpeta privada no sincronizada/publicada y comprobar sus permisos y copia de respaldo antes de aprobar retirada.

**Sólo para el propietario, después de aprobar expresamente la custodia y retirada; NO ejecutado aquí:**

```powershell
Set-Location -LiteralPath 'C:\Proyectos\NexFlow'
$privateBackup = Read-Host 'Ruta absoluta de custodia privada fuera del repositorio'
pwsh -NoProfile -File scripts/llm-first/Backup-ExposedEvaluation.ps1 -BackupDirectory $privateBackup
if ($LASTEXITCODE -ne 0) { throw 'Respaldo no verificado; conservar el índice y los originales' }
# Confirmar ACL privadas, integridad de ambas copias y aprobación del propietario.
# Ejecutar SÓLO después de esa confirmación, nunca como continuación automática:
git rm --cached -- NEXFLOW_EVALUACION_HOLDOUT_12_SINTETICAS_2026-11-03.json NEXFLOW_RUBRICA_HUMANA_HOLDOUT_2026-11-03.md
if ($LASTEXITCODE -ne 0) { throw 'Revisar el índice manualmente; no usar --force' }
Test-Path -LiteralPath 'NEXFLOW_EVALUACION_HOLDOUT_12_SINTETICAS_2026-11-03.json'
Test-Path -LiteralPath 'NEXFLOW_RUBRICA_HUMANA_HOLDOUT_2026-11-03.md'
git diff --cached --name-status -- NEXFLOW_EVALUACION_HOLDOUT_12_SINTETICAS_2026-11-03.json NEXFLOW_RUBRICA_HUMANA_HOLDOUT_2026-11-03.md
git check-ignore --no-index -- NEXFLOW_EVALUACION_HOLDOUT_12_SINTETICAS_2026-11-03.json NEXFLOW_RUBRICA_HUMANA_HOLDOUT_2026-11-03.md
```

Los dos Test-Path deben dar True; el diff **sólo de nombres/estados** debe mostrar D de ambas rutas. No mostrar un diff de contenido. `.gitignore` ya cubre ambas familias y private-evaluations; sus reglas se comprobaron. Ignorar no retira archivos rastreados. `--cached` conserva las copias locales y permite un commit normal de retirada **por el propietario**, junto con su revisión explícita. Esta instrucción sustituye el antiguo `git rm` sin --cached del cierre histórico; no seguir aquel comando si se quieren preservar las copias locales. No se ejecutó ningún rm ni staging.

El reemplazo lo crea otro evaluador fuera del repositorio y del workspace del implementador. Debe ser NUEVO, independiente y congelado por hash después de congelar la implementación. No lo crea ni lee Codex. Mantener evaluaciones futuras fuera de Git, incluso si hay reglas ignore; la allowlist ARM64 no incluye los archivos raíz ni corpora privados.

## 3. Revisión localizada del código publicado

| Control / evidencia de código | Dictamen local |
|---|---|
| `AgentHostProtection.cs:14–50` |Load valida rol, manifiesto máximo4096 bytes, pin, versión y GUIDs; identidad discordante falla con mensaje sanitizado. Rol Demo sin fuentes aborta. Rol ausente con todo DEMO ausente sigue siendo Legacy: límite conocido, no corregir con otra memoria |
| `AgentProtectionIdentity.cs:5–9`, `AgentDemoPolicy.cs:26–60` |identidad inmutable alimenta política singleton; cambiar/apagar AgentDemo no libera el workspace atestiguado. Allows exige workspace/instancia/contacto autorizados y autorización sintética. Deriva inhibe el diálogo; no concede licencias |
| DI Application:37–41, Infrastructure:31–38,105–119; `Program.cs:43–44` |Load antes de registrar clientes/workers, identidad Replace; motores decorados y SaveChanges interceptor instalado. Sin arranque de host ni conexiones en esta revisión |
| `ProcessIncomingMessageCommandHandler.cs:68–91`, `AiResponseOrchestrator.cs:84–85` |workspace protegido se desvía antes de interceptores legacy; apagado/revocación/canal ausente no habilitan fallback comercial |
| `AgentDemoChannel.cs:26–80` |journal y outbound existente, identidad del destinatario revalidada; Sent/Failed/UnknownDelivery/Attempting no generan otro POST. Preparación obsoleta no se sustituye; humano persiste antes de ACK. No equivalencia entre aceptación del proveedor y entrega al teléfono |
| `DemoCommercialGuards.cs:12–35` |7 entradas de mutación de reservas y3 de pedidos protegidas; lecturas y otros tenants delegan a motores existentes |
| `DemoReservationWriteInterceptor.cs:11–19` |Added/Modified/Deleted por SaveChanges sync/async pasan por DemandCommercialWrites. No es una ACL contra SQL privilegiado ni protege un escritor antiguo |
| `FirestoreAgentTurnJournal.cs:76–143` |claim/CAS/fencing y escritura de preparación/contexto dentro de Firestore; no atomicidad SQL+Firestore ni exactly-once de entrega |

No se identificó un bypass nuevo en las rutas examinadas. No es una auditoría general ni certificación del entorno. No se modificaron backend, frontend, tests, prompts, workflows, H06, G8 o motores. P0 histórico de reserva inesperada **ABIERTO**. Escritura comercial real prohibida.

**P1 operativo conocido, no cerrado:** desaparecer simultáneamente rol/manifiesto/pin/AgentDemo puede devolver el host a Legacy. Una imagen antigua tampoco conoce la guardia. Ningún test de DI ni bool de un formulario demuestra su aislamiento. La admisión externa debe negar despliegue/reinicio/rollback cuando falte cualquiera de las evidencias siguientes.

## 4. Atestación externa reproducible — no ejecutada

El operador debe completar una constancia privada por destino y réplica con fecha UTC, revisor, activo/destino explícitamente DEMO, RELEASE_SHA completo, Image ID/digest, DeploymentId, WorkspaceId y hash del manifiesto aprobado. En el informe compartido usar referencias opacas y resultados; no claves, teléfonos, nombres de clientes, payloads, ENV completo ni Compose expandido. Deben censarse también workers consumidores y réplicas fuera del proxy. Una constancia con campos vacíos, meras declaraciones sin evidencia o fuera de la ventana de cambio **no habilita despliegue**.

| ID | Comprobación por operador, de sólo lectura | Criterio de aceptación | Estado actual |
|---|---|---|---|
| O01 |Inventario aprobado del activo, stores, redes, canal e instancia; comparar con producción sin revelar credenciales |destino identificado como DEMO separado, datos sintéticos, sin rutas a clientes reales |BLOCKED_OPERATOR_ATTESTATION |
| O02 |Revisar configuración efectiva del despliegue, precedencia de variables/argumentos y política externa de admisión; mostrar sólo resultado de comparación |NexFlowHost__Role=Demo obligatorio e independiente de AgentDemo, Enabled=false inicialmente; no overrides CLI que lo anulen. Prohibir despliegue si falta la marca |BLOCKED_OPERATOR_ATTESTATION |
| O03 |Metadatos del montaje de cada contenedor e integridad del archivo aprobado; sin mostrar su contenido en logs |manifiesto persistente read-only, hash aprobado, Version1 y GUIDs esperados; ruta del contenedor coincide con ManifestPath |BLOCKED_OPERATOR_ATTESTATION |
| O04 |Identidad efectiva del proceso e inspección de permisos como ese UID/GID, sin intentar escribir |usuario1654:1654 puede leer el archivo y no modificarlo; no privilegios/montajes Docker que permitan remontar; custodia del host no modificable por la API |BLOCKED_OPERATOR_ATTESTATION |
| O05 |Comparar constancias independientes de TODAS las APIs/workers/replicas y generación de configuración |mismo WorkspaceId, DeploymentId, hash/pin y SHA/digest aprobado; registro de ninguna réplica omitida |BLOCKED_OPERATOR_ATTESTATION |
| O06 |Inventario de consumidores, ingress/proxy, imagen activa/anterior y conexiones de cada escritor |ninguna réplica/worker antiguo atiende o reclama el workspace, tampoco desde una cola compartida |BLOCKED_OPERATOR_ATTESTATION |
| O07 |Consultar relación autorizada instancia↔workspace y allowlist privada, sin enviar WhatsApp |instancia Evolution exclusiva asignada al workspace DEMO, contactos de prueba controlados; autenticación webhook preservada, sin alias ambiguos ni contactos reales |BLOCKED_OPERATOR_ATTESTATION |
| O08 |Consultas agregadas acotadas al GUID sintético en SQL y documentos Firestore |cero reservas/pedidos/solicitudes comerciales; sin backlog anterior SQL/outbound ni UnknownDelivery/Attempting que se pretenda reenviar |BLOCKED_OPERATOR_ATTESTATION |
| O09 |Revisar regla de ingreso/consumo independiente de la imagen y procedimiento de rollback |aislar ingreso DEMO Y consumidores antes de recuperar imagen antigua; el corte sobrevive a su arranque. Sin ese aislamiento, rollback prohibido |BLOCKED_OPERATOR_ATTESTATION |
| O10 |Aprobación de SHA/digest/destino/ventana y plan de health/observación; fresh census inmediato antes de cada cambio |doble revisión, todas O01–O09 comprobadas, sin migración ni habilitación comercial, sin cambios productivos no autorizados |BLOCKED_OPERATOR_ATTESTATION |

No se creó un script que convierta declaraciones del operador en PASS. Sin inventario/configuración/ACL reales no puede acreditar estas condiciones. La constancia debe adjuntar evidencias de lectura verificadas; un script preparado tampoco sería una atestación ejecutada.

Separar **admisión del artefacto/configuración propuesta** de **habilitación del ingreso**. Antes de un corte se revisan SHA/digest certificado, configuración deseada privada, montaje/pin y regla externa de aislamiento; las observaciones del contenedor actual no prueban cómo arrancará otro. Si la candidata aún no está instanciada en DEMO, sus comprobaciones runtime siguen bloqueadas. No iniciar API/workers para rellenarlas sin autorización separada y stores/canales ya aislados. Después de una instalación expresamente autorizada, con Enabled=false e ingreso/consumidores DEMO aislados, repetir O01–O10 sobre las instancias realmente creadas antes de permitir diálogo. No atribuir la configuración/UID de una imagen anterior a la nueva. En esta sesión no se aprobó ni ejecutó ninguno de esos cambios.

### Lecturas concretas preparadas para un operador autorizado

No ejecutadas aquí ni autorización para ejecutarlas en Oracle. El operador obtiene IDs y rutas de su inventario privado; no se inventan nombres de servicios, endpoints o destino. Puede inspeccionar **sólo campos seleccionados**:

```bash
# ID de contenedor verificado por el inventario; no imprimir ENV/Compose completo.
docker inspect "$API_ID" --format '{{.Id}} {{.Image}} {{.Config.User}}'
# MANIFEST_PATH_IN_CONTAINER debe coincidir privadamente con configuración y montaje.
# Lectura/permiso únicamente; no touch, chmod, remount ni ejecución de la API.
docker exec --user 1654:1654 "$API_ID" sh -c \
  'test -f "$1" && test -r "$1" && test ! -w "$1"' -- "$MANIFEST_PATH_IN_CONTAINER"
```

El exit0 de esos tests comprueba lectura/no escritura DAC, **no** atesta por sí solo bind read-only, configuración efectiva, ausencia de otras réplicas o segregación. Revisar los metadatos Mounts.RW/Destination y capacidades en memoria, sin volcar campos privados al informe. Comparar hash de bytes del manifiesto dentro y fuera con el aprobado; conservar sólo resultado/hash en custodia autorizada, no contenido. Ninguna shell del contenedor debe iniciar API/migrator.

SQL preparado sólo para el almacenamiento DEMO identificado, con sesión/rol de lectura, GUID sintético parametrizado por el operador y timeout acotado:

```sql
BEGIN READ ONLY;
SET LOCAL statement_timeout = '5s';
SELECT count(*) AS reservations FROM "Reservations" WHERE "WorkspaceId" = :'workspace_id'::uuid;
SELECT "Status", count(*) FROM "InboundMessages" WHERE "WorkspaceId" = :'workspace_id'::uuid GROUP BY "Status";
SELECT "Status", count(*) FROM "OutboxMessages" WHERE "WorkspaceId" = :'workspace_id'::uuid GROUP BY "Status";
ROLLBACK;
```

Son tablas/columnas existentes (`NexFlowDbContext.cs:26–29`, repositorios Inbound/Outbox). La sintaxis de parámetro es para psql; no conexión ni contraseña en comandos/reportes. Cualquier falta de permiso o timeout es BLOCKED, no cero filas. Interpretar estados con los enums vigentes; no publicar payloads ni teléfonos. Consultas de Firestore: agregados count por `workspaces/{GUID}/orders`, `requests` y estados Pending/Attempting/UnknownDelivery de `outboundMessages`, respetando los nombres/casing vigentes de cada repositorio. En un tenant nuevo pueden exigirse cero eventos antiguos; si aparecen históricos, preservar y detener, no borrarlos/reprocesarlos para lograr cero. El operador debe revisar todos los escritores externos y el censo, no inferirlo de estas tres tablas.

Rollback: mantener marca/manifiesto en imágenes compatibles, desactivar AgentDemo sin quitar identidad y confirmar corte de ingress/consumidores DEMO **antes** de arrancar una imagen antigua. El procedimiento read-only sólo verifica el corte; cambiar reglas, parar consumidores o recrear contenedores requiere autorización operacional separada. No se hizo simulación de contenedores, corte ni rollback en esta tarea.

## 5. Congelación y cuatro certificaciones de un único SHA

SHA evaluado por inspección: cb14cb65d485220af73a5ab1a500387d6fab7e53. Los cambios actuales son documentales. La retirada del índice y la publicación de documentos por el propietario producirán otro SHA: **no adivinarlo** ni declarar certificados sus artefactos con runs de cb14cb6 o de versiones previas.

Secuencia pendiente de aprobación: verificar respaldo/ACL → propietario autoriza `rm --cached` de las dos rutas → revisar nombres del staged/diff y documentos → propietario publica en feature, sin merge a main → exigir árbol aprobado limpio y coincidencia HEAD/remoto → fijar RELEASE_SHA completo → autorizar manualmente los cuatro workflows sin mover la rama entre ejecuciones. Nada de esta secuencia se ejecutó automáticamente.

| Workflow | Ref/inputs | Evidencia requerida | Run ID / intento / SHA observado | Estado |
|---|---|---|---|---|
| `rc3-release-checks.yml` |feature congelada |build/publicación y codec Windows; G8 actual≥2154/0 fallos/0 omisiones; histórico130/101/29 exacto; frontend≥83 TS/148 navegador; artifact rc3-release-unit-results |— / — / — |PENDING_CI |
| `rc3-reservation-integration.yml` |misma feature congelada |PostgreSQL temporal+Firestore Emulator aislado; TRX íntegro sin fallos/omisiones; PASS único del método completo de journal; artifact rc3-isolated-test-results |— / — / — |PENDING_CI |
| `concierge-redis-integration.yml` |misma feature congelada |Redis loopback temporal,1 ejecutada/1 PASS/0 fallos/0 omisiones; artifact concierge-redis-test-results |— / — / — |PENDING_CI |
| `agent-runtime-arm64-certification.yml` |mismo ref; release_sha=RELEASE_SHA completo; retain_api_image=false, storage_quota_confirmed=false, artifact_budget_mib=0 |nativo Linux/ARM64, API/migrator construidos sin ejecutar migrator/API; UID/GID1654, permisos, PNG/JPEG/WebP; artifact de evidencia ARM64 |— / — / — |PENDING_CI |

Links manuales: [Release](https://github.com/saintdeyb321/NexFlow/actions/workflows/rc3-release-checks.yml), [PostgreSQL/Firestore](https://github.com/saintdeyb321/NexFlow/actions/workflows/rc3-reservation-integration.yml), [Redis](https://github.com/saintdeyb321/NexFlow/actions/workflows/concierge-redis-integration.yml), [ARM64](https://github.com/saintdeyb321/NexFlow/actions/workflows/agent-runtime-arm64-certification.yml). Estos enlaces no son runs aprobados.

El validador publicado (`verify_integration_evidence.py:10–16`) exige exactamente una definición clase+método:

`NexFlow.Tests.Integration.AgentJournalEmulatorTests.Two_emulator_clients_enforce_one_owner_atomic_context_and_physical_tombstone`

No basta total≥37 ni un nombre parecido. La prueba usa EmulatorOnly y dos clientes; no certifica dos procesos/IAM productivo. Los4 tests del validador pasaron ahora, pero el caso real NO se ejecutó localmente. No habilitar Firebase real ni cuentas de servicio para sustituirlo.

### Ficha a completar por cada workflow e intento

- RELEASE_SHA esperado; workflow_path/ref; Run URL, run_id y **run_attempt**; event=workflow_dispatch; head_branch y head_sha de REST; started/completed UTC y conclusión del intento.
- Jobs: id, nombre, runner/arquitectura, pasos y conclusión; señalar cancelados/omitidos/errores de infraestructura, no contarlos como PASS.
- Cada batería: TRX/JUnit relativo, SHA256, total, executed, passed, failed, skipped/notExecuted, otros errores/timeout. Histórico:29 rojos exactos y resultado del validador, no cero rojos ficticios.
- Artifacts: id, nombre, intento, tamaño, expiración, SHA256 del archivo descargado y de cada evidencia relevante; nunca mezclar reruns. Ausencia/expiración/identidad incongruente = NO_GO.
- Identidad: Release usa `identity` en .g8.json; integración/Redis usan identity.json; ARM64 su paquete de identidad/provenance. Contrastar SHA/run/attempt/job contra REST del MISMO intento y el RELEASE_SHA, no sólo el nombre del zip.
- ARM64: Image ID API y migrator, revisión OCI, plataformas, UID/GID, permisos, codecs. Retener/exportar imagen requiere autorización separada de cuota/coste; no marcar esos inputs true para conseguir un TAR sin permiso.

## 6. Primer ensayo real: preparado el protocolo, aceptación BLOCKED

No se ejecutaron Groq, Gemini ni WhatsApp. No se buscaron claves en appsettings, Oracle o archivos privados. No se recibió nuevo corpus privado ni autorización/modelos/cuotas/presupuestos verificados en esta intervención. Estado **BLOCKED**, no PASS ni calidad humana certificada.

Runner publicado: `scripts/llm-first/evaluate.py:34–81` exige ruta externa+hash, rechaza corpus del repo/digest expuesto, atestación nueva privada, autorización/cuotas, modelos/keys de prueba sólo de entorno, un --case y topes compartidos explícitos. No usar un .env versionado ni pegar claves al chat. Proporcionar variables en el proceso seguro del evaluador, sin volcar sus valores:

```text
NEXFLOW_LLM_LIVE_AUTHORIZED=1
NEXFLOW_LLM_QUOTAS_VERIFIED=1
NEXFLOW_LLM_PRIVATE_CORPUS_APPROVED=1
NEXFLOW_TEST_GROQ_API_KEY / NEXFLOW_TEST_GROQ_MODEL
NEXFLOW_TEST_GEMINI_API_KEY / NEXFLOW_TEST_GEMINI_MODEL
```

Después de congelación y CI aceptados, otro evaluador podrá hacer primero preflight sin HTTP, sólo sobre NUEVO corpus privado:

```text
python -B scripts/llm-first/evaluate.py --live --dry-run --corpus <ruta externa privada nueva> --sha256 <hash nuevo aprobado> --case <un caso sintético del evaluador> --max-http <1..12 aprobado> --max-token-reservation <2048..100000 aprobado> --output <directorio privado nuevo>
```

No ejecutado aquí. Los límites son del arnés, **no** cuotas gratuitas del proveedor ni una estimación de facturación. Verificar modelos habilitados, límites actuales, presupuesto económico y autorización por escrito; una conversación no autoriza todo el corpus. Sólo tras revisar este preflight y cerrar el registro completo de abajo, quitar --dry-run con nueva aprobación de ejecución. No borrar markers Started/Uncertain/Terminal ni reiniciar automáticamente una identidad. No atribuir Gemini si sólo llamó Groq o viceversa; fallbacks cuentan en los topes.

### Registro de evaluación y limitación observada

| Campo por turno/ejecución | Fuente prevista / estado |
|---|---|
| Texto auténtico sintético de entrada / respuesta |corpus externo+case+turn y `live-<case>.json/Observations.PublicText`; custodiar de forma privada, sin PII |
| Proveedor/modelo/HTTP/latencia/tokens disponibles |Requests, HTTPAttempts, ReportedTokens, UsageResponses y summary.json; métricas no reportadas = UNKNOWN, no inferir consumo total |
| Acto interpretado / seguridad |Observations.Act, ActCorrect, Outcome, ResponseValidation, CommercialEffects y OutboundEffects; todos los efectos deben ser0 |
| Herramientas seleccionadas y hechos consultados |**BLOCKED_EVIDENCE_CAPTURE**: AgentTurnResult.Proposal.Tools y Reads existen (`AgentContracts.cs:57–64`), pero el reporte live actual no los serializa |
| Transición contextual completa |StateCorrect sólo es un bool de comprobación; no es snapshot antes/después. **BLOCKED_EVIDENCE_CAPTURE** para una trazabilidad completa |
| Errores/fallbacks |Requests/status y Failure/outcome deben distinguir transporte de comprensión; reporte actual conserva HTTP y errores de contrato, sin prometer toda excepción interna |
| Naturalidad/claridad/utilidad |dos revisores humanos independientes sobre transcript, sin ver expectativas, código ni prompt; PENDING, no derivar del booleano de política |

`AgentLiveEvaluationTests.cs:54–67` registra acto, texto público, aciertos/efectos y métricas, **no** la propuesta tipada, herramientas/hechos ni snapshots completos. No se inventó que esos campos ya están exportados. Antes del primer gasto, el evaluador debe preparar captura de esas observaciones en el arnés aislado y aprobar su sanitización; se necesita autorización/revisión del cambio de tooling o un mecanismo de captura independiente verificable. No se habilita ni reescribe el agente para suplirlo. Este pendiente no es un bypass comercial encontrado; impide considerar lista la evidencia completa del ensayo.

Ficha privada futura por turno: case/turn, texto sintético, respuesta observada, acto/evidencia de interpretación, herramientas pedidas/ejecutadas, fuente y hechos exactos, estado/pregunta/version antes/después, handoff, efecto comercial=0, HTTP/proveedor/modelo/latencia/usage y errores/fallbacks observados. Expectativas quedan sólo en el evaluador, nunca se pasan al modelo. Captura ausente = NOT_VERIFIED. Una muestra autorizada con interpretación real no certifica toda la NLU ni el canal WhatsApp.

## 7. Decisiones y siguiente acción exacta

| Gate | Dictamen | Alcance / condición pendiente |
|---|---|---|
| **OFFLINE_GO** |**GO para revisión/congelación documental** |código publicado correlacionado con evidencia previa íntegra y33 pruebas de tooling ejecutadas ahora; no nuevo build/suite C# ni CI certificado de cb14 |
| **PROVIDER_EVAL_GO** |**NO_GO / BLOCKED** |nuevo corpus privado, autorización, modelos/cuotas/presupuesto y captura completa de herramientas/hechos/contexto pendientes |
| **DEMO_DEPLOY_GO** |**NO_GO / BLOCKED_OPERATOR_ATTESTATION** |O01–O10, cuatro runs del SHA definitivo y autorización SHA/digest/destino/ventana; no reservas/pedidos reales |
| **COMMERCIAL_GO** |**NO_GO** |P0 histórico abierto, evaluación humana/NLU/WhatsApp, privacidad y costos sin certificar |

Cambios de esta sesión: únicamente este informe, encabezado/checkpoint necesario de ESTADO.md e identidad/requisitos de RELEASE_CANDIDATE.md. Sin código productivo/tests/workflows nuevos o modificados. Build nuevo NOT_RUN por alcance de auditoría; el condicional de regresiones completas sólo aplica ante parche de código, que no hubo. Diff check y revisión de los tres documentos realizados; índice sin cambios.

**Próximo paso del propietario:** revisar este paquete; autorizar y ejecutar custodia privada y retirada **sólo del índice**; aprobar/publicar el commit documental/retirada en feature; comunicar SHA definitivo y autorizar/iniciar los cuatro workflows manuales. Atestación del destino y captura/evaluación real son aprobaciones separadas. No habilitar el agente mientras falte alguna. **Detenido para autorización, sin nueva fase.**
