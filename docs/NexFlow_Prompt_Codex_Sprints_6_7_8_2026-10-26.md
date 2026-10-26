# PROMPT MAESTRO PARA CODEX — NexFlow MVP · Sprints 6, 7 y 8

**Rol:** ingeniero principal de release, auditor de seguridad multitenant y responsable de calidad E2E de NexFlow Agent Runtime 4.0. **Ejecuta los tres sprints consecutivamente en UNA intervención LOCAL, con un gate verificable entre fases.** No confundas preparar un gate con aprobarlo.

**Repositorio local:** `C:\Proyectos\NexFlow` (Windows).  
**GitHub:** `https://github.com/saintdeyb321/NexFlow.git`  
**Referencia publicada al auditar:** `be07f89c2146f7c4344effad849e1335c9614e86`. Verifica HEAD y `git status` antes de intervenir; no asumas que coinciden.  
**Última API reportada en Oracle:** `nexflow-api:306d7c8-perms1`, que **NO** contiene los Sprints 1–5. Identidad operativa actual NO VERIFICADA por esta tarea.

## 0. Documentos obligatorios y alcance

Leer, en este orden:

1. `C:\Proyectos\NexFlow\AGENTS.md`.
2. `C:\Proyectos\NexFlow\docs\NexFlow_Auditoria_S3_S4_S5_2026-10-26.md` (**auditoría independiente**).
3. `C:\Proyectos\NexFlow\docs\auditorias\IMPLEMENTACION_S3_S4_S5_MVP_2026-10-26.md`.
4. `C:\Proyectos\NexFlow\docs\auditorias\IMPLEMENTACION_S1_S2_MVP_2026-10-26.md`.
5. `C:\Proyectos\NexFlow\docs\auditorias\MATRIZ_DATOS_MODULOS_MVP.md` y `CASOS_REGRESION_WHATSAPP_MVP.md`.
6. Los runbooks **puntuales** `docs/agent-runtime/07_PILOTO_RELEASE.md` y `08_CIERRE_AGENT_RUNTIME_4.md`; consulta otros archivos solo cuando una referencia concreta lo requiera.

Este bloque **no autoriza tres sprints de rediseño**. El objetivo es cerrar release, datos y piloto sobre la arquitectura existente. Reutiliza `AiRouter`, `BusinessReadTools`, `KnowledgeService`, `OfferingService`, `ReservationEngine`, `OrderFlow`, `InboundMessageWorker`, `Outbox` y los workflows ya existentes. No inventes un motor, otro bot o herramienta autónoma que escriba SQL/Firestore. No alteres el contrato de reservas/pedidos ni F7-01.

### Restricciones generales

- Antes de cambiar algo, leer `git rev-parse HEAD`, `git status --short`, `git diff --stat`, y distinguir archivos del propietario y cambios propios. No hacer checkout, reset, pull, stash, rebase, commit, push, PR o `gh workflow run` sin aprobación nueva del operador.
- **Sin acceso a Oracle ni cambios de producción en esta intervención**: no deploy, no Docker remoto, no reinicios, no migraciones, no acciones sobre Evolution, Firebase real, Cloudinary, n8n o bases productivas. No copiar `.env`, claves, cuentas de servicio ni datos privados a artefactos.
- Se autorizan inspección/cambios locales estrictamente necesarios y tests locales disponibles. Si no hay Docker en Windows, no simules ARM64: genera validación reproducible y marca PENDIENTE. Si no hay servicios aislados, no inventes resultados; para ejecución externa entrega el paquete de evidencias y comandos verificados.
- No modificar corpus congelados ni sus evaluadores, listas de incompatibilidades esperadas, assertions sanas, umbrales CI o excluir tests para conseguir verde. Los **12 históricos rojos esperados** deben mantenerse visibles y con verificación exacta de identidad/razones.
- Ante cualquier indicio reproducido de cruce tenant, fuga de datos, efectos sin consentimiento, doble operación, rollback inseguro o F7-01: **STOP**. Reportar causa y regresión y no pasar al siguiente sprint.
- No usar nombres, teléfonos ni mensajes reales en fixtures o reportes; datos W-A/W-B y contactos sintéticos. No realizar llamadas a Groq/Gemini reales sin autorización, límites explícitos y presupuesto aprobados por el propietario.

## SPRINT 6 — Candidata reproducible, CI y ARM64

**Meta:** verificar que el código S1–S5 constituye una candidata de release identificable, sin perder ningún contrato de seguridad, y que la nueva imagen no repite el error `root:root 600` en `/app/appsettings.json`.

1. Inventaria el diff de `306d7c8…` a HEAD por código, documentación, Dockerfile y migraciones; determina si cambian esquema, contratos persistidos o interacción de contextos. Si no hay migraciones, documenta evidencia, no la supongas.
2. Confirma que la ruta `General→Profile`, preguntas `Order/OperationInformation`, selección de referencia de reserva, idempotencia, F7-01, licencia, CAS, privacidad de citas existentes y salida `UnknownDelivery` conservan las pruebas. Inspecciona los cuatro archivos de Application de S3–S5 y los seis componentes productivos de S1–S2; no abrir toda la arquitectura sin motivo.
3. Ejecuta compilación y suites locales ya disponibles: `dotnet build backend/backend.slnx --no-restore`, dirigidas de los flujos cambiados, runner G8 **current**, runner G8 **historical**, `git diff --check`. Usa TRX nuevos, sin sobrescribir evidencia previa. El conteo de referencia reportado es 1538/1538, pero el gate se basa en el conteo **real** ejecutado y cero fallos/omisiones del conjunto vigente. Los históricos son 130/118/12 incompatibles previstas, no 130 PASS.
4. Inspecciona sin alterar gratuitamente los workflows `rc3-release-checks.yml`, `rc3-reservation-integration.yml`, `concierge-redis-integration.yml`. Se activan por `workflow_dispatch`, por lo que deben ejecutarse de manera autorizada sobre **el mismo commit SHA**. Prepara un procedimiento **de operador** que registre URL, SHA, Run ID, intento, G8 identity/TRX y resultados: frontend TS/browser, 39 Integration descubiertas (mínimo contractual actual 37), exactamente una RedisIntegration y ningún skip. No lances workflows sin autorización.
5. Revisa el `backend/Dockerfile` corregido con `COPY --chown=$APP_UID:$APP_UID` para **API y migrator**. Prepara smoke Linux ARM64 reproducible desde contexto allowlist no secreto y publicación restrictiva. Asertar `Architecture=arm64`, `USER=1654`, lectura de appsettings/binarios y recorrido `/app` desde ese usuario, salud del runtime y codecs PNG/JPEG/WebP mediante harness existente. **No exigir necesariamente modo 600 resultante**: comprobar los permisos reales y una variante aislada restrictiva; no arrancar migrator contra BD. Sin ARM64 real = PENDIENTE.
6. Si el código de CI/smoke necesita una corrección **demostrada**, introduce el cambio mínimo y su prueba. Nunca reduzcas cobertura ni toques imágenes de Oracle. Separar gates locales de externos con marcas OK/PENDIENTE/NO-GO.

**GATE S6 local:** build/tests relevantes verdes, identidad de árbol preservada, matriz de workflows/smoke lista para un operador. **GATE S6 externo:** solo PASS con CI y ARM64 reales **del SHA final**; si faltan, no declarar certificación de release aunque puedas preparar S7/S8 documentalmente.

## SPRINT 7 — Datos reales del negocio, PDF y recuperación segura

**Meta:** garantizar que NexFlow lee información auténtica por workspace y que publicación de PDF/datos y recuperación son aceptables para un piloto y, después, clientes.

1. Reconciliar exclusivamente sobre **fixtures locales** las reglas PRODUCT vs SERVICE, estado activo, sedes, categorías, `RequiresReservation`, duración ≥5, precios/moneda, schedules por sede, licencias/capacidades y catálogo PDF. Con cinco servicios publicados/cuatro reservables, demostrar razón de exclusión por ítem; **no modificar flags para hacer coincidir cantidades**. Distinguir PDF Generated/Uploaded, CURRENT/STALE, SourceHash, GenerationId, callback HMAC y documento visual. Un hash no valida semántica del contenido.
2. Validar en código que los endpoint/servicios llamados para diagnóstico no muten el artefacto, y preparar **Q01–Q10** del informe con consultas mínimas, paginadas, parámetros SQL y `READ ONLY`, datos comerciales públicos o booleanos; no ejecutar contra Oracle. No entregar `contextJson`, `PayloadJson`, teléfonos, nombres privados, secretos ni URLs firmadas.
3. **Política fiscal:** constatar el campo `profile.TaxId` del payload de generación y su tratamiento en templates. Definir decisión pendiente como: OMITIR por defecto cualquier información no expresamente autorizada, pero **no modificar silenciosamente el contrato fiscal** si no hay una política aprobada. Si está autorizada documentalmente una proyección más mínima, implementarla con pruebas de payload, hash/vigencia, PDF y retrocompatibilidad. Si no lo está, marcar PENDIENTE Q10 y bloquear publicación comercial. No afirmar que un dato está impreso en PDF sin inspección visual válida.
4. **Recuperación Firestore:** investigar rutas disponibles según despliegue actual/Spark sin activar billing ni instalar servicios. Redactar una estrategia realizable de backup + restauración aislada para datos de negocio y estados durables, además de límites de retención/PII. PostgreSQL ya tuvo backup/restauración aislada para la versión anterior: no atribuirlo a Firestore. Si no hay mecanismo restaurable, sólo puede prepararse un piloto con datos descartables; ventas comerciales NO-GO.
5. Revisar que no haya mocks productivos en flujo de Knowledge/Order/Booking ni colecciones sintéticas conectadas a DI productiva; **respuestas deterministas de seguridad no son mocks**. No crear un diccionario de industrias ni sembrar respuestas fijas de una clínica.
6. Entregar matriz Q01–Q10 con columna de evidencia **local / requiere operador / requiere decisión del propietario** y método exacto, no resultados inventados.

**GATE S7:** pruebas locales sin regresión, política PDF y respaldo clasificadas honestamente; sin acceso a datos productivos. S7 puede concluir **documentalmente** si hay dependencias del operador, pero no obtiene GO comercial hasta probarlas.

## SPRINT 8 — Evaluación independiente, ensayo de piloto y preparación de despliegue

**Meta:** dejar una candidata lista para una **decisión verificable de despliegue controlado**, no auto-desplegar.

1. Construir plan E2E con **al menos 30 conversaciones inéditas**, externas a corpus del implementador, entre clínica multisede, comercio, negocio mixto y un tenant con módulos limitados. Registrar rúbrica antes de ejecutar: intención/evidencia, hechos verificables, continuidad multitur, selección/horas, consentimiento, aislamiento, privacidad, fallback, naturalidad y derivación humana. La evaluación debe reservar expectativas al evaluador independiente. Preparar suite contra Groq/Gemini reales **sin ejecutarla** hasta que exista aprobación de proveedor/modelo, presupuesto, llamadas/tokens y ventana.
2. Escenarios E2E WhatsApp autorizado: saludo/actividad del negocio, FAQ, servicios/productos y PDF, cuatro/five por sede, disponibilidad→acuse→selección→nombre→resumen→confirmación, pedido→productos/cantidades→cotización→consentimiento→PendingReview, consultar cita privada→human handoff, revocación a mitad de turno, provider fallido, replay/duplicado, recuperación después de fallo, UnknownDelivery sin reintento ciego, dos tenants sin datos cruzados. Deben correlacionar **estado durable/Outbox/inbound + respuesta**; `Sent` no demuestra Delivered.
3. Revisar panel web admin y configuración de negocio contra rutas **existentes**. Comprobar que roles/capacidades permiten cargar perfil, sedes, horas, FAQ, PRODUCT/SERVICE, licencia/módulos y conexión Evolution con acciones explícitas. No agregar un módulo de empleados, ERP, pagos, inventario ni nuevos endpoints por suposición.
4. Elaborar runbook de canary: fijar SHA y digest ARM64, comparar imagen activa y árbol, verificar configuración Compose real **sin emitir secrets**, plan de admisión/colas/workers, 5 min de ventana sin tráfico, checklist salud HTTPS (`/health/live`, `/health/ready`), Evolution/n8n/Gotenberg por separado, deploy **solo servicio API** con `--no-deps --no-build --pull never`, métricas y abortos. **No copiar órdenes de Compose anteriores ciegamente:** obtener lista `-f`/proyecto desde las labels actuales del contenedor y comprobar diff que solo cambia imagen.
5. Rollback no asumido: nunca volver a `0da524…` (release con F7-01). Verificar compatibilidad de contratos/estado con una imagen anterior antes de usarla. Si no hay rollback seguro, definir stop de admisión y fix-forward; ninguna reversión deshace reservas/pedidos ya confirmados. No `compose down`, `prune`, reinicio de dependencias, migraciones ni reenvíos de DLQ/UnknownDelivery.
6. Preparar checklist final de GO/NO-GO, responsables de aprobación, seguridad/licencias, PDFs, backups/restauración, gastos/cotas IA, suporte/handoff, privacidad/retención, capacidades del negocio, incidentes y criterios de aborto. **Ventas solo con todos los gates críticos verificados; `Healthy` por sí solo no certifica conversación ni seguridad.**

**GATE S8 local:** pruebas y runbooks completos; **GATE S8 externo:** CI mismo SHA, ARM64, Q01–Q10, política PDF aprobada, recuperación Firestore, evaluación independiente, proveedor/canal y piloto observado. Si faltan, entregar READY_FOR_OPERATOR / NO-GO para despliegue en vez de proclamar sprint comercial aprobado.

## 4. Entregables obligatorios

Crear o actualizar **solo** cuando corresponda estos archivos locales (sin sobrescribir documentos del propietario):

1. `docs/auditorias/IMPLEMENTACION_S6_S7_S8_MVP_2026-10-26.md` — evidencia por sprint, cambios, líneas, comandos, TRX, gates, resultados reales y pendientes.
2. `docs/auditorias/MATRIZ_Q01_Q10_VALIDACION_MVP.md` — fuente de verdad, pruebas locales, proyección permitida y resultado pendiente del operador.
3. `docs/auditorias/PLAN_PILOTO_E2E_WHATSAPP_MVP.md` — matriz de 30+ conversaciones, oráculos, riesgos, presupuesto/seguridad y cómo corroborar efectos durables sin PII.
4. `docs/auditorias/GO_LIVE_CHECKLIST_NEXFLOW_MVP.md` — CI/SHA/digest, ARM64, recuperación, PDF, piloto, canary/rollback compatible, abortos, responsable de aprobación, secuencia exacta por operador.

Si ya existen, no sobrescribir trabajo ajeno sin reconciliar. Los tests/código que se cambien deben estar justificados por reproducción real y alcance de S6–S8; limitar cambios a backend/tests/runbooks concretamente necesarios. No hacer refactor cosmético ni introducir otra arquitectura.

## 5. Reporte final y parada

Reportar **(a)** HEAD y estado local, **(b)** diff propio por fase, **(c)** S6 gate local/externo, **(d)** S7 datos/PDF/Firestore, **(e)** S8 plan de piloto y despliegue, **(f)** build/tests TRX y estado CI real, **(g)** P0/P1 y riesgos de negocio, **(h)** decisión final diferenciada: `GO revisión`, `READY_FOR_OPERATOR`, `GO piloto controlado` o `NO-GO`.

**No continuar con un Sprint 9 por inercia.** Si el desarrollo está cerrado y quedan exclusivamente aprobaciones externas, entregar el paquete para que el propietario y el auditor ejecuten CI, smoke ARM64, piloto y despliegue en sesiones autorizadas. No crear commits, pushear, lanzar workflows, instalar imágenes en Oracle ni activar tráfico real por tu cuenta. STOP antes de cualquier operación sobre producción.
