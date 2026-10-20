# NexFlow RC-3 — runbook de publicación y rollback

**PREPARADO, NO EJECUTADO. RELEASE NO-GO hasta cerrar checklist y certificación.** No es una orden de despliegue. Operador autorizado debe verificar configuración real; no se accedió a Oracle, secretos ni proveedores desde Codex.

## 1. Identidad y condiciones previas

Base de desarrollo `8af3419658f1e29b8b80145638d5f58fa3030820` + cambios locales aún sin commit. Ese SHA base **no contiene** los parches 5–7. Tras revisión/publicación independiente del propietario, fijar RELEASE_SHA del commit aprobado y árbol limpio; nunca etiquetar el árbol sucio como el SHA base. Mantener candidato anterior **compatible con epochs/fences/UpdateTime** y su digest, o escoger pausa de escrituras/corrección hacia delante. No inventar un artefacto de rollback.

Requisitos de autorización antes de la ventana:

1. Checklist local verde; 24/24 integraciones PG/Emulator sin omisiones, evidencia de Up/Down, locks/pool, recuperación y métricas. Workflows nuevos solo manuales, sin credenciales SaaS. Este encargo no los ejecutó.
2. Staging aislado ARM64 con imagen exacta: probar codec PNG/JPEG/WebP y carga de libSkiaSharp.so, login/licencias/permisos, reservas, notificaciones y flujos reales autorizados. Publish local AArch64 no sustituye ejecución en esa plataforma.
3. No habilitar borrado nuclear RC-3. Los jobs antiguos se retienen. El harness 4.2 instancia el worker explícitamente para pruebas de su algoritmo; el host de producción no lo registra. No existe override por ENV/API. No editar filas Deleting/jobs/fences para forzar limpieza.
4. Backup restaurable, plan de ventana sin APIs antiguas escribiendo y procedimiento de rollback compatible revisados. Si falta alguno: NO-GO, no tocar Oracle.

## 2. Inventario privado en Oracle (operación futura, solo lectura)

Servicios compartidos: **no** `docker compose down`, `docker prune`, borrado de volúmenes, reinicio global, reconfiguración de Caddy, PostgreSQL, Redis, Evolution, n8n o Gotenberg por este release.

Con autorización, recuperar metadatos sin imprimir ENV/secrets:

```bash
docker inspect nexflow_api --format '{{.Config.Image}} {{.Image}} {{.State.StartedAt}}'
docker inspect nexflow_api --format '{{index .Config.Labels "com.docker.compose.project"}}'
docker inspect nexflow_api --format '{{index .Config.Labels "com.docker.compose.service"}}'
docker inspect nexflow_api --format '{{index .Config.Labels "com.docker.compose.project.config_files"}}'
```

Registrar nombres reales, **todos** los archivos Compose/overrides en orden y proyecto, redes, volúmenes montados, arquitectura, versión PostgreSQL, historial EF/índices y digests. No asumir que Compose publicado es el efectivo ni que nexflow_api es el nombre actual. Si cambió, detener este paso y reconciliar inventario. Revisar config efectiva privadamente; no exportarla ni publicarla, pues puede expandir secretos. No abrir/copiar `.env` al repo ni al informe.

Construir COMPOSE_ARGS como array Bash con cada `-f` real verificado y su `-p` si corresponde; API_SERVICE es el servicio identificado. `docker compose "${COMPOSE_ARGS[@]}" config --services` permite comprobar los nombres sin volcar variables. Esas variables **no están predefinidas** ni se debe sustituir una lista de overrides por el Compose base.

## 3. Salud y diagnóstico de base

Consultar por HTTPS a través del Caddy actual, preservando forwarded headers/HTTPS. Inspeccionar HTTP **y cuerpo** de `/health/live`, `/health/ready`; PostgreSQL y Firestore Healthy; Redis cache degradado no demuestra salud completa aunque readiness responda 200. Redirección HTTP interna 307 no implica fallo de aplicación. Probar workers/canal aparte: Healthy no acredita entrega WhatsApp.

SQL agregado solo lectura, en consola privada autorizada y sin payloads/teléfonos:

```sql
SELECT "Status", count(*), min("ReceivedAt"), min("NextRetryAt")
FROM "InboundMessages" GROUP BY "Status";
SELECT "EventType", "Status", count(*), min("CreatedAt")
FROM "OutboxMessages" GROUP BY "EventType", "Status";
SELECT "Kind", count(*), min("NextAttemptAt")
FROM "ReservationResourceStates" WHERE "PendingOperationId" IS NOT NULL GROUP BY "Kind";
```

Última consulta solo si la tabla ya existe; no crearla para diagnosticar. Correlacionar IDs únicamente en entorno privado, sin adjuntar cuerpos. Los **cuatro Outbox CATALOG_GENERATION_REQUESTED históricos referidos por la auditoría** deben verificarse/clasificarse por separado: no se sabe aquí si aún existen ni su causa. **No** cambiar Status/RetryCount ni reinyectarlos. Inbound SQL, Outbox PDF y outbound Firestore son colas distintas. Sent indica aceptación de proveedor, no confirmación de entrega al móvil; UnknownDelivery nunca autoriza reenvío automático.

## 4. Respaldo y restauración verificada

Antes de DDL, operador con autorización de datos crea respaldo privado cifrado/protegido y verifica restauración en **otra base aislada**. No ejecutar backup/restore contra producción desde Codex. Preservar secretos y volúmenes existentes; una imagen no es backup.

Ejemplo futuro con servicios libpq preconfigurados privadamente, sin contraseña inline:

```bash
umask 077
# BACKUP_FILE fuera del repo; BACKUP_SERVICE aprobado de solo lectura.
pg_dump --format=custom --file "$BACKUP_FILE" "service=$BACKUP_SERVICE"
sha256sum "$BACKUP_FILE"
pg_restore --list "$BACKUP_FILE" > "$BACKUP_MANIFEST"
# Solo tras verificar identidad de host/base temporal aislada; nunca producción:
pg_restore --exit-on-error --no-owner --no-privileges \
  --dbname="service=$ISOLATED_RESTORE_SERVICE" "$BACKUP_FILE"
```

Comprobar esquema/historia/contadores/restauración real. `pg_restore --list` por sí solo no prueba restaurabilidad. Para Firestore/objetos externos, acordar respaldo/restauración consistente y permisos del propietario por separado; pg_dump no respalda documentos ni assets. No copiar datos de clientes en artefactos CI.

## 5. Construcción por commit exacto y digests

En checkout limpio de la revisión aprobada, builder autorizado compatible con ARM64 (no realizar checkout ni build remoto desde esta sesión):

```bash
test "$(git rev-parse HEAD)" = "$RELEASE_SHA"
git diff --exit-code
git diff --cached --exit-code
test -z "$(git ls-files --others --exclude-standard)"
docker buildx build --platform linux/arm64 --target final \
  -f backend/Dockerfile -t "nexflow-api:$RELEASE_SHA" \
  --metadata-file "$API_BUILD_METADATA" --load backend
docker buildx build --platform linux/arm64 --target migrator \
  -f backend/Dockerfile -t "nexflow-migrator:$RELEASE_SHA" \
  --metadata-file "$MIGRATOR_BUILD_METADATA" --load backend
docker image inspect "nexflow-api:$RELEASE_SHA" --format '{{.Id}} {{.Architecture}}'
```

El Dockerfile existente tiene contexto **backend**, targets final/migrator, usuario no root y publicación Release. No ejecuta migraciones en el entrypoint API de producción. Las imágenes base `:10.0` son móviles: registrar los digests resueltos/metadatos del build y conservar el artefacto construido; no reconstruir un tag suponiendo identidad. Si se usa registro, fijar image `repositorio@sha256:...` en el override privado autorizado; en host local fijar ID inmutable verificado y no reciclar tags. No publicar una imagen sin revisión de SHA/digest. Guardar digests del migrator y binario compatible anterior.

No asumir que estos comandos se ejecutaron aquí: solo hubo publish Release linux-arm64 local; no Docker, push ni registro remoto.

## 6. Ventana y migración pendiente

1. Detener admisión de escrituras y drenar **todas** las APIs/workers antiguos de NexFlow, incluidos consumidores de reservas/PDF, en todos los procesos/hosts identificados. Mantener servicios compartidos levantados. Pausar no equivale a perder trabajos: preservar colas, leases, identidades y volúmenes. No matar procesos a mitad de una operación sin evaluar su recuperación durable.
2. Verificar que no quedan escritores del protocolo anterior. **No es válido un rolling deploy mixto con escrituras.** API antigua puede desconocer epochs/fences aunque lea el esquema aditivo.
3. Revisar historial EF real y SQL exacto de Up; si hay pendientes distintos de lo esperado, detenerse. Migración esperada: `20261019170831_CoordinateReservationResourcesAndReuseCancelledSlots` (sin cambios en este encargo). Crea ReservationResourceStates y reemplaza índice de reserva exacta por único parcial `Status <> 'Cancelled'`; SourceMessageId/xmin se conservan.
4. Ejecutar **una sola** instancia del migrator aprobado con configuración privada ya autorizada y redes PostgreSQL verificadas. El target migrator hace MigrateAsync de pendientes y SystemCatalogSeeder: no asumir que solo aplica una migración ni usarlo sin revisar pendientes. No arranca API/Evolution/n8n. No poner connection strings en comandos/logs. El comando Compose concreto de one-shot depende de servicios/overrides privados auditados; no usar `compose run api` para fingir un migrator ni adjuntar credenciales a ejemplos públicos.
5. Verificar exit 0, migration history, tabla, FK/índices, muestras sintéticas canceladas/reutilización, SourceMessageId y ausencia de transacciones huérfanas. Up/CREATE INDEX no CONCURRENTLY puede bloquear escrituras: medir en staging y reservar ventana. No aplicar SQL parcial improvisado.
6. Activar exclusivamente API/workers del digest compatible con el esquema, con `ASPNETCORE_ENVIRONMENT=Production` verificado. Development ejecuta migraciones/seeder; no usarlo en Oracle. Comprobar que el worker nuclear queda ausente y DeleteClient devuelve 503 sin persistir intención nueva.

Ejemplo **solo una vez verificados** COMPOSE_ARGS/API_SERVICE/override con digest y finalizada la migración:

```bash
docker compose "${COMPOSE_ARGS[@]}" up -d --no-deps "$API_SERVICE"
```

No `down`, `--remove-orphans`, recreación de bases ni reinicio de Caddy/Evolution/n8n. Reabrir escrituras solo tras health y controles sintéticos internos correctos; después funcionales autorizadas reales con IDs propios, nunca eventos históricos.

## 7. Preflight y funcionales reales de proveedores

- PostgreSQL/Firestore/Redis: health, conectividad privada, índices y accesos reales, pool y transacciones; no probar caída de producción. Aislar fallos intencionales en staging.
- Groq/Gemini: modelos configurados, cuotas y gasto autorizados, timeout/fallback/429. No imprimir claves/prompts. Fallo de ambos debe quedar observable sin inventar datos ni perder borradores.
- Evolution: comprobar instancia exclusiva por workspace, key derivada por instancia y rechazo cruzado/fromMe/replay/dedup. Solo después de recepción real en **cada** instancia autorizar retiro de legacy (`Evolution__AllowLegacyWebhookKey=false`) como cambio separado de configuración. No borrar/logout sesiones automáticamente.
- n8n/Gotenberg: workflow activo y revisión de payload/callback exacta, HMAC/timestamp, PDF PRODUCT/SERVICE, cuota 3, fallo 404/503/timeout, FAILED persistente al plazo (default 5 min), cancelación lógica y callback tardío ignorado. No publicar/modificar workflows por este runbook sin autorización separada.
- Cloudinary: imagen normalizada y PDF real accesibles; no usar una URL/asset ambiguo como prueba de ownership. No purgar folders compartidos.
- Dos workspaces y cuentas autorizadas: login/roles/licencias, CRUD, reservas web/WhatsApp simultáneas y replay, pedido/solicitud única, interrupciones informativas, handoff una vez, inbox, actualización/invalidez PDFs, permisos revocados y cambio de sesión. Borrado nuclear **no** es parte de pruebas destructivas autorizadas.
- Notificación fallida con negocio confirmado: comprobar negocio por ID y notificación determinista antes de reparar el aviso. No reinyectar el inbound completo ni reejecutar órdenes/reservas/outbound inciertos. Failed/DeadLetter se revisan; UnknownDelivery exige evidencia del proveedor, nunca retry ciego.

## 8. Medición y aceptación operacional

Registrar p50/p95/p99 de ACK webhook, cola/turno, Firestore, IA, outbound e inbox; contadores y edad máxima Pending/Processing/Failed/DeadLetter por workspace (sin contenido), UnknownDelivery, cola PDF, CPU/RAM/disco/conexiones y latencia de advisory locks. Leer cuerpo de health, no solo código 200. Validar aviso/cooldown y recuperación tras reinicio en staging.

Objetivos **provisionales a aceptar por el operador**, no resultados medidos: ACK webhook p95 <1 s, turno saludable p95 <10 s sin degradación persistente de cola; investigar cualquier cabeza >5 min y cualquier duplicación comercial (NO-GO inmediata). PDF debe alcanzar estado terminal al deadline configurado aunque la página esté cerrada. Medir especialmente conexiones ocupadas por guardas durante Firestore, pool/cancelación y picos con PDF; no asumir capacidad por RAM/CPU referidas en documentos históricos. Ajustar objetivos con evidencia y presupuestos reales antes de clientes comerciales.

La medición local de 64 turnos fake (ver checklist) no prueba esos objetivos. El emulador tampoco certifica IAM/cuotas/latencia/consistencia operacional completa de Cloud. La ventana de commit remoto después del purge sigue abierta a certificación: **mantener borrado deshabilitado** aunque los 24 tests pasen.

## 9. Frontend Firebase Hosting

En revisión limpia aprobada, variables públicas Vite verificadas sin secretos y API HTTPS correcta:

```bash
cd frontend
npm ci
npm run build
npm run lint
node --import ./tests/reservations-loader.mjs --test tests/*.test.ts
node --test --test-concurrency=1 tests/*.test.mjs
# Solo con aprobación de publicación y cuenta/proyecto/target comprobados:
firebase deploy --only hosting:nexflow --config firebase.nexflow.json
```

Browsers existentes requieren Chromium instalado Windows (como runner manual preparado); en otros hosts no declarar tests aprobados si no está disponible ese entorno. Configs firebase.json/firebase.nexflow.json actualmente equivalentes: comando canónico especifica el segundo, ambos sirven **dist**, target nexflow y rewrite SPA index.html. Verificar proyecto/target efectivos del propietario antes del deploy; no cambiar Auth, reglas Firestore ni config de otros sitios. `.firebase` es caché local ignorada, no artefacto publicable. Confirmar rutas SPA, login, CORS/HTTPS y versión contra el backend compatible.

## 10. Rollback y criterios de parada

Parar reapertura de tráfico si migración/health/contratos fallan, aparece cruce de tenant, duplicación, error de codec o estado comercial inconsistente. No compensar borrando reservas ni devolviendo cuota a ciegas.

Rollback preferido: **solo aplicación**, manteniendo ReservationResourceStates/epochs/fences/índice parcial/historial y restaurando el digest anterior **certificado compatible con el protocolo y UpdateTime** mediante los mismos Compose/overrides reales. No restaurar API preprotocolo (`4d1d0fb` o anteriores) ni usarla con escrituras. Si no existe imagen compatible, mantener pausa y corregir hacia delante; no improvisar Down.

Down de la migración obtiene ACCESS EXCLUSIVE y debe rechazar si existe **cualquier epoch**, incluso Ready, o historia de slots reutilizados. No borrar/truncar estados/fences ni reservas para forzarlo. Rollback de esquema exige otra autorización, prueba aislada y apps detenidas. Restaurar backup implica pérdida potencial de escrituras posteriores y coordinación Firestore: decisión del propietario, no una acción automática ni prueba de reversibilidad entre dos almacenes.

Rollback frontend: versión Hosting anterior aprobada compatible con API; conservar contratos. No usar un rollback de UI para reactivar operaciones backend bloqueadas. Mantener volúmenes, secretos, Caddy e integraciones compartidas intactos.

Cerrar ventana solo con evidencias privadas sanitizadas de SHA/digests, tests, health, métricas, respaldo/restauración, funcionales y decisión del responsable. **Ninguna operación de este runbook se ejecutó en Oracle en este encargo.**
