# NexFlow LLM-first — Auditoría independiente y orden de cierre pre-DEMO

**Fecha:** 2026-11-03 (America/Lima). **Repositorio:** `saintdeyb321/NexFlow` (público). **Rama exclusiva:** `feature/llm-first-agent`.

## 0. Identidad y alcance realmente verificados

- `main`: `5f5ff850e31df3fa73cb2c7f02f65ba6081173c7`.
- HEAD remoto experimental **actual al auditar**: `5d26b7c73e7ed46823bf49f45e3bbeecb5b9bdc5`, commit **«Certificacion PRE DEMO»**, cinco commits adelante de main.
- GitHub API: **0 workflow runs para 5d26b7**, y combined statuses vacío. Los conteos `69/69`, `2128/2128`, `130/101/29` son **reportados en pruebas locales anteriores al commit**, no evidencia independiente CI de 5d26b7.
- El informe `CERTIFICACION_PRE_DEMO.md` describe un parche local sobre `6bba4c4` antes de publicación. El **código y documentos ya están publicados** en 5d26b7. No volver a presentar 6bba4c4 como versión actual.
- No hubo acceso a Oracle, datos reales, ejecución local del código de Windows, proveedores, cambios a GitHub ni workflows iniciados desde esta auditoría.

## 1. Hallazgos prioritarios

### BLOQUEO 1 — Protección de tenant no durable al reiniciar

`backend/NexFlow.Application/Features/Automation/Agent/AgentDemoPolicy.cs` implementa un `HashSet<Guid>` monotónico y una bandera `_identityChanged` en un singleton. `AddApplication` registra `IAgentDemoPolicy` singleton y `AddInfrastructure` usa `TryAddSingleton`. Un `IOptionsMonitor<AgentDemoOptions>` detecta cambios de configuración **dentro del mismo proceso**. Se verificó el código y los tests de identidad/DI.

**Riesgo:** con reinicio, despliegue, rollback o réplica, el set se pierde; si `AgentDemo:WorkspaceId` falta, cambia o se lee distinto, `Protects(oldWorkspace)` puede devolver falso. Entonces rutas antiguas pueden procesar mensajes o escribir para el tenant anterior. El preflight manual reduce riesgo, pero no constituye garantía durable de seguridad.

**Corrección requerida ANTES DE DEMO:** identidad de tenant protegido separada e independiente de la configuración dinámica del agente, estable a través de reinicios y reconocida por cada host/worker autorizado; validación temprana fail-closed **antes de iniciar listeners/workers**; protección de todos los motores/adapter/EF durante `Enabled=false`; bloqueo con telemetría sanitarizada ante omisión, deriva o discrepancia. Preferir un identificador inmutable de entorno de despliegue con manifest/config privado atestiguado o un registro protegido de identidad; no introducir migraciones/almacenes paralelos sin análisis. Si la falta de *ambas* fuentes pudiera permitir arranque inseguro, exigir una marca de entorno DEMO verificable de forma independiente (o prohibir ese despliegue). Mantener aislamiento externo del canal/instancia y colas como defensa adicional. **No afirmar que un HashSet entre scopes cumple este gate.**

Pruebas obligatorias: (1) reinicio/DI nuevo con identidad correcta; (2) reinicio con identidad omitida; (3) reinicio con identidad cambiada; (4) recarga de Enabled y opciones; (5) dos hosts con configuración discordante; (6) worker e inbound; (7) intento de mutar reservas/órdenes/EF por tenant protegido con agente apagado; (8) rollback a versión anterior con ingreso DEMO aislado. Si el rollback requiere infraestructura externa de corte, documentar y probar el procedimiento sin ejecutarlo en Oracle.

### BLOQUEO 2 — Holdout antes privado incluido en repositorio PÚBLICO

El commit `5d26b7` incluye en la raíz:

- `NEXFLOW_EVALUACION_HOLDOUT_12_SINTETICAS_2026-11-03.json`
- `NEXFLOW_RUBRICA_HUMANA_HOLDOUT_2026-11-03.md`

**No se abrió ninguno de esos dos archivos en esta auditoría.** Sin embargo, son públicos y parte de la historia Git; no puede afirmarse que permanezcan reservados frente al implementador. **Clasificarlos como conjunto expuesto/de desarrollo y NO usarlos para certificar comprensión ciega.** No inferir que Codex los leyó. No ejecutar `git filter-repo`, force-push ni reset destructivo para borrarlos del historial. Tras respaldo privado y revisión del propietario, puede eliminarse su copia del árbol en un commit normal; ello NO restaura el secreto histórico. Preparar un **nuevo holdout sintético privado, distinto**, generado/guardado por evaluador independiente fuera de repo y fuera del workspace de Codex, congelado por SHA-256 *después de congelar la implementación*. No abrirlo por parte del implementador, no poner rutas que hagan auto-discovery al IDE, no introducir expectativas en prompts/modelos. Las dos evaluaciones humanas independientes deben hacerse sobre salidas producidas sin ver las expectativas.

### BLOQUEO 3 — Certificación y calidad real aún ausentes

- `5d26b7`: 0 ejecuciones GitHub Actions registradas. Necesita los cuatro workflows **del mismo SHA que termine aprobado**.
- Prueba de journal en Firestore Emulator `AgentJournalEmulatorTests.Two_emulator_clients_enforce_one_owner_atomic_context_and_physical_tombstone` preparada, pero todavía sin ejecución real confirmada en ese SHA.
- Groq/Gemini real: ninguna muestra autorizada observada; naturalidad, NLU, latencia y cuota/costo siguen UNKNOWN. Tests con interpretación inyectada no son prueba de calidad humana.
- P0 histórico de reservas inesperadas permanece ABIERTO; DEMO solo con bloqueo independiente comprobado; sin operaciones comerciales reales.
- Retención física de journal es por compactación bajo scope sintético, no automática. Costos, IAM y privacidad reales pendientes. No datos personales reales.

## 2. Secuencia de trabajo a Codex (sin reiniciar fases A–K)

### P0 de trabajo 1 — Preflight local y prueba roja

1. Leer `AGENTS.md`, `RELEASE_CANDIDATE.md`, `docs/auditorias/llm-first-agent/CERTIFICACION_PRE_DEMO.md` y `ESTADO.md`.
2. Inspeccionar HEAD local, `origin/feature`, remoto, status tracked/staged/untracked, y verificar qué archivos fueron publicados. Preservar trabajo local y artefactos TRX; no reset/clean/stash automático.
3. Declarar `5d26b7` como versión remota observada, si sigue vigente; revalidar en el momento de la ejecución.
4. Crear test rojo para reinicio con identity omitida/cambiada y ruta comercial de tenant antes protegido. Demostrar fallo sobre la implementación actual (sin publicar ni ejecutar transacción real).

### P0 de trabajo 2 — Seguridad durable/fail-closed

Implementar el contrato de identidad protegida estable y prueba de arranque de Production/Staging **antes de activar workers**. Debe mantener seguro `main` sin DEMO configurado y conservar la ruta legacy para otros tenants. No agregar guard global que bloquee arbitrariamente clientes ajenos, pero sí rechazar el arranque del host explícitamente declarado DEMO cuando falte la identidad aprobada. Probar todas las superficies de escrituras comerciales y cambios de configuración; si hace falta un mecanismo operativo externo, entregar runbook verificable y marcar el gate BLOCKED hasta ejecutarlo. No confiar en un singleton por proceso como único control.

### P1 de trabajo 3 — Higiene del holdout y freezing

Identificar los dos artefactos expuestos por **NOMBRE, SIN LEER CONTENIDO**; registrar en el informe que están en el commit público. No reescribir historia. No crear un falso holdout desde Codex. Proponer retirada no destructiva del árbol para aprobación del propietario (nunca borrar las copias originales sin respaldo); dejar `.gitignore` adecuado para futuros corpora privados si no modifica corpus congelados. Excluirlos del staging del release candidate y confirmar que Docker allowlist no los incluye. El reemplazo privado lo proveerá un evaluador distinto del implementador.

### P1 de trabajo 4 — Certificación reproducible

Tras pasar pruebas locales y sin cambios pendientes, preparar para autorización de commit/push una revisión limpia en la misma rama. **No commit/push sin permiso expreso del propietario.** Congelar `RELEASE_SHA` después del commit autorizado. La identidad del release debe coincidir en todos los workflows y artefactos:

1. `rc3-release-checks.yml`: G8 vigente, histórico exacto y frontend.
2. `rc3-reservation-integration.yml`: PostgreSQL + Firestore Emulator. Exigir explícitamente ejecución PASS de nuevo test AgentJournalEmulatorTests, no sólo umbral de número de tests.
3. `concierge-redis-integration.yml`: Redis integrado temporal.
4. `agent-runtime-arm64-certification.yml`: arm64 nativo con `release_sha` SHA completo, sin ejecutar migrator. Custodia de imagen solo con autorización de cuota y coste.

No disparar workflows sin autorización de usuario. Si cambia el SHA, volver a certificar los cuatro en el nuevo SHA. Guardar Run IDs, intentos, head_sha, artefactos e inventario TRX. Todos los semáforos deberán permanecer NO_GO mientras se espera.

### P1 de trabajo 5 — Evaluación real controlada

Con nuevo holdout privado congelado, aprobación de modelos y cuotas, y autorización de presupuesto, ejecutar **una sola** conversación sintética por `scripts/llm-first/evaluate.py --live` con marcador anti-replay, topes HTTP/tokens y credenciales solo en entorno seguro. Si falla o hay timeout incierto, NO reintentar con la misma identidad. Evaluadores humanos independientes califican naturalidad, comprensión, contexto y utilidad sin ver expectativas ni prompt/código; 0 operaciones indebidas/filtración tenant/invenciones es condición absoluta. Los casos restantes requieren decisión posterior, no ampliar automáticamente gastos.

### P1 de trabajo 6 — DEMO_DEPLOY_GO

Solo autorizar entorno separado, instancias/teléfonos de prueba, datos sintéticos y protecciones inmutables atestiguadas. `AgentDemo__Enabled=false` durante despliegue inicial; ingreso DEMO físicamente/rutadamente aislado para rollback. Verificar `/health/live` y `/health/ready`, guard de escritura, workers, cola antigua vacía, outbox, Caddy, Evolution, imagen/digest exactos. No tocar main, no migraciones ni escrituras comerciales. El operador debe autorizar SHA/digest/destino/ventana antes de ejecutar el runbook. Ante fallo: deshabilitar/aislar ingreso DEMO, nunca volver silenciosamente al legacy desprotegido ni reenviar UnknownDelivery. P0 histórico sigue NO_GO comercial.

## 3. Entregables y gates

- `docs/auditorias/llm-first-agent/CIERRE_GUARDIA_REINICIO_Y_RELEASE.md`: defectos reproducidos, código modificado, resultados rojos/verdes, pruebas de reinicio/hosts/DI, rutas de escritura, privacidad y necesidad de controles operativos.
- Actualizar `docs/auditorias/llm-first-agent/ESTADO.md` y `RELEASE_CANDIDATE.md` con identidad nueva, holdout expuesto, matriz de certificación y próximos pasos.
- Tabla final explícita: `OFFLINE_GO`, `PROVIDER_EVAL_GO`, `DEMO_DEPLOY_GO`, `COMMERCIAL_GO` con evidencia y SHA.
- No atribuir los antiguos 2128 PASS a un nuevo commit sin reejecutarlos; nunca transformar 29 incompatibilidades históricas en 0 rojo por modificar tests. Distinguir mocks de NLU real.
- Nunca abrir corpus/rúbrica expuestos ni el eventual reemplazo privado como parte del trabajo de Codex. No usar códigos, frases o hashes del conjunto externo como fixture.
- **Stop** ante fuga de workspace, consentimiento bypass, operación comercial efectiva, datos reales o retroceso a legacy bajo protección. No hacer despliegue, proveedor real, WhatsApp real, push o merge sin permiso explícito.

## 4. Objetivo de producto invariable

NexFlow debe funcionar como **secretario virtual natural con IA**: entender frases espontáneas, reformulaciones, interrupciones, cancelaciones y referencias; consultar catálogo real; guiar al cliente con respuestas útiles y no rígidas. Nunca equiparar «compiló y pasaron tests simulados» con «calidad como AxionLabs». Validar con muestras reales de proveedor y juicio humano antes de anunciar experiencia lista.

