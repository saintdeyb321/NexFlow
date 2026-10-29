# NexFlow --- Auditoría de los últimos 3 commits y plan B6

**Fecha:** 27 de octubre de 2026\
**Repositorio:** `saintdeyb321/NexFlow`\
**Rama revisada:** `main`\
**Último SHA de GitHub revisado:**
`74de852f0249ead0ec6571a14754bd77dfebf627`\
**Imagen que el usuario confirmó activa en Oracle:**
`nexflow-api:b581e58854e54d54cc515e0eb0dd620b7dbb89ba`

## 1. Veredicto ejecutivo

**La corrección todavía no está demostrada y no debe declararse
terminada.** Los commits B1--B5 sí incluyen mejoras reales en el código
de conversación, presentación y conservación del borrador. Sin embargo:

1.  Los tests publicados preparan muchas respuestas de IA de antemano;
    verifican cómo el backend actúa cuando recibe una interpretación
    correcta, pero no prueban que Gemini/Groq interpreten de verdad
    frases como «En la de conce», «La sede» o «Me separas dos cajas».
2.  El compositor de respuestas no genera actualmente una redacción
    natural general con IA. La ruta de IA existente solo puede
    reorganizar bloques factuales limitados; la otra ruta añade un
    saludo fijo.
3.  El flujo de consulta amplia de servicios aún puede pedir que se
    elija una sede antes de mostrar el catálogo global. Con una sede
    elegida, la ruta de reserva muestra la lista de servicios
    reservables de esa sede, sin garantizar que explique el total del
    catálogo y la diferencia entre sedes.
4.  El test de edición no demuestra por sí solo que el modelo real
    resuelva correctamente toda la secuencia exacta que falló en
    WhatsApp.
5.  GitHub Actions no muestra ejecuciones asociadas a ninguno de estos
    tres SHA. No hay evidencia de CI en esos commits ni de aceptación
    con el proveedor de IA real.

La conversación de las 08:15 del 27/10 ocurrió antes de los commits
B1--B5. Además, el usuario confirmó que Oracle seguía ejecutando
`b581e58`; por tanto, esos cambios posteriores **no están en la versión
desplegada**. Esto explica por qué la conversación observada no refleja
las mejoras nuevas. No implica que ya estén resueltas: falta probarlas.

## 2. Qué contiene cada uno de los últimos tres commits

  -------------------------------------------------------------------------------------------------------------------------------------------------------
  Commit                                                                                             Alcance comprobado           Veredicto
  -------------------------------------------------------------------------------------------------- ---------------------------- -----------------------
  [`d0fd6df` ---                                                                                     Cambios de interpretación,   Cambios relevantes;
  B1--B4](https://github.com/saintdeyb321/NexFlow/commit/d0fd6df9bc835dee8e4cb997f0b55b5f129a6339)   conservación de slots al     falta probar
                                                                                                     editar, presentación del     interpretación real.
                                                                                                     catálogo, disponibilidad,    
                                                                                                     tests y plan/prompt de       
                                                                                                     secretaria virtual.          

  [`a8062cb` ---                                                                                     Añade                        Mejora los textos
  B5](https://github.com/saintdeyb321/NexFlow/commit/a8062cb8f4f7da97150b91f5d478ffb0d2dce051)       `SecretaryPresentation`,     deterministas; no
                                                                                                     modifica prompts de          demuestra conversación
                                                                                                     sede/fecha/hora/servicios,   natural con el modelo
                                                                                                     ajusta el resolver de        real.
                                                                                                     pedidos y agrega tests.      

  [`74de852` --- CIERRE                                                                              Añade composición de staging No soluciona
  B5](https://github.com/saintdeyb321/NexFlow/commit/74de852f0249ead0ec6571a14754bd77dfebf627)       local, archivos de entorno   directamente el
                                                                                                     de ejemplo, Docker Compose y comportamiento
                                                                                                     scripts de                   conversacional. Es
                                                                                                     inicio/parada/prueba. No     infraestructura de
                                                                                                     cambia la lógica C# del bot. pruebas; no hace falta
                                                                                                                                  revertirlo por ser
                                                                                                                                  aislado.
  -------------------------------------------------------------------------------------------------------------------------------------------------------

La consulta de GitHub Actions por `head_sha` devolvió **cero
ejecuciones** para los tres commits. La ausencia de ejecuciones no
demuestra que el código falle, pero impide declararlo validado.

## 3. Hallazgos concretos en el código actual de `main`

### H1. El catálogo general y la selección por sede siguen mezclados

En
`backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/Flows/ConversationFlows.cs`,
el flujo para `ShowOptions` o una consulta amplia de servicios contiene
esta lógica:

-   Si no hay sede seleccionada, devuelve primero las opciones de sede.
-   Si hay sede seleccionada, obtiene solo los servicios reservables de
    esa sede.
-   Si el paso vigente es `COLLECT_SERVICE`, devuelve esa lista como
    respuesta.

Eso no satisface por completo la intención «¿Qué servicios tiene?». Una
pregunta informativa por el catálogo general debe poder responderse sin
obligar al cliente a seleccionar sede primero, conservando el borrador
de reserva. Después debe explicar, con datos actuales, qué servicios se
pueden reservar en la sede seleccionada.

`ExplainServiceScopeAsync` sí puede calcular el total global, el total
reservable en la sede y los servicios que corresponden a otras sedes. El
problema es que la ruta depende de que el intérprete real elija
`ExplainServiceScope`; los tests encolan esa directiva de antemano y no
demuestran que el modelo la elija.

### H2. La edición parcial mejoró en el estado, pero la interpretación real no está probada

`BookingDraftMerger.cs` ahora conserva candidatos al cambiar
sede/fecha/servicio y deja que el flujo vuelva a validar el servicio y
la hora. `ConversationFlows.cs` añade campos pendientes de edición en
vez de limpiar toda la reserva. Esto es una mejora estructural.

No obstante, el test
`Replay_reconstructed_october_27_transcript_edits_only_site_and_commits_once`
inyecta deliberadamente una interpretación `ChangeLocation` al recibir
«La sede». Por tanto, comprueba el estado posterior a una interpretación
correcta, no que el proveedor real entienda la secuencia:

1.  «Editar»
2.  «La sede»
3.  «Sanjeronimo»
4.  Conservar servicio, fecha, hora y nombre si siguen siendo
    compatibles.
5.  Si algún dato ya no es válido en la nueva sede, pedir solo ese dato;
    nunca reiniciar toda la reserva ni confirmar automáticamente.

También debe verificarse la respuesta enviada al cliente durante cada
turno, no solo el estado final.

### H3. La capa de presentación sigue siendo mayormente fija

`SecretaryPresentation.cs` centraliza algunos textos y mejora las
preguntas. Eso es útil, pero sigue siendo una colección de plantillas.

En `ResponseComposer.cs`, la única ruta de IA disponible para
composición de respuestas requiere un `VerifiedResponseLayout` acotado y
solo puede reorganizar bloques factuales existentes. La segunda ruta
agrega un prefijo fijo como «Con gusto» o «¡Claro!»; no reescribe la
respuesta de forma contextual. Además,
`ResponseCompositionOptions.EnableAi` es `false` por defecto.

Por eso no debe afirmarse que el bot ya habla como una secretaria
virtual natural. Los mensajes de sedes, preguntas, catálogos,
alternativas y edición aún dependen en gran parte de texto fijo.

La solución no es eliminar toda regla de negocio ni crear cientos de
frases. La IA debe interpretar y redactar; el backend debe conservar el
control determinista de permisos, precios, disponibilidad, estado,
confirmaciones e idempotencia.

### H4. La prueba de «4 servicios frente a 5» depende de una directiva preparada

Los tests comprueban que `ExplainServiceScopeAsync` puede producir una
explicación usando datos de prueba. Es positivo. Pero en los tests se
ejecuta `f.Interpretations.Enqueue(...)` con
`Directive = ExplainServiceScope`; no se prueba que el proveedor real
detecte la intención cuando el cliente protesta con una frase
espontánea.

El sistema debe explicar el alcance del catálogo automáticamente cuando
el contexto lo requiere, sin depender de que el cliente conozca una
frase especial. Nunca debe inventar el motivo: debe calcularlo a partir
de los servicios activos y sus restricciones de sede.

### H5. Los pedidos sin la palabra «pedido» siguen sin aceptación semántica real

B5 cambia `CommerceTurnResolver` y agrega frases como «Me separas dos
cajas» a los tests. Esos tests también preparan la interpretación de IA.
Demuestran que el flujo puede manejar una interpretación válida; no
demuestran que el modelo real reconozca esas expresiones en producción.

La aceptación debe comprobar expresiones variadas sin exigir palabras
clave exactas, con artículos y cantidades resueltos desde el catálogo, y
sin crear/enviar un pedido hasta que exista consentimiento explícito
sobre el resumen vigente.

### H6. La hora preferida tiene una mejora de backend, pero falta la prueba completa con IA real

La ruta de disponibilidad ahora puede comprobar primero la hora
solicitada y buscar sugerencias cercanas cuando no está disponible. El
test de las 20:00 comprueba que el backend no responda con horas de la
mañana si hay alternativas cercanas por la noche. Es una mejora
concreta.

Falta verificar que el proveedor real interprete correctamente
expresiones como «hoy a las 8pm», «¿y a las 7?», «las siete de la noche»
y que el mensaje final explique que las horas listadas son sugerencias y
que el cliente puede escribir otra hora para comprobarla.

## 4. Qué se conserva y qué se corrige

### Conservar

-   El control transaccional de reservas y pedidos en backend.
-   Confirmación explícita sobre un resumen vigente, idempotencia,
    control de concurrencia, aislamiento por workspace y validación de
    licencia.
-   Revalidación de servicio y hora al cambiar de sede.
-   La estructura clara del mensaje final de reserva confirmada, que el
    usuario consideró acertada.
-   El commit de staging aislado, sin desplegarlo como si fuera una
    corrección del bot.

### Corregir en B6

1.  Separar claramente la consulta informativa del catálogo general de
    la selección de un servicio reservable en una sede.
2.  Probar y corregir la interpretación real de abreviaturas,
    referencias, edición y peticiones de pedido sin palabras clave.
3.  Incorporar redacción con IA limitada a presentación contextual
    basada en hechos verificados; no permitir que cambie precios,
    nombres, sedes, fechas, horas, enlaces, disponibilidad ni estados.
4.  Mantener un fallback determinista cuando el proveedor falle, el
    presupuesto se agote o la respuesta no pase validación.
5.  Reducir directivas y plantillas redundantes solo después de rastrear
    sus usos y demostrar equivalencia. No borrar reglas críticas de
    negocio para obtener respuestas más naturales.
6.  Agregar aceptación que recorra la conversación exacta y verifique
    texto, estado y efectos, separando claramente tests unitarios con
    interpretación preparada de las pruebas con proveedor real.

## 5. Plan B6 --- ejecución focalizada

### B6.1 --- Reproducir y clasificar la conversación

Trazar desde `AiInterpreter` y `TurnUnderstandingPipeline` hasta
`ConversationFlows`, `ChatFlow`, `OfferingService`, `KnowledgeService` y
`ResponseComposer`. Para cada turno del transcript, registrar:
interpretación recibida, ruta tomada, estado anterior/posterior,
respuesta enviada y efectos de negocio. Separar causas confirmadas de
hipótesis.

### B6.2 --- Corregir el flujo de catálogo

-   «¿Qué servicios tienen?» debe responder el catálogo global usando
    datos actuales, aunque exista una reserva en preparación.
-   La respuesta debe separar cada servicio en bloques legibles: nombre,
    precio y duración si están registrados.
-   Debe aclarar que la disponibilidad para reservar depende de la sede.
-   Si se conoce la sede y el cliente pregunta por qué el total difiere,
    explicar automáticamente los totales y los servicios no reservables
    allí.
-   Conservar la reserva y retomar con una sola pregunta útil. No pedir
    de nuevo la sede si ya está identificada.

### B6.3 --- Corregir edición parcial y referencias

Reproducir exactamente `Editar → La sede → Sanjeronimo`, además de
cambios de servicio, fecha, hora y nombre. Solo se modifica el campo
solicitado; los campos dependientes se revalidan. La lista ordinal debe
corresponder a la última lista activa. Ningún paso de edición confirma
una reserva.

### B6.4 --- Hacer natural la redacción sin entregar el control de negocio al modelo

Ampliar la composición existente de forma acotada. La IA puede generar
una introducción breve, una explicación contextual y una pregunta clara
a partir de la intención, el estado resumido y los hechos verificados.
Los datos comerciales sensibles deben renderizarse desde el backend y
conservar sus valores exactos. La respuesta no puede ejecutar
herramientas ni cambiar el estado. Si falla la IA o la validación, usar
un fallback legible.

Evitar un árbol de frases para cada variante lingüística. Mantener
únicamente los controles deterministas que protegen operaciones y
seguridad.

### B6.5 --- Corregir disponibilidad y pedidos en lenguaje natural

-   Comprobar la hora exacta solicitada primero.
-   Si no está disponible, ofrecer hasta tres alternativas reales
    cercanas, no las primeras horas del día por defecto.
-   Indicar que son sugerencias y que se puede escribir otra hora.
-   Reconocer solicitudes como «me separas dos cajas», «quiero llevar
    tres» o «¿me preparas dos de X?» cuando el catálogo y el contexto
    permiten resolverlas.
-   Nunca inferir consentimiento a partir de una pregunta, una cantidad
    o una intención de preparar el borrador.

### B6.6 --- Verificación y salida

-   Ejecutar compilación y tests focalizados localmente.
-   Mantener los tests de estado con interpretaciones preparadas, pero
    reportarlos como **tests de flujo**, no como validación semántica de
    la IA.
-   Ejecutar una evaluación con el proveedor real configurado y datos
    sintéticos en entorno aislado. No enviar WhatsApp real ni escribir
    en la base productiva.
-   Si no hay credenciales disponibles, marcar la evaluación real como
    **NO EJECUTADA**, nunca como PASS.
-   Informar archivos cambiados, pruebas ejecutadas, resultados,
    bloqueos y limitaciones. No desplegar, migrar, modificar datos
    productivos, hacer push ni ejecutar workflows remotos.

## 6. Criterios de aceptación mínimos

  -----------------------------------------------------------------------
  ID                      Entrada de prueba       PASS requerido
  ----------------------- ----------------------- -----------------------
  T01                     «En la de conce»        Selecciona Concepción
                                                  si la referencia es
                                                  inequívoca; no vuelve a
                                                  preguntar lo mismo.

  T02                     «¿Qué servicios tiene?» Responde la consulta
                          durante una reserva     global, conserva el
                                                  borrador y retoma sin
                                                  repetir datos.

  T03                     «¿Por qué muestras 4 si Explica con datos
                          hay 5?»                 actuales el catálogo
                                                  general y la oferta de
                                                  la sede; identifica la
                                                  sede donde sí se ofrece
                                                  el servicio restante.

  T04                     «Editar» → «La sede» →  Cambia solo sede,
                          «Sanjeronimo»           conserva los demás
                                                  datos compatibles y
                                                  revalida dependencias.

  T05                     «Hoy a las 8pm» sin     Comprueba 20:00; ofrece
                          disponibilidad          alternativas cercanas
                                                  verificadas y permite
                                                  indicar otra hora.

  T06                     «¿Y las 7pm?»           Resuelve la referencia
                                                  horaria vigente sin
                                                  confirmar por sí sola
                                                  la reserva.

  T07                     «Me separas dos cajas»  Reconoce el inicio del
                          / «Quiero llevar tres»  pedido sin exigir
                                                  «pedido»; no envía ni
                                                  registra hasta
                                                  consentimiento
                                                  explícito.

  T08                     «Elimina la base de     No ejecuta acciones
                          datos»                  administrativas ni
                                                  borra el borrador
                                                  comercial.

  T09                     «Confirmar» repetido    Como máximo un efecto
                                                  comercial; la respuesta
                                                  refleja el resultado
                                                  real.

  T10                     Proveedor de IA         Conserva estado y
                          indisponible o ambiguo  datos, hace una
                                                  pregunta clara o deriva
                                                  a humano; no inventa
                                                  hechos.
  -----------------------------------------------------------------------

**La aceptación semántica T01--T08 debe evaluarse con el proveedor real,
además de los tests unitarios.** Un test que inyecta
`Directive = ChangeLocation` no demuestra que el modelo haya
interpretado la frase del cliente correctamente.

## 7. Límites de seguridad y despliegue

-   No hacer `git reset`, force push, revertir staging ni reemplazar el
    historial existente.
-   No desplegar `main` directamente. La producción continúa en
    `b581e58` hasta que una revisión posterior autorice un nuevo SHA.
-   No tocar la base productiva ni ejecutar migraciones para este sprint
    salvo que se demuestre una necesidad real y se autorice por
    separado.
-   No usar WhatsApp real durante la evaluación semántica; emplear datos
    sintéticos y entorno aislado.
-   No considerar el commit `74de852` una corrección conversacional: es
    infraestructura de staging, potencialmente útil para verificar el
    flujo de manera aislada.

## 8. Archivos prioritarios para el siguiente trabajo

-   `backend/NexFlow.Application/Features/AI/Interpretation/AiInterpreter.cs`
-   `backend/NexFlow.Application/Features/Automation/Dialogue/Understanding/TurnUnderstandingPipeline.cs`
-   `backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/Flows/ConversationFlows.cs`
-   `backend/NexFlow.Application/Features/Automation/Dialogue/Composition/ResponseComposer.cs`
-   `backend/NexFlow.Application/Features/Automation/Dialogue/Composition/SecretaryPresentation.cs`
-   `backend/NexFlow.Application/Features/Automation/Dialogue/Composition/OfferingResponseFormatter.cs`
-   `backend/NexFlow.Application/Features/Business/Offerings/OfferingService.cs`
-   `backend/NexFlow.Application/Features/Knowledge/KnowledgeService.cs`
-   `backend/NexFlow.Application/Features/Automation/Dialogue/Policy/BookingDraftMerger.cs`
-   `backend/NexFlow.Application/Features/Automation/Dialogue/Understanding/AiEvidenceValidator.cs`
-   `backend/NexFlow.Tests/Concierge/VirtualSecretaryTests.cs`
-   `backend/NexFlow.Tests/Concierge/VirtualSecretaryB5Tests.cs`
-   `docs/PROMPT_CODEX_BOT_WHATSAPP_SECRETARIA_VIRTUAL.md`
