# NexFlow — UX-06A/P1: desbloquear Reservas sin permiso de Perfil

**Base obligatoria:** `5379a0de8a1cae50debf8980097006d0151b30cb` (`main`, certificación UX-06A). Verificar HEAD, branch, `git status --short` y cambios locales; no sobrescribir trabajo pendiente.

**Hallazgo reproducido:** `ReservationsPage.tsx` exige `BUSINESS_PROFILE/READ` para cargar la zona IANA del negocio; por eso un miembro con `RESERVATIONS/READ` pero sin lectura de perfil no puede consultar su agenda. `GET /api/reservations` ya calcula rangos mediante zona del workspace sin requerir ese permiso. Este es un **P1 funcional** y bloquea UX-06B.

## Objetivo y contrato mínimo

1. Exponer un endpoint de metadatos exclusivamente para la agenda, por ejemplo `GET /api/reservations/context`, con respuesta exitosa **solo** `{ "timeZone": "America/Lima" }` (zona efectiva del workspace, no zona del navegador). El nombre exacto de ruta puede adaptarse a las convenciones existentes, pero documentarlo y probarlo.
2. Reutilizar `WorkspaceMember`, `IWorkspaceContext`, licencia `RESERVATIONS` y capacidad `RESERVATIONS/READ`; **no** exigir `BUSINESS_PROFILE/READ` ni otorgarlo implícitamente. Rechazar workspace inexistente, licencia ausente o capacidad denegada; no aceptar `workspaceId` desde parámetros cliente.
3. Leer internamente la configuración de zona existente vía la abstracción del perfil del workspace, sin devolver nombre comercial, RUC, contacto ni el perfil íntegro. No leer otros workspaces.
4. El endpoint debe devolver la **misma zona efectiva que la consulta semanal**: unificar el resolver de zona con el comportamiento actual del backend (`America/Lima` solo cuando la configuración está realmente ausente o inválida), sin duplicar reglas divergentes. La cadena enviada debe ser utilizable por `Intl.DateTimeFormat` del frontend (identificador IANA soportado); si hay incompatibilidad plataforma/ID, manejarla explícitamente, no inventar zona. Conservar tratamiento de cambios DST, medianoches ambiguas y días omitidos que ya tiene UX-01A.
5. Mantener **sin cambios** el contrato legacy de `GET /api/reservations?locationId=...&date=...` y el semanal `GET /api/reservations?locationId=...&from=...&to=...`, incluyendo JSON lista, permisos, estados y aislamiento. Sin migraciones.

## Frontend

6. `ReservationsPage` dejará de depender de `BUSINESS_PROFILE/READ` y de `getBusinessProfile` para la zona. Consultará el endpoint anterior **solo** con `RESERVATIONS/READ`, usando un `queryKey` específico y aislado por workspace, con `AbortSignal` y las políticas de caché existentes. Evitar consultas duplicadas si la información ya está en caché.
7. No ejecutar GET de la semana ni mostrar horas aproximadas hasta recibir y validar la zona efectiva. En error 401/403/503 o zona no soportada, mostrar error/reintento apropiado; **nunca** cambiar silenciosamente a zona local o `America/Lima` tras fallo de lectura.
8. Mantener fecha actual, navegación semanal, formato de horas, creación, reagendamiento, disponibilidad, filtros, lista, histórico y claves de caché coherentes con la zona validada. Las consultas deben quedar invalidadas o recalculadas si cambia la zona efectiva; considerar el `PUT /api/business/profile` para invalidar el nuevo metadato cuando corresponda, sin ampliar permisos y sin actualizar datos de otros workspaces.
9. Conservar aislamiento por sesión, workspace y permisos, y el reseteo de formularios al cambiar identidad. No leer Firestore desde frontend ni introducir estado redundante en Zustand.

## Pruebas dirigidas (obligatorias)

- Backend HTTP: usuario con `RESERVATIONS/READ` y sin `BUSINESS_PROFILE/READ` obtiene solo la zona efectiva del workspace; sin `RESERVATIONS/READ`, sin licencia o sin workspace devuelve 403. Otro workspace nunca filtra su zona. Zona válida, ausente e inválida; compatibilidad del endpoint diario/semanal, sin cambios en formatos.
- Frontend navegador: perfil denegado y reservas permitidas **sí cargan una semana correctamente**, sin consultar `/business/profile`; fechas y horas correctas. Sin permiso de reservas, ni consulta de zona ni agenda. Respuestas 403/503 o zonas inválidas no activan semana ni muestran horas inventadas; recuperación mediante reintento. Cambio de workspace/cuenta/permisos y zona sin datos obsoletos. Corregir la prueba de caracterización P1 incorporada en UX-06A; NO conservarla como prueba de bloqueo esperado.
- Ejercicios DST de zona distinta de Lima, y semana cruzando mes/año, tanto en backend como en render.
- Verificar que guardar cambios autorizados al perfil refresca la zona sin mezcla de tenants.
- Compilaciones frontend/backend y **solo** las pruebas relacionadas (API reservas, utils de fechas, navegador de reservas, efectos directos de caché). No repetir cinco suites completas salvo riesgo justificado.

## Límites y entrega

- Cambio coordinado mínimo de backend + frontend + tests pertinentes, sin alterar permisos globales ni perfiles comerciales. No tocar Evolution, WhatsApp, Firestore, categorías, horarios, n8n, PDF, otras APIs, paquetes ni migraciones.
- Leer `AGENTS.md` y **solo este ticket**; examinar por `rg` los archivos/callers afectados. Mantener trabajo en repositorio local. No hacer commit, push, deploy ni tocar Oracle/Firebase.
- Entregar diff resumido, contratos exactos, pruebas con resultados reales y estado del P1 (RESUELTO/PENDIENTE). Si surge incompatibilidad de zona IANA entre plataformas, documentarla y **detener esa parte** antes de tomar atajos.
- Detenerse tras esta corrección para una nueva auditoría. **No iniciar UX-06B automáticamente.**
