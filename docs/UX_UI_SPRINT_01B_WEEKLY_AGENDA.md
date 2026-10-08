# NexFlow · UX-01B — Agenda semanal React

**Estado:** PENDIENTE · Ejecutar solo este sprint.
**Base esperada:** `dbaf2182c54841bddf81e0904a42af32483ee0db`; verificar primero el HEAD local y conservar cambios existentes sin sobrescribirlos.
**Objetivo:** convertir la pantalla de reservas de búsqueda por día a agenda semanal realmente utilizable, sin alterar backend ni datos persistidos.

## Alcance limitado

Leer `AGENTS.md`, este ticket y solo los componentes/herramientas necesarios de:
- `frontend/src/features/reservations/{pages,components,services,types}/`
- `frontend/src/core/query/queryKeys.ts`, `frontend/src/core/utils/dateTime.ts`
- `frontend/src/core/store/useAuthStore.ts`, `frontend/src/features/business/services/business.service.ts`
- Componentes UI compartidos realmente usados; consultar otros archivos solo mediante búsqueda dirigida (`rg`).

No leer todo el roadmap ni comenzar UX-02.

## Contrato de API YA IMPLEMENTADO en UX-01A

`GET /api/reservations?locationId={id|all}&from=YYYY-MM-DD&to=YYYY-MM-DD`
- Fechas civiles del negocio, intervalo **[from,to)**: lunes incluido hasta el lunes siguiente excluido, máximo 7 días.
- Respuesta: arreglo `ReservationDto` existente (`id`, `workspaceId`, `locationId`, `serviceId`, `customerName`, `customerIdentifier`, `dateTime` UTC, `status`).
- Estados existentes `Pending`, `Confirmed`, `Completed`, `Cancelled`. No inventar otros ni cambiar contratos.
- `all` permite lectura agregada; crear reservas sigue requiriendo una sede concreta.
- Backend actual en Oracle puede seguir siendo anterior: NO desplegar frontend nuevo antes de actualizar primero el backend y validar endpoint semanal con autenticación.

## Criterios funcionales

### A. Agenda semanal predeterminada
- Semana de **lunes a domingo**, basada en el día actual de la **zona horaria del negocio** (`profile.timeZone`, fallback `America/Lima` si corresponde a la lógica actual).
- Barra de navegación: **Anterior**, **Esta semana**, **Siguiente** y **Ir a fecha**; mostrar rango legible incluyendo mes/año. Permitir semanas históricas sin límite artificial.
- Vista principal en desktop: 7 columnas con fecha y día; filas **solo para horas de inicio que tengan alguna reserva** en esa semana. No crear filas de 00:00 a 23:59 vacías.
- Formatear `dateTime` a día y hora civiles en zona del negocio usando `Intl.DateTimeFormat`/`formatToParts` o una utilidad segura. Evitar `toISOString().slice(0,10)` en fechas locales y evitar parseos ambiguos `new Date('YYYY-MM-DD')` para aritmética de días.
- Si varias reservas caen en la misma hora/día, mostrar todas sin superposición; orden cronológico y por ID. Cada tarjeta muestra hora, cliente, servicio si se resolvió, estado y sede si el filtro es `all`.
- NO inventar duración/`endTime`: el DTO actual solo incluye inicio. No inferir duración histórica usando el servicio actual.
- Semana vacía: estado vacío útil, sin horas inventadas.

### B. Gestión e historial
- Resumen semanal por estados calculado exclusivamente sobre los resultados del rango cargado.
- Filtros de estado: Todas, Pendientes, Confirmadas, Completadas, Canceladas; no perder historial ni cambiar estados automáticamente cuando la fecha pasó.
- Alternador **Semana / Lista**: lista legible, con fecha, hora, cliente, servicio, estado, sede y acciones; búsqueda de cliente/servicio **solo dentro de la semana cargada**, etiquetada para no prometer búsqueda global.
- Respetar `RESERVATIONS` capacidades existentes para leer, crear, editar, completar y cancelar; no mostrar acciones imposibles para estados terminales. Mantener confirmaciones y mensajes de errores reales.
- `selectedLocationId='all'`: habilitar la **lectura** semanal agregada y mostrar sede real; no bloquear la lectura por no haber seleccionado una sede particular. Para **crear**, solicitar/seleccionar sede concreta (nunca enviar `all` al backend), sin alterar reglas de disponibilidad.
- Al pulsar un día en la agenda, abrir nueva reserva con esa fecha preseleccionada; conservar búsqueda/selección de slots reales desde `/reservations/availability` y la validación del backend. La acción desde semana pasada no debe crear automáticamente reservas vencidas.
- Crear, reagendar, completar y cancelar deben actualizar correctamente vista semanal, lista, resumen y disponibilidad mediante invalidación precisa de TanStack Query.

### C. Adaptación y rendimiento
- En escritorio mostrar 7 columnas; en móvil, cambiar a agenda de un día seleccionable con tarjetas verticales y navegación semanal, sin columnas aplastadas.
- Soportar semanas con muchas reservas mediante scroll legible, controles accesibles y sin sobreposición.
- Evitar siete GET diarios. Una sola query de servidor por semana+workspace+sede; claves TanStack Query específicas para el rango. Evitar estados compartidos entre workspaces y consultas excesivas.
- Estados distintos para cargando, error, vacío y datos obtenidos. Mantener información anterior con indicación de carga cuando cambie la semana, sin presentar datos de otra semana como actuales.
- Interacción por teclado, `aria-label` adecuados, foco visible y estado seleccionado no comunicado exclusivamente por color. Texto en español y diseño consistente con tokens NexFlow actuales. No rediseñar el resto del sistema.

## Implementación responsable

1. Inspeccionar flujo actual y proponer una composición pequeña (`WeekNavigation`, `WeeklyAgenda`, `ReservationList` reutilizado o adaptado, utilidades de fecha) sin sobreabstraer.
2. Adaptar `reservation.service.ts` a consulta semanal **manteniendo compatibilidad de llamadas existentes** donde haga falta.
3. Añadir/actualizar query keys con workspace+sede+lunes/rango; reutilizar `queryKeys.reservations.lists(workspaceId)` para invalidaciones.
4. Añadir fecha inicial opcional al formulario `CreateReservationModal` sin romper su comportamiento actual; jamás fabricar un slot.
5. Reusar componentes UI, estados, permisos y servicios actuales, sin introducir librerías de calendario externas.
6. Evitar cambios backend; si hay una imposibilidad contractual real, detenerse y describir el bloqueo, no editar el backend sin aprobación.

## Pruebas dirigidas

- Semana lunes-domingo y cruce de meses/años; avance y retroceso de semanas, salto a fecha.
- Zona `America/Lima` y al menos un escenario no UTC/DST sin desplazar día o hora.
- Varias reservas simultáneas, orden estable, semana sin reservas, días sin reserva sin filas.
- Todos los estados, historial pasado, filtros, búsqueda limitada a semana y vista de lista.
- `all` versus sede específica; lectura autorizada, creación exige sede; sin contaminación entre workspaces.
- Creación con día preseleccionado, slots reales y refresco después de mutaciones.
- Diseño/responsive (360 px, tablet y escritorio) y navegación por teclado.
- Verificaciones reales: `npm run build`, `npm run lint` y pruebas frontend **dirigidas** que ya soporte el repositorio. No atribuir pruebas E2E/manuales si no se ejecutaron.

## Prohibiciones y cierre

- NO cambiar `AGENTS.md`, backend, PostgreSQL, Firestore, WhatsApp, categorías, horarios, sidebar, despliegues o claves.
- NO instalar paquetes nuevos salvo bloqueo demostrado y aprobado.
- NO ejecutar `git commit`, `git push`, despliegues ni tocar Oracle/Firebase.
- NO correr pruebas globales ajenas sin motivo concreto; tests nuevos pequeños para lógica crítica.
- Revisar solo el diff propio. Informar: archivos tocados, criterios OK/PENDIENTE, comandos ejecutados y resultados, riesgos reales. Detenerse después de UX-01B para auditoría.
