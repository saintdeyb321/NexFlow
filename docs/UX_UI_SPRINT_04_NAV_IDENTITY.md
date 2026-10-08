# NexFlow · UX-04 — Navegación e identidad comercial

**Estado:** PENDIENTE · ejecutar solo UX-04.  
**Base auditada:** `51d2f52b7ea6d9c47fd4b5e5870c0b3286e8e5ad`. Verificar HEAD y cambios locales antes de comenzar.  
**Objetivo:** que NexFlow identifique claramente al negocio en el menú y la cabecera, y tenga navegación lateral moderna, ordenada y usable en desktop y móvil, sin cambiar permisos ni flujos de negocio.

## 1. Lecturas mínimas

1. `AGENTS.md` y **este ticket**; NO leer `UX_UI_ROADMAP.md` ni planes de otros sprints.
2. Archivos de código necesarios:
   - `frontend/src/layouts/WorkspaceLayout.tsx`
   - `frontend/src/features/business/components/ProfileTab.tsx`
   - `frontend/src/features/business/services/business.service.ts`
   - `frontend/src/features/business/types/business.types.ts`
   - `frontend/src/core/query/{queryKeys,queryPolicies,queryPersistence}.ts`
   - `frontend/src/core/store/useAuthStore.ts`, `frontend/src/core/auth/permissions.ts`, `frontend/src/core/types/auth.types.ts`
   - `frontend/src/app/router/AppRouter.tsx` **solo para conocer rutas existentes**
   - Componentes UI y tokens CSS vinculados directamente al sidebar, cabecera y navegación.
3. Buscar (`rg`) usos relacionados, sin auditar toda la solución. No modificar backend, Firestore, configuración, dependencias ni módulos ajenos.

## 2. Identidad del negocio (fuente de verdad)

- Mostrar el valor `commercialName` de `GET /api/business/profile` como nombre destacado del negocio en el lateral y en la cabecera/breadcrumb del workspace.
- **Fallback exacto:** `Por definir` si el perfil autorizado se cargó correctamente y el nombre es vacío/blanco/no configurado. Para estado de carga mostrar un placeholder discreto, no asumir que está vacío.
- El nombre del workspace en `/me` NO sustituye al `commercialName`; son conceptos distintos. Conservar `me.workspace.id` como identidad de autorización, nunca usar el nombre comercial como ID ni para filtrar datos.
- Respetar `BUSINESS_PROFILE/READ`: si no hay permiso, NO llamar al endpoint ni mostrar información almacenada de otro usuario/workspace; ofrecer un fallback neutro (p. ej., `Por definir`) sin presentar datos sensibles. Los errores de carga deben tratarse con discreción y permitir recuperación cuando proceda, sin cambiar la información guardada.
- `isSuperAdmin`: preservar la presentación y rutas especiales de la consola global; no sustituir "Administración Global" por información de un cliente por accidente.
- Reutilizar `queryKeys.business.profile(workspaceId)` y `queryPolicies.stable`; evitar solicitudes redundantes (no duplicar consultas, no almacenar copias persistentes en Zustand).
- Al guardar desde `ProfileTab`, el nombre en el menú y cabecera debe cambiar inmediatamente **después de respuesta satisfactoria** del PUT, sin recarga de página. Usar actualización/refresco seguro de TanStack Query; no confundir actualización visual con persistencia exitosa. Si la mutación falla, conservar el nombre previo y el borrador editable.
- Evitar datos antiguos al cambiar de workspace, usuario, sesión, permisos o estado del query (incluida caché restaurada): claves e identidad deben coincidir con el workspace autorizado. Respetar `useSessionMutation` y las políticas de invalidación existentes.
- Mantener el nombre/apellidos/correo de quien inició sesión en el área secundaria de **Mi cuenta**; no reemplazar esos datos personales por el nombre del negocio.

## 3. Navegación ordenada y eficiente

- Reorganizar SOLO la representación de enlaces existentes, sin inventar rutas ni conceder permisos:
  - **Operación:** Dashboard (según condición existente), Mensajes, Reservas, Pedidos, Solicitudes.
  - **Gestión:** Servicios, Catálogo, Base de conocimiento (actual FAQ).
  - **Administración:** Negocio y, cuando corresponda, Consola SuperAdmin.
- Usar el `MODULE_REGISTRY`/metadata actual y `can(module,'READ')`, licencias y condiciones existentes. No renderizar grupos vacíos, enlaces desautorizados o que apunten a rutas inexistentes.
- Conservar la condición original de acceso del enlace Dashboard o investigar la condición de acceso real antes de modificarla; no ampliar este sprint hacia cambios de autorización del router.
- Estado activo claro (`aria-current="page"`), foco visible y navegación por teclado; íconos consistentes, texto legible, jerarquía y espaciado sin saturación. Evitar depender solo del color.
- Mejorar distribución vertical: identidad/nombre comercial y selector de sede legibles, listado con scroll independiente, cuenta y cierre de sesión visibles en alturas reducidas. Truncar nombres largos sin perder accesibilidad (título/etiqueta apropiados).
- Selector de sede conserva `all` y sedes autorizadas; no cambiar la semántica global de `selectedLocationId` ni hacer nuevas escrituras. Mantener el restablecimiento cuando la sede deja de existir.
- Mobile: drawer funcional a 360 y 768 px, cerrar al navegar, no crear dos consultas de perfil por renderización de la misma página, preservar manejo existente de foco/Escape/scroll-lock del componente `Modal`.
- Cabecera/breadcrumb debe reflejar el módulo actual mediante coincidencias de ruta correctas, especialmente `/`, `/superadmin` y las páginas con subrutas; evitar colisiones por prefijos.
- Respetar estilos/tokens visuales de NexFlow. No hacer un redesign de `SettingsPage` tabs o del sistema visual completo: pertenecen a UX-05.

## 4. Resiliencia y calidad

- Mantener el área de notificaciones, el botón cerrar sesión, el link a contenido principal y el acceso desde móvil.
- No mostrar el nombre de otro workspace durante una petición lenta o después de cambio de identidad. Evitar consultas de perfil si el workspace es nulo o no hay READ.
- No introducir librerías nuevas, nuevas rutas ni pantallas ficticias; extraer componentes pequeños únicamente si simplifican mantenimiento.
- Textos visibles en español, consistentes y comprensibles para negocios pequeños.

## 5. Pruebas proporcionales al sprint

- Nombre comercial cargado correctamente; faltante/espacios => `Por definir`; loading/error/403 sin filtraciones.
- Cambio del nombre en `ProfileTab` tras PUT exitoso, sin reload; error de PUT mantiene nombre anterior y borrador; cambio de workspace durante PUT no contamina caché.
- Cambio de workspace/usuario/logout y perfil cacheado/restaurado: nunca mostrar identidad comercial ajena.
- Permisos sin BUSINESS_PROFILE READ: cero llamadas a perfil; enlaces no autorizados invisibles.
- Superadmin: identidad global y acceso especial conservados.
- Todos los enlaces existentes, grupos sin enlaces vacíos, rutas activas correctas, selector de sede y logout.
- Teclado, foco, navegación drawer móvil, cierre al elegir enlace; responsive 360, 768, 1280; nombres largos y sidebar bajo.
- Reutilizar infraestructura de tests local existente; si añades pruebas de navegador, usar fixtures locales sin tokens ni red externa.
- Ejecutar `npm run build`, `npm run lint` y tests **dirigidos**; informar resultados verificables y lo que no se ejecutó. No correr suites globales ajenas sin motivo.

## 6. Restricciones y salida

- **Solo frontend**: `WorkspaceLayout`, integración con lectura del perfil y, si hace falta, ajuste mínimo de `ProfileTab`/hooks compartidos directamente involucrados. No cambiar esquema ni endpoints del backend.
- NO alterar horarios, categorías, reservas, Evolution/WhatsApp, PDF, SettingsPage tabs, CI ni despliegues.
- No editar `AGENTS.md`, secretos, configuración, dependencias ni Git; no hacer commit/push/despliegue.
- Verificar diff del propio sprint, declarar criterios OK/PENDIENTE y los comandos/resultados reales. Detenerse para auditoría antes de UX-05.
