# PDF-1.1: concurrencia y recuperación

La cuota diaria de tres generaciones sigue siendo compartida por workspace. Los artefactos PRODUCT y SERVICE son independientes. La reserva escribe el estado GENERATING y el contador diario en **una transacción Firestore**, después de verificar la versión del artefacto y la ausencia de otra generación o upload. Un conflicto rechazado no incrementa el contador. Repetir el mismo GenerationId no consume otro token; los documentos diarios anteriores sin GenerationIds conservan su contador.

Firestore y PostgreSQL **no comparten una transacción**. El Outbox conserva su implementación, payload y ciclo durable existentes. Si el proceso se detiene entre la reserva y el encolado, o PostgreSQL pierde la confirmación del commit, el artefacto conserva GENERATING, GenerationId, cuota y PDF anterior. La API devuelve 503 si no puede confirmar el encolado; se debe consultar el estado antes de reintentar. Un callback válido todavía puede completar una generación cuyo Outbox sí quedó confirmado. La reconciliación existente al leer el artefacto convierte una generación de más de una hora en FAILED. La siguiente solicitud usa una reserva nueva. No hay reembolso: una reserva aceptada pero nunca encolada puede gastar uno de los tres tokens diarios. No se promete entrega entre sistemas ante esa interrupción.

Antes de contactar Cloudinary, una transacción Firestore registra la intención en `catalogPdfUploads` y bloquea el artefacto durante quince minutos. Cada operación usa un public ID RAW único, sin overwrite, dentro de `nexflow/workspaces/{workspaceId}/artifacts/{scope}/`. La publicación del PDF y la marca Committed del registro ocurren juntas en Firestore. El PDF anterior se mantiene hasta esa publicación.

Ante un fallo se intenta limpiar durante quince segundos, con un token independiente de la petición HTTP. La limpieza primero toma un lease y cambia el registro a CleanupPending en Firestore: desde entonces la publicación tardía queda prohibida. Comprueba referencias por URL o public ID en los dos scopes del workspace. Los registros Committed jamás se eliminan mediante este reconciliador, incluso si otro PDF ya los reemplazó; tampoco se recolectan PDFs legacy ni PDFs generados por n8n. Esto evita borrar versiones publicadas que todavía puedan utilizarse.

`CatalogUploadCleanupWorker` consulta hasta cincuenta operaciones vencidas cada minuto. La consulta usa un solo campo indexado (`NextReconciliationAt`); no requiere índice compuesto. Un lease de limpieza de dos minutos permite múltiples instancias. Si Firestore falla, **no se elimina el archivo**: queda pendiente la intención durable. Si Cloudinary falla, se reintenta. Una eliminación confirmada termina la operación solo cuando también se conoce que el upload terminó. Si el resultado del upload es incierto, se mantiene un registro CleanupPending y se repite la eliminación cada treinta minutos, porque el proveedor podría crear el archivo después de devolver timeout y después de un primer `not found`. Un fallo de limpieza se reprograma a dos minutos. Los IDs nunca se reutilizan.

La recuperación depende del retorno de Firestore/Cloudinary y de que al menos una instancia ejecute el worker. No existe atomicidad entre almacenamiento externo y Firestore. Los resultados inciertos conservan registros y generan llamadas periódicas; las versiones publicadas reemplazadas también se conservan. Esta retención deliberada prioriza la seguridad de referencias. Se deben observar los logs `PDF upload reconciliation cycle failed`, `PDF cleanup deferred` y `Upload cleanup deferred`, y el atraso de `NextReconciliationAt`. No borrar manualmente registros pendientes mientras pueda haber solicitudes externas en vuelo.

La suite permanente ejecuta los repositorios reales con un transporte RPC de Firestore en memoria, que simula transacciones en conflicto y confirmaciones perdidas. Cloudinary y el Outbox usan mocks. Las pruebas del callback utilizan exclusivamente SQLite en memoria con funciones de advisory lock simuladas: no prueban locks reales de PostgreSQL. Ninguna prueba contacta Firebase, Cloudinary, n8n ni WhatsApp. La validación contra servicios reales queda fuera de estas pruebas unitarias.

```powershell
dotnet build backend/backend.slnx --configuration Release
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --configuration Release
```

El proyecto de pruebas se ejecuta por separado de la solución de producción para conservar el restore actual del Dockerfile, que copia únicamente los cinco proyectos existentes. No se modificaron Dockerfile, solución ni esquema PostgreSQL.

## Archivos de esta entrega

Todos los cambios están dentro de `backend/`:

- API: `NexFlow.API/Controllers/Business/CatalogController.cs`, `NexFlow.API/Middleware/GlobalExceptionMiddleware.cs`.
- Application: `NexFlow.Application/Abstractions/ICatalogGenerationUsageRepository.cs`, `NexFlow.Application/Abstractions/IFileStorage.cs`, `NexFlow.Application/Abstractions/ICatalogUploadRepository.cs`, `NexFlow.Application/Common/ArtifactDependencyException.cs`, `NexFlow.Application/Features/Business/CatalogGenerationService.cs`, `NexFlow.Application/Features/Catalog/Uploads/PdfUploadOperation.cs`.
- Domain: `NexFlow.Domain/Entities/Catalog/CatalogArtifact.cs`, `NexFlow.Domain/Entities/Catalog/CatalogGenerationUsage.cs`, `NexFlow.Domain/Exceptions/CatalogQuotaExceededException.cs`.
- Infrastructure: `NexFlow.Infrastructure/Gateways/Storage/CloudinaryFileStorage.cs`, `NexFlow.Infrastructure/Persistence/DependencyInjection/DependencyInjection.cs`, `NexFlow.Infrastructure/Persistence/Firestore/FirestoreCatalogArtifactRepository.cs`, `NexFlow.Infrastructure/Persistence/Firestore/FirestoreCatalogArtifactDocument.cs`, `NexFlow.Infrastructure/Persistence/Firestore/FirestoreCatalogUploadRepository.cs`, `NexFlow.Infrastructure/Workers/CatalogUploadCleanupWorker.cs`, `NexFlow.Infrastructure/Properties/AssemblyInfo.cs`.
- Pruebas: `NexFlow.Tests/NexFlow.Tests.csproj`, `NexFlow.Tests/CatalogGenerationTests.cs`, `NexFlow.Tests/CatalogUploadTests.cs`, `NexFlow.Tests/CatalogHttpTests.cs`, `NexFlow.Tests/CatalogCallbackTests.cs`, `NexFlow.Tests/CatalogChatTests.cs`, `NexFlow.Tests/CloudinaryPdfStorageTests.cs`, `NexFlow.Tests/Fakes/CatalogFixture.cs`, `NexFlow.Tests/Fakes/InMemoryFirestore.cs`.
- Recuperación y manifiesto: `PDF-1.1-recovery.md`.

No había proyectos de pruebas backend en la solución de referencia. La suite nueva cubre las diez situaciones solicitadas y añade escenarios de confirmación perdida, lease vencido, recuperación tras reinicio, referencia en otro scope, datos legacy de cuota, HTTP y parámetros reales del SDK Cloudinary mediante transporte falso.
