# NexFlow · UX-05 — Configuración y sistema visual coherente

**Estado:** PENDIENTE · ejecutar solo UX-05.
**Commit base auditado estáticamente:** `158016e9ee55107cf0dc07c2d039c5ee059f47cf` (2026-10-08).
**Objetivo:** que Configuración del negocio sea clara, visualmente consistente, bien alineada y accesible, sin cambiar reglas de negocio, APIs ni el funcionamiento de WhatsApp.

## 0. Inicio y alcance

1. Verifica HEAD, branch y estado de trabajo. Si hay cambios ajenos sin guardar, no los sobrescribas. Lee `AGENTS.md` y **este ticket**. No leas el roadmap general ni tickets anteriores.
2. Inspecciona de manera dirigida `frontend/src/features/business/pages/SettingsPage.tsx`, `frontend/src/features/business/components/{ProfileTab,LocationsTab,HoursTab,HoursWeekEditor,WhatsAppTab}.tsx`, `frontend/src/components/ui/{Button,Form,Modal,ConfirmDialog,Feedback,Layout}.tsx`, `frontend/src/index.css` y los tests existentes relacionados. Abre otros archivos solo si existe dependencia demostrable.
3. Mantén el sistema visual actual (índigo/cian/neutros, tokens `--nf-*`, clases `nf-*`, Tailwind). No instales librerías ni reescribas toda la aplicación. Realiza cambios compartidos solo si son retrocompatibles y justificables.

## A. Pestañas de Configuración: funcionales y accesibles

- Mantén las cuatro opciones autorizadas: **Perfil**, **Sedes**, **Horarios**, **WhatsApp**. No muestres pestañas sin la capacidad READ y no alteres entitlements. No inventes rutas.
- Construye una barra centrada o alineada deliberadamente con el contenido, con tamaño proporcionado, iconos coherentes y estado activo inequívoco. El usuario no debe ver una hilera de botones blancos indistinguibles.
- Implementa el patrón WAI-ARIA de tabs de manera coherente: `role=tablist`, un tab seleccionado, `aria-selected`, asociación válida con `role=tabpanel` y `aria-labelledby`, y **roving tabIndex** (o un patrón equivalente igualmente accesible).
- Soporta flechas izquierda/derecha, Home/End y entrada por teclado. Usa activación automática o manual, pero de forma consistente y documentada. Si un permiso cambia o el tab activo desaparece, selecciona uno permitido sin dejar controles inaccesibles.
- En 360px/768px, no aplastes las cuatro pestañas: scroll horizontal contenido y enfocable, o composición responsive equivalente, sin overflow horizontal del documento. Conservar foco visible.
- Mantén el contenido montado solo según los patrones existentes; no desencadenes mutaciones ni nuevas consultas innecesarias al cambiar de tab.

## B. Configuración: jerarquía, alineación y lectura fácil

- Mejorar `SettingsPage` y, con modificaciones mínimas, los contenedores visuales de Perfil, Sedes, Horarios y WhatsApp: ancho coherente, cabecera diferenciada, tarjetas sobrias, espaciado vertical constante, acciones ubicadas de manera predecible.
- Reutilizar `PageHeader`, `nf-panel`, `FormField`, `Button` y tokens existentes. No aplicar gradientes intensos, estilos arbitrarios ni sombras desproporcionadas. Debe sentirse como un SaaS empresarial moderno, no un collage de controles.
- Jerarquía de información: contexto y descripción breve, contenido principal, acciones primarias claras, estados de guardado/carga/error/solo lectura. No ocultes errores del backend ni confundas un estado vacío con un error.
- Revisar modales usados por Configuración solamente si se observa un problema reproducible de tamaño/centrado en móvil; conservar la trampa de foco, Escape, bloqueo al guardar y restauración de foco. No alterar el drawer del sidebar de UX-04.
- Estándares: WCAG 2.2 AA como objetivo, texto y contrastes comprobables, objetivos táctiles >=44px donde sea posible, enfoque por teclado, `prefers-reduced-motion`, etiquetas y estados que no dependan únicamente del color.

## C. WhatsApp: presentación sin cambiar protocolo

- Dar a `WhatsAppTab` una presentación clara: estado actual prominente, explicación breve de la acción posible, acciones principal/secundaria bien jerarquizadas, instrucciones del QR legibles, márgenes y ancho adecuados, diseño responsive.
- Conservar **exactamente** la semántica del contrato `ConnectionStatus`: `DISCONNECTED`, `CONNECTING`, `QR_AVAILABLE`, `QR_EXPIRED`, `CONNECTED`, `RECONNECTING`, `UNAVAILABLE`, `DISCONNECT_PENDING`.
- Nunca mostrar “desconectado” ni habilitar nuevo QR por un HTTP `503` o por una petición pendiente: solo por estado confirmado por el backend. Si la respuesta es ambigua, mostrar “No se pudo confirmar la desconexión; revisa el estado” en lugar de afirmar éxito o fracaso definitivo.
- Conservar permisos `CONVERSATIONS/READ` y `CONFIGURE`, `canPairWhatsApp`, vencimiento del QR, límite de polling, confirmación de logout, protecciones de doble clic y claves TanStack Query. No iniciar conexión al montar ni ejecutar logout adicional por cambios visuales.
- **Importante:** el problema observado anteriormente (POST `/api/business/whatsapp/disconnect` devuelve 503 aunque Evolution se desconecta después) requiere diagnóstico específico de backend/Evolution. NO modificar timeouts, endpoints, state machine, Evolution, reconexión automática o polling en este sprint. Documentar como issue para sprint de integración posterior. Solo mensajes UI seguros si se pueden cambiar sin alterar la lógica.

## D. Tokens / componentes compartidos: cambio mínimo

- Revisar `index.css` y unificar de forma limitada: color de pestañas, bordes, espaciados, contraste, estados activos/hover/focus, superficies de ajustes, formularios y mensajes; evitar reglas globales que transformen Reservas, Catálogo, sidebar u otros módulos.
- Preferir clases reutilizables solo si tienen al menos dos usos reales. No introducir diseño oscuro ni una nueva dependencia. Mantener `prefers-reduced-motion` y estilos responsive.
- Al modificar cualquier componente compartido, verificar con pruebas dirigidas que no se rompan modales, botones, inputs o las vistas anteriores.

## E. Pruebas proporcionales, no suite gigante

**Lógica e integración visual:** permisos y tab inicial; cambio de pestaña; permisos que cambian; teclas ArrowLeft/Right, Home/End, Enter/Espacio según patrón elegido; asociación aria; foco; overflow en 360/768/1280; loading/error/empty; campos de Perfil; Horarios sin PUT automático; Sedes conservadas; WhatsApp conectado/sin conexión/QR/cierre pendiente/error 503 sin falso éxito; prohibición de mutaciones y consultas extra por cambiar tabs.

**Ejecución:** `npm run build`, `npm run lint`, pruebas dirigidas de Settings/WhatsApp y regresiones de primitivas afectadas. Si hay navegador local disponible, pruebas de interacción/responsive con fixtures locales sin identidad real ni API de producción. Si no está disponible, dejarlo como PENDIENTE con motivo; no inventar resultados. Reutilizar infraestructura y evitar suites ajenas salvo riesgo concreto.

## F. No hacer

- NO modificar backend, PostgreSQL, Firestore, migraciones, contratos, permisos ni licencias; no crear datos de prueba reales.
- NO cambiar la lógica de Evolution, webhooks, creación/reconexión de instancias, reservas, catálogos, horarios persistidos, navegación de UX-04 ni generación PDF.
- NO cambiar `AGENTS.md`, paquetes, lockfiles, variables de entorno, CI o Firebase; no commits, push, deploy ni operaciones en Oracle.
- Si un hallazgo exige cambiar lógica de negocio, detener esa parte y reportar el bloqueo, sin expandir alcance.

## G. Entrega y parada

Entregar un informe breve: archivos modificados, criterios OK/PENDIENTE, decisiones de accesibilidad, capturas locales si están disponibles, comandos/resultados reales, potenciales regresiones y tareas pendientes (especialmente el 503 de desconexión). Inspeccionar diff propio. **Detenerse antes de UX-06 para auditoría.**
