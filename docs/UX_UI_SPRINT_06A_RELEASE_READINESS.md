# NexFlow · UX-06A — Certificación local y preparación del release

**Estado:** pendiente. **Base auditada estáticamente:** `a28ff465b3f885924ffe1de5b955c9f07e54add8` (UX-05).  
**Objetivo:** verificar de extremo a extremo los sprints UX-01A, UX-01B, UX-02, UX-03, UX-04 y UX-05; resolver solo defectos demostrados que impidan su integración; emitir decisión documentada **GO / NO-GO** antes de cualquier despliegue.

## 0. Contrato de trabajo

1. Trabaja en `C:\Proyectos\NexFlow`. Verifica branch, HEAD, `git status --short` y diferencias locales. **No sobrescribas cambios ajenos.**
2. Lee `AGENTS.md` y **solo este ticket**. A diferencia de un sprint normal, esta fase requiere comprobar integraciones entre reservas, perfil, horarios, categorías, navegación y WhatsApp. Usa búsquedas dirigidas y archivos relacionados; evita inspecciones exhaustivas de motores no afectados.
3. No introduzcas funcionalidades ni un rediseño. No instales dependencias, no edites secretos/configuración, no hagas commit, push, deploy, migraciones ni toques producción.
4. Distingue: *prueba realmente ejecutada*, *revisión estática*, *prueba bloqueada* y *prueba pendiente*. No escribas “OK” por existir un test.
5. Si encuentras P0/P1 (pérdida de datos, fuga multi-tenant, permisos, incompatibilidad API, mutación silenciosa o error reproducible de flujo principal), detén la preparación de despliegue, reproduce y corrige **solo** el defecto concreto mediante el cambio mínimo con prueba de regresión; informa antes de ampliar alcance.

## 1. Gate de código y contratos

- Confirmar que la UI semanal usa `GET /api/reservations?locationId=<id|all>&from=YYYY-MM-DD&to=YYYY-MM-DD`, con **from inclusivo / to exclusivo** y máximo de 7 días. La API diaria `date` debe continuar siendo compatible.
- Comprobar coincidencia exacta frontend/backend para DTOs, estados, ruta, permisos y query keys. Usar API fuente de verdad, no inventar campos.
- Comprobar operaciones de crear, editar/reagendar, completar y cancelar reservas e invalidación de la semana afectada. Verificar límites de mes/año, lunes-domingo, `America/Lima`, y filas de hora dinámicas.
- **Hallazgo previo obligatorio:** reproducir perfil `RESERVATIONS/READ` concedido + `BUSINESS_PROFILE/READ` denegado. El usuario debe poder gestionar reservas según autorización de su módulo, sin recibir datos de Perfil sin permiso ni presentar horas incorrectas. Si la arquitectura impide resolver la zona horaria correctamente, documentar propuesta segura y marcar NO-GO en ese perfil, en lugar de asumir zona horaria por conveniencia.
- Categorías: `PRODUCT/SERVICE/SHARED`, permisos dobles para compartidas, inmutabilidad de scope, listas e invalidación separadas por workspace.
- Horarios: diferencia entre GET `[]` y 7 días cerrados; propuesta 08:00–20:00 sin PUT automático; borradores por sede/sesión; 7 días válidos y normalizados al guardar.
- Identidad: `commercialName` desde perfil con autorización, fallback `Por definir`, ausencia de fuga de caché al cambiar de usuario/workspace y sin interferir con SuperAdmin.
- Pestañas de configuración: permisos dinámicos, teclado, ARIA, móvil, no mutaciones al navegar.
- WhatsApp: no generar QR ni desconectar automáticamente; si POST `/api/business/whatsapp/disconnect` devuelve 503, no afirmar desconexión ni permitir otro QR sin confirmación. **El problema de reconciliación Evolution/API sigue pendiente; no cambiar el protocolo en UX-06A.** Registrarlo como riesgo de release y recomendar ticket técnico separado.

## 2. Pruebas locales reproducibles — sin acceso a producción

### Frontend

- Desde `frontend/`: inspeccionar `node --version`, `npm --version`; usar instalación existente compatible con lockfile. Si faltan paquetes, `npm ci` una vez sin actualizar versiones.
- Ejecutar `npm run lint`, `npm run build`.
- Ejecutar las pruebas TypeScript de los módulos modificados (Node con soporte de TS según entorno) y pruebas de navegador existentes para reservas, categorías, horarios, navegación y configuración, preferiblemente con fixtures locales. No simular éxito si Chrome/Edge no está disponible.
- Probar anchos 360, 768 y 1280 px; foco, teclado, overflow, estados loading/empty/error, tabs, modales, listas, QR y guardado.
- Revisar que el build no contenga secretos ni URLs de desarrollo equivocadas; **no imprimir valores sensibles de `.env`**.

### Backend

- Desde la raíz: `dotnet build backend/backend.slnx --no-restore` si los paquetes ya están restaurados; si no, restaurar una sola vez en entorno local sin Docker.
- Ejecutar tests relevantes de `backend/NexFlow.Tests` para consulta de reservas, fechas, workspace, permisos y persistencia. Documentar exactamente el filtro/comando usado.
- Si una prueba usa SQLite en memoria, indicar explícitamente que **NO valida PostgreSQL de producción**. Las verificaciones con PostgreSQL real pertenecen a UX-06B y requieren autorización y backup.

### Revisión cruzada

- Revisar cambios desde el último baseline en los módulos afectados, interfaces de servicios, claves de Query y rutas; registrar incompatibilidades comprobadas.
- Inspeccionar solo el diff generado durante UX-06A. Conservar frontend y backend desplegables por separado.
- Evaluar los archivos generados (por ejemplo `.firebase/hosting*.cache`) y proponer limpieza si están versionados; no eliminarlos en una operación fuera de alcance.

## 3. Clasificación del riesgo y decisión

- **P0**: fuga de datos, destrucción, seguridad crítica -> NO-GO.
- **P1**: flujo principal roto, contratos incompatibles, acciones persistidas equivocadamente, errores temporales no recuperables -> NO-GO para la parte afectada.
- **P2**: fricción de UX o incidencia recuperable con mitigación explícita -> decisión documentada; nunca ocultar.
- **P3**: estética o mejora futura -> backlog.
- No dar GO si `build`/`lint` fallan o hay pruebas críticas fallidas. Si un test no se pudo ejecutar, marcarlo PENDIENTE con su impacto.

## 4. Entregable y parada

Crear **solo un informe conciso** en `docs/UX_UI_RELEASE_READINESS_REPORT.md` con:

1. HEAD, branch, fecha y entorno de verificación.
2. Matriz UX-01A a UX-05: criterio, resultado, evidencia (archivo/test), prioridad si falla.
3. Comandos exactos + resultados (PASS/FAIL/BLOCKED), duración solo si se midió.
4. Defectos comprobados corregidos, diff puntual y pruebas añadidas; bloqueos sin corregir.
5. Lista de cambios que **requieren backend** vs **frontend**, migraciones si realmente existen, límites de validación de PostgreSQL real, datos/historial, feature compatibility.
6. Riesgo conocido de desconexión WhatsApp 503 y hallazgo de permisos de zona horaria en Reservas.
7. **Decisión GO / NO-GO** motivada para avanzar a UX-06B, más pasos de rollback propuestos (sin ejecutarlos).

**Detente después del informe. UX-06B será un despliegue controlado, por separado y con confirmación del usuario: backup/verificación servidor Oracle, compilar imagen ARM64 desde commit exacto, actualizar API, smoke test autenticado de reservas semanales/health, y solo después build/publicación de Firebase Hosting con rollback. No ejecutes UX-06B ahora.**
