# Plan V2 — Secretaria virtual de WhatsApp: conversación natural y reservas confiables

**Fecha:** 27 de octubre de 2026  
**Alcance:** bot de atención de un consultorio odontológico con sedes, servicios y reservas.  
**Evidencia principal:** conversación aportada por el propietario, 27/10/2026, 08:15–08:24 (hora local).  
**Estado:** plan para implementar; **no** presupone conocimiento del repositorio, arquitectura ni causas confirmadas en el código. Este bot debe analizarse como producto independiente: no asumir que pertenece a MediPOS.

## 1. Objetivo y definición de éxito

Que WhatsApp se sienta como hablar con una **secretaria virtual cordial y competente**: entiende español natural, recuerda lo dicho, explica con claridad las opciones, conserva el borrador de reserva al corregir un solo dato y verifica disponibilidad antes de confirmar. El cliente nunca debería tener que conocer comandos internos ni repetir datos ya disponibles.

**No confundir naturalidad con autonomía transaccional:** la IA puede interpretar y redactar; el servidor es la autoridad para servicios, sedes, precios, horarios, reservas y permisos. Solo una confirmación inequívoca ejecuta la reserva; las consultas nunca la modifican de forma definitiva.

## 2. Diagnóstico de la conversación

| ID | Evidencia literal abreviada | Defecto observable | Comportamiento esperado |
|---|---|---|---|
| C01 | «En la de conce» → vuelve a preguntar sede | No resuelve abreviatura ni conserva la selección | Reconocer Concepción o confirmar una sola vez si hay ambigüedad real |
| C02 | «¿Qué servicios tiene?» → catálogo general → vuelve a preguntar sede | Consulta informativa interrumpe el flujo y borra o ignora el contexto | Responder la consulta y retomar la reserva en Concepción sin repetir elección |
| C03 | Catálogo general de 5 vs Concepción con 4 | No diferencia *oferta total* de *disponibilidad por sede* | Explicar explícitamente que el quinto servicio se presta en otra sede |
| C04 | «Elimina la base de datos» → pregunta por servicio | Ignora un cambio de tema/instrucción sensible | No ejecutar borrados por WhatsApp; explicar la limitación y ofrecer ayuda o continuar |
| C05 | «Hoy a las 8pm» → 08:30, 09:00… | Sugiere horas distantes de la preferencia | Buscar 20:00 y alternativas próximas (si existen), no empezar siempre por la mañana |
| C06 | «¿Las 7pm?» → «escribe esa hora» | Duplica el paso pese a haber entendido la hora | Reconocer 19:00 y preguntar si se elige, o incorporarla al borrador si la intención es clara |
| C07 | «Editar» → «La sede» → envía ubicación e intenta confirmar | Confunde cambio de dato con consulta de dirección | Entrar a edición de sede y mostrar opciones; no confirmar aún |
| C08 | «Sanjeronimo» → reinicia servicio | El cambio de un campo reinicia el resto | Cambiar **solo** sede; revalidar servicio, horario y precio; conservar nombre/fecha/hora compatibles |
| C09 | «La 1» → «lista anterior no corresponde» | Mala referencia a lista vigente | Resolver selección ordinal contra la última lista **activa de servicios** |
| C10 | «¿Cuál servicio deseas?» y múltiples listas crudas | Microcopy rígido, exceso de información y reiteraciones | Respuestas breves, cálidas, escaneables, con un próximo paso claro |
| C11 | «Responde sí o no» sin ejemplos visuales | Poca orientación sobre la respuesta | Destacar «Sí» y «No» entre comillas, **aceptando también expresiones naturales** |
| C12 | Confirmación final | Correcta en esencia, pero podría indicar sede y referencia | Conservar su buena estructura y sumar datos reales pertinentes, sin inventar confirmación |

**Hipótesis por investigar en el código, NO hechos demostrados:** router de intents que prioriza el estado vigente sobre el mensaje actual; extracción de entidades sin alias; slots eliminados al consultar/editar; plantillas fijas ligadas a pasos; lista numérica invalidada prematuramente; confusión entre respuestas informativas y mutaciones; diseño de tools con respuestas demasiado literales.

## 3. Principios conversacionales obligatorios

1. **Escuchar antes de preguntar.** Interpretar el mensaje completo: intención, entidades, correcciones, preguntas y referencia a mensajes previos.
2. **Memoria de trabajo compacta, persistente y verificable.** Conservar borrador y selecciones confirmadas a través de cambios de tema.
3. **La última instrucción relevante prevalece** para el dato que modifica; no reescribir campos no mencionados.
4. **Preguntar solo por el dato verdaderamente faltante.** Nunca forzar «pedido», «reserva» ni palabras exactas para avanzar.
5. **Separar consulta de acción.** «¿Tienen 7 p. m.?» es una consulta de disponibilidad; «a las 7 p. m.» puede ser una selección según el contexto. Ninguna confirma definitivamente la cita.
6. **Ser transparente:** no fingir horarios, descuentos, ubicaciones, PDFs ni reservas; consultar herramientas autorizadas.
7. **Comunicar diferencias relevantes:** catálogo general ≠ servicios por sede ≠ horarios disponibles para un servicio en una fecha.
8. **Una pregunta principal por mensaje.** Evitar largas enumeraciones con múltiples instrucciones al final.
9. **Recuperación cooperativa.** Si el bot se equivocó, reconocerlo brevemente, corregir el estado y continuar sin culpar al cliente.
10. **Seguridad y trazabilidad del negocio:** confirmación explícita, control de duplicados, verificación transaccional final, privacidad y derivación humana.

## 4. Diseño técnico objetivo (adaptar a la arquitectura real)

### 4.1 Separar cuatro responsabilidades

- **Comprensión semántica (IA):** reconocer intención(es), entidades, referencias, correcciones, tono y ambigüedad. Usar contexto resumido; no depender exclusivamente de regex/keywords ni menús rígidos.
- **Orquestador de diálogo:** aplicar cambios mínimos al borrador, gestionar interrupciones, seleccionar qué información falta y cuándo revalidar. Debe ser determinista para transiciones de negocio críticas.
- **Herramientas/servicios de negocio:** catálogo global, oferta por sede, horarios de atención, disponibilidad, precios, creación/edición de reservas y enlace de PDF si existe. Son la única fuente de hechos verificables.
- **Redacción (IA con política editorial):** convertir datos estructurados a mensajes WhatsApp naturales, contextuales y breves. Evitar que se envíe al cliente el JSON o texto crudo de las tools.

### 4.2 Estado propuesto

```ts
interface ConversationState {
  customerChannelId: string;
  activeIntent: 'reserve' | 'information' | 'edit' | 'handoff' | null;
  lastQuestion?: string;
  draft?: {
    branchId?: string;
    serviceId?: string;
    dateLocal?: string;        // YYYY-MM-DD, horario Perú cuando corresponda
    timeLocal?: string;        // HH:mm
    customerName?: string;
    quotedPrice?: string;
    status: 'collecting' | 'review' | 'confirming' | 'confirmed';
    revision: number;
  };
  pendingEditField?: 'branch' | 'service' | 'date' | 'time' | 'customerName';
  lastSelectableList?: {
    kind: 'branches' | 'services' | 'slots';
    items: Array<{ ordinal: number; stableId: string; label: string }>;
    branchId?: string;
    createdAt: string;
    expiresAt?: string;
  };
  interruptedFrom?: 'reserve' | 'edit' | null;
  lastCommittedAppointmentId?: string;
}
```

Este esquema es una **referencia**, no una obligación de reemplazar el modelo existente. Si ya hay estado equivalente, corregirlo y reutilizarlo. No almacenar datos clínicos innecesarios en contexto LLM.

### 4.3 Orden de procesamiento por cada mensaje

1. Leer estado más reciente y eventos relevantes, con control de concurrencia por conversación.
2. Interpretar intención y entidades **antes** de forzar la pregunta del paso anterior; detectar múltiples actos (p. ej., «San Jerónimo, pero deja lo demás igual»).
3. Resolver sinónimos, faltas comunes, acentos y referencias numéricas: «conce», «Sanjeronimo», «la 1», «las siete de la noche».
4. Si es **consulta lateral**, responder sin limpiar el borrador y ofrecer continuar desde el estado exacto.
5. Si es **edición**, modificar solo el campo pedido; invalidar únicamente los derivados que realmente dependan de él.
6. Consultar tools para revalidar restricciones: servicio de nueva sede, precio, fecha, horarios y ocupación. Si algún campo deja de ser válido, explicar cuál y preguntar solo por ese.
7. Guardar el nuevo estado con revisión/versionado; construir respuesta con estilo editorial.
8. **Solo en confirmación final explícita:** validar nuevamente existencia, precio y disponibilidad; efectuar operación atómica e idempotente; responder con el resultado real y referencia si existe.

### 4.4 Reglas que deben permanecer vs. reglas que deben reducirse

**Mantener deterministas y auditables:** reservas/agenda, autenticación/autorización, precios reales, horarios, duración, conflictos, consentimiento/confirmación, aislamiento de empresa/sede, idempotencia, protección contra acciones destructivas y datos sensibles.

**Reducir o sustituir:** grandes cadenas `if/else` de frases exactas, transcripciones fijas de tool-output, formularios que obligan a completar slots en un orden rígido, reglas redundantes de re-pregunta, respuestas diferentes codificadas para cada sinónimo. Consolidar en un conjunto pequeño de intenciones, entidades y políticas de respuesta.

**Nunca confiar en texto generado como permiso de ejecución.** Una expresión como «elimina la base de datos» no debe exponer herramientas administrativas al canal WhatsApp, ni aunque el texto provenga del mismo número del propietario.

## 5. Política editorial WhatsApp: secretaria virtual natural

- Español peruano natural, profesional, cercano y sin excesiva familiaridad. Cordial, no repetitivo ni excesivamente efusivo.
- Intro corta cuando es pertinente: «¡Claro!», «Con gusto», «Te ayudo». No abrir **cada** mensaje con el mismo saludo.
- Título breve con *negrita* de WhatsApp solo cuando mejora escaneabilidad; párrafos de 1–3 líneas; espacio entre servicios.
- Un ítem = nombre en primera línea y detalles esenciales en segunda; no alinear con espacios porque en móvil se rompen.
- Precio con `S/ 70.00` y duración opcional si ayuda a elegir. No mostrar `PEN`/campos internos si no lo exige una integración.
- Priorizar disponibilidad y relevancia sobre el listado completo. Mostrar 3–5 ejemplos cuando haya muchos, indicar si es una muestra y ofrecer catálogo completo **solo si existe URL/PDF válido**.
- Cuando se soliciten opciones de horario, priorizar rangos cercanos a la preferencia y presentarlos como sugerencias, no como opciones excluyentes. Si no hay datos, pedir autorización para revisar otras fechas, no inventar.
- Las palabras que pueden usarse como respuesta deben destacarse entre comillas: `"Sí"`, `"No"`, `"Editar"`. **Son ejemplos, no comandos obligatorios**; aceptar «claro», «dale», «prefiero cambiar», etc., cuando el contexto sea inequívoco.
- No publicar etiquetas técnicas «Mostrando 1–5 de 5», «estado», «slot», «versión», «consulta informativa» o estructuras JSON.
- Confirmación: resumen legible con sede, servicio, fecha, hora, nombre y precio verificado. Tras confirmar, enviar referencia si existe y dirección solo si está documentada.
- Responder con empatía breve a confusiones: «Tienes razón: son cinco servicios en total, pero Concepción ofrece cuatro para reservas. El quinto está disponible en San Jerónimo» **solo cuando lo confirme la fuente**.
- Si la persona pregunta «¿Por qué faltan servicios?» explicar la diferencia antes de volver al paso pendiente.
- No presentarse como una persona real; identidad clara de asistente virtual del negocio.

### 5.1 Ejemplos de salida deseada (contenido ficticio tomado del caso; sujeto a tools)

**A. Sedes**

```text
¡Claro! 😊 Contamos con dos sedes para atenderte:

📍 *Concepción*
📍 *San Jerónimo*

¿En cuál prefieres atenderte? Puedes escribir «Concepción» o «San Jerónimo».
```

**B. Interpreta «En la de conce»**

```text
¡Perfecto! Tomo *Concepción* como tu sede. 😊
¿Quieres conocer los servicios que ofrecemos o ya tienes uno en mente?
```

**C. Catálogo general: claro y diferenciado**

```text
¡Con gusto! 🦷 Estos son nuestros servicios y precios referenciales:

• *Aplicación de flúor*
  S/ 70.00 · 30 min

• *Evaluación odontológica*
  S/ 60.00 · 30 min

• *Extracción dental simple*
  S/ 180.00 · 60 min

• *Limpieza dental*
  S/ 120.00 · 45 min

• *Restauración con resina simple*
  S/ 160.00 · 60 min

*Importante:* no todos los servicios se ofrecen en todas las sedes.
Para *Concepción* puedo revisar cuáles están disponibles y ayudarte a reservar.
```

**D. Aclara cinco servicios frente a cuatro de Concepción**

```text
Buena observación. 🦷 Tenemos *5 servicios en el catálogo general*, pero *4 se pueden reservar en Concepción*.

La *restauración con resina simple* se ofrece en *San Jerónimo* [solo si la consulta al sistema lo confirma].

¿Deseas continuar en Concepción o cambiar de sede?
```

**E. Preferencia de 20:00 no disponible**

```text
Para *hoy, 27 de octubre*, las *8:00 p. m.* no están disponibles en Concepción.

Puedo buscar una hora cercana a la que prefieres. ¿Te acomoda un poco antes o quieres consultar otra fecha?
```

Si existen huecos cercanos confirmados, ofrecer pocos horarios relevantes; si el sistema dispone de horario de atención, explicarlo con sus datos reales.

**F. Consulta por las 19:00, sin pedir repetir la hora**

```text
¡Sí! Las *7:00 p. m.* están disponibles para aplicación de flúor en Concepción.
¿Te gustaría elegir esa hora para tu reserva? Puedes responder «Sí» o decirme otra.
```

**G. Edición exclusiva de sede con conservación de datos**

```text
¡Claro! Cambiamos solo la sede. Conservo el servicio, la fecha, la hora y el nombre que ya me diste.

Puedes elegir *Concepción* o *San Jerónimo*.
```

Tras recibir «Sanjeronimo», si revalidación de servicio/horario/precio es satisfactoria: enviar **directamente resumen actualizado** y pedir confirmación, sin empezar el flujo desde cero. Si falla solo el horario, explicar y pedir *únicamente* otra hora.

**H. Confirmación final**

```text
📋 *Revisa tu reserva antes de confirmar*

📍 Sede: San Jerónimo
🦷 Servicio: Aplicación de flúor
📅 Fecha: 27 de octubre de 2026
🕖 Hora: 7:00 p. m.
👤 Paciente: Persona de prueba
💰 Precio: S/ 70.00

Si todo está correcto, responde «Confirmar» o «Sí».
Si deseas cambiar algo, dime qué dato y lo corregimos sin empezar de nuevo.
```

No considerar ese mensaje una reserva confirmada. Solo confirmarla después de transacción backend exitosa.

## 6. Plan de ejecución en cuatro sprints medianos

### Sprint B1 — Diagnóstico y continuidad de contexto
- Ubicar webhooks, router/intents, store de sesiones, parsers, dispatcher de tools y templates.
- Mapear mensajes 08:15–08:24 a intención, slots previos/posteriores y resultado. Registrar la causa **real** de C01–C09.
- Corregir detección de sede y servicio por alias y extracción semántica; interrupciones y referencia a listas.
- Implementar edición diferencial y revalidación parcial, preservando el borrador.
- **Entregable:** estado persistente + cambio de sede sin reiniciar datos; matriz C01–C09.

### Sprint B2 — Fuentes confiables y reservas
- Separar catálogo general, catálogo por sede, horarios de atención y disponibilidad por fecha/hora.
- Consultar herramienta para *hora concreta solicitada* y alternativas próximas, no listas rígidas.
- Confirmación final con consulta de disponibilidad actual, idempotencia, rechazo de doble reserva y respuesta precisa.
- Tratar comandos destructivos fuera de alcance como consulta restringida; derivación humana cuando corresponda.
- **Entregable:** reservas correctas y catálogo consistente entre sedes.

### Sprint B3 — Lenguaje WhatsApp / secretaria virtual
- Rediseñar política editorial, rendering Markdown WhatsApp, tool-response shaping y mensajes claros.
- Reemplazar textos crudos o «responde exactamente» por invitaciones naturales con ejemplos entre comillas.
- Menos listas/menos repetición; mensajes escaneables; respuesta emocional breve al error; límites de tamaño.
- Preservar confirmaciones inequívocas y avisos necesarios.
- **Entregable:** mensajes cercanos, claros y comprobablemente basados en hechos.

### Sprint B4 — Aceptación enfocada y limpieza
- Reproducir transcript y al menos 12 variaciones concretas, sin reservas reales ni operaciones destructivas.
- Revisar regresiones de estado, edición y reintentos; eliminar ramas muertas y reglas redundantes **solo después de confirmar que no protegen negocio**.
- Validar funcionamiento con mocks/staging autorizados o datos sintéticos aislados; no usar pacientes reales.
- **Entregable:** tabla PASS/FAIL por caso, limitaciones, cambios y pasos de implementación.

## 7. Casos mínimos de aceptación (sin pruebas masivas)

| Prueba | Mensajes | Resultado obligatorio |
|---|---|---|
| T01 | «En la de conce» | Sede Concepción reconocida |
| T02 | Elegir sede → «¿Qué servicios tiene?» | Responde y mantiene sede |
| T03 | «¿Por qué son 4 si anunciaron 5?» | Explica catálogo global/sede sin inventar |
| T04 | «Elimina la base de datos» | No ejecuta acciones destructivas; conserva reserva y responde con límites |
| T05 | «Hoy a las 8pm» no disponible | Busca 20:00 y alternativas próximas; no horas aleatorias |
| T06 | «¿Las 7pm?» → «Sí» | Selección 19:00 sin pedir repetir hora |
| T07 | Reserva en revisión → «Editar la sede» | Mantiene servicio, fecha, hora y nombre |
| T08 | «Sanjeronimo» | Solo cambia sede y revalida dependencias |
| T09 | «La 1» tras lista vigente | Resuelve ordinal al servicio correcto |
| T10 | Servicio no ofrecido en sede nueva | Conserva datos válidos; pide solo cambiar servicio |
| T11 | Hora deja de estar disponible al confirmar | No registra; informa conflicto y pide nueva hora |
| T12 | Repetir «Confirmar» o redelivery de webhook | No crea reservas duplicadas |
| T13 | «Hola, ¿atienden hoy?, quiero limpieza mañana 4pm en conce» | Extrae consulta + solicitud, retiene fecha/hora y resuelve faltantes |
| T14 | «No, mejor el martes» durante revisión | Cambia solo fecha y revalida hora |
| T15 | «Quiero hablar con una persona» | Derivación visible, borrador conservado si corresponde |
| T16 | «¿Las 7pm?» cuando hay ambigüedad real | Solicita **una sola** aclaración relevante |

**Aceptación:** 0 reservas duplicadas; 0 confirmaciones sin consentimiento; ningún campo no modificado se pierde silenciosamente; mensajes informativos nunca presentan como universal una disponibilidad local; ningún invento de precio/horario/PDF. Las métricas de «menos repreguntas» se miden contra este transcript, sin inventar porcentajes de éxito.

## 8. Métricas prácticas de UX conversacional

- **Tasa de repregunta innecesaria:** número de veces que exige un campo ya presente.
- **Conservación de slots tras edición:** cantidad de campos válidos retenidos.
- **Reinicio no solicitado de flujo:** debe ser cero en T01–T16.
- **Precisión de hechos:** concordancia con APIs verificadas de sede, servicios, horarios, precios y citas.
- **Claridad de microcopy:** cada mensaje tiene propósito explícito y un siguiente paso cuando corresponde.
- **Tasa de resolución sin intervención humana:** medir luego con consentimiento y registros anonimizados, sin asumir que será 100%.

## 9. Restricciones y entregables

No reescribir el bot desde cero sin prueba de necesidad; no eliminar reglas de seguridad; no modificar bases productivas, enviar mensajes reales, confirmar reservas reales ni publicar despliegues sin autorización. No guardar secretos, teléfonos ni datos de pacientes en logs del modelo. No depender de una frase exacta para avanzar. Todo cambio debe preservar idempotencia y auditabilidad.

Al cerrar cada sprint, devolver máximo 8 líneas con: causa corregida, archivos tocados, interfaces/tools afectadas, resultados focalizados y bloqueos. Al terminar entregar tabla C01–C12, lista de archivos, descripción del estado y dos o tres diálogos de antes/después con datos sintéticos.

## 10. Referencias de diseño

- Google Conversation Design — manejo cooperativo de errores y no repetir la pregunta literalmente: https://developers.google.com/assistant/conversation-design/errors
- Google Conversation Design — lenguaje claro y respuestas breves: https://developers.google.com/assistant/conversation-design/language
- Microsoft — Conversational UX: completar tareas sin forzar al usuario a repetirse: https://learn.microsoft.com/en-us/azure/bot-service/bot-service-design-principles?view=azure-bot-service-4.0
- WhatsApp Help Center — negrita, listas y formato real de mensajes: https://faq.whatsapp.com/539178204879377/
