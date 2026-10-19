# RC-3 Sprint 4.2 — integración aislada

Base: `e1af63324409ce7b6c2b0fe141403569069f11e3`, árbol inicialmente limpio.

## Componentes adicionales, instalación y coste

No se instalan componentes en Windows. El workflow manual utilizará un runner Ubuntu 24.04 desechable, PostgreSQL `15.10`, SDK .NET `10.0.401`, Node `22.14.0`, Temurin `21.0.8+9` y Firebase CLI `14.12.0`. Firebase CLI se instala exclusivamente en el runner con npm; `firebase setup:emulators:firestore` descarga el emulador. No requiere un proyecto Firebase real ni credenciales: ID `demo-nexflow-rc3`, endpoint loopback, reglas exclusivas de pruebas. No se añaden paquetes NuGet.

Coste: minutos de GitHub Actions según plan del propietario (job limitado a 30 minutos), almacenamiento temporal de resultados durante 7 días y descargas de herramientas. Cero llamadas a servicios de negocio o Firestore Cloud. No se ha ejecutado el workflow ni se promete coste monetario cero del runner.

## Estado inicial

Build baseline: 0 errores, 3 advertencias preexistentes (CS8981 x2, CS8602 x1). Suite explícita baseline: **669 aprobadas, 0 fallidas, 0 omitidas**. PostgreSQL y Java no están disponibles en PATH; no se inicia Docker en Windows.

## Veredicto

**IMPLEMENTACIÓN DE PRUEBAS COMPLETA — CERTIFICACIÓN PENDIENTE. NO-GO de certificación distribuida/despliegue.**

Se prepararon 24 casos de integración, compilados y descubiertos, pero **ninguno fue ejecutado**: no hay PostgreSQL ni Java en PATH y no se instaló software ni se ejecutó Docker. El workflow tampoco se ejecutó. No se presenta este estado como aprobación de migración, ausencia de inconsistencias o validación de Firestore Cloud. Sprint 5 no autorizado.

## Revisión final del protocolo (código, no evidencia distribuida)

| Punto | Evidencia / conclusión |
|---|---|
| Captura antes de Firestore | Create/Edit en ReservationEngine capturan Location/Service antes de leer configuración; SaveService captura antes del documento observado. |
| Locks hasta commit | Validate requiere transacción y usa FOR UPDATE, Location→Service/ID ordinal. Begin usa transacción ReadCommitted breve; el motor confirma reserva y Outbox dentro de Serializable. Las nuevas pruebas observan pg_blocking_pids y estado durable, sin quitar locks. PENDIENTE ejecutar. |
| Baja vs reserva | Begin espera el mismo recurso y comprueba futuras Confirmed antes de avanzar epoch. Validación de epoch obsoleto impide confirmar después de Begin. Casos de ambas ordenaciones preparados. |
| Recuperación | Intención SQL no caduca; Claim usa CAS y agenda 30 s. Otro scope/worker lee almacenamiento durable y aplica mismo ID. Fence y negocio comparten transacción Firestore; Complete usa CAS de identidad/epoch. |
| ACK incierto | Test interrumpe al llamador después del commit Firestore y antes de completar SQL. El worker nuevo debe observar el marcador y conservar UpdateTime; no se afirma que se haya cortado la red real. |
| Borrado | Middleware y ExecuteRecovery toman advisory compartido, scheduler/borrado exclusivo. El worker comprueba existencia/no Deleting antes de remoto. No se elimina esa guarda. Prueba usa TenantDeletionWorker y FirestoreTenantCleanupService reales. |
| Escritores alternativos | Búsqueda dirigida de Reservation.Create/Reschedule en API/Application/Infrastructure: escrituras en el mismo motor; SaveService/baja/sede pasan por coordinador. Cancelar/completar no crean intervalos. No se reauditaron módulos ajenos. |
| Conexiones | Los row locks de recurso no cubren Firestore. Sí la transacción de ciclo de vida: una conexión por intento mientras espera remoto. Worker limita intento a 30 s; borrado existente a 5 min. No se cambió producción sin reproducción. |
| Orden de ciclo de vida | Compartido de tenant precede a recursos; borrado reclama job en transacción breve y la cierra antes de esperar exclusivo, luego bloquea job. No se localizó inversión nueva demostrable. Pruebas de orden y deadlock deliberado 40P01 preparadas; no certificadas. |

No se confirmó un defecto nuevo que justifique parche de producción. El comentario local del fence no describe la guarda externa del tenant: la excepción se mantiene explícita aquí, sin prometer ausencia global de SQL durante IO.

## Arquitectura aislada y seguridad

- Un job manual, sin environment de despliegue y con `contents: read`; checkout no conserva credenciales. No referencias a secrets ni login cloud.
- PostgreSQL service efímero con credencial pública ficticia `local-test-only`. Cada caso crea una base UUID `nexflow_rc3_*`, aplica las migraciones reales y elimina **solo su base**. No usa EnsureCreated ni SQLite. El usuario sintético necesita CREATEDB (el contenedor de pruebas lo proporciona).
- Fixture rechaza hosts no loopback, base administrativa distinta, usuario/password no sintéticos y certificados/passfile explícitos. Firestore exige `demo-nexflow-rc3`, host loopback y `EmulatorOnly`; no se lee appsettings/.env ni se arranca la API con servicios de negocio.
- Repositorios, coordinador, motor, fence, migraciones y workers de producción. Fault probes solo delegan e interrumpen fronteras; no reescriben comandos SQL ni sustituyen FOR UPDATE/advisory locks.
- Casos serializados entre sí para medir sin carga incidental; dentro de cada caso hay conexiones/contextos independientes, barreras y observación SQL. Límites de comandos 15 s, caso 90 s, limpieza 30 s. Poll de 20 ms solo sobre condiciones verificables, no sleeps para inferir un ganador.
- Limpieza borra únicamente roots UUID registrados y la base generada; limpia su pool Npgsql. `emulators:exec` termina el emulador; el runner destruye el service container incluso si falla el job. No volúmenes persistentes ni datos del SaaS.
- Solo TRX de datos ficticios (7 días), sin logs de emulador, cadenas reales, payloads de usuarios ni dumps. IncludeErrorDetail desactivado. Una integración omitida/missing hace fallar la verificación de resultados de CI.

## Matriz preparada (todos PENDIENTES de ejecución con servicios)

Cada fila corresponde a un Fact de integración; los nombres completos están en los archivos enlazados al final.

| # | Suite | Caso / estado final exigido |
|---|---|---|
| 1 | PostgreSQL | Dos solicitudes de reserva, mismo slot: una reserva y un Outbox. |
| 2 | PostgreSQL | Reserva gana: baja bloqueada realmente hasta commit y luego rechazada; servicio sigue activo. |
| 3 | PostgreSQL | Desactivación gana contra snapshot antiguo: cero reserva/Outbox. |
| 4 | PostgreSQL | Eliminación sede gana contra reserva: cero reserva/Outbox; intención terminada. |
| 5 | PostgreSQL | Reagendamiento conflictivo: ambos intervalos e historial/Outbox intactos. |
| 6 | PostgreSQL | Dos Serializable con snapshot compartido: 40001 real, solo update ganador. |
| 7 | PostgreSQL | xmin generado/cambia realmente: escritor obsoleto rechazado, cancelación preservada. |
| 8 | PostgreSQL | Fallo después de SaveChanges antes de commit: rollback reserva+Outbox; reinicio/replay único. |
| 9 | PostgreSQL | Locks de dos conexiones, entrada invertida normalizada: espera hasta commit, otro workspace progresa. |
| 10 | PostgreSQL | Inversión deliberada fuera del protocolo: 40P01 diagnosticado, un superviviente, sin epochs alterados. |
| 11 | PostgreSQL | Cancelar espera de row lock: rollback y nueva intención válida. |
| 12 | Firestore + PG | Negocio+fence terminal y repetición misma identidad: UpdateTime no cambia. |
| 13 | Firestore + PG | Dos escritores misma identidad: contador comercial termina exactamente en uno. |
| 14 | Firestore + PG | ACK perdido en frontera postcommit: worker nuevo recupera sin nueva escritura comercial. |
| 15 | Firestore + PG | Reactivación/epoch nuevo: callback viejo no modifica documento/fence ni completa otro estado SQL. |
| 16 | Firestore + PG | Intención crítica vs edición inocua ganadora: rechazo terminal, nombre/duración e historia preservados. |
| 17 | Firestore + PG | Payload jsonb malformado: worker sella InvalidPayload, libera solo tras terminal, servicio intacto. |
| 18 | Firestore + PG | Excepción con escritura encolada: rollback comercial, solo Rejected; otro workspace intacto. |
| 19 | Migración PG | Up desde anterior con historia: tabla/índice parcial, dos canceladas+vigente, 23505 vigente y SourceMessageId, xmin/historia/modelo conservados. |
| 20 | Migración PG | Down sin uso de epochs y sin historia incompatible, seguido de Up: fila previa preservada. |
| 21 | Migración PG | Down con epoch Ready: 55000; tabla/historia/migration record conservados. |
| 22 | Migración PG | Down con horarios reutilizados: 55000, rollback DDL; índice parcial aún permite otra histórica cancelada. |
| 23 | Tenant | Compartido→exclusivo bloqueado→commit FS/ACK retenido→deadline acelerado→rollback SQL→worker de borrado→ACK tardío→nuevo proceso lógico rechazado; otro tenant intacto. |
| 24 | Tenant/pool | Pool de tamaño uno saturado: cancelar open, reutilizar pool; cancelar guarda y reutilizar contexto sin transacción colgada. |

## Ciclo de vida y límites de la medición

Caso 23 consulta pg_stat_activity para demostrar `idle in transaction` con xact_start y pg_blocking_pids para probar espera exclusiva. Registra milisegundos de la ventana compartida/cancelación; se mantienen dos conexiones transaccionales más observador, con pool de fixture limitado a 8. Caso 24 satura deliberadamente un pool independiente de tamaño 1. **Aún no hay valores medidos: casos no ejecutados.** No sustituirlos por números estimados.

El deadline se acelera a 50 ms **después** de observar el bloqueo, para probar la misma cancelación sin esperar 30 s artificialmente. El ACK retenido corresponde a un commit Firestore ya terminado. Abrir la barrera solo después del purge comprueba que su continuación cancelada no reanuda negocio. Esto **no prueba** un commit servidor todavía en vuelo después del purge, pérdida física de red, SIGKILL o failover. Esa ventana, la latencia/pool bajo carga y cleanup parcial cloud continúan como riesgo de integridad pendiente; no retirar el lock ni habilitar borrado en producción basándose solo en este harness.

Reinicio en estas pruebas significa nuevos objetos/scopes/contextos sobre PostgreSQL/Firestore persistentes del caso, no caída del sistema operativo. Dos conexiones reales sí se utilizan; no se afirma ejecución multiproceso adicional.

## Diferencias del emulador y Cloud

Emulador valida operaciones SDK, transacciones, precondiciones y replay en entorno local. No acredita IAM, índices compuestos desplegados, reglas de acceso de clientes, cuotas/costes Spark/Blaze, latencia geográfica, reintentos/failover regional, persistencia ante caída del host ni garantías operativas cloud. Admin SDK no valida reglas de clientes; reglas abiertas **solo** existen en el config de tests y nunca se publican. El scheduling/contención puede diferir de Cloud. No declarar GO de producción aunque el workflow quede verde.

## Migración, compatibilidad y rollback (no ejecutados)

Migración existente `20261019170831_CoordinateReservationResourcesAndReuseCancelledSlots`, Designer, snapshot y scripts **sin modificar**. Se prueban las migraciones EF reales sobre bases descartables, incluidos fallos 55000 y 23505. Validación local offline confirma modelo sin cambios; Up/Down reales siguen pendientes.

Rollout posterior requiere autorización: inspeccionar DDL/historia real, detener escrituras y drenar TODAS las APIs/workers antiguos, aplicar Up y activar únicamente binarios compatibles con epochs/fences/UpdateTime. No hay convivencia segura de escrituras con APIs anteriores. Medir duración del CREATE INDEX no CONCURRENTLY y locks DDL antes de aprobar ventana.

Rollback de aplicación conserva esquema e historial y usa solo binario compatible. Down adquiere ACCESS EXCLUSIVE y rechaza **cualquier epoch**, incluso Ready, o claves históricas duplicadas; no forzarlo borrando filas/fences/historia ni modificando reservas. Con uso normal, corregir hacia delante o pausar escrituras. Nada fue aplicado a Oracle ni a otra base.

## Evidencia local real

| Validación | Resultado |
|---|---|
| `dotnet build backend/backend.slnx --no-restore` baseline | 0 errores, 3 warnings conocidos CS8981 x2 / CS8602 x1. |
| Mismo build final incremental | 0 errores, 0 warnings. |
| `dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore` inicial | 669 aprobadas / 0 fallidas / 0 omitidas. |
| Mismo comando final (logger quiet) | **677 aprobadas / 0 fallidas / 24 omitidas / 701 total**, 8 s de tests. 669 previas intactas + 8 guardas locales nuevas. |
| Integraciones | 24 descubiertas y compiladas, explícitamente **NO EJECUTADAS** por opt-in desactivado/servicios ausentes. No son verdes. |
| EF `migrations has-pending-model-changes --no-build --project backend/NexFlow.Infrastructure --startup-project backend/NexFlow.Infrastructure -- --offline` | Exit 0, sin cambios. Aviso EF tools 10.0.10/runtime 10.0.11; no actualización. |
| Workflow / emulador | YAML/JSON parseados localmente con parser ya instalado de Firebase CLI. Trigger solo manual, contents read, sin secrets. No ejecución/descarga de herramientas ni validación remota de Actions. |

Durante desarrollo se corrigieron errores de compilación del harness (IClock ambiguo/ValueTask) y advertencias de aserciones nuevas; no eran defectos del producto. Primera invocación de build bajo sandbox no pudo completar MSBuild; reejecución autorizada normal exitosa. Sin restores, cambios de dependencias ni frontend en esta ejecución.

## Ejecución manual futura segura (NO ejecutada)

Primero revisar y publicar este diff por el propietario mediante autorización independiente. El archivo debe estar en la rama por defecto para que GitHub ofrezca workflow_dispatch. No se hizo commit/push aquí. Elegir exclusivamente la revisión aprobada; el SHA base por sí solo no contiene estos tests.

```powershell
gh workflow run rc3-reservation-integration.yml --repo saintdeyb321/NexFlow --ref main
gh run list --repo saintdeyb321/NexFlow --workflow rc3-reservation-integration.yml --limit 1
gh run view <RUN_ID> --repo saintdeyb321/NexFlow
gh run download <RUN_ID> --repo saintdeyb321/NexFlow --name rc3-isolated-test-results
```

Alternativa: Actions → RC3 disposable reservation integration → Run workflow → revisión aprobada. No elegir otro workflow ni añadir credenciales del SaaS.

En un host aislado que **ya** disponga de PostgreSQL/Java/Node/SDK y herramientas indicadas, crear usuario/base sintéticos locales antes de ejecutar. No apuntar variables a una base existente del negocio:

```powershell
$env:NEXFLOW_RC3_POSTGRES='Host=127.0.0.1;Port=5432;Database=nexflow_rc3_admin;Username=nexflow_rc3;Password=local-test-only'
$env:GCLOUD_PROJECT='demo-nexflow-rc3'
$env:FIRESTORE_EMULATOR_HOST='127.0.0.1:8080'
$env:NEXFLOW_RC3_INTEGRATION='1'
firebase emulators:exec --only firestore --project demo-nexflow-rc3 --config backend/NexFlow.Tests/Integration/emulator/firebase.json 'dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --filter Category=Integration'
Remove-Item Env:NEXFLOW_RC3_INTEGRATION
```

No ejecutar estos comandos en Oracle. Windows actual no tiene los requisitos; no se instalaron. El workflow es la vía preparada para ejecutarlos en runner efímero.

## Archivos y próximos pasos exactos

- Tests nuevos: `backend/NexFlow.Tests/Integration/IsolatedStores.cs`, `IntegrationSafetyTests.cs`, `IntegrationCollection.cs`, `PostgresResourceIntegrationTests.cs`, `FirestoreResourceIntegrationTests.cs`, `ReservationMigrationIntegrationTests.cs`, `TenantLifecycleIntegrationTests.cs`.
- Config de tests: `backend/NexFlow.Tests/Integration/emulator/firebase.json`, `firestore.rules`.
- Workflow nuevo: `.github/workflows/rc3-reservation-integration.yml`.
- Documentación: este informe y `NexFlow_RC3_PROGRESS.md`. Código productivo/migraciones/frontend/AGENTS/plan/secretos intactos.

1. Revisión humana del diff y publicación independiente del workflow; no automatizada aquí.
2. Ejecutar workflow manual sobre revisión aprobada, exigir 24/24 integraciones sin skips y al menos las 669 regresiones conservadas; guardar TRX y tiempos reales.
3. Investigar cualquier fallo antes de certificar: no modificar expectativas para ocultar diferencias de motor/emulador.
4. Evaluar resultados de tenant/pool/locks y la ventana explícita de commit servidor tardío; fijar límites operativos con evidencia, no estimaciones.
5. Emitir nueva decisión de certificación solo con esa evidencia; despliegue requiere autorización aparte. **Detenido al cerrar Sprint 4.2; no iniciar Sprint 5.**
