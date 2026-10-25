# Runtime 4.0 — F2: herramientas de lectura

Base local por lectura de archivos de referencia: `0aa3bfbe87e99008b44fdf7e0704a617899f77df`. No se ejecutó Git ni se certifica árbol limpio/remoto. La aprobación expresa permite F2→F3→F4 tras cada gate local. El informe F1 describe la etapa anterior a esa publicación; G1 está aprobado por el propietario.

## Implementación y contratos

`BusinessReadTools` es una fachada de lectura, sin bucle autónomo, estado persistido, motor ni orquestador nuevos. `BusinessReadTurn` recibe el workspace del backend autenticado y no admite cambiarlo. Los argumentos `KnowledgeRead` y `AvailabilityRead` no contienen WorkspaceId. Cada lectura y su publicación revalidan los módulos actuales. Su contexto efímero tiene máximo tres invocaciones y ocho segundos compartidos; propaga cancelación legítima y limita salidas a cinco opciones/6.000 caracteres factuales. La cuarta llamada no consulta fuentes. No se envían esos resultados completos ni perfiles/historiales al LLM.

| Consulta tipada | Fuente reutilizada | Autorización | Resultado |
|---|---|---|---|
| Profile | KnowledgeService / perfil | Workspace autenticado; proyección pública existente | Nombre, descripción declarada, contacto público, zona. Sin TaxId ni inferencia de rubro |
| Products / Services | KnowledgeService / OfferingService | CATALOG / SERVICES | Offering verificada, precio entero, moneda, tipo, referencias; offset≤500, límite≤5, HasMore |
| Locations | KnowledgeService / sedes | LOCATIONS | Hechos y referencias separadas de preguntas de aclaración |
| BusinessHours | KnowledgeService / calendarios | BUSINESS_HOURS | General/sede y aclaraciones existentes; configuración ausente no significa cierre |
| Faqs | KnowledgeService / FAQ | FAQ, incluida revocación | Respuestas activas, consulta y página acotadas |
| AvailabilityRead | IReservationEngine | RESERVATIONS + SERVICES | Hora local de consulta, slots UTC verificados; nunca crea ni selecciona |

ChatFlow usa la fachada para sus lecturas informativas; sus constructores anteriores conservan compatibilidad. La DI registra la misma fachada con el motor existente. `Profile`, añadido al final del enum de intención, permite que AiInterpreter/AiRouter clasifique preguntas de actividad/contacto sin ampliar vocabularios locales. Las interrupciones Profile conservan borradores y atraviesan la misma autorización/revalidación de respuesta de F1.

La identidad de negocio se proyecta bajo demanda en `PublicBusinessProfile`; las capacidades efectivas provienen de entitlements, no de contenido del negocio. No se provisionan agentes, modelos ni configuración persistida por cliente. No existe política aprobada para publicar RUC: se mantiene excluido de la proyección y se prueba su ausencia en la serialización.

## Cambios y compatibilidad

Aplicación: nuevos `Features/Knowledge/BusinessReadTools.cs`; cambios en `KnowledgeModels.cs`, `KnowledgeService.cs`, `DependencyInjection.cs`, `ConversationIntent.cs`, `AiInterpreter.cs`, `TurnUnderstandingPipeline.cs`, `AiResponseOrchestrator.cs` y ChatFlow dentro de `ConversationFlows.cs`.

Pruebas: nueva `Concierge/RuntimeReadToolsTests.cs`; los mocks de `AssistantConversationTests.cs` y `AssistantLicenseBudgetTests.cs` ahora devuelven fuente/sede correctas y admiten el token de deadline. No se alteraron aserciones. Se consolidó el formateo duplicado de productos/servicios en KnowledgeService. Se mantienen APIs anteriores, texto factual, valores previos de enums, AiRouter, referencias F1 y motores. Frontend e infraestructura sin cambios.

## Validación G2

Artefactos en `backend/NexFlow.Tests/TestResults/agent-runtime-f234/`:

- `f2-directed.trx`: 11/11 pruebas dirigidas iniciales aprobadas.
- `f2-regression.trx`: 1.006/1.009; tres fallos de mocks sin metadatos/tokens del nuevo contrato. `f2-g2.trx`: 1.007/1.009; quedaban dos tokens default. Se conservaron las evidencias y se corrigieron los setups.
- `f2-g2-final.trx`: **1.010 ejecutadas, 1.010 aprobadas, 0 fallidas, 0 omitidas**; incluye la prueba adicional de referencias/cancelación. Incluye regresiones F1 y catálogo; categorías Integration, RedisIntegration y ProviderIntegration excluidas.
- Build `dotnet build backend/backend.slnx --no-restore`: **correcto**, evidencia `f2-build.log`. No restore ni migraciones. El SDK necesitó ejecución local fuera del sandbox, como F1.

Filtro reproducible: `(FullyQualifiedName~Concierge|FullyQualifiedName~Assistant|FullyQualifiedName~Knowledge|FullyQualifiedName~AiResilienceTests|FullyQualifiedName~CatalogChatTests)&Category!=Integration&Category!=RedisIntegration&Category!=ProviderIntegration` sobre `backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore`.

Los tests usan los fixtures previos, KnowledgeService real, reloj/datos sintéticos y proveedores simulados. Verifican dos workspaces, precios/FAQ separados, RUC omitido, revocación antes/durante lectura, límites, páginas, cancelación, ausencia frente a caída de fuente, reservas sin módulos informativos independientes y Profile semántico sin efecto comercial.

## Límites y dictamen

El límite de tres herramientas **no equivale a tres documentos Firestore**: una consulta de horarios puede leer calendario general, sedes y un calendario concreto; algunas fuentes todavía recuperan colecciones antes de filtrar. No se afirma ahorro físico ni latencia/coste de proveedores. La paginación acota la respuesta, no certifica paginación en almacenamiento. Los atajos deterministas anteriores conservan sus propias guardas; no se rediseñaron para forzarlos a usar IA.

Se conserva información ausente/indisponible sin fabricar hechos. No hay RAG, embeddings, llamadas pagadas, SaaS real, cambios de reglas ni corpus privado. La calidad real de interpretación y carga siguen pendientes de pruebas autorizadas.

**G2 local: OK**, sin P0/P1 abierto en las verificaciones realizadas. Se permite continuar a F3 por autorización agrupada. No equivale a GO comercial o de producción.
