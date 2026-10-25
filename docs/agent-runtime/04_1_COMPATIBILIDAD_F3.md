# Compatibilidad histórica F3 — contrato de preferencia v2

Base local por lectura de `.git/HEAD` y su ref: `166a1773a02aae4771467ed8fbc73a000d78add8`. No se ejecutó Git; no se certifica árbol limpio o remoto. Autorización agrupada A→F5→F6.

## Evidencia reproducida

`backend/NexFlow.Tests/TestResults/agent-runtime-f56/a-historical-before.trx`: **130 ejecutadas, 118 aprobadas, 12 fallidas, 0 omitidas**. Se ejecutaron íntegros CommercialCertificationTests y QualityHoldout61Tests, sin excluir los casos fallidos. Mensajes exactos:

```text
clinic-09: VerifiedFact
clinic-10: VerifiedFact
hotel-09: VerifiedFact
hotel-10: VerifiedFact
restaurant-09: VerifiedFact
restaurant-10: VerifiedFact
bakery-09: VerifiedFact
bakery-10: VerifiedFact
limited-09: VerifiedFact
limited-10: VerifiedFact
H10/3/A: VerifiedFact
H21/3/A: VerifiedFact
```

Se contrastaron los turnos afectados y las comprobaciones del evaluador: todos exigen `16:00` al expresar una franja. El estado conserva COLLECT_TIME, fecha, sede, servicio y preferencia; no selecciona hora. H10 conserva la interrupción de precio y H21 clasifica el timeout como fallo del proveedor sin perder borrador. No apareció otro criterio fallido en esta ejecución. La evaluación pública detiene cada caso en la primera aserción fallida; la versión nueva también ejecuta los turnos posteriores para verificar que no oculta defectos posteriores.

## Especificación versionada, sin cambiar evidencia original

`RuntimeF3ContractV2Tests` carga los mismos doce guiones y datos desde los recursos existentes. No escribe los JSON ni invoca un evaluador histórico alterado. Sustituye únicamente la expectativa de lista automática por: preferencia conservada, hora vacía, pregunta de hora, ausencia de lista/selección y cero reservas nuevas. Exige exactamente una sustitución por guion; no admite una evaluación vacía.

Después agrega una petición explícita de opciones: exige disponibilidad real del motor, `16:00` y ningún cambio de TargetTime. Sigue los turnos originales; los guiones que terminaban en la pregunta también prueban selección puntual. Exige GetAvailabilityAtAsync, conserva aserciones de precio, fecha, sede, intención, borrador, fallos de proveedor, cantidades de efectos, confirmación tipada, una creación por reserva, ausencia de pedidos/handoff y workspace de las lecturas. No agrega frases especiales al código de producción.

`a-versioned.trx`: **38 ejecutadas, 38 aprobadas, 0 fallidas, 0 omitidas**: doce guiones v2, RuntimeNaturalBookingTests y RuntimeReadToolsTests. Esto no convierte las doce pruebas originales en aprobadas ni es evaluación ciega nueva.

SHA256 de corpus originales antes de modificar:

- CommercialCertificationCorpus.json: `E9C6311369F4D810B553BA546F3D06E7A4D6B6A901CC892AFEB2ECEBBF1E19F4`.
- QualityHoldout61.json: `3DFE02AC4CC1D6A067D1A875917615A4A3B3B73AB6D72C47EF2D63FE1F7E96E0`.

## Corrección F2 confirmada

`BusinessReadTools.ReadAsync` convertía KnowledgeStatus.Denied en NotFound. `a-denied-before.trx`: 1 ejecutada, 1 fallida; mensaje exacto `Expected: Denied / Actual: NotFound`. El parche devuelve BusinessReadStatus.Denied sin Facts, Knowledge ni Slots; conserva permisos y no consulta otra fuente como fallback. Regresión añadida a RuntimeReadToolsTests, aprobada en la batería v2.

Archivos: BusinessReadTools.cs, RuntimeReadToolsTests.cs, nuevo RuntimeF3ContractV2Tests.cs y este informe. Ningún cambio en corpus, evaluadores originales, motores, frontend, esquema o infraestructura.

Build `dotnet build backend/backend.slnx --no-restore`: correcto, 0 errores, 0 advertencias incrementales (`a-build.log`). Los tests usan fixtures y HTTP simulado existentes; sin SaaS ni base real. **Bloque A: OK local**, incompatibilidad histórica explícitamente conservada; permite F5. La calidad comercial independiente sigue pendiente.
