# Concierge 2.0 — Certificación técnica de candidata

## Identidad y dictamen

**SHA certificado: NINGUNO. GO técnico externo: NO-GO. GO comercial: NO-GO.**

Candidata publicada evaluada: `8be3a6c860186664ae0afaf0668580cd27c2cb68`. HEAD local y `refs/heads/main` remoto coincidieron con ese SHA; rama local `main`, árbol inicial limpio, tree Git `8aad84c044d8a71fac135f12c562cce32a940fb9`. Los diez archivos de código/pruebas coinciden byte a byte con sus SHA256 conservados en el cierre 6.2. No hubo cambios inesperados previos.

Se reprodujeron los dos riesgos solicitados y se aplicaron exclusivamente sus correcciones. El árbol final contiene seis archivos productivos modificados, una prueba nueva y este informe. **El resultado corregido sólo está local y requiere un nuevo SHA publicado tras auditoría.** No hereda resultados del SHA anterior. Sin commit, push, cambios de ramas, dispatch, despliegue ni acceso a producción.

Huella SHA256 del manifiesto ordenado de código/pruebas modificados, vinculada a la base anterior:
`decb3baecba81ce7b0a7ee37818f48bb4bfba1a037746a3c999ce74eced0ea5c`.
Es una huella de contenido local, no un SHA de commit. `candidate-freeze.json` registra identidad, archivos y huellas del parche e informe para revisión.

## Riesgos dirigidos: reproducidos y corregidos

**A — Continuación del PDF alternativo.** ChatFlow devolvía la pregunta productos/servicios sin opciones persistentes cuando recibía una interpretación amplia sin OfferingQuery. Reproducción: cuatro casos del handler real, con/sin borrador y ambos scopes; la recuperación durable devolvía una lista vacía. También se comprobó una ruta alcanzable con el resolutor comercial habilitado: consulta mixta «¿Me puedes pasar el PDF y sus horarios?» e interpretación semántica amplia simulada.

Corrección: ChatResponse transporta las opciones deterministas compartidas con CommerceTurnResolver. El orquestador las guarda mediante el mismo ClarifyAsync/CAS/RecoveryOptions existente. Selección textual o numérica, licencia revalidada, contexto recuperado desde persistencia, entrega durable por scope, replay sin duplicación y preservación del borrador. No se creó un segundo selector ni una política PDF distinta. La prueba mixta verifica la continuación de PDF; no certifica comprensión universal de peticiones múltiples ni un proveedor real.

**B — Pregunta relativa antigua.** Con LastQuestion «próxima semana», TargetDate `2026-10-22` o `2026-11-05`, interrupción informativa y «mejor el viernes», se obtenía incorrectamente `2026-10-30` en ambos casos. «Este jueves» también podía quedar rechazado por WeekMismatch pese a su fecha explícita.

Corrección: el mensaje actual y la fecha durable prevalecen sobre la pregunta anterior. LastQuestion sólo aporta contexto implícito desde el paso COLLECT_DATE; no desplaza un ancla durable, una fecha explícita ni un calificador actual. Se preservan las semanas explícitas, el contexto válido sin fecha, el reloj del negocio y la confirmación. No se modificaron los motores transaccionales ni las expectativas existentes.

## Resultados locales y artefactos

Evidencias en `C:/Users/deyvi/.codex/visualizations/2026/10/02/01a0fa60-4061-7571-be5e-8c8e0adbefd8/concierge-candidate-certification/`.

| Ejecución | Ejecutadas | Aprobadas | Fallidas | Omitidas |
|---|---:|---:|---:|---:|
| Reproducción sobre código publicado: candidate-risks-before.trx | 8 | 0 | 8 | 0 |
| Dirigidas tras parche, incluidas 33 del cierre: candidate-risks-after.trx | 46 | 46 | 0 | 0 |
| Regresión intermedia: candidate-local-final.trx | 1.335 | 1.335 | 0 | 0 |
| **Regresión final: candidate-local-certified-content.trx** | **1.336** | **1.336** | **0** | **0** |

Los grupos son subconjuntos, no se suman: **14 nuevas dirigidas, 33 de cierre, 50 lingüísticas, 14 de bienvenida, 100 comerciales y 30 históricas** aprobadas. La prueba adicional de alcanzabilidad con commerce habilitado está incluida en la regresión final. TRX anteriores permanecen como evidencia; no se cambiaron corpus JSON ni aserciones históricas para conseguir verde. QualityHoldout61.json conserva SHA256 `3dfe02ac4cc1d6a067d1a875917615a4a3b3b73ab6d72c47ef2d63fe1f7e96e0`.

`dotnet build backend/backend.slnx --no-restore`: **0 errores, 0 advertencias** en la ejecución final incremental; `build.log` conserva salida. Compilaciones previas mostraron las tres advertencias históricas CS8981 x2 y CS8602 en FirestoreCatalogRepository:228. `git diff --check`, whitespace de archivos nuevos y comprobación inversa del parche completo: OK.

Filtro local usado: `Category!=Integration&Category!=RedisIntegration&Category!=ProviderIntegration`. Las 37 Integration, 1 RedisIntegration y 2 ProviderIntegration quedan fuera por requerir infraestructura/autorización; no se cuentan como aprobadas ni como omitidas de la suite local. Las pruebas locales usan repositorios/transporte sintéticos y SDK Firestore sobre RPC en memoria; no certifican Emulator, Redis ni PostgreSQL reales.

## GitHub Actions: CI PENDIENTE

La consulta autenticada GET `/repos/saintdeyb321/NexFlow/actions/runs?head_sha=8be3a6c860186664ae0afaf0668580cd27c2cb68&per_page=100` devolvió **total_count=0**, también en la comprobación final. Se usó la colección general porque el wrapper específico de commits sólo consulta eventos pull_request. El JSON de identidad/consulta está conservado en `github-identity-and-runs.json`.

| Contrato externo | Workflow | SHA objetivo | Run ID / job / build | Tests reales / TRX / identity.json |
|---|---|---|---|---|
| PostgreSQL + Firestore Emulator | [rc3-reservation-integration.yml](https://github.com/saintdeyb321/NexFlow/actions/workflows/rc3-reservation-integration.yml) | Nuevo SHA pendiente; no hay runs para 8be3a6c | **PENDIENTE**, sin ejecución | 37 requeridas; 0 ejecutadas verificadas; artefactos inexistentes para esta consulta |
| Redis aislado | [concierge-redis-integration.yml](https://github.com/saintdeyb321/NexFlow/actions/workflows/concierge-redis-integration.yml) | Mismo nuevo SHA pendiente | **PENDIENTE**, sin ejecución | 1 requerida; 0 ejecutadas verificadas; artefactos inexistentes para esta consulta |

No hay fallos observados de configuración de CI ni infraestructura temporal: **no hubo runs que permitan evaluarlas**. Sí hubo dos defectos de código localmente reproducidos y corregidos. Ningún TRX local de este informe es un artefacto CI.

Los workflows existentes permanecen sin cambios. El de PostgreSQL/Emulator conserva guards contra fallos/omisiones y identity.json. El de Redis exige exactamente 1 ejecutada/aprobada y 0 fallidas, usa Redis temporal `127.0.0.1:16379`, base aislada 15, PostgreSQL temporal y Firestore Emulator de proyecto demo. Esto es inspección de configuración, no aprobación de ejecución.

## Archivos de esta entrega

Backend, bajo `C:/Proyectos/NexFlow/backend/NexFlow.Application/Features/Automation/`:
- `Dialogue/Understanding/BusinessDateResolver.cs`
- `Dialogue/Understanding/WorkspaceSlotResolver.cs`
- `Dialogue/Understanding/InformationalRequest.cs`
- `Dialogue/Understanding/CommerceTurnResolver.cs`
- `ProcessMessage/Services/Flows/ConversationFlows.cs`
- `ProcessMessage/Services/AiResponseOrchestrator.cs`

Prueba nueva: `C:/Proyectos/NexFlow/backend/NexFlow.Tests/Concierge/CandidateCertificationTests.cs`.
Informe nuevo: `C:/Proyectos/NexFlow/NexFlow_CONCIERGE_CANDIDATE_CERTIFICATION_REPORT.md`.
Frontend, workflows, esquemas, motores, infraestructura y configuración: sin cambios. Diff exacto: `candidate-certification.patch`; listado: `candidate-files.txt` en evidencias.

## Gate comercial: ejecución por evaluador diferente

1. Una persona independiente prepara **al menos 30 conversaciones inéditas** fuera del repositorio, sin mostrar contenido ni expectativas al implementador. Producto y evaluador fijan cobertura, criterios y umbrales antes de evaluar. Actualmente ese corpus aprobado no existe: pendiente.
2. Congelar SHA candidato, huella del corpus, versión del evaluador, relojes y datos sintéticos autorizados. Un runner separado evalúa ese SHA sin producción. Conservar resultados por identificador y agregados; el implementador no recibe casos ni expectativas privadas. No llamar ciega a la regresión histórica conocida.
3. Revisión humana con rúbrica de 1–5 para **naturalidad** (tono y continuidad sin rigidez/repetición), **claridad** (pregunta concreta, fechas y opciones comprensibles) y **utilidad** (hechos correctos y siguiente paso realizable). Registrar desacuerdos y arbitraje. Licencias, aislamiento, confirmación, no invención y no duplicación son condiciones de seguridad obligatorias. Los umbrales de aprobación deben acordarse previamente; este informe no los da por aprobados.
4. Tras autorización expresa de llamadas pagadas, ejecutar los **dos contratos existentes Groq/Gemini** con datos/credenciales sintéticos y opt-in NEXFLOW_CONCIERGE_REAL_AI_AUTHORIZED. Registrar SHA, proveedor/modelo, resultado, latencia, tokens/coste y errores sin secretos. No ejecutado aquí.
5. WhatsApp E2E autorizado posteriormente en workspace aislado y con cuentas consentidas: reservas/correcciones, interrupciones, handoff, productos/servicios, PDF y entrega incierta sin reenvío. Sólo después de gates técnicos y humanos. No se tocó Evolution, Oracle ni servicios reales.

## Siguiente procedimiento seguro

Auditar este diff mínimo y publicar manualmente una nueva candidata si se aprueba. El propietario inicia ambos workflows existentes sobre esa misma revisión, o autoriza expresamente su ejecución. Recuperar **Run ID, run_attempt, head_sha, jobs/steps, logs, ZIP, TRX y identity.json**. Verificar SHA idéntico en runs/identidades, suite local completa sin omisiones, **37/37** integraciones y **1/1** Redis, todos con 0 fallidas/omitidas. Si cambia el código, invalidar la atribución y repetir sobre el nuevo SHA; si falla CI, clasificar código/configuración/infraestructura con su evidencia, sin alterar aserciones para ocultarlo.

**NO-GO técnico externo** por evidencia CI ausente y nueva candidata aún sin SHA publicado. **NO-GO comercial** por evaluación ciega, revisión humana, proveedores y WhatsApp pendientes. La regresión local está aprobada; no sustituye esos gates ni autoriza despliegue. Detenerse para auditoría.
