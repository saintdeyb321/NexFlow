# Cierre MVP — evidencia S9 / S10 / S11

Fecha: 2026-10-26. Ejecutor local: Codex. Revisor independiente y firmas operativas: **PENDIENTES**. Este registro distingue pruebas locales, CI y operación real; **no autoriza producción ni declara terminados los sprints bloqueados**.

## 1. Identidad y alcance efectivo

| Fuente | Identidad observada |
|---|---|
| HEAD local, rama main | `991a4ac27c4d233a6fc0cd5d81c720859752fc74` |
| main remoto consultado mediante GitHub | `991a4ac27c4d233a6fc0cd5d81c720859752fc74` |
| Árbol del commit | `f93471ad628712fe3b033f164e23edac1257b7e5` |
| Árbol backend | `0ade814dc47dbfa3ba8bc7f6127702b891e73c9a`, igual a `be07f89c2146f7c4344effad849e1335c9614e86` |
| Diferencia publicada be07f89c → 991a4ac | Seis adiciones Markdown; cero cambios backend/frontend/workflows |
| Imagen activa Oracle / digest / Compose efectivo | **NO VERIFICADOS**. `nexflow-api:306d7c8-perms1` es referencia histórica, no inspección actual |
| RELEASE_SHA final | **NO CONGELADO**: hay una corrección local de test pendiente de revisión/publicación manual |

El árbol inicial conservaba dos documentos del propietario sin seguimiento: `docs/NexFlow_Auditoria_Cierre_S6_S8_2026-10-26.md` y `docs/NexFlow_PROMPT_FINAL_CODEX_S9_S10_S11_GO_LIVE_2026-10-26.md`. Se leyeron y no se modificaron. También se consultaron AGENTS, los cuatro informes S6–S8 y las regresiones relevantes de S1–S5.

Cambios propios: montaje del caso DST de `frontend/tests/reservations-browser.test.mjs`, este informe y la propuesta manual `.github/workflows/agent-runtime-arm64-certification.yml`, **autorizada expresamente por el propietario durante S9**. Sin cambios de producto, backend, expectativas históricas, dependencias, configuración, Dockerfile o workflows existentes. Las evidencias locales quedan en directorios ignorados; no se versionan TRX/JUnit con datos operativos. No se hicieron commit, push, dispatch, migraciones, conexiones productivas ni llamadas IA/WhatsApp.

**Identidad de resultados:** backend/TypeScript e históricos se ejecutaron sobre HEAD inicial sin cambios de código; la primera batería de navegador corresponde a ese árbol y fue roja. La repetición posterior corresponde al worktree con el ajuste de test. Un run de 991a4ac no certifica ese worktree; el propietario debe publicar y congelar un nuevo SHA para correlacionar todos los gates. No se atribuyen resultados de CI de otro SHA.

## 2. S9 — ejecución local y defecto reproducido

Entorno: Windows x64, .NET SDK 10.0.401, Node 24.18.1, Python 3.12.5, Chrome 155.0.8059.85. Se usaron dependencias instaladas y lockfile existente, sin instalaciones. SHA256 de `frontend/package-lock.json`: `6E4B0C903670491F6BC020D8450375A79B81654187384E968D6368DC3A74ABE9`.

| Validación ejecutada | Ejecutadas / aprobadas / fallidas / omitidas | Resultado y evidencia local |
|---|---|---|
| Build solución backend, sin restore | — | **PASS**, 0 errores, 3 advertencias previas; `s9-backend-build.log` |
| Build proyecto de tests, sin restore | — | **PASS**; `s9-tests-build.log` |
| G8 current, runner original | 1538 / 1538 / 0 / 0 | **PASS**; `s9-current.trx`, `s9-current.g8.json` |
| Históricos, runner original | 130 / 118 / 12 / 0 | **EXPECTED_HISTORICAL_INCOMPATIBILITIES**, no 130 PASS; `s9-historical.trx`, `s9-historical.g8.json` |
| Tests del validador G8 | 7 / 7 / 0 / 0 | **PASS**; `s9-g8-validator.log` |
| Frontend build y lint, antes y después del ajuste | — | **PASS**, dist generado; warning Vite de chunk >500 kB, sin error de lint |
| TypeScript | 83 / 83 / 0 / 0 | **PASS**; `typescript.xml`, `typescript.log` |
| Navegador completo inicial | 148 / 147 / 1 / 0 | **FAIL preservado**; `browser.xml`, `browser.log` |
| DST dirigido antes del parche | 1 / 0 / 1 / 0 | **FAIL reproducido**: `Repeated DST hour missing.`; `dst-before.xml` |
| DST dirigido después del parche | 1 / 1 / 0 / 0 | **PASS**; `dst-after.xml` |
| Navegador completo con el primer ajuste de setup | 148 / 144 / 4 / 0 | **FAIL preservado**: el fixture DST contaminaba el W compartido; `browser-after.xml`, `browser-after.log` |
| Navegador completo con setup y workspace aislados | 148 / 148 / 0 / 0 | **PASS**; `browser-final.xml`, `browser-final.log`; 186027 ms |
| Publish API Release sin restore | — | **PASS**, 3 advertencias previas; `s9-api-publish-release.log` |
| CodecSmoke contra ese publish Release, Windows x64 | PNG / JPEG / WebP | **PASS encode/decode/normalize**; `s9-codec-windows-release.log`; no se inició API/worker |
| Guarda CodecSmoke `--require-arm64` en Windows | — | Rechazo esperado exit 1; **no certifica ARM64**; `s9-codec-platform-guard.log` |
| Descubrimiento de pruebas externas, sin ejecución | 39 Integration + 1 RedisIntegration + 2 ProviderIntegration | **PASS inventario**, 0 ejecutadas; `s9-external-inventory.log` |
| Propuesta workflow ARM64, sólo validación estática | YAML + 4 scripts Python + sintaxis shell de permisos | **PASS estático**; `s9-arm64-workflow-static.json`; Docker/Actions no ejecutados |

Ubicaciones privadas locales de evidencia sintética:

- Backend: `backend/NexFlow.Tests/TestResults/mvp-s9-s11/`.
- Frontend: `frontend/node_modules/.tmp/s9/`.
- Publicación local: `backend/NexFlow.API/bin/mvp-s9-publish/`.
- Diagnóstico Chromium: `frontend/node_modules/.tmp/artifacts-browser-startup.json`.
- Manifest de hashes/identidad y conteos verificados directamente desde TRX/JUnit: `backend/NexFlow.Tests/TestResults/mvp-s9-s11/s9-evidence-manifest.json`. No equivale a `identity.json` de un workflow remoto.

Los JSON G8 locales tienen `GITHUB_SHA`, `GITHUB_RUN_ID`, `GITHUB_RUN_ATTEMPT` y `GITHUB_JOB` nulos: **no son artefactos CI**. El runner histórico devolvió exit 1 de dotnet y su verificador aceptó exclusivamente los doce casos/motivos congelados; cotejó inventario y hashes originales. Ningún corpus fue modificado.

### Defecto acotado del harness: dependencia de la semana del reloj

`frontend/tests/reservations-browser.test.mjs:437`: el caso DST cargaba cuatro reservas antes de sustituir el fixture por dos instantes `2026-11-01T05:30:00Z` y `2026-11-01T06:30:00Z`. El 26/10/2026, la semana inicial ya es `[2026-10-26,2026-11-02)`; saltar al 01/11 conserva la misma query key. TanStack Query reutiliza legítimamente la respuesta en caché, no los nuevos datos del fixture. Causa del fallo local reproducido: **setup temporal del test**, no defecto productivo DST demostrado.

Corrección final: preparar las dos filas **antes** de `resetAgenda`, en el workspace sintético exclusivo `workspace-zone-dst`, esperar el contexto horario y navegar. El primer intento corrigió la reproducción individual, pero la batería completa detectó cuatro fallos posteriores: el mapa `changed` del fixture conservaba esas dos filas para el W compartido al reiniciar otros casos en la misma semana. Aislar la identidad del caso evita esa contaminación sin cambiar el fixture ni el producto. Ambas ejecuciones rojas se conservan. Se mantiene exactamente la comprobación de semana/params, ambas horas UTC, una fila 01:30, dos reservas y render responsive. No se añaden invalidaciones al producto, sleeps, skips ni aserciones más permisivas. El diff final es 4 adiciones / 2 eliminaciones en el test.

El problema histórico `DevToolsActivePort` **no se reprodujo** en este equipo. La primera batería obtuvo puerto en 831 ms, sin error de spawn/stderr y salida 0 del proceso del harness de artefactos; esto no demuestra el comportamiento del runner Windows de GitHub.

Comandos locales principales ejecutados (desde raíz, excepto los de frontend):

```powershell
dotnet build backend/backend.slnx --no-restore
dotnet build backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore
python -B -m unittest discover -s .github/workflows -p test_g8_certification.py
python -B .github/workflows/g8_certification.py run current --trx backend/NexFlow.Tests/TestResults/mvp-s9-s11/s9-current.trx
python -B .github/workflows/g8_certification.py run historical --trx backend/NexFlow.Tests/TestResults/mvp-s9-s11/s9-historical.trx
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-build --no-restore --list-tests --filter 'Category=Integration|Category=RedisIntegration|Category=ProviderIntegration'
dotnet publish backend/NexFlow.API/NexFlow.API.csproj --no-restore --configuration Release --output backend/NexFlow.API/bin/mvp-s9-publish
```

```powershell
# CWD frontend; reporters spec/JUnit se dirigieron a node_modules/.tmp/s9/.
npm run build
npm run lint
node --import ./tests/reservations-loader.mjs --test tests/*.test.ts
node --test --test-name-pattern='render DST New York' tests/reservations-browser.test.mjs
node --test --test-concurrency=1 tests/*.test.mjs
```

Advertencias backend: CS8981 en migración histórica `refactor` y designer; CS8602 en `FirestoreCatalogRepository.cs:228`. Se registran, sin refactor ajeno.

### CI externo: ejecución manual acordada, evidencia aún ausente

Revalidación main remoto a **2026-10-26 20:48:17 UTC**: mismo SHA y árbol. Consulta GitHub Actions a **2026-10-26 20:48:18 UTC** para `head_sha=991a4ac27c4d233a6fc0cd5d81c720859752fc74`: **0 runs**. El propietario respondió: «Los ejecutaré manualmente y enviaré los Run IDs». No autoriza que el agente publique ni sustituye los resultados. `gh` no está instalado y el conector permite consulta, no dispatch.

| Workflow manual | Run ID / attempt / SHA observado en run | Artefacto requerido | Resultado |
|---|---|---|---|
| [rc3-release-checks](https://github.com/saintdeyb321/NexFlow/actions/workflows/rc3-release-checks.yml) | PENDIENTE / PENDIENTE / PENDIENTE | `rc3-release-unit-results`: TRX/G8, JUnit TS/browser, diagnóstico Chromium | **BLOQUEADO** |
| [rc3-reservation-integration](https://github.com/saintdeyb321/NexFlow/actions/workflows/rc3-reservation-integration.yml) | PENDIENTE / PENDIENTE / PENDIENTE | `rc3-isolated-test-results`: `identity.json`, TRX/G8, `integration.trx` | **BLOQUEADO** |
| [concierge-redis-integration](https://github.com/saintdeyb321/NexFlow/actions/workflows/concierge-redis-integration.yml) | PENDIENTE / PENDIENTE / PENDIENTE | `concierge-redis-test-results`: `identity.json`, `redis.trx` | **BLOQUEADO** |

Siguiente acción exacta del propietario: revisar el ajuste DST, publicar manualmente la revisión aprobada y comunicar **SHA completo**; abrir cada enlace → **Run workflow** → referencia que resuelva a ese SHA → enviar tres URLs/Run IDs. Si ya ejecutó 991a4ac, conservar esos runs, pero repetir sobre la revisión corregida. El auditor comprobará `head_sha`, attempt, jobs/steps, conclusión, artefactos descargados e identidad: current 1538 sin pérdidas, históricos exactos 130/118/12, TS≥83/browser≥148 sin omisiones, **39** integraciones y **1** Redis aprobadas. El mínimo 37 del workflow no permite aceptar desaparición de dos casos.

### ARM64: BLOQUEADO, sin sustitución por Oracle

El propietario confirmó que no dispone de Docker Windows ni runner ARM64 propio y **prohibió usar Oracle productivo para pruebas experimentales**. S9-C sigue **BLOQUEADO por falta de runner ARM64 certificado**. Sin Java/PostgreSQL/Redis ejecutables locales tampoco se ejecutaron las integraciones; no se instalaron servicios. No existen imagen/digest ARM64 ni resultados de UID/permisos/codec nuevos atribuibles a esta intervención.

Procedimiento ya preparado: [checklist, §2](GO_LIVE_CHECKLIST_NEXFLOW_MVP.md#2-arm64-y-permisos--s6-no-ejecutado). El operador debe aportar runner Linux **aislado** con SDK/Docker existentes y SHA congelado. Crear contexto temporal allowlist de fuentes versionadas, sin .env/firebase.json/cuentas/bin/obj/.git; revisar privadamente que la configuración versionada no contiene secretos. Ejecutar los builds `final` y `migrator` de ese Dockerfile para `linux/arm64` con metadata OCI del SHA. Nunca iniciar el entrypoint migrator.

A la receta existente se exige retener también: digests base y final, ID/arquitectura, UID **y GID** efectivos 1654; lectura de assembly, `.deps.json`, `.runtimeconfig.json` y recorrido de directorios en ambas imágenes. **Ajuste al contrato real:** `appsettings.json` es obligatorio en API; Migrator/Program.cs:11 usa exclusivamente variables de entorno y su csproj no publica ese archivo. Exigirlo al migrator, como hace el ejemplo anterior del checklist, produciría un falso fallo; la propuesta nueva comprueba los archivos realmente requeridos sin ejecutarlo ni alterar producción. Ensayar variante restrictiva 600/700 sin chown que oculte un error. Montaje de secreto **sintético**, sin Firebase ni valores reales: demostrar lectura con owner/grupo previstos, sólo lectura y denegación con permisos ajenos; no imprimir contenido. CodecSmoke con `--require-arm64` debe ejecutarse contra la **misma imagen final por ID inmutable**, red none y API apagada. Windows x64 no sustituye ese resultado. Entregar metadata/log sanitizado/digest y firma del revisor; sin ello S9-C permanece BLOQUEADO.

### Propuesta autorizada: GitHub Actions ARM64 efímero

Archivo nuevo: `.github/workflows/agent-runtime-arm64-certification.yml`. GitHub documenta [`ubuntu-24.04-arm` para repositorios públicos](https://docs.github.com/en/actions/how-tos/write-workflows/choose-where-workflows-run/choose-the-runner-for-a-job); la disponibilidad/cuota efectiva del repositorio sigue pendiente de un run. No se contrataron runners ni servicios.

- Sólo `workflow_dispatch`, permiso `contents: read`, timeout 35 min, checkout sin credenciales persistidas y sin secrets/environments de producción. Input `release_sha` de 40 hex debe coincidir con el SHA del ref que ejecuta el workflow. No permite certificar fuentes de otro commit con el workflow de una revisión distinta.
- Runner Linux ARM64 nativo, repositorio público, checkout limpio y contexto allowlist versionado. Copia fuentes de los cinco proyectos y archivos de build necesarios; excluye .env, firebase.json, bin/obj, cuentas y archivos ajenos. Configuración pública con campos de credenciales no vacíos detiene el gate sin imprimir valores. Contexto temporal con umask 077.
- Construye Dockerfile existente, targets `final` y `migrator`, `linux/arm64`, **sólo --load local**; nunca login/push/deploy. Registra SHA/OCI label, ID, manifest digest de build, arquitectura/USER y materiales/digests base de [metadata Buildx](https://docs.docker.com/reference/cli/docker/buildx/build/#write-build-result-metadata-to-a-file---metadata-file).
- Pruebas con red none, read-only, UID/GID 1654 y entrypoint shell explícito; verificación de ownership/lectura de archivos, variante restrictiva de modos sin chown y bind mounts sintéticos permitidos/denegados. Ni API, workers ni migrator arrancan.
- Extrae sólo SkiaSharp.dll de contenedor **detenido**, compila el harness existente y monta exclusivamente su DLL IL contra deps/runtimeconfig/nativos de la misma imagen API. Exige exit 0 y tres PASS PNG/JPEG/WebP con `--require-arm64`.
- Artifact `agent-runtime-arm64-evidence` por 7 días: `identity.json` con SHA/Run ID/attempt/job/outcomes, `context-manifest.json`, `images.json`, hashes y logs sintéticos. Fallos se propagan, `always()` sólo conserva evidencia; no hay continue-on-error.

**Validación local real de la propuesta:** PyYAML 6.0.3 parseó YAML; compilación de los cuatro scripts Python; `bash -n` del bloque POSIX de permisos. No se ejecutó el workflow ni Docker, ni se simularon resultados ARM64. Revisión independiente del workflow y ejecución remota aún PENDIENTES.

**Límite de procedencia:** no se publica ni sube un archivo de imagen; las imágenes locales desaparecen con el runner. `build_manifest_digest` e image ID no son un RepoDigest de registro ni un artefacto de despliegue retenido. Incluso con este smoke verde, antes de producción faltará una operación de custodia/publicación expresamente aprobada y correlacionar el digest exacto que se vaya a desplegar; reconstruir un tag no conserva automáticamente la certificación.

Acción manual posterior a revisión/publicación: seleccionar el ref del nuevo SHA en **Agent Runtime isolated ARM64 certification**, introducir ese mismo SHA completo y aportar Run ID/attempt + artifact. No se hizo dispatch. El operador aportará por separado el preflight Oracle sanitizado de sólo lectura, según su respuesta explícita.

## 3. Matriz de gates consecutivos

| Gate | Estado real | Evidencia / bloqueo | Responsable siguiente |
|---|---|---|---|
| S9-A identidad inicial | **PASS** | Local/remoto 991a4ac y árboles registrados; trabajo ajeno preservado | Codex; revisión humana pendiente |
| S9-A congelación final | **BLOQUEADO** | Setup DST corregido sólo localmente; no nuevo SHA publicado | Propietario |
| S9-B regresión local | **PASS vigente; históricos conocidos separados** | 1538 backend, 83 TS, 148 browser; TRX/G8/JUnit locales, no CI | Codex / auditor |
| S9-B tres CI mismo SHA | **BLOQUEADO** | Sin Run IDs/artefactos para auditar | Propietario ejecuta; auditor verifica |
| S9-C ARM64 final+migrator | **BLOQUEADO** | Workflow manual propuesto y sintaxis validada; sin ejecución/imagen/permisos/codecs ARM64 | Propietario / release engineer |
| S10-A Q01–Q10 | **NO EJECUTADO** | No autorización/acceso específico a datos/servicios vivos | Operador lectura + negocio |
| S10-B política fiscal y PDF visual | **BLOQUEADO** | Sin decisión TaxId ni template/PDF ficticio aportados | Producto/privacidad |
| S10-C backup/restore Firestore/objetos | **BLOQUEADO** | Herramienta lógica no autorizada/implementada/probada; emulador no iniciado | Responsable datos + propietario |
| S11-A corpus independiente/humanos | **BLOQUEADO** | No corpus privado ≥32 ni ejecutor/revisores aportados | Evaluador externo |
| S11-A Groq/Gemini | **NO EJECUTADO** | Sin modelos/presupuesto/límites/autorización | Propietario + QA |
| S11-B WhatsApp/panel E2E | **NO EJECUTADO** | Sin entorno/número/participantes/aislamiento autorizados | Operador + negocio |
| S11-C preflight/corte | **BLOQUEADO** | Identidad/config/quietud/recuperación no observadas; falta GO SHA/digest/ventana | Operador + propietario |

## 4. S10 — datos, PDF y recuperación

Se conserva el procedimiento parametrizado existente en [MATRIZ_Q01_Q10_VALIDACION_MVP.md](MATRIZ_Q01_Q10_VALIDACION_MVP.md). **Ninguna Q se ejecutó sobre Oracle/Firestore real.** No se tomaron backups ni se aplicaron migraciones, ni siquiera para improvisar un entorno local.

| Q | Dato todavía no observado | Acción mínima autorizable |
|---|---|---|
| Q01 | Imagen/digest actual, labels/Compose/runtime/EF | Operador ejecuta inspect con campos permitidos del checklist §4; confirmar esquema/readiness sin imprimir env/config |
| Q02 | Licencia + módulos + rol/capacidades efectivos | Rol lectura, W-DEMO aprobado; validar columnas/enums, SQL A parametrizado + proyección administrativa de permisos; SQL solo no reproduce todos los entitlements |
| Q03 | Cinco publicados/cuatro reservables por ítem real | SDK paginado por W/Type, proyección pública aprobada, categorías/sedes/flags/duración; emitir razón exacta por cada oferta |
| Q04 | PDF actual/origen/hash/edad/ámbito y contenido | Snapshots inertes PRODUCT/SERVICE, hash del contrato vigente; nunca GetArtifactAsync con efectos laterales |
| Q05 | Jornada global/sede y TimeZone | Snapshots acotados de profile/horas globales/sede y fuente de disponibilidad |
| Q06 | Referencia/pregunta/borrador/versiones reales | Procesar contexto privadamente; sólo flags/edades/alias de mensajes sintéticos autorizados |
| Q07 | Ruta IA/usage/latencia/costo | Telemetría proyectada por ventana/correlación; sin prompts, PII ni logs brutos |
| Q08 | Reserva única y Outbox real | Primero autorización separada de escritura DEMO; luego SQL B por W/source/slot, sólo counts/booleans |
| Q09 | Inbound/outbound/entrega reales | Escritura DEMO previa autorizada + SQL C/SDK inerte limitado; no normalize/resume ni reenvíos UnknownDelivery |
| Q10 | Decisión fiscal, template y PDF visual | Dueño decide whitelist/TaxId y aporta revisión privada de plantilla + PDFs ficticios PRODUCT/SERVICE |

Consultas propuestas: parámetros enlazados, sesión READ ONLY/REPEATABLE READ con timeout 5 s, límites/cursores revisados, ROLLBACK al terminar; confirmar primero esquema real. Conservar sólo fecha UTC, SHA/digest, alias W, método, conteos/booleans y responsable. No se crea índice si falta ni se leen datos privados para completar la tabla.

**Cinco/cuatro:** los fixtures demuestran filtros diferentes legítimos, no la razón del incidente real. Publicado exige tipo/activo/categoría; reservable por sede añade flag de reserva, duración válida y scope. El quinto puede no ser reservable, no tener duración o pertenecer a otra sede; `Origin`/`SourceHash`/edad del PDF también deben comprobarse. Sin Q03/Q04 no se elige arbitrariamente una de esas causas ni se cambian datos para cuadrar cantidades.

**Fiscal:** `CatalogGenerationService.GetContentAsync` transmite TaxId, pero no prueba que la plantilla lo imprima. No se cambia payload/hash por suposición. Pendiente decisión expresa de minimización/whitelist y revisión visual ficticia; cualquier parche de contrato requiere nueva candidata, regresiones de hash/CURRENT/STALE/callback/HMAC/GenerationId y S9 repetido.

**Recuperación:** el backup SQL histórico restaurado no acredita recuperación Firestore/PDF externos. Spark es información reportada, no se consultó billing. No se activó Blaze ni exportación administrada. Falta autorizar alcance/cuotas/destino cifrado/custodia/RPO/RTO/retención e implementación de copia lógica por SDK. Primero ensayo sintético en emulador loopback sin credenciales, API/workers/canal apagados: roundtrip tipado completo de subcolecciones/padres ausentes, IDs/versiones/CAS/dedupe/UnknownDelivery/terminales, scopes y coherencia SQL/objetos. No hay resultado de restore, RPO/RTO medidos ni manifest privado de respaldo nuevo. Sin quietud entre almacenes no se afirma snapshot consistente. **Datos no descartables y ventas bloqueados**; la posible excepción DEMO descartable exige aprobación separada y no elimina los demás gates.

## 5. S11 — evaluación y operación real

No se creó corpus privado en el repositorio ni se mostraron expectativas reservadas al implementador. Falta evaluador distinto con **32 conversaciones multitur inéditas** W-A clínica/W-B comercio/W-C mixto/W-D limitado, corpus/hash congelados fuera de repo, dos revisores humanos y auditor de invariantes. Rúbrica previa a resultados: cero fallos de seguridad/tenant/consentimiento/duplicado, propuesta ≥90% tareas resueltas/derivadas legítimamente y ≥4/5 naturalidad/claridad/utilidad, pendiente ratificación humana. No hay scores observados ni certificación ciega.

ProviderIntegration: dos tests descubiertos; **0 ejecutados, 0 llamadas, costo observado no medido**. El harness directo tiene ocho muestras por proveedor y no equivale al runtime/AiRouter completo. Antes de ejecutarlo: modelos, tarifas, máximo POST total con reintentos/fallback, tokens, costo en moneda, ventana/cuotas y responsable de parada firmados; demostrar límite efectivo antes de usar claves. No se leyeron secretos ni se establecieron flags de autorización.

WhatsApp/panel: **0 ensayos reales**. Pendientes número y participantes autorizados, DEMO descartable aislado, método real de admisión en API única, roles efectivos, umbrales p50/p95/backlog/error/costo ratificados y recuperación. Después de autorización separada, seguir [PLAN_PILOTO_E2E_WHATSAPP_MVP.md](PLAN_PILOTO_E2E_WHATSAPP_MVP.md): observar canal + persistencia única/outbox/panel por W, reserva con resumen/consentimiento y pedido PendingReview sin pago/stock garantizado, interrupciones, privacidad/handoff, revocación y replay. Sent no se contabiliza como Delivered/Read. Latencia/tokens/costo reales siguen **NO MEDIDOS**.

## 6. Corte: procedimiento condicionado, sin comando de producción ejecutable aún

El servidor no se inspeccionó. API_CONTAINER/API_SERVICE, project, config files y su orden, env-file paths, overrides efectivos, digest anterior/nuevo y ventana son **PENDIENTES**. No se convierten los doce overrides históricos ni el tag antiguo en parámetros actuales. Por tanto, **no existe todavía un comando exacto seguro de corte para ese host**.

Secuencia única restante, con operador autorizado:

1. Obtener Q01/read-only del [checklist §4](GO_LIVE_CHECKLIST_NEXFLOW_MVP.md#4-preflight-real-futuro-exclusivamente-lectura-primero), imagen/digest/labels/rutas sin env ni inspect completo, Production y compatibilidad EF sin DDL. Salud de dependencias por checks autorizados, sin QR/desconexión/workflow/conversión como health check.
2. Validar imagen ARM64 retenida por digest y preparar override **únicamente services.<API_SERVICE>.image**. Aplicar comparación Compose efectiva en memoria del checklist §5; cualquier otra diferencia = STOP. No imprimir/guardar config interpolada.
3. Acreditar mecanismo real de cierre de admisión y todos los escritores. Quietud global de cinco minutos, muestras cada 30 s: inbound accionable=0, Outbox pendiente/processing=0, outbound pending/attempting=0, PDF activo=0. Baseline de terminales/DeadLetter/UnknownDelivery preservado. Ningún DELETE/UPDATE/requeue para conseguir cero. Sin barrera o inventario completo = STOP.
4. Backup/restore aislado aprobado, imagen anterior retenida y rollback **demostrado compatible** o contención/fix-forward aprobado. Ni `0da524…` ni `306d7c8-perms1` son rollback autorizado por defecto; no convivir con escritores inseguros.
5. Propietario firma GO separado con **SHA completo, digest y ventana exacta**, impacto, thresholds y recovery. Sólo entonces podrá ejecutarse el comando del checklist §7, con arrays/servicio observados: `docker compose "${CANDIDATE_ARGS[@]}" up -d --no-deps --no-build --pull never "$API_SERVICE"`. Esta línea es una **plantilla no ejecutada**, no aprobación. Nada de down/prune/pull/rebuild/dependencias/migrator.
6. Observar digest efectivo/startup/liveness/readiness y transacciones DEMO durables/canal/panel; ante fuga/duplicado/falta de consentimiento/F7-01/bot tras takeover/entrega incierta reenviada, STOP de admisión y plan compatible aprobado. Reapertura/comercialización requieren decisiones distintas. No hubo post-corte ni health productivo en esta intervención.

## 7. Dictamen y continuación

| Decisión | Resultado | Motivo comprobable |
|---|---|---|
| GO_CANDIDATE | **NO-GO / BLOQUEADO** | Corrección local sin nuevo SHA, tres CI/artefactos y ARM64 pendientes |
| GO_DEMO_PILOT | **NO-GO / BLOQUEADO** | Falta evaluación independiente, permisos/presupuesto y aislamiento/canal DEMO |
| GO_CODE_DEPLOY | **NO-GO / BLOQUEADO** | Falta S9, identidad/config/quietud/recuperación y autorización SHA/digest/ventana |
| GO_COMMERCIAL | **NO-GO / BLOQUEADO** | Sin recuperación Firestore/objetos demostrada, política PDF y E2E/humanos/proveedores reales |

No se confirmó un nuevo P0/P1 productivo en esta ejecución; eso no prueba su ausencia. Defecto P2 de reproducibilidad del test DST corregido localmente. Riesgos de release que obligan a detener: evidencia CI/ARM64 incompleta, recuperación durable no acreditada, privacidad fiscal sin decisión, aislamiento/admisión y rollback sin demostrar. Las doce incompatibilidades históricas se conservan como evidencia roja conocida, no como PASS ni autorización comercial.

**Próxima acción mínima:** propietario revisa/publica el test y el workflow propuesto, congela el nuevo SHA y ejecuta manualmente los **tres workflows existentes y la propuesta ARM64** sobre él; aportar cuatro Run IDs. Operador autorizado aporta Q01 read-only sanitizado, según lo acordado. Auditar esos resultados en este mismo registro antes de habilitar S10/S11 operativos. Las respuestas operativas recibidas no autorizan publicar desde Codex, usar Oracle para smokes ni realizar un corte. No se abre Sprint 12 ni se cambia la API activa.
