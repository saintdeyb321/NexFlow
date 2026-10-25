# F5 — Realización conversacional verificable

Base: `166a1773a02aae4771467ed8fbc73a000d78add8` por ref local, sin Git. Bloque A aprobado; autorización para F5/F6, sin proveedores reales, producción o frontend.

## Recorrido y clases de respuesta

TurnUnderstandingPipeline determina el acto/intención con contexto F1; ChatFlow consulta OfferingQuery validada o BusinessReadTools/KnowledgeService; emite Facts y ResponsePlan. BookingFlow/OrderFlow conservan operaciones y consentimiento. AiResponseOrchestrator guarda pregunta vigente mediante CAS, compone y vuelve a leer StateVersion/modo humano y módulos antes de OutboundMessageService. No se introduce orquestador ni estado paralelo.

| Clase | Política F5 |
|---|---|
| Información pública | Hechos autorizados; agrupación opcional en párrafos, con orden de referencias inmutable |
| Dato faltante | Pregunta del backend; cero llamadas IA para agregar cortesías |
| Aclaración contextual | Referencia F1/ResponsePlan; no pregunta nueva inventada por compositor |
| Consentimiento/resultado/estado | Fallback determinista; nunca libremente reescrito |

## Hallazgo y cambio

El compositor anterior podía gastar presupuesto/router únicamente para anteponer Lead a una disponibilidad. Su diccionario OpeningWords validaba vocabulario, no redacción informativa. Se retira esa llamada y el diccionario; los estilos locales ya existentes siguen sin IA. La prueba anterior de apertura pagada se versiona explícitamente como prueba de ausencia de llamada. Las pruebas adversariales del escritor pasan a un plan factual elegible y mantienen la exigencia de fallback exacto.

`VerifiedResponseLayout` define bloques inmutables con claves opacas, introducción y conclusión obligatorias. OfferingResponseFormatter sólo declara independientes las comparaciones de 2–5 ofertas con hechos estructurados y sin descripción libre, aclaración ni lista amplia. No se fragmentan FAQ, advertencias, negaciones o condiciones por heurísticas de puntuación.

Contrato IA:

```json
{"Version":2,"Paragraphs":[["f0"],["f1"]]}
```

El modelo sólo puede elegir dónde separar párrafos, no escribir texto ni reordenar ofertas. El verificador, independiente del modelo, exige schema exacto, versión y que la secuencia aplanada de claves sea idéntica a la original, todas una sola vez. Rechaza propiedades extra/duplicadas, omisiones, repeticiones, cambios de precio/fecha/unidad en texto libre, preguntas y comandos. Renderiza exclusivamente cadenas del servidor; introducción y conclusión mantienen su posición. Cotas: 5 bloques, 1.000 caracteres por bloque, 3.000 de cuerpo, 512 de JSON. ResponsePlan.WithText invalida la realización si cambia el cuerpo; una interrupción con continuación añadida no reutiliza un layout que ya no coincide.

**Defecto encontrado y corregido durante revisión final:** el prototipo de schema v1 dejaba permutar ofertas, pero OfferingReference conservaba el orden original. `c-reference-order-before.trx` reproduce 1 fallo: se mostraba primero Limpieza profunda y «el primero» consultaba Limpieza dental. No era un fallo del corpus histórico. Se descartó la permutación y se implementó schema v2 con orden fijo, sin mutar el contexto ni añadir otro selector. Se comprueban tanto la agrupación válida como un intento de inversión, y su seguimiento por referencia/precio. La aprobación final depende de la regresión posterior a ese arreglo; el resultado inicial G5 no cubría este riesgo.

El flag existente EnableAi permanece opt-in y apagado por defecto; no se modifica configuración. La elegibilidad exige información pública PRODUCT/SERVICE, hechos no sensibles, ausencia de opciones/datos/pregunta pendiente y cuerpo idéntico al layout original. Revalida licencia/módulo, admite presupuesto idempotente del workspace, limita todo el tramo opcional a 100–2.000 ms (default 800) y usa IAiRouter/ComplexChat existentes, sin bucle/reintento del compositor. Sólo envía bloques públicos necesarios, sin teléfono, workspace, source, resumen personal o historial. Tras IA vuelve a comprobar autorización; el orquestador conserva además CAS, takeover y permisos antes de enviar. Un fallo devuelve FallbackText exacto.

## Implementado frente a propuesta

**Implementado:** agrupación estructural verificada de comparaciones con referencias ordinales estables, eliminación de cortesía IA, preservación literal de hechos/negaciones/condiciones y rutas deterministas para preguntas/resultados críticos. No es paráfrasis informativa libre ni un nuevo catálogo de aperturas.

**Propuesta, no habilitada:** `{claims:[{factId,relation,scope,polarity,conditions}], questionReference}` para expresar semántica. El modelo podría proponer esa estructura, pero la cadena natural requeriría validación independiente de todos los vínculos, alcance de negación, unidades y obligaciones. Comparar números/palabras o pedir al mismo LLM que se evalúe no lo demuestra. Sin un verificador independiente y una evaluación autorizada suficiente, no publicar ese texto: mantener fallback. No se pretende certificar naturalidad plena con agrupación visual de párrafos.

## Evidencia y G5

En `backend/NexFlow.Tests/TestResults/agent-runtime-f56/`:

- `b-composition.trx`: 139 ejecutadas/aprobadas, 0 fallidas/omitidas.
- `b-g5-regression.trx`: **953 ejecutadas/aprobadas, 0 fallidas/omitidas**. Incluye Concierge, Assistant, Knowledge, AiResilienceTests, CatalogChatTests, ReservationsPeruTimeTests y ZeroTokenKnowledgeAuthorizationTests; excluye categorías Integration/RedisIntegration/ProviderIntegration y separa los dos evaluadores históricos íntegros ya ejecutados en A (118/130, doce incompatibilidades conocidas). Incluye doce guiones F3 v2 y regresiones F1–F4.
- `b-build.log`: `dotnet build backend/backend.slnx --no-restore`, correcto, 0 errores/advertencias incrementales. Compilaciones de tests mostraron las tres advertencias preexistentes CS8981/CS8602 documentadas en F4.

Nuevas pruebas del mismo runtime con clínica/hotel/tienda, precios y condiciones intactos, JSON adversarial, preguntas duplicadas rechazadas, schemas extra, intentos de operación, replay sin nueva composición, revocación y takeover mientras el router simulado responde. Se mantienen pruebas de timeout, caída, presupuesto denegado, módulos, mutaciones de precio/hora/cantidad, contexto/referencias, errores de escritura y provider failure. Nada equivale a Gemini/Groq o WhatsApp real.

Archivos productivos: ResponsePlan.cs, nuevo VerifiedResponseLayout.cs, OfferingResponseFormatter.cs, ResponseComposer.cs, ChatResponse/ChatFlow en ConversationFlows.cs, AiResponseOrchestrator.cs. Tests: ResponseComposerTests.cs y nuevo RuntimeRealizationTests.cs. Sin nuevos paquetes, API externa, migraciones, motores, Outbox o infraestructura.

**G5 local: GO para la realización estrictamente verificable descrita.** Paráfrasis/explicación libre y evaluación comercial: PENDIENTE; el fallback es deliberado. La suite histórica original no se declara verde. Puede continuar F6 por autorización agrupada; no se autoriza despliegue.

Continuación F6: se añade AiTaskType.ResponseComposition, conservando valores anteriores y enrutamiento primario Gemini/fallback Groq. Permite separar su uso del ComplexChat genérico en telemetría; los mocks del contrato nuevo se ajustan a ese tipo. La prueba adicional de FAQ/instrucciones, posterior a la batería G5, está aprobada en `c-final-targeted.trx` junto a las doce conversaciones v2 y las pruebas del escritor (19/19).

Cierre posterior al defecto ordinal: `c-reference-order-fixed.trx` 94/94 aprobadas. `c-final-v2-all-with-historical.trx`: 1096 ejecutadas, 1084 aprobadas, los mismos doce fallos históricos y cero omisiones; todos los 966 casos restantes pasan, incluidas referencias tras agrupación y rechazo de inversión. Build final `c-final-v2-build.log` correcto, 0 errores. **Este es el resultado que respalda el GO local final**, no la aprobación inicial del prototipo con permutación.
