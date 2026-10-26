# NexFlow MVP — implementación y certificación local S3/S4/S5

## Identidad y alcance

HEAD inicial `f7efb1c79948a13c4d675d06e9cfb1e24a33dc2a`, rama main. Árbol de código inicialmente limpio; se conservan sin editar los dos documentos del propietario, inicialmente sin seguimiento: `docs/NexFlow_Auditoria_S1_S2_2026-10-26.md` y `docs/NexFlow_Prompt_Codex_Sprints_3_4_5_2026-10-26.md`. No se consultó el remoto ni Oracle. La coincidencia con el main publicado se refiere al SHA indicado por el propietario, no a una consulta remota de esta tarea. Producción histórica: `306d7c8…-perms1`; no es este árbol.

Se leyó el diff efectivo S1/S2 y la documentación requerida. Se preservan S1/S2, motores, CAS, FIFO, idempotencia, consentimiento, toma humana y bloqueo de citas privadas. Sin migraciones, servicios reales, cambios frontend, secretos, commit/push/CI remoto/despliegue.

## Checkpoint Sprint 3 — completado antes de editar Sprint 4

### Rojo antes del parche

`s3-before.trx`: **59 ejecutadas, 55 aprobadas, 4 fallidas, 0 omitidas**. Incluye regresiones S1/S2. Fallos nuevos funcionales:

- `RuntimeReadToolsTests.Mvp_general_information_does_not_let_a_faq_replace_the_public_profile(true)`: FAQ coincidente sustituye la actividad/perfil público. Variante no coincidente ya funciona.
- Tres variantes de `RuntimeOrderRequestTests.Mvp_order_capability_is_information_followed_by_explicit_preparation`: «¿Se puede hacer un pedido?», «¿Venden por WhatsApp?» y «¿Cómo encargo productos?»; devolvían aclaración genérica o búsqueda sin resultados, sin explicar la capacidad autorizada.

### Cambios mínimos

- ChatFlow: presentación general con acto Information usa Profile, sin anteponer una FAQ por coincidencia textual. Faq explícito conserva su ruta; General legacy sin ese acto conserva compatibilidad. No se deduce una nueva intención mediante diccionarios ni se usan datos LLM como fuente comercial. Pruebas con FAQ coincidente/no coincidente y dos workspaces comprueban perfil público y ausencia de TaxId; después verifican que Faq explícito sí responde la FAQ autorizada.
- CommerceTurnResolver: consume el acto existente OperationInformation/Order sólo con evidencia literal única, sin directiva ni RequiresClarification. Consulta capacidad mediante licencias y catálogo PRODUCT actual de la sede del contexto, sin escribir el borrador. Una pregunta sin entidad/referencia inequívoca puede llegar al intérprete existente en vez de devolver un falso «no encontré». No se cambia Start/Submit ni el motor de pedidos.
- AiInterpreter: aclara el contrato de interpretación de capacidad Order/OperationInformation y Profile, sin añadir campos, herramientas de escritura ni otro router.
- Regresiones: capacidad con/sin ORDERS/CATALOG, sólo SERVICES+RESERVATIONS, catálogo vacío/otra sede, revocación, interrupción de reserva, evidencia insuficiente y comienzo explícito posterior. Cero solicitudes por una pregunta; el inicio vacío y el envío PendingReview se conservan.

### Gate S3

- `s3-directed.trx`: **95/95**.
- Primer ensayo ampliado `s3-expanded.trx`: **102 ejecutadas, 101 aprobadas, 1 fallida** por texto de denegación supuesto en una prueba nueva. Se ajusta esa expectativa al contrato existente «No hay información de productos disponible en este momento», conservando cero lecturas sin CATALOG. Un intento intermedio de usar la constante interna desde tests produjo CS0122; se corrigió sólo esa referencia de prueba. Logs conservados; no se alteró visibilidad productiva.
- `s3-directed-final.trx`: **103/103**, sin fallos/omisiones. Incluye CommerceSafetyTests (F7-01: cotización, precio, cantidad, sede, licencia, consentimiento y recuperación durable).
- `dotnet build backend/backend.slnx --no-restore`: **0 errores, 0 advertencias** en build incremental (`mvp-s3-build-final.log`).
- Runner G8 vigente sin modificaciones: **1528/1528**, 0 fallidas/omitidas (`s3-checkpoint.trx`, `.g8.json` = PASS).
- Gate S3 local **OK**. No nuevo defecto de privacidad, consentimiento, duplicación o aislamiento demostrado. Se autoriza continuar S4 conforme al encargo; esto no certifica proveedores o producción.

## Sprint 4

Completado después de registrar el checkpoint S3.

### Evidencia y cambio

- Prueba inicial `s4-before.trx`: **2 ejecutadas, 1 aprobada, 1 fallida, 0 omitidas**. El aislamiento pasó usando una misma instancia de BusinessReadTools/KnowledgeService y un mismo InMemoryFirestore existente, con repositorios Firestore reales: workspaces y contactos distintos, IDs de offering/FAQ iguales, catálogos SERVICE/PRODUCT diferentes y módulos diferentes. Un ID de servicio de A no se interpreta como producto en B; la denegación/revocación impide nuevos RPC al almacén; los contextos no se cruzan.
- Único rojo: faltaba el tipo de herramienta en ConversationPhase. KnowledgeService añade `reason: query.Topic.ToString()` a la instrumentación existente. Se prueba Phase, Outcome, tipo y latencia, sin datos del negocio ni workspace en esa traza. Sin nuevo servicio de observabilidad ni cambios de consultas/validaciones. Aviso xUnit2031 de la nueva prueba corregido usando el overload recomendado de Assert.Single.
- CatalogChatTests añade dos comprobaciones de metadatos de PDF ajenos (workspace/scope): guardas existentes rechazan antes de leer conocimiento o notificar. **NO REPRODUCIDO** como defecto: no se cambia ChatFlow para ese caso. No se presupone que una URL por sí sola acredite propiedad o semántica.
- Se reutilizan RuntimeRealizationTests (FAQ hostil como dato, takeover y revocación durante composición), AssistantDurabilityTests (presupuesto, proveedor/JSON, replay), AssistantRedisRecoveryTests (durable prevalece ante caché obsoleta/fallo/expiración), AssistantRc1ConcurrencyTests y ExistingReservationSprint2Tests. Sin lecturas privadas de citas ni habilitación de bypass de identidad.
- Trazas conservadas: UnderstandingRoute Local/Semantic, DialogueDecision/ResponsePlan, KnowledgeQuery con tipo/estado/latencia, OfferingProductsRead/OfferingServicesRead, etapas durable/outbound y telemetría IA. RuntimeEfficiencyTests mide RPC del fake existente y llamadas semánticas; no equivale a latencia, tarifa ni consumo de producción.

### Matriz tipada vigente

| Intención/operación | Herramienta/flujo | Fuente | Autorización |
|---|---|---|---|
| Profile/presentación | ChatFlow → BusinessReadTools → KnowledgeService | business/profile | Workspace autenticado, proyección pública sin TaxId |
| Faq | KnowledgeRead(Faqs) | faqs activas de W | FAQ antes/después de lectura |
| Location | KnowledgeRead(Locations) | locations de W | LOCATIONS |
| BusinessHours | KnowledgeRead(BusinessHours) | hours/global o hours/sede | BUSINESS_HOURS; ausencia ≠ cerrado |
| ProductQuery | CommerceTurnResolver/OfferingService | catalogItems PRODUCT | CATALOG |
| ServiceQuery | Idem SERVICE | catalogItems SERVICE | SERVICES |
| Disponibilidad/reserva | BookingFlow → ReservationEngine | horario/servicio/perfil + Reservations | RESERVATIONS+SERVICES, consentimiento vigente para crear |
| Capacidad/preparación/envío pedido | CommerceTurnResolver → OrderFlow | PRODUCT → orders + aviso | ORDERS+CATALOG; envío tras cotización/consentimiento |
| Request | RequestFlow | requests de W | REQUESTS independiente |
| Support / cita existente | SupportFlow/ExistingReservationFlow | Handoff durable | Sin leer agenda privada antes de acreditación independiente |
| Folleto | CatalogGenerationService/ChatFlow | artefacto/productos o servicios de W | Módulo del scope, CURRENT, identidad/hash/generación/callback vigentes |

Reservar conserva sus lecturas internas de sede/horario sin habilitar consultas informativas independientes. El backend no entrega al LLM SQL, Firestore ni herramientas autónomas de escritura.

### Gate S4

- Dirigidas finales `s4-directed.trx`: **499/499**, 0 fallidas/omitidas.
- Build `dotnet build backend/backend.slnx --no-restore`: **0 errores, 0 advertencias** incremental (`mvp-s4-build.log`).
- Contrato vigente G8: **1532/1532**, 0 fallidas/omitidas (`s4-checkpoint.trx` y JSON PASS).
- Gate local **OK**; sin defecto crítico nuevo de seguridad/integridad que active la parada. No se ejecutaron integraciones reales: no se encontraron Docker, psql o redis-server ni listeners locales en 5432/8080/16379. Firebase CLI existe, pero no hay un conjunto aislado disponible. No se inician bases, emuladores ni migraciones; no se leen credenciales de aplicación.

## Sprint 5

Completado localmente después del checkpoint S4. Ningún cambio productivo adicional.

### Escenarios y alcance de la certificación

- `RuntimeEfficiencyTests.Mvp_configured_businesses_share_the_runtime_and_keep_read_interruptions_and_failure_safe`: clínica SERVICES+RESERVATIONS, comercio CATALOG+ORDERS y mixto, usando el mismo runtime existente y configuraciones sintéticas distintas. Perfil por KnowledgeService real, servicios/precio concreto, sede informativa, fecha/hora, reserva con resumen/consentimiento separado; capacidad/pedido, cantidades/cotización, horarios e interrupción por ProviderUnavailable. Se compara el borrador antes/después; replay no duplica la reserva/solicitud PendingReview. Los motores/transportes del fixture son simulados: no acredita commit SQL ni entrega WhatsApp real.
- Primer ensayo `s5-scenarios.trx`: **18 ejecutadas, 16 aprobadas, 2 fallidas**. Diagnóstico adicional `s5-scenario-diagnostic.trx`: **3 ejecutadas, 1 aprobada, 2 fallidas**. Causa: el nuevo guion asumía que «a las 10» en una respuesta compuesta completaría la reserva. El backend conservaba fecha/sede/servicio y pedía legítimamente precisar la hora (COLLECT_TIME). **No era pérdida de borrador ni defecto de consentimiento.** Se completó el guion contestando «10:00»; se mantuvo la exigencia de resumen y consentimiento, sin cambiar producción ni pruebas originales. `s5-scenarios-final.trx`: **18/18**.
- `BusinessProfileApiTests.Mvp_onboarding_uses_the_existing_authorized_profile_configuration`: tres altas/configuraciones sintéticas vía PUT `/api/business/profile`, mismo DTO actual y America/Lima; verifica workspace del backend, nombre/actividad persistidos y una escritura lógica. Reutiliza ApiFixture HTTP existente, sin endpoints/campos/roles nuevos. Los casos existentes sin licencia/capacidad UPDATE siguen en 403. `s5-onboarding.trx`: **21/21** (escenarios multitur + suite de perfil ampliada).
- Se preservan las rutas administradas actuales de categorías (`/api/catalog/categories`), PRODUCT (`/api/catalog`), SERVICE (`/api/services`), sedes/horarios/FAQ (`/api/business/...`) y conexión explícita de WhatsApp. Estas pruebas no conceden módulos a nadie ni certifican onboarding productivo. La licencia/capacidades debe prepararla el administrador por el mecanismo existente, sin SQL manual ni endpoints inventados.
- Flujo inspeccionado y cubierto por la regresión local: EvolutionWebhookController/InstanceResolver → InboundMessageRepository y worker/FIFO → handler/orquestador/pipeline → herramientas y motores → estado durable/outbound. PDFs: generación/Outbox/n8n/callback y estados del artefacto. Se reutilizan pruebas HTTP, Inbound/Outbox, Catalog*, AssistantRc1ConcurrencyTests, CommerceSafetyTests y EvolutionMediaTests del contrato vigente. La entrega física de proveedores y el motor PostgreSQL real siguen pendientes.

### Resultado final real

| Ejecución | Ejecutadas | Aprobadas | Fallidas | Omitidas |
|---|---:|---:|---:|---:|
| S3 checkpoint vigente | 1528 | 1528 | 0 | 0 |
| S4 checkpoint vigente | 1532 | 1532 | 0 | 0 |
| S5 vigente antes de añadir onboarding | 1535 | 1535 | 0 | 0 |
| S5 vigente FINAL (`s5-final-current.trx`) | **1538** | **1538** | **0** | **0** |
| S5 históricos FINAL (`s5-final-historical.trx`) | **130** | **118** | **12** | **0** |

No sumar ejecuciones repetidas como cobertura única. S1/S2 partían de 1515; se añaden 23 casos en clases existentes. TRX, JSON del gate y logs locales: `backend/NexFlow.Tests/TestResults/mvp-s3-s4-s5/` y `mvp-s*.log` en TestResults, ignorados por Git. Los rojos y ensayos intermedios se conservan.

Build final: `dotnet build backend/backend.slnx --no-restore`, **0 errores y 0 advertencias** incremental (`mvp-s5-final-build.log`). No fue necesario restore ni instalar dependencias. Frontend sin cambios: no build/lint frontend solicitado ni ejecutado para este árbol.

Runner G8 original: current = **PASS**; historical = **EXPECTED_HISTORICAL_INCOMPATIBILITIES**. Dotnet historical devuelve realmente 1; el validador devuelve 0 sólo tras comprobar exactamente 130/118/12, inventario, hashes congelados e identidad/motivo de cada fallo. Se conservan clinic/hotel/restaurant/bakery/limited -09/-10 y H10/H21 con sus motivos VerifiedFact. No son 130 verdes ni certificación ciega. No se cambiaron filtros, umbrales, corpus, evaluadores, aserciones originales ni workflows.

Descubrimiento sin ejecución (`--list-tests`): **Integration 39, RedisIntegration 1, ProviderIntegration 2**. Las tres categorías tienen **0 ejecutadas aquí**. No se lanzan para obtener omisiones por falta de dependencias ni se presentan como aprobadas. CI existente exige al menos 37 Integration y exactamente 1 Redis ejecutadas/aprobadas, sin omisiones; el inventario actual local descubre 39 Integration. Integraciones cubren recursos/migraciones/Serializable, fencing/xmin, comercio/moneda/precio/revocación, FIFO, presupuesto IA y caché real. ProviderIntegration requiere autorización/presupuesto separados.

Comandos de cierre ejecutados (PowerShell, desde la raíz):

```powershell
dotnet build backend/backend.slnx --no-restore
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --filter 'FullyQualifiedName~Mvp_configured_businesses|FullyQualifiedName~BusinessProfileApiTests' --logger 'trx;LogFileName=s5-onboarding.trx' --results-directory backend/NexFlow.Tests/TestResults/mvp-s3-s4-s5
python -B .github/workflows/g8_certification.py run current --trx backend/NexFlow.Tests/TestResults/mvp-s3-s4-s5/s5-final-current.trx
python -B .github/workflows/g8_certification.py run historical --trx backend/NexFlow.Tests/TestResults/mvp-s3-s4-s5/s5-final-historical.trx
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-build --no-restore --list-tests --filter Category=Integration
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-build --no-restore --list-tests --filter Category=RedisIntegration
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-build --no-restore --list-tests --filter Category=ProviderIntegration
git diff --check
```

S3 dirigidas: filtro `FullyQualifiedName~RuntimeNaturalBookingTests|FullyQualifiedName~RuntimeReadToolsTests|FullyQualifiedName~RuntimeOrderRequestTests|FullyQualifiedName~CommerceSafetyTests|FullyQualifiedName~AiInterpreter`. S4 dirigidas: `(FullyQualifiedName~Runtime|FullyQualifiedName~Catalog|FullyQualifiedName~Assistant|FullyQualifiedName~ZeroTokenKnowledgeAuthorizationTests|FullyQualifiedName~AiResilienceTests|FullyQualifiedName~ExistingReservationSprint2Tests)&Category!=Integration&Category!=RedisIntegration&Category!=ProviderIntegration`. Ambos con `dotnet test ... --no-restore --logger 'trx;LogFileName=...' --results-directory backend/NexFlow.Tests/TestResults/mvp-s3-s4-s5`.

## Hallazgos, estado y evidencia

| Severidad | Archivo:línea | Reproducción/prueba | Estado |
|---|---|---|---|
| P1 calidad G1 | ConversationFlows.cs:813 | FAQ coincidente sustituía perfil; RuntimeReadToolsTests:67, rojo→verde y FAQ explícita preservada | RESUELTO local |
| P1 funcional A05 | CommerceTurnResolver.cs:28 y :155; AiInterpreter.cs:135 | Tres preguntas de capacidad; RuntimeOrderRequestTests:17/:36/:60/:81 | RESUELTO local |
| P2 diagnóstico | KnowledgeService.cs:55 | RuntimeEfficiencyTests:94, Reason ausente→tipo Profile presente sin datos privados | RESUELTO local |
| Control crítico existente | OrderFlow.cs:107; CommerceSafetyTests:198/:219/:240/:265 | Cotización/consentimiento/precios/sede/licencia/CAS/recuperación | NO REPRODUCIDO como regresión; pruebas verdes |
| Control crítico existente | ExistingReservationFlow.cs:62; AssistantRc1ConcurrencyTests:22/:33/:63/:72 | Citas privadas a humano, takeover, ACK único y replay | NO REPRODUCIDO como regresión; no habilitado bypass |
| Control crítico existente | ConversationFlows.cs:794/:839; CatalogChatTests:22 | PDF de workspace/scope ajeno rechazado antes de publicación | NO REPRODUCIDO; URL sola no demuestra propiedad |
| P2 datos A02 | CatalogGenerationService.cs:88; OfferingService.cs:75 | Cinco publicados/cuatro reservables en S1/S2 se conserva | NO VERIFICADO en workspace real; Q03/Q04 |
| P2 privacidad A09 | CatalogGenerationService.cs:101; KnowledgeService.cs:194 | TaxId fuera del perfil público, presente en payload PDF; bytes/plantilla no inspeccionados | PENDIENTE decisión de producto/privacidad Q10 |
| P1 gate de release | Dockerfile:26/:33 | COPY chown no-root revisado, Docker local ausente | PENDIENTE smoke ARM64 de este árbol |
| P1 gate técnico externo | workflows rc3-reservation-integration y concierge-redis-integration | 39/1 descubiertas, 0 ejecutadas con almacenes reales | PENDIENTE CI mismo SHA |
| P1 gate comercial | ProviderCertificationTests y canal autorizado | 2 proveedores descubiertos, 0 ejecutados; sin WhatsApp real/evaluación ciega | PENDIENTE |

Rutas de aplicación relativas a `backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/Flows/` salvo AiInterpreter (`Features/AI/Interpretation/`), CommerceTurnResolver (`Features/Automation/Dialogue/Understanding/`), KnowledgeService (`Features/Knowledge/`), CatalogGenerationService (`Features/Business/`) y OfferingService (`Features/Business/Offerings/`). Líneas referidas al árbol final; los tests bajo `backend/NexFlow.Tests/`, Concierge cuando corresponde. No se encontró un nuevo P0/P1 de seguridad/duplicación/consentimiento/privacidad en las verificaciones ejecutadas. Esto no afirma ausencia global de defectos ni valida servicios reales.

## Docker/ARM64 — procedimiento pendiente, no ejecutado

Se revisó que API y migrator heredan USER `$APP_UID` y copian con `--chown=$APP_UID:$APP_UID`; no se modifica el Dockerfile en S3–S5. El modo 600 es legible por su propietario runtime, y los directorios 700 publicados son recorribles por ese mismo UID. Sigue pendiente comprobar el artefacto real y UID 1654. No se acepta la imagen antigua `-perms1` como evidencia de esta candidata.

Procedimiento para un runner Linux **desechable autorizado**, con soporte ARM64 nativo o emulación instalada previamente, nunca Oracle en esta tarea:

1. Congelar/publicar manualmente por el propietario el árbol revisado y registrar el SHA exacto. Crear un contexto temporal con sólo los proyectos de backend necesarios y archivos no secretos del checkout, mediante allowlist; excluir `.env`, credenciales, `bin/obj`, `.git` y archivos locales no versionados. No copiar una configuración real. No usar todo el directorio de trabajo sin revisar su contenido.
2. En ese contexto temporal, aplicar `umask 077`, archivos 600 y directorios 700. Construir ambos targets con tags de ese SHA. La construcción puede obtener dependencias/imágenes públicas; no lleva credenciales SaaS. Si no hay builder ARM64 funcional, PENDIENTE, no fallback a amd64 como evidencia arm64.
3. Ejecutar únicamente shell de verificación sobre cada imagen, nunca su entrypoint normal/migrator. Ejemplo (variables de contexto/tag definidas por el operador):

```sh
docker buildx build --platform linux/arm64 --target final --load -t "$API_TAG" -f "$CTX/Dockerfile" "$CTX"
docker buildx build --platform linux/arm64 --target migrator --load -t "$MIGRATOR_TAG" -f "$CTX/Dockerfile" "$CTX"
docker image inspect --format '{{.Os}}/{{.Architecture}} user={{.Config.User}} id={{.Id}}' "$API_TAG" "$MIGRATOR_TAG"
for image in "$API_TAG" "$MIGRATOR_TAG"; do
  docker run --rm --network none --read-only --cap-drop ALL --security-opt no-new-privileges \
    --entrypoint /bin/sh "$image" -c 'test "$(id -u)" = 1654 && test -x /app && test -r /app/appsettings.json && test "$(stat -c %a /app/appsettings.json)" = 600 && stat -c "%u:%g %a" /app/appsettings.json && dotnet --list-runtimes'
done
```

4. Atestar platform arm64, UID/GID/modo y runtime .NET 10; almacenar logs sanitizados más SHA/digest cuando exista imagen publicada. Si publish normaliza el modo a otro valor, investigar el caso 600 mediante un stage temporal aislado de ensayo, no relajar la aserción silenciosamente. No ejecutar migrator, no montar credenciales ni conectar servicios; quitar `--network none` invalida este smoke. Codec/arranque real requieren su smoke independiente: no se declaran aprobados aquí.

## Runbook Q01–Q10: sólo lectura por operador autorizado

**No ejecutado.** No autoriza acceso productivo. Requiere autorización posterior, rol de lectura, workspace DEMO y ventana/identificadores de prueba confirmados. Parámetros enlazados, timeout 5 s, máximo 20 fuentes de prueba y límites de resultados; nunca concatenar datos del usuario/LLM. No imprimir teléfonos, nombres de clientes, prompts, secretos, errores brutos, PayloadJson/contextJson completos ni URLs privadas. Usar alias técnicos consistentes. Una lectura no debe invocar recuperación o normalización que escriba estado.

| Consulta | Operación exacta acotada | Salida permitida |
|---|---|---|
| Q01 imagen/permisos | `docker container inspect --format '{{.Config.Image}} {{.Image}} {{.Config.User}}' <api>` y `docker image inspect --format '{{.Os}}/{{.Architecture}} {{.Id}}' <imagen>`; operador autorizado puede consultar `id`/`stat /app/appsettings.json /app` sin leer contenido | Tag/digest/SHA, UID/GID/modos. No inspect completo ni env |
| Q02 módulos efectivos | SELECT parametrizado siguiente con W y reloj UTC, mismas reglas de EntitlementService | Booleano de licencia/workspace válido y códigos activos; capacidades web revisadas por rol existente, no heredadas del bot |
| Q03 ofertas/sedes | `workspaces/W/catalogItems` WHERE Type==SERVICE y luego PRODUCT, LIMIT 501; proyección Type/Name/CategoryId/IsActive/RequiresReservation/DurationInMinutes/LocationScope/LocationIds y precios/moneda públicos. Categorías LIMIT 101 y locations LIMIT 101. Si rebasa 500/100, detener/paginar con autorización | Máximo cinco filas públicas del incidente, IDs/sedes con alias, categoría publicable, entra P/entra R(S), razón y orden real. Aliases sólo si son públicos/aprobados, no Metadata arbitraria |
| Q04 PDF | GetSnapshot directo `workspaces/W/catalogArtifacts/current_service` y `current_product`; proyectar Status/Origin/SourceHash/GenerationId/LastGeneratedAt y update-time del snapshot. Comparación interna del hash contra fuente consistente, sin exportar campos fiscales/contactos | Scope, origen, estado, tiempos, `hashMatches`, generación seudonimizada. No GET /catalog/artifact ni GetArtifactAsync (pueden reconciliar y escribir) |
| Q05 horarios | GetSnapshot `workspaces/W/business/profile` → sólo TimeZone; `hours/global/schedule` y `hours/S/schedule` → días/rangos/IsClosed. Usar sede validada por Q03 | Zona, calendarios públicos; ausencia ≠ cerrado; duración y UTC esperados. No llamar al motor para diagnosticar en sólo lectura |
| Q06 contexto | GetSnapshot directo de `conversationStates/<contacto de prueba>`; procesar contextJson sólo en memoria del operador y descartar | Goal/step, flags de campos completos, fecha/hora sintéticas, tipo/intención/validez de CurrentQuestion, edad de referencia, versión seudonimizada. Nunca JSON completo/identidad del titular |
| Q07 decisión/IA | Sólo trazas estructuradas de la ventana de prueba: UnderstandingRoute, DialogueDecision, KnowledgeQuery/Reason, outcome/ElapsedMs, AiUsage/Failure y handler | Conteos de rutas/proveedores, tipo de lectura/estado, latencias agregadas y tokens reportados. Sin prompt/respuesta; separar presupuesto de admisión de POST facturados |
| Q08 reserva/evento | SELECT por W+SourceMessageId de prueba, siguiente ejemplo; correlación de Outbox por W+Data.Id | Una reserva Confirmed/UTC/duración esperada y un evento lógico; no confundir evento con entrega externa |
| Q09 inbound/outbound | SELECT agrupado siguiente, sin mutaciones. Outbound: GetSnapshot por IDs SHA256 de las claves existentes, seleccionando únicamente status/kind/timestamp/transportStartedAt/transportLeaseUntil | Conteos/latencias/pendientes por fuente seudonimizada, Sent/Failed/UnknownDelivery. Sin reenviar ni llamar a TryResumeResponseAsync/GetOutbound normalizador |
| Q10 política/plantilla | Propietario revisa campos autorizados del payload y plantilla/PDF público suministrado de prueba, sin obtener objetos privados desconocidos | Atestación de publicación de TaxId sí/no y campos aprobados, cinco ofertas correctas y scope. CURRENT/hash no certifican semántica de bytes |

Ejemplos SQL **no ejecutados**, parámetros Npgsql enlazados por el operador; no son scripts psql con interpolación. Envolverlos en transacción READ ONLY con `SET LOCAL statement_timeout='5s'` y terminar con ROLLBACK. No ejecutar endpoints ni aplicar DDL/migraciones para obtener evidencia:

```sql
-- Q02. @workspace_id uuid; @now timestamptz fijado para la ventana.
SELECT COALESCE(w."Status" IN ('Active','Pending') AND l."Status"='Active'
       AND l."ValidFrom"<=@now AND (l."ValidTo" IS NULL OR l."ValidTo">=@now),FALSE) AS valid,
       ARRAY_REMOVE(ARRAY_AGG(m."Code" ORDER BY m."Code") FILTER (WHERE m."Status"='Active'),NULL) AS assigned_active_modules
FROM "Workspaces" w LEFT JOIN "Licenses" l ON l."WorkspaceId"=w."Id"
LEFT JOIN "LicenseModules" lm ON lm."LicenseId"=l."Id"
LEFT JOIN "Modules" m ON m."Id"=lm."ModuleId"
WHERE w."Id"=@workspace_id GROUP BY w."Status",l."Status",l."ValidFrom",l."ValidTo";
-- Si valid=false, ningún código asignado equivale a módulo efectivo autorizado.

-- Q08. No devolver datos del consumidor ni el identificador de reserva.
WITH r AS (SELECT "Id","Status","LocationId","ServiceId","StartTime","EndTime"
 FROM "Reservations" WHERE "WorkspaceId"=@workspace_id AND "SourceMessageId"=@source_id)
SELECT (SELECT COUNT(*) FROM r) AS reservations,
 (SELECT COALESCE(BOOL_AND("Status"='Confirmed' AND "LocationId"=@location_id
  AND "ServiceId"=@service_id AND "StartTime"=@expected_utc AND "EndTime"=@expected_end),FALSE) FROM r) AS expected_reservation,
 (SELECT COUNT(*) FROM "OutboxMessages" o JOIN r ON o."PayloadJson"::jsonb->'Data'->>'Id'=r."Id"::text
  WHERE o."WorkspaceId"=@workspace_id AND o."EventType"='RESERVATION_CREATED') AS events,
 (SELECT COALESCE(BOOL_AND(o."PayloadJson"::jsonb->>'IdempotencyKey'='res_'||r."Id"::text),FALSE)
  FROM "OutboxMessages" o JOIN r ON o."PayloadJson"::jsonb->'Data'->>'Id'=r."Id"::text
  WHERE o."WorkspaceId"=@workspace_id AND o."EventType"='RESERVATION_CREATED') AS expected_key;

-- Q09. Status inbound: 0 Pending, 1 Processing, 2 Processed, 3 Failed, 4 DeadLetter.
SELECT "Status",COUNT(*) AS messages,MAX("Attempts") AS max_attempts,
 AVG(EXTRACT(EPOCH FROM ("ProcessedAt"-"ReceivedAt"))) AS processed_seconds
FROM "InboundMessages" WHERE "WorkspaceId"=@workspace_id
 AND "ReceivedAt">=@from_utc AND "ReceivedAt"<@to_utc
 AND "ExternalMessageId"=ANY(@test_source_ids)
GROUP BY "Status" ORDER BY "Status";
```

Outbound Q09: el operador calcula internamente `Convert.ToHexString(SHA256(UTF8(key)))`, con `key=$"{W:N}:AI:{source}:response:1"` para texto o `$"{W:N}:AI:{source}:response:document:{scope}:1"` para PRODUCT/SERVICE, y consulta `workspaces/W/outboundMessages/{hash}`. No se exporta key, teléfono, source ni contenido. La ruta y representación vienen de OutboundMessageService:37–38/FirestoreConversationRepository:21. Un POST aceptado/Sent no demuestra entrega/lectura en WhatsApp; UnknownDelivery no se convierte en reintento.

## Política PDF y recuperación

Mantener TaxId sin cambios hasta decisión explícita del propietario: alternativa propuesta = omitir/minimizar por defecto su proyección PDF y permitirla sólo bajo una política aprobada. **Propuesta, no implementación ni contrato nuevo.** Revisar plantilla, payload y documento final antes del piloto; el origen Uploaded necesita validación humana del contenido. La proyección pública Knowledge ya excluye el campo fiscal. No modificar precios/hash/Origin para hacer coincidir un PDF.

Recuperación Firestore **PENDIENTE**: Spark no se considera respaldado por tener Redis o PostgreSQL. El operador debe acreditar mecanismo de exportación/retención permitido y una restauración aislada autorizada; no se activa billing, no se altera el plan y no se toma/restaura un backup aquí. No restaurar contexto/Outbox antiguo encima del estado reciente ni reenviar UnknownDelivery/DeadLetter para aparentar recuperación. Si no hay evidencia recuperable, bloquear expansión comercial.

## Protocolo de piloto y checklist comercial (sin ejecutar)

1. Tras revisión independiente, propietario congela una candidata nueva por SHA. Ejecuta manualmente los workflows existentes de release, PostgreSQL/Firestore y Redis sobre **ese mismo SHA**. Conservar Run ID/run_attempt, identity.json y TRX con inventario completo y sin omisiones. Los JSON locales de esta tarea tienen GITHUB_SHA/RUN_ID nulos, no son evidencia remota.
2. Aprobar workspace DEMO desechable, contacto/número de prueba autorizado, módulos/capacidades y ventana. Alta por módulos existentes: perfil y America/Lima; sedes/horarios; PRODUCT o SERVICE con estado/precio/moneda/scopes/duración/RequiresReservation; FAQ públicas; artefacto vigente revisado. Conectar Evolution sólo por acción explícita, sin tocar sesiones ajenas ni exponer claves.
3. Un evaluador independiente prepara fuera del repositorio al menos 30 conversaciones inéditas y rúbrica humana de naturalidad/claridad/utilidad. No revelar expectativas privadas al implementador. Evaluar sobre SHA congelado y no atribuirlo a suites sintéticas o corpus históricos.
4. Antes de llamadas reales Groq/Gemini, propietario aprueba ambos proveedores/modelos, **máximo de llamadas, tokens/costo y duración concretos**. Si falta cualquiera, no iniciar. Ejecutar el harness ProviderIntegration ya existente con datos sintéticos y credenciales administradas fuera de logs; registrar éxito/fallo, timeout/429/503, latencia y uso reportado. No hay permiso de usar crédito en esta tarea.
5. WhatsApp posterior autorizado: perfil/FAQ, productos/servicios, precio, sede/horario, disponibilidad→acuse→elección→resumen→consentimiento, pedido→cotización→envío PendingReview. Interrumpir/retomar y pedir humano; con citas privadas sólo handoff. Comprobar Q01–Q10, persistencia única, visibilidad panel/roles, no cruces entre workspaces DEMO, estados inbound/outbound y PDF PRODUCT/SERVICE. E2E cruza una dependencia a la vez; detener ante consentimiento indebido, fuga o duplicación.
6. Medir p50/p95 y errores de cola/Outbox/proveedor/entrega, llamadas IA y tokens observados; definir umbrales y presupuesto con el propietario antes de la prueba. No se inventan latencias/costos a partir del fake. Conservar sólo evidencias sanitizadas y separar Sent, delivered y read si el canal ofrece esas confirmaciones.
7. Antes de venta: alta/licencia válida y módulos correctos; roles mínimos; oferta y horarios revisados por el negocio; PDF/política fiscal aprobados; sesión WhatsApp exclusiva; consentimiento y privacidad del usuario; humano/soporte con responsable y horario; instrucciones de fallos/UnknownDelivery; recuperación Firestore probada; rollback de código compatible con datos/contratos actuales. No campaña ni clientes reales en esta tarea.

## Archivos, diff y decisiones finales

**S3 backend:** AiInterpreter.cs; CommerceTurnResolver.cs; ConversationFlows.cs. Pruebas: RuntimeOrderRequestTests.cs y RuntimeReadToolsTests.cs. **S4 backend:** KnowledgeService.cs; pruebas RuntimeReadToolsTests.cs, RuntimeEfficiencyTests.cs y CatalogChatTests.cs. **S5 sólo pruebas:** RuntimeEfficiencyTests.cs y BusinessProfileApiTests.cs (helper de conocimiento existente reutilizado entre pruebas). Rutas completas constan en `git diff --name-only`; código productivo limitado a cuatro archivos de Application, cinco de tests y este único informe nuevo. Frontend, Dockerfile, infraestructura, esquemas, secretos y documentos previos sin cambios. Dos documentos del propietario siguen sin seguimiento y no son obra de este diff.

Inventario exacto del cambio propio:

- `backend/NexFlow.Application/Features/AI/Interpretation/AiInterpreter.cs`
- `backend/NexFlow.Application/Features/Automation/Dialogue/Understanding/CommerceTurnResolver.cs`
- `backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/Flows/ConversationFlows.cs`
- `backend/NexFlow.Application/Features/Knowledge/KnowledgeService.cs`
- `backend/NexFlow.Tests/BusinessProfileApiTests.cs`
- `backend/NexFlow.Tests/CatalogChatTests.cs`
- `backend/NexFlow.Tests/Concierge/RuntimeEfficiencyTests.cs`
- `backend/NexFlow.Tests/Concierge/RuntimeOrderRequestTests.cs`
- `backend/NexFlow.Tests/Concierge/RuntimeReadToolsTests.cs`
- `docs/auditorias/IMPLEMENTACION_S3_S4_S5_MVP_2026-10-26.md`

HEAD final permanece `f7efb1c79948a13c4d675d06e9cfb1e24a33dc2a`; árbol modificado sin commit. No existe todavía SHA publicado que identifique estos cambios. `git diff --check` sin errores; sólo avisos LF/CRLF del checkout. Se revisó el diff propio de aplicación/tests; no se sobrescribieron cambios ajenos ni se alteraron históricos.

| Gate | Decisión | Evidencia/bloqueo |
|---|---|---|
| Código local S3/S4/S5 para auditoría | **GO** | Checkpoints secuenciales, build 0 errores, 1538/1538 vigente; históricos 118/130 con exactamente 12 incompatibilidades verificadas |
| Candidata técnica de release | **NO-GO externo / GO sólo revisión local** | Falta congelar nuevo SHA, CI mismo SHA, 39 integraciones + Redis real y smoke ARM64/600 |
| Piloto WhatsApp aislado | **NO-GO por ahora** | Requiere gates técnicos, Q01–Q10, política PDF, número/workspace/proveedores/presupuesto autorizados y aprobación posterior |
| Calidad conversacional/comercialización | **NO-GO** | Sin evaluación ciega/humana independiente, Groq/Gemini/WhatsApp reales ni recuperación Firestore/soporte acreditados |

Próxima acción segura: auditoría independiente del diff y de estas evidencias. Si se aprueba, el propietario podrá publicar/congelar manualmente otra revisión y autorizar sus gates externos. **Detención antes de commit, push, CI remoto, migraciones, despliegue o nueva fase.**
