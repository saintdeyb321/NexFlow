# NexFlow — Prompts por fase para Codex

**Documento:** `NEXFLOW_AGENT_RUNTIME_4_PROMPTS_CODEX.md`  
**Versión documental:** 1.0 · 25/10/2026  
**Proyecto:** NexFlow · `C:\Proyectos\NexFlow` · `https://github.com/saintdeyb321/NexFlow`  
**Base pública examinada:** `0da524d6e840ce6aeec77950d8e1f82b42c53e16` (verificar checkout local al iniciar).  
**Acompañar de:** `NEXFLOW_AGENT_RUNTIME_4_PLAN_MAESTRO.md` (debe situarse en el contexto de Codex antes de usar los prompts).

> **Modo de uso:** entregar a Codex el **Bloque común + un solo prompt de fase**. Empezar por F0. No enviar todas las fases como una orden única. No pasar a la siguiente fase hasta que el usuario haya auditado y aprobado la anterior. Si Codex no puede leer el plan maestro, adjuntarlo primero. Si el plan y el código discrepan, prevalece el código verificado y se documenta el cambio necesario; nunca afirmar implementación basándose solo en el plan.

---

## BLOQUE COMÚN — pegar junto con cada prompt

```text
Eres el arquitecto y desarrollador principal de NexFlow. Trabaja sobre el checkout LOCAL en C:\Proyectos\NexFlow, no sobre una copia hipotética. Antes de modificar, lee AGENTS.md (si existe), NEXFLOW_AGENT_RUNTIME_4_PLAN_MAESTRO.md, NexFlow_Concierge_3_Plan_Maestro.md y los documentos relevantes de docs/concierge-3/. Verifica el estado efectivo del árbol de trabajo y la versión base mediante archivos locales; no ejecutes Git por iniciativa propia.

NexFlow es un SaaS multiempresa para atender a clientes por WhatsApp. Ofrece información del negocio, catálogos, reservas de SERVICIOS y solicitudes de PEDIDOS. El pedido es una lista para revisión por el negocio: se notifica al dueño/encargado para que confirme disponibilidad, acuerde atención o recogida. NO es un procesador de pagos, ERP, POS, pasarela, cobro, facturación ni garantía de stock/entrega.

META: convertir la interfaz conversacional actual en un runtime de agente multiempresa, con una configuración lógica y contexto aislado por workspace, sin programar árboles de frases ni crear un bot/contenedor/modelo entrenado por cada empresa. El LLM interpreta el lenguaje y, si procede, ayuda a comunicar hechos; los motores y repositorios deterministas validan permisos, datos, borradores, consentimiento y operaciones.

REQUISITO: MANTENER AiRouter/IAiRouter y sus proveedores configurados Groq/Gemini; permitir mejoras compatibles de telemetría, JSON y fallback cuando la fase lo autorice. No sustituirlos ni agregar framework de agentes por moda. No reemplazar ReservationEngine, BookingFlow, OrderFlow, KnowledgeService, FIFO, Outbox o el estado persistido por implementaciones paralelas.

PROHIBIDO durante todas las fases: git checkout/pull/push/commit/rebase, despliegues, Docker en producción, migraciones, Oracle, secretos, bases productivas, envíos WhatsApp reales o llamadas pagadas a IA sin autorización separada. No cambiar frontend ni implementar módulo de empleados; registrar dependencias futuras sin programarlas. No generar un sistema de pagos. No usar usuarios, teléfonos, nombres o conversaciones privadas en logs ni corpus versionado.

GARANTÍAS: aislamiento workspace, licencias, autorización por herramienta, CAS/StateVersion, idempotencia por mensaje y operación, FIFO, Outbox y UnknownDelivery sin reenvío ciego, control humano prioritario, handoff, confirmación explícita sobre resumen vigente, dato de negocio respaldado, no acceso privado a reserva existente sin acreditación. Frente a ambigüedad o fallo IA, conservar contexto y no realizar escritura comercial.

PROCESO: implementa SOLO la fase autorizada. Evita un refactor de todo el proyecto. Si una modificación de arquitectura exige migración, cambio de licencia o ruptura de contrato, detente en propuesta y solicítalo en el informe: no lo ejecutes. Reutiliza la infraestructura de pruebas existente; no añadas mocks/fixtures nuevos sin justificar por qué los existentes son insuficientes. Nunca suavices asserts o finjas pruebas reales. Ejecuta build y las suites dirigidas disponibles cuando la fase autorice código; informa comandos, conteos, errores y omisiones exactos.

CRITERIO DE DESARROLLO: eliminar condiciones lingüísticas redundantes, no desplazarlas a otras clases. Los únicos textos rígidos intencionales son seguridad, consentimiento, estados transaccionales, ayuda mínima y fallback. La recuperación no debe perder la intención original ni preguntar indefinidamente lo mismo. La interpretación local inequívoca consume 0 tokens; la ambigua usa presupuesto acotado y evidencia verificable.

ENTREGA: diagnóstico con ruta:líneas y riesgo, lista exacta de archivos, diff razonado, regresiones demostrables, build/test y resultados, impacto en tokens/lecturas si se midió, deuda explícita, propuesta GO/NO-GO para la fase. Detente tras el informe. No avances a la siguiente fase ni declares listo para producción.
```

---

## PROMPT 0 — Diagnóstico arquitectónico y baseline (**SOLO LECTURA/INFORME**)

```text
FASE AUTORIZADA: F0 — AUDITORÍA Y BASELINE. NO MODIFICAR CÓDIGO DE APLICACIÓN NI PRUEBAS. Solo documento de diagnóstico bajo docs/agent-runtime/00_BASELINE_DIAGNOSTICO.md.

Inspecciona el camino real Evolution -> Inbound -> deduplicación/FIFO -> workspace/licencia -> contexto -> ConversationRecoveryService -> DialoguePolicy -> TurnUnderstandingPipeline -> AiInterpreter/AiRouter -> BookingFlow/OrderFlow/KnowledgeService -> ResponsePlan/ResponseComposer -> Outbox/Evolution. Usa rutas, contratos y puntos de retorno reales.

1. Identifica quién tiene hoy autoridad sobre la interpretación, el menú, la última pregunta, la confirmación y los borradores. Explica dónde hay decisiones repetidas o contradictorias y qué pasaría si uno de los componentes devuelve un resultado diferente.
2. Diagnostica los fallos de proveedor y JSON: error HTTP/429, configuración, timeout, InvalidResponse, evidencia rechazada, presupuesto y cuándo se dispara fallback. Distingue rutas visibles en código de incidentes confirmados por logs; no accedas a Oracle.
3. Reconstruye con evidencia de código los escenarios observados: pedido inicial que cae en fallback, consulta sobre actividad del negocio, RUC, seguimiento de horarios de otra sede, «sí» tras invitación a reservar, servicio reconocido pero no seleccionado, horarios enumerados por defecto, «¿qué?» tras consulta a las 4, nombre en minúsculas, confirmación insegura y solicitud de editar cita existente.
4. Inventaría reglas lingüísticas y mensajes fijos: clasifica seguridad/consentimiento/estado versus heurística lingüística/UX duplicada. Señala candidatos concretos a eliminar o fusionar.
5. Mapea datos autoritativos de perfil, FAQs, servicios, productos, horarios, reservas y solicitudes de pedido; anota permiso, tenant, coste probable de lectura y exposición de datos.
6. Define un corpus de evaluación anonimizado y variado para clínica, hotel y tienda, incluyendo frases inéditas fuera de las utilizadas para programar. Distingue evaluaciones semánticas locales de proveedores reales.
7. Diseña la migración incremental del agente compartido por workspace sin crear un segundo estado persistido. Incluye interfaces/contratos propuestos y diagramas sencillos, pero no código.
8. Presenta indicadores baseline disponibles y los que no pueden medirse sin proveedor o canal real. Jamás inventes porcentajes, tokens o costes.

ENTREGA SOLO docs/agent-runtime/00_BASELINE_DIAGNOSTICO.md, con hallazgos P0/P1/P2/P3, matriz causa→archivo→impacto, plan de pruebas y recomendaciones de F1 a F7. No avances; esperaremos aprobación G0.
```

**Revisión del usuario tras F0:** comprobar que distingue «hecho del código» de «hipótesis del proveedor», que no propone reescribir motores ni añadir cientos de regex, y que ningún riesgo de transacción queda olvidado.

---

## PROMPT 1 — Comprensión coherente y un solo resultado por turno

```text
FASE AUTORIZADA: F1 — GESTIÓN DEL TURNO Y PRECEDENCIA. Parte del diagnóstico F0 aprobado. Realiza un cambio incremental y verificable, sin introducir otro orquestador o segunda base de estado.

Objetivo: reducir la contradicción entre ConversationRecoveryService, DialoguePolicy, TurnUnderstandingPipeline y AiResponseOrchestrator. Un mensaje debe generar una interpretación coherente y una única acción autorizada. No renombres/refactorices indiscriminadamente el backend.

A. Conserva las rutas de máxima prioridad: modo humano, seguridad de licencia, cancelación explícita, disponibilidad de contexto versionado, consentimiento final y deduplicación. Deben seguir teniendo precedencia sobre el LLM.
B. Ajusta el contexto efímero del turno para tener una referencia a la última pregunta válida y la acción esperada; reutiliza ConversationContextDto/DialogueTurnContext y sus mecanismos existentes. No agregues otro estado autoritativo. Evita que un menú viejo intercepte «sí» o una hora si la pregunta actual es otra.
C. En caso de interpretación inequívoca local, no llamar IA. Si falta entendimiento semántico, pasar una proyección contextual mínima al AiInterpreter existente por AiRouter y validar evidencia literal/entidades autorizadas.
D. Diferencia con tipos/contratos: consultar, responder dato, seleccionar, corregir, suspender/reanudar, confirmar operación, cancelarla y tomar asistencia humana. Mapea a enums existentes en la medida de lo posible; no inventes stringly-typed commands libremente.
E. Evita un árbol de frases. Reconocer incertidumbre como incertidumbre. Aclarar solo lo faltante; no repetir «necesito respuesta concreta» y cinco menús si el cliente ya ofreció un dato válido.
F. Mantén operación comercial existente sin cambiar su implementación en esta fase; cualquier duda de compatibilidad requiere una prueba regresiva.

PRUEBAS: «sí» tras ofrecimiento de empezar reserva inicia borrador pero no crea; «sí» tras pregunta informativa no confirma transacción; «sí confirmo» tras resumen vigente sí puede activar la operación validada; «¿qué?» solicita aclaración contextual; «ya te lo dije» no borra dato previo; selección de menú antiguo se rechaza sin pisar el paso actual; takeover humano suprime respuesta; no replay de efectos. Variantes inéditas que no sean copias de las frases originales.

ENTREGA docs/agent-runtime/01_TURNO_Y_DIALOGO.md, diff, build/suite, tabla antes/después y límites. No implementar herramientas F2 ni módulos de empleado. Detente para gate G1.
```

---

## PROMPT 2 — Herramientas informativas, grounding y agente lógico por workspace

```text
FASE AUTORIZADA: F2 — HERRAMIENTAS DE SOLO LECTURA Y CONTEXTO POR WORKSPACE. Requiere aprobación G1.

Objetivo: que NexFlow utilice el conocimiento autorizado del negocio cuando se necesita, sin poner todo el catálogo en el prompt ni crear un Claude Project separado para cada cliente.

1. Diseña contratos tipados de lectura sobre fuentes YA EXISTENTES: perfil público, búsqueda de servicios/productos, horas por sede, FAQs y disponibilidad de reservas. Identifica qué servicios/repositorios se reutilizan exactamente; no dupliques el motor de reservas.
2. Construye el contexto lógico del agente para el workspace desde licencia/perfil/estado disponibles; sin scripts para provisionar otro bot, sin nueva colección/tables persistente por defecto. La configuración versionada persistida será un cambio posterior SOLO si se demuestra necesaria.
3. El LLM puede elegir una herramienta/consulta de lectura dentro del catálogo efectivo, pero nunca pasar WorkspaceId de texto ni traer entidades no autorizadas. Máximo acotado de herramientas por turno, límite de datos devueltos, paginación y deadline total; sin loops autónomos infinitos.
4. Los hechos son estructurados e incluyen fuente, scope, estado de disponibilidad e incertidumbre. Distinguir horario de atención de agenda libre; ausencia de horario global no significa sedes cerradas.
5. Para el RUC/taxId no asumir divulgación pública: confirmar política del negocio y control de divulgación; documentar y probar antes de exponerlo. La ausencia de rubro formal no autoriza inventarlo como hecho oficial.
6. Si hay documentos extensos, proponer RAG bajo demanda con aislamiento por workspace y fuentes citables; NO agregar vectores, embedding, índice o proveedor nuevo en F2 si los datos son estructurados o no está justificado.
7. Mantener respuesta útil cuando la IA o fuente está fuera de servicio; no presentar respuestas fabricadas.

PRUEBAS: un agente lógico de clínica y otro de tienda con el MISMO código; separación de workspace; permisos denegados; consultas equivalentes con ortografía distinta; no fuga de precio/FAQ de otro negocio; ausencia/indisponibilidad diferenciadas; máximo de herramientas/lecturas y cero efectos comerciales.

ENTREGA docs/agent-runtime/02_HERRAMIENTAS_LECTURA.md y diff acotado. No implementar F3/F4 ni tocar frontend. Detenerse en G2.
```

---

## PROMPT 3 — Reservas conversacionales sin menú obligatorio

```text
FASE AUTORIZADA: F3 — RESERVAS NATURALES. Requiere G2.

Objetivo: los clientes pueden aportar servicio, sede, fecha y hora en el orden que quieran. El asistente conduce la conversación haciendo solo una pregunta útil por vez y validando la disponibilidad mediante ReservationEngine.

1. Reutiliza BookingFlow, IReservationEngine y GetAvailabilityAtAsync. No cambies transacciones, conflictos, recursos, duración, zona horaria, idempotencia o política de consentimiento para simplificar UX.
2. Para COLLECT_TIME, pregunta hora preferida sin listado automático. No asumas que las primeras cinco horas de la mañana son todas las horas disponibles. Solo muestra opciones si el usuario las solicita o si su preferencia no puede atenderse y hay alternativas reales cercanas.
3. «¿Hay a las 4?» es consulta de lectura; «quiero esa hora» es selección sujeta a validación; «sí confirmo» tras resumen vigente permite creación. Nunca fusionarlas.
4. Interpreta nombres personales en minúsculas sin exigir mayúsculas ni nombre literal precedido por «me llamo». Conservar evidencia, rechazar etiquetas comerciales inequívocas y preguntar cuando exista ambigüedad real; no corregir nombres propios por LLM.
5. Usar datos ya aportados en mensajes separados o combinados. Una corrección de sede/fecha invalida exclusivamente campos dependientes. «¿Qué?» debe explicar el último resultado, no resetear la agenda.
6. Reservas ya REGISTRADAS: preservar guard de privacidad; transferencia humana si no hay acreditación independiente aprobada. No convertir la intención de editar cita existente en crear nueva.

PRUEBAS: horas 12/24h, una hora libre no presente en cuadrícula inicial, horario de cierre, servicio por sede, fecha en pasado, ambigüedad AM/PM, varias entidades en un mensaje, cambios de opinión, recuperación después de interrupción, nombres en minúsculas, confirmación estricta, replay, takeover y fallos de proveedor.

ENTREGA docs/agent-runtime/03_RESERVAS_NATURALES.md, diff, build, suite y evidencia de no transacción en consultas. Detente en G3.
```

---

## PROMPT 4 — Pedidos como solicitudes comerciales, NO pagos

```text
FASE AUTORIZADA: F4 — SOLICITUDES DE PEDIDO POR WHATSAPP. Requiere G3.

Definición vinculante: NexFlow NO procesa pagos, no descuenta stock de forma autoritativa ni promete que el pedido fue aceptado. Captura una lista solicitada, pregunta datos faltantes, ofrece resumen para confirmación y avisa al dueño/encargado autorizado. La empresa decide disponibilidad, precio final sujeto a política y coordinación de recogida/respuesta.

1. Reutiliza OrderFlow, catálogo autorizado, solicitudes/avisos actuales, datos de Firestore, sourceMessageId e idempotencia. No cambiar modelo comercial ni crear nuevo motor Order.
2. «Quiero hacer un pedido» inicia preparación aun sin nombrar producto. El bot pide qué productos/cantidades desea o ofrece consultar catálogo; no responde con un menú general ni exige keywords literales especiales.
3. Permite productos/servicios coexistentes sin convertir una consulta de servicio en compra de producto. Diferencia preguntas informativas e hipótesis de acciones sobre borrador.
4. Las cantidades/ítems se extraen de forma contextual y se validan contra catálogo/licencia vigente. Mantener anáforas («dos más», «quita el anterior»), aclaración ante varios candidatos y corrección del borrador, sin condiciones específicas por nombre del producto.
5. Mensaje final: «Solicitud de pedido enviada al negocio para revisión» únicamente cuando el efecto y notificación definidos estén confirmados duraderamente. Jamás «pedido pagado», «listo para retirar», «aceptado» o «stock confirmado» sin evento autoritativo correspondiente.
6. Modo humano: encargado responde en panel existente; no automatizar una promesa comercial que no emitió.
7. Reutilizar permisos y capacidades; no introducir módulo de empleados todavía.

PRUEBAS: pedido vacío, lista multítem, cantidad ambigua, cambio de unidad, anulación del borrador, catálogo vacío/no licenciado, no inventar productos, éxito con notificación única, replay, transporte incierto y contexto humano. Prueba E2E simulada local distinta de validación real WhatsApp.

ENTREGA docs/agent-runtime/04_PEDIDOS_SOLICITUDES.md, diff y suites. No introducir pagos ni F5. Detenerse G4.
```

---

## PROMPT 5 — Redacción contextual con hechos verificables

```text
FASE AUTORIZADA: F5 — RESPUESTA NATURAL SEGURA. Requiere G4.

Objetivo: reemplazar respuestas repetitivas y listas automáticas por mensajes claros, contextuales y específicos al negocio sin dar al LLM permiso de modificar hechos o estados.

1. Audita ResponsePlan, ResponseComposer, ChatFlow y textos rígidos. Conserva avisos de seguridad, confirmaciones y resultados de transacciones como backend-owned.
2. Diseña una salida de composición anclada en hechos tipados (precios, divisa, sede, fechas, disponibilidad, status y referencias a opciones). Validación independiente del modelo con fallback a texto verificado. NO publicar texto libre si la verificación de significado no es demostrable.
3. No restringir toda conversación a un diccionario de cortesías; tampoco sustituir el problema por cientos de plantillas. La variación puede provenir de organización y formulación de partes no factuales dentro de un contrato verificable.
4. Personalización de tono del agente por negocio solo con política legítima y contexto necesario. No enviar datos personales ni perfiles completos al modelo para decir «claro».
5. Una respuesta corta pregunta algo útil; no repetir el mismo menú al decir «¿qué?» o «ya te lo dije». Evitar listas salvo petición expresa o necesidad de desambiguar.
6. Usar AiRouter existente para composición opt-in y presupuestada; si falla, ResponsePlan.FallbackText exacto.

PRUEBAS: inversiones de «disponible/no disponible», mutación de precios/fechas/unidades, citas cambiadas, duplicación de preguntas, omisión de advertencias obligatorias, inyección en nombre de negocio, expiración de estado y takeover durante composición, latencia/presupuesto/fallback.

ENTREGA un contrato de salida más informe docs/agent-runtime/05_COMPOSICION_SEGURA.md. Implementa solo lo que permita verificación independiente de hechos; si no hay prueba suficiente, entregar propuesta y mantener la redacción actual. Detente en G5.
```

---

## PROMPT 6 — Resiliencia y economía multiempresa

```text
FASE AUTORIZADA: F6 — OPTIMIZACIÓN DE COSTO, LATENCIA Y CALIDAD. Requiere G5.

Objetivo: medir y optimizar un runtime que atiende múltiples workspaces; mantener AiRouter/IAiRouter, sin entrenar un modelo por empresa y sin nuevos contenedores por tenant.

1. Audita la instrumentación actual: IA por tipo de tarea, proveedor, éxito, timeout, HTTP 429, invalid JSON, rechazo de evidencia, fallback, presupuesto, invocaciones y latencia. Añade solo métricas agregadas necesarias. Nunca registrar entradas privadas, prompts, API keys o nombre/identificador de clientes.
2. Si el proveedor ofrece token usage, capturarlo de manera sanitizada y asociarlo al workspace autorizado; si no está disponible, no inventarlo. Determinar coste mediante tarifas/configuración efectivas separadas del código.
3. Verificar deduplicación por mensaje, límites por contacto/workspace, acotación de invocaciones y de tool calls; sin retries automáticos no limitados. Degradación segura cuando IA está caída, sin repetir siempre «¿Qué necesitas?».
4. Revisar lecturas de Firestore redundantes (por ejemplo validación final de nombre, perfiles, catálogos y recuperación de contexto) sin debilitar la autoridad de estado. Medir antes/después, no añadir caché insegura basada en suposiciones.
5. Evaluar clínica, hotel y tienda con el mismo código y diferentes configuraciones autorizadas, incluyendo conversaciones nuevas adversariales y típicas. Conservar suites de seguridad/durabilidad.
6. Proponer flags por workspace, rollout progresivo, kill switch, modo shadow sin mensajes duplicados, panel de métricas mínimo y límites de plan comercial. No desplegar.

ENTREGA docs/agent-runtime/06_EFICIENCIA_Y_EVALUACION.md, tabla de experimentos y resultados verdaderamente ejecutados, coste por 1.000 conversaciones sólo si se dispone de datos reales, plan de fallback y GO/NO-GO. Detente G6.
```

---

## PROMPT 7 — Preflight de piloto comercial y rollout (**SIN DESPLEGAR**)

```text
FASE AUTORIZADA: F7 — PREPARACIÓN DE PILOTO. Requiere G6. SOLO documentación operativa, scripts de LECTURA y checklist. No ejecutar SSH, Docker, producción, WhatsApp ni pruebas de proveedor.

1. Identifica commit y diff con la imagen ACTIVA real solo a partir de evidencia suministrada por operador. No adivinar qué versión está en Oracle.
2. Preparar verificación de compilación arm64, configuración Compose efectiva, herramientas, licencias, API health, backups restaurables, rollback compatible y redes sin reiniciar servicios compartidos.
3. Plan de activación únicamente de servicio API, si el diseño lo permite, con bandera por workspace y reversión de runtime. Prohibido docker compose down/prune/migrator por defecto.
4. Diseñar piloto: consultas de perfil/horarios/servicios; borrador de reservas sin confirmación; reserva de prueba con autorización expresa; solicitud de pedido ficticio con aviso único; takeover/release humano; falla de proveedor; varias sedes y workspaces; prueba de entrada ortográfica inédita.
5. Definir métricas, condición de parada y evidencia anonimizada; no almacenar teléfonos/mensajes privados completos en repo.
6. Separar niveles de GO: GO local, GO piloto privado, GO despliegue limitado y GO venta comercial; no confundirlos.

ENTREGA docs/agent-runtime/07_PILOTO_RELEASE.md. Solo proponer comandos de ejecución que el usuario podría autorizar luego, sin ejecutarlos. Terminar solicitando auditoría/decisión de rollout; no activar nada.
```

---

## PROMPT COMPLEMENTARIO — Auditoría independiente al cerrar cada fase

Puede emplearse para revisar el informe de Codex con ChatGPT u otro auditor que tenga acceso al diff **real**; no sustituye la aprobación del usuario:

```text
Audita la fase Fx del Agent Runtime de NexFlow sin escribir ni desplegar código. Base declarada: [SHA]. Commit/árbol candidato: [SHA o diff local adjunto]. Informe Codex: [archivo]. Compara implementación, pruebas y documentación con el Gate Gx del Plan Maestro. Busca rutas que contradigan consentimiento, workspace/licencia, CAS, FIFO, Outbox, handoff, límites de IA, no pagos, solicitudes de pedido y fidelidad factual. Distingue fallos demostrados de riesgos. Entrega veredicto: PASS / PASS CONDICIONADO / NO-GO, hallazgos P0–P3 con líneas y regresiones necesarias, sin inventar resultado de pruebas no ejecutadas ni certificar proveedores reales.
```

## Checklist de cierre de una fase

- [ ] Alcance F0–F7 correcto; ninguna fase posterior adelantada.
- [ ] `AiRouter` y proveedores preservados (o cambios compatibles explícitamente autorizados por fase).
- [ ] No sistema de pagos, stock confirmado o logística inventada.
- [ ] No empleado/roles/tab nuevo introducido prematuramente.
- [ ] Contratos de Workspace/licencia/verificación respetados.
- [ ] Una consulta no compromete reserva ni pedido; consentimiento válido siempre precede al efecto.
- [ ] Sin duplicados de Outbox, reenvío ciego de `UnknownDelivery` ni respuesta bajo takeover humano.
- [ ] Los hechos proceden de fuentes autorizadas; RUC/datos personales solo según política de publicación.
- [ ] No cientos de excepciones lingüísticas nuevas para cubrir variantes.
- [ ] Build y pruebas registradas honestamente; proveedores reales no declarados si no probados.
- [ ] Diff acotado, mediciones diferenciadas de estimaciones, documentación de deuda/rollback.
- [ ] Codex se detuvo; el usuario/auditor decide el siguiente Gate.

## Proyecto futuro **separado** — empleados

**No existe un prompt de implementación del módulo de empleados en este documento.** Cuando el runtime del agente supere el piloto, se diseñará otro plan dedicado al panel del dueño: invitación de empleados, delegación de módulos/capacidades, vistas autorizadas de conversaciones y solicitudes, toma/liberación humana, auditoría y revocación. El empleado usará la licencia del negocio sin recibir todas las atribuciones del dueño: permisos efectivos por intersección de licencia + delegación + rol. Su identidad y datos internos no deben formar parte del conocimiento público del agente.

---

**Primer mensaje a enviar a Codex:** adjuntar ambos Markdown y pegar **BLOQUE COMÚN + PROMPT 0**. Pedirle detenerse con el diagnóstico; no enviarle F1–F7 como tareas simultáneas.
