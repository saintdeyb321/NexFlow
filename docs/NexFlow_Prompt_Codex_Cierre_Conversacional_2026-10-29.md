# PROMPT CODEX — NEXFLOW: CIERRE CONVERSACIONAL REAL SOBRE EL ZIP AUDITADO

**Modelo recomendado:** GPT-6.1 Sol, esfuerzo alto o extra alto para la implementación; trabajar en el repositorio local `C:\Proyectos\NexFlow`.  
**Adjuntos:** `NexFlow_Auditoria_ZIP_Conversacion_2026-10-29.md` y la conversación del 27/10/2026.

## Misión

Reparar los bloqueos reales de la secretaria virtual de NexFlow. No hacer otra reestructuración ni crear nuevos entornos Docker/staging. Aprovecha B1–B5 existentes. El producto es un SaaS multi-tenant; IA interpreta y redacta contextualizando, pero backend valida y ejecuta. El objetivo es un MVP que se comprenda por WhatsApp, no aumentar la cantidad de tests.

## Paso 0 — proteger estado y confirmar commits

- Ejecuta `git status --short`, `git log -3 --oneline --decorate` y `git show --stat` de los tres commits reales. El ZIP anterior carecía de `.git`, así que confirma qué contiene efectivamente HEAD. No supongas que el ZIP y local son idénticos.
- Preserva cambios sin commit, incluidos archivos de staging. No hagas reset, clean, cherry-pick destructivo, merge, commit ni push.
- Lee `AGENTS.md`, el informe adjunto, el transcript y **solo** los archivos necesarios que el informe señala.
- No hagas una auditoría genérica, no reimplementes infraestructura ni empieces otra fase.

## P1 — catálogo general y servicios por sede

**Hallazgo de código:** `CommerceTurnResolver.ResolveAsync` (rama semantic Broad, líneas aproximadas 35–44 del ZIP) recibe `context.SelectedLocationId`; `AiResponseOrchestrator` enruta una interrupción de consulta por `ChatFlow`; `ConversationFlows.ProcessTurnAsync` maneja ShowOptions/ServiceQuery broad como menú de sede.

Corrige el contrato semántico de alcance para que:

1. «¿Qué servicios ofrecen?» pregunte por el catálogo global, aunque el cliente ya tenga una sede elegida o reserva pendiente.
2. «¿Cuáles puedo reservar en Concepción?» filtre solo los reservables en la sede indicada.
3. «¿Por qué el catálogo tiene 5 y aquí aparecen 4?» genere una explicación verdadera a partir de datos activos; la diferencia puede provenir de sede, activación o capacidad de reserva, nunca de una suposición.
4. La interrupción informativa no borre, reemplace ni reinicie el borrador; la pregunta para retomarlo no contradiga la respuesta.
5. Las páginas posteriores a 5 y el PDF existente sigan siendo accesibles. No prometer un PDF si no existe un artefacto válido.

Reutiliza las estructuras de intención existentes; evita un diccionario de frases para decidir el alcance.

## P1 — edición diferencial y menús vigentes

Recorre y corrige `Editar → La sede → Sanjeronimo` desde un resumen de reserva, preservando servicio, fecha, hora y nombre **cuando sigan siendo válidos** en la nueva sede.

- «La sede» identifica el campo a cambiar, no una consulta de dirección.
- La nueva sede debe verificarse contra el workspace y el servicio/hora deben revalidarse. Si falta validez, solicita solo el dato afectado.
- Al mostrar servicios, «La 1» se interpreta exclusivamente contra el último menú vivo correspondiente a la pregunta activa; jamás contra un menú anterior o de otra operación. No invalides innecesariamente una lista vigente por una consulta lateral.
- Preguntas como «¿y las 7?» solo consultan disponibilidad; la selección posterior debe ser inequívoca y ninguna pregunta confirma la reserva.
- Preserva la confirmación explícita vigente, CAS, idempotencia y takeover humano.

## P2 — redacción realmente útil en WhatsApp

`SecretaryPresentation` y `OfferingResponseFormatter` ya mejoraron ciertas plantillas. No las reemplaces por otras cien plantillas. `ResponseComposer.EnableAi` aparece desactivado por defecto y su ruta actual permite sobre todo agrupar bloques, no una redacción contextual suficiente.

- Diseña una composición mínima de texto natural basada en intenciones y hechos **inmutables** verificados del turno; el modelo puede redactar saludos breves, transiciones y explicaciones, nunca cambiar nombres/precios/duración/sedes/horarios/disponibilidad/estados/enlaces ni autorizar efectos.
- Conserva límites de tokens, presupuesto/entitlement, validación, tiempo máximo, protección multi-tenant, fallback y verificación posterior del texto.
- No habilites IA de salida globalmente sin un contrato acotado de comprobación de datos: si es inviable, deja un fallback legible y declara el límite.
- Ejemplo de sede: «Podemos atenderte en estas sedes: ... ¿En cuál te gustaría continuar? Puedes escribir el nombre, por ejemplo «Concepción».»
- Ejemplo de catálogo: cada servicio en bloque independiente `*Servicio*\nS/ 120.00 · 45 min`, aclarando que la posibilidad de reservar depende de la sede y que hay más servicios si aplica.
- Ejemplo de disponibilidad: «No encontré espacio a las 8:00 p. m. Estas son alternativas cercanas que sí pude verificar: ... También puedes proponer otra hora.»
- Una pregunta útil por turno; no repetir sedes/servicios ya escogidos. Opciones entre «comillas» son ejemplos, no contraseñas ni un requisito de coincidencia exacta.

## P2 — pedidos coloquiales

Conservar la ruta de `CommerceTurnResolver` para peticiones «me separas dos cajas» y sus equivalentes; verificar que el intérprete de IA realmente produce el contrato esperado y la referencia de producto es vigente, sin convertir preguntas o condiciones en operaciones. Nunca crear pedidos antes de confirmación explícita del resumen vigente.

## Validaciones mínimas, sin nuevos entornos aislados

1. Agrega solo regresiones focalizadas para: catálogo global vs sede, diferencia dinámica, edición parcial, referencia ordinal tras interrupción, 20:00 y sugerencias cercanas, pedido natural, consentimiento e idempotencia.
2. Identifica los tests con `f.Interpretations.Enqueue(...)` como pruebas de flujo; no afirmar que certifican a Groq/Gemini.
3. Ejecuta `dotnet build backend/backend.slnx --no-restore` y las suites focalizadas; en la validación final del SHA ejecutaremos los gates preexistentes. No recrees staging ni construyas 500 tests artificiales.
4. Para evaluar semántica real, prepara una matriz corta de 8–10 mensajes y la forma de probarla con el proveedor del propio NexFlow **sin nuevas infraestructuras**. No usar datos de clientes reales. Si no se puede invocar el proveedor de forma segura, marca NO EJECUTADO y deja la matriz lista para ejecutar por WhatsApp DEMO luego de CI y despliegue controlado.
5. Compara respuestas **textuales completas**, estado persistido y efectos comerciales; no basta con que `CurrentGoal` sea correcto.
6. Revisa `git diff --check` y el diff completo. No cambies frontend, workflows, esquemas de datos, migraciones ni infraestructura.

## Criterios de aceptación

- `A1`: «En la de conce» selecciona Concepción y deja de pedir la misma sede.
- `A2`: «¿Qué servicios tiene?» consulta catálogo general sin perder reserva.
- `A3`: «¿Por qué 4 si en total hay 5?» explica causas exactas verificadas y sedes aplicables.
- `A4`: «Editar» → «La sede» → «Sanjeronimo» conserva los demás datos compatibles.
- `A5`: «La 1» reconoce la lista aún vigente; no elige la de una conversación vieja.
- `A6`: «Hoy a las 8pm» prueba las 20:00 y muestra alternativas próximas reales.
- `A7`: «Me separas dos cajas» prepara borrador aunque no diga «pedido».
- `A8`: repetición de «Confirmar» no duplica operación.
- `A9`: la respuesta visible de cada paso es cordial, legible, no contradictoria y con una pregunta principal.
- `A10`: error del proveedor, licencia revocada o takeover mantiene las garantías de seguridad.

## Entrega y límites

- No crear otra instalación Docker, segundo proyecto Compose, despliegue, migración ni nuevo entorno aislado.
- No hacer commit, push, ejecutar workflows ni tocar Oracle/WhatsApp productivo.
- No deteriorar los controles de seguridad ni ocultar fallos con tests relajados.
- Entrega: `HEAD` inicial real, archivos modificados, causa raíz con líneas, pruebas PASS/FAIL/NO EJECUTADO, ejemplos de respuesta antes/después, limitaciones semánticas pendientes y secuencia exacta de validación manual WhatsApp DEMO.
- Detente tras las correcciones locales. El propietario y el auditor revisarán el diff y activarán los gates del SHA definitivo antes del siguiente despliegue.

**Prioridad:** resolver primero H01 y H02 (rutas de catálogo) y H04/H05 (edición/referencias); después la composición. Si el tiempo no alcanza, entrega funcionalmente los P1 con pruebas y deja P2 identificados, no inconclusos ni declarados terminados.
