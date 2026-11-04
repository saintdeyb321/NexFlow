# Cierre de guardia de reinicio y preparación de release LLM-first

2026-11-03, America/Lima. **Revisión local terminada; publicación, certificación externa y DEMO no autorizados.**

## Identidad y procedencia

- Rama local: `feature/llm-first-agent`. HEAD local, origin y `git ls-remote`: **5d26b7c73e7ed46823bf49f45e3bbeecb5b9bdc5**. Main local/remoto: **5f5ff850e31df3fa73cb2c7f02f65ba6081173c7**; cinco commits de diferencia, sin modificar main.
- Inicio: índice y archivos rastreados limpios; únicamente la especificación del propietario `docs/NEXFLOW_LLM_FIRST_AUDITORIA_CONTINUIDAD_CRITICOS_PRE_DEMO_2026-11-03.md` sin seguimiento, conservada.
- REST público de Actions, filtrado por head_sha=5d26b7: **0 runs** en esta revisión. No dispatch ni cambios remotos. No hay SHA nuevo ni imagen/digest certificado para el parche.
- Evidencia nueva: `backend/TestResults/LlmFirstDurableGuard_20261103/`. Describe **5d26b7 + diff local**, no CI del commit publicado. Se preservaron todos los TRX anteriores. Índice de hashes y fuente modificada: `evidence-index.json` y `revision.json` en ese directorio.
- El informe CERTIFICACION_PRE_DEMO y los anteriores 2128 PASS describen la revisión anterior. El singleton ya estaba publicado; no se reimplementó.

## Defectos reproducidos y corrección

**P1 confirmado: reinicio elimina el conjunto en memoria.** Los dos primeros tests, ejecutados antes del parche, fallaron: un nuevo contenedor DI sin WorkspaceId liberaba el tenant anterior; con otro WorkspaceId arrancaba sin objeción. `restart-red.trx`: 2 ejecutadas, 0 PASS, 2 FAIL, 0 omitidas.

Ahora hay una identidad de despliegue **independiente de AgentDemo**, leída una sola vez de un manifiesto persistente, fijado por hash. `AgentHostProtection.Load` (`backend/NexFlow.Infrastructure/Configuration/AgentHostProtection.cs:14`) valida rol, ruta local absoluta, tamaño máximo4096 bytes, SHA-256, JSON versión1 y GUIDs. Rechaza identidad dinámica discordante, incluso apagada, y configuración habilitada sin workspace. Fallos emiten razón sanitizada, sin payload/ruta/claves. No crea ni sustituye identidades.

`AddInfrastructure:30` invoca el preflight síncrono **antes de registrar clientes o workers**; Program mantiene AddApplication→AddInfrastructure antes de Build/Run. La identidad inmutable se registra por singleton y alimenta `AgentDemoPolicy.Live` (`AgentDemoPolicy.cs:34`). La política ya existente conserva su protección monotónica y su latch ante deriva; ahora parte siempre del workspace atestiguado. Omitir AgentDemo al reiniciar en un host DEMO correctamente atestiguado mantiene el bloqueo comercial y no habilita diálogo/legacy. El rol Legacy sin configuración DEMO conserva los demás negocios; un reload no puede activar el canal DEMO en ese host.

No hay nuevo almacén de conversación, migración, schema, motor ni llamada de red. Un archivo de identidad de despliegue es suficiente para este piloto de un tenant; necesita custodia operativa verificable. Una lectura acotada al iniciar, cero lecturas de manifiesto o llamadas IA adicionales por turno.

### Contrato operativo obligatorio y límite de garantía

Sólo en el entorno separado autorizado, no en archivos versionados:

```text
NexFlowHost__Role=Demo
AgentProtection__ManifestPath=<ruta absoluta del manifiesto persistente montado read-only>
AgentProtection__ManifestSha256=<SHA256 de los bytes aprobados del manifiesto>
AgentDemo__WorkspaceId=<mismo GUID autorizado>
AgentDemo__Enabled=false
```

Esquema, sin valores de ningún tenant real:

```json
{"Version":1,"DeploymentId":"<GUID aprobado del despliegue>","WorkspaceId":"<GUID sintético aprobado>"}
```

DeploymentId/WorkspaceId/hash deben coincidir en todas las réplicas/API/workers y reinicios. El manifiesto no es una clave ni concede módulos. Operador/evaluador genera y aprueba los valores fuera del repositorio; UID1654 debe poder leer el montaje y no modificarlo. No se generó un manifiesto de un negocio real ni se cambió configuración efectiva.

**Límite P1 pendiente:** si alguien elimina simultáneamente la marca de host, el manifiesto/pin y toda configuración DEMO, el código no puede distinguir ese host de un Legacy legítimo. Tampoco compara por red dos manifiestos distintos que un operador haya aprobado con pins diferentes. Exigir marca de entorno y montaje/pin obligatorios en la admisión del despliegue, inventario independiente por réplica y aislamiento del canal. Sin esa atestación **prohibir despliegue**, aunque el build esté verde. Un hash no es firma ni prueba de procedencia independiente.

**Reinicio/rollback seguro, todavía no ejecutado en infraestructura:**

1. Mantener Enabled=false y aislar el ingreso de la instancia DEMO; impedir que cualquier escritor antiguo pueda reclamar ese tenant. Revisar colas sin borrar ni reprocesar historia/UnknownDelivery.
2. En un host separado autorizado, comprobar el manifiesto/hash y la marca independiente. Ensayar arranque sin opciones dinámicas (protegido), sin manifiesto/pin (debe abortar antes de workers) y con identidad discordante (debe abortar). Los equivalentes DI locales pasaron; salud/arranque Linux y controles externos siguen pendientes.
3. Inventariar todas las réplicas y confirmar que ninguna imagen antigua atiende el tenant. No usar una imagen anterior al guard sobre ese ingreso, incluso con Enabled=false: no entiende el manifiesto.
4. Rollback de imagen sólo con ingreso DEMO físicamente/rutadamente aislado y prueba autorizada de que ningún worker antiguo procesa su cola. Verificar después aislamiento, identidad y health antes de reconsiderar habilitación. No revertir datos ni ejecutar migrator/Down. Si ese corte no puede demostrarse, mantener NO_GO y corregir hacia delante.

El código no prueba una regla de proxy, ACL, montaje read-only o rollback real. El procedimiento requiere prueba por operador en entorno separado; no se simuló evidencia de Oracle.

## Evidencia y regresiones

| Requisito | Test exacto / evidencia | Resultado |
|---|---|---|
| Reinicio con opciones omitidas | `AgentHostProtectionTests.Restart_with_omitted_dynamic_options_keeps_the_attested_tenant_protected` | rojo→verde |
| Reinicio con identidad distinta | `AgentHostProtectionTests.Restart_with_a_different_dynamic_workspace_refuses_to_build` | rojo→verde |
| Fallar antes de workers/clientes | `Invalid_attestation_refuses_registration_before_workers_or_clients` (13 variantes) | PASS; ServiceCollection sin registrar infraestructura |
| Dos hosts/alteración del manifiesto | `Two_hosts_share_the_pinned_identity_and_a_discordant_replica_cannot_start` | PASS; dos DI reales, sin conexiones |
| Apagado/revocación/opciones ausentes | `Attested_identity_survives_disabled_missing_and_revoked_dynamic_options` | PASS |
| Otros negocios | `Ordinary_legacy_host_remains_available_but_cannot_enable_DEMO_by_reload` y `Recreated_host_blocks_all_commercial_mutators_without_touching_other_tenants` | PASS;7 mutadores de reservas y3 de pedidos rechazados; otro tenant/lectura delegados |
| Escrituras EF tras reiniciar | `Recreated_host_blocks_direct_EF_writes_before_SQL` (Added/Modified/Deleted, sync/async) |6 PASS antes de SQL, sin crear schema |
| Ingreso sin caer a legacy | `Recreated_host_rejects_inbound_before_AI_outbound_or_legacy_effects` | PASS handler real/fixture existente;0 IA/outbound/efectos comerciales |
| Canal/journal | AgentDemoChannelTests, AgentTurnJournalTests, AgentJournalRetentionTests, AgentDialogueResilienceTests, AgentRegistrationTests | PASS en dirigidos; replay, CAS/fencing, humano, obsolescencia y UnknownDelivery sin cambios |

Todos son tests deterministas con mocks/SDK fake o archivos sintéticos, **no NLU ni Firestore real**. No se arrancaron listeners/workers conectados ni se ensayó un rollback de contenedores.

| Ejecución | Ejecutadas | PASS | FAIL | Omitidas |
|---|---:|---:|---:|---:|
| restart-red.trx, código anterior |2|0|2|0|
| restart-first-green.trx |23|23|0|0|
| guard-matrix.trx, matriz intermedia |58|58|0|0|
| guard-directed-final.trx |95|95|0|0|
| current-final.trx, G8 vigente final (una ejecución) |2154|2154|0|0|
| historical-final.trx, histórico final (una ejecución) |130|101|29|0|
| Runner Python, rojo previo |17|13|4|0|
| Runner Python final + custodia sintética |20|20|0|0|
| Validador G8 Python sin cambios |9|9|0|0|
| Validador integración específico |4|4|0|0|

No sumar los dirigidos al vigente: se solapan. 26 casos nuevos de AgentHostProtection explican2128→2154. Históricos: **29 fallos reales preservados**, validador exit0 sólo al verificar inventario/motivos/hashes exactos130/101/29; dotnet exit1 registrado. H06 sigue exactamente `H06/4/A: ReservationEffects`. Corpus/evaluadores congelados y g8_certification.py intactos.

Build exigido `dotnet build backend/backend.slnx --no-restore`: **exit0,0 errores,0 advertencias** (incremental final). Al recompilar Infrastructure para las dirigidas aparecieron3 advertencias antiguas: CS8981×2 y CS8602 en catálogo; no corregidas fuera de alcance. `git diff --check` y revisión de archivos nuevos: PASS. YAML4/4 y11 segmentos Python embebidos válidos; script de custodia parseado y ejecutado sólo con archivos opacos de TEMP.

Comandos reproducibles (corpus/salida/live desactivados sólo en los procesos .NET locales):

```powershell
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --disable-build-servers --tl:off -m:1 --filter "FullyQualifiedName~AgentHostProtectionTests|FullyQualifiedName~AgentDemoSafetyTests|FullyQualifiedName~AgentDemoChannelTests|FullyQualifiedName~AgentRegistrationTests|FullyQualifiedName~AgentTurnJournalTests|FullyQualifiedName~AgentJournalRetentionTests|FullyQualifiedName~AgentDialogueResilienceTests" --logger "trx;LogFileName=guard-directed-final.trx" --results-directory backend/TestResults/LlmFirstDurableGuard_20261103
python -B .github/workflows/g8_certification.py run current --trx backend/TestResults/LlmFirstDurableGuard_20261103/current-final.trx
python -B .github/workflows/g8_certification.py run historical --trx backend/TestResults/LlmFirstDurableGuard_20261103/historical-final.trx
python -B -m unittest discover -s scripts/llm-first -p "test_*.py"
python -B -m unittest discover -s .github/workflows -p test_g8_certification.py
python -B -m unittest discover -s .github/workflows -p test_integration_evidence.py
dotnet build backend/backend.slnx --no-restore
git diff --check
```

## Material expuesto y preparación del ensayo

`git ls-tree --name-only HEAD` confirmó, **sin abrirlos**, los dos archivos publicados en5d26b7:

- `NEXFLOW_EVALUACION_HOLDOUT_12_SINTETICAS_2026-11-03.json`
- `NEXFLOW_RUBRICA_HUMANA_HOLDOUT_2026-11-03.md`

Estado: **EXPOSED / evaluación de desarrollo, NO benchmark ciego**. No se leyó, calculó hash, copió ni borró ninguno; no inferir lectura previa del implementador. Retirarlos no restaura privacidad del historial público. `.gitignore` previene nuevos archivos con esos patrones pero no retira los ya rastreados.

Preparación owner-only: `scripts/llm-first/Backup-ExposedEvaluation.ps1 -BackupDirectory <directorio privado absoluto fuera del repo>`. Hace copia opaca y comparación SHA256, no imprime contenido/hash ni borra nada, no cambia Git, rechaza destino interno/enlazado y no sobrescribe copias distintas. **No se ejecutó con los originales**. El propietario verifica ACL/custodia y, sólo después de aprobar el respaldo, retira las dos rutas mediante `git rm -- <ruta exacta1> <ruta exacta2>` en commit normal. Sin staging masivo, force-push ni reescritura de historia. La allowlist ARM64 y el Dockerfile sólo incluyen proyectos backend seleccionados; estos archivos raíz no forman parte de la imagen.

Runner `scripts/llm-first/evaluate.py`: los4 tests rojos probaron que antes aceptaba corpus dentro de repo, copia del digest expuesto, ausencia de aprobación privada y presupuestos implícitos. Ahora live rechaza rutas del repo (también resuelve symlinks) **antes de leerlas**, exige ruta externa+SHA64hex, rechaza el digest público comunicado por el propietario (metadato, no contenido ni fixture), nueva aprobación independiente, autorización y cuotas/modelos verificados, un único --case y presupuestos HTTP/tokens explícitos. El denylist se prueba con digest sintético, no con el conjunto reservado. Mantiene claims durables, no reabre Terminal/Uncertain, no reintenta proveedores automáticamente por iniciativa del runner. La aprobación de privacidad es una atestación del evaluador, no una prueba criptográfica de independencia.

**Preparado, NO EJECUTAR ahora:** tras congelar nuevo SHA y aprobar los cuatro CI, otro evaluador crea y custodia un corpus NUEVO fuera del repositorio y del workspace del implementador. No generar reemplazo ni leer sus expectativas. Con autorización específica y variables de prueba sólo en entorno seguro:

```text
NEXFLOW_LLM_LIVE_AUTHORIZED=1
NEXFLOW_LLM_QUOTAS_VERIFIED=1
NEXFLOW_LLM_PRIVATE_CORPUS_APPROVED=1
NEXFLOW_TEST_GROQ_API_KEY / NEXFLOW_TEST_GROQ_MODEL
NEXFLOW_TEST_GEMINI_API_KEY / NEXFLOW_TEST_GEMINI_MODEL
python -B scripts/llm-first/evaluate.py --live --dry-run --corpus <ruta privada externa nueva> --sha256 <hash aprobado> --case <un Id elegido por el evaluador> --max-http <1..12 aprobado> --max-token-reservation <2048..100000 aprobado> --output <salida privada nueva>
```

Dry-run:0 HTTP; no se ejecutó sobre ningún holdout. Sólo tras revisar el preflight y el presupuesto autorizado se quita --dry-run para **una conversación**; no extender al resto. Token reservation es cota conservadora, no consumo facturado. Contabilizar HTTP de Groq/Gemini/fallback con presupuesto compartido; modelos, cuotas y tarifas se verifican por el operador, no se inventan. Ante timeout/resultado incierto conservar marcador, no repetir identidad. Requerir TRX, identity.json, reporte live completo, proveedores/modelos realmente llamados, latencia/usage disponibles y dos evaluaciones humanas independientes. Ninguna llamada real o comprensión humana certificada en este cierre.

## Cuatro workflows y congelación pendiente

Defecto de evidencia reproducido con TRX sintético: el control anterior aceptaba37 PASS sin journal. Ahora `.github/workflows/verify_integration_evidence.py:10` aplica el parser estricto G8, exige una única definición exacta de clase+método journal, PASS y matriz sin omisiones/errores/abortos. Sus4 tests rechazan ausencia, duplicación, clase falsa y fallo/omisión. Sin cambiar filtros, corpus, motores ni relajar el mínimo37. Sólo se modificó ese workflow; los otros3 preservados.

| Workflow (manual, rama congelada) | Evidencia obligatoria del MISMO SHA definitivo | Estado |
|---|---|---|
| RC3 Release Checks | build; vigente≥2154 sin fallos/omisiones; histórico130/101/29 exacto; frontend≥83 TS/148 navegador; TRX/.g8.json y JUnit del artifact rc3-release-unit-results | PENDIENTE |
| rc3-reservation-integration.yml | PostgreSQL temporal+Firestore EmulatorOnly loopback; total real sin fallos/omisiones; PASS único de `NexFlow.Tests.Integration.AgentJournalEmulatorTests.Two_emulator_clients_enforce_one_owner_atomic_context_and_physical_tombstone`; integration.trx+identity.json, G8 actual/histórico en rc3-isolated-test-results | PENDIENTE |
| concierge-redis-integration.yml | Redis temporal loopback16379;1 ejecutada/1 PASS/0 fallos/0 omisiones; redis.trx+identity.json en concierge-redis-test-results | PENDIENTE |
| agent-runtime-arm64-certification.yml | release_sha completo igual al ref/head_sha, Linux ARM64 nativo; API/migrator construidos, sin ejecutarlos; UID/GID1654/permisos/PNG-JPEG-WebP; identidad/provenance/Image ID. Custodia apagada por defecto, autorización separada cuota/coste si se solicita TAR | PENDIENTE |

Publicación por propietario sólo tras revisión del diff, respaldo/retiro aprobado de expuestos y selección explícita de archivos (la especificación untracked pertenece al propietario). Después exigir árbol aprobado limpio y fijar SHA completo. Iniciar manualmente los4 sin mover feature entre runs. No dispatch desde esta sesión. Por run guardar URL/Run ID/run_attempt/event/head_sha, conclusión jobs/steps, descargar artefactos y verificar hashes/identidad contra REST del mismo intento. Release identifica SHA/run dentro de .g8.json; no inventar identity.json allí. ARM64: retain_api_image=false, storage_quota_confirmed=false y artifact_budget_mib=0 salvo autorización nueva. Cualquier cambio de SHA, archivo ausente, test omitido o journal faltante => NO_GO, aunque la UI sea verde.

Firestore Emulator, PostgreSQL/Redis reales, ARM64, health, IAM/retención real, montaje y rollback: **NOT_RUN/BLOCKED** en esta máquina. No migraciones ni entornos adicionales. Journal conserva su política previa: plazo30s, payload24h, compactación manual acotada, tombstones sin readmisión; no se certifica limpieza/costo real sin ejecutar emulador. Outbound/UnknownDelivery, consentimiento, H06 y F7-01 intactos. No se garantiza exactly-once de entrega ni atomicidad entre PostgreSQL y Firestore.

## Decisiones

| Gate | Decisión | Motivo |
|---|---|---|
| OFFLINE_GO | **GO sólo para revisión del árbol local5d26b7+parche** |95 dirigidas,2154 vigentes, histórico exacto,33 Python, build y diffPASS; garantía operativa no incluida |
| PROVIDER_EVAL_GO | **NO_GO** |falta candidata publicada/CI, reemplazo privado independiente, modelos/cuotas/presupuesto y autorización |
| DEMO_DEPLOY_GO | **NO_GO** |faltan atestación externa obligatoria/aislamiento rollback,4 CI del nuevo SHA, emulador/ARM64 e imagen/destino/ventana autorizados |
| COMMERCIAL_GO | **NO_GO** |P0 histórico ABIERTO, NLU/humano/WhatsApp real y privacidad/costos sin certificar |

No commits, push, merge, dispatch, despliegues, llamadas IA/WhatsApp, migraciones, cambios Oracle ni operaciones comerciales reales. **Detenido para auditoría; no nueva fase.**
