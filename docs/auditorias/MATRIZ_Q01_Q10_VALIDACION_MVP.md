# NexFlow MVP — matriz Q01–Q10, datos/PDF y recuperación

2026-10-26; código local `be07f89c2146f7c4344effad849e1335c9614e86`. Ninguna consulta de este documento se ejecutó sobre servicios o datos reales. Referencia: [checkpoints S6–S8](IMPLEMENTACION_S6_S7_S8_MVP_2026-10-26.md).

## 1. Reglas de lectura

Operador autorizado, rol de sólo lectura y workspace de prueba confirmado fuera del chat. Parámetros enlazados; timeout 5 s por consulta, ventana UTC y hasta 20 fuentes de prueba; páginas de 100 documentos con límite total aprobado. Los IDs/cursores reales permanecen sólo en el entorno privado del operador. Mostrar alias consistentes, conteos, booleans y datos comerciales públicos aprobados. No exportar nombres/teléfonos de consumidores, prompts, contexto/payload bruto, claves, errores brutos ni URLs firmadas.

No arrancar el host API/worker para hacer consultas. **No usar GET /api/catalog/artifact ni CatalogGenerationService.GetArtifactAsync**: :116–139 puede expirar generación o guardar STALE. FirestoreCatalogArtifactRepository.GetCurrentArtifactAsync:45–55 sólo lee; preferir GetSnapshot SDK directo, sin reconciliación. Tampoco usar GetOutbound/TryResumeResponse que pueden normalizar o reanudar trabajo. Los GET de estado WhatsApp no sustituyen lectura inerte del documento.

Los nombres de SQL/enum siguientes coinciden con el modelo local, no con un esquema Oracle inspeccionado. Antes de ejecutar, operador contrasta `information_schema.columns` por tablas permitidas y metadatos EF, sólo lectura; ante discrepancia, parar. Ningún SQL es autorización para migrar.

## 2. Matriz de evidencia

| ID / fuente de verdad | Evidencia local y proyección permitida | Requiere operador / método exacto | Decisión del propietario | Estado REAL |
|---|---|---|---|---|
| Q01 imagen/runtime | Dockerfile:20–34 no-root/chown; migraciones sin diff vs306d7c8 | Inspect con formato limitado de imagen/id/user/labels Compose, stat sin cat; smoke externo según checklist | Aprobar revisión/digest/ventana | Local estático OK; imagen activa/ARM64 PENDIENTE |
| Q02 licencia/capacidades | EntitlementService:58–96; License.IsValidAt:97; TenantCapabilityFilter:50–115. Licencia y módulos no son permisos web por sí solos | SQL A; roles/capacidades efectivos mediante proyección administrativa existente, sin identidades de miembros | Módulos/roles DEMO y revocaciones a probar | Tests locales OK; efectivos reales PENDIENTE |
| Q03 PRODUCT/SERVICE/sedes | OfferingService:25–93, CatalogGenerationService:88–106; pruebas cinco/cuatro y shared-store | Query catalogItems WHERE Type=PRODUCT/SERVICE; SELECT campos públicos aprobados: Type, Name, CategoryId, IsActive, RequiresReservation, DurationInMinutes, LocationScope, LocationIds, PriceMinorUnits, Currency; categorías/sedes por W | Autorizar datos públicos/aliases y límites | Fixtures OK; razón por ítem real NO VERIFICADA |
| Q04 artefacto PDF | CURRENT y Origin/SourceHash/GenerationId/callback cubiertos por Catalog* | GetSnapshot de `workspaces/W/catalogArtifacts/current_service` y `current_product`; devolver estado/origen/update-time/edad/generación alias/hashMatches, no URL privada | Aprobar documento visual y ámbito | Metadatos locales OK; bytes/template real PENDIENTE |
| Q05 jornada/zona | KnowledgeService:150–178; ReservationEngine y ReservationsPeruTimeTests. Global ≠ sede | GetSnapshot `business/profile`→sólo TimeZone; `hours/global/schedule` y `hours/S/schedule`→DayOfWeek/OpenTime/CloseTime/IsClosed | Calendarios/fechas DEMO y política de disponibilidad | Fixtures OK; configuración real PENDIENTE |
| Q06 continuidad | Contexto Firestore autoritativo; referencia 10 min y huellas/CAS; Redis caché | GetSnapshot `conversationStates/<contacto sintético autorizado>`; procesar contextJson internamente sin imprimir; salir goal/step/flags de campos, fecha/hora de prueba, tipo/validez de pregunta, edad de referencia, versión alias | Alcance y retención de diagnóstico | Tests OK; estado real NO VERIFICADO |
| Q07 ruta/uso IA | UnderstandingRoute, DialogueDecision, KnowledgeQuery Reason=Topic, Outcome/ElapsedMs y AiTelemetry | Traces estructuradas de ventana/correlación de prueba; proyectar sólo nombres de fase/tipo/status/latencia/usage/fallback. No grep indiscriminado de mensajes o prompts | Modelos, presupuesto y umbrales | Instrumentación local OK; uso/costo real PENDIENTE |
| Q08 reserva y Outbox | ReservationEngine:201–239 y source idempotente; SQL B | W+SourceMessageId de prueba; retornar count/expected_slot/event_count/key_matches, sin IDs o DTO privado | Escritura de prueba futura consentida | Fixtures OK; fila real y entrega NO VERIFICADAS |
| Q09 inbound/outbound | PostgreSQL inbound y Firestore outbound/messageOrigins; UnknownDelivery sin reenvío | SQL C; lectura SDK de hasta 3 IDs de respuesta derivados, SELECT status/kind/timestamp/transportStartedAt/transportLeaseUntil; no normalize/resume | Ventana, permiso de lectura y manejo manual de incertidumbre | Tests OK; colas/transporte real PENDIENTE |
| Q10 fiscal/plantilla | TaxId en payload (CatalogGenerationService:101); perfil público lo omite (KnowledgeService:194–202) | Revisión privada por propietario de campos de plantilla externa y PDF público de prueba suministrado; atestación sanitizada, nunca workflow con secretos | **OMITIR por defecto lo no autorizado**, aprobar explícitamente la política antes de cambiar contrato/template | PENDIENTE; publicación comercial bloqueada |

Cada Q requiere: alias de W, SHA/digest aplicable, instante UTC, método/ventana, número de filas/documentos leídos, booleanos, responsable/revisor y localizador privado de evidencia. No anexar resultados privados al repositorio. No rellenar huecos con una captura de health.

### Paginación Firestore, sin ejecutar

Cliente servidor autorizado ya configurado fuera del repo, sin credenciales en este documento. Para `catalogItems`, usar WHERE Type exacto, OrderBy document ID, Limit(100), StartAfter último snapshot; SELECT sólo campos de Q03. Mismo patrón en categorías/locations. No descargar todo y luego limitar a cinco. La lista publicada se reconcilia internamente con todos los candidatos autorizados; el informe sólo muestra las cinco filas públicas relevantes y razones, no todos los documentos del workspace. Si falta índice o hay cuotas insuficientes: detener, no crearlo en esta tarea. No afirmar que paginar limita lecturas a las cinco filas mostradas.

Q04: leer snapshot de ambos ámbitos sin guardar estados. Calcular hash desde la misma proyección/orden/serialización local de CatalogGenerationService.GetContentAsync y CatalogHashService en ventana consistente; no hash del PDF ni JSON improvisado. La comparación puede hacerse internamente devolviendo sólo hashMatches. Aun coincidente, se necesita evaluación visual/contenido del documento.

### SQL propuesto — cliente Npgsql con parámetros, NO ejecutado

Sesión dedicada de lectura, transacción READ ONLY (preferiblemente REPEATABLE READ para correlacionar la ventana); `SET LOCAL statement_timeout='5s'`, terminar ROLLBACK. Esquema local: estado License/Workspace/Module/Reservation string; Inbound/Outbox enteros. Nunca imprimir cadenas de conexión ni PayloadJson.

```sql
-- A / Q02. @workspace_id uuid; @now timestamptz.
SELECT COALESCE(w."Status" IN ('Active','Pending') AND l."Status"='Active'
 AND l."ValidFrom"<=@now AND (l."ValidTo" IS NULL OR l."ValidTo">=@now),FALSE) AS valid,
 ARRAY_REMOVE(ARRAY_AGG(m."Code" ORDER BY m."Code") FILTER (WHERE m."Status"='Active'),NULL) AS assigned_active_modules
FROM "Workspaces" w LEFT JOIN "Licenses" l ON l."WorkspaceId"=w."Id"
LEFT JOIN "LicenseModules" lm ON lm."LicenseId"=l."Id"
LEFT JOIN "Modules" m ON m."Id"=lm."ModuleId"
WHERE w."Id"=@workspace_id GROUP BY w."Status",l."Status",l."ValidFrom",l."ValidTo";
-- Si valid=false, códigos asignados NO son capacidad efectiva.

-- B / Q08. @source_id sólo de un mensaje sintético autorizado.
WITH r AS (SELECT "Id","Status","LocationId","ServiceId","StartTime","EndTime"
 FROM "Reservations" WHERE "WorkspaceId"=@workspace_id AND "SourceMessageId"=@source_id)
SELECT (SELECT COUNT(*) FROM r) AS reservations,
 (SELECT COALESCE(BOOL_AND("Status"='Confirmed' AND "LocationId"=@location_id
  AND "ServiceId"=@service_id AND "StartTime"=@expected_utc AND "EndTime"=@expected_end),FALSE) FROM r) AS expected_slot,
 (SELECT COUNT(*) FROM "OutboxMessages" o JOIN r ON o."PayloadJson"::jsonb->'Data'->>'Id'=r."Id"::text
  WHERE o."WorkspaceId"=@workspace_id AND o."EventType"='RESERVATION_CREATED') AS event_count,
 (SELECT COALESCE(BOOL_AND(o."PayloadJson"::jsonb->>'IdempotencyKey'='res_'||r."Id"::text),FALSE)
  FROM "OutboxMessages" o JOIN r ON o."PayloadJson"::jsonb->'Data'->>'Id'=r."Id"::text
  WHERE o."WorkspaceId"=@workspace_id AND o."EventType"='RESERVATION_CREATED') AS key_matches;

-- C / Q09. Limitar @test_sources a <=20 identidades de prueba.
SELECT "Status",COUNT(*) AS messages,MAX("Attempts") AS max_attempts,
 AVG(EXTRACT(EPOCH FROM ("ProcessedAt"-"ReceivedAt"))) AS processed_seconds
FROM "InboundMessages" WHERE "WorkspaceId"=@workspace_id
 AND "ReceivedAt">=@from_utc AND "ReceivedAt"<@to_utc
 AND "ExternalMessageId"=ANY(@test_sources)
GROUP BY "Status" ORDER BY "Status";
-- Inbound: 0 Pending, 1 Processing, 2 Processed, 3 Failed, 4 DeadLetter.
```

Para outbound, SHA256 UTF8 en mayúsculas de `$"{W:N}:AI:{source}:response:1"` o `$"{W:N}:AI:{source}:response:document:{scope}:1"`, scope PRODUCT/SERVICE; GetSnapshot de `workspaces/W/outboundMessages/{hash}`. No exportar source/clave/contexto/contacto. Contar origen/estado, no usar IdempotencyKey como permiso para reenviar. Sent sólo significa confirmación del proveedor según contrato, no Delivered/Read del consumidor.

## 3. Cinco publicados/cuatro reservables: evidencia local, no datos Oracle

Prueba existente `CatalogGenerationTests.Mvp_public_catalog_and_reservable_services_use_distinct_real_filters` en CatalogGenerationTests.cs:25. Variar una sola causa del quinto, no combinar ni alterar datos reales:

| Ítem sintético | Activo | RequiresReservation/duración | Scope de variante base | Publicado P | Reservable R(sede-a) |
|---|---|---|---|---|---|
| service-0 Asesoría preventiva | Sí | true / 30 | ALL | Sí | Sí |
| service-1 Evaluación odontológica | Sí | true / 30 | ALL | Sí | Sí |
| service-2 Fluorización | Sí | true / 30 | ALL | Sí | Sí |
| service-3 Limpieza dental | Sí | true / 30 | ALL | Sí | Sí |
| service-4 Restauración con resina simple | Sí | false / 30 | ALL | Sí | No: no requiere reserva |

Otras ejecuciones del quinto: true/duración null→publicado pero no reservable; true/30/SPECIFIC sólo sede-b→publicado globalmente y reservable allí, no en sede-a; inactivo→no publicado ni reservable, cuatro/cuatro. Categoría SERVICE activa controlada, minor units/moneda actuales y hora/jornada reales del fixture; ningún flag se corrige para igualar cantidades. Menú vigente: Evaluación opción 2, Limpieza opción 4, ID backend, no índice inferido del PDF.

P filtra tipo/activo/categoría publicable; R(S) añade RequiresReservation, duración ≥5 y scope. No asumir que R(S) siempre es subconjunto de P con datos legacy incoherentes. Q03 debe dar razón por ítem real, no inferir que el quinto fue excluido por el flag del fixture. Generated/Uploaded, legacy Origin, CURRENT/STALE, hash/generación/HMAC/callbacks tardíos se verificaron localmente; el PDF real/plantilla externa siguen NO VERIFICADOS.

## 4. Fiscal y DI productiva

El contrato actual incluye TaxId en BusinessData. No se encontró una plantilla de render PDF versionada en las rutas backend inspeccionadas; N8nWorkflowGateway sólo transmite el envelope al workflow configurado. No se inspeccionó n8n ni bytes reales, por tanto no se afirma que TaxId esté impreso. Propuesta pendiente: **OMITIR por defecto cualquier dato no expresamente autorizado**, definir whitelist y política por el propietario; sólo después un parche autorizado con pruebas de payload/hash/vigencia/callback/legacy. Este encargo no cambia el contrato ni los datos fiscales.

DI Application:34 y :71–82 registra ReservationEngine, KnowledgeService, BusinessReadTools, OfferingService, BookingFlow y OrderFlow reales. Persistence DI: bloque Firestore registra repositorios de perfil/catálogo/FAQ/sede/horas/contexto/pedidos; SQL mantiene repositorios transaccionales. No hay Moq/NexFlow.Tests/InMemory en referencias de los tres proyectos productivos inspeccionados ni sustitución por fixtures en esos registros. **No se reprodujo un mock productivo en estas rutas**. Respuestas deterministas de seguridad/capacidad no son mocks ni datos comerciales inventados. No es una auditoría de cada componente del SaaS.

## 5. Recuperación Firestore / Spark — propuesta realizable, NO implementada ni ejecutada

El plan Spark es información proporcionada, no se consultó la consola. La exportación/importación administrada exige billing/Blaze; backup/PITR/restore administrados no tienen uso gratuito y requieren billing. No se activará ni propondrá ejecutar esa activación como condición silenciosa. Fuentes públicas oficiales consultadas el 26/10/2026: [export/import](https://firebase.google.com/docs/firestore/manage-data/export-import), [cuotas y funciones](https://firebase.google.com/docs/firestore/quotas).

Alternativa propuesta sin nuevo servicio: copia lógica por el SDK servidor ya disponible, con permisos de lectura, cuotas y destino cifrado aprobados. Es un procedimiento propio que requiere implementar/revisar un lector/restaurador de operador y ensayarlo, **no una capacidad de backup ya presente en NexFlow ni sustituto equivalente a snapshot administrado**. SDK permite lecturas y enumeración de subcolecciones en entorno confiable: [documentación de lectura](https://firebase.google.com/docs/firestore/query-data/get-data#list_subcollections_of_a_document).

### Alcance, consistencia y custodia

1. Aprobación posterior específica: W permitidos, tipos de datos, presupuesto de lecturas restante, destino cifrado fuera de repos/GitHub artifacts, claves custodiadas por otro responsable, RPO/RTO y ventana. No copiar cuenta de servicio ni .env. La copia consume lecturas, no se valora como costo cero.
2. Primero ensayo con **datos sintéticos en emulador**; no instalar herramientas nuevas ni ejecutar migraciones en esta intervención. Enumerar los W de la lista autorizada aunque el documento padre `workspaces/W` no exista; subcolecciones pueden existir sin datos del padre. No basta leer la colección workspaces ni el padre para respaldar sus hijos.
3. Allowlist revisada de perfil, categorías/items, sedes/horarios/FAQ, artefactos, orders/requests/notifications, conversationStates (incluido ai-budget), conversations/messages, outboundMessages/messageOrigins y operaciones PDF globales filtradas por WorkspaceId (`catalogPdfUploads`). Enumerar subcolecciones y padres ausentes; cualquier colección no inventariada = backup incompleto, detener. No exportar indiscriminadamente otros tenants.
4. Copiar Values tipados (Timestamp, Reference, bytes, geopoint, int64, arrays/maps/null) con rutas y versiones/update-time; no serializar ToDictionary a JSON genérico suponiendo roundtrip exacto. Manifest de esquema/colecciones, páginas, counts, checksums y comienzo/fin. Objetos PDF externos, índices/reglas y PostgreSQL no quedan respaldados por documentos; inventariarlos separadamente sin copiar secretos ni descargar activos privados no autorizados.
5. Una paginación en vivo no es snapshot atómico. Para copia de recuperación coherente, coordinar parada de admisión/escritores de los W afectados y drenaje (incluido callbacks/operaciones PDF) con el operador, o marcar copia como no consistente y **no certificarla restaurable**. Si no puede aislarse el tenant en este host compartido, no inventar un flag: esperar ventana aprobada o limitarse a DEMO descartable. Sin transacción distribuida PG/Firestore; correlacionar source IDs/versiones/eventos con snapshot SQL de la misma ventana aprobada.
6. Cifrar antes de persistir fuera de memoria; no PII en stdout/TRX/manifiesto público. Propuesta para DEMO sujeta a aprobación: copia cifrada ≤7 días, evidencia agregada ≤30 días y acceso auditado de dos responsables. La retención de clientes reales requiere política expresa; no habilitar TTL cloud ni borrar datos para imponerla. Acreditar destrucción de copias temporales sólo con autorización, fuera de este encargo.

### Restauración aislada y aceptación

7. Destino nuevo/vacío: Firestore Emulator loopback, proyecto **demo-nexflow-rc3**, con guardas IntegrationSafety de IsolatedStores.cs:30–58; cliente de restauración sin credenciales reales, red SaaS denegada y procesos API/workers **apagados**. Validar endpoint/proyecto antes de cada lote de escritura; prohibir destino cloud y restauración in-place. El backup real cifrado no se lleva al emulador local de Codex; un operador protegido usa ambiente autorizado y sólo publica atestaciones.
8. Preservar IDs/source/huellas/estados durables; mapear References sólo dentro del destino según manifest aprobado. No resetear cuotas, Sent/Failed/UnknownDelivery, recibos o source identities para que el ensayo pase. Datos de conversaciones son sensibles: validación sólo de booleans/hashes/estado. Claims Attempting antiguos requieren reconciliación segura, no conversión automática a Pending.
9. Verificar counts/checksums/tipos y referencias/categorías/slots/documentos huérfanos; recrear perfiles/horarios/ofertas; comparar hash de artefacto sin publicarlo; restaurar contexto con CAS y comprobar replay/takeover/no duplicación mediante fakes de transporte existentes. Redis vacío debe reconstruirse desde durable, no restaurarse como autoridad. No activar envíos ni proveedores sobre estados restaurados.
10. Medir duración (RTO), edad/corte (RPO), lecturas y pérdidas conocidas; revisar consistencia PG/Firestore/objetos con el responsable y firmar una atestación privada por copia. Restaurar filas/documentos aislados no autoriza promoción a producción ni reenviar pendientes: requiere reconciliación caso a caso y otro permiso.

**Estado actual: no hay un mecanismo de copia/restauración Firestore probado en esta intervención.** La prueba de Redis sólo acredita semántica de caché, y el respaldo PostgreSQL anterior no se atribuye a este almacén. Sin mecanismo restaurable sólo se puede preparar un piloto con datos 100% descartables; **ventas y uso de datos no descartables NO-GO**. No se tomó backup, no se restauró nada, no se activó billing ni se accedió al proyecto.
