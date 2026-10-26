# F7 — Piloto y release: historial y cierre local

**Actualización posterior al hotfix autorizado:** G7.1 aprobado localmente; preparación documental F7 completada. GO para cerrar desarrollo local bajo el contrato vigente y preparar la verificación operativa. Activación y comercial: NO-GO hasta aportar evidencia externa y autorización. La candidata es el árbol local basado en 883370715122068fdb668872537590e14c92a25f más el diff descrito en 08; aún no tiene un nuevo SHA publicado. El registro de parada siguiente se conserva como evidencia histórica y queda sucedido por las secciones finales.

## Registro original de parada

Referencia local: `85d349ac5690fbc3dcd3d97b72cf0c3a45e2ff23`, leída en `.git/HEAD` y `.git/refs/heads/main`. No se ejecutó Git. Esa lectura no certifica árbol limpio, remoto ni imagen activa en Oracle. La prueba añadida y estos informes son cambios locales posteriores a esa referencia, sin nuevo SHA publicado.

## Gate de parada activado

**NO-GO técnico local. No activar ni desplegar esta candidata.** Durante la auditoría de consentimiento se reprodujo F7-01, P1: después de revocarse la licencia durante un envío, el borrador conserva productos pero pierde la huella del resumen; restaurada la licencia y actualizado el precio, un nuevo envío crea la solicitud con importes que no se volvieron a mostrar.

La instrucción F7 exige documentar un P0/P1 nuevo y detenerse sin corregirlo. Por ello este documento registra una preparación **incompleta**, no un runbook ejecutable aprobado. El diagnóstico, la prueba y su resultado exacto están en `08_CIERRE_AGENT_RUNTIME_4.md`. No se modificó código productivo.

| Decisión | Estado y motivo |
|---|---|
| GO técnico local | **NO-GO**: F7-01 reproducido; regresión general F7 pendiente |
| GO para preparar piloto | **PENDIENTE**: esta preparación queda suspendida; versión operativa y rollback no verificados |
| GO para activar piloto | **NO-GO**: P1 abierto, autorización, aislamiento, respaldos y métricas operativas pendientes |
| GO comercial | **NO-GO**: además faltan corpus independiente, revisión humana, proveedores/canal y costos reales |

## Primer paso seguro y reanudación

El primer paso seguro es auditar F7-01 y autorizar por separado su corrección mínima. No se requiere acceder a Oracle para reproducirlo. Conservar la prueba roja y la evidencia; no debilitar expectativas para obtener verde.

Después del arreglo autorizado: ejecutar la prueba hasta verde, cubrir revocación/restauración, resumen inexistente y recuperación de un efecto ya persistido; repetir build, contrato vigente, históricos originales completos, F3 v2 y regresiones pertinentes. Congelar una nueva candidata y vincular evidencia al nuevo SHA/árbol. No atribuirle resultados de otro commit.

Sólo después, el operador podrá aportar evidencia sanitizada de la imagen API activa: referencia, ID/digest, arquitectura y etiqueta de revisión si existe. Una etiqueta no sustituye al digest ni prueba por sí sola la correspondencia de fuentes. No volcar variables de entorno, Compose interpolado íntegro, teléfonos, mensajes ni claves al informe.

## Preflight operativo aún pendiente

No se ejecutó ni se certificó ninguna de estas operaciones:

1. Comparar la candidata congelada con la versión realmente activa y revisar incompatibilidades de contratos, contexto y esquema. No asumir ausencia de migraciones sólo porque F7 no las modifica.
2. Revisar Dockerfile, contexto de construcción y archivos/overrides de Compose efectivos. Preparar entonces comandos exactos con sus rutas y nombre real del servicio; no inventar que el servicio se llama `api`.
3. Construir Linux ARM64 con versiones/digests de base y dependencias identificados, registrar digest y arquitectura; verificar usuario no root, bibliotecas nativas y codecs mediante la prueba existente, si corresponde. ARM64 y SkiaSharp no quedan aprobados por un build .NET de Windows.
4. Confirmar salud autorizada de PostgreSQL, Redis, Firestore, Evolution, n8n y Gotenberg; workers, cola, Outbox y conectividad. No reiniciar servicios compartidos para probar salud.
5. Confirmar backup y restauración **aislada** comprobable de los almacenes durables, alcance y fecha del respaldo, acceso restringido y procedimiento de restauración; no restaurar sobre producción para ensayar.
6. Preparar rollback exclusivamente del binario/imagen API compatible, con digest anterior retenido, configuración compatible y control de escritores. Un binario antiguo que ignore referencias conversacionales nuevas puede perder garantías: no dar por segura la convivencia de escritores.
7. Con autorización separada, reemplazar sólo el servicio API y comprobar salud y pruebas autorizadas. No ejecutar `docker compose down`, `prune`, migrator, borrado de volúmenes ni recreación automática de dependencias.

**Rollback:** no borra ni revierte reservas/pedidos ya confirmados, notificaciones ni mensajes enviados. No reprocesar DeadLetters ni reenviar UnknownDelivery para simular una reversión. Si no existe rollback compatible demostrado, detener la activación.

## Piloto propuesto, no ejecutado

Workspace aislado autorizado, contactos de prueba consentidos y presupuesto aprobado. Mismo runtime para clínica multisede, hotel y tienda; ninguna expectativa privada en este repositorio.

| Etapa | Evidencia exigida antes de avanzar |
|---|---|
| Información | Perfil público, sedes/horarios, servicios/productos, precios, FAQ y referencias correctos; revocación sin lectura restringida; sin datos de otro workspace |
| Reservas | Primero consultas/borradores sin creación. Después, sólo con autorización explícita, reserva aislada con resumen vigente, UTC/hora local, persistencia y efecto único |
| Pedidos | Solicitud ficticia consentida PendingReview, importes revisados, aviso durable único y recuperación; sin cobro, stock confirmado ni recogida garantizada |
| Control humano | Takeover suprime bot pendiente; devolución de control por mecanismo existente. Un transporte ya iniciado no se puede retirar físicamente |
| Proveedores | Groq/Gemini y fallback, 429, timeout, JSON inválido y presupuesto; fallos inducidos únicamente en entorno aislado, sin afectar otros negocios |
| Calidad y carga | Evaluador independiente, corpus privado de al menos 30 conversaciones inéditas y multitur, rúbrica humana, dos o más workspaces y carga acotada autorizada |

Rúbrica humana separada: comprensión, continuidad, orientación, presentación y operaciones. Puntuar claridad/utilidad y registrar referencias correctas, repeticiones, borrador conservado, hechos respaldados, tarea terminada o derivación segura. Una suite de mocks no demuestra calidad de Gemini/Groq ni entrega WhatsApp.

## Observabilidad, costos y parada

Antes de activar, verificar recolección/retención de los Meters y logs sanitizados existentes: HTTP por proveedor/tarea/rol, fallback, usage conocido/desconocido, presupuesto, latencia de fases/cola, aclaraciones repetidas, lecturas/escrituras de contexto y efectos durables únicos. No interpretar BusinessResult como número de pedidos nuevos ni sumar duraciones anidadas. Sin exporter operativo verificado no afirmar monitorización activa.

Costo por conversación = costo de tokens realmente informados por modelo/intento a tarifa verificada + costos atribuibles de infraestructura/almacenamiento/canal. Separar usage desconocido; no valorarlo como cero ni sumar Total a Input+Output. Multiplicar el promedio de una muestra representativa por 1.000 e indicar ventana, mezcla local/IA/fallback y cobertura. Tarifas, consumo y costos reales siguen pendientes. Definir presupuesto total de piloto, alarma y límite de participantes antes de ejecutarlo; no cambiar cuotas comerciales existentes.

Parada inmediata ante cruce de workspace, operación sin consentimiento vigente, duplicación, bot después de takeover, pérdida de contexto con efecto indebido, invención comercial, canal gravemente degradado, costo sin control o rollback incompatible. F7-01 ya activa esta condición. Se requiere auditoría humana antes de reanudar; no hay autorización de despliegue.

## F7-01 — Reproducción, corrección y verificación

La evidencia roja original de agent-runtime-f7/order-consent-reproduction.trx permanece: un resumen PEN 5.00 terminó en una solicitud PEN 6.00 sin nueva revisión. No se sobreescribió el TRX ni se modificaron sus aserciones.

OrderFlow exige ahora objetivo ORDER y coincidencia exacta de ORDER_REVIEW/ORDER_CONFIRM con Quote.Fingerprint; ORDER_COMMIT sólo admite la fuente original en recuperación. La acción debe ser envío explícito o afirmación inequívoca sobre ORDER_CONFIRM coincidente. Sin referencia válida se guarda ORDER_CONFIRM con resumen actual y pregunta concreta, sin crear ni notificar. Una revocación observada invalida la revisión; artículos intactos. Un efecto ya durable conserva TryResume/NotifyOnce aun revocada la licencia, sin segunda compra. No cambia el repositorio ni PendingReview.

G7.1: reproducción 1/1; comercio/durabilidad inicial final 153/153; la batería ampliada final incorpora además el checkpoint coincidente y mantiene todos esos casos aprobados. Build final 0 errores/0 advertencias incrementales. Detalle, intentos intermedios y compatibilidad en 08. Sin nuevo P0/P1 demostrado pendiente en estas verificaciones; no certifica servicios reales.

## Preflight exacto del operador — preparado, NO ejecutado

1. Auditar diff, congelar/publicar por el propietario la revisión mediante operación separada. RELEASE_SHA identifica esa revisión completa: 8833707 es la base, no el hotfix publicado. Recibir manifiesto/hash de fuentes, TRX y aprobación; no atribuir CI de otro SHA.
2. Recibir identidad sanitizada de la API activa. API_CONTAINER proviene del inventario real, no de un nombre supuesto. Con autorización futura de lectura:

~~~bash
test -n "$API_CONTAINER"
docker inspect "$API_CONTAINER" --format '{{.Config.Image}} {{.Image}} {{.State.StartedAt}}'
docker inspect "$API_CONTAINER" --format '{{index .Config.Labels "com.docker.compose.project"}}'
docker inspect "$API_CONTAINER" --format '{{index .Config.Labels "com.docker.compose.service"}}'
docker inspect "$API_CONTAINER" --format '{{index .Config.Labels "com.docker.compose.project.config_files"}}'
~~~

Conservar ID/digest y revisión OCI si existe; sin etiqueta exigir provenance adicional. No exportar inspect completo ni ENV. Comparar fuentes/contratos/contexto/escritores contra la versión efectiva y revisar historial EF mediante lectura privada. El hotfix no toca modelos/migraciones; eso no demuestra que la imagen activa tenga todo el esquema. Si hay pendientes, parar: este procedimiento NO autoriza migrator.

3. Dockerfile comprobado: backend/Dockerfile, contexto backend, target final, Release y USER $APP_UID de la base. Target migrator separado: no usarlo en este release. Program.cs ejecuta Migrate/Seeder sólo en Development: exigir Production. backend/.dockerignore excluye credenciales, .env, bin/obj y TestResults; no incorporar secretos al paquete.

En builder aislado autorizado con fuentes exactas y Docker existente, comandos futuros:

~~~bash
test -n "$RELEASE_SHA"
test -n "$API_BUILD_METADATA"
docker buildx build --platform linux/arm64 --target final \
  --label "org.opencontainers.image.revision=$RELEASE_SHA" \
  -f backend/Dockerfile -t "nexflow-api:$RELEASE_SHA" \
  --metadata-file "$API_BUILD_METADATA" --load backend
docker image inspect "nexflow-api:$RELEASE_SHA" \
  --format '{{.Id}} {{.Os}} {{.Architecture}} {{.Config.User}}'
~~~

Exigir Linux/arm64 y usuario no root comprobado. Bases sdk/aspnet :10.0 son móviles: registrar digests resueltos/dependencias y retener el artefacto por ID/digest. **No hay reconstrucción bit a bit reproducible certificada con tags móviles.** No reconstruir un tag suponiendo identidad. Pinning de bases queda para revisión operativa; Dockerfile no se modifica aquí.

4. Smoke existente: backend/tools/CodecSmoke; procedimiento exacto de extracción de SkiaSharp.dll de contenedor detenido y compilación contra ESA imagen en NexFlow_RC3_DEPLOY_RUNBOOK.md, sección 5.1. No copiar bibliotecas nativas del host. API_IMAGE debe ser el ID/digest aprobado; SMOKE_DLL la DLL resultante. Ejecución futura sin host API, red ni credenciales:

~~~bash
test -n "$API_IMAGE"
test -f "$SMOKE_DLL"
docker run --rm --pull=never --platform linux/arm64 --network none --read-only \
  --cap-drop ALL --security-opt no-new-privileges --pids-limit 64 --memory 512m --cpus 1 \
  --tmpfs /tmp:rw,noexec,nosuid,size=16m -e DOTNET_EnableDiagnostics=0 \
  --mount "type=bind,source=$SMOKE_DLL,target=/app/NexFlow.CodecSmoke.dll,readonly" \
  --entrypoint dotnet "$API_IMAGE" exec \
  --runtimeconfig /app/NexFlow.API.runtimeconfig.json \
  --depsfile /app/NexFlow.API.deps.json /app/NexFlow.CodecSmoke.dll --require-arm64
~~~

Aceptar sólo exit 0 y PASS PNG/JPEG/WebP Linux Arm64. Registrar emulación si existe; no prueba rendimiento Oracle. **ARM64/codec PENDIENTE**; no Docker ejecutado aquí.

5. No se localizaron Compose en raíz/backend inspeccionados. Overrides efectivos pendientes del operador: rutas/orden, proyecto y servicio sanitizados. COMPOSE_ARGS debe ser un array Bash con cada -f real en orden y -p verificado; API_SERVICE del inventario. Revisar privadamente image/digest, red, volúmenes, usuario, puertos y configuración sin publicar secretos:

~~~bash
docker compose "${COMPOSE_ARGS[@]}" config --services
~~~

WorkspaceStyles no es un flag de autorización de piloto por tenant. No existe aquí un kill switch nuevo por workspace: aislar entorno/participantes mediante acceso existente, sin activar otros negocios por suposición.

6. Backup privado PostgreSQL + documentos Firestore + objetos necesarios, alcance/fecha/hash y restauración comprobada en destinos aislados. pg_dump no respalda Firestore; listar un backup no prueba restaurabilidad. Sección 4 del runbook sólo con autorización de datos y destinos verificados. No copiar datos personales al repo ni ejecutar backup/restore desde esta sesión.
7. Health HTTPS autorizado: /health/live y /health/ready, HTTP y cuerpo; Postgres/Firestore sanos y Redis degradado no aceptado a ciegas. Evolution/n8n/Gotenberg, workers/colas y entrega se verifican aparte. Health no demuestra WhatsApp. Validar requisitos en RuntimeConfiguration sin exportar valores. No rotar secretos ni retirar webhook legacy en esta operación.
8. Retener digest API previo compatible con CAS, referencias y epochs/fences. Preparar admisión/drenaje de todos los escritores NexFlow; workers están integrados en el host API. No rolling mixto automático con escrituras. Si no hay rollback compatible demostrado: NO-GO.
9. Sólo con autorización posterior, overrides revisados, digest ya fijado y gates aprobados, reemplazar exclusivamente el servicio identificado:

~~~bash
docker compose "${COMPOSE_ARGS[@]}" up -d --no-deps "$API_SERVICE"
~~~

Rollback: mismo procedimiento con digest compatible previo, sin Down ni restauración automática. No revierte operaciones comerciales. Si el único rollback reintroduce F7-01, detener admisión/piloto y corregir hacia delante. No down/prune, migrator, borrado de volúmenes, reinicio compartido, reproceso DeadLetters o reenvío UnknownDelivery.

## Condiciones vigentes para activar el piloto

Respaldos restaurados aisladamente, identidad/ARM64, salud/observabilidad, módulos y cuotas verificados; presupuesto, responsable de parada, contactos y efectos autorizados expresamente. Las etapas propuestas en el registro original siguen vigentes, sin ejecución.

Evaluación independiente fuera del repo: mínimo 30 conversaciones nuevas multitur, SHA congelado, expectativas reservadas a otro evaluador, rúbrica separada de comprensión/continuidad/orientación/presentación/operaciones, y resultados brutos conservados. Completar proveedores/canal/costos reales; las 984 regresiones actuales simuladas no los sustituyen.

**Cierre local: GO bajo contrato vigente, con incompatibilidades históricas visibles. Preparar verificación operativa: GO documental. Activar piloto: NO-GO por evidencia/autorización pendiente. Comercial: NO-GO.** Ningún procedimiento operativo anterior fue ejecutado.
