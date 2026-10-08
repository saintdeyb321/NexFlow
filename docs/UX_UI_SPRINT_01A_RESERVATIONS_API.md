# NexFlow · UX-01A — API semanal de reservas

**Estado:** PENDIENTE · Solo este sprint  
**Línea base de referencia:** `2a09def64691094126d1807d0cc35eee21088ed7`; comprobar HEAD local antes de comenzar.  
**Objetivo:** habilitar consultas eficientes de una semana completa y de semanas históricas, sin cambios de frontend ni despliegues.

## Alcance estricto

Trabaja SOLO en la API, la abstracción/repositorio y los tests de reservas necesarios. Respeta `AGENTS.md` y la implementación local real. No leas otros planes o toda la solución; usa `rg` y abre archivos directamente relacionados.

Entradas iniciales a inspeccionar:

- `backend/NexFlow.API/Controllers/Business/ReservationsController.cs`
- `backend/NexFlow.Application/Abstractions/IReservationRepository.cs`
- `backend/NexFlow.Infrastructure/Persistence/PostgreSQL/Repositories/ReservationRepository.cs`
- `backend/NexFlow.Application/Features/Reservations/ReservationDtos.cs`
- Pruebas existentes de reservas y zonas horarias, buscadas de forma dirigida.

## Contrato requerido

1. Conservar `GET /api/reservations?locationId={id}&date=YYYY-MM-DD` y su respuesta actual. No romper al frontend desplegado.
2. Ampliar el mismo endpoint o incorporar un endpoint de rango coherente para aceptar `from=YYYY-MM-DD&to=YYYY-MM-DD` como intervalo **[desde, hasta)** en **fechas civiles de la zona horaria del negocio**. Definir claramente el contrato y evitar parámetros ambiguos combinados (por ejemplo, `date` y `from/to` simultáneos).
3. Admitir un rango de **1 a 7 días**, con límite superior exclusivo; para la semana de lunes a domingo, el frontend enviará lunes y lunes siguiente.
4. Resolver la zona horaria desde el perfil del negocio; fallback actual `America/Lima`. Convertir **cada medianoche local** en UTC de forma correcta, también ante cambios de horario de verano. No utilizar `DateTime.ToUniversalTime()` sobre una fecha local sin zona explícita ni confiar en la zona del servidor.
5. Validar fechas, orden y longitud del rango, devolviendo error 400 claro; respetar cancelación. Evitar leer todo el historial en memoria y filtrarlo allí.
6. Consultar PostgreSQL una sola vez por rango acotado, con límite inferior inclusivo, superior exclusivo y orden determinista (inicio, identificador).
7. Imprescindible `WorkspaceId` del contexto autenticado; nunca del cliente. Mantener licencias, capacidades, aislamiento y validación de sede del workspace. Definir explícitamente y probar cómo funciona la selección `all`/todas las sedes sin saltarse autorizaciones; no aceptar una sede ajena como si fuera válida.
8. Incluir `Pending`, `Confirmed`, `Completed` y `Cancelled`, incluso en semanas pasadas. No cambiar ni recalcular estados antiguos.
9. No tocar creación, cancelación, completado, edición, disponibilidad, motor de IA ni reglas de concurrencia. El acceso por rango es una lectura, no un nuevo motor de reservas.
10. Si ya existe infraestructura de consulta por rango (repositorio con `startUtc`/`endUtc`), preferir reutilizarla a duplicarla. Investigar índices existentes antes de proponer uno; **no crear/aplicar migraciones** en este sprint sin aprobación.

## Pruebas obligatorias

- Endpoint diario heredado conserva su contrato y datos.
- Lunes a domingo en una llamada; límites `[from,to)` no duplican reservas entre semanas.
- Semana que cruza mes/año y consultas históricas arbitrarias.
- Zonas horarias; cuando sea posible, caso con DST y límites de medianoche local.
- Fechas inválidas; rango vacío, negativo, mayor que 7 días y parámetros incompatibles.
- Todos los estados, incluidos completados/cancelados, visibles.
- Separación de workspaces, sede ajena no autorizada, filtro por sede y selección agregada válida.
- Consulta realmente filtrada en base de datos; no 7 llamadas diarias.
- Los cambios no alteran disponibilidad ni invariantes de concurrencia.

Si un test de integración de PostgreSQL no puede ejecutarse localmente, añadir unit/integration tests factibles y declarar con precisión lo que falta validar en PostgreSQL real.

## Fuera de alcance

- No modificar archivos de `frontend/`, estilos, modales, categorías, horarios o barra lateral.
- No crear un calendario visual ni consumir el endpoint desde React (eso es UX-01B).
- No modificar Firestore, Evolution, Firebase, webhook, licencias ni otros módulos.
- No instalar librerías, hacer `git commit/push` ni desplegar en Oracle.
- No ejecutar migraciones contra producción ni generar datos reales.

## Secuencia eficiente de ejecución

1. Consultar estado local del repositorio sin modificar Git.
2. Leer solo los archivos de arriba y tests cercanos; comprobar contratos.
3. Proponer en 3–5 líneas el contrato elegido y archivos que tocarás; seguir con la implementación si no hay bloqueo/riesgo de contrato.
4. Implementar cambio mínimo, pruebas específicas y correcciones.
5. Ejecutar build backend y tests relevantes, sin reinstalar dependencias salvo necesidad real.
6. Revisar exclusivamente el diff del sprint y detenerte. No empezar UX-01B.

## Criterios de salida

- [ ] GET diario sigue operativo para frontend actual.
- [ ] GET semanal cubre exactamente 7 días locales con una consulta PostgreSQL acotada.
- [ ] Estados e historial completos, resultados ordenados, permisos y tenant correctos.
- [ ] Errores de validación claros; fechas con zona correcta.
- [ ] Pruebas y compilación ejecutadas, resultados sin inventar.
- [ ] No hay cambios fuera del alcance, ni migraciones/despliegue.

## Formato de informe final Codex (breve)

- Contrato definitivo y ejemplo de query.
- Archivos modificados (máximo una línea por archivo).
- Aceptación: OK / PENDIENTE, solo criterios del ticket.
- Build/tests: comandos y resultado real.
- Riesgos/bloqueos reales, si existen.

**Detenerse y esperar auditoría humana después de este sprint.**
