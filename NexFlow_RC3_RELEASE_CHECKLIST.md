# NexFlow RC-3 — candidato de release

Fecha: 2026-10-20, cierre final de preproducción. HEAD local/main verificado `ddea5d96791b2c0eff1a969d7f32be722201c0a7`, publicado según propietario; árbol inicial limpio. Ese commit ya contiene las correcciones anteriores admin/licencias/seguridad, pruebas, smoke y CI (19 archivos del cierre anterior). **Únicamente el nuevo ajuste ImageUploader montado, tres regresiones, umbral CI y actualización documental están en diff local sin commit**. No atribuir este diff nuevo a ddea5d9; no se verificó remoto ni se publicó desde esta sesión.

**CODE COMPLETE — VALIDACIÓN PREPRODUCCIÓN PENDIENTE. RELEASE NO-GO.** Esta decisión concluye desarrollo local autorizado; no aprueba Oracle, migraciones, servicios reales ni despliegue.

## Resultados históricos de Sprints 5–7 (preservados)

| Fase | Resultado |
|---|---|
| Sprint 5 | NF-10 corregido con decodificación/normalización real; NF-11 mitigado bloqueando borrado nuclear; timestamp n8n fuera de rango corregido. 191 pruebas backend dirigidas + 25 Chromium aprobadas. |
| Sprint 6 | 206 pruebas dirigidas de resiliencia aprobadas. Medición local de orquestador/RPC fake sin información sensible; mecanismos durables y observabilidad existentes conservados. |
| Sprint 7 | Builds, publish ARM64, lint, 702 pruebas backend y 222 frontend aprobadas. Caché generada fuera del índice; CI manual adicional, checklist y runbook preparados. CI no ejecutado. |

Las ejecuciones dirigidas se solapan con las suites finales: **no sumar esos conteos para inflar cobertura**.

## Validación del cierre anterior (histórica; resultados de este encargo en adenda final)

| Comando / evidencia | Resultado real |
|---|---|
| `dotnet restore backend/backend.slnx` | Exit 0, paquetes fijados. |
| `dotnet build backend/backend.slnx --no-restore` | Inicial: 0 errores/3 warnings históricos (CS8981 x2, CS8602 x1). Final incremental: 0 errores/0 warnings. |
| `dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore` | **708 aprobadas / 0 fallidas / 24 omitidas / 732 total**, 17 s. Baseline 702 intacto + 6 nuevas. TRX local ignorado rc3-final-recovery.trx. |
| Publish Release API `linux-arm64`, self-contained false, UseAppHost=false | Exit 0, 3 warnings históricos. No se ejecutó la API ni su imagen ARM64. |
| Publish Release Migrator `linux-arm64`, self-contained false, UseAppHost=false | Exit 0; artefacto generado, NO ejecutado, sin migraciones. |
| `dotnet build backend/tools/CodecSmoke/NexFlow.CodecSmoke.csproj --configuration Release -p:ApiPublishDir=<publish API>` | Exit 0, sin paquetes nuevos. Contra el SkiaSharp del API aprobado. |
| Smoke usando deps/runtimeconfig del API local | **PNG/JPEG/WebP encode/decode/normalize aprobados Windows X64**. `--require-arm64` rechaza Windows con exit 1 esperado. No es certificación Linux ARM64. Procedimiento de contenedor real en runbook. |
| `npm ci` | Exit 0, lockfile intacto. **4 vulnerabilidades altas** transitivas y avisos de scripts pendientes (ver riesgos); sin npm audit fix. |
| `npm run build` | Exit 0, dist generado; JS 710,37 kB, aviso >500 kB conocido. |
| `npm run lint` | Exit 0. |
| `node --import ./tests/reservations-loader.mjs --test tests/*.test.ts` | **83/83**, 0 fallidas/omitidas, 4,68 s. Repetición técnica JUnit 83/83, no sumar. |
| `node --test --test-concurrency=1 tests/*.test.mjs` | **145/145**, 0 fallidas/omitidas, 192,95 s. Seis nuevas regresiones admin/uploads; total distinto frontend **228**. |
| CI manual (ambos workflows existentes) | YAML/controles revisados localmente; csproj explícito, TRX mínimo 708 locales y 24 integraciones sin omisiones; Chromium requerido; JUnit frontend con descubrimiento/skip/fallo. **NO ejecutado: sin Run ID ni CI verde.** |

Las reproducciones rojas incluyeron borrado ejecutable y upload tardío aplicado (2 fallos browser); suspensión sin Reason exigido por HTTP; dos casos Reactivate dejando la licencia suspendida. Se corrigieron causas, sin cambiar expectativas. Restricción nuclear y permisos permanecen.

El console.error del fixture `PRIVATE_INTERNAL_ERROR_MUST_NOT_BE_SHOWN` es deliberado: prueba contención del ErrorBoundary y ausencia del detalle en UI. No es una regresión productiva. No se suprimieron advertencias ni debilitaron pruebas.

## Checklist funcional: evidencia local vs pruebas reales

| Área | Comprobado localmente | Antes de release (PENDIENTE OPERACIONAL) |
|---|---|---|
| Login/licencias | Contratos/permisos simulados, 401/403, entitlements, cambios de cuenta/licencia y cache identity en suites existentes. | Firebase Auth real, expiración/renovación, licencias activas y retiro en dos sesiones. No se simuló una certificación de identidad real. |
| Workspaces/usuarios | Aislamiento API/Firestore; browser de provisión, renovación, asignación, suspensión/reactivación, 503 y sesión tardía. Reactivación restaura licencia suspendida sin ampliar vigencia; vencida sigue inválida. Miembros consultados por WorkspaceId autenticado. Borrado nuclear bloqueado backend y sin acción ejecutable UI. | Aprovisionar dos workspaces/cuentas autorizadas; roles/membresías, suspensión/reactivación y acceso de usuarios reales. No existe una suite browser integral de administración de usuarios. |
| Productos/servicios/categorías | Repositorios/integridad, precio histórico de pedidos, soft delete, modales PRODUCT/SERVICE/SHARED, permisos y caché. | CRUD con roles reales, índices/cuotas Firestore, referencias y cambios simultáneos. |
| Sedes/horarios | 83 unitarias frontend + browsers de horarios/reservas; preservación de borradores, sin escrituras automáticas y cambio de sede/tenant. | Persistencia real y dos sesiones con permisos distintos. |
| Reservas manual/WhatsApp | Motor común, fechas/UTC, replay, xmin configurado, validadores, transiciones y Outbox en pruebas locales. | **24 integraciones PG/Emulator**, FOR UPDATE/SSI/xmin reales y funcional móvil autorizado. No confundir SQLite con PostgreSQL. |
| Pedidos/solicitudes | Assistant/Rc3Sprint4Commercial: IDs/precios históricos, negocio único, ACK perdido, notificación idempotente y recuperación por replay controlado. | Confirmar pedidos/solicitudes reales y avisos; no hay browser dedicado de pedidos. No reenviar un turno entero para reparar solo una notificación. |
| Conversaciones/takeover | Inbox conserva panel sano; fromMe/duplicados/handoff/supresión de IA, reconocimiento una vez, UnknownDelivery sin reenvío. | Webhook→inbox SQL→contexto→outbound→móvil→web con cuentas autorizadas. |
| Recuperación | Redis no manda sobre Firestore; leases/restart lógico, dead letters, timeouts y errores de proveedores simulados. | Pool/latencia/recursos/colas reales; recuperación y alertas tras caídas autorizadas en staging. |
| PDFs PRODUCT/SERVICE | Generación/cuota/cancelación/FAILED, callbacks HMAC, deadline y PDF anterior conservado; invalidación durable. | n8n/Gotenberg/Cloudinary reales, PDFs válidos separados y callback dentro del plazo. No reinyectar 4 Outbox antiguos. |
| Notificaciones | Identidad estable workspace+módulo+operación, una inserción ante ACK perdido, fallo de aviso no duplica negocio. | Agotamiento queda terminal durable para revisión; medir alertas. Reparación administrativa de aviso por ID solo tras comprobar negocio y deduplicación, sin replay ciego. No se promete entrega universal. |
| Webhooks | Evolution firma derivada por instancia/owner/fromMe/dedup; n8n timestamp/HMAC/scope/WorkspaceId/GenerationId/SourceHash y late callbacks. | Transición legacy: demostrar key por instancia en cada conexión y **después**, por operación separada, desactivar legacy. Nunca cambiar antes de verificar proveedor. |
| Uploads | JPG/PNG/WebP reales, MIME/extensión/envelope, decoder, corrupción/truncado, límite declarado y stream, workspace y cancelación. | Smoke Linux ARM64 del decoder y upload de imágenes/PDF con Cloudinary real; medir recursos. |

## Cambios y garantías

- Imagen: máximo 5 MB de entrada y salida normalizada, 16 MP, estática, 2 decodificaciones simultáneas/proceso. Lectura en bloques 8192, nunca confiar solo en Length. Se rechazan discrepancias y sufijos fuera del envelope; se suben píxeles recodificados sin metadatos ni nombre original. `secureUrl` y flujo Cloudinary conservados. No se garantiza preservar animación, EXIF/GPS o calidad binaria original: su exclusión es parte de la frontera segura. Cancelaciones legítimas propagadas.
- Decoder final: [SkiaSharp 4.153.1](https://www.nuget.org/packages/SkiaSharp/4.153.1) y [Linux NoDependencies 4.153.1](https://www.nuget.org/packages/SkiaSharp.NativeAssets.Linux.NoDependencies/4.153.1), licencia MIT, sin instalaciones de SO/Docker. Conservar atribuciones al redistribuir. Se descartó ImageSharp al detectar exigencia de clave Release; no permanece referenciado ni se omitió su control de licencia.
- Borrado nuclear: `TenantDeletionSafety.IsEnabled` devuelve false sin bandera ni excepción administrativa. Handler/endpoint devuelve Workspace.DeletionUnavailable (503), scheduler rechaza antes de SQL, worker no registrado en host. Las intenciones antiguas se **retienen**, no se completan ni borran. Workspaces ya Deleting permanecen protegidos; no reactivarlos automáticamente.
- Recursos externos: PostgreSQL/Redis/Firestore/Evolution/Cloudinary/n8n/Gotenberg son servicios compartidos. Instancia Evolution reservada y prefijo de assets por workspace son identidades exclusivas, pero URLs/importaciones y dependencias no justifican purgas generales. No borrar recursos externos hasta tener inventario, ownership y limpieza durable certificada. Redis sigue cache únicamente; sus datos no autorizan recuperar un tenant eliminado.
- Migración nueva: **ninguna** en 5–7. Sigue pendiente la existente `20261019170831_CoordinateReservationResourcesAndReuseCancelledSlots`; no aplicada aquí.
- Higiene: no se encontraron bin/obj/node_modules/dist versionados. `frontend/.firebase/hosting.ZGlzdA.cache` retirado del índice en `88db9e0`, con ignore publicado en `df5cfac`; caché local ignorada; añadido ignore. Dos configs Hosting idénticos se conservan por compatibilidad/tests; comando canónico explícito en runbook. No se borraron migraciones/entidades.

## Medición y límites

TRX local de Sprint 6 (ignorado por Git): 32 lotes/64 turnos después de warmup, orquestador real y SDK con RPC en memoria, dos fixtures de workspace/proveedores fake. p50/p95/p99 **0,6664/2,1877/6,1303 ms por lote de dos turnos**; 128 read RPC attempts y 64 commit RPC attempts; cero llamadas pagadas, reservas o pedidos. No son lecturas facturadas, rendimiento Firestore, capacidad de concurrencia o SLO de Oracle.

No se optimizó contexto a costa de autoridad durable. Timeouts inspeccionados por defecto: Groq 5 s, Gemini 12 s, n8n HTTP 30 s, trabajo PDF 5 min. Umbral de cola demorada existente: 5 min, diagnóstico acotado/cooldown de aviso 1 h. El runbook incluye objetivos provisionales a contrastar, no valores prometidos.

## Riesgos y puertas de release

| Prio. | Estado / acción requerida |
|---|---|
| P0 potencial, **mitigado en candidato** | Commit remoto en vuelo tras purge no certificado; borrado nuclear deshabilitado. Si alguien elimina la restricción, vuelve a ser bloqueante de integridad; no habilitarlo con un workflow verde. |
| P1, bloqueante RELEASE | 24 integraciones reales no ejecutadas: migración, locks/SSI/xmin, fencing y tenant/pool requieren evidencia. No duplicar harness ni presentarlas como verdes. |
| P1, bloqueante RELEASE | Imagen/configuración efectiva Oracle, backup/restauración, rollback compatible y proveedores/credenciales/health reales no verificados. |
| P1, bloqueante RELEASE | Funcionales autorizadas de dos tenants, webhook por instancia antes de retiro legacy, PDF y transporte real; Linux ARM64 debe cargar el codec. |
| P2 | Terminales Failed/DeadLetter/UnknownDelivery requieren revisión; sin reenvío indiscriminado. Medir backlog, alertas y costes bajo carga. |
| P2, dependencia por revisar | npm audit: 4 altas en cadena firebase → firestore → grpc-js <=1.13.5 (GHSA-m9gg-hp2v-232j; GHSA-f596-whhp-79r4). Frontend usa app/auth, no Firestore/grpc Node; bundle sin getAuthContext/grpc-js. No hay exposición runtime demostrada; requiere revisión/aceptación de dependencia. La solución automática propone downgrade mayor Firebase 9.14.0 y no se aplicó. No declarar cero vulnerabilidades. |
| P2 | Advertencias históricas C# y bundle 710,37 kB, sin errores de build; no se hicieron refactors para ocultarlas. |

No se identificó un P0 local conocido sin mitigación en la funcionalidad habilitada. Eso **no** certifica ausencia global de defectos ni resuelve la causa operacional histórica del incidente.

Antes de cambiar RELEASE a GO: aprobar diff, generar SHA exacto limpio/digests, ejecutar las 24 integraciones sin skips, aplicar runbook en staging aislado autorizado, verificar respaldo/restauración y rollback compatible, funcionales reales y revisión del riesgo residual. No ejecutar workflows/Oracle/migraciones/deploy por este documento: requieren autorización independiente.

Inventario exacto de archivos, decisiones por sprint y resultados: `NexFlow_RC3_PROGRESS.md`. Procedimiento: `NexFlow_RC3_DEPLOY_RUNBOOK.md`.

## Cierre final de preproducción — evidencia vigente (2026-10-20)

Solo nuevo defecto: ImageUploader seguía ocupado al cambiar identidad sin desmontarse. Dos reproducciones rojas demostradas; parche de estado/sesión/abort y tres regresiones (usuario, workspace, finally antiguo durante nueva carga). No se rehace el cierre publicado. SuperAdmin/Reason/reactivación/vigencia/entitlements/nuclear pasan las regresiones existentes; sin cambio backend en este encargo.

| Validación NUEVA | Resultado |
|---|---|
| Restore solución backend | Exit 0. |
| Build solución backend --no-restore | Exit 0, **0 errores / 3 warnings históricos** (CS8981 x2, CS8602 x1). |
| Tests csproj explícito --no-restore | **708 aprobadas / 0 fallidas / 24 omitidas / 732 total**, 12 s. TRX rc3-preproduction-final.trx local ignorado. |
| npm ci | Exit 0; versiones/lockfile intactos, 4 altas y avisos de scripts pendientes. |
| npm run build / npm run lint | Ambos exit 0; dist generado, JS 710,55 kB (>500 aviso), CSS 50,73. |
| TypeScript (loader existente) | **83/83**, sin fallos/omisiones, 4,67 s. |
| Navegador/Hosting completo --test-concurrency=1 | **148/148**, sin fallos/omisiones, 153,34 s (última corrida JUnit de la versión final; primera corrida 184,05 s, no sumar). Total frontend distinto **231**, baseline 228 conservado + 3. |
| Smoke codec publicado contra API local | Build 0 errores/0 warnings; PNG/JPEG/WebP 3 PASS **Windows X64**. ARM64 real no ejecutado. Publish ARM64 del cierre previo sigue siendo evidencia histórica. |
| Workflows | YAML y AST PowerShell revisados; permisos mínimos/manual, mínimo 708/83/148; CI remoto **NO EJECUTADO**, sin Run ID. Bash local es alias inaccesible, validación en shell Linux pendiente; no se instala ni se bloquea el resto por ello. |

### Evaluación concreta de npm (4 alertas altas de una cadena, no cuatro fallos independientes)

| Paquete instalado | Papel / alcance |
|---|---|
| firebase 13.0.0 | Dependencia directa; app/auth usados. Alerta transitiva por Firestore. |
| @firebase/firestore-compat 0.4.15 | Transitiva; no importada por aplicación ni incluida en chunks emitidos. |
| @firebase/firestore 4.18.0 | Transitiva; entrypoints Node usan grpc-js; entrypoint browser separado. No importado por aplicación ni incluido en chunks emitidos. |
| @grpc/grpc-js 1.9.16 | Paquete raíz vulnerable señalado; dependencia Node de Firestore ~1.9.0. No es parte del backend .NET ni del bundle browser. |

Grafo real inspeccionado con plugin efímero generateBundle de Vite (sin modificar config): 287 módulos emitidos, 2 Firebase auth, **0 Firestore y 0 grpc-js**; mismo hash/tamaño de dist que el build normal. Fuentes usan exclusivamente firebase/app y firebase/auth. Hosting publica solo dist; Node/node_modules no se sirven ni despliegan como servidor.

El [aviso alto del proveedor](https://github.com/grpc/grpc-node/security/advisories/GHSA-m9gg-hp2v-232j) requiere autenticación de servidor mediante getAuthContext con requireClientCertificate=false. El [aviso bajo de errores de handlers](https://github.com/grpc/grpc-node/security/advisories/GHSA-f596-whhp-79r4) afecta servidores grpc-js que exponen excepciones. Estas rutas de servidor Node no existen en el runtime actual de NexFlow; no se encontró camino aplicable en este artefacto. Inferencia limitada al código/target inspeccionado, no afirmación de que el paquete instalado esté corregido.

Metadata oficial npm: Firebase 13.0.0 / Firestore latest 4.18.0 aún declaran ~1.9.0; parche grpc 1.13.7 disponible para ambos avisos pero **fuera de ese rango**. npm propone downgrade mayor Firebase 9.14.0. Ninguno se fuerza sin pruebas de compatibilidad del SDK Node ajeno al uso actual. Sin cambios de dependencias/overrides/lockfile. Mitigación: app/auth y Hosting estático, sin servidor JS grpc/SSR ni frontend Firestore; mantener escaneo, adoptar parche upstream compatible cuando exista y reevaluar antes de introducir esos runtimes. Riesgo de dependencia **P2 residual** para aceptación explícita; npm audit continúa con 4 altas, no se oculta.

### Puertas de GO/NO-GO vigentes

- [x] Código local y contratos corregidos, builds/lint y 708 + 231 pruebas locales sin fallos.
- [x] Permisos, aislamiento, Reason, vigencia, idempotencia y protección nuclear conservados.
- [ ] Publicar por propietario SHA final aprobado y limpio que incluya este nuevo diff; fijar artefactos/digests (ddea5d9 no lo incluye).
- [ ] Ambos workflows con SHA final, Run ID, TRX/JUnit; 24 integraciones PG/Emulator **ejecutadas sin omisiones**.
- [ ] Imagen Linux ARM64 exacta y smoke codec de contenedor aprobado.
- [ ] Inventario/esquema/history Oracle solo lectura; aprobación de migración pendiente y ventana sin escritores antiguos.
- [ ] Backup privado + restauración aislada, rollback de aplicación compatible con epochs/fences/UpdateTime.
- [ ] Health y funcionales autorizadas de dos workspaces: WhatsApp/Evolution/Inbox/reservas/pedidos/PDF PRODUCT y SERVICE/licencias, recursos compartidos intactos.
- [ ] Evidencia real incidente/4 PDF históricos y retiro legacy coordinado; sin replay ciego ni UnknownDelivery reenviado.

**P0 potencial mitigado:** nuclear deshabilitado y sin bypass. **P1 bloqueantes de release:** integraciones/ARM64/CI/configuración/migración/backup y prueba operacional real pendientes. **P2:** dependencia transitiva sin ruta runtime identificada, warnings/bundle y métricas/coste en carga. Ninguna migración nueva/aplicada, infraestructura/secretos/servicios modificados, commit/push ni despliegue.

**CODE COMPLETE / READY FOR STAGING (validación aislada) / RELEASE NO-GO.** Listo el alcance local; no autorización de despliegue productivo. Inventario de este encargo: ImageUploader.tsx, navigation-browser.test.mjs, fixtures/navigation-preview.tsx, rc3-release-checks.yml (umbral 148) y los tres documentos existentes. Backend y migraciones sin cambios. Ver runbook para siguientes operaciones seguras; no iniciar otro sprint.
