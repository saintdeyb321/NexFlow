# NexFlow MVP — evaluación independiente y piloto WhatsApp

Preparación S8, 2026-10-26; backend local evaluado `be07f89c2146f7c4344effad849e1335c9614e86`. **PLAN, NO EJECUCIÓN NI CORPUS CIEGO APROBADO.** Ver [checklist de release](GO_LIVE_CHECKLIST_NEXFLOW_MVP.md) y [Q01–Q10](MATRIZ_Q01_Q10_VALIDACION_MVP.md).

## 1. Independencia y congelación

Un evaluador diferente al implementador debe crear **al menos 32 conversaciones inéditas multitur**, fuera del repositorio y sin mostrar entradas/expectativas privadas al implementador. La matriz siguiente reserva cobertura, **no contiene conversaciones inéditas ni sus respuestas esperadas**. No reutilizar las frases/corpus públicos de Concierge 2/3/Runtime4 como prueba ciega. Hoy no se recibió ese corpus ni un comando de evaluador independiente: gate PENDIENTE.

Registrar antes de ejecutar: SHA/digest de candidata, hash del corpus privado y del evaluador, versión de rúbrica, configuración sintética congelada, fecha/modelos y revisores. El corpus conserva turnos/expectativas sólo en custodia del evaluador; al repositorio llegan scores, invariantes y conteos sanitizados. Cambiar candidata/configuración o corregir después de ver resultados exige repetir el gate; ningún CI previo acredita el nuevo SHA.

Cuatro workspaces DEMO **W-A ≠ W-B ≠ W-C ≠ W-D**, datos 100% descartables, contactos autorizados diferentes, y una variante controlada con mismo identificador sintético en dos tenants para probar partición. No nombres/teléfonos reales en este plan o artefactos. Configurar mediante módulos actuales, sin nuevos roles/endpoints:

| Perfil | Módulos comerciales y configuración | Permisos/controles |
|---|---|---|
| W-A clínica multisede | SERVICES+RESERVATIONS, dos sedes/calendarios, duración/RequiresReservation/scopes diferentes; FAQ/perfil públicos | Sólo módulos informativos contratados; dependencias internas de reserva no conceden consultas independientes |
| W-B comercio | CATALOG+ORDERS, PRODUCT con precios minor units/moneda/scope; sedes/horarios según licencia | Solicitudes PendingReview; sin pagos, stock garantizado o aceptación automática |
| W-C mixto | Ambos pares, PRODUCT/SERVICE con etiquetas parecidas, PDFs de ámbitos separados | No confundir índices del PDF/menú ni cambiar objetivo por consulta |
| W-D limitado | Subconjunto explícito, ej. SERVICES sin RESERVATIONS ni ORDERS/CATALOG, sin FAQ/sedes/horarios informativos en variantes | Denegación honesta sin lecturas restringidas, asistencia humana disponible |

Licencia activa no basta: dueño/revisor valida módulos, rol web, scopes, oferta y horarios de cada W. Zona configurada desde panel vigente: America/Lima. No datos privados reales para demostrar aislamiento.

## 2. Cobertura mínima de 32 conversaciones (guía pública, textos privados pendientes)

Cada fila será un caso multitur independiente creado por el evaluador, con parámetros nuevos y sin revelar su oráculo lingüístico privado. Las condiciones de seguridad públicas son obligatorias, no un listado de frases a memorizar.

| Cupo | Perfil | Eje que debe construir el evaluador | Invariante verificable públicamente |
|---|---|---|---|
| A01 | Clínica | Saludo y actividad natural | Profile autorizado, sin FAQ incidental ni hechos inventados |
| A02 | Clínica | Servicios publicados y reservables por sede | Razón por ítem; cinco/cuatro no se fuerza |
| A03 | Clínica | Precio/duración de servicio y consulta intermedia | Importe/duración exactos, sin PDF completo innecesario |
| A04 | Clínica | Disponibilidad con fecha relativa, acuse y elección | Consulta/acuse no seleccionan; fecha/hora sólo por elección vigente |
| A05 | Clínica | Nombre y datos juntos, resumen y consentimiento | Cero creación previa; una reserva durable después |
| A06 | Clínica | Hora ocupada/cerrado/fuera de jornada | Motivo correcto, alternativas reales, sin hora inventada |
| A07 | Clínica | Cambio de sede/servicio y referencia anterior | Revalidación y rechazo de referencia obsoleta |
| A08 | Clínica | Consulta/edición/cancelación de cita existente | Humano antes de SQL privado, sin filtrar existencia |
| B01 | Comercio | Pregunta de capacidad e inicio posterior | Pregunta sin pedido; inicio explícito crea sólo borrador |
| B02 | Comercio | Productos por nombre/alias y cantidades | IDs/activos/scope reales; sin confundir SERVICE |
| B03 | Comercio | Menú opcional, referencia ordinal y nueva lista | ID del menú vigente, no selección arbitraria |
| B04 | Comercio | Interrupción por perfil/horarios | Draft íntegro, pregunta siguiente coherente |
| B05 | Comercio | Corrección de cantidades/moneda e importes | Minor units y cotización actuales, sin total ficticio |
| B06 | Comercio | Precio/licencia cambia tras resumen | F7-01: revisión y nuevo consentimiento antes de enviar |
| B07 | Comercio | Consentimiento válido y replay concurrente | Una solicitud PendingReview y un aviso lógico |
| B08 | Comercio | Fallo tras commit/aviso y recuperación | Recupera efecto existente; no segunda compra |
| C01 | Mixto | Presentación y FAQ coincidente/no coincidente | Profile y Faq tipados, autorización actual |
| C02 | Mixto | PDF PRODUCT frente a SERVICE | CURRENT/scope/identidad correctos y contenido revisado aparte |
| C03 | Mixto | PDF Uploaded/Generated, stale y callback tardío | No publicar versión cancelada/obsoleta/ajena |
| C04 | Mixto | Catálogo genérico y aclaración de tipo | Continuación persistente sin selector paralelo |
| C05 | Mixto | Cambiar temporalmente pedido/reserva | Información no cambia objetivo; decisión explícita para descartarlo |
| C06 | Mixto | Timeout/429/JSON inválido entre turnos | Fallback honesto, borrador y consentimiento preservados |
| C07 | Mixto | Takeover durante IA/antes de outbound/commit | Bot suprimido, ACK de handoff lógico único |
| C08 | Mixto | Entrega incierta y callback repetido | UnknownDelivery sin reenvío ciego ni reejecución de negocio |
| D01 | Limitado | Producto/pedido no licenciado | Cero lectura/acción restringida; explicación y humano |
| D02 | Limitado | Reserva sin RESERVATIONS o SERVICES | No habilitación implícita por catálogo o saludo |
| D03 | Limitado | Ubicación/horario independiente restringido | Denegación; dependencia interna sólo si reserva autorizada |
| D04 | Limitado | FAQ revocada durante lectura | No publicación/relectura restringida |
| D05 | Limitado | IDs y URL/documento de otro W | Cero acción/dato ajeno, fallo cerrado |
| D06 | Limitado | Instrucciones hostiles dentro de datos | Datos como hechos, nunca SQL/tools/escrituras autónomas |
| D07 | Limitado | Redis obsoleto/fallo y contexto nuevo | Durable/CAS prevalecen; replay viejo no borra draft |
| D08 | Limitado | Reinicio/recuperación aislada y cola pendiente | FIFO/idempotencia, sin reprocesar DLQ ni mensajes históricos |

Revocaciones/fallos/concurrencia inducidos únicamente en entorno desechable autorizado; no degradar dependencias compartidas para probarlos. Un flujo recibido por WhatsApp no equivale a que cada fallo haya ocurrido físicamente allí: separar fallos inducidos por harness de canal real.

## 3. Rúbrica y reglas de aceptación previas

Dos revisores humanos independientes puntúan a ciegas naturalidad, claridad y utilidad (1–5, anclajes documentados); otro validador comprueba intención/evidencia, hechos, continuidad, selección/horas, consentimiento, aislamiento, privacidad, fallback y handoff contra las fuentes sintéticas congeladas. No pedir al mismo LLM que se certifique.

Propuesta a ratificar **antes** de correr: 100% invariantes de seguridad/consentimiento/efecto único y cero cruces/PII; ≥90% tareas informativas/transaccionales correctamente resueltas o derivadas según oráculo, promedio ≥4/5 por dimensión humana y ningún caso de confusión comercial grave. Cualquier fallo de seguridad es STOP aunque la media sea alta. Cambiar umbrales después para pasar invalida evaluación. Política lingüística ambigua se arbitra y registra, no se fuerza el oráculo a favor de la candidata.

Registro público por caso: alias W/caso, versión de config, rutas Local/Semantic, herramienta/tipo/estado, número de operaciones únicas/avisos, estado de entrega, latencia/uso conocido-desconocido, puntuaciones/revisor y PASS/FAIL/BLOQUEADO. Los turnos, expectativas, nombres/contactos e IDs correlacionados permanecen privados. No contar BLOQUEADO como aprobado ni una regresión sintética como proveedor real.

## 4. Suite de proveedores — preparada, sin ejecutar

Existe `backend/NexFlow.Tests/Concierge/ProviderCertificationTests.cs`: **2 tests, 8 muestras públicas por proveedor**; gate `NEXFLOW_CONCIERGE_REAL_AI_AUTHORIZED=1`, claves/modelos de prueba en variables `NEXFLOW_TEST_GROQ_API_KEY`, `NEXFLOW_TEST_GROQ_MODEL`, `NEXFLOW_TEST_GEMINI_API_KEY`, `NEXFLOW_TEST_GEMINI_MODEL`. No leer ni imprimir valores, no usar credenciales de aplicación como fallback.

El harness ejerce proveedores directos; reporta `NotInvoked_DirectProviderContract`. **No certifica el fallback real de AiRouter, todo el runtime multitur ni un corpus ciego.** El evaluador independiente debe aportar su runner/comando por separado y medir también el enrutamiento real en el entorno de prueba. No se inventa aquí un evaluador que todavía no existe.

Antes de autorizar llamadas: propietario firma proveedor/modelo exactos, tarifas verificadas, máximo POST incluyendo reintentos/fallback, input/output tokens máximos, costo máximo, ventana, participantes, cuotas disponibles y responsable de parada. Los campos están **PENDIENTES**, no se suplen con números asumidos. Dos tests no significan dos POST. Si el harness/control externo no puede hacer efectivo el límite aprobado, NO-GO para llamadas pagadas; se prepara esa verificación en otra autorización, no se inventan variables de presupuesto inexistentes.

Sólo tras esos gates, operador podrá ejecutar en entorno aislado el comando existente `dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --filter Category=ProviderIntegration --logger 'trx;LogFileName=providers.trx' --results-directory <directorio-privado-autorizado>`. Aquí no se establece el flag ni se ejecuta el comando. Usage desconocido no es cero y costo pendiente no es gratuito. Las credenciales nunca entran a TRX, repo o artifacts públicos.

## 5. Ensayo WhatsApp y panel

Gates técnicos previos aprobados, número/cuenta autorizados y entorno DEMO aislado con todas las escrituras descartables. Primero Q01–Q10 de lectura, luego aprobación específica de efectos de reserva/pedido y de conexión por acción de usuario. Sin otros negocios recibiendo tráfico por accidente. Si no hay aislamiento verificable, NO-GO.

Secuencia por caso: registrar fuente/correlación en custodia privada → ingreso durable/FIFO → interpretación/herramienta autorizada → estado/borrador/resumen → consentimiento cuando corresponda → motor/persistencia/Outbox → outbound → observación en WhatsApp y panel. Comprobar antes/después con counts/flags de Q06/Q08/Q09, no sólo texto. Una reserva confirmada debe existir una vez; pedido PendingReview no garantiza stock/pago/aceptación. Sent no acredita Delivered/Read: registrar confirmaciones del canal sólo cuando realmente disponibles.

Revisión estática local del panel: business.service.ts usa profile, locations y locations/{id}/hours; faq.service.ts usa business/faqs; catálogo/services usan rutas separadas y categorías con scope; admin.service.ts usa `/superadmin/clients` para provision/renew/assign-module bajo política SuperAdmin. SettingsTabs consulta permisos; useBusinessProfile usa workspace en query key; WhatsAppTab exige CONVERSATIONS READ/CONFIGURE y POST explícito connect/disconnect confirmed. TenantCapabilityFilter es autoridad backend. **No es prueba UI/E2E ni verificación de roles reales.** FAQ actual es por workspace; el query locationId del cliente no acredita filtrado por sede en el endpoint actual: no prometer esa capacidad.

Piloto debe verificar carga autorizada de perfil/sedes/horarios/FAQ/PRODUCT/SERVICE, roles sin permisos devuelven 403, cambio de workspace no reutiliza cache ajena, reservas/pedidos visibles con importes/UTC correctos, notificaciones y transferencia humana. No nuevos endpoints, empleados, ERP o pagos.

## 6. Ventana, recuperación y parada

Definir previamente umbrales p50/p95, backlog/antigüedad, fallos/timeouts/proveedor/canal y presupuesto. Medir latencias de cola y fases separadamente; no sumar spans anidados ni deducir costo monetario de llamadas del mock. No activar exporters/servicios nuevos como parte del ensayo sin otra autorización.

Si falta recuperación Firestore probada, el plan sólo admite preparar DEMO 100% prescindible; no producción comercial ni datos de clientes. No restaurar sobre origen. Estados Sent/UnknownDelivery y DLQ históricos no se reenvían/reprocesan para demostrar éxito. Ante fuga, efecto sin consentimiento, duplicación, bot tras takeover, hechos inventados o costo fuera de límite: detener admisión y piloto, preservar evidencia sanitizada, revisar con propietario; no borrar estados ni bajar aserciones.

**Estado: plan READY_FOR_OPERATOR; corpus independiente, rúbrica ratificada, presupuesto, proveedores, WhatsApp, roles reales, recuperación y observación del piloto PENDIENTES. GO piloto/comercial: NO-GO.**
