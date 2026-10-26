# NexFlow Agent Runtime 4.0 — Auditoría independiente S6–S8

**Fecha:** 26/10/2026. **Repositorio:** https://github.com/saintdeyb321/NexFlow  
**HEAD remoto verificado:** `991a4ac27c4d233a6fc0cd5d81c720859752fc74`.  
**Último commit con cambios productivos:** `be07f89c2146f7c4344effad849e1335c9614e86`.  
**Producción reportada en la conversación:** `nexflow-api:306d7c8-perms1`, salud HTTPS, PostgreSQL, Redis y Firestore aprobadas *en aquel momento*. No se ha vuelto a inspeccionar Oracle en esta auditoría.

## 1. Fuentes revisadas y alcance

- `docs/auditorias/IMPLEMENTACION_S6_S7_S8_MVP_2026-10-26.md`.
- `docs/auditorias/MATRIZ_Q01_Q10_VALIDACION_MVP.md`.
- `docs/auditorias/PLAN_PILOTO_E2E_WHATSAPP_MVP.md`.
- `docs/auditorias/GO_LIVE_CHECKLIST_NEXFLOW_MVP.md`.
- Historial GitHub `be07f89c…` → `991a4ac…`: seis documentos Markdown agregados y **cero cambios a código productivo**, suites, Dockerfile o workflows.
- Workflows manuales `.github/workflows/rc3-release-checks.yml`, `rc3-reservation-integration.yml` y `concierge-redis-integration.yml`.
- Listado de GitHub Actions asociado a `991a4ac…`: **cero ejecuciones** al momento de consultar. Las ejecuciones exitosas recientes correspondían a `306d7c8…`; no certifican los commits nuevos.

Se trata de una auditoría **estática de documentos, código versionado e identidad remota**. No ejecuté las suites .NET, Docker, Oracle, Firestore, WhatsApp ni la evaluación de proveedor; sus resultados se atribuyen al informe del implementador.

## 2. Resultado de la auditoría

| Tema | Resultado | Estado |
|---|---|---|
| S6, contrato conversacional | Informado: 205 dirigidas, 1538/1538 vigentes, históricos 118/130 con las mismas 12 incompatibilidades, siete pruebas validador G8 | GO local reportado, pendiente de réplica en CI del SHA final |
| S7, catálogo/permisos y PDF | Informado: 297/297. Cinco publicados/cuatro reservables reproducido sintéticamente, sin forzar filtros | GO local reportado; incidencia del workspace real no demostrada |
| S8, contratos de piloto | Informado: 135/135; 39 Integration, 1 Redis y 2 Provider sólo descubiertas | GO documental; **cero integraciones/proveedores externos ejecutados en S8** |
| Código productivo | Sin cambios en S6–S8 | Ningún defecto nuevo justifica otra refactorización general |
| Dockerfile | `COPY --chown=$APP_UID:$APP_UID` presente para API y migrator, runtime no-root | Diseño favorable; **falta smoke ARM64 de imagen final** |
| CI | Tres workflows `workflow_dispatch` disponibles, sin run para `991a4ac…` | **NO-GO de release** |
| Firestore y restauración | Existe propuesta de copia lógica cifrada/consistente, no restauración probada | **NO-GO para datos no descartables** |
| PDF/TaxId | `CatalogGenerationService.GetContentAsync` incluye `TaxId` en payload; perfil público no lo divulga | Riesgo de publicación no resuelto; no se ha demostrado presencia visual en PDF |
| Naturalidad y costes | Matriz de 32 conversaciones y rúbrica propuesta; no corpus privado ni medidas reales | **NO-GO de piloto comercial** |

## 3. Observaciones correctivas sobre los runbooks

1. **Identidad:** el prompt de cierre debe referenciar `991a4ac…` como HEAD remoto encontrado, pero consultar HEAD local antes de actuar. No atribuirle automáticamente pruebas locales anteriores aunque el delta sea documental; congelar un SHA completo y correlacionar cada artifact y run.
2. **CI:** para liberar se necesitan tres workflow runs `success` del mismo SHA, con artifacts TRX/JUnit/identity completos, cantidades y hashes validados. No basta una pantalla verde ni resultados del `306d7c8…`.
3. **ARM64:** validar imagen real por **ID/digest y revisión**, UID efectivo 1654, lectura/recorrido de todos los publicados y codecs PNG/JPEG/WebP; un modo exacto `600` sólo se exige en variante de prueba restrictiva, no al publish ordinario.
4. **Q01–Q10:** las consultas descritas son *propuestas*, no scripts certificados contra Oracle. Confirmar previamente tablas, columnas, enums, filtros y permisos. Evitar lecturas de APIs con efectos laterales (`GetArtifactAsync`, normalizadores Outbound/Recovery). No afirmar que Q02 SQL reproduce por sí solo todo `EntitlementService`.
5. **PDF:** no confundir `SourceHash`/`CURRENT` con validación visual de bytes. Obtener decisión de privacidad antes de alterar la proyección del payload y sus hashes/callbacks. Un cambio exige nuevo SHA, CI y smoke.
6. **Firestore Spark:** el backup SQL previo no protege documentos Firestore ni PDF externos. Copia lógica debe ser tipada, completa, cifrada, coherente entre servicios o declarar limitaciones; probar restauración **en emulador aislado**. Nunca promover ese ensayo a producción.
7. **Canary:** la infraestructura declarada usa API única en host compartido. No suponer aislamiento por tenant o «modo mantenimiento» inexistente. Confirmar barrera real de admisión/drenaje; de otro modo STOP.
8. **Regresión histórica:** los 12 fallos congelados no son 12 PASS. Conservar inventario/hash/reason y prohibir exclusiones nuevas para forzar certificación.
9. **Rollback:** `0da524…` no es opción segura; `306d7c8-perms1` no es automáticamente compatible con nuevo estado conversacional. Demostrar compatibilidad o disponer de fix-forward y contención aprobados antes del corte.
10. **Pruebas proveedor/canal:** pruebas `ProviderIntegration` directas no validan el enrutador completo ni una evaluación ciega de 32 conversaciones. Medir operaciones reales y revisar por humanos independientes, con presupuesto y límites de llamadas impuestos antes de usar servicios.

## 4. Decisión y ruta de cierre

**GO** para una *última ejecución de cierre* dividida en tres gates secuenciales: **S9 Evidencia externa y build ARM64**, **S10 Datos/privacidad/recuperación**, **S11 E2E/ensayo y release**. Son gates ejecutables, no otros tres sprints puramente documentales. Si no hay permisos o infraestructura para un gate, su resultado es `BLOQUEADO` con solicitud operacional exacta, no `COMPLETADO`.

**NO-GO actual para despliegue y comercialización.** La sustitución de la única API productiva requiere la autorización humana final después de todos los gates, y no debe ejecutarse por un prompt genérico. Si Firestore no dispone de recuperación verificada, como máximo podrá autorizarse por separado un piloto estrictamente DEMO descartable sin clientes reales, siempre que cumpla los controles de seguridad y aislamiento.

Esta auditoría no demuestra un nuevo defecto P0/P1 y tampoco demuestra ausencia de defectos no cubiertos.
