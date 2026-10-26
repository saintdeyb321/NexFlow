# Cierre MVP — evidencia S9 / S10 / S11

Fecha: 2026-10-26. Estado vigente: **cuatro certificaciones CI aprobadas sobre `cedd19469ca715616eb53d2cfd4d47732870b94e`**, auditadas independientemente según el propietario; metadata de los cuatro runs confirmada por lectura GitHub en esta intervención. Trabajo actual: custodia de la imagen, §2-C. El historial anterior se conserva con sus SHAs y no sustituye esa evidencia. **No hay autorización de despliegue ni artefacto binario retenido todavía.**

## 1. Identidad y alcance efectivo

| Fuente | Identidad observada |
|---|---|
| HEAD local, rama main | `cedd19469ca715616eb53d2cfd4d47732870b94e` |
| SHA de los cuatro runs GitHub consultados | `cedd19469ca715616eb53d2cfd4d47732870b94e` |
| Árbol del commit | `5f46a7bd679f6a4e31eb377e3cd9405a45a0bf09` |
| Árbol backend | `0ade814dc47dbfa3ba8bc7f6127702b891e73c9a`, igual a `be07f89c2146f7c4344effad849e1335c9614e86` |
| Diferencia publicada 991a4ac → b1ae17 | Workflow ARM64, setup DST, informe central y dos Markdown; backend sin cambios |
| Imagen activa Oracle | **Q01 inventario PASS, evidencia del operador**: `nexflow-api:306d7c8-perms1`, ID completo en §4; no es el candidato |
| RELEASE_SHA certificado | `cedd19469ca715616eb53d2cfd4d47732870b94e`; no confundir con el worktree de custodia nuevo sin publicar |

Al iniciar la tarea de custodia, HEAD local coincide con el SHA certificado y `git status --short` está vacío. No se sobrescribe trabajo ajeno. No se repiten auditorías generales ni suites del producto; el alcance actual es workflow/helper/pruebas de custodia e informe.

Cambios actuales: extensión opt-in del workflow ARM64, helper CI `.github/workflows/arm64_image_custody.py`, catorce pruebas puras de custodia/privacidad y este informe. La propuesta de exportación/upload/importación quedó autorizada expresamente por el propietario durante esta tarea; sólo se prepara localmente. **DST y código productivo intactos**; sin cambios backend/frontend, corpus, dependencias, Dockerfile ni otros workflows. Evidencia nueva ignorada: `backend/NexFlow.Tests/TestResults/arm64-custody/`. Sin commit, push, dispatch, Docker local/Oracle, migraciones, acceso productivo, proveedores, gastos ni transferencias.

**Identidad de resultados:** las pruebas backend/TypeScript/históricas siguientes se ejecutaron previamente sobre 991a4ac, y el navegador final sobre su worktree con el setup DST que después se publicó en b1ae17. No se atribuyen esos PASS a CI de b1ae17. En esta reanudación se ejecutan sólo las pruebas Python y validaciones estáticas afectadas por el parche de CI; no se repiten builds del producto intacto. Todos los gates remotos deben corresponder al nuevo SHA que publique y apruebe el propietario.

## 2. S9 — historial local preservado antes de publicar b1ae17

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

### CI externo: aprobado sobre cedd194; historial de preparación preservado

Histórico: no había runs para 991a4ac/b1ae17 en las consultas anteriores. Ese bloqueo quedó superado para **cedd194**. Los cuatro runs siguientes devuelven `completed/success` y el SHA certificado en la consulta actual. Conteos y contenido de TRX/JUnit/artefactos provienen de la auditoría independiente comunicada por el propietario; no se presenta esta lectura de metadata como una nueva descarga o auditoría de esos archivos.

| Workflow manual | Run ID / attempt / SHA observado en run | Artefacto requerido | Resultado |
|---|---|---|---|
| [rc3-release-checks](https://github.com/saintdeyb321/NexFlow/actions/runs/40405179329/attempts/2) | 40405179329 / 2 / cedd194 completo arriba | Backend 1538/1538; TS 83/83; browser 148/148; históricos 118 PASS + 12 incompatibilidades exactas | **PASS** |
| [rc3-reservation-integration](https://github.com/saintdeyb321/NexFlow/actions/runs/40405184965) | 40405184965 / 1 / mismo SHA | PostgreSQL + Firestore, 39/39 | **PASS** |
| [concierge-redis-integration](https://github.com/saintdeyb321/NexFlow/actions/runs/40405190080) | 40405190080 / 1 / mismo SHA | Redis, 1/1 | **PASS** |
| [agent-runtime-arm64-certification](https://github.com/saintdeyb321/NexFlow/actions/runs/40406074161) | 40406074161 / 1 / mismo SHA | API/migrator ARM64, permisos, PNG/JPEG/WebP | **PASS** |

La certificación anterior permanece asociada exclusivamente a **cedd19469ca715616eb53d2cfd4d47732870b94e**. No se solicita repetir su auditoría. La modificación de custodia actual no está publicada ni ejecutada: necesitará aprobación, un SHA nuevo y evidencia del workflow actualizado. No se atribuirán a ese SHA resultados de cedd194 ni se relajará la política de identidad única antes de desplegar.

### ARM64: PASS en run 40406074161; custodia binaria todavía pendiente

La ausencia de Docker Windows se mantiene y Oracle productivo sigue prohibido como runner. **La ejecución ARM64 sí existe ahora y está aprobada** en GitHub, según la evidencia indicada; no fue realizada localmente por Codex. Ese job no conservaba el binario. No es posible recuperar retroactivamente su imagen de un runner destruido. La futura exportación debe tomar el ID de la imagen ensayada **en la misma nueva ejecución**, no reconstruir otra después del smoke ni afirmar que recuperó el binario del run anterior.

Procedimiento ya preparado: [checklist, §2](GO_LIVE_CHECKLIST_NEXFLOW_MVP.md#2-arm64-y-permisos--s6-no-ejecutado). El operador debe aportar runner Linux **aislado** con SDK/Docker existentes y SHA congelado. Crear contexto temporal allowlist de fuentes versionadas, sin .env/firebase.json/cuentas/bin/obj/.git; revisar privadamente que la configuración versionada no contiene secretos. Ejecutar los builds `final` y `migrator` de ese Dockerfile para `linux/arm64` con metadata OCI del SHA. Nunca iniciar el entrypoint migrator.

A la receta existente se exige retener también: digests base y final, ID/arquitectura, UID **y GID** efectivos 1654; lectura de assembly, `.deps.json`, `.runtimeconfig.json` y recorrido de directorios en ambas imágenes. **Ajuste al contrato real:** `appsettings.json` es obligatorio en API; Migrator/Program.cs:11 usa exclusivamente variables de entorno y su csproj no publica ese archivo. Exigirlo al migrator, como hace el ejemplo anterior del checklist, produciría un falso fallo; la propuesta nueva comprueba los archivos realmente requeridos sin ejecutarlo ni alterar producción. Ensayar variante restrictiva 600/700 sin chown que oculte un error. Montaje de secreto **sintético**, sin Firebase ni valores reales: demostrar lectura con owner/grupo previstos, sólo lectura y denegación con permisos ajenos; no imprimir contenido. CodecSmoke con `--require-arm64` debe ejecutarse contra la **misma imagen final por ID inmutable**, red none y API apagada. Windows x64 no sustituye ese resultado. Entregar metadata/log sanitizado/digest y firma del revisor; sin ello S9-C permanece BLOQUEADO.

### Propuesta autorizada: GitHub Actions ARM64 efímero

Archivo ya publicado en b1ae17, con parche de provenance local pendiente: `.github/workflows/agent-runtime-arm64-certification.yml`. GitHub documenta [`ubuntu-24.04-arm` para repositorios públicos](https://docs.github.com/en/actions/how-tos/write-workflows/choose-where-workflows-run/choose-the-runner-for-a-job); la disponibilidad/cuota efectiva sigue pendiente de un run. No se contrataron runners ni servicios.

- Sólo `workflow_dispatch`, permiso `contents: read`, timeout 35 min, checkout sin credenciales persistidas y sin secrets/environments de producción. Input `release_sha` de 40 hex debe coincidir con el SHA del ref que ejecuta el workflow. No permite certificar fuentes de otro commit con el workflow de una revisión distinta.
- Runner Linux ARM64 nativo, repositorio público, checkout limpio y contexto allowlist versionado. Copia fuentes de los cinco proyectos y archivos de build necesarios; excluye .env, firebase.json, bin/obj, cuentas y archivos ajenos. Configuración pública con campos de credenciales no vacíos detiene el gate sin imprimir valores. Contexto temporal con umask 077.
- Construye Dockerfile existente, targets `final` y `migrator`, `linux/arm64`, **sólo --load local**; nunca login/push/deploy. Registra SHA/OCI label, ID, manifest digest de build, arquitectura/USER y materiales/digests base de [metadata Buildx](https://docs.docker.com/reference/cli/docker/buildx/build/#write-build-result-metadata-to-a-file---metadata-file).
- Pruebas con red none, read-only, UID/GID 1654 y entrypoint shell explícito; verificación de ownership/lectura de archivos, variante restrictiva de modos sin chown y bind mounts sintéticos permitidos/denegados. Ni API, workers ni migrator arrancan.
- Extrae sólo SkiaSharp.dll de contenedor **detenido**, compila el harness existente y monta exclusivamente su DLL IL contra deps/runtimeconfig/nativos de la misma imagen API. Exige exit 0 y tres PASS PNG/JPEG/WebP con `--require-arm64`.
- Artifact `agent-runtime-arm64-evidence` por 7 días: `identity.json` con SHA/Run ID/attempt/job/outcomes, `context-manifest.json`, `images.json`, hashes y logs sintéticos. Fallos se propagan, `always()` sólo conserva evidencia; no hay continue-on-error.

**Validación local inicial conservada:** cuatro scripts Python y sintaxis shell aprobados antes de publicar b1ae17. **Validación nueva:** YAML/PyYAML, cinco scripts Python (incluye paso de tests del parser), sintaxis del test nuevo y `bash -n` del bloque POSIX de permisos: PASS. Detalle del parser en §2-A. No se ejecutaron workflow/Docker ni se simularon resultados ARM64. Revisión independiente y ejecución remota PENDIENTES.

**Límite de procedencia:** no se publica ni sube un archivo de imagen; las imágenes locales desaparecen con el runner. `build_manifest_digest` e image ID no son un RepoDigest de registro ni un artefacto de despliegue retenido. Incluso con este smoke verde, antes de producción faltará una operación de custodia/publicación expresamente aprobada y correlacionar el digest exacto que se vaya a desplegar; reconstruir un tag no conserva automáticamente la certificación.

Acción manual posterior a revisión/publicación: seleccionar el ref del nuevo SHA en **Agent Runtime isolated ARM64 certification**, introducir ese mismo SHA completo y aportar Run ID/attempt + artifact. No se hizo dispatch. **Q01 ya fue aportado por el propietario y se registra PASS en §4; no se vuelve a solicitar.**

## 2-A. Auditoría y parche de provenance en esta reanudación

**Causa delimitada:** no se observó una ejecución ARM64 fallida de NexFlow. La ruta directa `buildx.build.provenance.materials` **sí es válida** para metadata Buildx; no se declara defectuosa por confundirla con una atestación completa. Fuente primaria fijada: [Buildx encodeProvenance](https://github.com/docker/buildx/blob/a512c8af77892e419c369ea4205dfab548edc5ec/build/provenance.go#L128) normaliza el registro SLSA; [su prueba de metadata](https://github.com/docker/buildx/blob/a512c8af77892e419c369ea4205dfab548edc5ec/tests/build.go#L1177) ejerce exporter Docker con metadata incluso en modo max. Esto respalda disponibilidad como contrato upstream de `--load`, **no demuestra la versión ni el JSON de nuestro runner aún no ejecutado**. Persistencia de una atestación dentro del image store y metadata de build son cosas distintas.

La [documentación SLSA de Docker](https://docs.docker.com/build/metadata/attestations/slsa-provenance/) presenta envelopes `predicate.materials`; v1 usa `predicate.buildDefinition.resolvedDependencies`. Son estructuras representadas **sintéticamente**, sin presentarlas como metadata capturada de un run nuestro. El parser anterior rechazaba esos envelopes. Además, su comprobación parcial de URI y digest no vacío **aceptaba una referencia de registro parecida y un SHA256 inválido**: defecto reproducido del gate, sin fallo productivo demostrado.

Reproducción aislada ejecutando exactamente las tres instrucciones antiguas extraídas del workflow: **4 pruebas, 1 control aprobado, 3 fallidas, 0 errores/omisiones**, `provenance-before.log`. Direct v0.2 funciona; wrapper legítimo falla; digest inválido y registro lookalike se aceptan indebidamente.

Parche mínimo, sin otro selector de negocio ni código productivo:

- Función pura dentro del workflow; reconoce el registro directo, envelope SLSA v0.2/v1 y clave Buildx específica `linux/arm64`, sin buscar recursivamente hechos en campos arbitrarios.
- Exige **ambas** bases `mcr.microsoft.com/dotnet/sdk` y `mcr.microsoft.com/dotnet/aspnet` por identidad exacta y SHA256 hexadecimal de 64 caracteres. Rechaza ausencia, esquema/plataforma ambiguos, campos malformados, otro platform y duplicados con digests contradictorios; no hay fallback a tags o `docker history`.
- Proyecta sólo las dos bases verificadas a `images.json`. Paso Python de regresión antes del build y su outcome obligatorio en `identity.json`; fallos permanecen rojos y sus logs se conservan.

Regresión nueva `.github/workflows/test_arm64_provenance.py`: **20 ejecutadas/aprobadas, 0 fallidas/omitidas**, fixtures públicos sintéticos en el propio test. Carga sólo la función delimitada, nunca los comandos Docker ni la configuración de la aplicación. Regresión Python de CI (incluye las siete G8 previas): **27/27**, 0 fallidas/omitidas. No se cambia ningún corpus, test C# ni aserción DST.

```powershell
python -B -m unittest discover -s .github/workflows -p test_arm64_provenance.py -v
python -B -m unittest discover -s .github/workflows -p 'test_*.py' -v
git diff --check
```

Evidencia nueva ignorada: `backend/NexFlow.Tests/TestResults/mvp-final-gates/{provenance-before.log,provenance-after.log,ci-python-regression.log,workflow-static.json}`. No se repiten builds/backend/frontend intactos; sus PASS históricos permanecen atribuidos a su árbol original. Nuevo SHA y cuatro runs son obligatorios después de publicar el parche. Identidad local de preparación en `candidate-freeze.json`: huella de archivos, **no un commit publicado ni un PASS CI**.

Propuesta local congelada para revisión sobre b1ae17: huella SHA256 normalizada de los dos blobs CI `ed6668c9a10c93df416bd7ffe8c91877aaf50d537553ba33c4490732d7b544b4`. Algoritmo y blob IDs constan en `candidate-freeze.json`; diff de los tres archivos propios en `review.patch`. No se cambió de rama ni se creó un SHA de commit. Si el propietario modifica esos archivos antes de publicar, debe revisar otra vez el diff; la identidad de certificación será únicamente el commit publicado que resulte, no esta huella.

## 2-B. Historial de custodia previo a la propuesta actual

El workflow de cedd194 sólo conservaba metadata/logs; `image_retained=false` fue correcto para ese run. **No se podrá recuperar su imagen de un runner ya destruido**, ni usar un rebuild Oracle como si fuera la misma imagen ensayada. La propuesta actual autorizada se describe en §2-C; conservará la imagen **en la misma ejecución que la verifica**. No se ejecutó publicación por inferencia; el nuevo workflow necesita revisión, nuevo SHA y evidencia propia.

Procedimiento de exportación propuesto para el job efímero, tras permisos/codecs PASS y autorización de custodia (no ejecutado):

```bash
# API_IMAGE_ID viene de images.json de ESE job, no de un tag reconstruido.
# Todas las rutas pertenecen al directorio de custodia aprobado.
docker image save --output "$APPROVED_DIR/nexflow-api.tar" "$API_IMAGE_ID"
sha256sum "$APPROVED_DIR/nexflow-api.tar" > "$APPROVED_DIR/nexflow-api.tar.sha256"
```

Guardar manifest firmado/revisado con RELEASE_SHA, Run ID/attempt, bases, plataforma, image ID, digest manifest Buildx, hash SHA256 **del archivo tar** y hashes de evidencias. Repetir conservación del migrator por su ID si entra en alcance autorizado, sin ejecutarlo. Custodiar el tar fuera de artifacts públicos salvo autorización específica; no contiene credenciales porque el contexto las excluye, pero eso no autoriza su distribución. No hubo save/login/push/SSH/import en este encargo.

Después de permiso separado de transferencia y **antes de cualquier activación**, operador verifica hash recibido y manifest, carga únicamente ese archivo autorizado, y comprueba image ID/label SHA/arquitectura iguales a lo ensayado. No iniciar API ni migrator como importación. Image ID identifica config local; build manifest digest identifica el manifest de build; SHA256 del tar identifica bytes de transporte; RepoDigest sólo existe tras publicación aprobada en registro. Ninguno se inventa a partir de otro. Para promoción por registro, publicar la imagen ya ensayada y comprobar su digest en destino, nunca sustituirla con un rebuild. Custodia/destino/digests finales: **BLOQUEADOS**, aún sin autorización ni artefacto binario.

## 2-C. Artefacto desplegable — propuesta actual y autorización pendiente

**Objetivo técnico preparado:** tomar `images.json.api.id`, después de todos los PASS existentes, y ejecutar `docker image save --output <archivo> <IMAGE_ID>`. No se invoca build ni se usa un tag para decidir qué exportar. El helper CI nuevo `.github/workflows/arm64_image_custody.py` verifica previamente permisos de ambos targets, codecs contra ese mismo ID, plataforma/usuario/label y fuente. No ejecuta Docker al importarse ni durante sus tests.

El diseño de custodia revisable es:

1. Inputs opt-in, desactivados por defecto; confirmación explícita del propietario de cuota y controles de gasto. Presupuesto de payload 2–512 MiB, por defecto 0. Si no hay presupuesto/cuota confirmados, STOP antes de generar o subir un binario. No se consulta billing ni se activa sobreconsumo.
2. Archivo Docker save **tar**, con límite Linux de tamaño durante la escritura (`RLIMIT_FSIZE`), reserva de 1 MiB para manifest/checksums/overhead y comprobación de disco libre. Se rechaza también una imagen desempaquetada mayor al presupuesto antes de copiar/guardar. Tras save, SHA256 streaming y tamaño real. Manifest: SHA fuente, Run ID/attempt, Image ID, build manifest digest, bytes/hash del tar, plataforma, UID/GID, revisión OCI y hashes de la evidencia de smoke. RepoDigest no se inventa. El hash de un ZIP de GitHub no sustituye al hash de este tar.
3. Propuesta de destino: artifact GitHub `nexflow-api-arm64-<SHA>-<attempt>`, **un día**, sin overwrite, sin caché/registro/Release; sólo tar, manifest y SHA256SUMS. Los logs/metadata existentes conservan su retención de siete días. El binario no se mezclaría en el artifact de diagnóstico actual. Se registraría el Artifact ID/digest real devuelto por GitHub.
4. Importación en **otro job `ubuntu-24.04-arm`, VM/daemon independientes**: descargar únicamente el Artifact ID de ese run; verificar hashes contra outputs independientes del job exportador, fuente y attempt, tamaño y nombre fijo antes de load; comprobar que el Image ID **no existe** en ese daemon. Después `docker image load` e inspect: mismo ID, linux/arm64, USER 1654 y label del RELEASE_SHA. Contenedor shell sintético, red none/read-only/caps drop y pull never, comprueba UID:GID 1654:1654 y lectura de appsettings/DLL/deps/runtimeconfig. Nunca arranca el entrypoint API ni migrator.
5. Resultado separado `import-verification.json`, con mismo ID/hash/SHA, `image_absent_before_load=true` y PASS real. Un job omitido/fallido o sólo una exportación no basta para aprobar custodia. Rerun completo si cambia attempt, sin mezclar el archive anterior. Sin reconstrucción, registry push ni transferencia a Oracle.

**Autorización y revisión automática:** el primer parche fue rechazado por la revisión automática por falta de autorización explícita del destino público. No se eludió el rechazo ni se subió nada. El propietario respondió después: «Autorizo preparar localmente la propuesta de GitHub Actions para custodiar temporalmente la imagen ARM64 certificada de NexFlow», con condiciones de imagen exacta, ausencia de secretos, opt-in, ≤512 MiB/un día, cuota/costo comprobados, SHA256 e importación aislada. Con esa autorización nueva se aplicó la propuesta de wiring, sin commit/push/dispatch/upload real ni cambios Oracle. La autorización permite **preparar y revisar código local**, no gastar, ejecutar el workflow ni distribuir ahora el tar.

El workflow está conectado así: preflight de inputs antes del build; export sólo después de todos los PASS y con `retain_api_image=true`; upload sólo después de export exit 0; segundo job `verify_archive` sólo si el certificador terminó success y se pidió custodia. Descarga por **Artifact ID**, no por búsqueda de nombres; hashes esperados llegan por outputs del exportador. `identity.json` conserva la certificación y marca custodia `PENDING_ISOLATED_IMPORT`; sólo el resultado del segundo job puede acreditar importación. Si se solicitó custodia y falta un Artifact ID válido, falla el job. Con opt-in falso, comportamiento de certificación existente preservado, sin archive/upload/import job.

**Barrera de privacidad implementada antes de save/upload:** comprobar env de la imagen contra nombres públicos permitidos; copiar sólo `/app` desde contenedor detenido y sin red, nunca ejecutar su entrypoint; rechazar symlinks, .env, rutas de credenciales/service accounts, certificados privados y configuraciones inesperadas. JSON limitado a los archivos públicos/generados conocidos y campos de credenciales vacíos. `appsettings.json` debe tener exactamente el hash de la fuente pública revisada en context-manifest; `web.config` sólo admite el formato generado, sin env privado ni argumentos extra. Una detección o resultado dudoso falla, no sube el tar y exige revisión privada. Sólo conteos/hash/PASS entran en `custody-safety.json`; no se imprimen valores ni se suben archivos del directorio de inspección. Esta defensa usa además la allowlist existente y bases públicas por digest; **no equivale a demostrar universalmente ausencia de secretos incrustados en cualquier binario**. El resultado de la inspección de la imagen real permanece PENDIENTE hasta ejecutar CI.

El código fuente permitido no contiene `.env`, service accounts ni configuración operacional; se conservan la allowlist y el rechazo de credenciales del workflow aprobado. Aun así, **ausencia de secretos no equivale a permiso de distribuir el binario**. Un lector autorizado del repositorio público podría descargar artifacts; no se promete confidencialidad. [GitHub documenta retención por artifact](https://docs.github.com/actions/configuring-and-managing-workflows/persisting-workflow-data-using-artifacts) y [almacenamiento compartido/medición y presupuestos](https://docs.github.com/en/billing/concepts/product-billing/github-actions): un día y un cap por archivo **no prueban cuota libre ni garantizan gasto cero**. Cuota real y controles de sobreconsumo deberán comprobarse por el propietario antes de un dispatch. No se han consultado ni cambiado.

**Alternativa si GitHub no es adecuado por cuota/tamaño/confidencialidad:** depósito privado de objetos **ya existente**, aprobado por el propietario, con cifrado, acceso limitado y versionado/retención inmutable. Clave de objeto basada en SHA fuente + hash tar; verificación de bytes tras guardar/descargar, manifest con ID/versión inmutable, retención y responsable de custodia. Mantener inicialmente el mismo cap 512 MiB y un día; ampliar tamaño o plazo sólo mediante nueva autorización de capacidad/costo. Debe integrarse **en la misma ejecución antes de destruir el runner**, y la prueba de importación debe descargar esa versión exacta. Sin destino/cuota/credenciales efímeras/condiciones de pago autorizados: BLOQUEADO, no crear buckets, cuentas o servicios ni copiar a Oracle. No registrar claves ni enlaces firmados en artifacts públicos.

Validaciones nuevas reales: **41 pruebas Python, 41 PASS, 0 fallidas/omitidas** (20 provenance + 14 custodia/privacidad + 7 G8). Las nuevas cubren default deshabilitado, aprobación/cuota/presupuesto, hash de bytes reales sintéticos, identidad/revisión, plataforma/usuario, ruta/hash/tamaño, rechazo de env/credenciales/JSON inesperados, config pública por hash y web.config sin env privado. No mockean ni simulan Docker. Primer intento sandbox tuvo un error de permisos Windows al limpiar el temporal; se conserva `windows-temp-error.log`; la repetición autorizada pasó sin cambiar aserciones. YAML, ocho scripts inline Python, helper/tests y dos shells con `bash -n`: **PASS estático**. Exportación, inspección de imagen real, importación, archivo binario y tamaño/hash reales **NO EJECUTADOS / PENDIENTES**.

Evidencia local: `backend/NexFlow.Tests/TestResults/arm64-custody/`. No se repiten builds ni pruebas backend/frontend: producto intacto. La validación estática y revisión de diff corresponden sólo a esta propuesta local, no a una nueva certificación remota.

Diff exacto de los cuatro archivos: `custody-review.patch` en ese directorio. `local-evidence.json` registra el SHA base, fingerprints de archivos y resultados locales; archive/hash/tamaño reales quedan nulos hasta CI. Los cambios no se han añadido al índice ni publicado.

**Procedimiento para obtener el artefacto, todavía condicionado:** auditar este diff; verificar cuota/costo del propietario; aprobar/publicar manualmente un SHA nuevo y autorizar por separado el dispatch. En el workflow ARM64 seleccionar ese ref, `release_sha` completo igual al ref, `retain_api_image=true`, `storage_quota_confirmed=true` **sólo después de comprobar cuota y controles**, y `artifact_budget_mib` con el presupuesto disponible real (2–512, no asumir 512 libres). Inputs default false/false/0 no exportan ni almacenan imagen. Exigir certificador **y verify_archive** PASS; conservar artifact binario, artifact de certificación y artifact separado de importación. Custodio descarga **antes del día de caducidad**, verifica manifest/hash contra la evidencia del run y registra recepción en el destino aprobado. No hay dispatch autorizado aún. No hacer Docker import en Windows ni Oracle como parte de esta tarea. Si supera tamaño/cuota, detecta datos sensibles, caducó sin custodia o falta prueba aislada: STOP, custodia privada aprobada o nueva decisión; no elevar el cap ni reconstruir a ciegas.

Archivos esperados tras una ejecución válida: `nexflow-api.tar`, `custody-manifest.json`, `SHA256SUMS`; artifact de importación con `import-verification.json`. Verificar `sha256sum -c SHA256SUMS` en custodio Linux autorizado, o `Get-FileHash -Algorithm SHA256 -LiteralPath .\nexflow-api.tar` en Windows **sin Docker**, comparando con el manifest y outputs del run. Registrar además el Artifact ID/digest ZIP del certificador. La prueba de importación debe corresponder a ese mismo SHA/run/attempt/hash/ID y no estar omitida. Conservar la recepción antes de caducidad; un archivo sin esos vínculos no es desplegable. Ninguno de esos nombres implica que exista ya el binario.

## 3. Matriz de gates consecutivos

| Gate | Estado real | Evidencia / bloqueo | Responsable siguiente |
|---|---|---|---|
| S9-A identidad certificada | **PASS** | HEAD local y cuatro runs: cedd194 completo; árbol inicial limpio | Propietario / auditor independiente |
| S9-A nueva propuesta de custodia | **PASS local / PENDIENTE auditoría** | Destino de propuesta autorizado; helper/41 tests/estática y wiring preparados, no publicados | Propietario / auditor |
| S9-A parser/estática locales | **PASS** | Reproducción 1/4 → 20/20 parser; total Python CI 27/27; YAML + cinco scripts + shell | Codex / auditor |
| S9-B regresión producto local anterior | **PASS histórico, no CI del SHA nuevo** | 1538 backend, 83 TS, 148 browser; no repetidos porque producto no cambia | Auditor |
| S9-B cuatro CI mismo SHA | **PASS para cedd194** | Cuatro Run IDs/attempts y conteos arriba; auditoría independiente aportada | Auditor independiente |
| S9 ARM64 final+migrator | **PASS para cedd194** | 40406074161, permisos/codecs aprobados | Auditor independiente |
| S9-C custodia de imagen exacta | **BLOQUEADO ejecución** | Wiring autorizado preparado; faltan cuota/costo, revisión/publicación/dispatch y tar/importación reales | Propietario / custodio |
| S10-A Q01 inventario | **PASS, operador** | Imagen/ID/UID/GID/modos/Compose/13 archivos aportados; detalle §4 | Propietario / auditor |
| S10-A Q01 salud/RepoDigest; Q02–Q10 | **BLOQUEADO / NO EJECUTADO** | Inventario no acredita readiness, esquema, datos ni permisos efectivos | Operador autorizado + negocio |
| S10-B política fiscal y PDF visual | **BLOQUEADO** | Sin decisión TaxId ni template/PDF ficticio aportados | Producto/privacidad |
| S10-C backup/restore Firestore/objetos | **BLOQUEADO** | Herramienta lógica no autorizada/implementada/probada; emulador no iniciado | Responsable datos + propietario |
| S11-A corpus independiente/humanos | **BLOQUEADO** | No corpus privado ≥32 ni ejecutor/revisores aportados | Evaluador externo |
| S11-A Groq/Gemini | **NO EJECUTADO** | Sin modelos/presupuesto/límites/autorización | Propietario + QA |
| S11-B WhatsApp/panel E2E | **NO EJECUTADO** | Sin entorno/número/participantes/aislamiento autorizados | Operador + negocio |
| S11-C preparación/corte | **BLOQUEADO** | Q01 inventario recibido; faltan config efectiva, quietud, recuperación y GO SHA/digest/ventana | Operador + propietario |

## 4. S10 — datos, PDF y recuperación

Se conserva el procedimiento parametrizado existente en [MATRIZ_Q01_Q10_VALIDACION_MVP.md](MATRIZ_Q01_Q10_VALIDACION_MVP.md). **Codex no ejecutó consultas sobre Oracle/Firestore real; Q01 inventario fue ejecutado por el operador y aportado por el propietario. Q02–Q10 no ejecutados.** No se tomaron backups ni se aplicaron migraciones, ni siquiera para improvisar un entorno local.

### Q01 inventario PASS — evidencia recibida, no repetir solicitud

Fuente: `docs/NexFlow_Auditoria_Candidata_Final_2026-10-26.md`, ratificada por el prompt operativo del propietario, ambos leídos completos el 26/10/2026. Responsable de la observación: operador autorizado/propietario; Codex sólo registra. Instante exacto del inspect y Run ID no se aportaron (Run ID no aplica a esta lectura de operador).

| Campo aportado | Valor observado |
|---|---|
| Contenedor / estado / reinicios | `nexflow_api` / running / 0 |
| Imagen | `nexflow-api:306d7c8-perms1` |
| Image ID | `sha256:e63453197ee666cfb023ae07b33a95fdc37711cf914c0dd0b13a5c51fff0ce45` |
| Plataforma / UID:GID | linux/arm64 / 1654:1654 |
| `/app` | root:1654, 755 |
| `/app/appsettings.json` | root:1654, 640 |
| Compose proyecto / servicio / directorio | `nexflow` / `api` / `/opt/saas/nexflow` |
| Archivos Compose / último override | **13** / `api-306d7c8-perms.override.yml` |

No se vuelve a pedir Q01. **No confundir** ese image ID con RepoDigest o con el SHA candidato. Readiness actual, digest de registro, esquema/EF, config efectiva de los 13 archivos y rollback compatible no quedaron acreditados por ese inventario. Son gates operativos distintos pendientes; no autorizan acceder, alterar, reiniciar ni desplegar en Oracle.

| Q | Dato todavía no observado | Acción mínima autorizable |
|---|---|---|
| Q01 | **Inventario ya observado PASS**; salud/RepoDigest/esquema pendientes por separado | Conservar evidencia anterior; no repetir solicitud ni inferir salud/rollback |
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

Preparación local adicional, sin ejecutar: antes de SQL A/B/C del documento Q, el operador enlazará `@schema='public'` y `@tables` como array de tablas permitidas (`Workspaces`, `Licenses`, `LicenseModules`, `Modules`, `Reservations`, `OutboxMessages`, `InboundMessages`). Dentro de su sesión READ ONLY autorizada, timeout 5 s:

```sql
SELECT table_name, column_name, data_type, is_nullable
FROM information_schema.columns
WHERE table_schema=@schema AND table_name=ANY(@tables)
ORDER BY table_name, ordinal_position
LIMIT 128;
```

La consulta no expone filas de negocio. Si devuelve 128, faltan columnas, tipos/enums difieren del modelo o no hay privilegios suficientes: STOP, no ejecutar los ejemplos de negocio ni DDL. Q01 no contiene esquema SQL observado; las consultas se preparan contra el modelo local y **no se afirman certificadas sobre Oracle**. Para Q03–Q07/Q09, SDK servidor sólo tras aprobar W-DEMO, rol de lectura y ventanas/límites de la matriz; ninguna credencial ni proyecto real se consulta aquí.

**Cinco/cuatro:** los fixtures demuestran filtros diferentes legítimos, no la razón del incidente real. Publicado exige tipo/activo/categoría; reservable por sede añade flag de reserva, duración válida y scope. El quinto puede no ser reservable, no tener duración o pertenecer a otra sede; `Origin`/`SourceHash`/edad del PDF también deben comprobarse. Sin Q03/Q04 no se elige arbitrariamente una de esas causas ni se cambian datos para cuadrar cantidades.

**Fiscal:** `CatalogGenerationService.GetContentAsync` transmite TaxId, pero no prueba que la plantilla lo imprima. No se cambia payload/hash por suposición. Pendiente decisión expresa de minimización/whitelist y revisión visual ficticia; cualquier parche de contrato requiere nueva candidata, regresiones de hash/CURRENT/STALE/callback/HMAC/GenerationId y S9 repetido.

**Recuperación:** el backup SQL histórico restaurado no acredita recuperación Firestore/PDF externos. Spark es información reportada, no se consultó billing. No se activó Blaze ni exportación administrada. Falta autorizar alcance/cuotas/destino cifrado/custodia/RPO/RTO/retención e implementación de copia lógica por SDK. Primero ensayo sintético en emulador loopback sin credenciales, API/workers/canal apagados: roundtrip tipado completo de subcolecciones/padres ausentes, IDs/versiones/CAS/dedupe/UnknownDelivery/terminales, scopes y coherencia SQL/objetos. No hay resultado de restore, RPO/RTO medidos ni manifest privado de respaldo nuevo. Sin quietud entre almacenes no se afirma snapshot consistente. **Datos no descartables y ventas bloqueados**; la posible excepción DEMO descartable exige aprobación separada y no elimina los demás gates.

## 5. S11 — evaluación y operación real

No se creó corpus privado en el repositorio ni se mostraron expectativas reservadas al implementador. Falta evaluador distinto con **32 conversaciones multitur inéditas** W-A clínica/W-B comercio/W-C mixto/W-D limitado, corpus/hash congelados fuera de repo, dos revisores humanos y auditor de invariantes. Rúbrica previa a resultados: cero fallos de seguridad/tenant/consentimiento/duplicado, propuesta ≥90% tareas resueltas/derivadas legítimamente y ≥4/5 naturalidad/claridad/utilidad, pendiente ratificación humana. No hay scores observados ni certificación ciega.

ProviderIntegration: dos tests descubiertos; **0 ejecutados, 0 llamadas, costo observado no medido**. El harness directo tiene ocho muestras por proveedor y no equivale al runtime/AiRouter completo. Antes de ejecutarlo: modelos, tarifas, máximo POST total con reintentos/fallback, tokens, costo en moneda, ventana/cuotas y responsable de parada firmados; demostrar límite efectivo antes de usar claves. No se leyeron secretos ni se establecieron flags de autorización.

Verificación estática nueva del harness: `ProviderCertificationTests.cs:39–55` construye HttpClient directo y ocho casos por proveedor, con mock IA router; `AiInterpreter.cs:167` hace una llamada de interpretación por caso; `AiTelemetry.cs:32` no reintenta. Groq (`GroqAiProvider.cs:48–63`) limita salida a 1024 y hace un PostAsync. Gemini usa salida 1024 en JSON mode (`GeminiAiProvider.cs:98–102`) y el fallback de :111–115 está deshabilitado en esta configuración sintética porque el test no configura FallbackModel. **Son hasta 16 invocaciones lógicas PostAsync en una ejecución única de ambos tests**, no dos llamadas ni una certificación de AiRouter. No es un límite monetario impuesto ni garantía del número de transportes subyacentes: HttpClientHandler conserva redirecciones predeterminadas, el test observa consumo pero no detiene por presupuesto y no limita tokens de entrada por costo. No ejecutar hasta fijar/controlar esos límites, modelos, tarifas, ventana y autorización. No se añade una política nueva al proveedor estable.

WhatsApp/panel: **0 ensayos reales**. Pendientes número y participantes autorizados, DEMO descartable aislado, método real de admisión en API única, roles efectivos, umbrales p50/p95/backlog/error/costo ratificados y recuperación. Después de autorización separada, seguir [PLAN_PILOTO_E2E_WHATSAPP_MVP.md](PLAN_PILOTO_E2E_WHATSAPP_MVP.md): observar canal + persistencia única/outbox/panel por W, reserva con resumen/consentimiento y pedido PendingReview sin pago/stock garantizado, interrupciones, privacidad/handoff, revocación y replay. Sent no se contabiliza como Delivered/Read. Latencia/tokens/costo reales siguen **NO MEDIDOS**.

## 6. Corte: procedimiento condicionado, sin comando de producción ejecutable aún

El operador ya inspeccionó el inventario Q01: API_CONTAINER=`nexflow_api`, API_SERVICE=`api`, project=`nexflow`, directorio=`/opt/saas/nexflow`, **13 archivos** y último override conocido. No faltan esas observaciones ni se vuelven a pedir. Config efectiva/equivalencia de esos archivos, orden/rutas completos necesarios para construir los argumentos, env-file paths, digest de imagen candidata retenida, quietud y ventana siguen **PENDIENTES**; la evidencia recibida no los contiene ni los sustituye. Por tanto, aún no existe un comando completo seguro de corte. No inventar nombres para los otros doce archivos ni copiar un Compose histórico.

Secuencia única restante, con operador autorizado:

1. Partir de **Q01 inventario ya recibido**, sin solicitarlo otra vez. Tras autorización operativa separada, completar equivalencia de config/runtime Production/esquema EF y salud de dependencias; sin env/inspect completo, DDL, QR, desconexión, workflow ni conversión como health check.
2. Validar imagen ARM64 retenida por digest y preparar override **únicamente services.<API_SERVICE>.image**. Aplicar comparación Compose efectiva en memoria del checklist §5; cualquier otra diferencia = STOP. No imprimir/guardar config interpolada.
3. Acreditar mecanismo real de cierre de admisión y todos los escritores. Quietud global de cinco minutos, muestras cada 30 s: inbound accionable=0, Outbox pendiente/processing=0, outbound pending/attempting=0, PDF activo=0. Baseline de terminales/DeadLetter/UnknownDelivery preservado. Ningún DELETE/UPDATE/requeue para conseguir cero. Sin barrera o inventario completo = STOP.
4. Backup/restore aislado aprobado, imagen anterior retenida y rollback **demostrado compatible** o contención/fix-forward aprobado. Ni `0da524…` ni `306d7c8-perms1` son rollback autorizado por defecto; no convivir con escritores inseguros.
5. Propietario firma GO separado con **SHA completo, digest y ventana exacta**, impacto, thresholds y recovery. Sólo entonces podrá ejecutarse el comando del checklist §7, con arrays/servicio observados: `docker compose "${CANDIDATE_ARGS[@]}" up -d --no-deps --no-build --pull never "$API_SERVICE"`. Esta línea es una **plantilla no ejecutada**, no aprobación. Nada de down/prune/pull/rebuild/dependencias/migrator.
6. Observar digest efectivo/startup/liveness/readiness y transacciones DEMO durables/canal/panel; ante fuga/duplicado/falta de consentimiento/F7-01/bot tras takeover/entrega incierta reenviada, STOP de admisión y plan compatible aprobado. Reapertura/comercialización requieren decisiones distintas. No hubo post-corte ni health productivo en esta intervención.

## 7. Dictamen y continuación

| Decisión | Resultado | Motivo comprobable |
|---|---|---|
| GO revisión local de custodia | **GO** | Propuesta autorizada, 41 pruebas Python y validación estática verdes; sin afirmación de Docker ejecutado |
| GO para ejecutar custodia | **BLOQUEADO** | Faltan revisión del diff, cuota/costo comprobados y autorizaciones separadas de publicación/dispatch |
| GO_CANDIDATE / certificación externa de cedd194 | **GO técnico acotado** | Cuatro runs aprobados; no significa custodia/producción/comercialización |
| GO_DEMO_PILOT | **NO-GO / BLOQUEADO** | Falta evaluación independiente, permisos/presupuesto y aislamiento/canal DEMO |
| GO_CODE_DEPLOY | **NO-GO / BLOQUEADO** | Falta imagen retenida/importada, config/quietud/recuperación y autorización SHA/digest/ventana |
| GO_COMMERCIAL | **NO-GO / BLOQUEADO** | Sin recuperación Firestore/objetos demostrada, política PDF y E2E/humanos/proveedores reales |

No hay cambios productivos en esta tarea. Provenance y DST forman parte de cedd194 ya certificado. Lo pendiente ahora es imagen exacta sin custodia/importación real y autorización de almacenamiento; además continúan los gates de recuperación durable, privacidad fiscal, evaluación/canal, aislamiento/admisión y rollback. Las doce incompatibilidades históricas se conservan separadas, no se convierten en PASS ni en autorización comercial.

**Próxima acción mínima:** auditar el diff local de custodia en §2-C; el destino de la propuesta ya fue autorizado. Confirmar cuota/costo y, sólo mediante instrucciones separadas, publicar el SHA aprobado y ejecutar CI opt-in. **No repetir la auditoría de los cuatro runs ya aprobados de cedd194**; el workflow modificado no hereda sus resultados para otro SHA. Imagen/tar/hash/size y load real se completarán con el nuevo run, no con valores ficticios. Q01 continúa PASS, sin nueva solicitud. No se publica desde Codex, no se usa Oracle como runner ni se realiza corte.
