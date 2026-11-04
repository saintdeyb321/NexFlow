# Certificación pre-DEMO LLM-first — 2026-11-03

## 1. Identidad y alcance evaluados

Rama local `feature/llm-first-agent`. HEAD, origin/feature y remoto por `git ls-remote` coinciden en **6bba4c4d86ac5bd0a5dbb3c6476916fbb5b238dd**. main local/origin/remoto permanece en **5f5ff850e31df3fa73cb2c7f02f65ba6081173c7**. Cuatro commits de diferencia frente a main. Inicio: sin cambios rastreados ni staged; tres archivos del propietario sin seguimiento: auditoría de certificación, corpus externo y rúbrica. Conservados.

El commit6bba4c4 publicó F–K. Los encabezados f537cee+diff de CIERRE_FINAL_LLM_FIRST/ESTADO/RELEASE_CANDIDATE eran una fotografía anterior a esa publicación, no el estado actual. Comparados los **22 archivos backend/tests** del manifiesto local anterior con sus blobs en HEAD: hashes LF idénticos. Los antiguos TRX2123/67/histórico son evidencia previa correlacionada por fuentes, **no ejecuciones nuevas sobre6bba4c4 ni CI**.

Se reprodujo un defecto P1 en6bba4c4 y se corrigió localmente. Por tanto, el árbol final es **6bba4c4 + parche sin commit**; no hay SHA nuevo certificado. No publicar ni certificar6bba4c4 como si incluyera este arreglo. Inventario, diff, hashes y nuevos TRX en `backend/TestResults/LlmFirstPreDemo_20261103/` (ignorados, conservar privadamente). No cambios de arquitectura, prompts, proveedores, frontend, motores comerciales, schema, migraciones, Docker o workflows.

GitHub REST público `GET /repos/saintdeyb321/NexFlow/actions/runs?head_sha=6bba4c4d86ac5bd0a5dbb3c6476916fbb5b238dd&per_page=100` devolvió **total_count=0**. Consulta de sólo lectura preservada en remote-actions-base.json; el conector de status también devolvió lista vacía, pero por sí solo no demuestra ausencia de dispatch manual. Ningún workflow iniciado aquí. **CI PENDIENTE** para la nueva revisión aún no publicada.

## 2. Hallazgos y corrección mínima

| Severidad/estado | Evidencia | Dictamen y criterio de aceptación |
|---|---|---|
| **P1 REPRODUCIDO/CORREGIDO local**: cambio/omisión de WorkspaceId libera protección | En6bba4c4, AgentDemoPolicy.Protects consultaba sólo `_current().WorkspaceId`; DI registraba política Scoped. `identity-reload-red.trx`:2/0/2/0 al cambiar tenant u omitirlo con Enabled=false, en un scope nuevo | AgentDemoPolicy.cs:31–49 mantiene conjunto monotónico por host y detecta deriva de identidad; App DI:36 e Infra DI:30 usan singleton compartido. Tenant anterior y nuevo observado quedan protegidos; deriva impide el diálogo incluso al restaurar la identidad. Enabled puede apagarse/encenderse sólo sin deriva. Test `Workspace_identity_reload_cannot_release_the_previous_tenant_in_a_new_DI_scope` pasa ambos casos. Ninguna regla de frases ni cambio transaccional |
| **P1 LÍMITE CONFIRMADO/PENDIENTE operativo**: reinicio/rollback no conserva memoria de identidades anteriores | La protección acumulada pertenece a la vida del proceso, no existe registro durable independiente de configuración | No afirmar protección tras reiniciar con toda la sección omitida o usar imagen antigua. Antes de arrancar/reiniciar, el operador debe verificar GUID protegido idéntico en todas las APIs/workers/configuración efectiva, mantener ingreso DEMO aislado y no retirar WorkspaceId. Deriva exige revisión y reinicio autorizado; nunca reinicio automático para eludir el bloqueo. Sin esa evidencia, NO_GO DEMO. No se introduce un almacén paralelo ni migración para suplirla |
| **P0 histórico ABIERTO** | No hay nuevas trazas de Oracle del incidente de reserva inesperada | No se determina causa ni se declara resuelto. Barreras independientes de mutación DEMO verificadas offline, condicionadas a configuración correcta y ausencia de escritores antiguos. No autoriza reservas/pedidos reales |
| **P1 certificación externa PENDIENTE** | Sin runs, emulador, ARM64 ni canal real ejecutados en esta intervención | Exigir los cuatro workflows del próximo SHA y preflight del destino, sin usar verdes anteriores |
| **P2 retención/privacidad/costo PENDIENTE** | FirestoreAgentTurnJournal.cs:35–56,65–70,109–146; limpieza manual y tombstones permanentes | Payload vencido se elimina al observar/compactar, no hay barrido automático. No se certificó limpieza física/IAM en Firestore real. Sólo datos sintéticos hasta resolver operación/privacidad y medir costo |
| **P1 operativo: backlog/otros escritores no verificados** | OutboxProcessorWorker.cs:43–93 sigue enviando eventos existentes a IWorkflowGateway; no usa el kill switch de diálogo | No es creación de nuevas reservas por este agente. Un backlog anterior puede notificar n8n independientemente de Enabled. Antes del piloto exigir workspace nuevo/sintético, colas comerciales vacías y ningún escritor antiguo/servicio externo que opere en él. No borrar, reprocesar ni reenviar registros para conseguir esa condición |
| **P1 custodia del holdout** | Corpus/rúbrica privados actualmente untracked en la raíz | No se abrieron, leyeron ni calcularon sus hashes. Propietario debe moverlos fuera del repo antes de preparar publicación y no usar staging masivo. Los workflows versionados no deben incluirlos. No se los borra ni modifica |

## 3. Rutas de escritura, Production y workers

- `Program.cs:43–44`: AddApplication → AddInfrastructure en el host real. DI configura IReservationEngine→DemoReservationEngine y IOrderRepository→DemoOrderRepository, DbContext con DemoReservationWriteInterceptor. Prueba nueva `Production_registration_shares_the_policy_and_installs_the_EF_and_order_barriers` verifica registro conjunto y sólo Infrastructure (Migrator), scopes compartidos, interceptor instalado y rechazo de pedido antes de persistencia. Usa Npgsql **sólo como opciones**, SDK fake y mocks; no abre PostgreSQL/Redis ni obtiene credenciales Firebase ni inicia workers. No es arranque/health de Production real.
- `DemoCommercialGuards.cs:12–36`: bloquea create/cancel/cancel-active/edit/complete y ambas operaciones de cita existente; tres escrituras de pedidos (create/status/amount). Lecturas delegan sin cambiar motores. `DemoReservationWriteInterceptor.cs:14–19` bloquea Reservation Added/Modified/Deleted antes de SQL, tanto SaveChanges síncrono como async. Pruebas existentes verifican las tres variantes de entidad por vía async; no se atribuye una prueba síncrona adicional no ejecutada.
- `ReservationRepository.cs:33` añade entidad al ChangeTracker; las mutaciones examinadas de controllers/engine usan SaveChanges y quedan bajo el interceptor. Controllers Reservations usa IReservationEngine; Orders usa IOrderRepository; edición directa de reservas usa el mismo DbContext. Búsquedas dirigidas no hallaron INSERT/UPDATE/DELETE raw sobre Reservations en esas rutas. No equivale a una ACL de PostgreSQL ni impide SQL de un operador con permisos.
- El concreto FirestoreOrderRepository está registrado para construir el decorador; los consumidores productivos encontrados usan IOrderRepository. No hay consumidor productivo encontrado que inyecte directamente el concreto para escribir. Esto no impide un futuro bypass introducido por código nuevo o por un cliente Firestore privilegiado: preservar revisión DI y aislamiento del destino, no afirmar que Security Rules limitan al SDK administrativo.
- `ReservationResourceCoordinator.cs:21,77,86,102` escribe metadata de epochs/fencing/recuperación; no escribe citas. ReservationResourceRecoveryWorker reconcilia sedes/servicios, no crea/cancela/reagenda reservas. Las lecturas de disponibilidad pueden crear metadata de coordinación: draft-only **no significa cero escrituras de infraestructura**. Presupuestos, contexto, journal, inbound/outbound y notificaciones siguen siendo durables.
- `InboundMessageWorker.cs` abre scopes y usa el mismo handler registrado. InboundMessageRepository.cs:83–151 reclama el más antiguo por tenant/teléfono y mantiene lease/lock, validando instancia/workspace. No se probaron aquí locks de PostgreSQL real. No hay atomicidad entre SQL y Firestore: journal/outbound permiten recuperación ante ACK perdido; no se anuncia transacción distribuida.
- OutboxProcessorWorker y workers de catálogo conservan sus funciones. TenantDeletionWorker sólo se registra cuando TenantDeletionSafety.IsEnabled; no se habilita borrado nuclear ni se autoriza eliminación de workspaces en el piloto. GenericBackgroundWorker no tiene productores encontrados de QueueBackgroundWorkItemAsync en el código actual; no se usa como ruta comercial del nuevo agente.
- `Program.cs:172–180`: Development migra/seedea automáticamente; para la candidata sólo Production/Staging conforme al runbook, **sin ejecutar migrator ni DDL**. Ningún arranque ni migración ejecutados en esta auditoría.

## 4. Canal, obsolescencia, idempotencia y journal

`ProcessIncomingMessageCommandHandler.cs:68–90` verifica workspace protegido antes de resume legacy/interceptores. `AiResponseOrchestrator.cs:84` deriva por Protects, no por Allows: apagado/revocación entre ingreso y orquestador rechaza, no cae a legacy. Sin workspace DEMO configurado, ruta histórica intacta.

`AgentDemoChannel.cs:26–80` observa journal con workspace/conversación/source/texto; comprueba destinatario, licencia y preparación vigente antes de enviar. No usa un segundo gateway ni llama al proveedor directamente. El **outbound durable de WhatsApp es Firestore outboundMessages** mediante OutboundMessageService; el OutboxMessages SQL de n8n es otro circuito, no una nueva cola chat. Pending sin journal/texto correcto se rechaza; replays Sent/Failed/UnknownDelivery/Attempting no repiten POST. StartOutboundAsync (FirestoreConversationRepository.cs:97–133) reclama transporte transaccional y suprime respuesta AI ante toma humana. Sent significa aceptación del proveedor, no lectura/entrega al móvil.

La regresión ampliada `Prepared_but_unsent_facts_are_not_delivered_after_revocation_or_manual_takeover` incluye ahora **cambio ordinario de versión del contexto** además de revocación y humano:0 envíos y0 reevaluaciones adicionales. Verifica obsolescencia antes de reanudar un resultado no enviado. No demuestra que se pueda retirar un HTTP ya iniciado. Reintentos concurrentes, error antes de outbox, antes de transporte y ACK incierto se revalidaron con gateway interceptado. UnknownDelivery permanece sin reenvío automático. Handoff persiste modo Human antes de ACK recuperable; no acredita notificación ni aceptación de un asesor.

Journal: identidad canónica hash por workspace/conversación/source y fingerprint de cliente/texto; Version2; Begin exists=false; fencing; CAS de StateVersion/fingerprint y deadline en Complete; contexto y resultado se confirman juntos **dentro de Firestore**. Started vence30s por defecto y no vuelve a llamar IA; payload vence24h por defecto; replay obsoleto/revocado/humano se rechaza. Tombstone no readmite el mismo ID. Compactación≤100 documentos/lote, sólo scope de mantenimiento sintético, sin worker/API automático. No elimina conversationStates, historial ni outbound. Perfil/nombres comerciales pueden permanecer en texto realizado; historial no es un anonimizador general. Sólo datos sintéticos.

Costos no medidos: un recibo por turno, escrituras de claim/resultado/contexto/presupuesto y lecturas adicionales de revalidación; tombstones crecen con mensajes distintos. Compactación lee página + transacción por documento y escribe sólo vencidos; no es gratis ni una certificación de cuota. TTL lógico no garantiza borrado físico de documentos no visitados. Precios, volumen productivo, uso/tokens y p50/p95 reales UNKNOWN. No cambiar límites ni activar cuotas globales para inferirlos.

## 5. Evidencia local nueva (árbol parcheado)

Directorio nuevo `backend/TestResults/LlmFirstPreDemo_20261103`, sin sobrescribir TRX previos:

| Evidencia | Ejecutadas | PASS | FAIL | Omitidas | Resultado |
|---|---:|---:|---:|---:|---|
| identity-reload-red.trx |2|0|2|0|Defecto original reproducido en6bba4c4|
| identity-reload-green.trx |31|31|0|0|Parche mínimo y regresiones|
| pre-demo-directed.trx |69|69|0|0|DI Production, barreras, canal, CAS/fencing, retención, fallos/UnknownDelivery y estado obsoleto|
| current-patched.trx |2128|2128|0|0|G8 vigente, una ejecución final, validador PASS|
| historical-patched.trx |130|101|29|0|Inventario/motivos/hashes exactos; validador EXPECTED_HISTORICAL_INCOMPATIBILITIES exit0; dotnet exit1 preservado|
| build-patched.log |—|—|—|—|Build solución exit0,0 errores/0 advertencias incremental final|
| workflows-static.json |4 workflows|4|0|—|YAML y12 segmentos Python embebidos parseados; workflow_dispatch, contents:read y timeouts presentes|
| Firestore Emulator/Redis/PG real/ARM64/proveedores/WhatsApp |0|0|0|0|NOT_RUN/BLOCKED, no se ejecutaron ni se convierten en skips aprobados|

FIRESTORE_EMULATOR_HOST no está configurado. El test de emulador no se invocó; no se certifica limpieza, transacciones distribuidas ni IAM real. Una compilación inicial del test de DI usó Assert.Throws sobre Task y falló CS0619/xUnit2014: corregida a await Assert.ThrowsAsync, sin cambiar expectativas; ese intento no produjo TRX. Tres advertencias preexistentes al recompilar Infrastructure (CS8981×2 y CS8602 catálogo) conservadas sin cambios ajenos. No se repitieron suites de frontend ni runner/proveedores sin modificaciones.

Comandos ejecutados (las cuatro variables de corpus/salida/live se anularon **sólo en los procesos de prueba** para no cargar la muestra reservada):

```powershell
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --disable-build-servers --tl:off -m:1 --filter "FullyQualifiedName~Workspace_identity_reload_cannot_release" --logger "trx;LogFileName=identity-reload-red.trx" --results-directory backend/TestResults/LlmFirstPreDemo_20261103
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --disable-build-servers --tl:off -m:1 --filter "FullyQualifiedName~AgentDemoSafetyTests|FullyQualifiedName~AgentDemoChannelTests|FullyQualifiedName~AgentRegistrationTests|FullyQualifiedName~AgentTurnJournalTests|FullyQualifiedName~AgentJournalRetentionTests|FullyQualifiedName~AgentDialogueResilienceTests" --logger "trx;LogFileName=pre-demo-directed.trx" --results-directory backend/TestResults/LlmFirstPreDemo_20261103
python -B .github/workflows/g8_certification.py run current --trx backend/TestResults/LlmFirstPreDemo_20261103/current-patched.trx
python -B .github/workflows/g8_certification.py run historical --trx backend/TestResults/LlmFirstPreDemo_20261103/historical-patched.trx
dotnet build backend/backend.slnx --no-restore --disable-build-servers --tl:off -m:1
git diff --check
```

No sumar dirigidos a2128: se solapan. G8/H06 intactos: H06 exactamente `H06/4/A: ReservationEffects`, contrato130/101/29. Corpus históricos y validadores no cambiaron. Fuente publicada comparada por hashes con evidencia anterior; nuevos TRX/manifiesto tienen SHA base6bba4c4 y diff explícito, no un SHA final imaginario.

SHA256: current-patched.trx=`baf07ed56e03d4a95175c445bd29a064e0fff80d5d944ecfe63252a6a8d4bd22`; historical-patched.trx=`92ff756a3947724209cfc524ce781434e329020bf1d29807956226c5d614cea5`; pre-demo-directed.trx=`0ccbb3db29f8d1b7ea30cb9fbef70499e5dbd848ef5d752392ac6fd12808b60f`. Índice completo en evidence-index.json; manifiesto/diff de esta revisión en el mismo directorio.

## 6. Cuatro workflows: procedimiento pendiente de autorización

No se modificaron. Tras revisión y **publicación por el propietario de un nuevo SHA** que incluya el parche, congelar ese RELEASE_SHA completo y no mover feature entre dispatches. En Actions seleccionar cada workflow y branch `feature/llm-first-agent`; propietario los inicia manualmente. No usar los runs de6bba4c4 para certificar el parche. Si la rama avanza entre runs, detener y volver a certificar el mismo SHA; no mezclar resultados.

| Workflow/enlace | Inputs y controles | Artefactos a exigir |
|---|---|---|
| [RC3 Release](https://github.com/saintdeyb321/NexFlow/actions/workflows/rc3-release-checks.yml) | Rama congelada; build, vigente sin fallos/omisiones y **no menos de2128** para este inventario, histórico130/101/29 exacto, frontend≥83 TS/148 navegador sin errores/skips | rc3-release-unit-results: rc3-unit.trx/.g8.json, rc3-historical.trx/.g8.json y JUnit frontend. Release usa identidad dentro de .g8.json, no inventar identity.json separado |
| [PostgreSQL + Firestore](https://github.com/saintdeyb321/NexFlow/actions/workflows/rc3-reservation-integration.yml) | PostgreSQL efímero + emulador127.0.0.1/demo-nexflow-rc3, NEXFLOW_RC3_INTEGRATION=1. Min37 del YAML no basta para aceptar caso nuevo ausente: exigir nombre completo `NexFlow.Tests.Integration.AgentJournalEmulatorTests.Two_emulator_clients_enforce_one_owner_atomic_context_and_physical_tombstone`, **una ejecución PASS**, cero fallos/omitidas y demás inventario intacto | rc3-isolated-test-results: integration.trx, current/historical TRX y .g8.json, identity.json. Registrar total real, no inventar un número. Dos clientes no acredita dos procesos/IAM productivo |
| [Redis](https://github.com/saintdeyb321/NexFlow/actions/workflows/concierge-redis-integration.yml) | Redis temporal loopback16379, Category=RedisIntegration; **exactamente1 ejecutada/1 PASS/0 FAIL/0 skips** | concierge-redis-test-results/redis.trx e identity.json |
| [ARM64](https://github.com/saintdeyb321/NexFlow/actions/workflows/agent-runtime-arm64-certification.yml) | release_sha=RELEASE_SHA completo, mismo ref; ubuntu-24.04-arm nativo, API/migrator construidos sin ejecutar host/migrator, UID/GID1654, archivos, codecs PNG/JPEG/WebP. retain_api_image=false/storage_quota_confirmed=false/artifact_budget_mib=0 salvo autorización separada | artifacts/arm64: identidad, provenance, manifiesto/contexto, Image ID/etiqueta SHA, permisos y logs codec PASS. Si se aprueba custodia, misma Image ID exportada/importada, SHA256/tamaño TAR; máximo512MiB/1día y cuota/coste confirmados |

Por cada run guardar URL/Run ID/run_attempt, head_sha exacto, rama, event=workflow_dispatch, conclusión de todos los jobs/steps y artefactos descargados con hashes. Contrastar GITHUB_SHA/GITHUB_RUN_ID/GITHUB_RUN_ATTEMPT/GITHUB_JOB de identity/.g8.json con REST de ese intento, TRX con total=executed,0 omitidas/abortadas/timeout; comparar inventario de tests y motivos rojos autorizados. En reruns no mezclar artefactos de intentos anteriores. Ausencia de artefacto/identidad/nuevo test o SHA distinto = **NO_GO**, aunque la UI esté verde. Un upload con if-no-files-found:warn no sustituye evidencia faltante.

## 7. Holdout sellado y siguiente ensayo

**Ahora sí existe** conjunto independiente:12 conversaciones según propietario, creado por otro evaluador. SHA256 suministrado: **A64059637C90E802F7A4692BF3B4BED38CF9070D16BC0F0F7E8C8EFEA33549B5**. No se verificaron bytes porque no está autorizado abrirlo; no se leyó su contenido ni rúbrica. No se cambiaron prompts/código para ajustarlos a expectativas. Archivos actuales (sólo nombres observados por git status):

- `C:\Proyectos\NexFlow\NEXFLOW_EVALUACION_HOLDOUT_12_SINTETICAS_2026-11-03.json`.
- `C:\Proyectos\NexFlow\NEXFLOW_RUBRICA_HUMANA_HOLDOUT_2026-11-03.md`.

Propietario/evaluador los mueve fuera de repo público y verifica hash; no se ejecuta Move/Delete desde Codex. Tras candidata limpia congelada, gates de infraestructura y autorización específica, un evaluador separado puede hacer preflight de **una conversación** sin exponer expectativas:

```text
python -B scripts/llm-first/evaluate.py --live --dry-run --corpus RUTA_PRIVADA --sha256 A64059637C90E802F7A4692BF3B4BED38CF9070D16BC0F0F7E8C8EFEA33549B5 --case ID_DEL_EVALUADOR --max-http 12 --max-token-reservation 30000 --output DIRECTORIO_NUEVO
```

No se ejecutó ni ese dry-run sobre el corpus durante esta auditoría. Live requiere autorización separada, cuotas/modelos vigentes verificados y credenciales sintéticas privadas por entorno del arnés; nunca buscarlas en Oracle/appsettings/archivos privados ni enviarlas al chat. Quitar --dry-run sólo tras aprobación expresa de una conversación, tope compartido≤12 HTTP/30000 tokens reservados o inferior autorizado; fallback/rechazos cuentan. No borrar markers Started/Uncertain/terminal ni ampliar a las12 automáticamente. Registrar identity.json, evaluation.trx, live-ID.json, summary.json, proveedor/modelo efectivamente llamado, HTTP, tokens realmente reportados/UNKNOWN, p50/p95 observado y rúbrica humana independiente. Dos evaluadores humanos reales siguen pendientes; una suite sintética no los reemplaza.

## 8. Archivos y decisiones finales

Backend: AgentDemoPolicy.cs y DI Application/Infrastructure (3). Tests: AgentDemoSafetyTests.cs, AgentRegistrationTests.cs y AgentDemoChannelTests.cs (3). Documentación: este informe, actualización contextual de RELEASE_CANDIDATE.md y checkpoint en ESTADO.md. Frontend, workflows, prompts, proveedores, motores, migraciones, appsettings y archivos del propietario intactos. Sin commits, push, merges, dispatch, Docker, migraciones, Oracle, IA externa ni WhatsApp real.

| Decisión | Estado y alcance |
|---|---|
| **OFFLINE_GO** | **GO sólo para6bba4c4+parche local evaluado**:69 dirigidos,2128 vigente,130/101/29 histórico y build PASS.6bba4c4 publicado sin parche no es candidato aprobado: tiene P1 reproducido. Falta nuevo SHA publicado/recertificado |
| **PROVIDER_EVAL_GO** | **NO_GO / WAITING_FOR_OWNER**: corpus existe y permanece sellado; faltan congelación final, gates externos, autorización específica, cuotas/modelos y ensayo con revisión humana.0 HTTP real |
| **DEMO_DEPLOY_GO** | **NO_GO**: nuevo SHA, cuatro runs/artefactos, emulador/ARM64, stores/ingreso aislados, constancia del GUID en reinicios/rollback, colas vacías y autorización SHA/digest/ventana todavía pendientes |
| **COMMERCIAL_GO** | **NO_GO**: P0 histórico sin causa cerrada, calidad/proveedores/canal/privacidad/costos reales sin certificar. Prohibidas operaciones comerciales reales |

Próxima acción concreta: revisión del pequeño parche; propietario custodia corpus/rúbrica fuera del repo y autoriza separadamente publicación del nuevo SHA y los cuatro runs manuales. Después se verifica evidencia del mismo SHA antes de solicitar una muestra real. No se inicia otra fase de desarrollo ni se modifica API activa. **DETENIDO PARA AUDITORÍA.**
