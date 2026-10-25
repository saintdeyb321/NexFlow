# Agent Runtime 4.0 — F7: cierre detenido por consentimiento de pedidos

## Estado e identidad

**F7 INCOMPLETA / NO-GO.** Referencia local `85d349ac5690fbc3dcd3d97b72cf0c3a45e2ff23` en `main`, leída directamente de HEAD/ref. No se ejecutó Git ni se certificó limpieza, remoto o versión en Oracle. Evidencia de esta ejecución: árbol local más una prueba de reproducción añadida. No existe un nuevo commit candidato generado por este trabajo.

Se leyeron AGENTS, plan/prompts Runtime 4.0 y los informes 00–06/04_1. Los documentos previos describen resultados de sus propias etapas; no son resultados ejecutados en F7. La regla expresa de detenerse ante un P0/P1 nuevo prevalece sobre completar las demás verificaciones.

## F7-01 — P1 confirmado y reproducido

**Defecto:** pérdida de la referencia al resumen del pedido tras revocación de licencia durante el commit. Al restaurarse la licencia, un envío explícito posterior puede persistir precios nuevos sin presentarlos ni solicitar consentimiento sobre ellos.

### Reproducción local

Se amplió únicamente `backend/NexFlow.Tests/Concierge/CommerceSafetyTests.cs:163`, reutilizando su fixture `Durable`, OrderFlow, ContextRecoveryService, OfferingService y repositorios Firestore reales sobre el **InMemoryFirestore existente**. No se añadió simulador, fixture ni infraestructura. Las licencias y notificaciones utilizan los mocks ya existentes.

Método: `Reauthorization_after_a_revoked_commit_requires_consent_to_the_updated_quote`.

1. Sembrar producto sintético Pan integral a 250 unidades menores y preparar dos unidades. El resumen contiene `5.00`.
2. Enviar con fuente `revoked-commit`: primera consulta de módulos permite ORDERS+CATALOG; segunda consulta, inmediatamente antes del efecto, devuelve ninguno.
3. Comprobar dos consultas de licencia, cero pedidos y borrador conservado con cantidad dos. Estas aserciones pasan.
4. Restaurar ORDERS+CATALOG y actualizar catálogo a 300 por unidad.
5. Enviar con fuente nueva `restored-commit`, sin una consulta intermedia de resumen.
6. Exigir cero pedidos y nuevo resumen `6.00` con ORDER_CONFIRM antes de crear/notificar. **Falla la primera de estas garantías**: existe una solicitud por 600 unidades menores.

Mensaje exacto del TRX:

```text
A changed quote requires renewed review before creating an order; actual orders=1, total minor units=600.
```

La prueba deja como regresión las comprobaciones posteriores de importe nuevo, paso ORDER_CONFIRM y ausencia de notificación; **no llegaron a ejecutarse** porque la creación indebida ya hace fallar el test. No se presentan como aprobadas. Es reproducción directa del flujo productivo y repositorio sobre SDK fake, no prueba E2E del webhook, Emulator real, Oracle ni WhatsApp. No se afirma incidencia observada en producción.

### Causa raíz y alcance

| Evidencia local | Responsabilidad |
|---|---|
| `backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/Flows/OrderFlow.cs:108–112`, ProcessPlannedAsync/Submit | Compara huella sólo si el paso empieza por ORDER_COMMIT, ORDER_REVIEW u ORDER_CONFIRM. Un paso nulo evita todas esas comparaciones y llama Submit |
| Mismo archivo `:139–144`, función Submit | Guarda checkpoint CAS, revalida módulos y, si se revocaron, persiste CurrentStep=null, perdiendo la huella pero manteniendo ítems |
| Mismo archivo `:148–154` | Construye ExpectedCatalog con el catálogo recién leído y crea el pedido; ese snapshot ya contiene el precio nuevo |
| `backend/NexFlow.Infrastructure/Persistence/Firestore/FirestoreOrderRepository.cs:98–115`, CreateOrderAsync | El fence transaccional compara contra catálogo actual, correctamente coincidente con ese nuevo snapshot. No acredita que ese precio se haya mostrado al consumidor |
| `backend/NexFlow.Application/Features/Automation/Dialogue/Understanding/CommerceTurnResolver.cs:36–56`, ResolveAsync | Envío explícito genera Submit; no exige por sí mismo un paso con huella |
| `backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/AiResponseOrchestrator.cs:236–245` | Delega el efecto al mismo OrderFlow; el control posterior de StateVersion no reconstruye consentimiento perdido |

No es duplicación ni cruce de tenant demostrado. La licencia sí impide el primer efecto, el catálogo se revalida y el pedido sigue siendo PendingReview. **Eso no satisface el consentimiento sobre importes vigentes**, por lo que la clasificación es P1 y bloquea el cierre. No se modificó ninguna de estas clases.

### Corrección mínima propuesta, no implementada

Preservar o restablecer explícitamente el estado de revisión después de una revocación sin efecto durable y exigir una huella de resumen válida antes de cualquier nuevo envío. Si falta o difiere, mostrar resumen actual y exigir un nuevo consentimiento. No convertir la conservación de ORDER_COMMIT en un bloqueo permanente ni impedir recuperar una solicitud ya persistida. Mantener TryResume, CAS, identidad de operación, NotifyOnce y revalidación de licencia/catálogo. No borrar borradores ni recibos.

Regresión necesaria tras autorización: esta prueba hasta verde; precio sin cambios; ausencia/legacy de huella; revocación en ambas guardas; recuperación de commit ya persistido tras revocación; replay, concurrencia, notificación fallida y catálogo cambiado dentro de transacción. Extender al handler real para completar la evidencia de entrada. No requiere un motor nuevo ni migración por este diagnóstico.

## Build y pruebas realmente ejecutadas en F7

Directorio de artefactos: `backend/NexFlow.Tests/TestResults/agent-runtime-f7/`.

| Ejecución | Resultado real |
|---|---|
| `dotnet build backend/backend.slnx --no-restore` | Correcto, salida 0, 0 errores y 3 advertencias preexistentes; `build.log` |
| Prueba de reproducción añadida | 1 ejecutada, 0 aprobadas, 1 fallida, 0 omitidas; `order-consent-reproduction.trx` y `.log` |
| Contrato vigente, históricos completos, F3 v2 y suites adicionales F7 | **NO EJECUTADOS**: parada obligatoria por P1; no son omisiones de la ejecución anterior ni suites aprobadas |
| ARM64, servicios reales, proveedores y WhatsApp | No ejecutados; no autorizados en este encargo |

Comando de reproducción desde raíz:

```powershell
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --filter FullyQualifiedName~Reauthorization_after_a_revoked_commit_requires_consent_to_the_updated_quote --logger 'trx;LogFileName=order-consent-reproduction.trx' --results-directory backend/NexFlow.Tests/TestResults/agent-runtime-f7
```

La primera compilación del test nuevo produjo CS0019/CS1503 al usar Count como propiedad sobre IEnumerable. Se corrigió exclusivamente el test materializando ToArray; la ejecución posterior compiló y falló por la garantía comercial descrita. No se atribuye ese error de preparación al backend. El build de solución anterior a la prueba ya era correcto; la ejecución final de dotnet test recompiló correctamente el proyecto modificado.

Advertencias del build: dos CS8981 de `20260929030327_refactor` y su Designer; CS8602 en FirestoreCatalogRepository:228. Sin restore ni cambio de dependencias. Comandos .NET con ejecución local aprobada fuera del sandbox; sin servicios externos.

La referencia documental F6 es 1084 aprobadas/1096 ejecutadas, con 12 incompatibilidades históricas y 966 casos actuales aprobados, incluidos 12 F3 v2. **No se reprodujo ese resultado en F7 ni se certifica para esta entrega.** El nuevo test rojo amplía el conjunto y no se oculta para conservar una cifra previa.

Hashes de corpus al revisar F7, iguales a 04_1:

- CommercialCertificationCorpus.json: `E9C6311369F4D810B553BA546F3D06E7A4D6B6A901CC892AFEB2ECEBBF1E19F4`.
- QualityHoldout61.json: `3DFE02AC4CC1D6A067D1A875917615A4A3B3B73AB6D72C47EF2D63FE1F7E96E0`.

Los doce fallos históricos registrados exigían lista automática después de una franja, incompatible con F3. Quedan históricos pendientes de repetición, no se confunden con F7-01 ni se cambian corpus/evaluadores.

## Mapa consolidado para continuar la auditoría

Lo siguiente sintetiza la arquitectura documentada y leída en F0–F6; **no sustituye una verificación completa F7**, suspendida en el control de pedidos.

| Capa | Implementación y autoridad existente |
|---|---|
| Entrada | EvolutionWebhookController autentica instancia y resuelve workspace; Inbound SQL deduplica y worker conserva FIFO. No se toma tenant del texto/LLM |
| Estado | ConversationStateService, ContextRecoveryService y FirestoreConversationStateRepository; contexto durable CAS/StateVersion; Redis caché contrastada con durable |
| Continuidad F1 | ConversationQuestion opcional en el contexto existente, vigencia y huella de borrador/opciones; invitación, información, selección y consentimiento diferenciados |
| Comprensión | Recovery/DialoguePolicy/pipeline + resolutores locales y AiInterpreter por AiRouter compartido; evidencia y entidades autorizadas, no ejecución arbitraria |
| Lecturas F2 | BusinessReadTools: perfil público, productos, servicios, sedes, horarios, FAQ y disponibilidad; máximo tres llamadas/ocho segundos, salida limitada, licencia antes/después. No es loop de herramientas autónomo del LLM |
| Reservas F3 | BookingFlow + ReservationEngine existentes: preferencia sin hora inventada, consulta puntual de inicios libres, selección y consentimiento separados. Citas registradas con guard de privacidad/derivación humana |
| Pedidos F4 | OrderFlow/OrderDraftMerger + repositorio existente: borrador vacío/multítem, cotización, solicitud PendingReview, recibo y aviso recuperable. **Garantía de revisión incompleta en F7-01** |
| Respuesta F5 | ResponsePlan + VerifiedResponseLayout/ResponseComposer: bloques literales, agrupación de párrafos con orden fijo; sin paráfrasis libre. Fallback determinista |
| Salida | OutboundMessageService/ledger Firestore para texto y medios; UnknownDelivery sin reenvío ciego. Outbox SQL de eventos de negocio hacia workflow es un mecanismo distinto |
| Medición F6 | AiTelemetry y ConversationDiagnostics: intentos, uso informado/desconocido, rutas y fases; no nuevo almacén ni autoridad de negocio |

El mismo runtime usa datos/entitlements/contexto de cada workspace. No se crea agente, modelo entrenado, motor comercial, contenedor ni estado paralelo por empresa. IA propone intención/entidades/directivas y, opt-in, agrupación de hechos; backend decide permisos, datos, disponibilidad, transiciones y persistencia. No pagos, stock garantizado ni módulo de empleados.

F1 consolidó referencia vigente y algunas puertas de inicio/consulta duplicadas; F2 consolidó lecturas y proyección pública; F6 evitó un Save consecutivo demostrado. Persisten capas de clasificación en Recovery/Policy/Commerce y formateo rígido. Su reducción adicional es deuda, no autorización para refactor en F7. Revalidaciones de licencia/CAS/humano tras esperas son defensas, no duplicación prescindible.

## Calidad, economía y garantías pendientes

Comprensión semántica, continuidad, orientación, presentación y operaciones son ejes distintos. Fixtures de clínica, hotel y tienda y guiones históricos no equivalen a evaluación independiente. Preguntar hora preferida y aceptar nombres inequívocos mejora orientación local; no acredita comprensión de erratas inéditas con proveedores reales. La agrupación F5 no demuestra redacción natural plena. Preguntas de pasos, aclaraciones y resultados siguen siendo deterministas y pueden resultar rígidos; requieren revisión humana.

Una paráfrasis futura necesita hechos/relaciones/alcance/polaridad/condiciones y pregunta referenciada, con un verificador independiente que demuestre preservación semántica, fallback exacto, pruebas adversariales y límites de presupuesto/latencia. Validar números o pedir aprobación al mismo modelo no basta. No se habilita aquí.

P2 pendientes de evidencia: paginación física de colecciones y carga; contención de presupuesto/locks SQL; retención de contexto/PII y métricas; interpretación/proveedores, costo y latencia reales; compatibilidad de rollback y escritores antiguos. Usage ausente no es cero; admisión presupuestaria no es HTTP ni cargo; BusinessResult no cuenta efectos únicos. F6 documenta estas distinciones, pero su inspección y regresión finales F7 quedan pendientes.

No se ha demostrado en F7 un P0, una fuga entre workspaces, reserva duplicada o fallo de takeover; **tampoco se certifica su ausencia** al no completar las suites. P1 abierto: F7-01. P3: deuda documental/presentación a revisar sin parches cosméticos. No inventar un hallazgo productivo de Oracle a partir de estas pruebas.

## Checklist de cierre y archivos propios

- OK: identidad de ref local; lectura documental; build; reproducción localizada con fixtures existentes; evidencia roja conservada; ninguna modificación productiva.
- PENDIENTE: corrección autorizada de F7-01; regresión F0–F6 completa; cierre de matrices de seguridad/costos/naturalidad; runbook exacto y ARM64; versión activa y rollback; backups restaurables; piloto y evaluación independiente.
- Creado: `docs/agent-runtime/07_PILOTO_RELEASE.md` y este documento.
- Modificado: `backend/NexFlow.Tests/Concierge/CommerceSafetyTests.cs`, sólo una prueba nueva.
- Artefactos locales generados: build.log, order-consent-reproduction.log/.trx y salidas normales bin/obj de .NET. Sin frontend, código productivo, configuración, secretos, migraciones o documentos previos modificados por esta tarea.

**NO-GO técnico local y NO-GO de activación/comercial. Preparación del piloto suspendida.** El paso seguro es auditoría del P1 y autorización humana de su corrección mínima; después congelar otra candidata y repetir gates. No avanzar ni desplegar automáticamente.
