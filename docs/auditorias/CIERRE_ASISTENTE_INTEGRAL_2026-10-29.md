# Cierre local del asistente — 29/10/2026

Base local: `7b601bfe4a7cc1aea4cb4c8d8cd28e94580b59c5`. Los únicos archivos pendientes inicialmente eran los dos MD de auditoría/instrucciones; se conservaron. No se consultó GitHub, Oracle ni ningún servicio real. El código modificado todavía no tiene un SHA publicado ni evidencia CI propia.

## H1–H9: dictamen verificado

Rutas bajo `backend/`; líneas de la revisión local final.

| Hallazgo | Verificación y estado final | Solución / riesgo pendiente |
|---|---|---|
| H1 — herramientas por licencia | **PARCIALMENTE CONFIRMADO.** `Infrastructure/Engines/AI/GroqAiProvider.cs:89` no implementa chat con herramientas; Gemini no serializa `tools`. Ya existían `KnowledgeRead` y `AvailabilityRead`. | Se agregó `Application/Features/Knowledge/BusinessReadTools.cs:20`: manifiesto, adaptación `Intent → KnowledgeRead` y autorización comparten una matriz. El intérprete recibe las lecturas habilitadas; ChatFlow reutiliza el adaptador. No se anuncia function-calling nativo ni escrituras autónomas. Extender proveedores queda fuera del parche mínimo. |
| H2 — composición natural general | **CONFIRMADO / PENDIENTE.** `Application/Features/Automation/Dialogue/Composition/ResponseComposer.cs:44` sólo admite organización verificada de bloques y sigue siendo opt-in. | Se conserva el fallback. Verificar sólo cifras o placeholders no garantiza polaridad, ausencia de promesas ni consentimiento. No se implementó redacción libre ni se habilitó una segunda llamada general. Es un límite funcional explícito. |
| H3 — precedencias lingüísticas | **CONFIRMADO, CORREGIDOS DOS CONFLICTOS.** El prompt pedía tanto `ShowOptions` como `None` para catálogos amplios; la limpieza de intención Location eliminaba una edición tipada válida. | `AiInterpreter.cs:114,230`: contrato compacto sin ejemplos por frase; una edición con evidencia se vincula a `EditReservationField` vigente antes de limpiar campos informativos; Broad + ShowOptions conserva la consulta de lectura. No se eliminan otras heurísticas sin reproducción. |
| H4 — llamadas frente a gasto | **CONFIRMADO / PARCIAL.** `FirestoreConversationStateRepository.cs:62` limita admisiones por minuto, no dinero. **Descartada la ausencia de telemetría:** `AiTelemetry.SendAsync/Usage` ya registraban intentos físicos y tokens informados. | Prueba nueva demuestra Groq 503 → Gemini 429 → modelo alternativo 200: tres envíos HTTP interceptados. Se añadió medición de caracteres del contrato/contexto/mensaje sin contenido. Falta presupuesto financiero durable; tokens no informados no equivalen a cero. |
| H5 — prompt/contexto excesivos | **CONFIRMADO / CORREGIDO LOCALMENTE.** Había instrucciones duplicadas/contradictorias y las pistas no se filtraban nuevamente al proyectarlas. | Se unificó el contrato y se filtraron pistas por módulos; datos de pedido sólo para ORDER autorizado y referencias horarias sólo para reserva autorizada. Se mantienen límites, evidencia literal, fechas, validación JSON y control determinista. Calidad semántica real pendiente. |
| H6 — «de acuerdo» | **REPRODUCIDO / CORREGIDO.** Los dos handlers de prueba creaban una reserva/pedido con ese acuse ante una revisión vigente. | `ConversationConfirmation.cs:8` ya no lo acepta como consentimiento. `ConversationRecoveryService.cs:222` responde sin escribir; `AiResponseOrchestrator.cs:302` conserva identidad, fingerprint y expiración de la revisión, sin renovarla. Confirmar después sigue generando un único efecto; pedido permanece PendingReview. |
| H7 — permisos humanos frente al canal | **PARCIALMENTE CONFIRMADO.** `Services/EntitlementService.cs:100` aplica roles humanos; `:200` distingue explícitamente llamada del sistema y usuario HTTP. No se demuestra que el webhook deba heredar un rol humano. | Se formalizó la matriz de lecturas del canal sin ampliar licencias. Reservas existentes siguen exigiendo verificación/toma humana (`ExistingReservationFlow.cs:69`). No se habilitan cancelaciones por activar un módulo. |
| H8 — composición de otras lecturas | **CONFIRMADO / PENDIENTE.** ChatFlow dispone de un ResponsePlan factual común; no todas las lecturas tienen un layout apto para el compositor. | Reutilizado el adaptador de lectura existente, sin crear un segundo envelope. FAQ/perfil/horarios siguen con hechos y fallback; no se declara composición contextual general resuelta. |
| H9 — evidencia semántica real | **CONFIRMADO / PENDIENTE.** | No se invocó Gemini/Groq ni WhatsApp. Las pruebas locales no certifican interpretación, naturalidad o latencia de servicios reales. |

La prueba de pistas no autorizadas construye deliberadamente un contexto con pistas que no corresponden a sus módulos: demuestra defensa insuficiente en esa frontera, **no una fuga productiva observada**. La corrección filtra esa proyección; no sustituye la autorización de ejecución ni convierte su snapshot en permiso permanente.

## Matriz de capacidades del canal

| Entrada semántica / lectura | Licencia exigida | Servicio / efectos |
|---|---|---|
| Profile → Profile | Perfil público; guard de entrada valida workspace/licencia | KnowledgeService; sólo campos públicos |
| ProductQuery → Products | CATALOG | KnowledgeRead; catálogo, sin prometer stock |
| ServiceQuery → Services | SERVICES | KnowledgeRead; catálogo global/sede; no confirma horarios |
| Location → Locations | LOCATIONS | KnowledgeRead independiente |
| BusinessHours → BusinessHours | BUSINESS_HOURS | KnowledgeRead independiente |
| Faq → Faqs | FAQ | KnowledgeRead; revocación bloquea repositorio |
| AvailabilityRead | RESERVATIONS + SERVICES | Motor de reservas, sólo lectura; máximo 5 resultados |
| Reservation | RESERVATIONS + SERVICES | Preparación/motor existente; consentimiento final. Dependencias internas de sede/horarios conservadas |
| Order | ORDERS + CATALOG | Preparación/revisión; consentimiento final; PendingReview, sin pagos/stock garantizado |
| Request | REQUESTS | Flujo existente; no es herramienta de escritura expuesta al modelo |
| Support / citas existentes | Política y verificación actuales | Transferencia humana; sin elevación de permisos |

Los selectores del manifiesto son el `Intent` del JSON existente, no una nueva API de tools. El backend construye la solicitud tipada con el workspace autenticado y comprueba módulos de nuevo antes y después de la lectura. Se mantienen 3 lecturas/8 segundos; no se expone ninguna herramienta de empleados, SQL o escritura directa.

## Diff por archivo

| Archivo | Cambio |
|---|---|
| `Application/Features/AI/Interpretation/AiInterpreter.cs` | Consolidación del prompt, manifiesto, filtrado de contexto y resolución tipada de conflictos |
| `Application/Features/AI/Router/AiTelemetry.cs` | Histograma `nexflow.ai.input.characters` y log de tamaños/versiones sin texto |
| `Application/Features/Automation/Dialogue/Policy/ConversationConfirmation.cs` | Acuse ambiguo excluido del consentimiento |
| `Application/Features/Automation/ProcessMessage/Services/ConversationRecoveryService.cs` | Acuse conserva revisión sin efectos; tampoco se acepta como descripción de REQUEST_DETAILS |
| `Application/Features/Automation/ProcessMessage/Services/AiResponseOrchestrator.cs` | Conservación de revisión vigente tras acuse, sin extender su expiración |
| `Application/Features/Automation/ProcessMessage/Services/Flows/ConversationFlows.cs` | ChatFlow reutiliza el adaptador de lectura, elimina el mapa duplicado |
| `Application/Features/Knowledge/BusinessReadTools.cs` | Manifiesto y matriz compartida; mismos permisos de ejecución |
| `Tests/Concierge/IntegralAssistantTests.cs` | Once casos nuevos reutilizando fixtures, handler y parser reales, incluida precedencia humana y detalles de solicitudes |
| `Tests/AiResilienceTests.cs` | Un caso nuevo de fallback físico y privacidad de logs |

No se modificaron frontend, proveedores productivos, ResponseComposer, motores, persistencia, infraestructura, configuración ni secretos. El diff completo se obtiene con `git diff`; el test y este informe nuevos también deben incluirse al revisar archivos no seguidos.

## Pruebas reales y comandos

Resultados TRX locales en `backend/TestResults/IntegralAssistant/` (salida ignorada por Git):

| Evidencia | Ejecutadas | Aprobadas | Fallidas | Omitidas |
|---|---:|---:|---:|---:|
| `integral-before.trx`: reproducciones antes del parche | 5 | 0 | 5 | 0 |
| `integral-new-final.trx`: regresiones nuevas finales | 10 | 10 | 0 | 0 |
| `integral-related.trx`: suites directamente afectadas finales | 319 | 319 | 0 | 0 |
| `existing-reservations.trx`: gestión de citas existentes | 25 | 25 | 0 | 0 |
| `interpreter-final.trx`: intérprete/adaptadores tras acotar precedencia Support | 94 | 94 | 0 | 0 |
| `request-ack-before.trx`: regresión detectada al separar acuse/consentimiento | 1 | 0 | 1 | 0 |
| `request-continuity-final.trx`: continuidad de pedidos/solicitudes tras corregirla | 45 | 45 | 0 | 0 |

Las 10 primeras nuevas están incluidas en las 319. La revisión acotó la normalización para no sustituir Support/Order/Request y la reverificó con las 94. Al seguir los consumidores del consentimiento se encontró y reprodujo otra regresión: «de acuerdo» pasaba como descripción de una solicitud. Se corrigió reutilizando el clasificador de acuses existente y se verificó con las 45 últimas (incluyen **11 casos de IntegralAssistantTests**). Junto con el nuevo fallback de AiResilienceTests son **12 regresiones nuevas**. La unión por nombre de test de las cuatro suites exitosas contiene **377 pruebas distintas**; las filas se solapan y no deben sumarse. Las 319 preceden a esos dos ajustes puntuales, que se reverificaron en sus suites afectadas. No se ejecutó el gate histórico/release completo.

```powershell
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --filter "FullyQualifiedName~IntegralAssistantTests|FullyQualifiedName~Cross_provider_and_model_fallback_count_three" --logger "trx;LogFileName=integral-new-final.trx" --results-directory backend/TestResults/IntegralAssistant
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --no-build --filter "FullyQualifiedName~IntegralAssistantTests|FullyQualifiedName~AiResilienceTests|FullyQualifiedName~VirtualSecretary|FullyQualifiedName~OrderConfirmationAuditTests|FullyQualifiedName~RuntimeReadToolsTests|FullyQualifiedName~ResponseComposerTests|FullyQualifiedName~RuntimeRealizationTests" --logger "trx;LogFileName=integral-related.trx" --results-directory backend/TestResults/IntegralAssistant
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --no-build --filter "FullyQualifiedName~ExistingReservationSprint2Tests" --logger "trx;LogFileName=existing-reservations.trx" --results-directory backend/TestResults/IntegralAssistant
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --filter "FullyQualifiedName~IntegralAssistantTests|FullyQualifiedName~AiResilienceTests" --logger "trx;LogFileName=interpreter-final.trx" --results-directory backend/TestResults/IntegralAssistant
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --filter "FullyQualifiedName~IntegralAssistantTests|FullyQualifiedName~RuntimeOrderRequestTests" --logger "trx;LogFileName=request-continuity-final.trx" --results-directory backend/TestResults/IntegralAssistant
dotnet build backend/backend.slnx --no-restore
git diff --check
```

Build final PASS, 0 errores y 0 advertencias en esa compilación incremental. La compilación inicial de tests mostró las advertencias preexistentes CS8981 (dos nombres de migración `refactor`) y CS8602 en FirestoreCatalogRepository:228; no se modificaron. `git diff --check`: PASS; Git advierte conversión LF/CRLF en archivos ya existentes, sin errores de espacios.

Separación de evidencia:

- Handler y almacenamiento sintético: conservación, acuse, confirmación posterior, idempotencia, estado PendingReview, catálogo, sede, ordinales, horarios, toma humana y revocación.
- Intérprete real con router interceptado: parser, validación de evidencia, resolución del campo vigente, catálogo de lectura y filtrado de contexto. No equivale al modelo interpretando texto.
- Adaptadores/router reales con HTTP interceptado: fallback, intentos, JSON y telemetría. No hubo tráfico externo.
- Gemini/Groq reales y WhatsApp E2E: **NO EJECUTADOS**.

## Mensajes y consumo

Antes: «de acuerdo» ante una revisión podía terminar en confirmación y escritura. Ahora: «Conservamos el resumen, sin confirmar. Si deseas autorizarlo tal como está, puedes responder “Confirmar”; también puedes indicar un cambio.» El resumen y su expiración originales se conservan.

Antes: una interpretación `Location + ChangeLocation + Correction` de «La sede» perdía el cambio. Ahora se vincula al campo de edición vigente, y la ruta existente pide la nueva sede conservando los datos compatibles.

Antes: el propio prompt pedía `ShowOptions` y `None` para consultas amplias. Ahora catálogo informativo y opciones del paso tienen contratos distintos. Las respuestas verificadas de B6.1 (catálogo, diferencia por sede, alternativas próximas y una pregunta principal) se mantienen; no se atribuyen a una nueva redacción libre por IA.

El segmento fuente de construcción del contrato pasó de 11.182 a 5.585 caracteres antes de los últimos filtros auxiliares; es una medida de plantilla, **no tokens facturados**. La captura real del prompt nuevo para SERVICES, sin borrador ni pistas ajenas, mide **5.727 caracteres**. Se registran tamaños del contrato/contexto/mensaje, nunca sus contenidos. No hay una medida real de ahorro monetario o latencia de proveedor.

La prueba de fallback usa uso sintético 10 input / 5 output / 15 total para la respuesta exitosa. No se interpreta como consumo real ni se asigna coste cero a fallos sin usage. El deadline conversacional de interpretación de 20 segundos, límites de salida existentes y rate limit durable se conservan.

Un límite de gasto requiere una reserva durable previa de presupuesto por workspace/ventana, topes de intentos/modelos, tarifas verificadas y conciliación de usage desconocido sin reembolsos inseguros. El contador actual por mensaje no proporciona ese contrato. No se añadió un ledger ni se inventaron precios/credenciales para simularlo.

## Gate DEMO y pendientes

**GO para preparar la candidata del piloto y revisión local del diff. NO-GO para ejecutar el piloto, desplegar o comercializar con esta evidencia solamente.** Falta congelar/publicar por el propietario un SHA, sus gates y autorización del entorno DEMO existente. H2/H8 y el presupuesto financiero siguen explícitamente pendientes.

Después de certificar y autorizar esa candidata, una conversación supervisada con datos ficticios debe cubrir: (1) «En la de conce»; (2) catálogo global; (3) diferencia 5/4 desde datos vigentes; (4) Editar → La sede → Sanjeronimo; (5) ordinal tras interrupción; (6) Hoy 8pm y ¿y a las 7?, sin selección implícita; (7) «me separas dos cajas»; (8) acuse ambiguo frente a consentimiento explícito; (9) redelivery sin duplicación; (10) asesor/takeover. Comparar texto, interpretación real, borrador y efecto persistido; no sólo capturas ni tests inyectados. No se autoriza ninguna operación remota en este cierre.
