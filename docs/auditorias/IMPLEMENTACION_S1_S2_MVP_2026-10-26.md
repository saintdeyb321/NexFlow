# Implementación Sprint 1 + Sprint 2 MVP — 2026-10-26

## Identidad y límites

HEAD inicial: `7bff779e6940f27f2af8efb61ef7a06242735088`, árbol inicialmente limpio. Frente a `306d7c8af41aacfd14c71d949da039f628beed95` sólo había seis Markdown añadidos, sin diferencias productivas. Sin cambios de rama, commit, push, despliegue, migraciones, servicios reales ni proveedores IA.

## Checkpoint Sprint 1 (completado antes de editar Sprint 2)

- Reproducción inicial: 11 ejecutadas, 8 aprobadas, 3 fallidas, 0 omitidas (`s1-before.trx`). Dos fallos: «vale» invalidaba la referencia informativa; otro: escribir la hora exacta no recuperaba la fecha de la consulta.
- Causa: la recuperación cambiaba la intención de la pregunta informativa y la selección literal de hora no utilizaba la referencia disponible. Se conserva la intención y se comparte la selección validada, sin convertir un acuse en consentimiento.
- Referencia: workspace, TTL, pregunta vigente, huella del borrador, sede/servicio y fecha/hora válidas. La selección vuelve por el pipeline y los controles CAS/disponibilidad existentes. La consulta no modifica el borrador y no renueva artificialmente la observación.
- Regresiones: literal/semántica, «vale», confirmación separada, replay, expiración, workspace ajeno, cambio de sede y ocupación posterior. A03 **NO REPRODUCIDO** con evidencia literal autorizada: se conserva el resolutor existente. Menú real 2 = Evaluación odontológica, 4 = Limpieza dental; una errata sin evidencia no selecciona arbitrariamente.
- Dirigidas: `RuntimeNaturalBookingTests`, 24 ejecutadas/aprobadas, 0 fallidas/omitidas (`s1-directed.trx`).
- Build: `dotnet build backend/backend.slnx --no-restore`, 0 errores y 0 advertencias en el checkpoint incremental.
- Checkpoint ampliado: 985 ejecutadas/aprobadas, 0 fallidas/omitidas (`s1-checkpoint.trx`). Filtro: `(FullyQualifiedName~Concierge|FullyQualifiedName~Assistant|FullyQualifiedName~ReservationsApiTests|FullyQualifiedName~ReservationsRepositoryTests|FullyQualifiedName~EvolutionMediaTests)&Category!=Integration&Category!=RedisIntegration&Category!=ProviderIntegration&FullyQualifiedName!~CommercialCertificationTests&FullyQualifiedName!~QualityHoldout61Tests`.
- Resultados locales: `backend/NexFlow.Tests/TestResults/mvp-s1-s2/`. Las suites históricas incompatibles conocidas no se modifican; se separan del checkpoint vigente. No se afirma integración PostgreSQL ni proveedor real mediante fixtures.

## Sprint 2

Completado después de aprobar y registrar Sprint 1.

### Reproducción y corrección A01

- Antes del parche: 7 ejecutadas, 5 aprobadas, **2 fallidas**, 0 omitidas (`s2-before.trx`). «¿De qué trata este negocio?» y «¿Qué actividad realizan aquí?» recibían aclaración genérica cuando el intérprete devolvía General con acto Information respaldado por el texto. La variante Profile funcionaba.
- Causa demostrada: el pipeline convertía General en SemanticAmbiguity incondicionalmente y el orquestador interceptaba General antes de llegar a las consultas autorizadas existentes de ChatFlow.
- Cambio mínimo: sólo sin borrador activo, sin directiva, sin RequiresClarification y con un único acto Information respaldado por evidencia literal, General puede llegar al camino FAQ/perfil existente. No se promueve General sin evidencia a respuesta factual ni se usa contenido del LLM como fuente comercial. No se cambian AiRouter, licencias, herramientas ni proyecciones públicas.
- Evidencia: [TurnUnderstandingPipeline:204](C:/Proyectos/NexFlow/backend/NexFlow.Application/Features/Automation/Dialogue/Understanding/TurnUnderstandingPipeline.cs:204), [AiResponseOrchestrator:149](C:/Proyectos/NexFlow/backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/AiResponseOrchestrator.cs:149), [regresiones de perfil:20](C:/Proyectos/NexFlow/backend/NexFlow.Tests/Concierge/RuntimeReadToolsTests.cs:20).
- Dos workspaces sintéticos consultan su propio perfil mediante KnowledgeService real y repositorios del fixture existente. Se comprueban hechos públicos, ausencia de TaxId en la respuesta y cero operaciones comerciales. Ambigüedad, falta de evidencia, fallo de proveedor, límite de IA, perfil inexistente y repositorio indisponible conservan resultados diferenciados; estos dos últimos tienen regresión en el handler real (:63).

### Coherencia A02 y privacidad/procedencia A09

- [CatalogGenerationTests:25](C:/Proyectos/NexFlow/backend/NexFlow.Tests/CatalogGenerationTests.cs:25): el servicio real de generación y OfferingService, sobre fixtures existentes, prueban **cinco publicados/cuatro reservables** por separado por RequiresReservation=false, duración legacy nula o scope de otra sede. En esta última variante el quinto sí se reserva en la sede permitida. Un servicio inactivo se excluye también de publicación: cuatro/cuatro.
- El payload mantiene Evaluación odontológica segunda y Restauración con resina simple quinta en la preparación sintética. S1 comprueba por separado el menú vigente 2/4. No se equiparan listas, no se alteran filtros ni se infieren flags de producción.
- Se verifican SourceHash, ámbito SERVICE sin lectura PRODUCT, Origin Uploaded/CURRENT y transición STALE al cambiar duración. Se reutilizan CatalogChatTests, CatalogCallbackTests, CatalogRecoveryTests y pruebas HTTP/upload: CURRENT para envío automático; generación y hash vigentes, firma HMAC, rechazo de callbacks tardíos/cancelados/obsoletos, conservación del PDF anterior, separación de workspace/scope y lectura legacy de Origin.
- **A02 NO REPRODUCIDO como defecto de filtros.** La causa exacta del cinco/cuatro del workspace real sigue **NO VERIFICADA**: requiere lectura autorizada de los cinco SERVICE (estado, flags, duración, scope/IDs y categoría), artefacto (estado/origen/hash/generación) y comparación con el PDF emitido. No se consultaron datos reales.
- **A09 P2 pendiente de política**: KnowledgeService proyecta perfil público sin TaxId; el payload de CatalogGenerationService (:101) sí lo incluye. No se cambia ni se interpreta la política fiscal sin autorización. La plantilla/PDF real no se inspeccionó; no se afirma exposición indebida. SourceHash acredita la fuente capturada, **no** equivalencia semántica de bytes PDF, sea GENERATED o UPLOADED. La prueba de upload usa un PDF sintético y no certifica su contenido.

### Checkpoint y evidencia real

Todos los resultados son locales, sin llamadas a IA, WhatsApp, PostgreSQL real, Firestore real, n8n ni almacenamiento externo. Los TRX se conservan bajo `backend/NexFlow.Tests/TestResults/mvp-s1-s2/` (salida local ignorada por Git). Los conteos de cada fila son ejecuciones distintas; no deben sumarse como cobertura única.

| Batería / TRX | Ejecutadas | Aprobadas | Fallidas | Omitidas |
|---|---:|---:|---:|---:|
| S1 reproducción / s1-before | 11 | 8 | 3 | 0 |
| S1 dirigidas / s1-directed | 24 | 24 | 0 | 0 |
| S1 checkpoint / s1-checkpoint | 985 | 985 | 0 | 0 |
| S2 reproducción / s2-before | 7 | 5 | 2 | 0 |
| S2 herramientas / s2-directed | 20 | 20 | 0 | 0 |
| S2 primer ensayo catálogo / s2-catalog | 181 | 177 | 4 | 0 |
| S2 dirigidas finales / s2-catalog-verified | 183 | 183 | 0 | 0 |
| Contrato vigente G8 / s2-current | 1515 | 1515 | 0 | 0 |
| Históricos congelados G8 / s2-historical | 130 | 118 | 12 | 0 |

Los cuatro fallos del primer ensayo de catálogo fueron del montaje nuevo de la prueba: SeedCurrent intentaba insertar un artefacto nuevo sobre otro con versión existente; CAS lo rechazó correctamente. Se cambió sólo esa prueba para utilizar el flujo Upload existente después del estado terminal. No se relajó CAS ni se tocó el fixture o código de persistencia. El TRX rojo se conserva. Después se añadieron dos variantes de ausencia/fallo de perfil, de ahí 183 frente a 181.

Comandos reproducibles del cierre:

```powershell
dotnet build backend/backend.slnx --no-restore
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --filter 'FullyQualifiedName~RuntimeReadToolsTests|FullyQualifiedName~RuntimeNaturalBookingTests|FullyQualifiedName~Catalog' --logger 'trx;LogFileName=s2-catalog-verified.trx' --results-directory backend/NexFlow.Tests/TestResults/mvp-s1-s2
python -B .github/workflows/g8_certification.py run current --trx backend/NexFlow.Tests/TestResults/mvp-s1-s2/s2-current.trx
python -B .github/workflows/g8_certification.py run historical --trx backend/NexFlow.Tests/TestResults/mvp-s1-s2/s2-historical.trx
git diff --check
```

- Build final: **0 errores, 0 advertencias** (incremental). Compilación inicial de las dirigidas mostró los tres avisos existentes: dos CS8981 en migraciones históricas y CS8602 en FirestoreCatalogRepository:228; no se alteraron para esta entrega.
- El runner G8 existente permanece intacto: contrato vigente **PASS**, sin exclusiones nuevas; **24 casos añadidos**, base 1491 → 1515. Incluye regresiones de F7-01, permisos/revocación, reservas, idempotencia, control humano, replay/CAS y UnknownDelivery.
- Históricos: dotnet devuelve realmente 1 por doce casos rojos; el validador existente devuelve 0 sólo después de verificar exactamente inventario, razones de fallo y hashes congelados. Diez casos *-09/*-10 de clinic/hotel/restaurant/bakery/limited y H10/H21 conservan VerifiedFact esperado. Decisión `EXPECTED_HISTORICAL_INCOMPATIBILITIES`, **no** 130 pruebas verdes ni evaluación ciega aprobada. JSON/TRX conservados y corpus/evaluadores sin modificaciones.
- Sin Run ID ni SHA de CI nuevos: estos resultados corresponden al árbol local modificado sobre el HEAD inicial, no a una candidata publicada.

## Dockerfile — reparación permanente mínima

[backend/Dockerfile:26](C:/Proyectos/NexFlow/backend/Dockerfile:26) y [:33](C:/Proyectos/NexFlow/backend/Dockerfile:33): ambas instrucciones COPY de publicación añaden `--chown=$APP_UID:$APP_UID`. USER continúa siendo `$APP_UID` no-root. El propietario runtime puede leer un archivo publicado con modo 600 y recorrer directorios publicados con modo 700. No se relajan permisos a 777 ni se cambia el usuario final a root. Se revisa también el target migrator, sin ejecutarlo.

`Get-Command docker -ErrorAction SilentlyContinue` no encontró Docker. **Construcción/smoke Linux ARM64 PENDIENTE**, incluida la comprobación dentro de la imagen de UID, ownership y lectura de appsettings con permisos restrictivos. No se atribuye a este árbol un smoke de la imagen anterior.

## Archivos cambiados y alcance revisado

Backend productivo:

- `backend/NexFlow.Application/Features/Automation/Dialogue/Contracts/TurnUnderstanding.cs`
- `backend/NexFlow.Application/Features/Automation/Dialogue/Policy/DialoguePolicy.cs`
- `backend/NexFlow.Application/Features/Automation/Dialogue/Understanding/TurnUnderstandingPipeline.cs`
- `backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/ConversationRecoveryService.cs`
- `backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/AiResponseOrchestrator.cs`
- `backend/Dockerfile`

Pruebas backend, ampliando clases/fixtures existentes:

- `backend/NexFlow.Tests/Concierge/RuntimeNaturalBookingTests.cs`
- `backend/NexFlow.Tests/Concierge/RuntimeReadToolsTests.cs`
- `backend/NexFlow.Tests/CatalogGenerationTests.cs`

Documentación: únicamente este informe nuevo. Frontend: sin cambios. Informes Sprint 0, corpus, infraestructura, secretos, migraciones y motores transaccionales: sin cambios. Diff propio revisado; `git diff --check` sin errores (Git avisa sobre conversión LF/CRLF del checkout).

## Criterios y decisión

| Criterio | Estado |
|---|---|
| S1 consulta sin mutación, acuse sin consentimiento, referencia vigente, selección explícita y confirmación separada | OK local |
| S1 rechazo de referencia expirada/ajena, cambio de sede y ocupación posterior; menú/errata respaldados | OK local; A03 sin defecto reproducido |
| S2 ruta informativa respaldada, herramientas autorizadas, estados diferenciados y TaxId fuera del perfil público | OK local |
| S2 filtros cinco/cuatro, callbacks/vigencia/ámbitos y seguridad existente | OK local; causa del workspace real NO VERIFICADA |
| Build, contrato vigente y conservación exacta de históricos | OK local |
| Permisos Docker de API y migrator | Cambio y revisión estática OK; smoke ARM64 PENDIENTE |
| PostgreSQL/Firestore/Redis aislados, proveedores y WhatsApp reales; evaluación independiente | PENDIENTES, no ejecutados ni autorizados aquí |
| Política fiscal/plantilla y contenido efectivo del PDF de producción | PENDIENTE |

No se ha demostrado un nuevo P0/P1 de seguridad, duplicación, consentimiento o cruce de tenant que active la detención extraordinaria. La cobertura local no certifica el servidor ni el comportamiento lingüístico real de Gemini/Groq.

**GO para revisión del diff de Sprint 1 + Sprint 2. NO-GO para afirmar preparación/certificación de producción** mientras falten los gates anteriores. Se conservan toma humana y verificación de titularidad de citas existentes. Detención antes de Sprint 3, commit, push o despliegue.
