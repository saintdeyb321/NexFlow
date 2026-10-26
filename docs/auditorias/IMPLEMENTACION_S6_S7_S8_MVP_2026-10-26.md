# NexFlow MVP — checkpoints locales S6, S7 y S8

Fecha: 2026-10-26. Evidencia de esta intervención, sin operaciones productivas.

## Identidad y delimitación

- HEAD inicial: `be07f89c2146f7c4344effad849e1335c9614e86`, rama `main`, coincide con la referencia del propietario. `git status --short`: sólo los dos documentos obligatorios nuevos del propietario sin seguimiento; no se modifican.
- Archivos del propietario: `docs/NexFlow_Auditoria_S3_S4_S5_2026-10-26.md` y `docs/NexFlow_Prompt_Codex_Sprints_6_7_8_2026-10-26.md`. Los cuatro entregables de este encargo no existían.
- La identidad remota actual y la API reportada `306d7c8-perms1` son **NO VERIFICADAS** aquí. No se consultó GitHub Actions, remoto Git, Oracle ni credenciales.
- Frente a `306d7c8af41aacfd14c71d949da039f628beed95`: 9 archivos Application, Dockerfile, 7 archivos de pruebas y 10 Markdown. `git diff --name-status` no muestra migraciones. Comprobación adicional con `git diff --exit-code` sobre `backend/NexFlow.Infrastructure/Migrations`, configuraciones/contexto EF, Domain, repositorios Firestore y abstracciones de caché: sin diferencias.
- No cambia el esquema ni el shape de contratos persistidos en ese intervalo. Sí cambia semántica de interpretación/continuación, referencia y precedencia Profile/FAQ: un JSON legible por una versión anterior no demuestra compatibilidad de garantías o rollback. Las referencias legacy sin huella/pregunta vigente se rechazan de forma segura; no se migra contexto para recuperarlas.
- Se consultaron AGENTS, auditoría independiente, informes S1/S2 y S3/S4/S5, matriz/casos previos y secciones puntuales de 07/08 Agent Runtime. El registro rojo histórico F7 fue sucedido por su hotfix, no se interpreta como permiso para retirar F7-01.

## Sprint 6 — checkpoint previo a S7

Inspección del diff productivo completo del intervalo: selección explícita de fecha/hora y guardas de referencia; acuse sin consentimiento; General/Information→Profile; Order/OperationInformation respaldado; KnowledgeQuery con tópico; COPY chown API/migrator. Se conservaron OrderFlow, motores, licencias, CAS, privacidad de citas existentes y UnknownDelivery. No se justificó un cambio de código, Dockerfile o CI para este sprint.

| Ejecución NUEVA | Ejecutadas | Aprobadas | Fallidas | Omitidas |
|---|---:|---:|---:|---:|
| S6 dirigidas, s6-directed.trx | 205 | 205 | 0 | 0 |
| S6 G8 current, s6-current.trx | 1538 | 1538 | 0 | 0 |
| S6 G8 historical, s6-historical.trx | 130 | 118 | 12 | 0 |
| Pruebas del validador G8, unittest | 7 | 7 | 0 | 0 |

Build `dotnet build backend/backend.slnx --no-restore`: **0 errores, 3 advertencias existentes**: CS8981 en `20260929030327_refactor.cs:9` y `.Designer.cs:16`; CS8602 en `FirestoreCatalogRepository.cs:228`. No se corrigen fuera de alcance. SDK local 10.0.401, Python 3.12.5. No restore ni dependencias nuevas.

G8 current=PASS; historical=EXPECTED_HISTORICAL_INCOMPATIBILITIES. El proceso dotnet histórico devuelve 1 y el validador sólo acepta exactamente inventario, motivos y hashes congelados de las doce incompatibilidades. No son 130 PASS. No se modificaron evaluadores, corpus, umbrales ni exclusiones. Artefactos nuevos, sin sobrescribir S1–S5: `backend/NexFlow.Tests/TestResults/mvp-s6-s7-s8/`.

Comandos ejecutados desde la raíz:

```powershell
dotnet build backend/backend.slnx --no-restore
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --filter 'FullyQualifiedName~RuntimeNaturalBookingTests|FullyQualifiedName~RuntimeReadToolsTests|FullyQualifiedName~RuntimeOrderRequestTests|FullyQualifiedName~RuntimeEfficiencyTests|FullyQualifiedName~RuntimeRealizationTests|FullyQualifiedName~CommerceSafetyTests|FullyQualifiedName~AssistantRc1ConcurrencyTests|FullyQualifiedName~AssistantRedisRecoveryTests|FullyQualifiedName~ZeroTokenKnowledgeAuthorizationTests|FullyQualifiedName~ExistingReservationSprint2Tests|FullyQualifiedName~EvolutionMediaTests|FullyQualifiedName~AiInterpreter' --logger 'trx;LogFileName=s6-directed.trx' --results-directory backend/NexFlow.Tests/TestResults/mvp-s6-s7-s8
python -B .github/workflows/g8_certification.py run current --trx backend/NexFlow.Tests/TestResults/mvp-s6-s7-s8/s6-current.trx
python -B .github/workflows/g8_certification.py run historical --trx backend/NexFlow.Tests/TestResults/mvp-s6-s7-s8/s6-historical.trx
python -B -m unittest discover -s .github/workflows -p test_g8_certification.py
```

CI: se inspeccionaron los tres workflows manuales, sus filtros, límites y artefactos, sin dispatch ni modificación. El procedimiento de identidad/descarga/revisión y smoke ARM64 figura en [GO_LIVE_CHECKLIST_NEXFLOW_MVP.md](GO_LIVE_CHECKLIST_NEXFLOW_MVP.md). Release checks exige frontend TS ≥83 y navegador ≥148 sin fallos/omisiones; local frontend no se ejecuta en este encargo sin cambios frontend. Integraciones/Redis requieren sus servicios aislados, no la configuración de aplicación. Docker no está disponible en PATH: **ARM64, UID/runtime, modos reales y PNG/JPEG/WebP PENDIENTES**. Las bases :10.0 son móviles: registrar digests y retener el artefacto; no se declara reproducción binaria bit a bit.

**Gate S6 local OK**: código sin cambios, build/dirigidas/current/historical verificados y paquete operacional preparado. **Gate externo PENDIENTE / NO-GO release**, sin Run ID, TRX remoto ni smoke del SHA final. Conforme al prompt, se continúa únicamente con S7/S8 locales/documentales.

## Sprint 7

Checkpoint completado local/documental después de S6. No se modificó código ni se alteraron datos/flags.

- `s7-data-privacy.trx`: **297 ejecutadas/aprobadas, 0 fallidas/omitidas**. Fixtures de datos/horarios/precios/moneda, revocación/capacidades, cinco publicados/cuatro reservables, Origin/estado/hash/callback, conservación de PDF y recuperación de caché/borrador. Comando: `dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-build --no-restore --filter '(FullyQualifiedName~Catalog|FullyQualifiedName~RuntimeReadToolsTests|FullyQualifiedName~RuntimeNaturalBookingTests|FullyQualifiedName~RuntimeOrderRequestTests|FullyQualifiedName~BusinessProfileApiTests|FullyQualifiedName~AssistantRedisRecoveryTests|FullyQualifiedName~CommerceSafetyTests|FullyQualifiedName~ReservationsPeruTimeTests|FullyQualifiedName~ZeroTokenKnowledgeAuthorizationTests)&Category!=Integration&Category!=RedisIntegration&Category!=ProviderIntegration' --logger 'trx;LogFileName=s7-data-privacy.trx' --results-directory backend/NexFlow.Tests/TestResults/mvp-s6-s7-s8`.
- Reconciliación por ítem/variante en [MATRIZ_Q01_Q10_VALIDACION_MVP.md](MATRIZ_Q01_Q10_VALIDACION_MVP.md). La exclusión real de la quinta oferta permanece NO VERIFICADA; los fixtures no autorizan concluir nada sobre Oracle.
- Diagnóstico de artefacto: GetArtifactAsync:116–139 puede guardar expiración/STALE; repo GetCurrentArtifactAsync:45–55 sólo lee. Matriz exige GetSnapshot/SQL READ ONLY, sin llamar a reconciliadores o resumers. Queries propuestas contrastadas con modelo local; esquema real PENDIENTE antes de ejecutarlas.
- No se encontró una sustitución por mocks en DI Knowledge/Offering/Booking/Order inspeccionada. Application DI:34/:71–82 y Persistence DI registran motores/repositorios reales; referencias productivas no incluyen Moq/NexFlow.Tests/InMemory. No se introduce otra arquitectura ni se reaudita el SaaS completo.
- TaxId confirmado en payload de CatalogGenerationService:101. Perfil público lo omite. Render/plantilla n8n externos y PDF real NO VERIFICADOS; no se halló plantilla PDF versionada en las rutas backend consultadas. Política pendiente: OMITIR por defecto lo no expresamente autorizado; no se cambia contrato/hash por suposición. Q10 y publicación comercial bloqueados.
- Recuperación Spark investigada sólo con fuentes públicas oficiales, sin APIs/proyectos/credenciales. La matriz contiene propuesta SDK/copia lógica cifrada y restore sólo a emulador aislado, alcance, consistencia/RPO/RTO, protección PII y límites de retención propuestos. No hay backup/restore Firestore ejecutado o mecanismo certificado. No se atribuye la restauración SQL histórica a Firestore ni se activa billing.

**Gate S7 local/documental OK**: pruebas verdes, matriz preparada y dependencias explícitas. **Datos reales, Q01–Q10, política fiscal y restauración: PENDIENTES / NO-GO comercial.** Se continúa S8 documental conforme al prompt, sin operar fuentes reales.

## Sprint 8

Checkpoint completado local/documental después de registrar S7. No hay autorización para operaciones reales.

- `s8-local-pilot-contracts.trx`: **135 ejecutadas/aprobadas, 0 fallidas/omitidas**, con runtime multitur, configuración de perfil HTTP y denegaciones, permisos informativos, handoff/CAS/replay, medios/UnknownDelivery, inbound/outbox y callbacks HTTP de catálogo. No son conversaciones WhatsApp reales.
- Comando: `dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-build --no-restore --filter '(FullyQualifiedName~RuntimeEfficiencyTests|FullyQualifiedName~BusinessProfileApiTests|FullyQualifiedName~ZeroTokenKnowledgeAuthorizationTests|FullyQualifiedName~AssistantRc1ConcurrencyTests|FullyQualifiedName~EvolutionMediaTests|FullyQualifiedName~Inbound|FullyQualifiedName~Outbox|FullyQualifiedName~CatalogCallbackTests|FullyQualifiedName~CatalogRecoveryHttpTests)&Category!=Integration&Category!=RedisIntegration&Category!=ProviderIntegration' --logger 'trx;LogFileName=s8-local-pilot-contracts.trx' --results-directory backend/NexFlow.Tests/TestResults/mvp-s6-s7-s8`.
- Descubrimiento `dotnet test ... --no-build --no-restore --list-tests --filter Category=<categoría>`: **39 Integration, 1 RedisIntegration y 2 ProviderIntegration**, todas con **0 ejecuciones externas** en esta tarea. Logs discovery-* conservados. No arrancar almacenes para producir omisiones verdes.
- [PLAN_PILOTO_E2E_WHATSAPP_MVP.md](PLAN_PILOTO_E2E_WHATSAPP_MVP.md): 32 cupos multitur distribuidos entre clínica multisede/comercio/mixto/limitado; rúbrica y oráculos de seguridad públicos; textos/expectativas nuevos reservados al evaluador externo. No se inventó corpus privado ni aprobación ciega. Presupuesto/modelos/ventana y runner independiente PENDIENTES.
- Revisión estática del panel y rutas existentes: business.service.ts, faq.service.ts, servicios/catalog.service.ts, admin.service.ts; settingsTabs/useBusinessProfile/WhatsAppTab; Business/Services/Catalog/ClientsController y TenantCapabilityFilter. TenantCapabilityFilter global (Program.cs:143–144) aplica permisos por módulo/HTTP/acción; SuperAdmin controla provisión/licencia/asignación; conexión/desconexión son explícitas con CONFIGURE. No se reemplazan permisos backend por UI. UI real/roles efectivos requieren CI/piloto posterior.
- Precisión de contrato: FAQ actual está particionada por workspace; el parámetro locationId enviado por el cliente no demuestra filtrado backend por sede. No se promete una capacidad inexistente ni se hace un parche fuera del alcance. El piloto valida el scope realmente contratado/configurado.
- [GO_LIVE_CHECKLIST_NEXFLOW_MVP.md](GO_LIVE_CHECKLIST_NEXFLOW_MVP.md): identidad de tres runs, ARM64+codecs y permisos fundamentales (sin exigir 600 como publish normal); Q/backup/fiscal; Compose derivado de labels reales y diff efectivo sólo de imagen; cinco minutos de admisión cerrada/drenaje; reemplazo sólo API con `--no-deps --no-build --pull never`; health sanitizado y dependencias separadas; responsables/abortos. Ningún comando operacional fue ejecutado.
- Rollback no demostrado: no autorizar `0da524…`, no asumir segura la imagen antigua reportada. Se requiere prueba aislada de compatibilidad; si no existe, detener admisión y fix-forward previamente aprobado. No Down/restauración in-place, no reenvío de UnknownDelivery/DLQ ni reversión ficticia de reservas/pedidos.

**Gate S8 local/documental OK — READY_FOR_OPERATOR. Gate externo PENDIENTE; NO-GO para desplegar, activar piloto o comercializar.** No se inicia otro sprint ni se modifica la API activa.

## Hallazgos y pendientes priorizados

| Severidad / ID | Evidencia local | Estado y criterio de cierre |
|---|---|---|
| P1 release R6-01 | Workflows manuales inspeccionados; G8 local 1538/1538 | CI real NO CONSULTADO/PENDIENTE; tres Run ID/attempt, mismo SHA y artefactos completos |
| P1 release R6-02 | Dockerfile:20–34; CodecSmoke/Program.cs:6–30; Docker ausente | ARM64/UID/permisos 600–700/codecs PENDIENTES; no atribuir imagen anterior |
| P1 recuperación R7-01 | SDK disponible y propuesta de copia lógica; caché no autoritativa | Backup/restore Firestore no implementado/certificado aquí; datos no descartables y venta NO-GO |
| P2 privacidad R7-02 | TaxId en payload :101, no en perfil público | Q10/plantilla/visual/política PENDIENTES; no alterar fiscal por suposición |
| P2 coherencia R7-03 | Cinco/cuatro verificado por variante, CatalogGenerationTests:25 | Workspace real NO VERIFICADO; cerrar Q03/Q04 por ítem, no por conteo |
| P1 operativo R7-04 | Matriz de sólo lectura preparada; GetArtifactAsync escribe | Q01–Q10 reales PENDIENTES; schema y parámetros validados por operador antes de consultar |
| P1 calidad R8-01 | 32 cupos públicos, corpus/evaluador privados no recibidos | Independencia, proveedores, presupuesto y WhatsApp PENDIENTES |
| P1 operativo R8-02 | Sin migraciones locales nuevas; estado operativo desconocido | Identidad/Compose/admisión/drenaje y rollback/fix-forward PENDIENTES; sólo API tras aprobación |

No hay nuevo defecto reproducido de seguridad, cruce de tenant, doble efecto, consentimiento, rollback o F7-01 en estas verificaciones que active STOP extraordinario. No significa ausencia global de defectos. Ninguna prueba roja se silenció ni se introdujo un parche para obtener verde; no se modificaron código, tests, contratos, CI o infraestructuras.

## Entrega, diff y límites de garantía

HEAD final: `be07f89c2146f7c4344effad849e1335c9614e86`. Backend/frontend/CI/AGENTS y documentos históricos sin cambios frente al inicio. No nuevo SHA publicado ni imagen construida. Diff propio = **cuatro Markdown nuevos**:

1. `docs/auditorias/IMPLEMENTACION_S6_S7_S8_MVP_2026-10-26.md`
2. `docs/auditorias/MATRIZ_Q01_Q10_VALIDACION_MVP.md`
3. `docs/auditorias/PLAN_PILOTO_E2E_WHATSAPP_MVP.md`
4. `docs/auditorias/GO_LIVE_CHECKLIST_NEXFLOW_MVP.md`

Los dos Markdown sin seguimiento del propietario se conservan aparte. Artefactos .NET/TRX/logs/JSON nuevos son salida local ignorada por Git; no se copiaron secretos ni datos reales. Estos resultados corresponden al código be07f89c sin cambios productivos, no a CI o a la API histórica. Los conteos entre suites se superponen: no sumar como cobertura única.

Validación final: `git diff --check` sin errores; revisión del contenido propio, links locales/fences Markdown y parse sintáctico del ejemplo Python del checklist sin ejecutarlo. SQL, Bash/Docker/Compose, backups y operadores **no ejecutados ni certificados contra servidor**. El código no cambió después del build y los gates; no se repite build por cambios sólo documentales. Tres advertencias preexistentes siguen visibles.

| Decisión | Resultado |
|---|---|
| GO revisión local | **GO**: S6/S7/S8 secuenciales y evidencia comprobada |
| Paquete operacional | **READY_FOR_OPERATOR**: instrucciones/evidencia y responsables preparados; parámetros reales por completar |
| Certificación técnica externa / release | **NO-GO** hasta CI mismo SHA, integraciones/Redis reales y smoke ARM64 aprobados |
| GO piloto controlado | **NO-GO actual**: falta autorización/datos/config/seguridad, corpus/proveedores/canal y gates técnicos; sin restore sólo podrá proponerse DEMO descartable |
| Comercialización/datos no descartables | **NO-GO**: PDF/política, recuperación, evaluación humana/proveedores, costos, soporte y evidencias reales pendientes |

Siguiente paso seguro: auditoría del paquete; el propietario puede autorizar ejecución de CI y smoke aislado, luego lecturas Q01–Q10 y aprobaciones de recuperación/privacidad/piloto en sesiones separadas. **No quedan cambios de código locales independientes demostrados. STOP antes de commits, push, dispatch, migraciones, producción o Sprint 9.**
