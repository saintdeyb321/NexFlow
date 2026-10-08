# NexFlow — Roadmap UX/UI 2.0

**Estado:** planificación aprobable, sin implementación ni despliegue  
**Línea base examinada:** `main` en `2a09def64691094126d1807d0cc35eee21088ed7` (2026-10-08)  
**Fuente de verdad:** código local actualizado y `AGENTS.md`; verificar diferencias con la línea base antes de ejecutar.  
**Objetivo:** reducir pasos, errores y carga cognitiva en las operaciones diarias de pequeños negocios, sin debilitar el SaaS multi-tenant.

## Forma de trabajo — mínimo contexto, máxima calidad

1. `AGENTS.md` define invariantes globales. No duplicarlo en los tickets.
2. Este roadmap orienta la planificación **humana**: Codex no debe leerlo en cada sprint.
3. Crear **un archivo de ticket para la fase activa** con criterios medibles, archivos relevantes y pruebas. Codex lee solo `AGENTS.md`, ese ticket y los archivos de código imprescindibles.
4. **Un sprint, un diff, una auditoría.** No continuar al siguiente sin aprobación del usuario.
5. Cada cambio debe dejar contratos existentes funcionales y con pruebas. No ampliar alcance para arreglos estéticos o refactors incidentales.
6. Primero implementar y probar localmente; después revisión del diff/commit; desplegar backend antes de frontend si hay cambios de API.
7. No ejecutar acciones sobre Oracle, PostgreSQL/Firestore de producción, Firebase Hosting, Git remoto ni Evolution API sin autorización expresa. No publicar datos de clientes ni credenciales en pruebas/logs.

## Principios de producto

- **Seguridad y negocio:** autorización del backend, aislamiento por `WorkspaceId`, licencias/capacidades, reglas de reservas y concurrencia intactas.
- **Idioma del usuario:** usar etiquetas comprensibles; esconder nombres internos como `SHARED`/`scope` sin cambiar su semántica persistida.
- **Menos esfuerzo:** valores iniciales editables, operaciones masivas voluntarias, filtros útiles; nunca guardar datos de configuración automáticamente por mostrar un preset.
- **Estados honestos:** distinguir error, operación pendiente, resultado confirmado y ausencia de datos. Nunca deducir éxito por un HTTP 503.
- **Información histórica:** mostrar reservas completadas/canceladas/pasadas sin cambiar de estado ni borrarlas.
- **Performance:** una consulta semanal acotada, caché con identidad del workspace, invalidación específica, evitar lecturas Firestore innecesarias.
- **Accesibilidad:** contraste, foco visible, keyboard, `aria`, tamaño táctil, responsive y `prefers-reduced-motion` con referencia WCAG 2.2 AA.
- **Diseño:** preservar identidad índigo/cian de NexFlow; usar tokens existentes; coherencia > decoración.

## Sprints independientes

### UX-01A — API de reservas por rango (primer ticket)

**Problema:** el endpoint `GET /api/reservations` exige `locationId` y `date`, y la página solo consulta un día.  
**Meta:** permitir una consulta semanal de una sola llamada sin romper clientes actuales.  
**Entregable:** contrato retrocompatible `from`/`to` (`[from,to)`, fechas locales de negocio), validación de rango máximo 7 días, filtro de sede, aislamiento multi-tenant, orden estable, estados históricos incluidos y tests de zona horaria.  
**No incluye:** rediseñar React, cambiar reglas de disponibilidad ni ejecutar migraciones.  
**Ticket detallado:** `docs/UX_UI_SPRINT_01A_RESERVATIONS_API.md`.

### UX-01B — Agenda semanal funcional

**Meta:** vista de semana lunes-domingo por defecto, anterior/siguiente/hoy/salto a fecha, siete columnas desktop y vista diaria móvil; **solo filas de horas donde haya reservas**, múltiples tarjetas por celda sin solapamiento.  
**Complementos:** lista alternativa; filtros por estado/sede; historial; búsqueda local en el rango cargado; resumen semanal; detalles de cita; creación contextual usando disponibilidad real del backend.  
**Cuidado:** usar zona horaria del negocio para agrupar días y horas; no derivar duración histórica a partir de configuración actual del servicio. Mantener permisos, acciones de completar/cancelar/editar, invalidaciones TanStack Query y estado visible al navegar entre semanas.  
**Tests:** cambio mes/año, semanas históricas, citas simultáneas, estados completos, mobile, zonas horarias y accesibilidad.

### UX-02 — Categorías y modales

- Desde Catálogo: categoría `PRODUCT` por defecto; desde Servicios: `SERVICE`.
- Ofrecer "Disponible en productos y servicios" solo si los permisos permiten `SHARED`.
- Quitar jerga técnica visible sin eliminar campo o alcance del backend; `scope` existente no debe cambiar durante edición.
- Formularios progresivos: nombre, descripción opcional, activo, orden avanzado; errores junto al campo.
- Modal centrado, jerarquía visual y acciones coherentes; preservar focus trap, restauración de foco, Escape, scroll móvil y diálogos anidados.
- Tests de permisos, ediciones, categorías existentes y modales.

### UX-03 — Horarios fáciles de configurar

- En sedes realmente **sin horarios persistidos**, mostrar borrador propuesto `08:00–20:00`; marcarlo como **no guardado**.
- Presets: toda la semana, lunes a sábado, días seleccionados; aplicación masiva con una acción.
- No sobrescribir horarios existentes, ni guardar por defecto, ni mezclar borradores entre sedes.
- Mantener validaciones `HH:mm`, apertura menor que cierre, días únicos, guardado por sede y invalidación de disponibilidad.
- Estados diferenciados: sin configurar, guardado, cargando, error y cambios sin guardar.

### UX-04 — Identidad y navegación

- Mostrar `commercialName` del perfil cuando esté autorizado y disponible; si está vacío, texto exacto "Por definir"; conservar cuenta/usuario en sección secundaria.
- Al guardar perfil, invalidar y actualizar nombre de forma segura sin filtraciones entre sesiones/workspaces; mantener superadmin separado.
- Agrupar menú: Operación / Gestión / Administración; mostrar únicamente capacidades otorgadas.
- Mejorar scroll lateral, ocupación vertical, selector de sede, breadcrumbs, header y menú móvil.

### UX-05 — Configuración y sistema visual

- Tabs de Perfil, Sedes, Horarios, WhatsApp: mejor distribución, navegación teclado, ARIA y responsive.
- Unificar estilos mediante `src/index.css` y componentes UI existentes: tarjetas, modales, formularios, errores, vacíos, badges, acciones y loaders.
- WhatsApp UX: mostrar "desconexión pendiente" durante reconciliación incierta; **no** habilitar nuevo QR hasta confirmación. Registrar el HTTP 503 como incidencia separada; no modificar protocolo Evolution en una tarea visual.
- Validar 360/768/1024 px y escritorio, sin incorporar dependencias grandes salvo justificación concreta.

### UX-06 — Regresión final y preparación de producción

- Builds y tests de los proyectos tocados; revisar contratos API y aislamiento; controles de permisos; auditoría de diffs; pruebas visuales y por teclado.
- Comprobar que módulos PDF, FAQ, licencias, WhatsApp y servicios no hayan sufrido regresiones.
- Crear un checklist de despliegue sin ejecutar acciones: backend/Oracle (si aplica) **antes** que frontend/Firebase, verificación de endpoints y plan de reversión.

## Definición de terminado por sprint

- [ ] Todos los criterios del ticket activo verificados.
- [ ] Pruebas pertinentes añadidas o actualizadas y ejecutadas con resultado real.
- [ ] Build/lint aplicables aprobados; si no se pudieron ejecutar, indicar por qué.
- [ ] Diff revisado, sin cambios fuera de alcance ni secretos.
- [ ] Riesgos y compatibilidad documentados brevemente.
- [ ] Sin commit, push, migración ni despliegue no autorizados.
- [ ] Codex se detiene y entrega un informe corto para auditoría antes de seguir.

## Contexto técnico (no recopilar de nuevo en cada sprint)

- `frontend/src/features/reservations/pages/ReservationsPage.tsx`: actualmente maneja selección de una sola fecha.
- `frontend/src/features/reservations/services/reservation.service.ts`: consulta `GET /reservations?locationId=...&date=...`.
- `backend/NexFlow.API/Controllers/Business/ReservationsController.cs`: convierte día local en intervalo UTC.
- `backend/NexFlow.Infrastructure/Persistence/PostgreSQL/Repositories/ReservationRepository.cs`: consulta PostgreSQL por rango UTC; preservar `WorkspaceId`.
- `frontend/src/features/catalog/components/CategoryManager.tsx`: muestra scopes técnicos.
- `frontend/src/features/business/components/HoursTab.tsx`: inicializa faltantes como cerrados.
- `frontend/src/layouts/WorkspaceLayout.tsx`: usa el nombre del workspace, no el comercial.
- `frontend/src/components/ui/Modal.tsx`: ya implementa gestión de foco importante.

**No reutilizar sin revisión este documento como un gran prompt único.** Para cada ticket dar a Codex un pedido de 3–5 líneas que identifique explícitamente el archivo del sprint y la condición de parada.
