# Concierge 2.0 — runbook de piloto aislado (NO EJECUTADO)

Este procedimiento prepara la autorización del piloto; no autoriza producción. Usar exclusivamente negocio, licencia, contactos, mensajes y catálogos sintéticos. No usar conversaciones de clientes ni reprocesar inbound histórico.

## 1. Gate de código e integraciones

1. Revisar el SHA y el diff de esta entrega; registrar el SHA exacto de la imagen candidata y de la imagen anterior compatible. No cambiar migraciones ni servicios compartidos.
2. Ejecutar el build y la suite local:

```powershell
dotnet build backend/backend.slnx --no-restore
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --filter 'Category!=Integration&Category!=RedisIntegration&Category!=ProviderIntegration' --logger 'trx;LogFileName=local.trx'
```

3. En un runner desechable, ejecutar el workflow existente `rc3-reservation-integration.yml`: PostgreSQL temporal y Firestore Emulator `demo-nexflow-rc3`. Exigir cero fallos y cero omisiones en `Category=Integration`, incluyendo los nuevos contratos de pedidos. Conservar Run ID, SHA y TRX. No inferir aprobación de Emulator a partir de pruebas SDK en memoria.
4. Redis tiene batería separada; el workflow RC3 actual **no aprovisiona Redis**. En un runner ya preparado con PostgreSQL/Emulator y Redis desechable en `127.0.0.1:16379`, sin credenciales de aplicación, ejecutar:

```powershell
# Sólo en el entorno de pruebas desechable, con las variables RC3 del workflow existente.
$env:NEXFLOW_RC3_INTEGRATION='1'
$env:NEXFLOW_CONCIERGE_REDIS_INTEGRATION='1'
$env:NEXFLOW_CONCIERGE_REDIS_ENDPOINT='127.0.0.1:16379'
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --filter 'Category=RedisIntegration' --logger 'trx;LogFileName=redis.trx'
```

La batería utiliza únicamente claves de workspace sintético generado, base Redis 15, sin flush. El harness RC3 acepta sólo PostgreSQL loopback con base administrativa y credenciales sintéticas predefinidas y el proyecto Emulator demo. Una dependencia indisponible con opt-in activado debe fallar; no debe convertirse en un resultado verde.

## 2. Contratos opcionales Groq/Gemini

Requieren autorización explícita adicional para llamadas pagadas y credenciales **de prueba** por canal privado. No utilizar `appsettings`, `.env`, secretos productivos ni mensajes privados. Preparar `NEXFLOW_TEST_GROQ_API_KEY`, `NEXFLOW_TEST_GROQ_MODEL`, `NEXFLOW_TEST_GEMINI_API_KEY`, `NEXFLOW_TEST_GEMINI_MODEL`; los nombres coinciden con el contrato del harness. No imprimir sus valores.

Tras obtener esa autorización, habilitar `NEXFLOW_CONCIERGE_REAL_AI_AUTHORIZED=1` y ejecutar:

```powershell
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --filter 'Category=ProviderIntegration' --logger 'trx;LogFileName=providers.trx'
```

Son ocho extracciones sintéticas por proveedor; no es redacción/composición. Registrar disponibilidad, latencia, JSON, intención, entidades, estado HTTP, clasificación de cuota/timeout, solicitudes y tokens reportados. Los contratos directos no inducen agotamiento de cuota ni validan fallback real entre proveedores. Medir ese fallback durante el piloto autorizado mediante incidencia controlada de la cuenta de prueba; conservar la cobertura simulada existente. Si no hay consumo o tarifa verificada, indicar coste **NO MEDIDO**. No registrar prompts, respuestas completas, claves ni nombres/teléfonos en las trazas.

## 3. Preparación del workspace de piloto

1. Operador autorizado crea/selecciona un workspace nuevo de prueba. Verifica usuario miembro, capacidades de `/me` y licencia activa. Para pedidos: `CATALOG+ORDERS`; para reservas: `SERVICES+RESERVATIONS`. Habilita `LOCATIONS` y `BUSINESS_HOURS` sólo si se probarán consultas independientes. No conceder módulos a clientes para facilitar la prueba.
2. Cargar desde la aplicación dos sedes sintéticas inequívocas, horarios y catálogo en PEN; registrar alias/precios, un producto inactivo y un servicio reservable. Configurar el reloj del negocio en `America/Lima`. No importar datos reales.
3. Health: comprobar `/health/live` y `/health/ready` y su cobertura real. Completar lectura supervisada de conectividad PostgreSQL/Redis/Firestore y workers cuando no estén incluidos en readiness; readiness no certifica diálogo. Registrar latencia, cola, edad del head, reintentos y DeadLetters, Outbox y recursos, sin exportar payloads.
4. Conectar WhatsApp mediante la acción explícita de la aplicación (`api/business/whatsapp/connect`, estado en `api/business/whatsapp/status`). Usar número y cuenta autorizados de prueba. Verificar instancia exclusiva del workspace y webhook autenticado por instancia en `api/webhooks/evolution`. No revelar claves ni activar webhook legacy para sortear autenticación.

## 4. Ejecución supervisada y evidencia

Registrar por turno un ID interno/hash, workspace anonimizado, duración, ruta local/semántica, comprensión, fase, consultas al motor, resultado comercial y entrega. No exportar texto del cliente, nombres ni teléfonos.

1. Saludo y consulta concreta: comparar precio/duración con el catálogo; ninguna operación ni redacción pagada por cortesía.
2. Reserva: petición multi-slot, información interrumpida, corrección de fecha/sede, franja sin hora inventada, resumen, confirmación explícita; verificar exactamente una reserva y efecto durable. Consultar la agenda y disponibilidad con permisos reales.
3. Pedido: dos artículos, corrección absoluta, retiro, hipótesis y subtotal; confirmar y verificar `PendingReview`, moneda, precios, cantidades y sede estructurada en el documento. Repetir sólo un webhook sintético controlado con el mismo ID: un único pedido y notificación.
4. Cambiar precio/moneda entre resumen y envío: no aceptar el precio anterior, mostrar recotización y exigir nueva confirmación. Eliminar la sede sintética: exigir elegir otra; no perder el borrador ni enviarlo sin sede por un reintento.
5. Revocar módulos de la licencia de prueba: denegar la operación y las consultas independientes restringidas, conservando el borrador. Con licencia `SERVICES+RESERVATIONS`, comprobar que las sedes/horarios internos de la reserva siguen funcionando.
6. PDF: sólo artefacto CURRENT del scope correcto; confirmar estado durable de documento. Ante fallo, seguir ayuda por texto. `UnknownDelivery` implica consulta/conciliación supervisada; **nunca reenviar automáticamente**.
7. Handoff explícito: una confirmación, modo humano durable y supresión de respuestas automáticas pendientes; no borrar el borrador.
8. Fallo controlado de cuenta/servicio de prueba: inspeccionar la cadena inbound → comprensión → validación → efecto → outbound. Un fallo después del commit debe recuperar el mismo origen sin nueva compra. Un fallo de Redis no debe devolver un contexto por encima del durable.

## 5. DeadLetter, parada y rollback

- Detener el piloto ante duplicado, precio/sede inventados, falta de licencia, filtración entre workspaces o pérdida de confirmación. Detener también si no se puede explicar un head bloqueado o un envío incierto.
- Diagnosticar por IDs y estados durables; distinguir `Processed` de `Sent`. FIFO conserva el head fallido hasta reintento o DeadLetter. Si un `ORDER_COMMIT` quedó pendiente, comprobar primero pedido y notificación por la identidad original; no cambiar su `SourceMessageId` ni crear otra orden para desbloquear.
- La recuperación administrativa existente debe ser supervisada y limitada al origen sintético afectado. No borrar estados, hacer reseteos masivos, reabrir históricos ni marcar entregado un mensaje no confirmado.
- Rollback: suspender nuevas entradas del piloto mediante el control operacional autorizado; drenar escritores activos antes de instalar API/workers compatibles del digest anterior. Esta entrega no tiene migración; el campo Firestore opcional no requiere Down. La versión anterior puede leer los documentos, pero **no ofrece la recuperación nueva** ni persiste la sede estructurada en los pedidos nuevos que cree: no usar el rollback como prueba de compatibilidad funcional. No borrar el campo ni pedidos, y no reiniciar Evolution/Redis/n8n compartidos.
- Revalidar health, permisos y estados pendientes tras volver. Mantener el piloto detenido hasta resolver la causa. No se incluye ni se ejecutó ningún comando de despliegue en esta entrega.

## Evidencia necesaria para GO

Build y regresiones locales; 100 conversaciones con al menos 95% de resolución y cero violaciones críticas; todas las integraciones críticas reales sin omisiones; disponibilidad/contratos de cuentas IA autorizadas; revisión humana independiente de las transcripciones del piloto; WhatsApp E2E; recuperación y rollback supervisados. Cualquier evidencia ausente se registra PENDIENTE/NO MEDIDO.
