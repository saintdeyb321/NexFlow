# NexFlow LLM-first — candidata DEMO, NO desplegada

## Identidad y límites

Rama única: `feature/llm-first-agent`. F–K ya fue publicado: HEAD local/origin/remoto **6bba4c4d86ac5bd0a5dbb3c6476916fbb5b238dd**. `main` permanece en **5f5ff850e31df3fa73cb2c7f02f65ba6081173c7**. La certificación pre-DEMO reprodujo/corrigió un P1 de recarga de identidad: candidata actual **6bba4c4+parche local sin commit**, todavía sin SHA nuevo, imagen/digest ni runs certificados. No atribuirle CI de otra revisión. Evidencia nueva: `backend/TestResults/LlmFirstPreDemo_20261103/`; la anterior LlmFirstFinal_20261103 se preserva con su procedencia. Dictamen vigente: `docs/auditorias/llm-first-agent/CERTIFICACION_PRE_DEMO.md`.

Frente a main, la rama publicada incorpora cuatro commits de shadow/journal/evaluación/preparación/cierre F–K, además del nuevo parche de protección monotónica por host. `git diff main --stat` sólo cuenta archivos rastreados; revisar también untracked **sin abrir el corpus/rúbrica privados**. Retirarlos del repo por el propietario antes de staging/publicación; no agregarlos accidentalmente. No cambian frontend, Dockerfile, Compose, appsettings, migraciones ni corpus/evaluadores históricos.

Al aprobar la revisión, el propietario publicará por separado el commit final. Antes de cualquier CI/build: `git branch --show-current`, `git rev-parse HEAD`, `git status --short`, comprobar SHA remoto y exigir árbol aprobado limpio. Fijar `RELEASE_SHA` completo; no usar main/latest ni reconstruir después una imagen que se presente como la ya certificada.

## Cuatro gates independientes

Resultados definitivos y TRX: `docs/auditorias/llm-first-agent/CIERRE_FINAL_LLM_FIRST.md`.

- OFFLINE_GO: **GO local del árbol parcheado**. Build0 errores; vigente2128/2128; histórico130/101/29 exacto; dirigidos69/69; diff checkPASS. YAML4/4 y12 scripts Python embebidos parseados. Python13+9 corresponde al cierre anterior, no se relanzó. TRX y motivos rojos históricos preservados; no equivale a CI/proveedor real ni aprueba el P1 de6bba4c4 sin parche.
- PROVIDER_EVAL_GO: **NO_GO/WAITING_FOR_OWNER**. Corpus independiente12 y rúbrica ahora existen y permanecen sellados, sin lectura por Codex; faltan candidata congelada, gates externos, modelos/cuotas y autorización del ensayo real.
- DEMO_DEPLOY_GO: **NO_GO/WAITING_FOR_OWNER**. Falta SHA final publicado, CI/integraciones reales/ARM64 de ese SHA, custodia de imagen y autorización exacta de destino/ventana. La implementación local prepara esa revisión.
- COMMERCIAL_GO: **NO_GO**. P0 histórico no resuelto, calidad/proveedores/WhatsApp reales no certificados, privacidad/retención aún limitada a datos sintéticos.

## Configuración backend DEMO (sin archivos privados versionados)

Sólo mediante el mecanismo privado de configuración existente. Nombres reales:

```text
AgentDemo__WorkspaceId=<GUID del workspace sintético autorizado>
AgentDemo__InstanceName=<instancia Evolution exclusiva, ya asignada a ese workspace>
AgentDemo__TestPhones__0=<contacto internacional canónico de prueba autorizado>
AgentDemo__OperatorAuthorization=SYNTHETIC_DEMO_NO_COMMERCIAL_WRITES
AgentDemo__Enabled=false
AgentDialogue__Enabled=false
AgentShadow__Enabled=false
```

No copiar teléfonos, claves o configuración efectiva a este documento ni a Actions. TestPhones permite1–5 teléfonos canónicos; no admite un remitente arbitrario. No hay activación por mensaje, header o URL. La ruta DEMO no requiere AgentExperimentScope; sólo el dispatcher backend abre su scope diferente. Sin WorkspaceId DEMO configurado, legacy conserva su comportamiento. **Con WorkspaceId protegido y Enabled=false se suspende la respuesta DEMO, no se vuelve a legacy y siguen bloqueadas las escrituras comerciales.** No quitar WorkspaceId para apagarlo. El parche hace la política singleton: conserva por host las identidades observadas y, ante sustitución/omisión, bloquea diálogo aunque después se restaure el GUID. Esa acumulación no es durable entre reinicios: comprobar GUID idéntico en configuración efectiva de todas las APIs/workers y aislar ingreso antes de reiniciar/rollback, o NO_GO. IOptionsMonitor observa recargas; env necesita reinicio autorizado sólo de API. HTTP ya enviado no puede deshacerse. El kill switch no drena ni cancela eventos n8n anteriores: exigir workspace sintético nuevo y colas comerciales vacías.

Tras cerrar los gates y recibir autorización del operador: habilitar exclusivamente AgentDemo__Enabled para el workspace sintético. Licencias reales de prueba, datos sintéticos y permisos siguen siendo obligatorios; el flag no concede módulos. La instancia debe pertenecer al mismo workspace y el webhook conservar la autenticación existente. No vincular un teléfono/instancia de cliente, ni abrir un túnel ni cambiar Evolution desde este procedimiento.

API en Production/Staging con contratos existentes de PostgreSQL/Redis/Firestore y proveedores: ConnectionStrings__DefaultConnection, ConnectionStrings__Redis, Firebase__ProjectId, GOOGLE_APPLICATION_CREDENTIALS y Gemini/Groq__ApiKey/Model privados. No hay nuevos secretos. No iniciar en Development: Program ejecuta migración/seeder en ese entorno. Production no inicia migrator. Ningún acceso a Oracle ni cambio de estos valores se ha ejecutado aquí. El destino, almacenes aislados y workspace no están autorizados por este archivo.

## Certificación remota manual del mismo SHA

Sólo después de autorización expresa de publicación y dispatch, propietario selecciona **feature/llm-first-agent**, el mismo SHA definitivo y conserva Run ID/run_attempt/head_sha, jobs, TRX e identity.json:

1. `.github/workflows/rc3-release-checks.yml`: backend vigente sin fallos/omisiones, histórico exactamente130/101/29 con los mismos motivos/hashes; build y contratos frontend existentes.
2. `.github/workflows/rc3-reservation-integration.yml`: PostgreSQL temporal + Firestore Emulator loopback. Cero fallos y omitidas; inventario debe incluir `AgentJournalEmulatorTests.Two_emulator_clients_enforce_one_owner_atomic_context_and_physical_tombstone`. Es una prueba con **dos clientes**; no acredita dos procesos ni IAM de producción. La nueva prueba no requiere migraciones; el workflow existente sólo usa migraciones de su PostgreSQL temporal autorizado.
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

P0 histórico **ABIERTO**, barreras independientes probadas offline **AISLADO_SOLO_DEMO** bajo las condiciones de configuración/aislamiento indicadas, no causa histórica resuelta. Corpus externo ahora existe, pero faltan evaluación humana, ensayo autorizado Groq/Gemini, integración real del journal, CI/ARM64 del próximo SHA con parche, verificación de destino/health/canales y autorización del propietario. Ninguna validación local autoriza despliegue ni comercialización.
