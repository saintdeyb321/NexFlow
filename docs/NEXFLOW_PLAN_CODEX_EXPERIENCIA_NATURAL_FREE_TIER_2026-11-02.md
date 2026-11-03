# NEXFLOW — Plan ejecutable para un asistente claro, contextual y sostenible con cuotas gratuitas

**Fecha:** 2026-11-02  
**Destinatario:** Codex trabajando en `C:\Proyectos\NexFlow`  
**Base a verificar:** `39432d7bf870bf5e0ba370738b05e3bd750bc9ab` (último SHA comprobado en `main` y desplegado en Oracle al elaborar este plan).  
**Naturaleza:** especificación de desarrollo local, **no** autorización de producción ni prueba de capacidad real.  
**Objetivo de producto:** clientes que hablan naturalmente en español peruano y entienden la respuesta en la primera lectura; módulos **SERVICES, CATALOG informativo, RESERVATIONS**, con atención humana. **ORDERS y nuevas solicitudes comerciales quedan fuera del piloto**, sin borrar ni modificar operaciones existentes.

## 0. Dictamen y decisiones que guían la implementación

1. **El problema no es solo el número de tokens.** NexFlow combina extracción semántica, reglas y motor de reservas, pero responde muchas veces con fallbacks genéricos o plantillas técnicas. El `ResponseComposer` existente (`EnableAi=false`) no proporciona una reescritura natural general al activarlo; su composición verificada reagrupa bloques y su otro camino añade un inicio corto. Mejorar solo el prompt no resuelve enrutamiento ni comportamiento.
2. **Evitar un bosque infinito de condiciones y regex.** Diseñar una política central de decisiones conversacionales: la IA propone intención/acto/tema con evidencia; la capa de dominio valida datos y permisos; un realizador sencillo expresa el resultado de manera clara y ofrece el siguiente paso. Las reglas deterministas quedan para seguridad, selecciones exactas, menú global, validaciones, idempotencia, caducidad y consentimiento.
3. **Gastaremos IA de forma inteligente, no sistemática.** Una interacción difícil merece interpretación con buen contexto. Una selección de sede, ordinal de lista vigente, confirmación, horario consultado o pregunta guiada no siempre necesita otra generación. La redacción de información compleja puede usar IA, pero con hechos verificados, control de salida y presupuesto explícito.
4. **Preservar reservas.** NO relajar P0 histórico, H06/G8, consentimiento ligado a resumen y versión, CAS, tenencia de workspace, deduplicación, `UnknownDelivery` ni verificación de horarios. Una redacción más humana jamás autoriza una operación.
5. **El piloto de 20 negocios no es certificable bajo free tier sin verificar cuota real y carga.** Se debe calcular capacidad por proveedor/cuenta, no sumar límites internos de 60 llamadas por workspace como si fueran cuotas externas independientes.

## 1. Evidencia de la conversación del 02/11/2026 (casos de aceptación)

**Entradas anonimizadas; no copiar nombres, teléfonos ni datos privados al repositorio.** La fuente completa ya fue aportada al propietario; construir fixtures sintéticos con el mismo comportamiento.

| ID | Entrada / contexto | Defecto visible | Resultado exigido |
|---|---|---|---|
| UX-01 | «Quiero hacerme una consulta odontologixa» | Respuesta genérica | Identificar una necesidad de evaluación/consulta dental, mostrar información de **evaluación odontológica** solo si existe en servicios autorizados, invitar a conocerla o reservarla sin elegir por cuenta del usuario |
| UX-02 | Tras ver servicios: «Quiero hacer una consulta» | Repite «¿Qué información necesitas?» | Usar contexto sin confundir «consulta informativa» y «reservar cita»; ofrecer 1–2 opciones concretas |
| UX-03 | «Quero hacer una reserva» | No empieza reserva | NLU tolerante, transición a sede/servicio sin confirmar nada |
| UX-04 | «Cancelo mi pedido, quiero reservar mañana» y «Reserva nueva» | Regresa al pedido | Como pedidos quedan fuera, guiar descarte seguro del borrador y conservar intención nueva **solo tras confirmación**. Nunca borrar ni confirmar silenciosamente |
| UX-05 | «Mándame al menú», «Menú no servicios» | Enseña servicios / aclaración | «Menú principal» tiene prioridad global; debe mostrar opciones reales habilitadas, sin ORDERS ni REQUESTS en el piloto |
| UX-06 | «¿Qué doctor está disponible mañana?» | Fallback sin explicación | Si falta agenda de profesionales: decirlo explícitamente y ofrecer horarios de sedes/servicios o asesor, sin inventar doctor |
| UX-07 | «Operación dental», «extracción» | No orienta | Mencionar servicios relevantes autorizados; nunca diagnosticar ni indicar procedimiento como clínicamente necesario |
| UX-08 | «Conce» entre dos sedes | Pide de nuevo de forma genérica | Usar alias configurados o aclaración específica con las dos sedes; no elegir sede con abreviatura ambigua |
| UX-09 | «Quiero cambiar mi horario» tras cita registrada | Fallback genérico | Reconocer **gestión de reserva existente**; ofrecer camino de identificación/verificación si existe y está autorizado, si no asesor; sin mutación no verificada |
| UX-10 | Tras descartar reserva, nueva reserva muestra nombre anterior | Posible reuso de identidad | Trazar procedencia del campo, distinguir identidad verificada/consentida de nombre de borrador descartado; impedir arrastre indebido. Clasificar como candidato P0 hasta prueba |
| UX-11 | Usuario: «Para mañana en la mañana» → respuesta usa ejemplo «a las cuatro de la tarde» | Incoherencia en orientación | Ejemplos consistentes con franja, horarios y zona local; una pregunta breve |
| UX-12 | Reserva confirmada y consultas laterales | Fallback repetido | Estado y último objetivo consistentes, respuesta contextual y siguiente acción útil |
| UX-13 | Elegir menú, luego opción 2/4 | Ordinal ligado a pregunta anterior posible | Elegir SOLO de lista vigente/versionada, o aclarar si caducó |
| UX-14 | Respuestas de texto vacío o instrucción no comprendida | Burbuja vacía / frase de recuperación repetitiva | Nunca enviar cuerpo vacío; alternativa inteligible, escalada humana después de errores reiterados |
| UX-15 | Preguntar stock de producto | Disponibilidad no confirmada | Distinguir catálogo publicado de inventario verificado, sin afirmar existencias |

### Ejemplos de redacción objetivo (ilustrativos, no inventar datos)

- **Usuario:** «Quiero hacerme una consulta odontologixa». **Bot:** «¡Claro! Tenemos evaluación odontológica. Puedo contarte el precio y cuánto dura, o ayudarte a reservar. ¿Qué prefieres?» *Solo si el servicio está efectivamente publicado; si hay precio/duración verificados, pueden mencionarse.*
- **Usuario:** «¿Qué doctor está libre mañana?». **Bot:** «Puedo revisar los horarios disponibles para una cita, pero no tengo información de qué doctor atenderá. ¿Quieres que revise horarios o prefieres hablar con el negocio?» *Si la agenda de profesionales no existe.*
- **Usuario:** «Menú, no servicios». **Bot:** menú principal habilitado, sin reinterpretar como una consulta de servicios.
- **Usuario:** «Quiero cambiar mi horario». **Bot:** «¿Quieres cambiar la hora de una cita que ya tienes? Puedo ayudarte a revisarla si el negocio tiene habilitados esos cambios; si no, te comunico con un asesor». *Luego ruta real autorizada, no cambio automático.*

## 2. Restricciones y modos de operación

- **Prohibido:** commit, push, merge, rebase, deploy, migraciones aplicadas, acceso remoto a Oracle o producción, envíos reales de WhatsApp, llamadas reales a Groq/Gemini sin autorización/budget específico, cambios a claves/configuración productiva, limpieza destructiva de Git.
- Fijar HEAD, `AGENTS.md`, `git status --short`, cambios previos y licencias antes de editar. Conservar todo lo existente y las skills `.agents/skills/systematic-debugging`, `.agents/skills/code-testing`.
- Cualquier opt-in nuevo por workspace debe verificarse realmente en la capa de acceso; no activar la bandera GLOBAL `AssistantRecovery.EnableContextualFocus` en producción compartida. No fingir que se puede activar solo para DEMO con un setting global.
- **Pausar ORDERS/REQUESTS por licencia/entitlement o una feature gate verificada por workspace**, en el piloto y solo con autorización posterior para aplicar la configuración real. No borrar endpoints, historiales, órdenes ni efectos anteriores. La UI/menú y la detección de intención tienen que decir la verdad. Si la feature gate aún no existe, añadir mecanismo mínimo opt-in por workspace probado, NO apagar módulos globales de negocios ajenos. Conservar acceso a consulta de pedidos históricos según contrato.
- `ConversationQuestion`, `AiEvidenceValidator`, reservas y sus motores conservan autoridad; IA no propone IDs confiables ni emite confirmaciones.
- Guardar fixtures con datos sintéticos, sin nombres/teléfonos reales ni transcripciones privadas completas.

## 3. Cuotas gratuitas y modelo de capacidad — información verificable

### 3.1 Fuentes oficiales y límites

- Groq `openai/gpt-oss-20b`, tabla pública de referencia **Free Plan**: **30 RPM**, **1.000 RPD**, **8.000 TPM**, **200.000 TPD**. La cuota efectiva y posibles excepciones se comprueban en el panel de la organización; consumos combinados bajo una misma organización. Fuente: https://console.groq.com/docs/rate-limits
- Gemini: los límites **RPM/TPM/RPD varían por modelo, proyecto y tier**; la cuota diaria aplica por proyecto, no por clave; la fuente de verdad es AI Studio. **Sin acceso a ese panel, no declarar ninguna cifra RPD/TPM específica como cuota efectiva.** Fuente: https://ai.google.dev/gemini-api/docs/rate-limits y https://aistudio.google.com/
- Modelo `gemini-3.5-flash` marcado como deprecado y redirigido a `gemini-3.6-flash` según documentación actual. No sustituir el ID hasta verificar cuota, disponibilidad, calidad, costo y autorización. Fuente: https://ai.google.dev/gemini-api/docs/models/gemini-3.5-flash
- El nivel gratuito de Gemini puede utilizar contenido enviado para mejorar productos; evaluar su idoneidad para datos personales de pacientes antes de usar información identificable. Fuentes: https://ai.google.dev/gemini-api/docs/pricing y términos del proveedor. No enviar PII a una prueba de proveedor.

### 3.2 Fórmulas de simulación (cambiar supuestos por mediciones después)

`conversaciones_diarias = negocios * conversaciones_por_negocio_dia`

`llamadas_por_proveedor_dia = conversaciones_diarias * llamadas_por_conversacion_al_proveedor + reintentos + llamadas_extra_de_composicion`

`tokens_del_proveedor_dia = suma(tokens_entrada_reales + tokens_salida_reales + otros_tokens_facturados_o_limitados)`

`capacidad_conversaciones_por_dia = min(RPD / llamadas_por_conv, TPD / tokens_por_conv, capacidad_RPM_TPM_concurrencia, capacidad_servicios_backend)`

Si un límite falta o no se conoce, marcarlo **UNKNOWN**, nunca inferir que es ilimitado. Debe contemplarse un margen de seguridad (por ejemplo, 30 %, configurable) y reparto justo entre workspaces.

### 3.3 Ejercicio conservador para 20 conversaciones por negocio/día

| Negocios | Conversaciones/día | 1 Groq/conv | 2 Groq/conv | 4 Groq/conv |
|---:|---:|---:|---:|---:|
| 10 | 200 | 200 | 400 | 800 |
| 20 | 400 | 400 | 800 | 1.600 |
| 30 | 600 | 600 | 1.200 | 2.400 |

**Ejemplo no medido:** cada interpretación suma **2.000 tokens**. Solo por TPD de Groq, la cuota de referencia permitiría ~**100 llamadas diarias** (menos con margen, tokens adicionales o picos). Por tanto, 20 negocios × 20 conversaciones no es sostenible con Groq gratuito si cada conversación necesita siquiera una interpretación similar. Con un supuesto de **2.700 tokens**, la cifra bajaría a ~74 llamadas. La primera barrera diaria es TPD antes que RPD en estos supuestos; a nivel minuto, 8.000 TPM también limita ráfagas.

**No sumar Gemini como si garantizara ese excedente**: faltan sus cuotas efectivas, y el router puede gastar fallback en el otro proveedor. Una conversación puede tener cero llamadas IA si solo usa un selector fiable; esto debe medirse, no suponerse. El número de negocios no equivale a capacidad; exige tasa por conversación, distribución de tráfico y cuotas.

### 3.4 Observabilidad y protección de cuota requeridas

1. Reutilizar `AiTelemetry` para registrar de forma agregada por provider/modelo/tarea/workspace ofuscado: intentos, tokens de entrada/salida realmente reportados, longitud de prompt, 429, timeouts, tasa de fallback, p50/p95, aceptación/rechazo de interpretaciones, cantidad de aclaraciones y abandono; **sin contenido ni PII**.
2. Instrumentar **admisión global duradera por proveedor/modelo** además de la vigente por cliente/tenant, con soporte de RPM, RPD, TPM y TPD (cuando el proveedor lo reporte), margen configurable y reserva para otros workspaces. No usar contadores solo en memoria en presencia de varias réplicas; no correr con cuotas inventadas. El estado UNKNOWN debe producir política conservadora sin bloquear por error toda la ruta determinista.
3. Leer `Retry-After` y cabeceras de límites cuando existan, respetar ventanas/zonas de reseteo y no provocar cascadas. Un `429` de Groq puede causar fallback a Gemini **solo si hay presupuesto y autorización del proveedor alternativo**, no como mecanismo para evadir cuotas. No reintentos ciegos.
4. **Primero medir y reducir overhead del prompt**: el contrato actual de `AiInterpreter` ronda 6.4k caracteres fijos *antes* de contexto; medir tokens reales con la telemetría. Asegurar que al compactarlo no se pierdan campos/evidencia/validaciones. No reducir calidad para ahorrar tokens.
5. Crear `capacity_simulator` reproducible (Python o C#; sin llamadas reales), configurable: negocios [10,20,30], conversaciones/día [5,10,20], mensajes/conversación, mix determinista/IA, tokens entrada/salida P50/P95, retries, fallback, picos horarios, cuotas por proveedor. Entregar CSV/Markdown con PASS/FAIL/UNKNOWN por límite, estimación de costo **solo en plan pagado y si se carga precio actual**, nunca llamar «capacidad real» al cálculo.

## 4. Arquitectura objetivo mínima (integrar, no reescribir)

**Entradas** → validaciones tenant/licencia/handoff/anti-replay → **DialogueGoalPolicy** (intención global, pregunta vigente, evidencias, posibilidades habilitadas) → NLU IA estructurado en lenguaje difícil → **resolución fundamentada** (catálogo y sedes autorizadas) → **motores existentes** (reserva segura) → **ResponseRealizer** legible (hechos/acciones permitidas y pregunta siguiente) → salida WhatsApp.

No son servicios obligatorios nuevos: nombres de responsabilidades para adaptar a clases actuales (`TurnUnderstandingPipeline`, `DialoguePolicy`, `AiInterpreter`, `AiResponseOrchestrator`, `ConversationRecoveryService`, `ResponseComposer`, `SecretaryPresentation`, `BookingFlow`, `ChatFlow`). **Evitar duplicar estados o introducir frameworks de agentes.**

### 4.1 Comprensión con pocos principios generalizables

- Distinguir **objetivo** (informarse, conocer servicios, reservar, gestionar cita, hablar con alguien) de **datos** (sede, fecha, servicio, nombre). Pocos actos tipados, sin una lista infinita de palabras exactas.
- Interpretar la expresión del usuario con contexto de la pregunta vigente, últimos datos autorizados y un resumen lingüístico acotado. No incluir transcripción completa ni PII innecesaria.
- El prompt del NLU debe contemplar errores de tipeo, sinónimos coloquiales y elípsis sin inventar entidades. Un candidato solo se acepta tras validar fragmento de entrada, permisos, entidad y estado.
- Resolver «quiero una consulta odontológica» como necesidad informativa o intención de evaluación, **sin iniciar confirmación**; si realmente hay dos posibilidades, responder con opciones cortas y relevantes.
- Navegación global (`menú`, `volver al inicio`, `asesor`) con prioridad estable; `sí/no/segunda` sujetos a pregunta/versiones, no a la última lista arbitraria. Manejar pedidos suspendidos o fuera de piloto sin quedarse atrapado.
- Si falta capacidad (agenda del doctor, stock, diagnóstico), explicar el límite específico y ofrecer una acción permitida; no contestar con un fallback intercambiable.
- Reducir malentendidos **semánticamente**, no añadiendo regex por cada frase capturada. Las nuevas expresiones deben servir como **tests de generalización** y no como `if (text.Contains(...))` para superar la suite.

### 4.2 Realización natural y segura de respuestas

- Respuestas con **hecho verificado + ayuda práctica + una pregunta**. Lenguaje sencillo apto para lectores jóvenes y mayores. 1–3 bloques cortos; objetivo 25–70 palabras salvo listas/resumen legal o transaccional; coherencia entre ejemplos y franjas horarias; formato de soles legible; evitar «borrador», «operación», «fingerprint», «foco», «tenancy», «módulo» cuando no aporten.
- Usar una **política de presentación central y reutilizable**: tono empático sin exceso de adornos, opciones breves, confirmación clara y continuidad. Conservar hechos verificados, orden de opciones, precios, horarios, atribución de incertidumbre y texto transaccional obligatorio.
- No volver a habilitar `ResponseComposition.EnableAi` a ciegas: comprobar su alcance actual. Si una **composición IA verdaderamente útil** se propone, hacerla **opt-in y solo informativa**, con entrada `VerifiedFacts` + `AllowedActions`, salida restringida, cotejo programático de hechos críticos y preguntas autorizadas, timeout y fallback seguro. Prohibido usar composición libre para resúmenes/confirmaciones, consentimiento, cambio de sede/fecha/hora, operaciones o efectos.
- Priorizar mejoras gratuitas sobre las plantillas centrales y la selección de la respuesta concreta; IA de redacción solo para preguntas ricas que lo merezcan y que quepan en el presupuesto.
- Respuestas de error diferenciadas: `no entiendo qué servicio`, `no hay agenda de profesionales`, `no tengo stock confirmado`, `servicio fuera del catálogo`, `proveedor no disponible`, `tengo dos opciones similares`. Pregunta mínima, sin culpar al usuario.

### 4.3 Separación de permisos y pausa de pedidos

- El menú del piloto muestra únicamente acciones habilitadas. `ORDERS` y `REQUESTS` ocultos/deshabilitados **solo para workspaces piloto** si está aprobado el cambio local correspondiente. Ante petición de compra, informar que el catálogo es informativo y ofrecer atención humana; no iniciar un pedido nuevo. Preservar lecturas de registros históricos conforme a autorización.
- No afectar otros tenants ni colocar configuración global como si fuera por tenant. Si el mecanismo de licencias ya permite retirar ORDERS a un workspace, documentar cómo hacerlo y probarlo, **pero no modificar la licencia real sin aprobación**.
- Cualquier solicitud de cambiar una **reserva registrada** requiere identificación, permiso y circuito del backend; si no existe, informar y ofrecer asesor, no afirmar que se cambió. La reserva en preparación y la ya registrada son estados diferentes.
- **Riesgo de nombre reutilizado:** establecer con tests si es identidad verificada, dato persistente voluntario o arrastre de borrador cancelado. Nunca reutilizar el nombre de un borrador descartado como si el usuario lo hubiera aportado para el nuevo, salvo procedencia de identidad confiable documentada y política explícita.

## 5. Fases ejecutables y entregables

### Fase 0 — Baseline, git, cuotas y protección

- Leer `AGENTS.md`, los dos planes previos, `docs/auditorias/agente-conversacional/2026-11-02/RESULTADOS_FINALES.md`, el código y tests relevantes. Registrar `HEAD`, rama, cambios locales y API configuración fuente (NO claves). Reutilizar skills ya instaladas si son pertinentes.
- Crear `docs/auditorias/experiencia-conversacional-free-tier/2026-11-02/` con `ESTADO.md`, `DIAGNOSTICO.md`, `CUOTAS_Y_CAPACIDAD.md`, `MATRIZ_ESCENARIOS.md`, `CAMBIOS.md`, `RESULTADOS.md`.
- Reproducir el comportamiento de la rama base con tests aislados que ejerciten handler y estado real con providers fake. Registrar qué fallos corresponden a comprensión versus orientación versus proveedor/cuota.
- Obtener cuotas efectivas de Groq/Gemini **solo si ya hay datos autorizados disponibles**; si no, guardar `UNKNOWN` + procedimiento seguro para que el propietario las consulte sin compartir secretos. Nunca adivinar la cuota de Gemini.

### Fase 1 — Corpus independiente y métricas antes del parche

- Añadir fixtures sintéticos con casos UX-01..UX-15 más 25–40 reformulaciones **nuevas**, sin trucos léxicos. Positivos/negativos contrastivos: deseo de reservar vs pregunta sobre qué es una reserva; «consulta odontológica» vs consulta de un precio; cancelación de borrador vs cita confirmada; pregunta de doctor sin agenda; dos servicios parecidos.
- Validar significado (objetivo/acto/entidad permitida), estado, llamada a lectura, evidencia, mensaje final y cero efectos no autorizados. Incluir preguntas de opción fuera de contexto y mensajes duplicados.
- Medir baseline de **éxito de intención, completitud, comprensión de respuesta por humanos, fallbacks genéricos/100 mensajes, cantidad de preguntas para iniciar la reserva, mensajes vacíos, uso de IA, tokens reales donde disponibles y latencia p95**. No inventar métricas sin mediciones.
- Separar un set de evaluación congelado de los ejemplos con los que se desarrolla; no editar tests para simular éxito.

### Fase 2 — Comprensión / control del diálogo sin árboles infinitos

- Identificar puntos exactos donde se clasifica General/ProviderUnavailable, se ignora intención nueva, se confunde menú/catálogo, o se pierde pregunta vigente. **Primero prueba roja y causa.**
- Rediseñar *solo* el mínimo contrato semántico o orden de precedencia que resuelva clases de fallos; reutilizar `AiInterpreter`, `TurnUnderstandingPipeline`, `DialoguePolicy`, `AiEvidenceValidator`, `AiRouter`, `ConversationQuestion`.
- Garantizar que una interpretación válida se materializa incluso con slots vacíos, pregunta clara y sin operación real; conservar correcciones 39432d7 de catálogo.
- No confiar en JSON de IA para decidir compras o consentir/cancelar; los motores siguen validando. No inventar la causa de P0 por un test verde.

### Fase 3 — Respuestas humanas en catálogo y reservas

- Simplificar frases globales/plantillas compartidas y jerarquía de información; reducir textos repetidos y técnicos; mencionar precio/duración/servicio únicamente desde datos autorizados. No rediseñar frontend.
- Hacer que cada respuesta indique lo que entendió, lo útil que sabe y un siguiente paso. Puede ofrecer alternativas sin obligar a contestar una fórmula. Una sola pregunta por turno, salvo lista o resumen.
- En reservas, comprobar coherencia de franjas, selección sede/servicio, consulta de horario, datos de paciente, resumen y confirmación. No cambiar semántica de consentimiento ni motor de agenda.
- Si se evalúa redacción IA, habilitarla **solo dentro del fixture/piloto aislado**, con acceso a hechos verificados y comprobación estricta de salida. Comparar versus respuestas locales; si no mejora significativamente y usa cuota, descartarla.

### Fase 4 — Cuotas free, equidad y modos de degradación

- Añadir o adaptar observabilidad estructurada y simulador de escenarios 10/20/30 negocios a 5/10/20 conversaciones/día. Modelar 0/1/2/4 interpretaciones por conversación, 0/1 redactado por IA, 100/200/400/600 conversaciones/día, picos y fallback. Evaluar tanto RPD/TPD como RPM/TPM y cuota por cuenta/proyecto.
- Probar agotamiento de cuota global, cuenta desconocida, 429, Retry-After, timeout, fallos transitivos, fallbacks; ningún bucle de llamadas o de aclaraciones ni un negocio monopoliza todo.
- Una ruta sin IA debe seguir funcionando cuando el presupuesto se acaba. Si un mensaje requiere IA y no hay capacidad, disculpa específica y vía de ayuda, sin mutación ni información falsa. No añadir proveedores o claves rotativas para sortear límites.
- Obtener mejoras de consumo primero por **menos errores y menos repeticiones**: no compresión ciega de prompts ni bajada del número de evidencias.

### Fase 5 — Seguridad y regresión

- Tests de aislamiento por workspace, sede, licencia, pregunta vigente y TTL, handoff, CAS, idempotencia, `UnknownDelivery`, provider failures, nombre viejo, P0 histórico sin declararlo resuelto.
- Revisar conteos históricos exactos G8/H06 y hashes de corpus **sin corregir pruebas históricas para hacerlas verdes**. Ejecutar suites dirigidas y build; cuando el diff esté estable, **una suite completa consolidada** contra HEAD local con TRX nuevo; si falla, corregir y volver a ejecutar el gate final. Distinguir esperado histórico de fallo vigente.
- Criterios: **cero** confirmaciones sin consentimiento, **cero** cruce de tenant, **cero** duplicaciones, **cero** mensajes vacíos, **cero** respuesta que afirme médico/stock no verificado. Los requisitos UX-01..UX-15 críticos deben pasar en fixtures; objetivo ≥95 % en set de comprensión congelado sin sustituir gates de seguridad; reducción comprobada de fallbacks, sin aumentar llamadas IA innecesarias.

### Fase 6 — Revisión final y recomendación de piloto

- Entregar informe de causas raíz (archivo/método/linea), lista exacta de archivos, matriz de pruebas antes/después, TRX, cumplimiento de seguridad, consumo de tokens, límites/UNKNOWN y recomendaciones claras para piloto.
- Evidencia real de Groq/Gemini SOLO con opt-in del propietario, presupuesto máximo por proveedor y datos sintéticos. Ninguna ejecución externa por defecto.
- Resultado `GO_TO_CODE_REVIEW`, `NO_GO_EXTERNAL_MODEL`, `NO_GO_LIVE_RESERVATIONS`, `NO_GO_DEPLOY` según hechos; no declararse desplegado. Si persisten P0 u otros bloqueos importantes, no marcar listo para reservas reales.

## 6. Contrato de calidad para el bot (aceptación humana)

El usuario común debe poder identificar, leyendo el texto **una sola vez**:

1. **¿Qué entendió el asistente?** Expresa intención plausible, sin suponer decisiones clínicas.
2. **¿Qué sabe realmente?** Solo hechos de catálogo, sede, horario y disponibilidad que verificó.
3. **¿Qué debo hacer ahora?** Una acción sencilla o pregunta corta, con opciones cuando ayudan.
4. **¿Qué pasó con mis datos?** Si se cambió o descartó algo, explicar el efecto real en palabras cotidianas.
5. **¿Está confirmada la cita?** Antes de confirmación persistida, decir explícitamente que NO; después, referencia segura.

Un evaluador humano ajeno al equipo debe poder seguir al menos 10 conversaciones completas con poca o ninguna guía. Registrar número de preguntas repetidas, solicitudes de aclaración del usuario y confusiones reportadas, sin usar el dato privado en repositorio.

## 7. Secuencia de trabajo eficiente para Codex

1. Fase 0 y reproducción de 3–5 errores de máximo impacto (UX-01, UX-03, UX-05, UX-09, UX-10); no rehacer auditoría completa de F1–F4.
2. Resolver causa transversal en intención/control y salidas; luego extender tests al resto.
3. Introducir presupuesto global y simulador con proveedor fake. No aumentar primero cuotas internas ni duplicar llamadas en cada turno.
4. Ejecutar pruebas focalizadas tras cada parche, gate total solo al cierre estable.
5. Actualizar `ESTADO.md` al final de cada fase: SHA, ruta de archivos, tests/resultado exacto, siguiente comando y bloqueos, para retomar tras pérdida de tokens.
6. Si el cambio requiere múltiples fases y hay límite de recursos, no inventar que finalizó; dejar diff parcial trazable y la siguiente acción concreta. No solicitar autorización por cambios locales menores dentro del alcance; sí para credenciales, proveedor real, producción, migraciones, commit/push.

## 8. Referencias técnicas de código

- `backend/NexFlow.Application/Features/AI/Interpretation/AiInterpreter.cs` — contrato y prompt NLU.
- `backend/NexFlow.Application/Features/AI/Router/AiRouter.cs` — tareas y fallback por proveedor.
- `backend/NexFlow.Application/Features/AI/Router/AiTelemetry.cs` — uso y duración.
- `backend/NexFlow.Infrastructure/Engines/AI/GroqAiProvider.cs` — GPT-OSS 20B, JSON, timeout.
- `backend/NexFlow.Infrastructure/Engines/AI/GeminiAiProvider.cs` — Gemini, JSON y fallbacks.
- `backend/NexFlow.Application/Features/Automation/Dialogue/Understanding/TurnUnderstandingPipeline.cs` — decisiones y aclaraciones.
- `backend/NexFlow.Application/Features/Automation/Dialogue/Composition/ResponseComposer.cs` y `SecretaryPresentation.cs` — presentación.
- `backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/AiResponseOrchestrator.cs` y `ConversationRecoveryService.cs` — respuesta, recuperación, cuota local.
- `backend/NexFlow.Application/Features/Automation/Dialogue/Contracts/ConversationQuestion.cs` — pregunta vigente.
- `backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/Flows/ConversationFlows.cs` — respuestas de catálogo y apoyo.
- `backend/NexFlow.Tests/Concierge/AgentStabilizationTests.cs` — fixtures previos; NO modificar aserciones previas para fabricar verdes.

**Nota final a Codex:** la calidad alta no depende de reglas para miles de frases ni de gastar IA por turno. Requiere un contrato semántico generalizable, respuestas claras basadas en hechos, continuidad fiable y administración global de cuota. Ejecutar las fases locales, probar y documentar; mantener seguridad de reservas y datos reales fuera del alcance.
