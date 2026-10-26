# NexFlow — Auditoría independiente de Sprints 3, 4 y 5

**Fecha:** 26 de octubre de 2026  
**Repositorio:** `https://github.com/saintdeyb321/NexFlow`  
**Código examinado:** `f7efb1c79948a13c4d675d06e9cfb1e24a33dc2a` → `be07f89c2146f7c4344effad849e1335c9614e86` (`main` al momento de la revisión).  
**Ámbito:** informe `docs/auditorias/IMPLEMENTACION_S3_S4_S5_MVP_2026-10-26.md`, diff publicado, cuatro componentes productivos y workflows de certificación. Esta revisión **no ejecutó .NET, Docker, PostgreSQL, Firestore, proveedores IA ni WhatsApp**.

## Dictamen

**GO para preparar e implementar Sprints 6–8; NO-GO para publicación de nueva API, piloto con datos no descartables y venta.** Las modificaciones observadas son acotadas y preservan motores transaccionales, controles de licencia y separación entre interpretación de IA y decisiones del backend. No es una prueba de ausencia de defectos.

## Hechos corroborados desde GitHub

- `main` apunta a `be07f89c2146f7c4344effad849e1335c9614e86`, un commit por delante de `f7efb1c…`. El commit contiene diez archivos: **cuatro archivos de Application**, cinco de pruebas y un informe. No se modificaron frontend, Dockerfile, migraciones ni infraestructura en este ciclo.
- `CommerceTurnResolver` incorpora una consulta informativa de capacidad de pedido con `ConversationIntent.Order`, `DialogueAct.OperationInformation`, una evidencia literal, ausencia de ambigüedad/directiva, autorización por módulos y lectura de productos activos. No persiste un pedido por esa consulta.
- `ChatFlow` da precedencia al perfil público cuando llega `General` con acto `Information`; las FAQ explícitas retienen ruta independiente. Este ajuste atiende la desviación detectada en S1/S2.
- `AiInterpreter` añade instrucciones de interpretación, no capacidades de escritura arbitrarias.
- `KnowledgeService.QueryAsync` añade el tópico como razón tipada de diagnóstico; no aparece una nueva dependencia de datos en esa modificación.
- Los workflows de GitHub `.github/workflows/rc3-release-checks.yml`, `rc3-reservation-integration.yml` y `concierge-redis-integration.yml` se activan por **`workflow_dispatch`**. Una consulta de ejecuciones asociadas a este nuevo SHA devolvió **cero runs** al momento de la revisión.
- En `CatalogGenerationService.GetContentAsync`, el objeto `BusinessData` incluye `profile.TaxId` en el payload de generación. Esto **no demuestra por sí solo** que el campo aparezca impreso en el PDF; requiere política y verificación del template/documento.
- El `backend/Dockerfile` actual copia artefactos API y migrator con `--chown=$APP_UID:$APP_UID`; el runtime conserva USER no-root. Falta una construcción y un smoke real Linux/ARM64 de **este SHA**, no de imágenes antiguas.

## Evidencia únicamente reportada por Codex

- S3: reproducción 59 (55 PASS, cuatro fallos iniciales), checkpoint **1528/1528**.
- S4: diagnóstico 2 (uno PASS, uno inicial rojo), checkpoint **1532/1532**.
- S5: contrato vigente **1538/1538**; históricos **118 PASS + 12 incompatibilidades esperadas** sobre 130, sin omisiones.
- Compilación .NET local con 0 errores y 0 advertencias según informe. Los artefactos TRX y logs citados viven en `backend/NexFlow.Tests/TestResults/mvp-s3-s4-s5/` local, ignorados por Git; **no fueron leídos ni reproducidos** durante esta revisión.
- La suite local usa intérpretes encolados/fakes e InMemoryFirestore en varios casos. No acredita persistencia PostgreSQL y Firestore Emulator reales, costo y precisión Groq/Gemini, ni entrega Evolution a WhatsApp.

## Riesgos y observaciones

| ID | Importancia | Observación | Acción de cierre |
|---|---|---|---|
| R6-01 | P1 gate release | Cero CI runs verificados para `be07f89c…`; los workflows son manuales | Ejecutar los tres workflows sobre el **mismo SHA**, conservar Run ID, intento, identidad y TRX; no cambiar umbrales para obtener verde |
| R6-02 | P1 gate release | Smoke Linux/ARM64 del Dockerfile corregido no ejecutado | Verificar final/migrator sin arrancar migrator, UID 1654, lectura de appsettings/binarios con contexto restrictivo, arquitectura y codecs PNG/JPEG/WebP |
| R7-01 | P1 comercial | PostgreSQL respaldado no equivale a recuperación Firestore | Aprobar estrategia soportada y probar restauración aislada o limitar piloto a datos 100% prescindibles; ventas bloqueadas sin recuperación suficiente |
| R7-02 | P2 privacidad | `TaxId` incluido en payload de generación; publicación visual desconocida | Decisión explícita del propietario, minimización acordada y comprobación de PDF real; no tocar hash/callback por intuición |
| R7-03 | P2 coherencia | Cinco servicios PDF/cuatro reservables explicados en fixtures, no en workspace real | Completar Q03/Q04 con proyección sanitizada y reconciliar razón por ítem/alcance |
| R7-04 | P1 operación | Q01–Q10 sin ejecución del workspace real | Operador autorizado realiza consultas **estrictamente read-only** y entrega solo resultados agregados/seudonimizados |
| R8-01 | P1 piloto | Sin evaluación independiente de al menos 30 conversaciones inéditas ni proveedores/canal reales de la candidata | Medir semántica, continuidad, seguridad, operaciones, latencia y costo con límites aprobados y números de prueba |
| R8-02 | P1 despliegue | Oracle continúa con imagen histórica `306d7c8-perms1` según último resultado compartido, sin comprobación actual desde esta auditoría | Leer identidad activa, comparar compatibilidad de rollback, preparar admisión/colas y cambiar **solo `api`** después del GO |

### Observaciones sobre los runbooks

1. No aceptar una aserción de que `/app/appsettings.json` debe tener **exactamente** modo 600 en todas las publicaciones. Aceptar la propiedad fundamental: usuario efectivo no root (UID 1654) y lectura/recorrido garantizados **aun bajo un escenario de publicación restrictivo**. Registrar modos reales sin inventarlos; usar un test aislado específico para 600/700.
2. Los ejemplos Q02/Q08/Q09 del informe son **propuestas de consulta**, no SQL productivo validado en servidor. Antes de ejecutarlos, contrastar nombres de campos, enums y licencias contra esquema realmente desplegado; usar parámetros, permisos de lectura y límites. Una consulta GET que reconcilia artefactos puede escribir; no usarla como prueba estrictamente read-only.
3. `Sent` no equivale a `Delivered` o `Read`. `UnknownDelivery` no autoriza reenvío. No reprocesar DeadLetter/Outbox históricos como parte de un smoke.
4. No asumir compatible un rollback a imágenes anteriores: la candidata cambia semántica del contexto y el release vulnerable previo `0da524…` nunca es opción segura. Ante bloqueo posterior al corte, detener admisión/piloto y corregir hacia adelante hasta confirmar compatibilidad.

## Decisión de ingeniería

No iniciar otros tres sprints de funcionalidades nuevas. Continuar con **S6: release reproducible/CI y ARM64; S7: integridad de datos, PDF y recuperación; S8: certificación independiente del piloto y runbook de despliegue**. Ejecutar en secuencia con checkpoints, distinguir lo que Codex puede validar localmente de los gates remotos operados por el propietario. Ningún gate externo pendiente se convierte en PASS por escribir un runbook.

**Estado:** GO para trabajo local acotado. NO-GO para actualizar Oracle, activar negocio real o comercializar hasta cierre externo documentado.
