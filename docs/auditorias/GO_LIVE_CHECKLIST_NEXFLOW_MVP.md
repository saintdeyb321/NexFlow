# NexFlow MVP — checklist de gates y procedimiento del operador

Preparado 2026-10-26 sobre `be07f89c2146f7c4344effad849e1335c9614e86`. **NO es autorización de publicación ni comando para ejecutar desde esta tarea.** Estado inicial: READY_FOR_OPERATOR para recopilar evidencia; NO-GO para modificar Oracle.

## 1. Identidad y evidencias CI — S6

Responsable: propietario/release engineer; revisor independiente valida artefactos. No se hicieron dispatch, consulta de runs ni descargas remotas aquí. El dato histórico «cero runs al auditar» no describe necesariamente el estado actual.

1. Elegir explícitamente `RELEASE_SHA` después de revisar el diff y congelarlo manualmente. El backend probado localmente es be07f89c; si se publica otra revisión, registrar el SHA completo y repetir los gates sobre ella. No atribuirle runs de otra revisión aunque sólo cambie documentación.
2. Propietario inicia los tres workflows desde Actions sobre la referencia verificada. **No se incluye dispatch automático en ningún script.** Si main cambia entre ejecuciones, no combinar resultados; comprobar `head_sha` de cada run y attempt.

| Workflow y enlace del operador | Evidencia esperada del MISMO SHA |
|---|---|
| [rc3-release-checks.yml](https://github.com/saintdeyb321/NexFlow/actions/workflows/rc3-release-checks.yml) | Build, G8 current sin fallos/omisiones (local actual 1538), históricos exactos 130/118/12, TS ≥83/browser ≥148 sin omisiones, frontend build/lint y codec Windows (no ARM64) |
| [rc3-reservation-integration.yml](https://github.com/saintdeyb321/NexFlow/actions/workflows/rc3-reservation-integration.yml) | G8 + PostgreSQL/Firestore Emulator: inventario actual esperado 39 Integration (mínimo del workflow 37), todas ejecutadas/aprobadas; ninguna desaparecida/omitida |
| [concierge-redis-integration.yml](https://github.com/saintdeyb321/NexFlow/actions/workflows/concierge-redis-integration.yml) | Exactamente 1 RedisIntegration ejecutada/aprobada; PostgreSQL, Firestore Emulator y Redis desechables en loopback |

3. Para cada workflow registrar URL `https://github.com/saintdeyb321/NexFlow/actions/runs/<RUN_ID>/attempts/<ATTEMPT>`, head_sha, ref, run_attempt, job/steps, conclusión, fecha y artefactos. El job verde no sustituye TRX/JUnit. Descarga manual autorizada: artefactos `rc3-release-unit-results`, `rc3-isolated-test-results`, `concierge-redis-test-results`. No instalar gh si falta.
4. Release: revisar `rc3-unit.trx/.g8.json`, `rc3-historical.trx/.g8.json`, `rc3-typescript.xml`, `rc3-browser.xml` y diagnóstico Chromium si existe. Integration/Redis: `identity.json`, `unit.trx`, `historical.trx` cuando corresponda, `integration.trx`/`redis.trx`. Identidad release está también en los JSON G8; no inventar identity.json donde el workflow no lo genera.
5. Verificar counters total=executed=passed para vigente/integraciones/Redis, failed/notExecuted/error/aborted/timeout=0; frontend sin failure/error/skipped. Comparar inventario local, no aceptar 37 si han desaparecido dos de las 39 actuales sin explicación. G8 conserva mínimo vigente 1491/runtime984; no se altera para esta entrega. Para esta candidata, menos de 1538 requiere investigar, no ocultar pérdida de cobertura.
6. Histórico: ejecutar el validador existente `python -B .github/workflows/g8_certification.py verify historical --trx <TRX> --test-exit-code 1` sobre checkout del SHA exacto. Debe verificar hashes/inventario/razones, no sólo cantidades. Vigente análogo con `verify current --test-exit-code 0`. No suprimir códigos de error; artifacts ausentes, SHA distinto, skips o fallo nuevo = NO-GO.

Campos a completar, hoy **PENDIENTES**: SHA final; tres Run ID/attempts; enlaces TRX/JUnit/identity; fechas; revisor; conclusión. Los JSON locales tienen identidad GitHub nula; no son CI remoto.

## 2. ARM64 y permisos — S6, NO EJECUTADO

Runner Linux aislado autorizado con Docker/SDK existentes, ARM64 nativo o emulación registrada. No instalar ni construir en Oracle en este encargo. Preparar contexto allowlist de fuentes versionadas del SHA aprobado: backend.slnx, Dockerfile/.dockerignore y los cinco proyectos Domain/Application/Infrastructure/API/Migrator. Revisar recursos requeridos y excluir tests, bin/obj, .git, .env, cuentas de servicio y secretos. `appsettings.json` versionado sólo entra tras verificar privadamente que es configuración pública sin valores reales; nunca copiar configuración operativa. No imprimir su contenido.

En contexto temporal desechable con ruta verificada, probar checkout `umask 077`, archivos fuente 600/directorios 700. El Dockerfile real conserva COPY chown y runtime no-root. No se modifica el Dockerfile ni se inicia migrator. Comandos futuros de builder (variables previamente fijadas por el operador):

```bash
set -euo pipefail
test -n "$RELEASE_SHA" && test -n "$CTX" && test -n "$EVIDENCE_DIR"
docker buildx build --platform linux/arm64 --target final --load \
  --label "org.opencontainers.image.revision=$RELEASE_SHA" \
  --metadata-file "$EVIDENCE_DIR/api-build.json" -f "$CTX/Dockerfile" -t "nexflow-api:$RELEASE_SHA" "$CTX"
docker buildx build --platform linux/arm64 --target migrator --load \
  --label "org.opencontainers.image.revision=$RELEASE_SHA" \
  --metadata-file "$EVIDENCE_DIR/migrator-build.json" -f "$CTX/Dockerfile" -t "nexflow-migrator:$RELEASE_SHA" "$CTX"
for image in "nexflow-api:$RELEASE_SHA" "nexflow-migrator:$RELEASE_SHA"; do
  test "$(docker image inspect "$image" --format '{{.Os}}/{{.Architecture}}')" = linux/arm64
  test "$(docker image inspect "$image" --format '{{.Config.User}}')" = 1654
  main=NexFlow.API.dll
  if test "$image" = "nexflow-migrator:$RELEASE_SHA"; then main=NexFlow.Migrator.dll; fi
  docker run --rm --pull=never --platform linux/arm64 --network none --read-only -e MAIN_DLL="$main" \
    --cap-drop ALL --security-opt no-new-privileges --entrypoint /bin/sh "$image" -c \
    'set -eu; test "$(id -u)" = 1654; test -x /app; test -r /app/appsettings.json; test -r "/app/$MAIN_DLL"; bad=$(find /app -type d ! -executable -print -quit); test -z "$bad"; bad=$(find /app -type f -name "*.dll" ! -readable -print -quit); test -z "$bad"; stat -c "%u:%g %a %n" /app /app/appsettings.json; dotnet --list-runtimes'
done
```

El shell falla ante un archivo/directorio inaccesible; no se depende del status de `find -exec test` ni sólo del último stat. MAIN_DLL es un nombre público de assembly, no configuración SaaS. Registrar modos reales sin exigir exactamente 600 como modo normal de publish.

Variante restrictiva específica: derivar temporalmente cada imagen aprobada en el builder, USER root únicamente en ese stage de ensayo para chmod de archivos publicados a 600 y **subdirectorios publicados** a 700; volver a USER 1654. No hacer chown en el stage de ensayo: ocultaría un COPY incorrecto. `/app` es WORKDIR de la base y debe conservar su acceso de recorrido; no poner 700 a un directorio root-owned artificialmente. Asertar owner 1654 de appsettings/assembly y acceso real. No usar 777, no cambiar imagen productiva ni ejecutar migrator. Conservar receta y resultados de ambas variantes. Si no se puede demostrar el caso restrictivo, PENDIENTE.

### CodecSmoke contra la MISMA imagen

Reutilizar `backend/tools/CodecSmoke`, `NexFlow_RC3_DEPLOY_RUNBOOK.md` §5.1. Crear contenedor detenido de la imagen, copiar sólo `/app/SkiaSharp.dll` a directorio de referencia sintético y compilar el harness con `-p:ApiPublishDir=<referencia>`; nunca copiar librerías nativas del host. Montar únicamente la DLL IL del harness, legible por 1654. No arrancar API ni migrator:

```bash
docker run --rm --pull=never --platform linux/arm64 --network none --read-only \
  --cap-drop ALL --security-opt no-new-privileges --pids-limit 64 --memory 512m --cpus 1 \
  --tmpfs /tmp:rw,noexec,nosuid,size=16m -e DOTNET_EnableDiagnostics=0 \
  --mount "type=bind,source=$SMOKE_DLL,target=/app/NexFlow.CodecSmoke.dll,readonly" \
  --entrypoint dotnet "$API_IMAGE" exec --runtimeconfig /app/NexFlow.API.runtimeconfig.json \
  --depsfile /app/NexFlow.API.deps.json /app/NexFlow.CodecSmoke.dll --require-arm64
```

Exigir Linux Arm64, exit 0 y PASS Png/Jpeg/Webp encode/decode/normalize; también runtime .NET 10 y acceso a nativos. Windows/amd64/inspección ELF no equivalen. Registrar SHA, imagen ID, digest de registro cuando exista, digests base resueltos, plataforma, emulación, outputs sintéticos y herramienta. Tags sdk/aspnet :10.0 móviles impiden afirmar reproducibilidad bit a bit; retener el artefacto exacto por digest. Todo el bloque permanece PENDIENTE sin ejecución real.

## 3. Condiciones previas S7/S8 y responsables

S7 local/documental verificado, evidencia real aún pendiente. [Q01–Q10 y recuperación](MATRIZ_Q01_Q10_VALIDACION_MVP.md); [evaluación/piloto](PLAN_PILOTO_E2E_WHATSAPP_MVP.md). Tabla obligatoria a firmar; hoy no hay aprobaciones externas:

| Gate | Responsable / revisor | Estado actual |
|---|---|---|
| SHA, CI vigente/históricos y TS/browser | Release engineer / auditor | Local 1538/1538; CI PENDIENTE |
| PostgreSQL/Firestore Emulator y Redis reales | QA/operador / auditor | 39+1 descubiertas, 0 ejecutadas aquí |
| Digest ARM64, UID/600–700 y codecs | Release engineer / auditor | PENDIENTE |
| Esquema activo compatible, sin migraciones requeridas | Operador DBA / responsable backend | Sin diff de esquema local; Oracle NO VERIFICADO |
| Q01–Q10 y cinco/cuatro por ítem real | Operador de lectura / dueño de negocio | PENDIENTE |
| Whitelist fiscal/plantilla y PDF visual | Propietario producto/privacidad / negocio | PENDIENTE; no publicar por suposición |
| Backups y restore aislado PG/Firestore/objetos | Responsable de datos / auditor | Historial SQL anterior no basta; Firestore PENDIENTE |
| Corpus independiente ≥32, rúbrica y revisión humana | Evaluador ajeno / producto | PENDIENTE, no se creó corpus privado en repo |
| Presupuesto/modelos/cuotas y proveedores | Propietario / responsable técnico | PENDIENTE, ninguna llamada real aquí |
| Número/contactos/workspace DEMO y aislamiento | Propietario / operador | PENDIENTE, ningún WhatsApp real aquí |
| Roles web/configuración/soporte y handoff | Negocio/soporte / seguridad | Contratos estáticos y tests locales OK; permisos reales PENDIENTE |
| Admisión/drenaje, abortos y rollback/fix-forward | Operador / propietario + auditor | Procedimiento listo; implementación operativa NO VERIFICADA |

Si no hay recuperación Firestore probada, sólo puede proponerse un piloto 100% descartable con aceptación expresa de pérdida; no ventas ni datos de clientes. Esto no exime CI, privacidad, consentimiento, ARM64, presupuesto o aislamiento. Cualquier fila crítica pendiente impide declarar GO de despliegue/comercialización.

## 4. Preflight real futuro: exclusivamente lectura primero

**No ejecutado.** Tras nueva autorización, operador identifica API_CONTAINER del inventario real, no de un ejemplo histórico. No imprimir inspect completo, Compose interpolado completo, env, credenciales o logs brutos. Comandos de campos permitidos:

```bash
docker container inspect "$API_CONTAINER" --format '{{.Config.Image}} {{.Image}} {{.Config.User}}'
docker container inspect "$API_CONTAINER" --format '{{index .Config.Labels "com.docker.compose.project"}}'
docker container inspect "$API_CONTAINER" --format '{{index .Config.Labels "com.docker.compose.service"}}'
docker container inspect "$API_CONTAINER" --format '{{index .Config.Labels "com.docker.compose.project.config_files"}}'
docker container inspect "$API_CONTAINER" --format '{{index .Config.Labels "com.docker.compose.project.working_dir"}}'
docker container inspect "$API_CONTAINER" --format '{{index .Config.Labels "com.docker.compose.project.environment_file"}}'
```

Registrar imagen efectiva/digest/revisión OCI si existe; retener el artefacto anterior **sin declararlo compatible**. Comparar fuentes y esquema con RELEASE_SHA. Verificar RuntimeEnvironment Production y que no se inicia migrator; Program.cs:172 migra sólo en Development, así que un entorno erróneo es NO-GO antes de arrancar. Sin campos/labels confiables o sin esquema confirmado: detener, no reconstruir Compose de memoria.

Operador construye arrays Bash con `-p`, `--project-directory`, cada `-f` en orden de las labels y los `--env-file` efectivos **por ruta validada**, nunca su contenido en el informe. API_SERVICE debe ser la etiqueta verificada del host NexFlow, aprobada por propietario; no suponer que cualquier contenedor con nombre api lo es. No expandir config_files como una cadena de shell ni ejecutar `eval`. Comprobar privadamente puertos/red/volúmenes/usuario, digests, permisos, workers, cuotas e historial EF sin DDL.

Inspección de servicios permitida: `docker compose "${COMPOSE_ARGS[@]}" config --services`. Salud PostgreSQL/Redis/Firestore por comprobaciones existentes y proyecciones Q; Evolution/n8n/Gotenberg por health del despliegue **por separado**, con URLs/versión autorizadas por el operador. No crear instancia, pedir QR, desconectar sesión, activar workflow, convertir documento real o enviar WhatsApp como health check. Liveness API no comprueba esas dependencias.

## 5. Override de sólo imagen y comparación efectiva

Preparar fuera del repo, en área temporal protegida del operador, un override que contenga únicamente `services.<API_SERVICE>.image: <RELEASE_IMAGE>`. RELEASE_IMAGE tiene que ser referencia inmutable de registro por digest aprobado, ya cargada/disponible localmente en el host; comparar ID/digest/arquitectura y label SHA. No construir ni hacer pull durante el corte. Si sólo existe tag mutable o digest no verificado: NO-GO. No cambiar variables, rutas, volúmenes, puertos, flags, perfiles ni dependencias para acomodar la imagen.

`BEFORE_ARGS` representa Compose efectivo actual; `CANDIDATE_ARGS` conserva su orden y añade sólo el override. Comprobar que el **config efectivo** difiere exclusivamente en esa imagen, sin guardar/imprimir secretos. Ejemplo de verificador en memoria para operador, no archivo nuevo del repositorio:

```python
# Requiere BEFORE_ARGS_JSON/CANDIDATE_ARGS_JSON: arrays de argumentos/rutas,
# API_SERVICE y RELEASE_IMAGE públicos; nunca credenciales como argumentos.
import json, os, subprocess, sys
def effective(name):
    args = json.loads(os.environ[name])
    if not isinstance(args, list) or not all(isinstance(a, str) for a in args):
        sys.exit('NO-GO: invalid compose arguments')
    p = subprocess.run(['docker', 'compose', *args, 'config', '--format', 'json'],
                       capture_output=True, text=True)
    if p.returncode != 0:
        sys.exit('NO-GO: compose validation failed; inspect privately')
    try: return json.loads(p.stdout)
    except ValueError: sys.exit('NO-GO: invalid compose JSON')
a, b = effective('BEFORE_ARGS_JSON'), effective('CANDIDATE_ARGS_JSON')
service = os.environ['API_SERVICE']
if service not in a.get('services', {}) or service not in b.get('services', {}):
    sys.exit('NO-GO: API service not found')
if b['services'][service].get('image') != os.environ['RELEASE_IMAGE']:
    sys.exit('NO-GO: candidate digest differs')
a['services'][service].pop('image', None)
b['services'][service].pop('image', None)
if a != b: sys.exit('NO-GO: effective change exceeds the API image')
print('OK: effective image-only diff')
```

No publicar stdout/stderr de Compose en caso de error. El verificador muestra sólo resultado, no JSON. Un diff inesperado pide revisión/autorización, nunca un override adicional automático. Los nombres y rutas reales permanecen PENDIENTES hasta inspección autorizada del contenedor.

## 6. Admisión, escritores y cinco minutos de quietud

1. Aprobar cierre de admisión por el mecanismo operativo existente. No hay un kill switch nuevo de NexFlow por tenant. El operador debe identificar cómo detiene productores de mensajes, mutaciones web y callbacks pendientes sin perderlos, manteniendo health de lectura; no inventar env flags ni cambiar Caddy/Evolution/n8n dentro de este procedimiento. Si no puede aislarse el tráfico en el host compartido, NO-GO. Las pruebas de degradación se hacen fuera de dependencias productivas compartidas.
2. Dejar terminar trabajo legítimo antes del corte. InboundMessageWorker y OutboxProcessorWorker residen en el host API, pero inventariar también réplicas/workers externos o tareas administrativas. Si hay escritores antiguos separados, este permiso de reemplazar sólo API no autoriza tocarlos: solicitar un plan operacional aparte y no mezclar versiones por defecto.
3. Drenar sin borrar/reencolar ni cancelar a ciegas: inbound Pending/Processing/Failed reintentable = 0; Outbox Pending/Processing = 0; outbound Pending/Attempting = 0 y generaciones/operaciones PDF sin trabajo activo. Si existe entrega incierta, preservar estado y resolver alcance con operador; no convertirla a Pending. Failed terminal/DeadLetter históricos se registran como baseline, no se reprocesan.
4. Medir **cinco minutos continuos** sin nuevas entradas ni efectos/enviar pendientes, con muestras de inicio y al menos cada 30 s. Una foto de cero no basta. Cualquier nuevo trabajo reinicia la ventana o aborta. Verificar ventana global del host escritor, no sólo W-DEMO si procesa otros tenants.
5. Consultas agregadas de drenaje bajo READ ONLY, con timeout y permiso explícito de agregados globales del host:

```sql
SELECT COUNT(*) FILTER (WHERE "Status" IN (0,1,3)) AS actionable_inbound
FROM "InboundMessages";
SELECT COUNT(*) FILTER (WHERE "Status" IN (0,3)) AS actionable_outbox
FROM "OutboxMessages";
```

Inbound 3 es Failed aún reintentable; Outbox 3 es Processing (enums distintos). Complementar fuentes Q09 con últimas fechas/counts, claims/leases y métricas existentes. Outbound/PDF se verifican por lecturas documentales limitadas y estado sin normalizador; si falta inventario/index requerido, no crear índice en esta tarea ni asumir cero. No imprimir documentos privados. No SQL UPDATE para hacer que el drenaje pase.

6. Revisar grace period/stop-signal efectivos y ausencia de claims en vuelo. Un SIGKILL por timeout invalida la supuesta parada limpia y exige reconciliación; no asumir que apagar un contenedor deshace un efecto. Backups/restore y compatibilidad deben estar aprobados antes de cambiar binario.

## 7. Canary: una única API, aprobación final obligatoria

Esta sección es **futura y no ejecutada**. Exige firmas y todas las variables reales/manifestos comprobados. No es una autorización contenida en este Markdown.

1. Revalidar inmediatamente digest local/ARM64/USER, SHA, config image-only y quietud. Asegurar que API_SERVICE es el servicio aprobado y CANDIDATE_ARGS el array ya comparado. Ninguna migración pendiente o variable Development; ningún migrator/worker incompatible añadido.
2. Único reemplazo aprobado, sin tocar dependencias:

```bash
docker compose "${CANDIDATE_ARGS[@]}" up -d --no-deps --no-build --pull never "$API_SERVICE"
```

3. Comprobar imagen/ID/label efectiva del contenedor nuevo contra digest aprobado. No aceptar que el comando termine sin error como prueba de versión o salud. No `compose down`, `prune`, eliminación de volúmenes, reinicio de Postgres/Redis/Evolution/n8n/Gotenberg ni migrator.
4. Verificar HTTPS `/health/live` y `/health/ready`, HTTP 200 y JSON Healthy; registrar sólo status/check-component/status/duration, **nunca description** (puede contener diagnóstico sensible). Liveness usa Predicate=false; por sí solo no prueba PostgreSQL/Firestore/Redis. Dependencias/canal por separado y sin operaciones de negocio. Dominio/certificado/URL deben estar confirmados; no credenciales en la URL.
5. Mantener admisión general cerrada. Sólo tras salud/identidad correctas permitir el grupo DEMO explícitamente aislado y autorizado, con [plan E2E](PLAN_PILOTO_E2E_WHATSAPP_MVP.md). Un canary en API única es una ventana controlada, no un despliegue gradual por tenant si la infraestructura no ofrece esa capacidad. Si no se puede limitar participantes/tráfico, NO-GO.
6. Observar por ventana y umbrales **fijados antes del corte**: 5xx, readiness, cola/antigüedad, leases/claims, DeadLetter/UnknownDelivery nuevos, efectos/avisos duplicados, p95, CPU/RAM/pool y uso/costo IA. Los valores de alarma/ventana posterior aún requieren aprobación, no se inventan PASS ni thresholds operativos. Correlacionar panel, W/source y reservas/pedidos/Outbox con Q08/Q09; Sent ≠ Delivered/Read. No prueba comercial si sólo hay health.
7. Antes de reabrir tráfico normal: QA/operador aporta evidencia, propietario y auditor aprueban. Un fallo mantiene admisión/piloto detenidos; no reabrir por presión de terminar la ventana.

## 8. Rollback/fix-forward y abortos

**Prohibido volver a `0da524…` o cualquier imagen que reintroduzca F7-01.** La imagen histórica `306d7c8-perms1` tampoco queda aprobada como rollback por existir. No se pudo ensayar compatibilidad con imagen activa: PENDIENTE. Probar antes en ambiente aislado lectura/semántica de referencias/preguntas/contextos escritos por candidata, CAS, licencia/cotizaciones/recuperación de commit, esquema/epochs/xmin y supresión UnknownDelivery/humano. No mezclar escritores viejos/nuevos.

Si no hay digest anterior compatible **demostrado**, no ejecutar rollback automático: mantener cierre de admisión/piloto y fix-forward con nueva candidata aprobada, repitiendo gates. El propietario debe aprobar esa capacidad operativa antes del corte; no sustituye recuperación de datos. Una reversión de imagen no revierte reservas/pedidos confirmados, notificaciones ni mensajes entregados.

Abortos inmediatos: cruce de tenant/PII; operación sin consentimiento; doble efecto; F7-01; bot tras takeover; precios/horas/hechos inventados; imagen/SHA/config no coincidentes; migración o writer no autorizado; pérdida de datos/borrador con efecto; degradación persistente según umbral; costo sin control; sin aislamiento o restore/rollback/fix-forward ejecutable. Preservar evidencia sanitizada, no borrar ni reprocesar DLQ/UnknownDelivery para mejorar el reporte.

## 9. Decisión y siguiente paso

- **GO revisión local / READY_FOR_OPERATOR:** cuatro documentos, build/regresiones y comandos preparados; no nueva programación necesaria demostrada.
- **NO-GO release/piloto activo/comercial:** faltan CI mismo SHA, ARM64/codec/600–700, identidad/config/colas reales, Q01–Q10, política PDF, recuperación Firestore, evaluación independiente, proveedor/canal, presupuesto y aprobaciones operativas.
- Próximo paso: propietario/auditor revisan paquete y autorizan sesiones externas separadas. Si sólo se permite DEMO descartable, registrarlo expresamente; no anunciar venta ni escalar a clientes reales. **No Sprint 9 por inercia; ninguna API de Oracle fue modificada.**
