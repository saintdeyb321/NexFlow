# NexFlow · UX-02 — Categorías claras y modales consistentes

**Estado:** PENDIENTE · ejecutar únicamente UX-02.  
**Base auditada:** `02671460da8a0447c3d8cebe26f06e5fad89fdb2` (2026-10-08). Antes de empezar, comprobar HEAD y cambios locales.  
**Meta:** simplificar la administración de categorías y mejorar el diseño de sus diálogos con estándares de accesibilidad, sin cambiar modelos ni reglas de negocio.

## Preparación mínima

Leer `AGENTS.md`, **este ticket** y solo archivos involucrados:

- `frontend/src/features/catalog/components/CategoryManager.tsx`
- `frontend/src/features/catalog/pages/CatalogPage.tsx`
- `frontend/src/features/services/pages/ServicesPage.tsx`
- `frontend/src/features/catalog/services/catalog.service.ts`
- `frontend/src/features/shared/types/business-offering.types.ts`
- `frontend/src/components/ui/{Modal,Form,Button,ConfirmDialog,Feedback}.tsx`
- `frontend/src/core/query/queryKeys.ts` y permisos/query hooks relacionados, solo donde sea necesario.
- Revisar contratos relevantes de `backend/NexFlow.API/Controllers/Business/CatalogController.cs` **solo como lectura**.

No leer `UX_UI_ROADMAP.md` ni otros planes, salvo bloqueo concreto. No empezar UX-03.

## Invariantes del backend (no cambiar)

- `BusinessCategoryDto`: `id?`, `name`, `description?`, `scope: PRODUCT | SERVICE | SHARED`, `isActive`, `displayOrder`.
- `GET /api/catalog/categories?scope=PRODUCT|SERVICE` ya incluye categorías `SHARED` visibles para el módulo.
- `POST /api/catalog/categories`, `PUT /api/catalog/categories/{id}`, `DELETE /api/catalog/categories/{id}` conservan DTOs actuales.
- Una categoría existente **no puede cambiar de `scope`**; el servidor lo rechaza.
- `SHARED` requiere acceso de escritura a **ambos** módulos `CATALOG` y `SERVICES`. Conservar comprobación de capacidades; el servidor es la autoridad.
- La eliminación falla si la categoría contiene productos/servicios: mantener mensaje real, no ocultar el error ni borrar elementos.
- Mantener aislamiento por workspace y query keys con identidad de tenant.

## Criterios funcionales

### A. Jerga técnica fuera del formulario

1. Desde **Catálogo**, categoría nueva con `PRODUCT` predeterminado; desde **Servicios**, `SERVICE`.
2. No mostrar `scope`, `PRODUCT`, `SERVICE` ni `SHARED` en etiquetas o lista de categorías. Emplear texto claro: **«Para productos»**, **«Para servicios»**, **«Para productos y servicios»** cuando sea útil informar al usuario.
3. Opción de nueva categoría **«Usar también en productos y servicios»** únicamente cuando el usuario tenga `CREATE` en ambos módulos. Mapear a `SHARED` internamente. Si se desmarca, volver al alcance según la pantalla de origen.
4. Al editar, conservar exactamente el alcance persistido, aunque el usuario carezca de capacidad para escoger otros. No habilitar controles que impliquen cambiarlo ni enviar un scope distinto por error.
5. Listar categorías activas e inactivas con estados comprensibles; mostrar carga, vacío y fallo con recuperación. Evitar contenido ambiguo o controles deshabilitados sin explicación.

### B. Flujo de gestión sencillo

1. Separar visualmente el listado de la creación/edición, mediante una composición simple y sin cascadas innecesarias de modales.
2. Campos principales: **Nombre** (obligatorio; trim; validación visible), **Descripción** (opcional), **Activa** (interruptor/checkbox claramente etiquetado).
3. **Orden de visualización** en «Opciones avanzadas» con entero validado; ofrecer un valor predeterminado compatible con el backend, sin sobrescribir órdenes existentes ni introducir reordenación automática destructiva.
4. Edición debe cargar datos y permitir **Cancelar** sin mutar estado remoto; «Nueva categoría» reinicia borrador a alcance por contexto. Evitar borradores que persistan entre workspaces.
5. Deshabilitar doble envío y conservar datos del formulario al producirse un error; informar éxito únicamente tras respuesta exitosa.
6. Eliminación siempre confirmada con `ConfirmDialog`, preservando el manejo de errores del API. No borrar referencias/productos/servicios asociados.
7. Tras guardar o eliminar, invalidar correctamente las listas de `PRODUCT`, `SERVICE`, `SHARED` relevantes y artefactos afectados mediante las query keys actuales. No forzar recarga de todo el proyecto ni generar lecturas redundantes.

### C. Diseño y accesibilidad

1. Mejorar el diálogo de categorías: alineación centrada, cabecera con propósito, bloques y espaciados coherentes, pie de acciones legible, scroll de contenido en pantallas pequeñas. Usar tokens `nf-*` y componentes existentes; evitar visual saturado.
2. Evaluar cambios **mínimos y retrocompatibles** al componente genérico `Modal.tsx` **solo si son necesarios**. No rediseñar otros módulos ni tocar sus modales por estética.
3. Preservar portal, foco inicial y restauración, trap Tab, Escape, apilamiento de modales, scroll lock, bloqueo durante guardado, semántica `role=dialog` y `aria-labelledby`.
4. Mostrar errores asociados a campos (`FormField`) y estados accesibles. Objetivos táctiles adecuados, foco visible y buen contraste.
5. Comprobar en anchos 360, 768 y 1280 px, sin overflow horizontal ni botones fuera de pantalla.

## Pruebas proporcionales al sprint

- Creación desde Productos => `PRODUCT`; desde Servicios => `SERVICE`.
- `SHARED` solo con permisos combinados; edición conserva scope previo.
- Validación de nombre vacío/blancos, descripción, activo y orden; Cancelar no persiste.
- Doble guardado bloqueado; error mantiene formulario; éxito reinicia correctamente.
- Filtrado/listado/estado inactivo; actualización de caché (ambos contextos para `SHARED`).
- Eliminar con confirmación; API impide eliminación si hay referencias y el UI muestra error real.
- Accesibilidad básica: foco, teclado, diálogo anidado; responsive en 360/768/1280.
- Ejecutar `npm run build`, `npm run lint` y pruebas dirigidas del frontend existentes o nuevas; informar exactamente lo ejecutado. No ejecutar suites ajenas sin motivo concreto.

## Límites y entrega

- **No cambiar backend**, DTOs persistidos, Firestore, PostgreSQL, Evolución/WhatsApp, PDF, reservas, horarios, navegación o despliegues.
- **No tocar** `AGENTS.md`, dependencias, paquetes, lockfile, secretos ni archivos de Firebase, salvo un bloqueo demostrado y aprobado.
- No instalar librerías visuales pesadas; preferir Tailwind y componentes actuales.
- No ejecutar commits, push, deploy ni migraciones. No corregir defectos ajenos a este sprint.
- Revisar solo diff propio. Reportar archivos cambiados, criterios OK/PENDIENTE, comandos de build/lint/tests y riesgos reales. **Detenerse para auditoría** antes de UX-03.
