# CODEX — ÚLTIMO PROMPT OPERATIVO NEXFLOW MVP

**Repo local:** `C:\Proyectos\NexFlow`  
**Repo GitHub:** `https://github.com/saintdeyb321/NexFlow.git`  
**SHA observado de main:** `b1ae17fcc2f9e99a0399fdca5b865ee7ea6d2b5e` (volver a verificar antes de actuar).

Trabaja como release engineer / auditor QA. NO abras un Sprint 12. Termina los gates pendientes **S9 → S10 → S11** con evidencia; no declares completada ninguna prueba bloqueada. Lee `AGENTS.md`, `docs/auditorias/CIERRE_MVP_GO_LIVE_EVIDENCIA.md`, `docs/auditorias/GO_LIVE_CHECKLIST_NEXFLOW_MVP.md`, `docs/auditorias/MATRIZ_Q01_Q10_VALIDACION_MVP.md`, `docs/auditorias/PLAN_PILOTO_E2E_WHATSAPP_MVP.md`, informes S1–S8 y, si el propietario lo guardó en docs, `NexFlow_Auditoria_Candidata_Final_2026-10-26.md`. No inventes contenidos de archivos faltantes.

**Restricciones absolutas:** sin checkout/reset/stash/pull que destruya trabajo, ni commit/push/PR/dispatch/despliegue, ni migraciones, ni acceso a cuentas reales o gastos sin autorización adicional precisa. Oracle de producción NO es runner de pruebas. Conservar F7-01, CAS, consentimiento, licencias, aislamiento multitenant, FIFO/idempotencia y UnknownDelivery sin reenvío. No debilitar pruebas, corpus ni inventarios conocidos; conservar históricos con doce fallos incompatibles, no etiquetarlos como PASS. No exponer secretos, teléfonos, mensajes, IDs privados ni contextJson/PayloadJson.

## 0. Identidad exacta

Inspecciona `git rev-parse HEAD`, `git status --short`, cambios locales y remoto. GitHub ya publicó `b1ae17...` que incluye workflow ARM64, test DST y el informe. Verifica SHA antes de cualquier otra corrección. El informe Codex se generó sobre el árbol anterior y los resultados locales no constituyen CI para `b1ae17...`. Separar SHA de commit, image ID, build digest, RepoDigest y SHA del artefacto OCI. Cualquier modificación del workflow o test requiere publicar manualmente nuevo SHA y repetir los gates relacionados. Nunca utilizar prefijos como identidad final.

## GATE S9-A. Auditoría inmediata del workflow ARM64

Audita `.github/workflows/agent-runtime-arm64-certification.yml` y `frontend/tests/reservations-browser.test.mjs`. La modificación DST ya está publicada: no la reescribas sin fallo reproducido.

Revisa **especialmente** esta línea del workflow:

`materials = build.get('buildx.build.provenance', {}).get('materials', [])`

Valida contra el JSON real de Buildx con `BUILDX_METADATA_PROVENANCE=max`, formatos SLSA v0.2 (`predicate.materials`) y v1 (`predicate.buildDefinition.resolvedDependencies`), y la disponibilidad efectiva de provenance usando `--load`. Construye tests unitarios de parser con metadatos sintéticos representativos; falla cerrado si no se acreditan con digests las bases SDK y ASP.NET. No elimines las comprobaciones para conseguir verde. Si el parser ya funciona en el entorno soportado, documenta NO REPRODUCIDO; si presenta un problema, corrige mínimamente solo el workflow/fixtures de test y valida YAML, shell y Python. No afirmar ARM64 PASS por tests del parser.

Confirma contexto allowlist sin secretos, runner `ubuntu-24.04-arm`, checkout por SHA y repositorio público, UID/GID 1654, variantes de permisos sin `chown` de encubrimiento, montajes sintéticos, runtime Linux ARM64, `final` y `migrator` sin ejecución, PNG/JPEG/WebP contra imagen API por ID, logs sanitizados y timeout razonable. Que compile en Windows NO certifica ARM64.

## GATE S9-B. Cuatro CI del mismo SHA: ejecución manual del propietario

Una vez aprobado y publicado el SHA final, proporcionar estos enlaces y pedir al propietario que pulse Run workflow en la referencia coincidente:

1. `https://github.com/saintdeyb321/NexFlow/actions/workflows/rc3-release-checks.yml`
2. `https://github.com/saintdeyb321/NexFlow/actions/workflows/rc3-reservation-integration.yml`
3. `https://github.com/saintdeyb321/NexFlow/actions/workflows/concierge-redis-integration.yml`
4. `https://github.com/saintdeyb321/NexFlow/actions/workflows/agent-runtime-arm64-certification.yml` (el `release_sha` introducido debe ser el SHA completo del ref seleccionado).

No lanzar workflows por cuenta propia. Después de que el propietario comparta cuatro Run IDs, usar lectura del conector GitHub para verificar `head_sha`, run_attempt, conclusión, jobs, TRX/JUnit/artifacts e identidad. Requisitos: backend actual 1538/1538 o inventario nuevo justificado, históricos exactamente 130 ejecutados/118 verdes/12 incompatibilidades verificadas por hash y razón; frontend TypeScript 83+, browser 148+, sin skipped/fail; Integration 39/39, RedisIntegration 1/1; ARM64 API+migrator, propiedad/permisos, codecs tres PASS y provenance/digests compatibles. Un check verde sin artifacts no basta. No mezclar resultados de revisiones diferentes.

## GATE S9-C. Custodia de imagen exacta

La imagen generada por `--load` en GitHub no se retiene ni publica. Prepara un plan de exportación OCI/tar con SHA256 o registro por digest inmutable que permita llevar a Oracle **la misma imagen que pasó** el smoke, sin reconstruirla a ciegas. No ejecutar push, login en registro, copia SSH/import ni cambiar Compose sin autorización específica. Diferenciar image ID, digest manifest y digest de registro; exigir comprobación en destino.

## GATE S10. Estado Oracle ya aportado y datos/privacidad/recuperación

**Q01 inventario observado y sanitizado (NO volver a pedir):** `nexflow_api` running, 0 reinicios; `nexflow-api:306d7c8-perms1`; image ID `sha256:e63453197ee666cfb023ae07b33a95fdc37711cf914c0dd0b13a5c51fff0ce45`; linux/arm64; UID/GID 1654; `/app` root:1654 755 y `/app/appsettings.json` root:1654 640; Compose proyecto `nexflow`, servicio `api`, directorio `/opt/saas/nexflow`, 13 archivos, último override `api-306d7c8-perms.override.yml`. Marcar **Q01 inventario PASS**, pero identidad de digest de registro y salud actual PENDIENTES. No autorización para operaciones.

Q02–Q10: preparar consultas solo lectura exactas por esquema observado y parámetros, workspace DEMO autorizado, límites y datos sanitizados. No ejecutar SQL/SDK/GET con efectos colaterales ni llamadas a Oracle/Firestore/n8n sin aprobación. Reconciliar por ítem cinco servicios publicados/cuatro reservables sin alterar filtros. Solicitar decisión explícita de política fiscal antes de modificar `TaxId` en payload/plantilla; verificar bytes visuales de PDF y no confundir hash de fuente con contenido. Si hay un cambio, nuevo SHA y todos los gates relevantes.

Firestore Spark: no existe backup/restore probado solo porque PostgreSQL tenga dump. Preparar copia lógica tipada, cifrada, con cobertura de subcolecciones/objetos externos, límites, consistencia con SQL y restauración aislada en Emulator. No declarar capacidad recuperable sin ensayo realizado, no activar billing ni copiar datos de clientes sin aprobación. Para clientes/datos reales: restauración certificada obligatoria; piloto 100% descartable requiere aceptación específica y demás controles.

## GATE S11. Evaluación independiente, piloto y eventual go-live

Requerir mínimo 32 conversaciones multitur inéditas mantenidas por evaluador distinto, scoring humano de utilidad/naturalidad, invariantes de seguridad del 100%, Groq/Gemini con modelos, precios, cupo de llamadas/tokens, ventana y presupuesto previamente aprobados. Validar WhatsApp real solo en contactos y workspaces DEMO controlados: perfil, FAQ, PRODUCT, SERVICE, reservas, pedidos PendingReview, lectura de horarios, PDF, interrupciones, humano, duplicados, fuente única, outbox/entrega. Comprobar persistencia y datos de cada negocio, no solo la respuesta textual. Nunca hacer tests de fallos destruyendo servicios compartidos.

**No desplegar como parte de esta orden.** El reemplazo de la API Oracle requiere GO específico posterior para SHA completo, imagen por digest realmente certificada, ventana y plan de admisión/drenaje global de host compartido, respaldo y restitución adecuados, compatibilidad o fix-forward seguro, y revisión de configuración efectiva con los 13 archivos Compose. La única mutación autorizable después de firmas sería servicio `api` con `--no-deps --no-build --pull never`, nunca apagar/recrear PostgreSQL/Redis/Caddy/Evolution/n8n/Gotenberg. Ni `0da524...` ni `306d7c8-perms1` están aprobadas automáticamente como rollback.

## Informe central único

Actualizar **únicamente** `docs/auditorias/CIERRE_MVP_GO_LIVE_EVIDENCIA.md` para cierres documentales, con estado `PASS / FAIL / BLOQUEADO / NO EJECUTADO` y fuente, fecha, SHA/digest, Run ID/attempt, evidencia y responsable por gate. Registrar Q01 ya verificado; separar GO revisión/GO certificación/GO piloto DEMO/GO deploy/GO ventas. Ningún permiso o evidencia ausente puede convertirse en PASS; no abrir Sprint 12. Si un gate requiere acción humana, proporcionar el siguiente paso concreto y detener esa acción sin inventar resultados.

**Empieza por auditar la compatibilidad real del parser de provenance y comprobar HEAD, luego entrega el paquete de ejecución manual de los cuatro workflows.**
