# NexFlow LLM-first — Auditoría remota y certificación previa a DEMO

Fecha: 2026-11-03 (hora del proyecto, Perú). Repositorio `https://github.com/saintdeyb321/NexFlow`. **No se desplegó ni se ejecutó CI ni IA externa desde esta revisión.**

## 1. Identidad real verificada

- `main`: `5f5ff850e31df3fa73cb2c7f02f65ba6081173c7`, sin integrar la arquitectura experimental.
- `feature/llm-first-agent`: `6bba4c4d86ac5bd0a5dbb3c6476916fbb5b238dd`, **cuatro commits adelantada** respecto a main; incluye el cierre F–K publicado después del informe que afirmaba árbol sin commit. Verificar cualquier cambio posterior antes de trabajar.
- No se encontraron runs de Actions bajo `feature/llm-first-agent` al momento de la auditoría; los resultados 2123/2123, dirigidos 67/67, históricos 130/101/29, Python 13+9 y build0 son evidencias **locales documentadas**, no CI independiente. Los TRX originales están en `backend/TestResults/LlmFirstFinal_20261103`, ignorados y ausentes de la fuente remota.
- Consultar `docs/auditorias/llm-first-agent/CIERRE_FINAL_LLM_FIRST.md` y `RELEASE_CANDIDATE.md` en el SHA **6bba4c4**; sus encabezados todavía reflejan el HEAD local anterior `f537cee`. No falsificar procedencia ni atribuir tests al nuevo SHA sin ejecución fresca.

## 2. Código inspeccionado y alcance real

- `AgentDemoPolicy.cs`: protección persistente de un único WorkspaceId, allowlist de instancia Evolution y 1–5 teléfonos, autorización de operador fija, Enabled false conserva Protección **si WorkspaceId no cambia**. Si la configuración omite o cambia WorkspaceId, las barreras dejarían de proteger al anterior. Testear la protección durante recargas y el rollback; no depender solo de buena operación humana.
- `AgentDemoChannel.cs`: usa journal del turno y `OutboundMessageService` duradero, contrasta texto/identidad, evita repost de UnknownDelivery; maneja handoff como solicitud/modo persistido, no notificación demostrada. No hay un segundo gateway.
- `ProcessIncomingMessageCommandHandler.cs` + `AiResponseOrchestrator.cs`: DEMO entra antes de interpretación legacy por la vía protegida; kill switch no cae a legacy; otros workspaces usan la ruta histórica.
- `DemoCommercialGuards.cs`: decorador de siete operaciones de IReservationEngine y mutaciones del IOrderRepository registradas. `DemoReservationWriteInterceptor.cs`: bloqueo EF SaveChanges para entidades Reservation. Esto **no prueba todas las rutas de escritura posibles** (SQL directo, otros repositorios/adaptadores, workers); comprobar call graph, DI de Production y la infraestructura aislada.
- `AgentBookingDrafts.cs`, `AgentResponseAuthority.cs`, `AgentDialogueRuntime.cs`: extracción tipada de slots, borrador reversible y redacción con fragmentos factuales de backend; los tests usan intenciones preparadas. Redacción natural/entendimiento verdadero todavía pendientes de evidencia de proveedor y personas.
- `FirestoreAgentTurnJournal.cs`: transacciones CAS y replay, `CompactBatchAsync` limitado y manual, conserva tombstones. No es purga automática, no certifica IAM/costos ni elimina contenido de contextos, historial u outbox. Probar con emulador real antes de canal DEMO.
- `AgentJournalEmulatorTests.cs`: test `Category=Integration` listo; el workflow `rc3-reservation-integration` ejecuta `Category=Integration` en PostgreSQL efímero + Firebase Emulator; confirmar en TRX y logs que el caso efectivamente se ejecutó, no skip.
- `scripts/llm-first/evaluate.py`: runner offline/live con hash y huella de revisión, protección de intentos inciertos. Aún no hay ensayo HTTP real ni aprobación de cuotas.

## 3. Riesgos abiertos, ordenados

**P0:** incidente histórico de reservas inesperadas SIN causa raíz cerrada. Prohibido reservar, modificar, cancelar citas o pedidos de clientes reales. El DEMO debe mantener bloqueo de mutaciones independientemente de la interpretación.

**P1 previo al despliegue DEMO:** certificación real de Firestore Emulator, límites de IAM, rutas alternativas de escritura, protección invariable de workspace, build e imagen ARM64 del SHA, efectividad de outbox/UnknownDelivery en infraestructura, recursos y rollback de API. Los cuatro workflows verdes de main anterior no valen para la nueva rama.

**P1 de producto:** comprensión espontánea de lenguaje cotidiano, naturalidad, referencias, interrupciones, y fallos de proveedor/JSON bajo datos sintéticos. Los 40 casos de desarrollo no son benchmark ciego. No prometer alcanzar AxionLabs.

**P2 operación:** política de retención/compactación más costos Firestore y límites de tokens cuando aumenten los workspaces. Handoff completo a humano no probado.

## 4. Material holdout independiente creado fuera de la rama

- Corpus sintético separado del implementador: `NEXFLOW_EVALUACION_HOLDOUT_12_SINTETICAS_2026-11-03.json` (12 conversaciones / 39 turnos, esquema compatible con `AgentEvaluationData`, no diseñado por Codex).
- SHA256 **A64059637C90E802F7A4692BF3B4BED38CF9070D16BC0F0F7E8C8EFEA33549B5**. Congelar bytes; al copiar revisar hash.
- Rúbrica preparada: `NEXFLOW_RUBRICA_HUMANA_HOLDOUT_2026-11-03.md`; necesita **dos evaluadores humanos reales**. No se ha hecho evaluación humana ni llamadas de IA.
- Guardar ambos archivos **fuera de C:\Proyectos\NexFlow y fuera de Git**, sin enviar etiquetas/expectativas a Codex. Solo revelar al runner la ruta+hash después de congelar el código y modelos. Si el implementador abre las respuestas esperadas, la muestra deja de ser ciega. No usar un fallo del holdout para ajustar prompts y después presentar la misma muestra como independiente.

## 5. Protocolo de continuidad — ahora certificación, NO más features

### Gate A — Identidad y auditoría del árbol

1. Leer `AGENTS.md`, esta auditoría, `CIERRE_FINAL_LLM_FIRST.md`, `RELEASE_CANDIDATE.md` y `ESTADO.md`.
2. Verificar `git status --porcelain=v1`, branch, HEAD, `origin/main`, `origin/feature/llm-first-agent`, diff de tracked **y untracked** sin reset/clean/stash.
3. Verificar que `6bba4c4` contenga el código del cierre y que el árbol local no tenga cambios posteriores. Si difiere, no atribuir evidencias equivocadas; detener publicaciones y hacer diff.
4. Revisar especialmente DI/EF/Firestore y todos los mutadores del workspace protegido; aportar lista y evidencia por rutas directas. Si hay agujero crítico, escribir test rojo y corregir sin desactivar barreras.

### Gate B — Cuatro workflows del mismo commit (autoriza propietario)

- `.github/workflows/rc3-release-checks.yml`
- `.github/workflows/rc3-reservation-integration.yml` (**exigir test AgentJournalEmulatorTests ejecutado y PASS**)
- `.github/workflows/concierge-redis-integration.yml`
- `.github/workflows/agent-runtime-arm64-certification.yml` con `release_sha` exactamente la revisión seleccionada, validación UID/GID 1654; imagen custodiada solo con cuota y autorización explícita.

Exigir los cuatro resultados `success`, SHA exacto, run_id/attempt, cobertura y sin skipped inesperados. Ningún workflow viejo cuenta. Ejecutar los workflows de forma manual solo después de autorización del propietario, y no tocar main/Oracle.

### Gate C — Muestra independiente real, sin efectos

1. El propietario valida cuotas/modelos y política de datos de Groq/Gemini; NO pegar claves en prompt, log ni chat.
2. Ejecutar `--dry-run` sobre el corpus sellado primero. La propia validación consume 0 HTTP.
3. Con autorización expresa de **UNA conversación**, ejecutar runner `--live`, máximo HTTP y reserva configurados; bloquear si hay intentos históricos uncertain. Revisar reporte, `ProviderHttp`, tokens reportados o UNKNOWN y observaciones por turno. Si un proveedor no se usó, indicarlo.
4. Obtener después aprobación de nuevas muestras; no ejecutar automáticamente las 12 conversaciones. Entregar transcript sintético a evaluadores humanos para rúbrica (sin expectativas).
5. La comparación legacy vs agente debe registrar utilidad, claridad, repetición, errores factuales, referencias, interrupciones y coste; NO evaluar con JSON inyectado.

### Gate D — Release candidate DEMO (solo cuando A/B/C cumplan)

1. Confirmar workspace sintético exclusivo y Evolution instance dedicada, teléfonos allowlist, configuración privada de `AgentDemo__WorkspaceId` y `AgentDemo__Enabled=false` durante preflight; el workspace se mantiene protegido al apagar el flag.
2. Confirmar que **ningún cliente real** tenga tráfico sobre ese workspace, que no haya mutaciones ni workers alternativos, que el P0 sigue bloqueado y que no se expondrá PII.
3. Construir/importar imagen ARM64 verificada por digest, conservar imagen anterior y el Compose efectivo/overrides, nunca ejecutar migrator.
4. Solicitar autorización específica de SHA/digest, servidor, ventana, configuración y prueba de rollback. No iniciar deploy si falta una.
5. Una vez autorizado, reemplazar **solo servicio API** con el runbook `RELEASE_CANDIDATE.md` y el inventario real; verificar health, envío desde teléfono DEMO, no-effect en DB, replay, UnknownDelivery y kill switch. El rollback debe mantener DEMO aislado; imagen vieja podría no tener barreras comerciales.

## 6. Gates finales independientes

| Decisión | Estado en esta auditoría |
|---|---|
| OFFLINE_GO | GO **reportado local**; requiere reejecución/correlación SHA de código publicado |
| PROVIDER_EVAL_GO | HOLD hasta quotas/credenciales/autor. La muestra sellada ahora existe; no equivale a resultado |
| DEMO_DEPLOY_GO | NO-GO hasta workflows/ARM64/FireStore/scope y autorización expresa |
| COMMERCIAL_GO | NO-GO mientras P0, privacidad, operaciones y calidad humana no estén certificados |

El objetivo siguiente no es sumar miles de tests sin comprender clientes: primero validar infra y ejecutar una conversación real sintética del modelo bajo presupuesto. Nunca afirmar producción segura a partir de un preview.

## 7. Entrega requerida a Codex en su siguiente intervención

- Una auditoría del **commit publicado exacto** y diferencias del árbol actual, con evidencia de DI, workers, mutadores e interceptores.
- Si el árbol está limpio: runbook preciso para los cuatro workflows (no ejecutarlos sin autorización); checklist de verificación de run_id/sha/journal emulator.
- `docs/auditorias/llm-first-agent/CERTIFICACION_PRE_DEMO.md` con matriz de gates y rutas exactas del holdout **sin contenido ni etiquetas**.
- Solo defectos concretos reproducidos deben generar cambios; evitar agregar un framework nuevo o reglas por frases.
- Sin commit/push/merge, sin main, sin migraciones, sin providers, sin WhatsApp ni Oracle hasta su autorización.
