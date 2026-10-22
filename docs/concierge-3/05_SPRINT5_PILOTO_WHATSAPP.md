# Concierge 3.0 — Sprint 5: preparación del piloto WhatsApp

**Primera versión: Gate A preparado; piloto BLOQUEADO. No se ejecutaron conversaciones reales.**

## A. Identidad, alcance y autorización

Referencia candidata aportada por el propietario: `8104f05ef765dce4dc2cd51059fb51abf9d801dc` (Sprint 4). Código examinado: archivos locales efectivos del flujo descrito abajo. No se verificaron HEAD, árbol ni publicación: está prohibido ejecutar Git/GitHub. Los SHA de Sprint 0 y de los runbooks son históricos y no identifican esta candidata ni Oracle.

Versión desplegada, imagen activa, digest y configuración efectiva: **NO VERIFICADOS**. No se accedió a Oracle, WhatsApp, proveedores, bases reales ni logs remotos. No consta autorización de despliegue, piloto o escrituras comerciales de prueba.

| Gate | Estado actual | Condición de apertura |
|---|---|---|
| A — Preparación local | OK para esta entrega | Lectura, trazado, checklist, ocho guiones y revisión estática de correlación realizados. No certifica infraestructura ni calidad real. |
| B — Despliegue | NOT_RUN / NO AUTORIZADO | Aprobación explícita, candidata/build identificados, servicios/rollback revisados, workspace/contacto y observación delimitados. No hace falta desplegar si se demuestra que el entorno ya ejecuta exactamente la candidata. |
| C — Conversaciones | BLOCKED | Identidad de ejecución probada y autorización explícita del piloto, canal y observación. Una reserva/pedido de prueba requiere además autorización de escritura y destino seguro. |
| D — Cierre funcional | BLOCKED | Transcripciones supervisadas anonimizadas y evidencias reales de respuestas/efectos; no sustituibles por tests. |

Los mensajes del futuro piloto generarán registros técnicos durables de entrada, diálogo y salida. La autorización de conversaciones informativas no autoriza crear pedidos/reservas, modificar configuración ni manipular documentos para reiniciar sesiones.

### Reconocer la versión desplegada sin Git

1. Operador autorizado identifica todos los procesos API/workers y servicios reales, proyecto Compose y orden de overrides. Recopila **sólo** metadatos de imagen/configuración de ejecución, no un volcado de ENV o Compose expandido. No asumir nombres de contenedores.
2. Conservar etiqueta solicitada, ID inmutable de imagen, RepoDigest disponible, plataforma Linux/arm64, hora de inicio y procedencia del artefacto. El ID de configuración de imagen y el digest de un manifiesto/índice de registro no son intercambiables: comparar cada uno con su campo correspondiente del manifiesto de release/build.
3. Exigir evidencia que vincule esos identificadores con la candidata aprobada y su build: manifiesto/metadatos de publicación conservados, procedencia del build o atestación del operador con huellas de los artefactos. Una etiqueta SHA mutable, un contenedor healthy o la afirmación de que main está actualizado no bastan. Verificar también procesos antiguos que puedan seguir escribiendo.
4. Contrastar entrypoint API/worker, entorno Production y artefacto compatible anterior para rollback. `backend/Dockerfile:17–33` ya tiene usuario no root y entrypoints separados API/migrator, pero no declara LABEL de revisión ni ARG de versión. No se encontró un endpoint de versión en Program; health no identifica la release.
5. El archivo **generado local**, `backend/NexFlow.API/obj/Debug/net10.0/NexFlow.API.AssemblyInfo.cs:16–20`, todavía declara `1.0.0+b038c6ea16517143d0f7d086b4b1275464124a29`. Es evidencia histórica de build, no del HEAD ni de Oracle; una revisión embebida tampoco prueba que no hubiera diff al compilar. No usar ese binario como certificación de `8104f05`.

**Si no existe vínculo verificable candidata → artefacto → imagen en ejecución, mantener BLOCKED y solicitar evidencia al operador.** No se añade endpoint, etiqueta ni pipeline en este encargo. Los comandos Git y las instrucciones de migración de los runbooks históricos no quedan autorizados por su lectura.

### Checklist operativo previo — pendiente de evidencia del operador

- [ ] Identidad anterior resuelta, aprobación del piloto y ventana/operador responsable registrados por canal privado.
- [ ] Workspace **ya existente y autorizado de prueba**, contacto controlado con consentimiento, sin números de clientes. Identificar mediante alias en el informe; no publicar números, nombres ni identificadores de instancia.
- [ ] Licencia activa y módulos reales comprobados desde el backend: SERVICES para servicios, CATALOG para productos; RESERVATIONS+SERVICES para reservar; ORDERS+CATALOG para pedidos; LOCATIONS/BUSINESS_HOURS para consultas independientes. Revisar capacidades efectivas del operador; no conceder módulos para hacer pasar el piloto.
- [ ] Perfil público y zona horaria configurada verificados; sedes, servicios reservables/duración, productos activos, precios/monedas y horarios globales/por sede cotejados por operador. Registrar fuente/revisión/hora, sin copiar datos privados. No tratar calendario de apertura como disponibilidad.
- [ ] Estado de conexión de la instancia exclusiva del workspace comprobado con permisos reales en la aplicación. Contrato existente: `GET /api/business/whatsapp/status`, CONVERSATIONS/READ; conectar/desconectar exige CONFIGURE y son acciones separadas, **no se ejecutan en preparación** (`BusinessController.cs:211–243`). No retirar legacy ni alterar webhook/Evolution.
- [ ] PostgreSQL, Firestore y Redis observados; revisar cuerpo y estado de `/health/live` y `/health/ready`. Redis Degraded puede mantener HTTP 200; no equivale a canal completamente sano (`Program.cs:135–141,215–232`). Workers/cola/Outbox y recepción real se verifican aparte.
- [ ] Groq/Gemini: modelos disponibles, límites, cuotas, presupuesto autorizado y errores sanitizados; no obtener ni imprimir claves. No inducir fallos ni agotar cuotas de servicios compartidos. Las pruebas directas pagadas requieren autorización adicional.
- [ ] PDF SERVICE/PRODUCT existente: estado CURRENT, scope/workspace correctos y datos vigentes. Si no hay PDF, evaluar fallback factual; no generar un folleto, activar n8n ni crear assets para preparar esta prueba.
- [ ] Inbox y asesor disponibles con CONVERSATIONS/READ, TAKEOVER y RELEASE. La liberación usa la aplicación y `POST /api/conversations/{id}/release`; no borrar contexto ni forzar modos en Firestore (`ConversationsController.cs:76–93`).
- [ ] Logs del piloto accesibles por autorización y retención acordadas; scopes preservados y correlación por InboundId comprobada. Privacidad, evidencia y responsable de parada definidos.
- [ ] Para cualquier commit comercial: aprobación separada, identidad propia controlada, sede/servicio/recurso de prueba sin interferir con clientes y comprobación autorizada del efecto único. Sin ello, detener antes de confirmar. No cambiar/cancelar citas existentes ni hacer limpieza posterior por iniciativa propia.
- [ ] Rollback de **aplicación** con digest anterior compatible con epochs/fences/UpdateTime revisado y evidencia de respaldo/restauración aislada disponible para la revisión operacional. No crear ni restaurar respaldos en esta preparación. Sin imagen compatible, detener el piloto y corregir hacia delante; no ejecutar Down, restaurar datos, reiniciar servicios compartidos ni drenar/mutar producción desde esta preparación.

## B. Recorrido contrastado con el código actual

Rutas bajo `backend/`; referencias estáticas, no trazas de Oracle:

| Tramo | Método / evidencia | Garantía a observar después |
|---|---|---|
| Entrada | API/Controllers/Webhooks/EvolutionWebhookController.cs:30–117, ReceiveMessage | Header de webhook por instancia, compatibilidad legacy explícita, instancia/owner válidos, remitente normalizado; ACK después de persistir inbound. No basta un HTTP 200 para afirmar procesamiento. |
| Remitente/licencia | Application/Features/Automation/ProcessMessage/Services/IncomingMessageGuard.cs:29–70, NormalizePhone/CheckMessageAsync | Contacto canónico, workspace coincidente, licencia válida. No convierte un teléfono/nombre en acreditación de titularidad. |
| Cola/FIFO | Infrastructure/Persistence/PostgreSQL/Repositories/InboundMessageRepository.cs:78–168, GetAndLockNextMessagesAsync/ProcessClaimedAsync; Infrastructure/Workers/InboundMessageWorker.cs:22–94 | Idempotencia de envelope, exclusión por workspace/contacto y conservación del orden; fallo/retry no es una entrada perdida. |
| Contexto | ContextRecoveryService.GetOrRecoverContextAsync, Services/ContextRecoveryService.cs:31–56 | Firestore prevalece sobre Redis incluso en hit; versión y CAS conservados. TTL Redis no es vida de una reserva. |
| Comprensión | ConversationRecoveryService.RecoverAsync; Dialogue/Understanding/TurnUnderstandingPipeline.cs:24–159; Features/AI/Interpretation/AiInterpreter.cs:94–209 y AI/Router/AiRouter.cs:29–93 | Recovery/policy/resolutores antes de IA; extracción acotada para ambigüedad, presupuesto transaccional, deadline y fallback clasificados. Sin hechos ni consentimiento inventados. |
| Hechos comerciales | Features/Knowledge/KnowledgeService.cs:54–198, QueryAsync/QueryBusinessHoursAsync; Business/Offerings/OfferingService.cs:24–61 | Perfil/ofertas/horarios oficiales del workspace y ámbito correcto. Precio referencial reutiliza lectura puntual del turno; no catálogo privado de otro negocio. |
| Operaciones | Services/Flows/ConversationFlows.cs:113–391, BookingFlow.ProcessPlannedAsync; Flows/OrderFlow.cs:55–185 | Draft y datos faltantes, disponibilidad real y confirmación vigente antes de motor/persistencia. Consulta informativa no confirma ni descarta borrador. |
| Reserva registrada | Flows/ExistingReservationFlow.cs:55–75, TryHandleAsync | Handoff **antes** de lectura privada SQL; gestión automática bloqueada sin acreditación independiente. |
| Respuesta/salida | Services/AiResponseOrchestrator.cs:110–113,278–324; Dialogue/Composition/ResponseComposer.cs:33–48; Services/OutboundMessageService.cs:39–195 | Revalidar versión/modo/licencia; composición determinista y outbound persistido antes del transporte. Sent es aceptación del proveedor, no lectura del móvil. UnknownDelivery no se reenvía. |
| Transporte/humano | Infrastructure/Gateways/EvolutionMessageGateway.cs:40–108; Conversations/HumanHandoffService.cs:23–44; FirestoreConversationRepository.SetHandoffAsync:388–412 | Transporte con resultado durable; conversación/contexto cambian a Human en transacción. Una confirmación antigua no atraviesa las barreras de modo/versión. |

Los prefijos Application/Features/Automation/ProcessMessage y Application/Features/Automation se mantienen para las rutas abreviadas de servicios/diálogo. El diagnóstico Sprint 0 describe código histórico: sus carencias no se consideran defectos actuales sin reproducción. El runbook piloto 2.0 y los conteos RC-3 son referencias históricas, no gates automáticamente aprobados para 3.0.

## C. Ocho guiones manuales preparados

**No ejecutados.** Antes de usarlos, completar el checklist con datos existentes. Sustituir «clínica» por el tipo real de negocio si corresponde. Usar «limpieza dental» **sólo si existe y está autorizado**; en otro negocio elegir un servicio configurado equivalente o marcar ese caso BLOCKED. No completar precios, fechas exactas, sedes ni disponibilidad desde este documento.

Separar sesiones/contactos de prueba autorizados cuando sea necesario. Entre casos incompatibles, usar la liberación real del asesor; no borrar documentos ni hacer logout. Mantener 4→5→6 en el mismo borrador; ejecutar 7→8 en sesión aparte, sin cita comercial real.

1. **Información.** En modo automático: «Hola, quisiera información de la clínica» → cotejar únicamente perfil público, sin menú innecesario ni TaxId. Seguir: «¿Y cuáles son sus horarios?» → verificar alcance global/sede; si hay aclaración, elegir una sede **de las opciones vigentes**. Día no configurado no es día cerrado.
2. **Rectificación.** Tras la consulta anterior: «No te pregunté por las direcciones, sino por los horarios» → volver al tema afirmado, ignorar el negado, usar calendario real. Registrar respuesta íntegra anonimizada; no atribuir un desvío a IA sin comprobar rama/proveedor.
3. **Servicios/precio.** «¿Qué servicios ofrecen?» → catálogo SERVICE vigente, PDF CURRENT si corresponde o hasta cinco ofertas reales/fallback; sin publicidad de otros módulos. «¿Cuánto cuesta la limpieza dental?» (o servicio real elegido) → precio/moneda del dato actual, sin enviar un PDF completo innecesario. Comparar producto y servicio por scope, no por suposición.
4. **Reserva nueva.** «Quiero reservar una limpieza dental» (servicio existente) → suministrar sólo datos faltantes desde opciones reales. «Mejor el martes por la tarde» → comprobar fecha resuelta con reloj/zona del negocio y slots actuales; una franja no es una hora inventada. Llegar como máximo al resumen. **STOP antes de «sí»/confirmación** sin autorización de escritura segura. Con autorización separada, confirmar una vez y comprobar mismo SourceMessageId/efecto único mediante evidencia privada del operador, sin reinyectar mensajes.
5. **Interrupción y continuidad.** En ese borrador, «Antes dime cuánto cuesta» → precio vigente y mismos campos del draft. «Perfecto, continuemos» → paso pendiente correcto, sin volver a solicitar datos completos ni confirmar. No enviar el resumen a un proveedor para inventar un resultado.
6. **Consentimiento condicionado.** Ante resumen pendiente: «Sí, pero quiero otra hora» → cero commit, conservar sede/servicio/fecha/nombre seguro y pedir/ofrecer hora válida. «¿Ya quedó confirmada?» → respuesta basada en estado comprobable, sin convertir pregunta en consentimiento ni afirmar reserva inexistente. Mantener STOP comercial del caso 4.
7. **Cita registrada.** «Quiero cambiar mi cita confirmada» → verificar transferencia real al asesor, sin nueva reserva ni datos privados. No seleccionar una cita comercial ni efectuar edición/cancelación. No habilitar la implementación de gestión subyacente ni aportar un booleano que eluda titularidad.
8. **Control humano.** Tras el handoff, enviar una pregunta informativa del piloto: el bot no responde automáticamente; asesor disponible, un solo acknowledgment y ninguna duplicación. Observar supresión de pendientes por InboundId/estado durable. La prueba de una confirmación antigua susceptible de escribir negocio sigue BLOCKED sin autorización de escritura sintética: no reenviar históricos ni usar una cita de cliente. Liberar sólo por operador autorizado en la aplicación y comprobar un nuevo turno automático; no restablecer documentos manualmente.

### Matriz inicial de resultados

B1 = versión desplegada no demostrada; B2 = piloto no autorizado; B3 = workspace/contacto/canal de observación sin evidencia; B4 = escritura comercial no autorizada ni destino seguro acreditado.

| Caso | Esperado | Observado | Estado | Causa / evidencia |
|---|---|---|---|---|
| 1 Información/horarios | Perfil público y calendario de ámbito correcto | No observado | BLOCKED | B1–B3; únicamente revisión local de rutas. |
| 2 Rectificación | Horarios afirmados, sin direcciones negadas | No observado | BLOCKED | B1–B3; sin transcripción real. |
| 3 Servicios/precio/PDF | SERVICE y precio oficiales, scope correcto | No observado | BLOCKED | B1–B3; catálogo/artefacto real no cotejados. |
| 4 Reserva nueva | Draft sin inventos; commit sólo autorizado y único | No observado | BLOCKED | B1–B4; no se creó reserva de prueba. |
| 5 Interrupción | Precio y continuidad sin perder draft | No observado | BLOCKED | B1–B3; ningún borrador real utilizado. |
| 6 Consentimiento | Corrección/pregunta sin confirmar | No observado | BLOCKED | B1–B3; sin respuesta/estado real. |
| 7 Cita registrada | Handoff, cero divulgación/edición/alta | No observado | BLOCKED | B1–B3; guard confirmado por lectura, no E2E. |
| 8 Toma/liberación humana | Supresión automática, acknowledgment único | No observado | BLOCKED | B1–B3; subprueba de confirmación comercial también B4. |

**0 conversaciones ejecutadas, 0 PASS, 0 FAIL, 8 BLOCKED.** BLOCKED no demuestra un defecto funcional. Tampoco significa que la aplicación esté certificada.

## D. Observabilidad y captura de incidencias

**Correlación ya implementada, emisión efectiva pendiente.** `InboundMessageWorker.cs:48–49,140–144` crea un BeginScope con InboundId (GUID técnico de la entrada SQL) y WorkspaceId. Ese scope envuelve el turno asíncrono y sus dependencias. `ConversationDiagnostics.cs:10–29` no repite InboundId en cada plantilla: depende de que el proveedor/formateador de logging preserve scopes. No se encontró configuración de formateador explícita en Program; no se inspeccionó configuración privada de Oracle. No es correcto afirmar que faltan identificadores en el código ni que ya aparecen en el log desplegado.

La correlación HTTP `X-Correlation-ID` de `CorrelationIdMiddleware.cs:18–47` pertenece a la petición web; no sustituye la identidad de un worker diferido. Nunca proporcionar teléfonos/textos como ID ni agrupar turnos sólo por WorkspaceId o proximidad temporal: el worker procesa en paralelo.

Comprobación futura: para un turno consentido, el operador conserva una muestra sanitizada con el mismo InboundId en Claimed, HandlerOutcome, ConversationPhase y cierre. Verificar aislamiento de IDs entre turnos concurrentes y distinguir cada intento de retry. Si los scopes se descartan, **propuesta mínima condicionada**, no aplicada: habilitar su serialización en el formateador existente mediante cambio operacional aprobado y revisar privacidad. No crear otro correlacionador, cache ni estado conversacional. Si el sink ya los conserva, no cambiar código.

| Medición futura | Fuente actual | Resultado en este encargo |
|---|---|---|
| Tiempo total y fases | Turn, ContextRecovery, Interpretation, KnowledgeQuery, Offering*Read, ResponseGeneration, Composition, Delivery | N real=0; NO MEDIDO. Turn/ResponseGeneration son inclusivos; no sumar fases anidadas. |
| IA y fallback | AiProvider: proveedor, motivo, AiInvocations; AiRouter | NO MEDIDO. Cuenta invocaciones de router, no cada HTTP/modelo interno ni tokens facturados. Ausencia de logs no prueba cero IA si no se verificó captura. |
| Cola/reintentos/CAS | InboundStage/FailureKind, InboundStalled, intentos y edades agregados; error de concurrencia | NO MEDIDO. No exportar PayloadJson ni datos de reservas. Un reintento no autoriza nuevo efecto comercial. |
| Entrega/handoff | Ledger outbound, estados Sent/Failed/UnknownDelivery y modo Human, observación del móvil controlado | NO OBSERVADO. Sent/Processed no prueban entrega/lectura; nunca reenviar UnknownDelivery. |
| Firestore/SQL/Redis | Instrumentación de servicios y métricas agregadas autorizadas de SDK/almacén | NO MEDIDO. El logger de Sprint 4 no cuenta universalmente documentos facturados, sentencias SQL, retries Firestore o accesos Redis. No derivarlos de ElapsedMs. |

p50/p95: no calculados (muestra=0). Tras autorización, informar N y ventana por flujo/resultado, separar cola, interpretación y transporte y declarar muestra insuficiente cuando no permita una cola estadística útil. No inventar objetivos ni ahorro monetario. El informe Sprint 4 contiene estimaciones estáticas y pruebas sintéticas, no esta medición.

Para cada incidencia futura guardar: caso/turno técnico, UTC y zona de negocio, entrada resumida/anonimizada, intención esperada y decidida, fuente/scope y revisión, respuesta observada anonimizada, respuesta correcta basada en hechos, componente/causa demostrados o hipótesis, severidad, referencia privada de evidencia y parche mínimo propuesto. No publicar nombres, números, prompts, cuerpos completos ni credenciales. Usar alias para entidades/personas y conservar expectativas autorizadas del operador fuera del informe público.

Parar inmediatamente ante cruce de workspace, divulgación privada, commit sin consentimiento, duplicado comercial, precio/horario inventado, respuesta automática bajo control humano o envío incierto no explicable. Preservar evidencias/colas; no borrar datos ni compensar mediante reproceso/rollback por iniciativa propia.

## E. Hallazgos, validación local y riesgos

| Prioridad / naturaleza | Hallazgo | Acción mínima / riesgo |
|---|---|---|
| P0 | Ningún defecto nuevo demostrado en esta preparación | No equivale a ausencia de P0 en producción; sin conversaciones reales no hay dictamen funcional. |
| P1, bloqueo de evidencia | No se puede vincular ejecución Oracle con `8104f05`; no hay piloto autorizado/destino de escritura acreditado | Obtener identidad/procedencia y autorizaciones antes de Gate C. No parchear módulos correctos ni desplegar para adivinar. |
| P1, barrera de seguridad vigente | Falta acreditación independiente para gestión automática de citas registradas | Mantener handoff y NO-GO automático; no probar sobre citas reales ni omitir el guard. |
| P2, riesgo operativo pendiente | Exportación de scopes y correlación efectiva no comprobadas; métricas de proveedor/almacenes ausentes | Verificar muestra privada. Cambio sólo si se demuestra pérdida de correlación; configuración productiva requiere aprobación. |
| P2, pendiente previo | Alcance amplio de IsRepair y consumo/contención reales del presupuesto | Observar especialmente caso 2 y errores de proveedor, sin ampliar reglas por intuición ni cambiar límites. Sin incidencia real nueva aquí. |

**Correcciones productivas: ninguna.** No hay antes/después funcional nuevo ni regresión que ejecutar. Sólo se crea este informe; backend/frontend, pruebas, configuraciones y documentos anteriores intactos. No se añadieron mocks, fixtures ni bases simuladas. No se ejecutaron builds ni suites en esta entrega documental. La evidencia histórica de Sprint 4 (build con 0 errores/3 advertencias y 872 tests aprobados) no se atribuye a una nueva compilación ni certifica el SHA desplegado.

Preparación de rollback: aplicar únicamente el procedimiento vigente de `NexFlow_RC3_DEPLOY_RUNBOOK.md:187–197` tras autorización separada. Preservar protocolo, estados, datos y servicios compartidos. Las antiguas instrucciones de migración, despliegue o cambios de configuración del piloto 2.0 no se ejecutan ni se presuponen necesarias para Sprint 5. Sus umbrales históricos de conversaciones no son una medición ni un nuevo criterio aprobado para esta sesión.

## F. Decisiones independientes y siguiente gate

| Capacidad | Decisión actual | Justificación |
|---|---|---|
| Consultas informativas reales | NO-GO para iniciar/certificar el piloto ahora | Identidad, autorización y evidencia de casos 1–3 pendientes. |
| Creación de reservas nuevas | NO-GO | Además de lo anterior, falta autorización de escritura y verificación del efecto único en destino seguro. Preparación puede detenerse antes de confirmar. |
| Pedidos | NO-GO | No se ejecutó un pedido real de prueba ni existe autorización de creación; los ocho guiones no certifican esa transacción. Requerirá validación específica autorizada. |
| Transferencia humana | NO-GO de certificación real | Caso 7–8 y captura durable/supresión no observados en el entorno desplegado. |
| Gestión automática de citas registradas | NO-GO, protección obligatoria | No existe acreditación independiente aprobada/validada; el piloto sólo debe verificar el handoff seguro. |
| Ampliación comercial | NO-GO | Sin transcripciones supervisadas, muestra real, revisión humana y resultados de proveedores/canal autorizados. |

Próximo paso seguro: el propietario entrega evidencia sanitizada de identidad de ejecución y condiciones del checklist, designa workspace/contacto/operador/canal privado y autoriza expresamente el alcance del piloto. Si hace falta desplegar, tramitar Gate B por separado. Hasta entonces **detenerse en esta preparación**, sin mensajes, escrituras comerciales, Git/GitHub, migraciones ni despliegues.
