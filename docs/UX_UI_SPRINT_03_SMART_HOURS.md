# NexFlow · UX-03 — Horarios inteligentes por sede

**Estado:** PENDIENTE · ejecutar solo UX-03.  
**Commit base auditado:** `c9c9ba1dbf6e261612c0127f948390af2abc89f3`. Confirmar HEAD local y cambios pendientes antes de editar.  
**Objetivo:** configurar horarios semanales con pocas acciones, sin cambios persistidos involuntarios, sin nuevas dependencias y sin tocar otros módulos.

## Contexto mínimo / alcance

Leer `AGENTS.md`, **este ticket**, e inspeccionar únicamente los archivos necesarios:
- `frontend/src/features/business/components/HoursTab.tsx`
- `frontend/src/features/business/types/business.types.ts`
- `frontend/src/features/business/services/business.service.ts`
- `frontend/src/core/query/queryKeys.ts`, `frontend/src/core/query/queryPolicies.ts`, `frontend/src/core/query/useSessionMutation.ts`
- `frontend/src/core/store/useAuthStore.ts`
- Primitivas de `frontend/src/components/ui/` reutilizadas por Horarios.
- **Solo lectura de contrato** si hace falta: `backend/NexFlow.API/Controllers/Business/BusinessController.cs` (rutas `locations/{locationId}/hours`), `backend/NexFlow.Infrastructure/Persistence/Firestore/FirestoreBusinessHoursRepository.cs`.

No leer el roadmap completo ni auditar toda la solución; usar `rg` dirigido. No empezar UX-04.

## Contrato real que debe preservarse

- `BusinessHoursDto = { dayOfWeek: number, openTime: string, closeTime: string, isClosed: boolean }`.
- Días: domingo `0`, lunes `1` … sábado `6`.
- `GET /api/business/locations/{locationId}/hours` devuelve registros persistidos (puede devolver `[]`).
- `PUT /api/business/locations/{locationId}/hours` recibe el arreglo semanal y responde `204` al guardar.
- El backend exige `HH:mm`, apertura anterior a cierre y días únicos; al guardar días cerrados normaliza ambas horas a `""`.
- Los horarios se guardan **por workspace y por sede** en Firestore; la lectura proviene de la API, no de Firestore directamente.
- **Caso crucial:** `[]` significa *sin registros de horario*; **7 registros cerrados** significa configuración persistida y deliberada. No confundirlos. Si hay **datos parciales**, conservar los registros existentes y completar solamente días ausentes con estado cerrado **en el borrador**, sin sobreescribir silenciosamente ningún día guardado.
- Las capacidades `BUSINESS_HOURS / READ` y `UPDATE` controlan lectura y edición. El backend continúa siendo la autoridad.

## UX requerida

### A. Inicio inteligente, sin escrituras automáticas

1. **Solo cuando GET retorne `[]` con éxito para una sede**, mostrar una propuesta **NO guardada**: lunes a sábado abiertos `08:00–20:00`, domingo cerrado; explicar que es una sugerencia editable. Ofrecer opción de abrir también el domingo.
2. No crear ese borrador si GET falla, no hay permisos o no se ha seleccionado una sede concreta; distinguir carga, error, sede sin seleccionar, ausencia de configuración y horarios existentes.
3. Si GET devuelve una semana guardada, usar exactamente sus valores sin aplicar presets ni alterar días cerrados. Para días faltantes en configuración parcial, completar con filas cerradas y avisar discretamente que se puede completar la semana al guardar.
4. Nunca realizar PUT por montar un componente, cambiar pestaña o seleccionar una sede. **Solo guardar tras acción explícita del usuario.**

### B. Edición rápida de toda la semana

1. Tabla/tarjetas legibles de lunes a domingo con columnas **Día / Abierto o cerrado / Apertura / Cierre**. Reducir campos innecesarios y usar etiquetas en español (preferible control "Abierto" a checkbox "Cerrado" ambiguo).
2. Horas preestablecidas del borrador inicial: `08:00` y `20:00`, formato `HH:mm`, modificables individualmente.
3. Controles de aplicación masiva: **Toda la semana**, **Lunes a sábado**, **Solo días seleccionados**. Elegir apertura/cierre una sola vez y aplicar al conjunto escogido. La acción masiva modifica **solo el borrador**, nunca persiste automáticamente.
4. Permitir editar un día específico, marcar cerrado y volver a abrir sin perder accidentalmente las horas que el usuario estuviera editando; al persistir, respetar la normalización actual de días cerrados. Si un día guardado como cerrado se vuelve a abrir, ofrecer un horario editable válido, no campos vacíos que obliguen a repetir siete acciones.
5. Mostrar número de días abiertos y una vista resumen clara antes de guardar, con aviso de cambios sin guardar.
6. Ofrecer **Descartar cambios** y **Guardar horarios**. Descartar debe recuperar exactamente lo obtenido del servidor o el preset no persistido que correspondía a sede no configurada. No confundir "restablecer borrador" con borrar horario remoto.
7. Guardar **una sola vez** la semana completa; bloquear doble envío y reflejar éxito únicamente después de la respuesta correcta de la API. Ante fallo mantener el borrador editable y mostrar el error real con opción de reintentar.

### C. Seguridad, sedes, caché y concurrencia de UI

1. El estado de borrador debe estar aislado por **workspace, sede y sesión**, sin contaminación al cambiar de negocio/cuenta/sede. No mostrar ni guardar un borrador de otra sede.
2. Si cambia la sede con cambios sin guardar, evitar pérdida silenciosa: confirmar descarte o impedir cambio hasta resolverlo, **sin alterar globalmente el selector** si no es necesario. Como mínimo, preservar los borradores separados por sede dentro de la sesión y avisar de que quedan sin guardar; elegir la estrategia menos invasiva y probarla.
3. Nunca enviar `locationId='all'` ni ejecutar la consulta de horarios en ese estado. Mantener las salvaguardas de `useSessionMutation` y los permisos reales.
4. Guardar exactamente 7 objetos con `dayOfWeek` único, orden lógico consistente y horas válidas para días abiertos. Los días cerrados no deben propagar horarios contradictorios al API.
5. Tras respuesta exitosa, invalidar `queryKeys.hours.byLocation(workspaceId, locationId)` y `queryKeys.reservations.availabilityByLocation(workspaceId, locationId)`; evitar invalidar workspaces/sedes ajenas o hacer refetch redundantes.
6. Evitar que un GET lento de una sede anterior reemplace el borrador visible de la sede actual. Considerar los datos en caché como servidor y el borrador como UI local, sin duplicación innecesaria.

### D. Diseño, accesibilidad y responsive

- Usar tokens visuales NexFlow y componentes existentes (`FormField`, `Button`, alertas, etc.); fondo, espacios y jerarquía consistentes. No rediseñar los tabs generales: eso pertenece a UX-05.
- Buen aspecto en **360, 768 y 1280 px**, sin desplazamiento horizontal de la página ni controles diminutos.
- Etiquetas explícitas de día, apertura y cierre; accesibilidad por teclado, foco visible y estados no dependientes solo del color.
- Mostrar carga, error, vacío, solo lectura y estado de guardado sin textos técnicos.

## Pruebas dirigidas (proporcionales al cambio)

- GET `[]` => propuesta no guardada lunes–sábado `08:00–20:00`; **0 PUT** hasta que se pulsa Guardar.
- GET con 7 días cerrados => todos continúan cerrados; ningún preset automático.
- GET parcial => no se pierde ningún horario registrado; faltantes tratados de forma explícita.
- Acción masiva todos los días / lunes–sábado / selección personalizada; cambios individuales conservados donde no fueron seleccionados.
- Cambiar un día cerrado a abierto obtiene horas válidas; cerrar no crea horario inválido al guardar.
- Validación de `HH:mm`, apertura < cierre, días únicos y 7 días en el payload final.
- Sede `all`, sin permisos, GET fallido, guardado 4xx/5xx y doble clic: sin escrituras inapropiadas y con mensajes correctos.
- Cambio de sede/workspace/cuenta durante carga o con borrador sin guardar: sin fuga de datos ni escrituras al destino incorrecto.
- Guardado exitoso invalida horas y disponibilidad exclusivamente de la sede correspondiente; borrador queda reconciliado con datos guardados.
- Accesibilidad y responsive: 360/768/1280, controles etiquetados, teclado, foco y estados comprensibles.

Ejecutar `npm run build`, `npm run lint` y pruebas frontend **dirigidas** existentes o nuevas. Reutilizar la infraestructura de pruebas ya incorporada; no instalar paquetes nuevos para probar.

## Fuera de alcance y condición de parada

- No modificar backend, esquema, Firestore de producción, WhatsApp/Evolution, reservas, categorías, navegación, tabs generales, ni diseño global.
- No modificar `AGENTS.md`, dependencias, configuración, secretos o archivos de despliegue.
- No ejecutar `git commit`, `git push`, despliegue ni migraciones.
- Si el contrato actual impide diferenciar *sin configurar* de *configurado cerrado*, comprobar primero el comportamiento de GET; si existe ambigüedad real que requiera backend, **detenerse y reportar**, sin inventar un estado.
- Al terminar, inspeccionar solo el diff relacionado y entregar informe conciso: archivos, criterios OK/PENDIENTE, build/lint/tests realmente ejecutados, y riesgos. **Detenerse antes de UX-04.**
