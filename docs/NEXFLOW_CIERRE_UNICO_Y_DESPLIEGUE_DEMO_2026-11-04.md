# NEXFLOW — CIERRE ÚNICO DE CERTIFICACIÓN Y DESPLIEGUE DEMO

Fecha: 2026-11-04 (America/Lima). Repositorio: `saintdeyb321/NexFlow`.

## Diagnóstico verificado, no conjeturas

**Candidata LLM-first:** `feature/llm-first-agent` = `652a277cccce40a9fc18eec5da3d04c66b899888`.
**Main:** `5f5ff850e31df3fa73cb2c7f02f65ba6081173c7`.

Los cuatro runs enviados por el propietario fueron despachados a **main**, no a feature:

| Run | Workflow | GitHub `head_sha` | Estado |
|---|---|---|---|
| 41904768650 | RC3 release checks | `5f5ff85` | success |
| 41904776798 | PostgreSQL + Firestore | `5f5ff85` | success |
| 41904786867 | Redis integration | `5f5ff85` | success |
| 41905222616 | ARM64 | `5f5ff85` | failure |

**Causa confirmada del fallo ARM64:** el run fue ejecutado con `GITHUB_SHA=5f5ff85` (rama main) y con input `RELEASE_SHA=652a277`. El paso `Require an approved SHA on a native public ARM64 runner` rechazó correctamente que no coincidan. Se abortó **antes de checkout y build**; no es una prueba de fallo del código ni de ARM64. Artefacto de 591 bytes contiene evidencia del rechazo, no una imagen.

Enlaces fuente: https://github.com/saintdeyb321/NexFlow/actions/runs/41904768650 ; https://github.com/saintdeyb321/NexFlow/actions/runs/41904776798 ; https://github.com/saintdeyb321/NexFlow/actions/runs/41904786867 ; https://github.com/saintdeyb321/NexFlow/actions/runs/41905222616 .

**No modificar el guard del workflow. No rerun de 41905222616:** conservaría el contexto de main y fallaría otra vez.

## Resultado deseado esta misma intervención de Codex

Cerrar los fallos de certificación **en un solo lote de corrección**, certificar la feature y **desplegar sólo un canal DEMO sintético aislado** en Oracle ARM64 (si el entorno efectivamente disponible lo permite), con la API y proveedores reales pero **sin transacciones comerciales**. El propietario simulará conversaciones desde teléfonos autorizados. No abrir otra reconstrucción A–K ni esperar un benchmark ciego para iniciar un DEMO acotado; la evaluación independiente y el P0 histórico siguen siendo requisitos para producción comercial.

## Procedimiento concreto sin ciclos de micro-parches

### 1. Inventario y referencia exacta

- Inspeccionar primero HEAD local/remoto; no destruir cambios ni rebases. Exigir `feature/llm-first-agent` y el SHA aprobado; si ha cambiado, identificar el nuevo SHA y reconsiderar la evidencia existente antes de ejecutar.
- Verificar que los workflows usan `workflow_dispatch` y seleccionar expresamente `feature/llm-first-agent` en el selector *Run workflow*. La UI de GitHub suele preseleccionar `main`.
- Si Codex dispone de credenciales de GitHub con permiso Actions write, puede usar `gh workflow run ... --ref feature/llm-first-agent` o GitHub REST `POST /repos/saintdeyb321/NexFlow/actions/workflows/{workflow_id}/dispatches` con body `{"ref":"feature/llm-first-agent"}`; los secretos nunca se imprimen. `gh` ausente **no es un defecto de NexFlow**: utilizar API si hay autorización, o enviar al propietario enlaces/valores listos para un clic. No inventar que se hizo un dispatch.

### 2. Una ronda de CI correcta, sin tocar código aún

- RC3 Release: https://github.com/saintdeyb321/NexFlow/actions/workflows/rc3-release-checks.yml
- PostgreSQL + Firestore: https://github.com/saintdeyb321/NexFlow/actions/workflows/rc3-reservation-integration.yml
- Redis: https://github.com/saintdeyb321/NexFlow/actions/workflows/concierge-redis-integration.yml
- ARM64: https://github.com/saintdeyb321/NexFlow/actions/workflows/agent-runtime-arm64-certification.yml

Seleccionar la rama **feature/llm-first-agent** para TODOS. En ARM64: `release_sha=652a277cccce40a9fc18eec5da3d04c66b899888`; `retain_api_image=false`, `storage_quota_confirmed=false`, `artifact_budget_mib=0` para certificación inicial sin retención, salvo cuota privada ya autorizada y verificada por propietario. La ejecución con retención de imagen es una acción distinta y debe registrar tamaño/cuota/costo. Un artefacto de evidencia no es una imagen distribuible.

Verificar `head_sha`, `run_attempt`, jobs, TRX y hash de artefactos; específicamente PASS de `Two_emulator_clients_enforce_one_owner_atomic_context_and_physical_tombstone`, histórico 130/101/29 y ausencia de omisiones. Nunca aceptar los 3 runs verdes sobre main como evidencia de feature.

### 3. Si aparece fallo real en la feature, corregirlo **de una sola vez**

Recopilar TODOS los fallos y logs de TODOS los workflows de feature antes de editar. Clasificar: bug real, entorno, falso positivo verificable, artefacto/custodia o limitación de cuota. Preparar prueba roja específica cuando aplique, corregir **con un solo parche coherente** y ejecutar dirigido, actual, histórico, Python, build y diff check. No debilitar tests, no alterar consentimiento, P0 histórico, CAS/replay/UnknownDelivery ni límites de IA. Publicar sólo feature (previa coincidencia de repo/permisos y revisión de staged); fijar el nuevo SHA, ejecutar **una ronda completa** de los cuatro CI sobre él. Si falla de nuevo, reportar la causa exacta, no declararlo listo. Evitar cambios decorativos en commits de release.

### 4. Imagen ARM64 exacta, distribución y Oracle

Una certificación ARM64 `retain_api_image=false` **no deja un TAR apto para despliegue**. Para instalar:

- Opción preferida: tras verificar presupuesto de almacenamiento, ejecutar el workflow ARM64 con retención temporal aprobada del mismo SHA (sin cambios de código), descargar el TAR verificado, confirmar Image ID, SHA-256, etiqueta OCI y resultados de smoke, importar y comprobar **esa misma imagen** en Oracle. El workflow impone máximo/retención limitados; comprobar que la imagen cabe y que no se suben secretos.
- Si no se dispone de custodia de la imagen: en un builder ARM64 aislado, construir desde checkout limpio del SHA congelado, registrar digests de bases, ejecutar los mismos tests de arquitectura, permisos, codec y health sobre **la imagen que se pretende instalar**. Esa imagen tiene otra identidad; certificarla de forma independiente, no afirmar que es la del primer run. Evitar publicación accidental.

Reconocer que este chat no tiene acceso SSH/Oracle y no conoce nombres de servicios, DNS, Compose ni credenciales. Codex solo operará si tiene acceso ya autorizado por el propietario al destino inequívocamente DEMO.

### 5. Despliegue real **solo DEMO** (el propietario lo solicita)

Preflight operativo mínimo basado en hechos, no formularios nuevos:

- Destino, imagen anterior, respaldo, servicio API, Compose args y conexión al workspace sintético identificados realmente. No editar `main`, no migrar, no tocar frontend, PostgreSQL/Redis/Firestore/Evolution/n8n de otros workspaces, no `docker compose down`.
- Aislamiento independiente del ingreso y consumidores del workspace DEMO; instancia Evolution y contactos de prueba dedicados; ceros en escrituras/colas comerciales previas; ninguna réplica antigua consume ese workspace.
- `NexFlowHost__Role=Demo`; manifiesto persistente solo lectura con `DeploymentId`, `WorkspaceId`, `AgentProtection__ManifestPath`, `AgentProtection__ManifestSha256` adecuados y fijados en cada réplica. Montaje legible por UID1654; identidad real atestada contra configuración efectiva. Mantener `AgentDemo__Enabled=false` al instalar. Eliminar rol+manifest+pin simultáneamente está **prohibido**: sin ellos puede arrancar Legacy.
- Configurar en privado `AgentDemo__WorkspaceId`, `AgentDemo__InstanceName`, `AgentDemo__TestPhones__0`, `AgentDemo__OperatorAuthorization=SYNTHETIC_DEMO_NO_COMMERCIAL_WRITES`; licencia/módulos de test válidos. Claves Groq/Gemini ya autorizadas sin volcarlas. No usar clientes, pedidos, citas o pagos reales.
- Instalar o recrear **sólo** API desde la imagen aprobada mediante el servicio/Compose confirmado; no lanzar migrator. Con DEMO disabled verificar health live/ready, contenedores, configuración de identidad, outbox/replay, y que operadores comerciales del workspace de prueba permanecen bloqueados.
- Encender únicamente `AgentDemo__Enabled=true` en el workspace sintético autorizado; probar desde teléfono propio autorizado: saludos, preguntas sobre servicios, consulta lateral durante reserva, edición parcial, alcance de cancelación, consentimiento y ayuda humana, además de mensajes duplicados y destinatario no autorizado. Verificar efectos de SQL/Firestore = 0, precios de hechos sintéticos, sin alucinaciones ni publicaciones de confirmaciones reales. Guardar transcripciones sanitizadas, métricas y llamadas/tokens/costos observados; el usuario realizará simulaciones adicionales.
- Si falla barrera comercial, se pierde identidad, una réplica antigua recibe tráfico o hay envíos inciertos no recuperables: **kill switch** (`AgentDemo__Enabled=false`, sin retirar identidad), aislar ingreso DEMO; rollback a imagen anterior únicamente con tráfico DEMO aislado ANTES de iniciarla. No reenviar UnknownDelivery ni reprocesar DeadLetters.

El P0 histórico sigue abierto: **prohibido habilitar cualquier mutación real de reservas/pedidos**, aunque la experiencia DEMO sea buena. No exigir corpus independiente como condición para probar la conversación sintética con el propio usuario; sí será requisito para dar COMMERCIAL_GO y para medir capacidad con estándares.

## Entregable de una sola ejecución

`docs/auditorias/llm-first-agent/CIERRE_EJECUCION_DEMO_2026-11-04.md`: matriz de cuatro Run IDs + SHA, fallos y corrección única, Image ID/digest y proveniencia, alcance real de Oracle si se realizó, health/rollback, cuatro gates (OFFLINE_GO, PROVIDER_EVAL_GO, DEMO_DEPLOY_GO, COMMERCIAL_GO), smoke y pruebas de cero efectos comerciales. Nunca poner secrets, phone o dump de compose. Si acceso al destino/creds/canales no existen, no inventarlos ni sustituir por un target productivo; dejar el comando preciso que falta y el estado `BLOCKED_ACCESS`, no declarar deployment completado.

**Fuera de alcance:** merge a main, producción comercial, migraciones, reconfiguraciones masivas, pagos/órdenes/citas reales, envío arbitrario a teléfonos de clientes y cambios no relacionados.
