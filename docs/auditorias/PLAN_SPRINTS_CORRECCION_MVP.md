# Plan acotado de corrección del MVP

26/10/2026. Base de diagnóstico: **306d7c8af41aacfd14c71d949da039f628beed95**. **Propuesta para autorización; no se ha iniciado Sprint 1.** No reemplaza ni reinicia las fases F0–F7 de Agent Runtime 4.0.

Fuentes: [auditoría con causas y líneas](C:/Proyectos/NexFlow/docs/auditorias/AUDITORIA_WHATSAPP_MVP_2026-10-26.md), [matriz de datos/evidencias pendientes](C:/Proyectos/NexFlow/docs/auditorias/MATRIZ_DATOS_MODULOS_MVP.md) y [regresiones dirigidas](C:/Proyectos/NexFlow/docs/auditorias/CASOS_REGRESION_WHATSAPP_MVP.md).

## Decisiones que no se vuelven a abrir

- AiRouter y abstracciones Gemini/Groq continúan centrales. Una interpretación propone tipos/evidencia; backend valida tenant, datos y transición. Sin SQL libre, tool loops autónomos ni escritura Firestore por el LLM.
- BookingFlow/ReservationEngine/OrderFlow, PostgreSQL, Firestore autoritativo, Redis cache, CAS, FIFO, recursos, Outbox y entrega incierta se conservan. No un segundo runtime, bot por empresa, estado paralelo o CRM.
- Consulta de disponibilidad no selecciona ni confirma. Un acuse no equivale al consentimiento final. Reserva real sólo después del resumen vigente y revalidación del motor.
- F7-01 sigue protegido: envío requiere cotización revisada con huella exacta; revocar/restaurar licencia no restaura consentimiento. [OrderFlow:107–123](C:/Proyectos/NexFlow/backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/Flows/OrderFlow.cs:107).
- Citas existentes siguen en humano antes de lecturas privadas. No un toggle de verificación, OTP inventado ni eliminación del control. [ExistingReservationFlow:62–72](C:/Proyectos/NexFlow/backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/Flows/ExistingReservationFlow.cs:62).
- ORDERS es solicitud PendingReview; no pagos, inventario, garantía de stock o aceptación comercial automática.
- Corregir con principios/contexto, no una rama por «odontologixa», «vale» o nombre de sede.

## Sprint 0 — Estado de cierre

**Completado documentalmente, pendiente de auditoría humana.** Sólo cuatro Markdown nuevos, sin código/tests/configuración alterados. SHA y diff local revisados; contratos y rutas inspeccionados. No builds ni pruebas ejecutadas en este sprint de sólo lectura. Los conteos históricos de otros SHA no se atribuyen a éste.

Antes de atribuir causas al incidente: Q02–Q09 de la matriz requieren proyecciones públicas/seudonimizadas del operador, sin clientes reales en el repositorio. La ausencia de esos datos no bloquea reproducir localmente las variantes sintéticas; sí bloquea declarar cerrado el incidente productivo.

## Sprint 1 propuesto — Continuidad segura de fecha/hora y selección

**Prioridad P1: A04, reproducción dirigida de A03.** Primer sprint recomendado. No contiene despliegue ni cambio de motor.

### Diagnóstico de partida

- [BookingFlow:132–150](C:/Proyectos/NexFlow/backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/Flows/ConversationFlows.cs:132) conserva consulta fuera del borrador; :548 invita a escribir sólo la hora.
- [Pipeline:162–170](C:/Proyectos/NexFlow/backend/NexFlow.Application/Features/Automation/Dialogue/Understanding/TurnUnderstandingPipeline.cs:162) ya sabe seleccionar fecha+hora de referencia mediante una propuesta explícita; la ruta de hora literal puede carecer de fecha.
- [ConversationQuestion:25–46](C:/Proyectos/NexFlow/backend/NexFlow.Application/Features/Automation/Dialogue/Contracts/ConversationQuestion.cs:25) y [ReservationTimeReference:76–82](C:/Proyectos/NexFlow/backend/NexFlow.Application/Features/Automation/Dialogue/Contracts/TurnUnderstanding.cs:76) condicionan la validez. No permitir que un acuse pierda sin explicación un antecedente aún útil, ni que lo renueve indefinidamente.
- A03 no acredita ausencia total de tolerancia a erratas: [AiEvidenceValidator:21–36](C:/Proyectos/NexFlow/backend/NexFlow.Application/Features/Automation/Dialogue/Understanding/AiEvidenceValidator.cs:21), :95–119 ya ofrece validación acotada. Primero reproducir antes de modificar.

### Trabajo mínimo una vez autorizado

1. Reutilizar fixtures/relojes actuales y reproducir la secuencia sin TargetDate previo: consulta con fecha/hora → acuse → elección explícita → resumen → consentimiento. Registrar qué falla antes del parche.
2. Dar al acuse una continuación basada en la referencia vigente, sin seleccionar ni crear. Una sola pregunta concreta: si desea seleccionar la combinación comprobada. No reiniciar arbitrariamente a fecha ni promover «vale» a confirmación.
3. Consumir la referencia con una elección explícita respaldada; asociar su fecha cuando corresponda, revalidar sede/servicio/observación/versiones y disponibilidad actual. No reutilizar fecha de otra consulta ni un dato vencido.
4. Mantener la distinción entre referencia informativa y resumen final. Actualizar pregunta/intención de forma coherente, sin duplicar estado ni introducir un segundo selector.
5. Reproducir errata y menú en catálogo sintético con cinco/cuatro y posiciones controladas; sólo parchear producción si falla la ruta respaldada. No tocar resolver sano por una salida inválida del proveedor ni reinterpretar «la 4» fuera de su lista.

**Fuera de alcance:** transacciones/índices/recursos, reglas de jornada/inicios libres, nuevos sinónimos, proveedor real, cambio de titularidad, regeneración de PDF o de catálogos, frontend.

**Aceptación:** R08–R17 y R22–R26; cero creación por consulta/acuse/selección sola; cita única después de consentimiento; referencia caduca y no cruza workspace; horario ocupado informa razón real. Servicio elegido coincide con el ID mostrado y el resumen, sin borrar nombre/borrador seguros. Si no se reproduce una variante, se documenta y no se cambia ese componente.

## Sprint 2 propuesto — Información pública, diagnóstico y coherencia de servicios

**A01 P1, A02 P2, A09 P2.** Depende de Q02–Q05/Q07/Q10 y de la revisión de Sprint 1; no necesita modificar motores.

1. Reproducir preguntas equivalentes de perfil con datos públicos y rutas IA simuladas: Profile, General, RequiresClarification, timeout/JSON inválido. [Orchestrator:149–179](C:/Proyectos/NexFlow/backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/AiResponseOrchestrator.cs:149).
2. Si se demuestra desvío, encaminar intención informativa respaldada al KnowledgeTopic existente y conservar una aclaración para ambigüedad real. Fallo de IA, ausencia de dato y falta de licencia deben tener resultados distinguibles. No respuestas generales repetidas que escondan el motivo.
3. Comparar P contra R(S) con snapshot público coherente. No cambiar flags o duración para hacer coincidir cantidades. Una diferencia legítima requiere explicación clara de sede/reservabilidad, no un parche transaccional.
4. Sólo si los datos prueban un defecto de filtro/vigencia, corregir ese filtro y su regresión. Conservar CURRENT, HMAC, GenerationId, SourceHash, reemplazo y ámbito PRODUCT/SERVICE. No eliminar control de vigencia para entregar cualquier PDF.
5. Revisar política TaxId del payload PDF con el propietario. No publicar un campo por estar almacenado; no afirmar que el template lo muestra sin evidencia. Si procede, corregir la proyección mínima en un cambio separado y explícito.

**Aceptación:** R01–R07 y R28; hechos exactos del workspace, cero lecturas FAQ/sedes/horarios sin sus módulos, dependencias internas de reserva autorizadas disponibles. Cinco/cuatro queda explicado por ítem, no por una hipótesis. PDF subido y generado se evalúan según su propia procedencia, sin afirmar validación semántica por hash.

## Sprint 3 propuesto — Preguntas de capacidad y preparación de pedidos

**A05 P1 funcional.** Primero confirmar Q02/Q03 para no reparar una denegación legítima.

- Reproducir «¿se puede hacer un pedido?» y variantes inéditas como preguntas informativas, con CATALOG+ORDERS, sólo servicios, módulo revocado, catálogo vacío y reserva activa.
- Reutilizar CommerceTurnResolver/OrderFlow. [CommerceTurnResolver:23–51](C:/Proyectos/NexFlow/backend/NexFlow.Application/Features/Automation/Dialogue/Understanding/CommerceTurnResolver.cs:23) distingue preguntas; [OrderFlow:81–90](C:/Proyectos/NexFlow/backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/Flows/OrderFlow.cs:81) ya inicia preparación vacía autorizada.
- Añadir sólo la decisión tipada/respuesta de capacidad que falte, respaldada por módulos y catálogo. Una pregunta puede recibir respuesta y orientación; no tiene que crear por sí misma un ORDER. El inicio explícito posterior debe pedir productos/cantidades.
- No compartir consentimientos de reserva/pedido ni conservar una cotización obsoleta tras revocación/precio/sede cambiados.

**Aceptación:** R18–R24/R26 y las pruebas de F7-01 existentes; solicitud única PendingReview con un aviso lógico recuperable, sin duplicados por replay, sin promesa de aceptación/pago/stock. Sólo SERVICES+RESERVATIONS recibe límite claro y vía humana, no catálogo inventado. No reescribir OrderRepository ni la durabilidad existente.

## Corrección de empaquetado propuesta — prerequisito del próximo release

**A08 P1**, independiente de los parches conversacionales. No se pospone como riesgo aceptado de publicación.

- Evidencia: [Dockerfile:20–34](C:/Proyectos/NexFlow/backend/Dockerfile:20) mantiene usuario no root pero COPY no fija propietario/modo para los publicados. `-perms1` es evidencia declarada de corrección externa, no una solución versionada.
- Cambio futuro mínimo: fijar acceso de lectura/recorrido mediante ownership/permisos explícitos del publish, revisando final y migrator. Nada de correr la API como root, permisos 0777 o leer secretos en logs.
- Requiere Q01 y autorización separada para modificar Dockerfile y construir/probar imagen aislada. **Este Sprint 0 no ejecuta Docker.** El target migrator sólo se verifica como artefacto, nunca se ejecuta contra una base real para probar permisos.
- R27: usuario efectivo puede leer configuración restrictiva sin credenciales; smoke ARM64/arranque según harness existente; SHA+digest exactos y diagnóstico sanitizado. No instalar el parche por comandos manuales dentro de cada contenedor como única solución permanente.

## Eficiencia diferida, sólo si se demuestra costo relevante

A10 P2 no justifica una refactorización transversal. Primero cuantificar rutas con [ConversationDiagnostics](C:/Proyectos/NexFlow/backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/ConversationDiagnostics.cs:10) y [AiTelemetry:32](C:/Proyectos/NexFlow/backend/NexFlow.Application/Features/AI/Router/AiTelemetry.cs:32), separando herramientas de documentos físicos, intento HTTP de admisión y resultado de operación de filas únicas. R29 exige las mismas decisiones ante revocación, CAS, replay y takeover.

No hay un sprint de caché adicional autorizado. Si un snapshot del mismo turno elimina una lectura demostrablemente redundante sin perder revalidación, proponer ese cambio concreto. No cachear entitlements entre turnos ni hacer que Redis prevalezca sobre Firestore.

## Validación por entrega autorizada

1. Capturar SHA, estado local y diff propio; preservar trabajo ajeno y corpus/evaluadores históricos.
2. Reproducción dirigida antes del parche y resultados reales después, usando fixtures existentes y datos ficticios. No ampliar el corpus por cada frase ni suavizar aserciones correctas.
3. Para backend modificado, ejecutar `dotnet build backend/backend.slnx --no-restore`; restore sólo si hace falta. Ejecutar pruebas afectadas y regresión local relevante. No frontend si no se toca.
4. Para candidata final autorizada, usar los runners CI actuales sin exclusiones nuevas: vigente sin fallos/omisiones y cobertura mínima aprobada, históricos completos con identidades/motivos/hashes preservados. Determinar conteos mediante ejecución real; las cifras de otra revisión no certifican ésta.
5. Integraciones PostgreSQL/Firestore Emulator/Redis sólo con almacenamiento temporal aislado y autorización expresa cuando impliquen escrituras/DDL. Nada de Oracle, Firebase real ni proveedores pagados por defecto.
6. En futuro piloto autorizado, usar workspace/contactos exclusivamente de prueba, presupuesto y ventana definidos; lectura inicial y luego escrituras consentidas. Comprobar filas, contexto, Outbox y entrega sin reenviar UnknownDelivery ni reprocesar DeadLetters históricos.

## Gates y parada

| Gate | Decisión actual / evidencia que falta |
|---|---|
| Auditoría Sprint 0 | Documentos entregados; revisión independiente pendiente |
| Comenzar Sprint 1 | **NO autorizado todavía**; requiere aceptación de alcance y regresiones |
| Defectos del incidente cerrados | **NO-GO**: no hay parche ni reproducción ejecutada en este sprint; faltan Q01–Q10 pertinentes |
| Nueva candidata técnica | Pendiente: fuentes+diff, pruebas exactas y CI/artefactos del mismo SHA, empaquetado permanente |
| Piloto ampliado/comercial | **NO-GO en esta auditoría**; salud declarada y mensajes reales aislados no sustituyen continuidad, titularidad, evaluación independiente y efectos durables verificados |

Parada inmediata ante operación sin consentimiento, cruce de tenant, fuga privada, reintroducción de F7-01, duplicación o falta de correlación durable. No usar rollback a una revisión que retire controles; tampoco restaurar datos, borrar estados o reenviar mensajes para aparentar recuperación. **Detenerse en Sprint 0 y esperar autorización.**
