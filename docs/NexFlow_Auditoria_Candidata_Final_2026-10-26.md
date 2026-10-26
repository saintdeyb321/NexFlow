# NexFlow — Auditoría independiente de candidata final

**Repositorio:** saintdeyb321/NexFlow. **GitHub main consultado:** `b1ae17fcc2f9e99a0399fdca5b865ee7ea6d2b5e`. **Estado:** GO para certificación externa, NO-GO para despliegue comercial.

## Cambios verificados

Comparación `991a4ac27c4d233a6fc0cd5d81c720859752fc74` → `b1ae17fcc2f9e99a0399fdca5b865ee7ea6d2b5e`: workflow nuevo `.github/workflows/agent-runtime-arm64-certification.yml`; modificación de seis líneas (+4/-2) en `frontend/tests/reservations-browser.test.mjs`; informe `docs/auditorias/CIERRE_MVP_GO_LIVE_EVIDENCIA.md` y dos documentos. Sin modificaciones al backend productivo.

Los resultados siguientes provienen del informe de Codex, **no fueron ejecutados por esta revisión**: G8 vigente 1538/1538, históricos 118 aprobados y doce incompatibilidades esperadas de 130, TypeScript 83/83, navegador 148/148, build y CodecSmoke Windows correctos. Se conserva la secuencia de tests rojos→verdes del fixture DST sin rebajar aserciones. Al inspeccionar GitHub Actions no había runs para el nuevo SHA.

## Riesgo prioritario ARM64

En el workflow, la extracción `build.get('buildx.build.provenance', {}).get('materials', [])` asume una ubicación del campo materials que puede no coincidir con la estructura real del registro SLSA emitido por Buildx. Según versión/procedencia puede aparecer como `predicate.materials` o `predicate.buildDefinition.resolvedDependencies`. **Riesgo, no fallo observado**, porque la ejecución aún no existe. Antes del dispatch, contrastar con ejemplos reales de metadata y tests de parser. Exigir SDK y ASP.NET base por digest, con rechazo explícito si no hay evidencia; no omitir validación para forzar PASS.

El workflow realiza `--load`: **la imagen no queda publicada ni conservada al terminar el runner**. Identificador local/digest de build no bastan para autorizar una nueva construcción distinta en Oracle. Se necesita procedimiento de custodia/promoción de la misma imagen probada por digest, con autorización aparte. Su alcance aislado, UID/GID 1654, permisos restrictivos, migrator sin ejecución y CodecSmoke PNG/JPEG/WebP es correcto como intención estática. Aún sin pruebas ARM64 reales.

## Oracle: Q01 ya comprobado por el propietario

Contenedor `nexflow_api` running, 0 reinicios; imagen `nexflow-api:306d7c8-perms1`; image ID `sha256:e63453197ee666cfb023ae07b33a95fdc37711cf914c0dd0b13a5c51fff0ce45`; linux/arm64, usuario 1654:1654; `/app` 755 root:1654, `/app/appsettings.json` 640 root:1654. Compose `nexflow`, servicio `api`, directorio `/opt/saas/nexflow`, 13 archivos, último `api-306d7c8-perms.override.yml`. **Q01 inventario de solo lectura: VERIFICADO.** No acredita digest de registro, readiness actual, compatibilidad de rollback ni autoriza cambios.

## Pendientes

1. Cuatro workflows manuales para un SHA único, artefactos completos y revisión de Run IDs/attempts (3 RC3 + ARM64).
2. Imagen idéntica a la ensayada, conservada y transferible por digest aprobado.
3. Q02–Q10: licencias, catálogo 5/4 del workspace, datos de negocio, PDF/TaxId, colas y persistencia, bajo acceso explícito de solo lectura.
4. Backup/restauración aislada Firestore y objetos para uso con datos no descartables.
5. Evaluación independiente con al menos 32 conversaciones; Groq/Gemini y WhatsApp reales dentro de presupuesto/consentimiento y con separación DEMO.
6. Plan de admisión/drenaje, contención/fix-forward y aprobación del corte solo de servicio `api`.

**Decisión:** continuar la única fase final S9–S11 basada en gates medibles, no nuevos sprints genéricos ni publicación improvisada.
