# F3 — Reservas naturales

Base local de F1: `0aa3bfbe87e99008b44fdf7e0704a617899f77df`. Se continúa después de G2. Sin Git, proveedores, WhatsApp ni almacenes externos.

## Defectos y reproducción

`RuntimeNaturalBookingTests`: primera ejecución `f3-before.trx`, 7 ejecutadas, 5 aprobadas y 2 fallidas: listado automático al solicitar una franja y rechazo del nombre en minúsculas. La regresión también detectó que eliminar únicamente la restricción de capitalización podía interpretar prosa compuesta como nombre; `f3-name-precedence-before.trx` reproduce ese defecto (1 fallida). No se permite sobrescribir el nombre con una referencia lingüística.

## Cambios y contratos

- `BookingFlow` pregunta una hora preferida en COLLECT_TIME sin consultar la cuadrícula. Una franja no selecciona hora. Petición explícita de opciones y alternativas ante indisponibilidad conservan los horarios reales del motor. `GetAvailabilityAtAsync` valida inicios libres, duración, sede y ocupación; ReservationEngine no cambia.
- `AiResponseOrchestrator` no añade una segunda lista cuando no hay opciones. `ConversationRecoveryService` encamina la solicitud explícita de horarios al mismo BookingFlow.
- `NameSlotResolver`, `WorkspaceSlotResolver` y `CommercialCourtesy` admiten nombres literales en minúsculas cuando se solicitan y separan entidades conocidas de fragmentos libres. Las referencias y la prosa ambigua quedan para comprensión semántica, sin inventar nombres. Se reutilizan entidades autorizadas ya leídas.
- `ReservationTimeReference`, dentro del contexto existente, conserva una consulta de disponibilidad vigente, workspace, sede, servicio, fecha y hora. Caduca a los diez minutos y requiere pregunta informativa vigente. `SelectReferencedTime` exige evidencia literal y acto de selección, vuelve a comprobar disponibilidad y nunca confirma una reserva. Consulta, selección y consentimiento son pasos distintos. Una hora explícita consume la referencia anterior.
- AiInterpreter recibe únicamente fecha/hora referida y pregunta vigente; no recibe historial completo ni identificadores comerciales para esa referencia. AiRouter no cambia.

No se modifican motores, permisos, privacidad de citas registradas, CAS, recibos, recursos, Outbox ni transporte. Contextos antiguos sin la referencia opcional siguen siendo válidos; se pide hora explícita si falta evidencia vigente.

## Pruebas y gate

Resultados en `backend/NexFlow.Tests/TestResults/agent-runtime-f234/`:

| Ejecución | Ejecutadas | Aprobadas | Fallidas | Omitidas |
|---|---:|---:|---:|---:|
| f3-functional-and-historical.trx | 1044 | 1032 | 12 | 0 |
| f3-current-contract.trx | 914 | 914 | 0 | 0 |

La primera incluye Concierge, Assistant, Knowledge, AiResilienceTests, CatalogChatTests y ReservationsPeruTimeTests. Excluye categorías Integration, RedisIntegration y ProviderIntegration. La segunda mantiene esos filtros y separa explícitamente CommercialCertificationTests y QualityHoldout61Tests; no representa la suite completa.

Los doce fallos ampliados son `VerifiedFact`: casos públicos `clinic/hotel/restaurant/bakery/limited-09/-10` y holdout congelado H10/H21. Exigen horas automáticamente después de indicar una franja; F3 exige preguntar la hora sin lista. Se conservan corpus, expectativas y evidencia roja. No constituyen una certificación comercial aprobada. Las pruebas interactivas actualizadas solicitan opciones explícitamente antes de comprobar la lista; mantienen consentimiento, UTC, duración, licencias y efectos únicos.

G3 **OK local para el contrato F3, con incompatibilidad histórica P2 registrada**: horas 12/24, inicio libre, franjas sin hora inventada, opciones explícitas, nombre en minúsculas, consulta sin mutación, referencia revalidada, consentimiento, correcciones, zonas/DST, replay, CAS y handoff en la batería indicada. Sin P0/P1 funcional pendiente demostrado. No se declara toda la regresión verde ni calidad conversacional externa.

Build `dotnet build backend/backend.slnx --no-restore`: correcto, 0 errores, 0 advertencias en la ejecución incremental `f3-build.log`.

## Riesgos y límites

La semántica referencial se probó con el intérprete simulado existente, no con Gemini/Groq. La evaluación ciega independiente sigue pendiente. Los corpus históricos requieren una revisión explícita de contrato; no se ajustan para ocultar defectos. No se ha certificado PostgreSQL/Firestore/Redis real, proveedores ni WhatsApp. No se atribuyen ahorros monetarios o latencia de proveedor: solo se verificó que la pregunta inicial de hora no consulta cuadrícula ni IA y que nombres inequívocos no consumen IA.
