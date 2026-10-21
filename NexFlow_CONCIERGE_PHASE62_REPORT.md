# Concierge 2.0 — Fase 6.2: correcciones locales y arbitraje pendiente

**Técnico: NO-GO. Calidad: NO-GO. Piloto aislado: NO-GO.** Las correcciones locales están verificadas; H06, la evaluación ciega independiente y la infraestructura real siguen pendientes. No se declara la suite verde ni se autoriza un piloto/despliegue.

## Identidad y límites

Base auditada y HEAD conservado: `fd1aa17007fff2393d26fec4f8cae8b164e84a3e`, rama `main`, árbol inicialmente limpio. Se leyeron AGENTS, los informes 6/6.1 y las secciones aplicables del maestro Concierge. La candidata es esa base más el diff local; no hay un SHA publicado nuevo.

No se modificaron motores de reservas/pedidos, persistencia durable, contratos externos, frontend, migraciones, esquemas, workflows, infraestructura, secretos ni proveedores. No hubo commit, push, despliegue ni acceso a Oracle/Firebase real. No hubo llamadas IA pagadas.

## Reproducción antes de tocar código productivo

Se ejecutaron **H04, H12, H22, H16, H27 y H06 por separado**: un test ejecutado/fallido en cada `before-Hxx.trx`. Luego se capturó la batería histórica completa: **24 aprobadas / 6 fallidas / 0 omitidas** en `before-historical.trx`.

Se inspeccionaron los nueve componentes pedidos, incluidos el pipeline, orquestador, resolución de entidades/fechas y recuperación. La instrumentación añadida es sólo de pruebas y opt-in: `NEXFLOW_LINGUISTIC_TRACE_DIRECTORY`. `reproduction-turns.ndjson` reúne **32 registros antes/después** de los seis casos: acto, intención del pipeline o camino Recovery, ruta local/semántica simulada, IDs de entidades, resolución de fechas, borrador antes/después, consultas por método, respuesta sintética y criterio incumplido. Se omiten teléfonos y versiones/identidades operacionales; los nombres sintéticos se redactan. No es logging productivo.

| Caso | Causa verificada, distinta de su consecuencia | Corrección / resultado |
|---|---|---|
| H04 | `porfa` permanecía en el término de producto. Al fallar coincidencia, la compra débil devolvía null y llegaba a la semántica simulada General. | Cortesía final separada sólo fuera de nombres/alias autorizados; cantidad 5 local, sin commit. |
| H12 | Mismo fallo en compra débil de queques; no era un problema de licencia. | Misma corrección compartida; cantidad 2 local, con CATALOG+ORDERS. |
| H22 | La compra explícita devolvía propuesta Clarify sin líneas; no existía carrito para el subtotal posterior. | Mismo análisis de cortesía; cantidad 3, JSON inválido sin pérdida del carrito y subtotal verificable. |
| H16 | El clasificador amplio no reconocía «folleto» con verbo de envío; la ruta local emitía Query específica vacía y no llegaba a IA. | Reconocimiento estructural de peticiones de catálogo/PDF, lectura amplia autorizada y alternativa factual existente. |
| H27 | La petición natural de opciones no estaba reconocida y no preparaba opciones antes de recibir el número. | Reconocimiento de menú solicitado, reutilizando opciones, CAS, vigencia y paginación existentes. |
| H27, harness | Tras corregir el menú, la selección ya llegaba a QueryAsync, pero el fixture devolvía un texto fijo S/25 en vez del catálogo sintético esperado. | `UseCatalogKnowledge` ejecuta **KnowledgeService real** contra los repositorios controlados del mismo workspace. No se fabricaron hechos ni se cambió ninguna expectativa. |
| H06 | `el jueves` da candidatos 22 y 29 de octubre. La fecha ausente impide confirmar; los fallos posteriores son consecuencia de esa ambigüedad. | Se conserva aclaración, borrador y ausencia de reserva. **PENDIENTE de arbitraje**, no parcheado para elegir 22. |

## Correcciones generales y seguridad

- `CommercialCourtesy` analiza cláusulas finales de cortesía y protege spans de nombres/alias del catálogo autorizado, incluyendo sus formas plurales. No agrega `porfa`, `gracias` ni otras palabras a una blacklist global. Los prefijos «me das»/«agrégame» sólo se eliminan en posición de comando, no dentro del nombre.
- Las coincidencias literales conservan palabras funcionales. Un nombre completo como «Pan Por Favor» tiene prioridad sobre el subspan «Pan»; nombres/alias iguales conservan ambigüedad, y menciones separadas no se descartan. No hay fuzzy matching arbitrario ni elección del primer ID.
- `InformationalRequest` reconoce estructura de catálogo/menú; no contiene nombres de productos ni IDs de casos Hxx. Las peticiones específicas conservan sus hechos y no disparan envío del catálogo completo. Las peticiones amplias no inician compras ni generan PDFs.
- Se reutilizan ChatFlow y el outbound durable: sólo PDF CURRENT del scope/workspace correcto; un PDF ausente o GENERATING usa texto factual. UnknownDelivery conserva el estado y no se reenvía al repetir el origen. La licencia se comprueba de nuevo antes de entregar media.
- El menú natural no intercepta «opciones de limpieza» ni otros nombres de entidad fuera de su vocabulario de control. Mantiene el mismo selector contextual, revalidación de módulo, paginación y asistencia humana con licencia vacía.

Las regresiones adversariales del parche detectaron y corrigieron antes del cierre: selección del producto corto al perder «Por Favor»; «me das el precio/información» tratado como compra de una unidad; y token parecido a verbo dentro de «Das Tres» interpretado como cantidad. Se conservan `linguistic-first.trx`, `polite-information-reproduction.trx` y `name-quantity-reproduction.trx`. No hubo pedidos persistidos durante esas reproducciones; sí borradores incorrectos, que las pruebas ahora rechazan. Los asserts no se relajaron.

## H06 — informe de política, sin decisión de negocio aprobada

Entrada: martes 20/10/2026 a las 16:00 America/Lima; «Quisiera una limpieza dental en San Jerónimo el jueves por la tarde». El servicio y la sede se resuelven, pero el día de semana puede ser 22/10 o 29/10. Una franja no prueba disponibilidad ni autoriza inventar una hora.

| Alternativa | Ventajas | Riesgos y casos límite |
|---|---|---|
| A. Próximo día futuro por defecto | Menos preguntas; encaja con usos frecuentes de «el jueves». | Puede elegir semana equivocada. Debe definir mismo día/horario ya pasado, límite de reserva, fecha fuera del horizonte, cierre del negocio y cambio de mes/año. No se debe saltar a otra semana porque no haya cupo: es un hecho distinto. El horizonte debe venir de una regla real del negocio, no inventarse. |
| B. Aclarar sin referencia de semana | Evita selección arbitraria; conserva la distinción entre fecha deseada y disponibilidad. Es el comportamiento conservador actual. | Añade un turno incluso cuando el cliente suponía el próximo jueves. La pregunta debería permitir contestar con fecha o semana inequívoca; no debe perder sede, servicio, franja ni nombre ya aportados. |
| C. Interpretación contextual | Puede resolver cuando ya se ofreció una semana/fecha y el usuario responde a esa pregunta. | Requiere definir qué contexto es vigente y autorizado; una pregunta genérica «¿qué día?» no basta. Riesgo con opciones caducadas, interrupciones, cambio de fecha/sede, sesión recuperada y horizonte modificado. No puede usar la última lista de otro workspace ni asumir que disponibilidad fija intención. |

**Decisión: PENDIENTE.** El usuario confirmó que H06 debe permanecer pendiente de arbitraje. No hay aprobación independiente que cambie B por A/C o que corrija la expectativa histórica. No se modificó BusinessDateResolver, no se alteró el test H06, no se creó una versión corregida que aparente aprobación. Una nueva regresión caracteriza la aclaración actual y la continuación con fecha explícita; **no sustituye ni oculta el fallo histórico**.

La revisión debe involucrar responsable de producto/negocio y evaluador independiente del resultado esperado: acordar casos límite y horizonte real, decidir la política y registrar aprobación antes de implementar o versionar una expectativa nueva. Esta entrega no toma esa decisión por ellos.

## Corpus histórico y evaluación ciega

`QualityHoldout61.json` permanece byte a byte intacto: SHA256 `3dfe02ac4cc1d6a067d1a875917615a4a3b3b73ab6d72c47ef2d63fe1f7e96e0`. Ninguna aserción ni expectativa fue modificada; sólo se fortaleció la dependencia factual del harness y se añadió exportación opt-in de diagnóstico.

Resultado histórico de la candidata: **29/30 aprobadas**, H06 fallida. Al haberse investigado sus casos, este corpus es ahora **regresión conocida, no evaluación ciega**. El usuario confirmó que no existe todavía un corpus privado aprobado por otra persona: **PENDIENTE / NO-GO de calidad**. No se creó un corpus por el mismo implementador para simular independencia. Tras congelar la candidata, se preparará fuera del repositorio mediante otra persona/evaluador, sin exponer expectativas al implementador y con una única ejecución por candidata. No se ejecutó ese corpus ni se inventaron puntuaciones humanas.

## Validación final real

```powershell
dotnet build backend/backend.slnx --no-restore --disable-build-servers -m:1
dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --disable-build-servers -m:1 --filter 'Category!=Integration&Category!=RedisIntegration&Category!=ProviderIntegration' --logger 'trx;LogFileName=phase62-final.trx'
```

| Comprobación | Resultado |
|---|---|
| Build backend final | **0 errores, 0 advertencias en incremental**; `backend-build-final.log`. |
| Suite local completa sin integraciones externas | **1.289 ejecutadas: 1.288 aprobadas, 1 fallida (H06), 0 omitidas**. Exit code 1 conservado. |
| Bienvenida | **14/14** aprobadas. |
| Corpus original comercial | **100/100** aprobadas. |
| Corpus histórico 6.1 | **29 aprobadas / H06 fallida / 0 omitidas**. |
| Regresiones lingüísticas nuevas | **50/50** aprobadas. |
| Procesamiento comercial, recuperación y reservas | Incluidos en la suite local completa; ningún fallo fuera de H06. |
| Diff y corpus congelado | `git diff --check`, whitespace de archivos nuevos y comprobación inversa del parche completo: OK. Hash del corpus intacto. |

Los grupos son subconjuntos, no se suman. Las 50 regresiones cubren cortesía, nombres/alias reales inusuales, cantidades y multiartículo, negación/hipótesis, ambigüedad, precio durante borrador, licencia revocada, menú vacío/handoff, referencias obsoletas, dos workspaces, repetición, confirmación, PDF ausente/GENERATING/CURRENT/UnknownDelivery en ambos scopes, fecha ambigua y corrección posterior. No hubo operaciones prematuras, duplicadas ni no licenciadas en las verificaciones finales.

Las compilaciones iniciales mostraron tres advertencias históricas: CS8981 x2 en migraciones refactor y CS8602 en FirestoreCatalogRepository:228. Dos avisos xUnit2031 introducidos durante el desarrollo se corrigieron con el overload de Assert.Single; no permanecen en las pruebas finales. No se tocaron dependencias ni frontend.

## Métricas antes/después

Dos ejecuciones con el mismo filtro histórico y 86 invocaciones, incluyendo replays y segundo workspace. Catálogo/motores/outbound son sintéticos; SDK Firestore sobre RPC en memoria. La comparación también incluye la corrección factual del harness H27: no representa una medición aislada del cambio productivo ni de calidad IA real.

| Métrica | Antes | Después |
|---|---:|---:|
| Conversaciones aprobadas | 24 | 29 |
| Turnos que cumplen sus criterios | 77 | 83 |
| Llamadas semánticas simuladas | 11 | 6 |
| Lecturas SDK Firestore en memoria | 473 | 474 |
| Commits SDK Firestore en memoria | 108 | 103 |
| p50 local (ms) | 1.76 | 2.63 |
| p95 local (ms) | 23.05 | 35.67 |
| Handler errors / sin respuesta inesperada | 0 / 0 | 0 / 0 |
| Pedidos / reservas sintéticos | 1 / 2 | 1 / 2 |
| Sent sintéticos | 82 | 82 |

No se afirma mejora de latencia: las muestras finales fueron más lentas y son sensibles a JIT/contención. No son un SLO de servidor. Entregas son del doble de transporte, no WhatsApp real. Tokens/coste/provider calls reales, DeadLetters de workers e infraestructura real: **NO MEDIDO**.

Reproducción de métricas con la herramienta existente:

```powershell
python backend/tools/ConciergeCertification/collect_quality_evidence.py --results '<evidencias>/before-historical.trx' --output '<evidencias>/before-metrics'
python backend/tools/ConciergeCertification/collect_quality_evidence.py --results '<evidencias>/after-historical-final.trx' --output '<evidencias>/after-final-metrics'
```

## Infraestructura y riesgos pendientes

El gate de la tarea exige aprobar primero la suite local. **No se satisface mientras H06 esté fallida**: no se dispararon las 37 integraciones PostgreSQL/Firestore Emulator ni Redis real. No hay Run ID/TRX nuevo de CI para este diff sin publicar. No se conectó a servicios compartidos ni se utilizaron mocks como aprobación de Emulator. Los workflows existentes permanecen sin cambios; no se afirma nada sobre runs remotos que no se consultaron en esta fase. Proveedores pagados: no autorizados/no ejecutados.

- P1 de homologación: arbitraje H06, evaluación privada independiente, 37 integraciones y contrato Redis real. Son bloqueos reales de aprobación, no defectos que se oculten con exclusiones.
- P2: la gramática local es conservadora, no comprensión universal de español. Fuera de las construcciones inequívocas sigue el pipeline semántico y su presupuesto existente. Verificar calidad humana y latencias representativas con la candidata congelada.
- No se validó producción, WhatsApp real ni comportamiento de cuentas IA reales. Las verificaciones locales no sustituyen esas evidencias.

## Archivos y evidencia

Backend productivo:
- `C:/Proyectos/NexFlow/backend/NexFlow.Application/Features/Automation/Dialogue/Understanding/CommercialCourtesy.cs` (nuevo).
- `C:/Proyectos/NexFlow/backend/NexFlow.Application/Features/Automation/Dialogue/Understanding/InformationalRequest.cs` (nuevo).
- `C:/Proyectos/NexFlow/backend/NexFlow.Application/Features/Automation/Dialogue/Understanding/OrderMentionResolver.cs`.
- `C:/Proyectos/NexFlow/backend/NexFlow.Application/Features/Automation/Dialogue/Understanding/CommerceTurnResolver.cs`.
- `C:/Proyectos/NexFlow/backend/NexFlow.Application/Features/Automation/Dialogue/Understanding/EntityMentionResolvers.cs`.
- `C:/Proyectos/NexFlow/backend/NexFlow.Application/Features/Automation/ProcessMessage/Services/ConversationRecoveryService.cs`.

Pruebas:
- `C:/Proyectos/NexFlow/backend/NexFlow.Tests/AssistantConversationTests.cs` (KnowledgeService real opcional para el harness).
- `C:/Proyectos/NexFlow/backend/NexFlow.Tests/Concierge/QualityHoldout61Tests.cs` (sin cambios de expectativas).
- `C:/Proyectos/NexFlow/backend/NexFlow.Tests/Concierge/LinguisticRegression62Tests.cs` (nuevo).
- `C:/Proyectos/NexFlow/backend/NexFlow.Tests/Concierge/LinguisticTrace.cs` (nuevo).

Informe: `C:/Proyectos/NexFlow/NexFlow_CONCIERGE_PHASE62_REPORT.md`. Frontend: sin cambios.

Evidencias: `C:/Users/deyvi/.codex/visualizations/2026/10/02/01a0fa60-4061-7571-be5e-8c8e0adbefd8/concierge-phase62/`: TRX anteriores/finales, `reproduction-turns.ndjson`, `phase62-metrics.json`, `phase62.patch`, `phase62-files.txt` y `candidate-freeze.json`. Esta última identifica el contenido de código/pruebas de la candidata local, no un commit ni una imagen desplegada. No modifica el repositorio.

**Cierre local para auditoría: correcciones verificadas, H06 y certificación pendientes. Técnico NO-GO / calidad NO-GO / piloto NO-GO.** Sin avance automático a otra fase.
