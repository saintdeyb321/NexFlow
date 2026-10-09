# NexFlow — Informe UX-06B0

Fecha: 2026-10-08. Certificación local en Windows; sin acceso a Oracle, Firebase ni servicios de producción.

## Dictamen

**NO-GO para preparar UX-06B1. P1 PENDIENTE de certificación. Pérdida de borradores RESUELTA.**

La regresión de desmontaje se reprodujo antes de modificar componentes y quedó cubierta por pruebas de navegador. Los controles permanecen bloqueados y las horas ocultas durante la revalidación. Persiste un fallo real y anterior a UX-06B0 en la equivalencia histórica Windows/IANA: no se omitió ni se relajó esa prueba. Este informe no autoriza un despliegue ni certifica producción.

## Referencia y alcance

- Rama inicial y final: `main`. HEAD verificado: `24f989d1c04c9359df29bb1aa215f8c85450d311`, coincidente con el esperado.
- Estado inicial: únicamente `docs/UX_UI_SPRINT_06B0_PREFLIGHT.md` sin seguimiento. Se conservó sin editar.
- Se leyeron `AGENTS.md` y exclusivamente el ticket UX-06B0 como documentación de alcance.
- Se reutilizó el P1 existente. No se modificaron backend, contratos API, autorización, CORS, middleware, datos, migraciones, dependencias, infraestructura, Evolution ni WhatsApp.
- Sin commit, push, reset, despliegue, consulta de credenciales ni cuentas reales. La revisión final se limitó al diff modificado.

Archivos cambiados en esta tarea:

| Área | Archivo | Cambio |
| --- | --- | --- |
| Frontend | `frontend/src/features/reservations/pages/ReservationsPage.tsx` | Conserva la agenda montada durante revalidaciones; oculta agenda y bloquea operaciones. Descarta borradores ante 401/403. |
| Frontend | `frontend/src/features/reservations/components/CreateReservationModal.tsx` | Conserva campos seguros, bloquea formulario/submit y oculta slots; recalcula fecha y limpia selección cuando cambia la zona. |
| Frontend | `frontend/src/features/reservations/components/EditReservationModal.tsx` | Conserva el borrador con la misma zona; oculta fecha/hora durante revalidación y exige una hora nueva tras cambiar la zona. |
| Pruebas frontend | `frontend/tests/reservations-browser.test.mjs` | Once regresiones dirigidas de borradores, errores, cambios de zona e identidad. |
| Fixture frontend | `frontend/tests/fixtures/reservations-api.ts` | Marca respuestas completadas para esperar explícitamente una respuesta tardía, sin sleeps añadidos. |
| Documentación | `docs/UX_UI_06B0_PREFLIGHT_REPORT.md` | Evidencia, bloqueos y planificación de despliegue/rollback. |

Backend: ningún archivo modificado. El candidato incluye cambios locales sin commit sobre el HEAD indicado; ese HEAD por sí solo no identifica el resultado completo de esta tarea.

## Reproducción y corrección

Antes del cambio, `context.isFetching` devolvía `LoadingState` y desmontaba `ResolvedAgenda`. Dos pruebas abrieron Nueva reserva/Reagendar, introdujeron datos, provocaron pérdida/recuperación de foco mediante el `focusManager` real de TanStack Query y esperaron el inicio de la petición retardada del fixture. Ambas fallaron con `Focus revalidation unmounted the create draft` / `Focus revalidation unmounted the reschedule draft` (0/2 aprobadas).

Después del cambio, las mismas aserciones pasan (2/2), sin relajarlas. Se mantiene el montaje mientras existe contexto previo de la misma identidad, pero no se muestra la agenda ni los slots y no se permite confirmar, reagendar, cancelar o completar durante la revalidación. Loading/error usan los componentes accesibles existentes. También se comprobó que un submit programático no envía una reserva con contexto sin verificar.

Con la misma zona se conservan nombre, teléfono, fecha, hora de Reagendar y semana seleccionada. Si la zona cambia, Nueva reserva conserva cliente/sede/servicio, recalcula la fecha al día actual de la nueva zona y borra el slot. Reagendar recalcula la fecha desde el instante de la reserva y borra la hora propuesta. Ambos avisos exigen revisión explícita; no se envían operaciones automáticamente.

Un error 503 conserva el borrador bloqueado y permite reintentar. 401/403 lo descartan. Logout, cambio de usuario/workspace o revocación de permisos mantienen la limpieza de sesión/caché existente; las respuestas retardadas no reabren modales ni restauran datos de la identidad anterior. Los borradores siguen siendo estado local de React y no se persisten en almacenamiento del navegador.

## Comandos y resultados reales

Desde la raíz del repositorio:

```text
dotnet build backend/backend.slnx --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false --filter "FullyQualifiedName~ReservationsApiTests|FullyQualifiedName~ReservationsRepositoryTests" --verbosity minimal
git diff --check
```

Desde `frontend`:

```text
node --test --test-concurrency=1 --test-name-pattern="UX-06B0" tests/reservations-browser.test.mjs
node --import ./tests/reservations-loader.mjs --test tests/reservations-agenda.test.ts
node --test --test-concurrency=1 tests/reservations-browser.test.mjs
npm run build
npm run lint
```

| Validación | Resultado observado |
| --- | --- |
| Backend build, configuración predeterminada del ticket | PASS, 0 errores, 3 advertencias, 21,68 s. No hubo restore. |
| ReservationsApiTests | 52 aprobadas / 1 fallida / 53 totales. |
| ReservationsRepositoryTests | 5 aprobadas / 5 totales. SQLite en memoria. |
| Backend dirigido conjunto | 57 aprobadas / 1 fallida / 58 totales; 0 omitidas; salida 1. |
| Regresión navegador antes de corregir | 0/2; ambas fallan por desmontaje del borrador. |
| Mismas regresiones después de corregir | 2/2; salida 0. |
| reservations-agenda.test.ts | 32/32; 0 omitidas; salida 0; 1.650,2545 ms. |
| reservations-browser.test.mjs final | 35/35, incluidas 11 nuevas; 0 omitidas; salida 0; 21.486,8012 ms. |
| Frontend build | PASS; TypeScript y Vite producción; salida 0. |
| Frontend lint final | PASS; sin mensajes de error/advertencia; salida 0. |
| Diff | `git diff --check` sin errores. Avisos de normalización LF/CRLF en fixtures/tests. |

Advertencias de compilación: CS8981 en la migración histórica `20260929030327_refactor.cs` y su Designer; CS8602 en `FirestoreCatalogRepository.cs:198`. Son existentes y no se modificaron. Vite advierte un chunk JS de 705,98 kB (210,74 kB gzip), superior a 500 kB. No se alteraron dependencias ni configuración para silenciarlo.

Limitaciones de ejecución: el primer intento de Chromium dentro del sandbox no expuso su puerto local y falló en el hook, antes de ejecutar las aserciones. El primer intento VSTest dentro del sandbox fue anulado tras 90 s sin conectar a testhost, sin ejecutar pruebas. Se reportaron ambas causas antes de repetir los mismos comandos con permiso para procesos/localhost. Las ejecuciones finales usaron mocks, datos ficticios y navegador con bloqueo de peticiones externas; no se consumieron servicios reales ni se ejecutaron suites ajenas.

## Criterios comprobados y bloqueo

| Criterio | Estado y evidencia |
| --- | --- |
| RESERVATIONS/READ sin BUSINESS_PROFILE/READ | OK en API y navegador; contexto mínimo sin consultas frontend al perfil. |
| Sin READ no hay lecturas de contexto/semana; rechazo 403 | OK; incluye caché previa y revocación durante revalidación. |
| Workspace/sesión y respuestas tardías | OK en API y navegador; borradores/cache anteriores no reaparecen. |
| Contratos diario y semanal, expectativa de zona y 409 | OK; se conservan contratos existentes y `Reservation.TimeZoneChanged`, sin seleccionar la zona desde el cliente. |
| DST actual, cruce de mes/año, UTC y zonas válidas | OK en pruebas dirigidas; ambas ocurrencias de 01:30 de New York siguen representadas por instantes distintos. |
| 401/403/503 e información horaria inválida | OK; sin horas supuestas, con recuperación explícita. |
| Nueva reserva/Reagendar con revalidación de la misma zona | OK; borradores conservados, horas ocultas y confirmación bloqueada. |
| Cambio de zona con formularios abiertos | OK; solo campos seguros se conservan y la hora/slot exige nueva elección. |
| Windows/IANA histórico | PENDIENTE; falla la regresión backend existente. |

Fallo exacto: `Windows_zone_metadata_matches_iana_historical_calendar_boundaries`, `ReservationsApiTests.cs:63`. Para `SA Pacific Standard Time`, el contexto comunica `America/Bogota`, pero el límite de la semana del 01/06/1992 resulta `1992-06-01T05:00:00Z` en Windows frente a `1992-06-01T04:00:00Z` esperado para IANA. No se cambió el calendario backend, no se inventó otra zona y no se omitió la prueba. Debe resolverse la política de equivalencia y aprobarse esta regresión antes de emitir GO. No se ha ejecutado sobre Linux ARM64; el fallo local no constituye una medición del comportamiento de Oracle.

Riesgo restante de actualización: sin push/polling permanente, la revalidación ocurre al entrar, recuperar foco o consultar una semana cuya expectativa el backend rechace. No se garantiza notificación instantánea de cambios hechos desde otra sesión. Las pruebas locales no certifican credenciales, red, salud ni almacenamiento de producción.

## Plan para un despliegue posterior — no ejecutado

La preparación queda bloqueada por el dictamen NO-GO y pendiente de auditoría. El orden siguiente solo se ejecutaría en una tarea autorizada después de resolver el fallo:

1. Identificar el candidato completo, sus artefactos inmutables y el estado vigente. Registrar digest/tag de la API ARM64 candidata y anterior, y version/release ID de Firebase Hosting candidata y anterior. Todos esos IDs/tags están **PENDIENTES**; no se inventaron ni se consultó producción.
2. Verificar backups recuperables de PostgreSQL y de los documentos de negocio Firestore, con fecha, IDs y evidencia de restauración. Registrar salud actual de Redis (solo caché), PostgreSQL, Firestore y la API mediante los mecanismos existentes. Salud y backups de producción están **NO VERIFICADOS**. No se requieren migraciones para este diff.
3. Desplegar primero la API ARM64 en Oracle. Verificar salud y smoke autenticado antes de publicar el frontend: `GET /api/reservations/context` debe devolver exclusivamente `{ "timeZone": "<zona autorizada compatible>" }` con RESERVATIONS/READ; un usuario sin READ debe recibir 403. Nunca incluir perfil, tokens ni credenciales en las evidencias.
4. Probar el contrato semanal `GET /api/reservations?locationId=...&from=YYYY-MM-DD&to=YYYY-MM-DD&timeZone=...`, con límite superior exclusivo y expectativa opcional. Comprobar un workspace A/B, sede ajena rechazada, llamadas diarias antiguas sin expectativa, DST/cruce de año y `409 Reservation.TimeZoneChanged` ante contexto desactualizado. Verificar errores 503 mediante mecanismos controlados, sin interrumpir dependencias productivas.
5. Solo después de esos resultados, publicar el frontend en Firebase Hosting. Smoke con usuario de Reservas sin permiso de perfil, foco con borradores de Nueva reserva/Reagendar, cambio de zona, cambio de identidad y recuperación. Cualquier escritura de prueba necesitaría autorización y datos de prueba aislados; nunca automatizar reservas reales.
6. Ante fallo, restaurar primero la versión frontend anterior y después, si corresponde, la imagen API anterior por los IDs inmutables registrados, verificando contratos y salud. No borrar ni restaurar datos como rollback rutinario: este cambio no altera esquemas. Confirmar continuidad de Evolution/WhatsApp con las comprobaciones existentes; no cambiar sesiones, protocolos, webhooks ni configuración.

No se realizó acceso a Oracle/Firebase, backup, inspección de secretos, modificación de infraestructura ni despliegue. Detenido para auditoría antes de UX-06B1.
