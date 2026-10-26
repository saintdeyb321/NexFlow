# PROMPT FINAL DE CIERRE — NexFlow Agent Runtime 4.0 / MVP comercial

**Rol:** principal engineer, QA/release lead y coordinador del operador de NexFlow. **Este es el ÚLTIMO encargo transversal. No abrir un Sprint 12 ni entregar sólo otro paquete documental.** Ejecutar tantos gates como permitan las autorizaciones/herramientas y, cuando se necesite intervención humana externa, entregar el comando/procedimiento exacto y detener esa acción hasta tener resultados. Volver al mismo programa de cierre después de cada evidencia, no crear otra hoja de ruta.

**Repositorio local:** `C:\Proyectos\NexFlow` (Windows).  
**GitHub:** `https://github.com/saintdeyb321/NexFlow.git`.  
**HEAD remoto observado en auditoría:** `991a4ac27c4d233a6fc0cd5d81c720859752fc74` (S6–S8 documentales).  
**Último commit que modificó código:** `be07f89c2146f7c4344effad849e1335c9614e86` (S3–S5).  
**Producción reportada históricamente:** `nexflow-api:306d7c8-perms1`, Oracle Ubuntu ARM64; su estado **actual no está verificado aquí**.  
**Frontend público reportado:** `VITE_API_URL=https://api.158.101.120.8.sslip.io/api`; base de health: `https://api.158.101.120.8.sslip.io`. No asumir que sigue vigente sin comprobar.

## 0. Material que DEBES leer antes de ejecutar

1. `AGENTS.md`.
2. `docs\auditorias\IMPLEMENTACION_S6_S7_S8_MVP_2026-10-26.md`.
3. `docs\auditorias\MATRIZ_Q01_Q10_VALIDACION_MVP.md`.
4. `docs\auditorias\PLAN_PILOTO_E2E_WHATSAPP_MVP.md`.
5. `docs\auditorias\GO_LIVE_CHECKLIST_NEXFLOW_MVP.md`.
6. `docs\auditorias\IMPLEMENTACION_S3_S4_S5_MVP_2026-10-26.md` y `IMPLEMENTACION_S1_S2_MVP_2026-10-26.md`, sólo puntos de regresión relevantes.
7. **Auditoría de cierre del revisor (documento del propietario):** `docs\NexFlow_Auditoria_Cierre_S6_S8_2026-10-26.md`.

Si un archivo no existe, indícalo y **usa la versión del repositorio cuando corresponda**; no inventes su contenido. Conserva los documentos ajenos. No hagas checkout/reset/pull/stash, merges, commits ni push sin una instrucción nueva y explícita del propietario. Ejecuta `git status --short`, `git rev-parse HEAD`, `git diff --check` y delimita worktree/untracked. Separa fuentes certificadas de documentos y de imagen activa; jamás asumas igualdad de SHA.

## 1. Principios inviolables

- IA (`AiRouter`, Groq/Gemini) interpreta intents/evidencias/acciones tipadas; backend es la **única autoridad** para workspaces, licencias, precios, horarios, consentimiento y transacciones. Nunca SQL, Firestore o HTTP arbitrario dado al LLM, ni loop autónomo de escrituras.
- Una sola arquitectura compartida; NO crear un bot por negocio, otro motor de pedidos/reservas, estado paralelo ni árbol de keywords como sustituto de comprensión.
- `CATALOG`=PRODUCT, `SERVICES`=SERVICE, `ORDERS+CATALOG`=solicitud PendingReview, `RESERVATIONS+SERVICES`=reserva real, `REQUESTS` independiente. No pagos, stock garantizado, ERP, módulos de empleados.
- Preservar `WorkspaceId`, CAS/StateVersion, FIFO, idempotencia, recibos/outbox, revalidación de licencia/sede/importe, F7-01, `UnknownDelivery` sin reenvío ciego, takeover humano y prohibición de consultar/editar/cancelar citas privadas sin acreditación independiente.
- Una pregunta, una disponibilidad o un «vale» NO equivalen a seleccionar ni confirmar. Resumen y consentimiento explícitos vigentes para operación real; no replicar efectos comerciales.
- No degradar aserciones/hashes/corpus históricos ni ocultar 12 incompatibilidades congeladas. Nunca escribir secretos, teléfonos, clientes, contextJson/PayloadJson bruto, tokens, enlaces firmados o claves en código, informes ni logs.
- **Nunca modificar Oracle, producción, Evolution, n8n, PostgreSQL, Redis, Firestore real, roles, licencias o conexiones de WhatsApp por inferencia de este prompt.** Toda acción sobre servicios/datos vivos requiere autorización específica del propietario en el momento del gate y un operador con acceso real. Una aprobación de desarrollo no equivale a consentimiento para cambiar producción.

## Método de trabajo (fundamental)

Ejecuta **S9 → S10 → S11** como gates consecutivos; entrega una tabla `OK / NO-GO / BLOQUEADO / NO EJECUTADO` por subgate, con evidencia SHA/ID/run/artefacto, firma/responsable y próximos comandos. Si falta un permiso, utiliza la alternativa no destructiva y produce un paquete de ejecución exacto; **NO marques como completado** ese gate ni saltes a despliegue. Puedes continuar tareas locales independientes de gates bloqueados sin afirmar liberación global. Una falla P0/P1 de consentimiento, aislamiento, duplicación, fuga, F7-01 o incompatibilidad con estado durable bloquea release, preserva evidencia y solo admite corrección mínima con pruebas rojo→verde. Cambios de código => nuevo SHA y repetir TODOS los gates afectados. Mantén el estado final en `docs/auditorias/CIERRE_MVP_GO_LIVE_EVIDENCIA.md`, sin generar sprints nuevos.

# SPRINT 9 — CERTIFICACIÓN REPRODUCIBLE DEL SHA FINAL

**Meta:** sustituir los PASS locales reportados por evidencia remota correlacionada y validar la imagen real ARM64. No reescribir backend salvo defecto reproducido.

### S9-A. Congelar candidato

- Comprueba HEAD y worktree. Inspecciona cambios respecto a `be07f89c…` y `991a4ac…`; cuando solo difieran Markdown, márcalo. Decide `RELEASE_SHA` inmutable tras revisión del propietario. Si el candidato local no está publicado, **solicita al propietario permiso específico de publicar la revisión**; no hagas push por defecto.
- Registra `<SHA completo>`, commit tree, origen, responsable, permisos de lectura y link de GitHub. Nunca certifiques por prefijo `991a4ac`.

### S9-B. CI real del MISMO SHA

- Los tres workflows son manuales, NO se han ejecutado para `991a4ac…` al auditar:
  1. `.github/workflows/rc3-release-checks.yml` (backend G8, históricos, frontend lint/build/TS/browser, codec Windows).
  2. `.github/workflows/rc3-reservation-integration.yml` (PostgreSQL real desechable + Firestore Emulator).
  3. `.github/workflows/concierge-redis-integration.yml` (Redis desechable + PostgreSQL + Firestore Emulator).
- Si existe acceso a GitHub Actions y permiso explícito de **lanzar workflows** para el SHA congelado, puedes iniciar esos tres y recopilar results; de lo contrario produce los enlaces de Actions, comandos reproducibles `gh workflow run ... --ref ...` únicamente **si `gh` está instalado y el `ref` existe**, y solicita al operador que los ejecute. No inventes Run ID.
- Exige en cada run `head_sha == RELEASE_SHA`, `status=completed`, `conclusion=success`, run_attempt, artifact ID/identidad y resultados completos. No aceptar ejecuciones de `306d7c8` u otra revisión.
- G8 `current` sin fallos/skips (inventario vigente 1538/1538 reportado localmente; si difiere, investigar), `historical` exactamente 130/118/12 con hash+motivo de los doce casos congelados y validador original aprobado; frontend TS≥83/browser≥148 sin omisiones. Integration inventario actual 39 descubiertos, ejecutados/aprobados todos (workflow mínimo 37 no permite ignorar 2 que desaparezcan); Redis exactamente 1 PASS. ProviderIntegration (2) NO se cubre por estos workflows.
- No borrar archivos existentes, cambiar filtros/thresholds ni hacer tolerar nuevas incompatibilidades por conveniencia. Si falla, aislar causa con logs sanitizados, corregir sólo el defecto y emitir nuevo SHA (reiniciar gates).

### S9-C. Build y pruebas Linux ARM64 **AISLADAS**, no en producción

- Ejecutar solo en runner efímero/host expresamente autorizado, nunca crear contenedores de prueba sobre el servidor productivo sin permiso. Contexto allowlist de fuentes versionadas, excluyendo .env, firebase.json, claves, bin/obj, .git y archivos ajenos. Respetar build Dockerfile versionado.
- Construir ambos targets (`final`, `migrator`) para `linux/arm64`; etiquetar con SHA OCI y registrar ID/digest reales de imagen, plataforma y digests de bases. No ejecutar el entrypoint migrator ni conectarlo a PostgreSQL real.
- Comprobar usuario UID/GID 1654 no-root, lectura de `/app/appsettings.json`, DLL/deps/runtimeconfig y directorios incluso en variante restrictiva 600/700; no exigir modo 600 en todos los publish normales. Probar que los secretos montados sólo se leen con los permisos previstos sin imprimirlos; en smoke sintético no conectar Firebase real.
- Ejecutar CodecSmoke sobre la misma imagen Linux ARM64 para PNG, JPEG y WebP; exit 0 y resultados observables. Una inspección de arquitectura sin codec no es PASS. Retener artefacto por digest y su procedencia.
- Si no hay runner aislado ARM64, produce un procedimiento ajustado a runner CI; etiqueta `BLOQUEADO`, NO tomes Docker de Oracle como sustituto automático.

**Gate S9:** tres runs mismos SHA con resultados y artifacts auditados + build ARM64/UID/permisos/codecs reales + esquema API/config compatible. Si cualquier ítem falta, NO-GO para release; continúa solo preparación no destructiva de S10.

# SPRINT 10 — DATOS REALES, PDF, PRIVACIDAD Y RECUPERACIÓN

**Meta:** eliminar bloqueos de seguridad/comercial sin fingir que mocks y documentos son pruebas de datos reales.

### S10-A. Evidencia Q01–Q10 del workspace DEMO, solo lectura

- Leer `MATRIZ_Q01_Q10_VALIDACION_MVP.md` íntegro. Prepara **consultas por SDK/SQL parametrizadas**, con timeout y límites, y solicita permiso/rol de lectura del operador antes de consultar Oracle/Firestore. Confirma el esquema real y enum/columnas antes de ejecutar ejemplos SQL. Nunca concatenar identificadores controlados por usuario/IA.
- Q01 imagen/identidad; Q02 módulos/roles/licencia efectivos; Q03 catálogo PRODUCT/SERVICE y sedes; Q04 metadata/correspondencia PDF CURRENT/Origin/SourceHash; Q05 horarios/timezone; Q06 referencia/pregunta/estado seudonimizado; Q07 interpretación y uso IA; Q08 efecto de reserva+outbox real en lectura posterior autorizada; Q09 inbound/outbound/delivery; Q10 decisión visual fiscal/publicación. Distinguir Q08/Q09 que exigen escrituras DEMO previas del paso sólo lectura.
- No usar endpoints de lectura que puedan guardar cambios (`GetArtifactAsync`, normalizadores outbound o recovery). No exportar PII/prompts/secretos. Proyección pública de las cinco ofertas, cuatro reservables y **razón exacta por ítem** (requiere flag, duración, sede, estado, categoría y versiones reales). No reemplazar datos del negocio para hacer cuadrar cantidades.

### S10-B. Política fiscal y PDFs

- Revisar con el dueño la política exacta sobre `TaxId`: incluir en documento solo si existe base/autorización expresa; política recomendada de minimización por defecto. `CatalogGenerationService.GetContentAsync` lo transmite actualmente, pero NO hay prueba de que se imprima en PDF. Validar template n8n y PDF final de prueba con datos ficticios por ámbito PRODUCT/SERVICE.
- Si la decisión obliga a corregir código/template, primero reproducir el problema, corregir **mínimamente** proyección/plantilla/whitelist, regeneración de SourceHash, status `CURRENT`/`STALE`, callback/HMAC/GenerationId y regressions. No alterar servidor n8n ni PDF productivo sin autorización separada. Un cambio exige nuevo RELEASE_SHA y reejecución S9.

### S10-C. Recuperación Firestore Spark y almacenamiento externo

- PostgreSQL tiene un backup histórico restaurado de forma aislada; **NO** respalda Firestore ni PDF objetos externos. No tocar plan/billing. Si se elige mantener Spark, diseñar e implementar únicamente cuando sea autorizado un backup **lógico por SDK** acotado, cifrado y completo (colecciones y subcolecciones por W, campos tipados, IDs/versiones, estados de conversación/outbound, operaciones globales por workspace, artefactos, objetos externos/manifest según alcance), con límites de cuota y almacenamiento protegido. No introducir exportación administrada incompatible con plan sin revisión.
- Probar primero backup+restore **sintético** en Firestore Emulator desechable en loopback, sin credenciales cloud, API/workers/WhatsApp desactivados; verificar esquema, conteos, hashes, tipos, CAS, dedupe, `UnknownDelivery`, registros terminales, ámbitos, consistencia con snapshot SQL y que Redis no es autoridad. **No restaurar sobre producción**, ni reenviar mensajes ni resetear estados/huellas para que apruebe.
- El ensayo real posterior necesita autorización expresa y revisión de RPO/RTO, custodia/retención/cifrado; una exportación no atómica es evidencia insuficiente hasta demostrar quietud y coherencia. Si no puede recuperarse Firestore, el único candidato admisible será DEMO **totalmente descartable y aprobado**; bloquear ventas y datos reales.
- Evitar añadir un servicio permanente o modificar infraestructura si una herramienta interna de operación aislada satisface el objetivo con menor riesgo. No afirmar PASS sobre recuperación solo por crear scripts y documentos.

**Gate S10:** proyecciones Q01–Q10 observadas dentro del alcance que corresponda, decisión/validación PDF y recuperación/restauración aislada firmada para uso comercial. Si Firestore bloquea, estado `NO-GO COMERCIAL`; no gastar ni cambiar plan sin el propietario.

# SPRINT 11 — EVALUACIÓN INDEPENDIENTE, PILOTO Y PREPARACIÓN DEL CORTE

**Meta:** certificar operación real con varios negocios y disponer de un deployment plan exacto y seguro, sin reemplazar la API por el simple hecho de terminar un prompt.

### S11-A. Evaluación independiente antes de piloto

- `PLAN_PILOTO_E2E_WHATSAPP_MVP.md` exige **mínimo 32 conversaciones multitur inéditas** custodiadas por un evaluador ajeno, W-A clínica, W-B comercio, W-C mixto, W-D limitado; corpus/hash/expectativas fuera del repositorio y sin acceso del implementador a oráculos privados. Si no hay evaluador/corpus, **BLOQUEADO**; nunca fabricar un resultado «ciego».
- Rúbrica ratificada **antes** de observar resultados: cero fallos de seguridad/consentimiento/tenant/duplicado; propuesta documental ≥90% tareas correctas o derivadas legítimamente, ≥4/5 promedio por dimensión humana, ningún error comercial grave. Dos revisores humanos distintos, otro auditor de invariantes. Registrar scores y oráculos privados sin PII al repositorio.
- Proveedores Groq/Gemini: verificar el harness ProviderIntegration (2 tests) y distinguirlo de evaluación del AiRouter y runtime completo. Solicitar modelos exactos, presupuesto/costo en moneda, máximo de intentos/reintentos/tokens, cuotas, responsable y autorización de llamadas antes de ejecutar. No asumir que dos tests equivalen a dos POST; si no se puede garantizar el máximo de costo, NO llamar.

### S11-B. Piloto real controlado (solo con consentimiento aparte)

- En entorno DEMO aislado/descartable, participantes y número de WhatsApp autorizados, usar conexión Evolution preexistente sin desconectar sesiones ajenas. En host API único, confirmar que existe un método **real** de aislar tráfico/admisión; no inventar flags per-tenant inexistentes. Sin aislamiento, NO-GO.
- E2E: perfil/FAQ, productos/precios, servicios/sedes/horarios, PDF PRODUCT vs SERVICE, reserva disponibilidad→acuse→selección→resumen→confirmación (exactamente una reserva real), pedido capacidad→inicio→cotización→consentimiento→una solicitud `PendingReview`, interrupciones, handoff de citas privadas, revocación, replay, incertidumbre de entrega y fallo proveedor sólo en entorno desechable controlado.
- Verificar por cada transacción respuesta en WhatsApp **y** durable PostgreSQL/Firestore/outbox/outbound y panel, con IDs internos privados y métricas sanitizadas. No asumir que HTTP 200/Sent == Delivered/Read. Ante fallo de consentimiento, mezcla W, datos privados, doble efecto o `UnknownDelivery` reenviado: STOP.
- Registrar p50/p95 latencia, errores, backlog, intentos IA, tokens/usage y costo observado; umbrales y presupuesto aprobados antes de ejecutar, no fabricados a posteriori. Rol/permiso web y onboarding/licencia del tenant deben pasar con datos reales DEMO.

### S11-C. Preparación y gate de despliegue (NO automático)

- Leer `GO_LIVE_CHECKLIST_NEXFLOW_MVP.md`. Hacer **preflight read-only** por operador autorizado: estado actual de `nexflow_api`, labels Compose, image ID/digest, project/service/config files, 12 overrides reportados históricamente, runtime Production, esquema/EF compatible, APIs y workers. El servidor puede haber cambiado: **nunca reutilizar ciegamente rutas o base SHA**.
- Preparar override de SOLO `services.api.image` con imagen digest arm64 exacta y probar equivalencia efectiva de `docker compose config --format json` **comparando en memoria**, sin imprimir configuración/secretos. No tocar redes, mounts, env, base de datos, migrator, Firebase, Caddy, Redis, Evolution, n8n o Gotenberg.
- Identificar barrera de admisión global y todos los escritores; drenar de manera comprobable, incluyendo inbound/outbox/outbound/PDF, con período de quietud y thresholds previos. No cancelar, borrar, reencolar ni drenar a la fuerza. No `docker compose down`, `prune`, DDL, migraciones automáticas o restauración in-place.
- Backup y restauración aislada de datos dentro del alcance autorizado, imagen antigua retenida; probar rollback compatible **o** disponer de fix-forward plan con contención, sin reintroducir F7-01 ni convivir con escritores de versiones inseguras. No hacer rollback a `0da524…`; `306d7c8-perms1` no es compatible por definición.
- Preparar comandos con parámetros observados, plan de pruebas post-corte, salud HTTPS `/health/live`, `/health/ready`, Redis/PostgreSQL/Firestore, conexión Evolution/n8n/Gotenberg y monitoreo; protocolo de STOP/abortos y recuperación de admisión.
- **Gate humano final obligatorio:** presentar evidencia resumida, digests, impacto, backup/restore, riesgos, ventanas/umbral y un comando de reemplazo de una sola API. Obtener una **autorización explícita específica** del propietario PARA ESE SHA/DIGEST y esa ventana antes de ejecutar el reemplazo. El presente prompt NO es tal autorización. La ejecución, si autorizada luego, será sólo del servicio API: `docker compose ... up -d --no-deps --no-build --pull never api`, con parámetros/servicio verificados y sin dependencias.
- Después de un corte autorizado: identidad de contenedor/digest, startup, readiness, mensajes DEMO, persistencia única, entrega y observabilidad dentro de límites. Si falla, detener admisión y usar exclusivamente plan de recuperación compatible aprobado; no acciones improvisadas. Reabrir tráfico y comercializar son decisiones **separadas**, respaldadas por E2E y soporte.

## 4. Entregable final único y señal de cierre

Actualizar `docs/auditorias/CIERRE_MVP_GO_LIVE_EVIDENCIA.md` (único documento central; artifacts TRX/JSON privados por ubicación). Contener:

1. Identidad local/remota/código/imagen/prod; diff exacto y verificación de que no se sobrescribió trabajo ajeno.
2. **Matriz S9/S10/S11** con `PASS / FAIL / BLOQUEADO / NO EJECUTADO`, evidence link/Run ID/attempt/SHA/digest/hashes y revisores, no promesas.
3. CI vigente/históricos/TS/browser/integración/Redis, imagen ARM64 y codec, permisos no-root; detalle de todo fallo y corrección.
4. Q01–Q10, permisos/flags/5vs4, fiscal/PDF y restauración Firestore (RPO/RTO, límites, ubicación privada de manifest).
5. Proveedores/ejecutor independiente, 32 conversaciones, resultados humanos, llamadas/costo/latencia; WhatsApp real y transacciones con evidencia durable por W.
6. Runbook final con comando exacto derivado del servidor y gate de autorización **PENDIENTE** o resultado ejecutado autorizado; estado post-corte, salud, errores, decisiones de admisión/rollback/fix-forward.
7. Decisión separada: `GO_CANDIDATE`, `GO_DEMO_PILOT`, `GO_CODE_DEPLOY`, `GO_COMMERCIAL`, cada una sólo con evidencias; sin equiparar una a otra.

Si faltan accesos/firmas/datos, **no concluyas «sprint completado»**; entrega una matriz de bloqueadores con **la acción manual mínima exacta** que el operador debe efectuar, y conserva una sola ruta de continuación. No construyas un Sprint 12 ni otro informe teórico. No uses la expresión «MVP terminado» salvo cuando los gates correspondientes estén firmados y probados. No hagas commit, push, cambios a producción ni gastos por defecto.

**FIN DEL PROMPT MAESTRO: continuar con los gates de release y operador, no seguir acumulando documentación o sprints.**
