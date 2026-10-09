# NexFlow — Certificación local UX-06A

**Decisión: NO-GO para avanzar a UX-06B.** Las compilaciones y las suites locales finales pasan, pero reservas sigue bloqueado para un perfil autorizado en RESERVATIONS sin BUSINESS_PROFILE/READ. La prueba nueva confirma el bloqueo seguro; no certifica el criterio funcional como cumplido. No se desplegó ni se accedió a producción.

## Base y entorno

- Fecha: 8 de octubre de 2026, America/Lima. Repositorio: `C:\Proyectos\NexFlow`.
- Branch `main`; HEAD inicial y final: `a28ff465b3f885924ffe1de5b955c9f07e54add8`, coincide con la base solicitada.
- Estado inicial: ningún cambio tracked ni staged; `docs/UX_UI_SPRINT_06A_RELEASE_READINESS.md` ya existía como untracked y se conservó.
- Windows/PowerShell, Node `v24.18.1`, npm `11.16.0`, SDK .NET `10.0.401`, Chrome headless instalado. Dependencias existentes; sin instalación, restore ni cambios de versiones.
- Se leyeron AGENTS.md y únicamente el ticket UX-06A. Historia local confirma UX-01A (`dbaf218`), UX-01B (`0267146`), UX-02 (`c9c9ba1`), UX-03 (`51d2f52`), UX-04 (`158016e`) y UX-05 (HEAD). Revisión dirigida de contratos, callers y diferencias de estos módulos; sin auditoría general.

## Matriz de aceptación

PASS significa comprobado localmente con el alcance indicado, no una prueba en producción. ESTÁTICO y PENDIENTE no equivalen a PASS ejecutado.

| Fase | Resultado y criterio | Evidencia |
| --- | --- | --- |
| UX-01A | **PASS local**: GET semanal `locationId=<id\|all>&from&to`, inicio inclusivo/fin exclusivo, máximo 7 días, compatibilidad `date`, JSON/estados, historial, cancelación, workspace autenticado y permisos. **PENDIENTE** PostgreSQL y autenticación real. | `ReservationsApiTests`, `ReservationsRepositoryTests`; controller/DTO y `reservation.service.ts`. HTTP localhost con autenticación de prueba, repositorio SQLite en memoria. |
| UX-01B | **PASS**: 19 unidades y 11 escenarios habituales de navegador; lunes-domingo, mes/año, Lima/DST, filas reales, consulta semanal única, crear/reagendar/cancelar/completar e invalidación por workspace/sede. **FAIL funcional P1**: reservas autorizado y perfil denegado. | `reservations-agenda.test.ts`, `reservations-browser.test.mjs` (12 casos con la caracterización nueva); `ReservationsPage.tsx` y `queryKeys.ts`. |
| UX-02 | **PASS local**: 13 unidades y 13 casos de navegador; PRODUCT/SERVICE/SHARED, permiso combinado, scope inmutable, conservación de registros, errores, cancelación, doble envío y aislamiento. **ESTÁTICO**: autorización de categorías en backend; no prueba contra Firestore real. | `category-management.test.ts`, `categories-browser.test.mjs`; DTOs, `CatalogController` y `TenantCapabilityFilter` registrado en MVC. |
| UX-03 | **PASS local**: 12 unidades y 15 casos de navegador; `[]` distinto de siete días cerrados, propuesta 08:00–20:00 sin PUT automático, borradores por sede/sesión, payload semanal válido, errores 400/403/503 e invalidación limitada. Contrato backend **ESTÁTICO**. | `business-hours.test.ts`, `hours-browser.test.mjs`; `HoursTab`, `business.service.ts`, `BusinessController`, `BusinessHoursDto`. |
| UX-04 | **PASS local**: 7 unidades y 13 casos de navegador; commercialName autorizado, Por definir/loading/error, guardado confirmado, Query compartida, caché por usuario/workspace/permisos, logout, rutas, SuperAdmin y drawer. | `workspace-navigation.test.ts`, `navigation-browser.test.mjs`; `WorkspaceLayout`, `ProfileTab`, `useBusinessProfile`, `queryPersistence`. |
| UX-05 | **PASS local**: 5 unidades de tabs, 14 de WhatsApp y 15 casos de navegador; permisos dinámicos, teclado/ARIA, navegación sin escrituras, errores y estados WhatsApp, doble clic y cierre explícito. **PENDIENTE** reconciliación real de desconexión 503. | `settings-tabs.test.ts`, `whatsapp-state.test.ts`, `settings-browser.test.mjs`; suites backend Evolution seleccionadas. |

Las cinco suites de navegador comprobaron 360/768/1280 px, foco y teclado, overflow, modales, loading/empty/error y recuperación con fixtures. Se inspeccionaron también capturas locales de agenda móvil, WhatsApp móvil y horarios. Es cobertura dirigida de accesibilidad, no certificación exhaustiva WCAG ni prueba con lector de pantalla. No hubo llamadas reales a Firebase, Cloudinary, n8n o Evolution; el navegador bloqueó tráfico fuera de localhost.

## Ejecuciones y resultados

Comandos frontend desde `C:\Proyectos\NexFlow\frontend`; backend desde la raíz. Duraciones solo donde las emitió la herramienta.

| Comando | Resultado final |
| --- | --- |
| `npm run lint` | **PASS**, exit 0, sin advertencias/errores. |
| `npm run build` | **PASS**, exit 0. Vite 8.2.2: 2021 módulos; bundle JS 703,88 kB (gzip 210,14 kB). Advertencia real por chunk superior a 500 kB, sin ocultarla ni cambiar límites/dependencias. |
| `node --import ./tests/reservations-loader.mjs --test tests/reservations-agenda.test.ts` | **PASS 19/19**, 3,937 s. Se utiliza el cargador existente de TS/JSX y fixtures. |
| `node --test tests/category-management.test.ts tests/business-hours.test.ts tests/workspace-navigation.test.ts tests/settings-tabs.test.ts tests/whatsapp-state.test.ts` | **PASS 51/51**, 0,863 s. |
| `node --test --test-concurrency=1 tests/reservations-browser.test.mjs tests/categories-browser.test.mjs tests/hours-browser.test.mjs tests/navigation-browser.test.mjs tests/settings-browser.test.mjs` | **PASS 68/68**, 104,913 s; categorías 13, horarios 15, navegación 13, reservas 12, configuración 15. |
| `dotnet build backend/backend.slnx --no-restore --verbosity normal -m:1` | **PASS**, 0 errores, 3 advertencias, 33,35 s; configuración Debug. |
| `dotnet build backend/backend.slnx --no-restore --configuration Release -m:1 -p:UseSharedCompilation=false --verbosity minimal` | **PASS**, 0 errores, 3 advertencias, 60,21 s. |
| Tests backend, filtro A debajo | **PASS 78/78**, 0 omitidas, duración indicada 2 s; binarios Debug compilados previamente. |
| Tests backend, filtro B debajo | **PASS 61/61**, 0 omitidas, duración indicada 3 s; mocks del proveedor. |

Comandos backend exactos, A y B respectivamente:

```powershell
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~ReservationsApiTests|FullyQualifiedName~ReservationsRepositoryTests|FullyQualifiedName~CatalogHttpTests|FullyQualifiedName~EvolutionSecurityTests|FullyQualifiedName~EvolutionLifecycleTests" --verbosity minimal
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~EvolutionConnectionTests|FullyQualifiedName~EvolutionFetchTests" --verbosity minimal
```

**Intentos fallidos/bloqueados, resueltos para la pasada final:**

- El comando TypeScript con los seis archivos juntos, sin `--import ./tests/reservations-loader.mjs`, dio 51 PASS y un módulo FAIL (`ERR_MODULE_NOT_FOUND`, import de auth en reservas). Las 19 pruebas de ese módulo no se habían ejecutado. Se corrigió la invocación, no auth ni dependencias.
- Primera pasada de las cinco suites: **64/67 PASS, 3 FAIL** en navegación (`Button unavailable`). Reproducciones posteriores también expusieron una carrera del fixture al cambiar identidad/permisos (una lectura adicional). Una ejecución intermedia de navegación+reservas terminó **24/25 PASS**. Las esperas/publicación del fixture se corrigieron sin relajar las aserciones; navegación aislada final **13/13**, pasada completa final **68/68**.
- `dotnet build backend/backend.slnx --no-restore` y el primer `dotnet test ... --no-restore --filter ... --verbosity normal` devolvieron exit 1 con 0 errores y sin pruebas ejecutadas dentro del sandbox. La compilación secuencial resolvió el build; el intento de tests con `-m:1 -p:UseSharedCompilation=false` se anuló por timeout VSTest/testhost de 90 s. Las ejecuciones locales autorizadas fuera del sandbox pasaron, sin aumentar timeouts ni usar servicios externos.
- Advertencias backend existentes: **CS8981** en `20260929030327_refactor.cs:9` y su Designer `:16`; **CS8602** en `FirestoreCatalogRepository.cs:198`. No se modificaron migraciones ni código ajeno.

## Cambios, defectos y límites

**Frontend — solo pruebas:**

- `frontend/tests/navigation-browser.test.mjs`: reset espera Dashboard y finalización de consultas antes de seguir; evita observar el render anterior.
- `frontend/tests/fixtures/navigation-preview.tsx`: publicación explícita de identidad con `flushSync` para que CDP observe el estado ya renderizado del fixture. No cambia el store de autenticación del producto ni certifica Firebase real.
- `frontend/tests/reservations-browser.test.mjs`: añade reproducción de RESERVATIONS autorizado y BUSINESS_PROFILE denegado; comprueba mensaje, cero consultas al perfil/semana, ausencia de horas supuestas y creación deshabilitada.

**Backend:** ningún archivo modificado. **Documentación:** este informe. Diff puntual: 21 líneas añadidas/1 sustituida en tres archivos de pruebas; revisión final y `git diff --check` sin errores de whitespace. Ninguna funcionalidad, protocolo, esquema, dependencia o configuración fue modificada.

**P1 sin corregir — zona horaria y permisos.** `ReservationsPage` requiere `canReadProfile` para `zoneReady`; sin él no consulta la semana. `/me`, el DTO de reserva y los slots no entregan la zona del negocio. El backend calcula fechas con el perfil interno bajo autorización RESERVATIONS, pero la UI no puede inferir una zona IANA fiable de instantes UTC, especialmente en semanas vacías y DST. El bloqueo protege la confidencialidad y evita horas incorrectas, pero impide el flujo principal del usuario autorizado.

Propuesta segura, **no implementada en UX-06A**: acordar metadatos mínimos de agenda que entreguen exclusivamente la zona efectiva del workspace bajo RESERVATIONS/READ; backend conserva validación de identidad, frontend usa Query con workspace y evita consultar el perfil completo. Requiere cambio coordinado **backend + frontend** y regresiones de permisos, ausencia de datos, cambio de workspace y DST. No ampliar BUSINESS_PROFILE/READ ni usar Lima como sustituto de una lectura denegada. No se ha identificado necesidad de migración para esta propuesta.

**WhatsApp 503 pendiente.** La UI probada conserva sesión y error, exige desconexión explícita, no afirma cierre ni permite otro QR sin estado confirmado. Esto no demuestra que Evolution cierre/reconcilie la sesión real. Abrir ticket técnico separado backend/Evolution con reproducción autorizada del 503, trazabilidad sin secretos y recuperación idempotente; no cambiar protocolo ni eliminar sesiones automáticamente. El cambio de número real no queda certificado.

**Build inspeccionado:** comprobación local dirigida, sin imprimir valores de `.env`: siete variables VITE, todas dentro de la lista permitida de API/configuración pública Firebase; URL API incluida, HTTPS, host no local, ruta `/api`, sin credenciales/query/fragment. No se detectaron bloques de claves privadas, credenciales de BD, asignaciones de secretos de proveedores, URLs literales localhost/127.0.0.1 ni marcadores de fixtures. La clave cliente Firebase incluida es configuración pública; no sustituye autorización backend. Esta búsqueda no es un análisis exhaustivo de secretos ni confirma disponibilidad del servidor configurado.

**PENDIENTE, no ejecutado:** PostgreSQL real (SQLite en memoria no valida xmin, SQL Npgsql, locks ni datos de producción), persistencia Firestore real, autenticación Firebase real, proveedor Evolution y smoke test con servidor desplegado. La preservación de datos/historial se comprobó con fixtures y lectura SQLite; no hubo escrituras en bases reales. La suite backend completa no fue solicitada para esta fase y no se declara ejecutada: se ejecutaron los filtros indicados, 139 casos distintos.

`frontend/.firebase/hosting.ZGlzdA.cache` está versionado. Se propone retirar del versionado e ignorar ese artefacto en una tarea separada; se dejó intacto. Capturas y dist generados permanecen en rutas ignoradas.

## Condiciones de salida y rollback propuesto

NO-GO por el P1 reproducido; ningún P0 observado en las verificaciones ejecutadas. Resolver el contrato de zona horaria y repetir su criterio funcional antes de solicitar UX-06B. La reconciliación 503 necesita evaluación técnica independiente; los servicios y datos reales permanecen PENDIENTES. Frontend y backend siguen construibles por separado; esta fase no genera artefactos que deban desplegarse para corregir comportamiento del producto.

Rollback futuro, **solo propuesto**: conservar identificadores de la última imagen API y release Hosting aprobados y un backup verificado; si falla un despliegue autorizado, volver a esas versiones, comprobar health y smoke tests autenticados por workspace y revisar compatibilidad antes de reanudar. No se propone reset de este checkout ni restauración destructiva de datos. UX-06B, backup, ARM64, Oracle y Firebase Hosting no se ejecutaron. Trabajo detenido para auditoría.
