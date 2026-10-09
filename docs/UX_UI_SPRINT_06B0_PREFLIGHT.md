# NexFlow — UX-06B0: puerta de salida local antes de despliegue

**Referencia exacta:** `main` HEAD esperado `24f989d1c04c9359df29bb1aa215f8c85450d311` (UX-06A/P1 cierre). Verifica branch, HEAD, estado de Git y no sobrescribas cambios locales. Lee `AGENTS.md` y solo este ticket; el repositorio actual es fuente de verdad.

## Dictamen previo

La implementación P1 está en código: `GET /api/reservations/context` entrega únicamente zona horaria tras comprobar `RESERVATIONS/READ`; la agenda ya no requiere `BUSINESS_PROFILE/READ`. La consulta semanal opcionalmente envía `timeZone` como expectativa; discrepancia responde `409 Reservation.TimeZoneChanged` y provoca relectura. Hay tests añadidos, pero no CI en GitHub para este commit ni resultados de ejecución asociados. **No afirmar PASS sin ejecutar.**

## Objetivo único y defecto a verificar

1. Certificar el cierre de P1 con pruebas reales y corregir **solo si se reproduce** esta regresión de UX: `WorkspaceAgenda` devuelve `LoadingState` mientras `context.isFetching`, desmontando `ResolvedAgenda`, sus modales y cualquier borrador si el usuario cambia de pestaña y vuelve (por `refetchOnWindowFocus: 'always'`) o si se invalida el contexto. Reproducir con navegador: abrir Nueva reserva, escribir nombre/teléfono/fecha y cambiar foco de pestaña; constatar si se pierde el borrador al volver. Probar también Reagendar.
2. Si se confirma, implementar una corrección pequeña que preserve el borrador y el contexto de navegación en **revalidaciones de la misma identidad y zona**. Mientras la zona esté siendo revalidada, no mostrar horas potencialmente obsoletas ni permitir confirmar operaciones; manejar loading/error de forma accesible, sin perder datos ya introducidos. Si cambia la zona, invalidar/recalcular fechas y slots pertinentes, limpiar horarios seleccionados incompatibles y preservar solo campos seguros (p. ej., nombre/teléfono) según reglas existentes. Nunca enviar operaciones automáticamente.
3. Mantener aislamiento entre usuario/workspace/sesión y revocación de permisos: en logout, cambio de workspace o usuario se limpian borradores y cachés propios; no se muestra información anterior. Una respuesta tardía no debe reabrir modales ni habilitar controles.
4. Mantener sin cambios los contratos de reservas, autorización `RESERVATIONS/READ`, `409 Reservation.TimeZoneChanged`, errores 401/403/503, CORS, middleware y consultas diarias anteriores. No ampliar permisos ni inventar zona horaria.

## Pruebas dirigidas y evidencia

- Backend: `dotnet build backend/backend.slnx --no-restore -m:1 -p:UseSharedCompilation=false` y pruebas filtradas `ReservationsApiTests` y `ReservationsRepositoryTests`; si un comando falla, reportar causa antes de adaptarlo.
- Frontend (desde `frontend`): `npm run lint`, `npm run build`, `node --import ./tests/reservations-loader.mjs --test tests/reservations-agenda.test.ts` y `node --test --test-concurrency=1 tests/reservations-browser.test.mjs`; ampliar solamente por impacto demostrado.
- Comprobar P1: READ sin BUSINESS_PROFILE; sin READ no hay consultas; contexto y semana consistente en DST y cambio de año; 409 por cambio externo; recuperación; cambio de identidad; fallos 403/503 sin horas supuestas. Tests que afirman bloqueo antiguo deben haber sido sustituidos.
- Añadir un test de navegador que **falle antes de corregir** si existe la pérdida de borrador, y que pase después. No relajar aserciones ni introducir sleeps arbitrarios. Evitar consumir cuentas/servicios reales.
- Registrar comandos, resultados, conteos y advertencias con evidencia; `git diff --check` y diff acotado. Inspeccionar que no se introduzcan tokens o credenciales en artefactos.

## Preflight de despliegue (solo planificación)

- Crear `docs/UX_UI_06B0_PREFLIGHT_REPORT.md` con decisión `GO para preparar UX-06B1` o `NO-GO`, requisitos pendientes, orden de despliegue (API ARM64 Oracle primero; frontend Firebase después), contratos `/api/reservations/context` y semanal, prerrequisitos de backup, IDs/tags de rollback, salud Redis/Postgres/Firestore, smoke tests autenticados por workspace y verificación de que Evolution/WhatsApp no se altera.
- **No inspeccionar secretos**; no entrar a Oracle, no desplegar contenedores, no migrar BD, no subir Firebase Hosting, no hacer commit/push/reset, no cambiar infraestructura ni borrar datos. No modificar archivos ajenos salvo los imprescindibles para la regresión probada y este informe.
- Si la regresión resulta no reproducible, documentar evidencia y no tocar el componente.

**Entrega:** archivos cambiados, resultados reales, P1 RESUELTO/PENDIENTE, pérdida de borrador RESUELTA/NO REPRODUCIDA/PENDIENTE, y decisión GO/NO-GO para **preparación**, no producción. Detenerse y solicitar auditoría antes de UX-06B1.
