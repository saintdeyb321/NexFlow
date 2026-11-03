# NEXFLOW — Informe de calidad conversacional, modelo de IA y simulación de capacidad

**Fecha de análisis:** 2026-11-02  
**Versión de referencia:** commit `39432d7bf870bf5e0ba370738b05e3bd750bc9ab`, desplegado y saludable según salida anterior de Oracle  
**Material:** conversación de prueba del familiar del propietario (2/11/2026, 53 turnos del cliente y 53 entradas del canal del negocio), mensaje independiente «Quiero hacerme una consulta odontologixa», código del repositorio y tarifas/documentación oficiales consultadas.  
**Decisión de producto:** suspender pedidos/solicitudes comerciales en el piloto; priorizar servicios, catálogo informativo y reservas con validación segura.  
**Naturaleza:** análisis e hipótesis de mejora, NO benchmark del VPS ni de proveedores reales, NO autorización para cambiar producción ni habilitar reservas live sin investigar el P0.

## 1. Dictamen ejecutivo

NexFlow no falla exclusivamente porque se «ahorran tokens». Falla una cadena de **detección de objetivo, decisión sobre el estado vigente, resolución de la intención y diseño del mensaje de salida**. La infraestructura y los motores han superado pruebas y despliegue técnico; esa evidencia no acredita una experiencia clara para pacientes reales. El usuario se ve obligado a descubrir palabras rígidas, repetir instrucciones y comprender «borrador», «solicitud», «selección» y otras categorías internas. El sistema no está suficientemente orientado a resolver la necesidad.

**Decisión recomendada:** introducir un perfil de asistente guiado con lenguaje cotidiano, manteniendo un núcleo determinista de autoridad y seguridad. Gastar *más IA donde reduce fricción* —interpretaciones ambiguas y respuestas informativas complejas—, no en ornamentar cada mensaje. La calidad primero, con presupuestos, observabilidad y pruebas A/B. No modificar límites globales ni habilitar CTA de forma indiscriminada.

## 2. Evidencia de conversaciones

Se cuentan **53 turnos del cliente y 53 entradas del canal del negocio**, dos entradas sin texto útil (posibles mensajes de formato/adjuntos); respuesta del canal del negocio **media aproximada 163 caracteres, mediana 129**, y **8 entradas con más de 300 caracteres**. La cadena exacta de fallback «Podemos continuar con una consulta concreta...» aparece **7 veces** y «Quiero asegurarme de entenderte...» **3 veces**. Son recuentos de la transcripción aportada; no representan tasas del conjunto de usuarios. Las entradas del canal del negocio pueden incluir intervención humana, por lo que no se clasifican todas sin más como IA.

| Evidencia (parafraseada o fragmento breve) | Resultado visible | Hallazgo / nivel | Comportamiento deseado |
|---|---|---|---|
| «Quiero hacerme una consulta odontologixa» | Fallback genérico | P1: mala interpretación de intención + errata | Reconocer orientación hacia atención odontológica, mencionar evaluación disponible si está autorizada y ofrecer consultar precio o reservar, con una sola pregunta concreta |
| «Quiero hacer una consulta» después de ver servicios | Pide «¿Qué información necesitas?» | P1: falta de contexto semántico | Distinguir consulta informativa vs cita; ofrecer evaluación odontológica sin asumir que el paciente la eligió |
| «Cancelo mi pedido, quiero hacer una reserva para mañana» | Se desvía a nueva/existente; al responder «Reserva nueva» vuelve al pedido | P1: cambio de flujo/múltiples intenciones y control del borrador | Explicar que el pedido está en preparación y solicitar confirmación para descartarlo; continuar automáticamente el objetivo de reserva **solo después** de la decisión válida, preservando «mañana» si está permitido por el contrato |
| «Quero hacer una reserva», «para mañana en la tarde» | Respuesta genérica y luego mensaje de aclaración repetitivo | P1: tolerancia tipográfica y continuidad | Reconocer deseo de reservar y pedir la sede o servicio que falte |
| «Mándame al menú de nuevo» | Lista de servicios; «Menú no servicios» produce nueva aclaración | P1: navegación global subordinada a contexto obsoleto | «Menú principal» siempre muestra menú principal, no un catálogo incidental |
| «¿Qué doctor está disponible mañana?» | Mensaje genérico | P1: límite de capacidad no explicado | Si no existe agenda de profesionales, expresar «Puedo revisar horarios de servicios, pero no tengo información sobre qué doctor atenderá; ¿quieres que te comuniquemos con el negocio?» |
| «Operación dental», «extracción» | Respuesta genérica antes de entrar al flujo | P1: vocabulario coloquial y consulta de servicios | Presentar opciones autorizadas y distinguir orientación no clínica de diagnóstico; no afirmar que una extracción específica es indicada |
| «Conce» | No identifica sede; después «sanjeronimo» sí | P2: abreviatura contextual no resuelta | Aclaración con opciones vigentes sin selección arbitraria; admitir alias autorizados si hay unicidad |
| «Quiero cambiar mi horario» después de una confirmación | Fallback genérico | P1: detección de administración de reserva existente | Explicar cómo identificar/verificar cita y proponer cambiar fecha/hora solo mediante flujo seguro habilitado |
| Después de descartar una reserva y crear otra | Resumen nuevo contiene nombre anterior sin pregunta nueva | **P0 candidato**: identidad del cliente o dato persistente reutilizado | Investigar origen/ámbito de `RealCustomerName`, `PendingCustomerName`, identidad del consumidor, revisión y transición. Puede ser reutilización legítima del perfil, pero no darlo por probado sin traza. Nunca confirmar la identidad de un paciente sin verificar su procedencia. |

**Otros puntos:** «Cancelar» significa «descartar este borrador» o «cancelar una cita ya registrada», según el estado; la conversación muestra varias aclaraciones largas en lugar de una distinción sencilla. La reserva de prueba llegó efectivamente a confirmarse; esto demuestra funcionamiento del flujo, pero refuerza que las pruebas siguientes deben detenerse antes de confirmar una operación real mientras el P0 histórico no esté cerrado o aislado.

## 3. Arquitectura de la versión actual: hechos de código

**Comprensión:** `AiInterpreter` usa un prompt de extracción tipada y evidencia JSON, de aproximadamente 6.4 mil caracteres de plantilla fija antes de añadir contexto. `AiRouter` dirige `IntentExtraction` a Groq y `ComplexChat`/`ResponseComposition` a Gemini; solo hay fallback cruzado ante errores transitorios. `GroqAiProvider` usa `temperature=0`, `max_completion_tokens=1024` y esfuerzo de razonamiento bajo para GPT-OSS. `GeminiAiProvider` usa JSON con `maxOutputTokens=1024`; sus fallbacks internos pueden generar intentos adicionales. El texto generado por el modelo es **interpretación no confiable**, no hecho comercial ni consentimiento.

**Estado y límites:** `AssistantRecoveryOptions`: 6 admisiones de IA/cliente/minuto, 60/workspace/minuto, 3 aclaraciones, 2000 caracteres por entrada y expiración de contexto configurable de 24 horas. Estos topes son por cliente y tenant, NO un límite global para todos los negocios; Groq y Gemini aplican cuotas propias compartidas en la cuenta/proyecto. `ConversationQuestion.Current` verifica workspace, revisión/fingerprint, TTL y lista vigente. `EnableContextualFocus` es una bandera global desactivada por defecto.

**Respuestas:** `AiResponseOrchestrator` contiene literalmente el fallback «Podemos continuar con una consulta concreta o solicitar un asesor». `TurnUnderstandingPipeline` contiene el fallback «Quiero asegurarme de entenderte. No he cambiado tu selección...». `SecretaryPresentation` y los flujos producen textos predeterminados. El `ResponseComposer` está `EnableAi=false` por defecto. Cuando está habilitado, su rama verificable **solo reorganiza bloques**, no reescribe libremente los hechos; su otra rama añade un saludo corto. **Activarlo tal cual NO convertiría el bot en un asistente natural** y aumentaría llamadas en algunos casos.

**Observabilidad disponible:** `AiTelemetry` ya registra intentos HTTP, latencia, tokens reales de entrada/salida reportados por proveedores y tamaño del prompt, etiquetados por tarea/proveedor y una clave de workspace ofuscada. No se dispone en este encargo de series temporales de Oracle, panel de límites de Groq, consumo histórico, especificación exacta de CPU/RAM ni percentiles reales. No inferir esas métricas a partir de fixtures.

**Fuentes de código inmutables examinadas:**
- https://github.com/saintdeyb321/NexFlow/blob/39432d7/backend/NexFlow.Application/Features/AI/Interpretation/AiInterpreter.cs
- https://github.com/saintdeyb321/NexFlow/blob/39432d7/backend/NexFlow.Application/Features/AI/Router/AiRouter.cs
- https://github.com/saintdeyb321/NexFlow/blob/39432d7/backend/NexFlow.Application/Features/Automation/Dialogue/Composition/ResponseComposer.cs
- https://github.com/saintdeyb321/NexFlow/blob/39432d7/backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/AiResponseOrchestrator.cs
- https://github.com/saintdeyb321/NexFlow/blob/39432d7/backend/NexFlow.Application/Features/Automation/Dialogue/Understanding/TurnUnderstandingPipeline.cs
- https://github.com/saintdeyb321/NexFlow/blob/39432d7/backend/NexFlow.Application/Features/AI/Router/AiTelemetry.cs

## 4. Qué significa «parecerse a AxionLabs»

No hay métricas comparables ni una prueba controlada de AxionLabs en este encargo; el objetivo se define por lo que el propietario observa y solicita: **entender lenguaje espontáneo, anticipar el siguiente paso, explicar opciones y hablar con sencillez**. Evitar prometer una réplica técnica de su arquitectura interna.

**Propuesta de política conversacional (nivel de lectura apto para niños y mayores):**

1. **Interpretar la necesidad antes de preguntar.** Si dice «quiero una consulta odontológica», ofrecer la evaluación odontológica si está disponible en ese tenant, mencionar sus datos verificados y preguntar una sola cosa concreta: «¿Quieres conocer el precio o que te ayude a reservar?».
2. **Dar información útil, no solo preguntar.** «Tenemos evaluación odontológica de 30 minutos y precio S/60 en el catálogo de ejemplo; puedo ayudarte a reservar. ¿En qué sede?» Solo si fuente/alcance actuales lo autorizan.
3. **Guiar con una pregunta breve por turno.** El usuario no debería tener que entender palabras como `CTA`, «borrador», «fingerprint» o «operación registrada»; «Tu cita aún no está confirmada» es suficiente.
4. **No repetir información innecesaria.** Se puede reutilizar sede/fecha/servicio si siguen válidos y ligados al mismo borrador; las confirmaciones exigen resumen nuevo si cambian datos.
5. **Si no puede responder, explicar la limitación precisa.** «Puedo mostrarte los horarios del local, pero no sé qué doctor estará de turno.» Ofrecer asesor u otra consulta verificable; no fingir existencia de agenda de profesionales.
6. **No presentar pedidos.** La lista de productos es informativa, sin compras, carrito, stock afirmativo ni solicitud comercial mientras el módulo esté en pausa.
7. **Consistencia humana.** Respuestas cortas (objetivo orientativo de 25–70 palabras, salvo catálogos o resúmenes), datos legibles, una pregunta principal, ortografía y formato de moneda uniforme, trato respetuoso.

**Antes (actual):** «Podemos continuar con una consulta concreta o solicitar un asesor. ¿Qué información necesitas?»  
**Después propuesto:** «¡Claro! ¿Buscas una evaluación dental o información sobre algún tratamiento? Tenemos una evaluación odontológica en nuestro catálogo. Puedo explicarte el servicio o ayudarte a reservar.» (Si el catálogo vigente contiene ese servicio).  
**Advertencia clínica:** no convertir «me duele un diente» en un diagnóstico o prescripción; se puede ofrecer orientación administrativa y atención humana.

## 5. Simulación de tokens, solicitudes y costo

### 5.1 Supuestos explícitos (NO mediciones reales)

- **Una conversación = 12 mensajes entrantes del cliente.** Se simulan conversaciones representativas; la prueba del primo tiene 53 mensajes y no es una muestra estadística de usuarios.
- **Una interpretación IA (NLU):** 2,000 tokens de entrada + 150 de salida. La plantilla actual tiene ~6.4k caracteres y añade contexto; **los tokens reales dependerán de tokenizer, contexto y proveedor**, por tanto este valor es una hipótesis, no contador de Oracle.
- **Una respuesta de estilo IA selectiva:** 500 tokens de entrada y 180 de salida; solo en cuatro turnos informativos complejos del escenario premium, con datos del backend verificados.
- **30 días/mes**, todos los negocios con actividad igual y sin cache, errores, reintentos, cargos adicionales ni descuentos; cifras en USD. Dos casos de volumen: (X) 20 negocios × 10 conversaciones/día = 6,000 conversaciones/mes y (Y) 30 negocios × 15 conversaciones/día = 13,500/mes.
- Los escenarios con 8 interpretaciones **no** significan ocho llamadas para cada cliente en un minuto: el tráfico de una conversación se extiende en el tiempo. Las cuotas por cliente/workspace pueden bloquear ráfagas igualmente.

**Precios oficiales consultados (USD por millón de tokens, no tarifas contratadas):** Groq GPT-OSS-20B entrada 0.075, salida 0.30; Gemini 3.1 Flash-Lite entrada 0.25, salida 1.50; Gemini 3.5 Flash entrada 1.50, salida 9.00. Gemini incluye posibles tokens de razonamiento en salida; nuestra hipótesis puede subestimar el costo real. Fuentes: https://console.groq.com/docs/model/openai/gpt-oss-20b y https://ai.google.dev/gemini-api/docs/pricing.

### 5.2 Resultados calculados

| Perfil hipotético | Llamadas Groq/conversación | Llamadas Gemini/conversación | Tokens totales/conversación* | Costo API/conversación | USD/mes, X | USD/mes, Y |
|---|---:|---:|---:|---:|---:|---:|
| A. Actual orientativo | 4 | 0 | 8,600 | $0.00078 | $4.68 | $10.53 |
| B. Mejor interpretación | 8 | 0 | 17,200 | $0.00156 | $9.36 | $21.06 |
| C. Conversacional selectivo Lite | 8 | 4 | 19,920 | $0.00314 | $18.84 | $42.39 |
| D. Conversacional selectivo Flash | 8 | 4 | 19,920 | $0.01104 | $66.24 | $149.04 |

* Suma de tokens nominales de entradas y salidas de ambos proveedores. No incluye caché ni retries ni razonamiento extra no capturado en supuestos. **El perfil A no es una medición del estado actual**, solo un proxy de baja intensidad; B–D son diseños propuestos.

Para la cartera del caso X, el perfil C correspondería aproximadamente a **1,600 llamadas Groq y 800 Gemini por día**; la cifra mensual de API sería ~USD 18.84. Es **solo costo de inferencia**, sin WhatsApp/Evolution, servidores, redes, almacenamiento, colas, observabilidad, impuestos o soporte humano.

### 5.3 Carga estimada por negocios

| Negocios | Conversaciones/negocio/día | Conversaciones totales/día | Mensajes clientes/día | Groq/día (perfil A) | Groq/día (B–D) | Tokens Groq/día (A) |
|---|---:|---:|---:|---:|---:|---:|
| 10 | 5 | 50 | 600 | 200 | 400 | 430,000 |
| 10 | 10 | 100 | 1,200 | 400 | 800 | 860,000 |
| 20 | 5 | 100 | 1,200 | 400 | 800 | 860,000 |
| 20 | 10 | 200 | 2,400 | 800 | 1,600 | 1,720,000 |
| 20 | 20 | 400 | 4,800 | 1,600 | 3,200 | 3,440,000 |
| 30 | 5 | 150 | 1,800 | 600 | 1,200 | 1,290,000 |
| 30 | 10 | 300 | 3,600 | 1,200 | 2,400 | 2,580,000 |
| 30 | 20 | 600 | 7,200 | 2,400 | 4,800 | 5,160,000 |

### 5.4 ¿Cuántos negocios soporta realmente hoy?

**No hay un número verificable todavía.** La máquina puede alojar registros de 20–30 negocios y aun así saturar el proveedor a 3–5 conversaciones por negocio al día si comparte cuotas bajas; inversamente, una cuota pagada elevada podría permitir mucha más actividad sin incrementar CPU del VPS. La capacidad depende de **mensajes/s, concurrencia, latencias, p95 de PostgreSQL/Redis/Firestore, forma y carga de Oracle, límites de cada proveedor, volumen de IA por diálogo y tasa de reintentos**.

Para visualizar un posible cuello de botella: la tabla pública de límites que muestra hoy Groq para `openai/gpt-oss-20b` incluye **30 RPM, 1,000 RPD, 8,000 TPM y 200,000 TPD** para un nivel mostrado en la página. **NO se ha verificado que ese sea el nivel de la cuenta de NexFlow**; Groq indica que pueden existir excepciones y que el panel de la organización es la fuente real. Además, otras tablas de su documentación de modelos muestran límites superiores para planes distintos. Con 2,150 tokens por llamada, el límite hipotético de **200,000 TPD permitiría solo 93 interpretaciones/día** (sin contar caché); el de 1,000 RPD permitiría 1,000 solicitudes/día, pero el límite de tokens se agotaría antes. Eso equivale a **~23 conversaciones/día en el perfil A o ~11 en el B** si todas las llamadas usaran exclusivamente Groq. Con un margen operativo del 30%, serían ~16 y ~8. No es una predicción de producción: demuestra la necesidad de conocer cuotas efectivas antes de prometer 20–30 empresas activas. Fuente: https://console.groq.com/docs/rate-limits.

La API de Gemini también limita RPM/TPM/RPD por **proyecto** y modelo; una segunda clave del mismo proyecto no duplica las cuotas. Fuente: https://ai.google.dev/gemini-api/docs/rate-limits.

**Distinción adicional:** los límites internos (6 IA/cliente/min; 60 IA/workspace/min) no suman una protección global por proveedor. Se recomienda una admisión global compartida con reserva de margen para todos los tenants, además de la admisión existente. Un único negocio intenso no debe monopolizar la cuota externa.

**Servidor Oracle:** solo conocemos ARM64 y que `/health/live` y `/health/ready` respondieron 200; no tenemos nproc, RAM, RAM restante, uso CPU/p95 bajo carga ni restricciones Compose. No asumir que la VM dispone del máximo teórico de Ampere A1. La documentación de Oracle y sus cupos varían entre planes; confirmar la forma asignada en el panel del propio VPS. Fuente: https://docs.oracle.com/en-us/iaas/Content/FreeTier/freetier_topic-Always_Free_Resources.htm.

### 5.5 Sondas seguras para el VPS (solo lectura; nunca carga masiva en producción)

```bash
nproc
free -h
df -h /
docker stats --no-stream --format 'table {{.Name}}\t{{.CPUPerc}}\t{{.MemUsage}}\t{{.MemPerc}}'
docker inspect nexflow_api --format 'MemLimit={{.HostConfig.Memory}} NanoCpus={{.HostConfig.NanoCpus}}'
```

Tomar las métricas bajo tráfico natural (sin spam de WhatsApp). Solicitar en consola privada de Groq los límites **RPM, RPD, TPM, TPD**, y en Gemini AI Studio el tier/cuotas del modelo, pero **nunca compartir claves**. Instrumentar métricas agregadas sin nombres ni teléfonos, incluidas llamadas, tokens `AiTelemetry`, latencias, 429, reintentos, mensajes en cola y sesiones por tenant.

## 6. Plan técnico recomendado: invertir tokens para mejorar experiencia

### Fase Q0 — Acotar producto y seguridad (antes de piloto)

- Pausar funcionalidad `ORDERS` (y revisar `REQUESTS` si permite solicitudes comerciales equivalentes) mediante **autorización del backend y entitlements**, no solo ocultar el menú en frontend. Asegurar que menús, selección ordinal, texto libre y APIs directas no inicien ni registren pedidos. Las conversaciones con un pedido previo activo necesitan ruta explícita de cierre o consulta, nunca eliminación silenciosa. No ejecutar cambios productivos sin aprobación.
- Mantener `CATALOG`, `SERVICES`, `RESERVATIONS`, `LOCATIONS`, `BUSINESS_HOURS` habilitados según licencia. `EnableContextualFocus` continúa global y apagado en API compartida. **Reservas reales siguen NO-GO hasta aclarar o aislar P0 histórico y comprobar la procedencia de datos reutilizados entre borradores**.

### Fase Q1 — Comprensión de intención prioritaria

- Definir objetivos de nivel superior independientes del step: `buscar atención`, `conocer servicio`, `reservar nueva`, `modificar reserva existente`, `mostrar menú principal`, `consultar catálogo`, `hablar con persona`, `abandonar borrador`. No inventar enums nuevos innecesariamente; mapear a contratos actuales.
- Capa determinista para `menu`, ordinals vigentes, confirmación/cancelación y transacciones. Una sola NLU de Groq para frases ambiguas con contexto ligero de **pregunta vigente, sede, selección actual y catálogo autorizado**. Evitar que el estado anterior fuerce a leer «consulta odontológica» como consulta genérica.
- Interpretación por **esquema de objetivo + evidencia + confianza calibrada**; confianza del modelo no autoriza un efecto. Si la IA declara un objetivo respaldado pero hay dos servicios plausibles, preguntar una sola aclaración específica.
- Incluir dataset de erratas, muletillas, español peruano y mensajes mixtos como «quiero la evaluación mañana y dime cuánto cuesta», con negative tests de falsos positivos.

### Fase Q2 — Guía y redacción natural sin hechos inventados

- Separar **plan de respuesta aprobado por backend** (hechos con origen, opciones, próxima pregunta y límites) de la **forma de presentarlo**. La presentación recibe solo datos autorizados, nunca puede crear una fecha, precio, disponibilidad, ID, profesional o confirmación.
- Plantillas editoriales por momento de conversación para el 70–85% de los turnos: saludo, elección de sede, servicio, fecha/hora, disponibilidad, corrección, falta de datos y explicación de catálogo. Suavizar idioma, reducir redundancias y usar solo una pregunta final.
- **Generación IA selectiva** en consultas informativas difíciles, comparaciones, expresiones imprecisas o preguntas que requieren explicación; empezar con Gemini Flash-Lite y comparar con Flash solo si mejora la evaluación humana. Para las respuestas transaccionales críticas (resumen, confirmación, error, consentimiento), conservar renderización determinista verificada.
- No activar simplemente `ResponseComposer.EnableAi` actual esperando un asistente «tipo AxionLabs»: su restricción es deliberada, hay que diseñar una composición contextual verificable nueva y acotada.

### Fase Q3 — Experiencia de reservas existente y modificación

- Preservar BookingFlow/ReservationEngine, fingerprint, lock, disponibilidad y consentimiento. Reducir textos pero mantener pasos. Si el usuario escribe «mañana en la tarde» al principio, conservar la preferencia si el contrato lo autoriza; luego preguntar solo la sede o servicio faltante.
- Tratar «quiero cambiar mi horario» como **gestión de reserva registrada**, nunca como dato del borrador anterior; reconocer la intención y solicitar identificación/verificación de manera segura o transferir a asesor si no hay operación permitida.
- Investigar reutilización de `RealCustomerName` después de descarte. Determinar si procede de identidad previamente autorizada del consumidor, estado residual o bug; tests `discard → new booking → review` y casos concurrentes/tenants.

### Fase Q4 — Medir antes de subir cuotas

- Ejecutar A/B con al menos 100 mensajes sintéticos representativos y mínimo 20–30 diálogos completos por variante. Preferir una cohorte DEMO aislada antes de un cambio global.
- **Criterios sugeridos:** identificación del objetivo ≥95% en corpus etiquetado; cero falsos consentimientos, cero cruce tenant y cero hechos inventados; ≥90% de sesiones informativas resueltas y ≥85–90% de reservas sintéticas con datos correctos; reducción ≥50% de fallbacks genéricos; puntuación de claridad ≥4/5 con lectores no técnicos; p95 de latencia monitorizado antes/después, no estimado como real.
- Capturar `tokens_input`, `tokens_output`, `ai_calls_per_conversation`, `coste_por_conversación`, `first_useful_response`, `clarification_loops`, `intent_accuracy`, `handoff_rate`, `p95_latency`, `provider_429`, tasas de pasos completados, con agregados sin PII.
- Establecer techo de gasto mensual por negocio/global y admisión justa por provider. No aumentar el límite 6/minuto de cada cliente hasta demostrar que la ganancia lo justifica.

## 7. Orden de ejecución propuesto

**Primero:** seguridad y menú sin pedidos → **segundo:** comprensión de intención y menú global → **tercero:** guiones y mensajes naturales para servicios, catálogo y reservas → **cuarto:** corrección de gestión de cita, estado de nombre y continuidad multiturno → **quinto:** selección de modelo y costo con evaluación real acotada → **sexto:** carga controlada en entorno aislado y cupo efectivo de negocios.

No iniciar otro rediseño de agent frameworks, no retocar motores transaccionales sin reproducción roja, no activar funciones globales de CTA en la API compartida y no generar respuestas clínicas automáticas.

## 8. Conclusión sobre el sacrificio de optimización

**Sí es justificable gastar más tokens** si el cliente percibe la mejora y la operación mantiene seguridad. El diseño del perfil C cuesta, en el caso de simulación X, **aprox. USD 18.84/mes en inferencia** frente a USD 4.68 del perfil A; este incremento es pequeño frente al costo de perder clientes, pero NO garantiza capacidad: cuotas RPM/TPM/RPD/TPD y latencia deben ampliarse o repartirse de forma legítima en cuentas con capacidad suficiente. Además, la experiencia puede mejorar sustancialmente con cambios de política y redacción local que apenas aumentan tokens.

**NexFlow está suficientemente avanzado para optimizar la experiencia, no para asegurar que soporta un número fijo de negocios.** El objetivo práctico es que una persona diga «quiero revisarme los dientes» y el bot le presente el servicio pertinente, responda una pregunta real y la lleve hacia una reserva segura sin repetir frases genéricas. Ese progreso debe demostrarse con usuarios que no hayan recibido instrucciones sobre cómo hablar al bot.

## 9. Referencias de mercado/proveedores

- Groq modelo y precios: https://console.groq.com/docs/model/openai/gpt-oss-20b
- Groq límites y nivel efectivo en consola: https://console.groq.com/docs/rate-limits
- Gemini precios: https://ai.google.dev/gemini-api/docs/pricing
- Gemini límites por proyecto: https://ai.google.dev/gemini-api/docs/rate-limits
- Oracle ARM y cuotas: https://docs.oracle.com/en-us/iaas/Content/FreeTier/freetier_topic-Always_Free_Resources.htm
- Código NexFlow: https://github.com/saintdeyb321/NexFlow/tree/39432d7bf870bf5e0ba370738b05e3bd750bc9ab
