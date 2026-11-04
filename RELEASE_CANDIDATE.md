# NexFlow LLM-first — candidata DEMO, NO desplegada

## Identidad y límites

Rama única: `feature/llm-first-agent`. HEAD local/origin/remoto **5d26b7c73e7ed46823bf49f45e3bbeecb5b9bdc5**; main **5f5ff850e31df3fa73cb2c7f02f65ba6081173c7** intacta. La candidata es **5d26b7+guardia de reinicio local sin commit**. El singleton ya estaba publicado; ahora se añade identidad independiente del agente y preflight antes de workers. No hay SHA nuevo, imagen/digest ni CI certificado del parche. REST de Actions:0 runs para5d26b7. Evidencia nueva: `backend/TestResults/LlmFirstDurableGuard_20261103/`; resultados anteriores preservados, no transferidos al nuevo código. Dictamen vigente: `docs/auditorias/llm-first-agent/CIERRE_GUARDIA_REINICIO_Y_RELEASE.md`.

La rama publicada está5 commits adelante de main. Inicio rastreado/índice limpio; sólo la nueva auditoría del propietario untracked, preservada. Los dos archivos de evaluación están **publicados y EXPOSED**, confirmados por nombres del árbol Git sin abrirlos; NO son privados ni benchmark ciego. Propietario debe preservar copias privadas verificadas y aprobar retirada del árbol en commit normal, sin reescribir historia. Script de preparación: `scripts/llm-first/Backup-ExposedEvaluation.ps1`; no ejecutado sobre los originales, que siguen intactos. `.gitignore` no retira archivos ya rastreados. Seleccionar explícitamente los cambios, no staging masivo. No cambian frontend, Dockerfile, Compose, appsettings, migraciones ni corpus/evaluadores históricos.

Al aprobar la revisión, el propietario publicará por separado el commit final. Antes de cualquier CI/build: `git branch --show-current`, `git rev-parse HEAD`, `git status --short`, comprobar SHA remoto y exigir árbol aprobado limpio. Fijar `RELEASE_SHA` completo; no usar main/latest ni reconstruir después una imagen que se presente como la ya certificada.

## Cuatro gates independientes

Resultados actuales y TRX: `docs/auditorias/llm-first-agent/CIERRE_GUARDIA_REINICIO_Y_RELEASE.md`. CIERRE_FINAL y CERTIFICACION_PRE_DEMO se conservan como historia.

- OFFLINE_GO: **GO sólo del árbol local parcheado**. Build0 errores/0 advertencias incremental; vigente2154/2154; histórico130/101/29 exacto; dirigidos95/95; Python33/33; diff checkPASS. YAML4/4 y11 segmentos Python válidos. Dos tests rojos de reinicio y cuatro del runner reproducidos antes de corregir. No equivale a CI, emulador, proveedor, atestación externa ni calidad humana.
- PROVIDER_EVAL_GO: **NO_GO/WAITING_FOR_OWNER**. El conjunto publicado está expuesto. Falta NUEVO reemplazo privado independiente fuera del repo, candidata congelada, gates externos, modelos/cuotas/presupuestos y autorización del ensayo de una conversación. Runner rechaza corpus del repo/digest expuesto y exige atestación de corpus nuevo, ruta y hash externos. No se ejecuta proveedor ahora.
- DEMO_DEPLOY_GO: **NO_GO/WAITING_FOR_OWNER**. Falta SHA final publicado, CI/integraciones reales/ARM64 de ese SHA, custodia de imagen y autorización exacta de destino/ventana. La implementación local prepara esa revisión.
- COMMERCIAL_GO: **NO_GO**. P0 histórico no resuelto, calidad/proveedores/WhatsApp reales no certificados, privacidad/retención aún limitada a datos sintéticos.

## Configuración backend DEMO (sin archivos privados versionados)

Sólo mediante el mecanismo privado de configuración existente. Nombres reales:

```text
NexFlowHost__Role=Demo
AgentProtection__ManifestPath=<ruta absoluta local del montaje persistente read-only>
AgentProtection__ManifestSha256=<SHA256 de los bytes aprobados por el operador>
AgentDemo__WorkspaceId=<GUID del workspace sintético autorizado>
AgentDemo__InstanceName=<instancia Evolution exclusiva, ya asignada a ese workspace>
AgentDemo__TestPhones__0=<contacto internacional canónico de prueba autorizado>
AgentDemo__OperatorAuthorization=SYNTHETIC_DEMO_NO_COMMERCIAL_WRITES
AgentDemo__Enabled=false
AgentDialogue__Enabled=false
AgentShadow__Enabled=false
```

No copiar teléfonos, claves o configuración efectiva a este documento ni a Actions. TestPhones permite1–5 teléfonos canónicos; no admite un remitente arbitrario. No hay activación por mensaje, header o URL. La ruta DEMO no requiere AgentExperimentScope; sólo el dispatcher backend abre su scope diferente. Manifiesto privado independiente: JSON versión1 con DeploymentId y WorkspaceId GUID no vacíos, máximo4096 bytes. Custodiar fuera del repo, montaje persistente read-only legible por UID1654 y pin idéntico en todas las réplicas. No es nuevo estado comercial ni concede licencia.

El preflight síncrono aborta antes de workers/clientes si falta manifiesto/pin en un host Demo, hash/schema/identidad son inválidos o las opciones dinámicas discrepan. Con manifiesto válido y AgentDemo omitido/apagado, el tenant sigue protegido, sin diálogo/legacy ni escrituras comerciales. Un Legacy ordinario sin configuración DEMO conserva otros negocios. **La marca de host y el montaje/pin deben imponerse independientemente por el despliegue.** Si se eliminan TODAS esas fuentes y AgentDemo, el código no puede reconocer un antiguo DEMO entre hosts Legacy legítimos: mantener NO_GO hasta atestar esa garantía por réplica y ensayar reinicio/rollback aislados. No es una promesa de seguridad contra cambios arbitrarios del operador.

IOptionsMonitor conserva el kill switch y latch por deriva; no quitar identidad protegida para apagar. Env necesita reinicio autorizado sólo de API; HTTP ya iniciado no puede deshacerse. El kill switch no drena eventos n8n anteriores: exigir workspace sintético nuevo y colas comerciales vacías, sin escritores antiguos. Rollback a imagen sin guard exige cortar ingreso/consumidores DEMO antes de arrancarla; de lo contrario, no autorizarlo.

Tras cerrar los gates y recibir autorización del operador: habilitar exclusivamente AgentDemo__Enabled para el workspace sintético. Licencias reales de prueba, datos sintéticos y permisos siguen siendo obligatorios; el flag no concede módulos. La instancia debe pertenecer al mismo workspace y el webhook conservar la autenticación existente. No vincular un teléfono/instancia de cliente, ni abrir un túnel ni cambiar Evolution desde este procedimiento.

API en Production/Staging con contratos existentes de PostgreSQL/Redis/Firestore y proveedores: ConnectionStrings__DefaultConnection, ConnectionStrings__Redis, Firebase__ProjectId, GOOGLE_APPLICATION_CREDENTIALS y Gemini/Groq__ApiKey/Model privados. No hay nuevos secretos. No iniciar en Development: Program ejecuta migración/seeder en ese entorno. Production no inicia migrator. Ningún acceso a Oracle ni cambio de estos valores se ha ejecutado aquí. El destino, almacenes aislados y workspace no están autorizados por este archivo.

## Certificación remota manual del mismo SHA

Sólo después de autorización expresa de publicación y dispatch, propietario selecciona **feature/llm-first-agent**, el mismo SHA definitivo y conserva Run ID/run_attempt/head_sha, jobs, TRX e identity.json:

1. `.github/workflows/rc3-release-checks.yml`: backend vigente≥2154 sin fallos/omisiones, histórico exactamente130/101/29 con los mismos motivos/hashes; build y contratos frontend existentes. Release registra SHA/run en .g8.json, no en identity.json separado.
2. `.github/workflows/rc3-reservation-integration.yml`: PostgreSQL temporal + Firestore Emulator loopback. Cero fallos/omisiones; el nuevo validador exige una única definición exacta `NexFlow.Tests.Integration.AgentJournalEmulatorTests.Two_emulator_clients_enforce_one_owner_atomic_context_and_physical_tombstone` PASS, no sólo conteo≥37. TRX e identity.json obligatorios. Es una prueba con **dos clientes**, no dos procesos ni IAM de producción; NOT_RUN local. No requiere migración de journal; el workflow existente sólo aplica migraciones en PostgreSQL temporal autorizado.
3. `.github/workflows/concierge-redis-integration.yml`: Redis temporal loopback, sin datos/credenciales productivos, TRX e identidad verificables.
4. `.github/workflows/agent-runtime-arm64-certification.yml`: `release_sha=RELEASE_SHA`; targets API/migrator, UID/GID1654, permisos y CodecSmoke PNG/JPEG/WebP. No ejecuta migrator ni API. Retención de la imagen exacta es opt-in, exige cuota/coste aprobados; máximo512MiB y1día según workflow. Conservar manifiesto de fuentes, Image ID, etiqueta OCI, TAR/hash, prueba de importación y digest disponible. No copiar credenciales operativas al contexto.

No se ejecutó ninguno de estos workflows desde esta sesión. Los verdes de otra rama/SHA no certifican la candidata.

## ARM64 por SHA y custodia

Preferir el workflow oficial anterior: prepara un contexto versionado por allowlist, sin archivos locales privados, y ejecuta codec sobre la **misma Image ID**. Alternativa futura en builder ARM64 aislado autorizado, checkout limpio revisado y contexto sin secretos:

```bash
test "$(git rev-parse HEAD)" = "$RELEASE_SHA"
git diff --exit-code
git diff --cached --exit-code
test -z "$(git ls-files --others --exclude-standard)"
docker buildx build --platform linux/arm64 --target final --load \
  --label "org.opencontainers.image.revision=$RELEASE_SHA" \
  --metadata-file "$API_BUILD_METADATA" -f backend/Dockerfile \
  -t "nexflow-api:$RELEASE_SHA" backend
docker buildx build --platform linux/arm64 --target migrator --load \
  --label "org.opencontainers.image.revision=$RELEASE_SHA" \
  --metadata-file "$MIGRATOR_BUILD_METADATA" -f backend/Dockerfile \
  -t "nexflow-migrator:$RELEASE_SHA" backend
docker image inspect "nexflow-api:$RELEASE_SHA" \
  --format '{{.Id}} {{.Os}}/{{.Architecture}} {{.Config.User}} {{index .Config.Labels "org.opencontainers.image.revision"}}'
```

Registrar bases resueltas/digests, Image ID, usuario y smoke; construir migrator **no autoriza ejecutarlo**. No publicar imágenes ni transferir a Oracle desde este documento. Para distribución, usar la custodia/importación verificada existente; tag no sustituye Image ID/digest. Sin artefacto íntegro y correspondencia exacta de SHA: detener.

## Preflight y corte futuro de API únicamente

Sigue `NexFlow_RC3_DEPLOY_RUNBOOK.md`, contrastado con el servidor efectivo por operador autorizado. Ese runbook contiene referencias históricas, no identifica la imagen actual. Inventariar de forma privada contenedor/API, Image ID/digest anterior, proyecto/servicio Compose, todos los archivos/overrides en orden, redes, volúmenes y salud; no volcar ENV ni `docker compose config` expandido al informe. Preparar `COMPOSE_ARGS` array y `API_SERVICE` a partir de esas evidencias, no inventarlos.

Exigir ausencia de cambios de esquema y compatibilidad de rollback, colas comerciales del workspace DEMO vacías, ningún escritor antiguo para ese workspace, ninguna copia de datos de clientes y autorización separada de SHA/digest/destino/ventana. Drenar sólo trabajo API afectado antes del corte; nunca reprocesar DeadLetters, reenviar UnknownDelivery ni alterar estados históricos. Verificar previamente respaldo/restauración aislada conforme al runbook si el operador exige rollback de datos; una imagen no respalda Firestore. Este release no pide DDL ni migraciones.

Con imagen inmutable ya custodiada/importada, override privado revisado y GO separado, la operación prevista se limita al servicio verificado:

```bash
# API_SERVICE y COMPOSE_ARGS deben venir del inventario autorizado.
docker compose "${COMPOSE_ARGS[@]}" config --services
docker compose "${COMPOSE_ARGS[@]}" up -d --no-deps --no-build "$API_SERVICE"
```

No usar `down`, prune, borrar volúmenes, reiniciar PostgreSQL/Redis/Evolution/n8n/Gotenberg ni ejecutar migrator. La API aloja workers: confirmar que no quedan escritores antiguos del workspace DEMO, y no levantar una segunda API con el mismo consumo por accidente. No se ejecutaron estos comandos aquí.

## Salud, smoke sintético y rollback

Operador, tras autorización, verifica `/health/live` y `/health/ready` por el proxy HTTPS real: HTTP **y cuerpo**, dependencias y workers; Healthy no prueba WhatsApp. No usar una URL supuesta. Probar desde contacto de prueba permitido: consulta amplia → datos múltiples → pregunta lateral → edición de sede/hora → alcance de descarte → consentimiento → humano. Verificar precios/fechas contra datos sintéticos, no afirmar reserva; contar0 filas comerciales nuevas y0 modificaciones/cancelaciones, nunca inferirlo sólo del texto. Probar negativo de remitente/instancia no autorizados, revocación, duplicado, fallo de proveedor y replay. UnknownDelivery no se reenvía. El handoff acusa solicitud/modo persistido, no garantiza asesor notificado o disponible.

Kill switch: deshabilitar Enabled conservando WorkspaceId y protecciones. Si se necesita rollback de imagen, mantenerlo apagado y **pausar/aislar el ingreso DEMO antes de volver a una imagen antigua**: la antigua no tiene estas barreras. Verificar/drenar únicamente la cola DEMO según el mecanismo operativo autorizado, sin borrar/reprocesar registros. Restaurar sólo el ID/digest anterior registrado y el override anterior compatible, recrear únicamente API con --no-deps --no-build, repetir health; ninguna reversión de datos ni migración Down automática. Si la imagen anterior no soporta los documentos de contexto/journal o no puede aislarse el ingreso, mantener DEMO pausado y hacer corrección hacia delante.

## Privacidad y pendientes

Sólo datos sintéticos: texto de respuesta24h por defecto (AgentDialogue__ResultRetentionHours), ejecución30s (ExecutionSeconds), tombstone durable sin re-admisión. CompactBatchAsync elimina payload vencido en lotes≤100 bajo scope sintético y no tiene worker/API automático; payload no observado/compactado puede persistir. Contexto/historial/outbound tienen sus propias políticas; esta función no los elimina. **No autorizar PII real** hasta limpieza/IAM/costos certificados. Tombstone sin caducidad práctica evita replay pero crece1 documento/turno; presupuestar lecturas/escrituras/almacenamiento desde volumen observado y tarifas verificadas, actualmente UNKNOWN.

P0 histórico **ABIERTO**; barreras probadas offline, aislamiento operativo pendiente, causa histórica no resuelta. El corpus publicado está EXPOSED y necesita reemplazo externo independiente. Faltan evaluación humana, ensayo autorizado Groq/Gemini, integración real del journal, cuatro CI del nuevo SHA con parche, atestación de marca/manifiesto por réplica, rollback aislado, verificación de destino/health/canales y autorización del propietario. Ninguna validación local autoriza despliegue ni comercialización.
