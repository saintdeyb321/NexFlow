# Matriz de datos y módulos del MVP

Sprint 0, 26/10/2026. Fuente inspeccionada: **306d7c8af41aacfd14c71d949da039f628beed95**. Datos del workspace del incidente: **NO VERIFICADOS**. No se ejecutaron las consultas propuestas abajo. Los símbolos W, S, contacto y source representan parámetros confiables del operador autorizado, nunca argumentos elegidos por el LLM.

## 1. Fuentes, permisos y exposición

| Dato/operación | Fuente durable y campos relevantes | Autorización conversacional | Uso y límite |
|---|---|---|---|
| Identidad del workspace | PostgreSQL Workspaces.EvolutionInstanceName, estado; licencia/módulos | Instancia autenticada, coincidencia única y licencia válida | Backend decide W. El modelo no recibe autoridad de tenant. [Resolver:27](C:/Proyectos/NexFlow/backend/NexFlow.Infrastructure/Gateways/DefaultInstanceResolver.cs:27), [Guard:36](C:/Proyectos/NexFlow/backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/IncomingMessageGuard.cs:36) |
| Licencia efectiva | PostgreSQL licencia, vigencia, LicenseModules y módulos activos | Relecturas por EntitlementService; no requiere identidad de un empleado para la llamada de sistema | No inferir CATALOG/ORDERS a partir de SERVICES/RESERVATIONS ni del PDF. [GetSnapshotAsync:58](C:/Proyectos/NexFlow/backend/NexFlow.Application/Services/EntitlementService.cs:58) |
| Perfil público | Firestore `workspaces/W/business/profile`: CommercialName, Description, ContactEmail, WhatsAppNumber, TimeZone | Perfil público permitido para canal/tenant válido; no exige BUSINESS_PROFILE/READ de un usuario web | Knowledge devuelve proyección limitada, sin TaxId. Su contenido no se carga entero al intérprete. [Perfil:13](C:/Proyectos/NexFlow/backend/NexFlow.Infrastructure/Persistence/Firestore/FirestoreBusinessProfileRepository.cs:13), [Knowledge:194](C:/Proyectos/NexFlow/backend/NexFlow.Application/Features/Knowledge/KnowledgeService.cs:194) |
| FAQ | Firestore `workspaces/W/faqs`: IsActive, Question, Answer | FAQ antes de lectura en ChatFlow/fachada; revocación debe impedir nuevas lecturas | Búsqueda textual y máximo 5 respuestas; no RAG ni conocimiento de entrenamiento. [Knowledge:183](C:/Proyectos/NexFlow/backend/NexFlow.Application/Features/Knowledge/KnowledgeService.cs:183), [ReadTools:58](C:/Proyectos/NexFlow/backend/NexFlow.Application/Features/Knowledge/BusinessReadTools.cs:58) |
| Productos | Firestore `workspaces/W/catalogItems`, Type=PRODUCT | CATALOG | IsActive, Name, Description, PriceMinorUnits, Currency, LocationScope/Ids, Metadata. Son artículos comerciales, no inventario ni stock garantizado. [OfferingService:25](C:/Proyectos/NexFlow/backend/NexFlow.Application/Features/Business/Offerings/OfferingService.cs:25) |
| Servicios publicados | Misma colección, Type=SERVICE | SERVICES | Activos; puede incluir servicios sin reserva. DurationInMinutes y RequiresReservation no son condiciones generales de publicación. [OfferingService:36](C:/Proyectos/NexFlow/backend/NexFlow.Application/Features/Business/Offerings/OfferingService.cs:36) |
| Servicio reservable en sede S | SERVICE activo, scope ALL o SPECIFIC con S, RequiresReservation, duración ≥5 | SERVICES + RESERVATIONS | ID del workspace y sede existentes; no basta el nombre del PDF. [BookingFlow:594](C:/Proyectos/NexFlow/backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/Flows/ConversationFlows.cs:594) |
| Sedes informativas | Firestore `workspaces/W/locations`: Name, Address, Reference, MapUrl, IsMain | LOCATIONS | Paginación de salida, no permisos de editar configuración. La reserva autorizada puede leer sedes como dependencia interna sin LOCATIONS independiente. [SlotResolver:70](C:/Proyectos/NexFlow/backend/NexFlow.Application/Features/Automation/Dialogue/Understanding/WorkspaceSlotResolver.cs:70) |
| Horario general | Firestore `workspaces/W/hours/global/schedule` | BUSINESS_HOURS para consulta independiente | No configura por herencia automática la agenda de todas las sedes. [HoursRepository:20](C:/Proyectos/NexFlow/backend/NexFlow.Infrastructure/Persistence/Firestore/FirestoreBusinessHoursRepository.cs:20) |
| Horario por sede | Firestore `workspaces/W/hours/S/schedule`: DayOfWeek, OpenTime, CloseTime, IsClosed | BUSINESS_HOURS informativo; SERVICES+RESERVATIONS para dependencia de reservar | Días 0–6; cadena HH:mm. Ausencia ≠ cierre. Motor consulta la sede, no un calendario global inventado. [Knowledge:150](C:/Proyectos/NexFlow/backend/NexFlow.Application/Features/Knowledge/KnowledgeService.cs:150), [Motor:115](C:/Proyectos/NexFlow/backend/NexFlow.Application/Engines/Reservation/ReservationEngine.cs:115) |
| Disponibilidad | Horarios/servicio Firestore + Reservations PostgreSQL + reloj/perfil | SERVICES + RESERVATIONS | Duración, jornada, fechas futuras, ocupación. Devuelve slots UTC; hora informada local del negocio. No un horario inventado por LLM. [Motor:99](C:/Proyectos/NexFlow/backend/NexFlow.Application/Engines/Reservation/ReservationEngine.cs:99) |
| Reserva nueva | PostgreSQL Reservations + OutboxMessages | Licencia anterior, propuesta/fuente/versión y consentimiento sobre resumen vigente | SourceMessageId único por W; Confirmed no es pago. Revalidación de recursos y ocupación al commit. [Motor:159](C:/Proyectos/NexFlow/backend/NexFlow.Application/Engines/Reservation/ReservationEngine.cs:159) |
| Citas existentes | PostgreSQL Reservations/recibos operacionales | **Bloqueadas en bot sin acreditación independiente**, aunque el motor tenga métodos | Handoff antes de leer SQL privado. Teléfono/nombre no son credencial. [ExistingReservationFlow:62](C:/Proyectos/NexFlow/backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/Flows/ExistingReservationFlow.cs:62) |
| Solicitud de pedido | Firestore `workspaces/W/orders/{hash fuente}` + notificación durable | ORDERS + CATALOG, productos activos y cotización/consentimiento | PendingReview, no pago ni aceptación del negocio. Precio/moneda del catálogo; transacción verifica snapshots. [OrderFlow:107](C:/Proyectos/NexFlow/backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/Flows/OrderFlow.cs:107), [OrderRepository:89](C:/Proyectos/NexFlow/backend/NexFlow.Infrastructure/Persistence/Firestore/FirestoreOrderRepository.cs:89) |
| Solicitud transversal | Firestore requests por workspace; RequestService | REQUESTS | Independiente de pedidos y reservas, con notificación/derivación. No convertir cualquier frase «solicito» en pedido. [RequestFlow:721](C:/Proyectos/NexFlow/backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/Flows/ConversationFlows.cs:721) |
| PDF vigente | Firestore `workspaces/W/catalogArtifacts/current_service` o `current_product`; almacenamiento externo | SERVICES o CATALOG según scope al consultar y publicar | Status, SourceHash, GenerationId, Origin, LastGeneratedAt, PersistenceVersion y URL. CURRENT es necesario para envío automático. [ChatFlow:794](C:/Proyectos/NexFlow/backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/Flows/ConversationFlows.cs:794), :839–857 |
| Contexto conversacional | Firestore `workspaces/W/conversationStates/{contacto}`: contextJson + versión | Canal validado, partición por W/contacto, CAS | Borrador, pregunta, opciones, información y referencia horaria; contiene PII, no exportarlo íntegro. Redis no es fuente de verdad. [StateRepository:16](C:/Proyectos/NexFlow/backend/NexFlow.Infrastructure/Persistence/Firestore/FirestoreConversationStateRepository.cs:16) |
| Respuesta durable | Firestore `workspaces/W/outboundMessages` y conversations/messages, origen por mensaje | Scope de conversación, claim y modo humano | Estados Pending/Attempting/Sent/Failed/UnknownDelivery; texto y documentos comparten ciclo. No reenviar UnknownDelivery. [Outbound:41](C:/Proyectos/NexFlow/backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/OutboundMessageService.cs:41) |

Los permisos de usuario web se evalúan por rol/capacidad, no se heredan del bot de sistema. [EntitlementService.HasCapabilityAccessAsync:118](C:/Proyectos/NexFlow/backend/NexFlow.Application/Services/EntitlementService.cs:118) exige pertenencia/capacidad; el inbound autenticado opera con módulos del workspace. No ampliar permisos web para arreglar una conversación.

## 2. Regla formal de cinco frente a cuatro

Para un snapshot de W:

```text
P = ítems Type=SERVICE, IsActive y categoría publicable (o sin categoría)
    del payload de generación PDF global SERVICE.
R(S) = ítems Type=SERVICE, IsActive, RequiresReservation=true,
       DurationInMinutes>=5 y (LocationScope=ALL
       o LocationScope=SPECIFIC con S en LocationIds).
```

P no es por definición R(S). Tampoco se garantiza R(S)⊆P en datos legacy con categorías inconsistentes: el menú no aplica exactamente el filtro de categoría del PDF. No corregir esa posibilidad sin verificar el snapshot.

**A02:** diferencia de conjuntos CE, severidad P2 de coherencia; causa del quinto ausente NV. Evidencias: [GetContentAsync:88–106](C:/Proyectos/NexFlow/backend/NexFlow.Application/Features/Business/CatalogGenerationService.cs:88), [filtro de sede:75–93](C:/Proyectos/NexFlow/backend/NexFlow.Application/Features/Business/Offerings/OfferingService.cs:75), [menú:514–531](C:/Proyectos/NexFlow/backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/ConversationRecoveryService.cs:514). Regresiones R04–R07; propuesta mínima: explicar diferencia legítima o reparar exclusivamente la condición que los datos demuestren incorrecta. Aceptación: razón verificable por ítem y coherencia entre ID mostrado/seleccionado/motor.

### Proyección que falta obtener, sólo por operador autorizado

Una fila por servicio público, **sin información de clientes**:

| Campo | Evidencia necesaria |
|---|---|
| Identidad | Alias técnico de W/S, ID de offering seudonimizado de forma consistente, Name público |
| Filtros | Type, IsActive, RequiresReservation, DurationInMinutes, LocationScope, pertenencia a S, categoría activa/scope |
| Tiempo | UpdateTime del documento y momento del menú/PDF; misma ventana o snapshot coherente |
| Decisión | Entra en P, entra en R(S), razón exacta de exclusión; no sólo conteo |
| Orden | Posición **real mostrada** en PDF y menú, clave de RecoveryOptions de esa posición y huella válida |

Conservar los hechos comunicados: **Evaluación odontológica en posición 2; Restauración con resina simple quinto servicio**. No rellenar el resto de filas con supuestos, no marcar evaluación como ausente, no atribuir automáticamente a restauración el flag excluyente. Valores de todos esos campos: **NO VERIFICADOS** en Sprint 0.

### Vigencia y procedencia del PDF

1. Leer el documento de artefacto sin mutarlo: scope SERVICE, status, Origin, SourceHash, versión, fecha y GenerationId seudonimizado. Origin legacy ausente se interpreta Generated: [ArtifactDocument:29–36](C:/Proyectos/NexFlow/backend/NexFlow.Infrastructure/Persistence/Firestore/FirestoreCatalogArtifactDocument.cs:29).
2. Reconstruir el hash **fuera de producción**, con una proyección autorizada consistente y el mismo orden/serialización de GetContentAsync/ComputeContentHash; no un hash improvisado del PDF. El hash incluye campos de perfil y sedes: no exportar TaxId/contactos para hacerlo público. Alternativa: operador compara internamente y devuelve sólo `hashMatches` y versión.
3. Comprobar las cinco ofertas del **PDF público ya suministrado** contra los ítems del payload de esa generación. No descargar objetos privados ni abrir enlaces firmados desconocidos desde esta auditoría.
4. Generated+hash igual acredita vinculación de solicitud, no que n8n haya renderizado todos los datos correctamente. Uploaded puede ser CURRENT con un hash capturado al subir, pero no existe validación semántica de sus bytes. [CatalogArtifact.ValidateSourceHash:144](C:/Proyectos/NexFlow/backend/NexFlow.Domain/Entities/Catalog/CatalogArtifact.cs:144), [CompleteUpload:116](C:/Proyectos/NexFlow/backend/NexFlow.Domain/Entities/Catalog/CatalogArtifact.cs:116).
5. HMAC/timestamp/hash/GenerationId protegen callback y reemplazo, no certifican contenido: [N8nWebhookController:38–104](C:/Proyectos/NexFlow/backend/NexFlow.API/Controllers/Webhooks/N8nWebhookController.cs:38).

**Atención:** `GetArtifactAsync` puede reconciliar expiración y guardar STALE/CURRENT ([servicio:116–137](C:/Proyectos/NexFlow/backend/NexFlow.Application/Features/Business/CatalogGenerationService.cs:116)). No usar GET del endpoint como diagnóstico estrictamente sin escrituras. Preferir GetSnapshot/SELECT y comparación interna, sin invocar servicios que reparan estado.

## 3. Horarios, zona y memoria

| Concepto | Contrato vigente | Qué no se puede inferir |
|---|---|---|
| Zona | Perfil del workspace; fallback America/Lima según resolver. Booking y motor convierten local Unspecified↔UTC | No asumir configuración efectiva ni usar zona del servidor como reloj del negocio |
| Jornada | Calendario de S para reservar; global es información independiente | No inferir sede cerrada porque falte calendario global; no inventar herencia de horarios |
| Disponibilidad | Inicio libre puntual + duración completa dentro de jornada + no ocupación; cuadrícula es sugerencia | Una hora no listada inicialmente puede ser válida; 10:00 no se presume libre |
| Borrador | Firestore autoritativo, CAS; consulta de disponibilidad no selecciona | QueryDate no es TargetDate: importa en A04 |
| Referencia de hora | Diez minutos desde ObservedAt, workspace y pregunta Information/Reservation válidos | Que siga en JSON no implica que Current la acepte tras renovar pregunta |
| Pregunta tipada | Diez minutos y huellas de draft/opciones; controles extra de consentimiento | No renovar consentimiento por repetir un «vale», cambiar precio o restaurar licencia |
| Contexto lógico | Default 24 h; el borrador vencido pide decisión segura | No borrado físico automático Firestore ni eliminación de reservas |
| Redis | TTL 30 min, clave workspace+contacto; cache hit se contrasta con StateVersion durable | No mantiene la conversación por sí solo ni puede prevalecer sobre Firestore |

Evidencias: [motor:55–83](C:/Proyectos/NexFlow/backend/NexFlow.Application/Engines/Reservation/ReservationEngine.cs:55), [KnowledgeHours:150](C:/Proyectos/NexFlow/backend/NexFlow.Application/Features/Knowledge/KnowledgeService.cs:150), [ConversationQuestion:25–46](C:/Proyectos/NexFlow/backend/NexFlow.Application/Features/Automation/Dialogue/Contracts/ConversationQuestion.cs:25), [referencia:76](C:/Proyectos/NexFlow/backend/NexFlow.Application/Features/Automation/Dialogue/Contracts/TurnUnderstanding.cs:76), [ContextRecovery:34](C:/Proyectos/NexFlow/backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/ContextRecoveryService.cs:34), [Redis:26](C:/Proyectos/NexFlow/backend/NexFlow.Infrastructure/Cache/RedisConversationCache.cs:26).

Costo/escala **NV**: cada recuperación hace lectura durable; saves sucesivos hacen CAS+cache; colecciones se cargan antes de limitar la salida en varias rutas. Máximo tres herramientas no significa máximo tres lecturas físicas. Consultar telemetría agregada existente, no exportar contextJson para contar costos ni estimar tarifas sin usage/medición.

## 4. Paquete de evidencia de sólo lectura pendiente

Ninguna consulta de esta sección se ejecutó. Requiere operador con autorización y canal seguro, parámetros tenant comprobados, límites de tiempo/filas y datos de prueba autorizados. El repositorio sólo recibirá resultados agregados o proyecciones comerciales públicas; **nunca teléfonos, nombres, prompts, texto de clientes, secretos ni PayloadJson/contextJson completos**.

| Solicitud | Lectura mínima y salida sanitizada | Qué discrimina |
|---|---|---|
| Q01 identidad | ID/digest/revisión de la imagen y resumen del diff `-perms1`; UID/GID/modo de `/app/appsettings.json` y directorios, sin contenido | Fuentes iguales vs imagen parchada y causa de permisos |
| Q02 licencia | Licencia vigente y lista de códigos activos de W en la ventana, sin titulares/datos de facturación | ORDERS+CATALOG, SERVICES+RESERVATIONS y permisos independientes |
| Q03 ofertas/sedes | Query Type=SERVICE y Type=PRODUCT en `workspaces/W/catalogItems`, proyección de sección 2; sedes públicas/categorías | Cinco/cuatro, scopes, disponibilidad de productos, duración y alias |
| Q04 artefacto | GetSnapshot `workspaces/W/catalogArtifacts/current_service` y comparación interna de hash; devolver `hashMatches`, Origin, status, versión y tiempos | Vigencia, origen subido/generado y ámbito global |
| Q05 horario | GetSnapshot de `hours/global/schedule` y `hours/S/schedule`, días y rangos públicos; perfil.TimeZone | Fecha local, jornada real, ausencia vs cerrado |
| Q06 continuidad | Para conversación de prueba autorizada, proyección calculada internamente: goal/step, flags de campos, fecha/hora de prueba, tipo/intención/validez de pregunta, tipo/edad de AvailabilityReference y versión seudonimizada | Si query no seleccionó, acuse perdió referencia o se invalidó por cambio legítimo |
| Q07 ruta IA | Ventana acotada de métricas/logs AiHttpAttempt, AiUsage, InterpretationFailure, SemanticEvidence, UnderstandingRoute, DialogueDecision, Clarification y handler; alias consistente de workspace | Proveedor/JSON/presupuesto vs decisión determinista. No capturar prompt o respuesta privada |
| Q08 reserva real | Conteo por W+SourceMessageId con booleanos de servicio/sede/UTC/duración esperados; evento correlacionado sin Data completo | Confirmación durable única y Outbox; no implica entrega WhatsApp/n8n |
| Q09 inbound/outbound | Conteos de estado/intentonas/latencia por contacto de prueba seudonimizado; origen, estado de entrega y bloqueo, sin contenidos | Repetición lingüística frente a replay, cola FIFO o fallo de transporte |
| Q10 publicación | Política aprobada para TaxId y atestación sanitizada de los campos del template PDF | A09: transferencia interna no equivale a publicación aprobada |

Ejemplo SQL **no ejecutado**, parámetros enlazados por el operador; no concatenar identificadores proporcionados por usuario/LLM. Sólo agregados para la reserva de prueba autorizada:

```sql
SELECT COUNT(*) AS matches,
       COUNT(*) FILTER (WHERE "Status" = 'Confirmed') AS confirmed,
       COALESCE(BOOL_AND("LocationId" = @location_id
                        AND "ServiceId" = @service_id
                        AND "StartTime" = @expected_utc
                        AND "EndTime" = @expected_end_utc), FALSE) AS expected_slot
FROM "Reservations"
WHERE "WorkspaceId" = @workspace_id
  AND "SourceMessageId" = @source_message_id;
```

Luego, sin extraer datos personales, relacionar internamente el ID obtenido con `OutboxMessages` del mismo W, EventType=RESERVATION_CREATED y `PayloadJson::jsonb -> 'Data' ->> 'Id'`; devolver sólo cantidad/estados y que `IdempotencyKey=res_{reservation.Id}` coincide. El DTO y envelope están definidos en [ReservationDtos:5](C:/Proyectos/NexFlow/backend/NexFlow.Application/Features/Reservations/ReservationDtos.cs:5) e [IWorkflowGateway:8](C:/Proyectos/NexFlow/backend/NexFlow.Application/Abstractions/Integrations/IWorkflowGateway.cs:8). No ejecutar ni migración, ni motor, ni endpoint de creación para comprobar la existencia.

Revisar índices/EF history sólo como metadatos y sin DDL si el operador necesita certificar el esquema; el código de [ReservationConfiguration:15–23](C:/Proyectos/NexFlow/backend/NexFlow.Infrastructure/Persistence/PostgreSQL/Configurations/ReservationConfiguration.cs:15) no demuestra que esos índices estén instalados en Oracle. El handoff de citas privadas no se elimina para conseguir esta evidencia administrativa.

## 5. Pendientes y aceptación

Q01–Q10: **NO VERIFICADOS** en Sprint 0. Sólo están confirmados los contratos y las consultas que permitirían discriminar las causas. Para cerrar A02/A03/A06 no basta una captura de cinco/cuatro, un mensaje «confirmada» o que la infraestructura responda health. Deben coincidir identidad, snapshot, ámbito, fuente, versión y efecto durable.

Matriz propuesta de aprobación: perfil/FAQ y permisos → R01–R03/R28; datos/PDF → R04–R07; selección → R08–R11; continuidad → R12–R17; pedidos → R18–R21; reservas/privacidad/entrega → R22–R26; release/costo → R27/R29. Especificaciones en [CASOS_REGRESION_WHATSAPP_MVP.md](C:/Proyectos/NexFlow/docs/auditorias/CASOS_REGRESION_WHATSAPP_MVP.md). No se concede acceso productivo mediante este documento.
