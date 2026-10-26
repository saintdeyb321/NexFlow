# G8 — Contrato estricto de certificación CI de Agent Runtime 4.0

## Identidad y dictamen

Referencia local main leída directamente de HEAD/ref: **664ad0e5810e99f21641fb0fbe83929de3ad787e**, coincidente con el encargo. Sin comandos Git, consulta remota, publicación o dispatch. El árbol previo no se presume limpio; se capturó el contenido inicial de los tres workflows para revisar exclusivamente el cambio propio.

**Adecuación del gate implementada y validada localmente. Certificación/release: NO-GO.** Los doce fallos históricos están separados y verificados estrictamente, pero la suite local completa contiene **otros cuatro fallos** que no pueden excluirse ni corregirse en este encargo. No se declara CI remoto aprobado: no hay Run ID/SHA de ejecución aportados.

La imagen ARM64/CodecSmoke y la restauración PostgreSQL del SHA base son evidencia indicada por el propietario, no reejecutada aquí. Un commit futuro de G8 tendrá otro SHA; vincular manualmente fuentes/imagen/evidencia cuando se congele. No acceder a Oracle ni cambiar Firebase/Spark, backups, plan, secretos o datos reales. Este gate no resuelve ni aprueba las limitaciones operativas del respaldo Firestore.

## Diagnóstico y filtros efectivos

Los dos workflows RC3 ejecutaban el mismo filtro local:

~~~text
Category!=Integration&Category!=RedisIntegration&Category!=ProviderIntegration
~~~

Ese filtro **sí incluye** CommercialCertificationTests y QualityHoldout61Tests. Exigían después que total=executed=passed y al menos 708 aprobadas. dotnet test ya devolvía 1 por los doce fallos originales: el job se detenía antes de las validaciones posteriores. No existía una batería histórica separada ni verificación de sus identidades/motivos. Redis no tenía ese problema: sólo ejecuta Category=RedisIntegration.

Descubrimiento real local mediante --list-tests --no-build --no-restore, sin ejecutar cuerpos de integración:

| Workflow/filtro | Selección efectiva en esta base | Regla preservada/adaptada |
|---|---:|---|
| RC3 release, local original | 1621 casos: 1491 actuales + 130 históricos | Se divide sólo por las dos clases históricas |
| RC3 integración, local original | Los mismos 1621 casos | Mismo gate compartido que release |
| RC3 integración, Category=Integration | **39 descubiertos**, no ejecutados aquí | Se conserva comando, aislamiento y mínimo existente ≥37; todos los descubiertos deben ejecutarse/pasar sin omisiones |
| Redis, Category=RedisIntegration | **1 descubierto**, no ejecutado aquí | Conservado íntegro: exactamente uno aprobado, cero fallos/omisiones |

El mínimo 708 estaba desactualizado. Las **984/984** de F7 son una batería dirigida, no todas las clases locales seleccionadas por los workflows. La ejecución completa separada determina un mínimo **1491**; además se exige mantener el subconjunto Agent Runtime ≥984 y F3 v2 ≥12. No se excluyen clases Rc3Sprint0/4 ni cualquier otro fallo ajeno a las dos clases históricas.

Filtros nuevos, usados literalmente por g8_certification.py:

~~~text
# Contrato actual: categorías locales originales, menos sólo las dos clases
Category!=Integration&Category!=RedisIntegration&Category!=ProviderIntegration&FullyQualifiedName!~NexFlow.Tests.Concierge.CommercialCertificationTests.&FullyQualifiedName!~NexFlow.Tests.Concierge.QualityHoldout61Tests.

# Históricos: las mismas categorías, únicamente esas dos clases
Category!=Integration&Category!=RedisIntegration&Category!=ProviderIntegration&(FullyQualifiedName~NexFlow.Tests.Concierge.CommercialCertificationTests.|FullyQualifiedName~NexFlow.Tests.Concierge.QualityHoldout61Tests.)
~~~

El namespace completo y punto final delimitan la clase, sin excluir otras por palabras genéricas. Ambos filtros se ejecutaron realmente. No hay filtro por los doce casos fallidos; se ejecutan los **130 completos**.

## Cambios acotados

- .github/workflows/rc3-release-checks.yml: compila explícitamente el proyecto de pruebas antes de --no-build; añade prueba del validador, batería actual y batería histórica separada. Conserva restore/build/publish, smoke Windows, frontend npm ci/build/lint y suites TS/browser (mínimos 83/148), Chromium y artefactos previos.
- .github/workflows/rc3-reservation-integration.yml: sustituye sólo el gate local por las mismas baterías/validador. Conserva PostgreSQL 15.10 temporal, demo-nexflow-rc3, Firestore Emulator, permisos read, concurrencia, timeout, versiones, integración, identity.json y upload always.
- .github/workflows/concierge-redis-integration.yml: auditado, **sin cambios**. Conserva Redis loopback 16379, PostgreSQL loopback 5432, flags de integración, Emulator demo, exactamente una prueba y artefactos/identidad.
- .github/workflows/g8_certification.py: runner/verificador común, Python estándar sin dependencias nuevas.
- .github/workflows/test_g8_certification.py: pruebas del contrato del validador CI, sin modificar pruebas C#.
- Este informe nuevo. Ningún archivo backend/frontend, corpus, evaluador C#, infraestructura o secreto modificado.

No se crea otro workflow. Ambos siguen siendo sólo workflow_dispatch. No se añaden permisos de escritura, acceso a nube ni credenciales. Retención existente de artefactos: siete días.

## Regla de aceptación del runner

### Contrato vigente

El comando real de dotnet test debe devolver **0**. TRX obligatorio, con resultados individuales y definiciones coherentes:

- total=executed=passed ≥1491, failed=0.
- Cero notExecuted, timeout, error, aborted, inconclusive, notRunnable y otros contadores anómalos; sin resultados desconocidos.
- ExecutionId y TestId únicos. No se exige nombre visual único: el TRX real contiene tres nombres visuales repetidos con TestId distintos.
- Ninguna de las dos clases históricas dentro de esta batería.
- Al menos 984 resultados del filtro dirigido de F7, determinado por clase+método sin distinguir mayúsculas, y al menos 12 de RuntimeF3ContractV2Tests.
- Fuentes históricas/evaluadores congelados sin cambios.

Un resultado vacío, TRX viejo, incompleto, omitido o código de salida fallido nunca acredita el gate. El runner elimina sólo el TRX destino antes de ejecutar para no aceptar evidencia vieja tras un fallo de arranque.

### Históricos originales

El comando real debe devolver **exactamente 1**, conservado como test_exit_code en JSON; summary TRX Failed. Sólo después de comprobar TODO se considera válida la **evidencia de incompatibilidad esperada**, no aprobadas las pruebas fallidas:

1. Exactamente 130 resultados ejecutados, 118 Passed, 12 Failed, sin omisiones ni otros estados.
2. Inventario completo derivado de los JSON congelados: 100 públicos y 30 holdout; identidad de cada nombre parametrizado, clase/método y TestId; sin casos ausentes, adicionales o duplicados.
3. Sólo fallan los diez casos públicos clinic/hotel/restaurant/bakery/limited -09/-10 y H10/H21.
4. Mensaje exacto, permitiendo únicamente espacios exteriores: para cada público, ID + ': VerifiedFact'; H10: 'H10/3/A: VerifiedFact'; H21: 'H21/3/A: VerifiedFact'. Nada de motivos adicionales concatenados.
5. Cualquier caso que pase cuando se esperaba fallar, o viceversa, es NO-GO hasta revisión del contrato. No basta obtener otra vez 118/12.
6. Exit 0/2/otro, errores de compilación/infraestructura, razón distinta, hashes cambiados, falta de artefactos o discovery distinto: NO-GO.

No se usa continue-on-error, '|| true' ni supresión indiscriminada. La única traducción controlada de exit 1 a éxito del **verificador** es la evidencia histórica completa descrita. El TRX permanece rojo y el JSON declara EXPECTED_HISTORICAL_INCOMPATIBILITIES. El step actual fallido sigue haciendo fallar el job aunque el histórico sea conforme.

La batería histórica se ejecuta con condición !cancelled() y build backend correcto, incluso si falla la batería vigente. Si falla el build o se cancela el run, falta evidencia y no hay certificación; no se intenta ejecutar un assembly inexistente.

### Fuentes congeladas y portabilidad

Se verifican cuatro SHA-256 fijados a partir de esta base:

| Archivo bajo backend/NexFlow.Tests/Concierge | SHA-256 normalizado LF |
|---|---|
| CommercialCertificationCorpus.json | db52197eb3d15ba6b9415d94bdefcfa0b893d3ea69f6748bf715dec743125442 |
| QualityHoldout61.json | 541a80b3c1d00e511debd85cb7f5d881db1baa534496b48c2524fddb95d5d0a2 |
| CommercialCertificationTests.cs | 9c9b8eb89f8a5eef0de34314fd7a059e3796580e38f9523366963a9610bcce04 |
| QualityHoldout61Tests.cs | 0de0c9c605f0487e0b7e63d3852d37d33c3012a74054fd84cdd6f80031117ce4 |

Se normaliza exclusivamente CRLF→LF por checkout Windows/Ubuntu, sin eliminar BOM, espacios ni newline final. Cualquier otra diferencia exige NO-GO y nueva revisión; no actualizar hashes automáticamente para obtener verde. Los hashes binarios Windows originales de ambos corpus siguen siendo E9C631...E19F4 y 3DFE02...96E0, documentados en F7.

## Evidencia local real

Artefactos locales ignorados por la regla TestResults: .github/workflows/TestResults/g8/. No datos de producción. La compilación implícita de dotnet test --no-restore inicial fue correcta; no se solicitó build/publish independiente de aplicación porque no se modificó código. Recompilación mostró las advertencias preexistentes ya documentadas en F7; no se corrigieron aquí.

| Ejecución | Ejecutadas | Aprobadas | Fallidas | Omitidas | Resultado del gate |
|---|---:|---:|---:|---:|---|
| g8-current-baseline.trx, dotnet directo | 1491 | 1487 | 4 | 0 | NO-GO |
| g8-historical-baseline.trx, dotnet directo | 130 | 118 | 12 | 0 | Incompatibilidades verificadas |
| g8-current.trx, runner CI real local | 1491 | 1487 | 4 | 0 | Runner exit 1 / NO-GO |
| g8-historical.trx, runner CI real local | 130 | 118 | 12 | 0 | dotnet exit 1; verificador exit 0 sólo por coincidencia exacta |
| unittest del validador | 7 | 7 | 0 | 0 | OK, con variantes adversariales en subtests |

No se suman repeticiones: los dos TRX finales contienen **1621 casos distintos, 1605 aprobados, 16 fallidos**, doce históricos y cuatro actuales. El subconjunto F7 extraído del TRX completo mantiene **984/984**. Esto explica por qué la aprobación dirigida F7 no demostraba que todas las clases locales estuvieran verdes.

Se comprobaron las tres sintaxis YAML con PyYAML 6.0.3 instalado, sin instalar paquetes; sintaxis Python y pruebas unitarias del runner. Se ejecutaron los filtros y se validaron los TRX reales mediante el mismo código de CI. No se ejecutó el motor de GitHub Actions; su evaluación remota queda pendiente. No se repitieron frontend, PostgreSQL/Emulator o Redis: sus controles siguen presentes, pero no se presentan como ejecutados.

Una comparación estructural contra las capturas iniciales comprobó claves YAML únicas y conservación de triggers, permisos, concurrencia, runners, timeout, servicios, entorno, versiones de acciones y pasos frontend/integración/identidad. El workflow Redis permanece íntegro. También se analizó la sintaxis del Python embebido existente. Esto es validación local de sintaxis/controles, no una ejecución remota de GitHub Actions.

Las siete pruebas Python cubren aceptación del contrato exacto, casos ausentes/duplicados/ajenos, identidades duplicadas, fallo movido manteniendo cantidades, motivo distinto, salida errónea, omisiones, hash alterado, normalización CRLF/LF, cobertura actual/F3 v2, TRX inválido y definición incorrecta. Son pruebas del gate, no certificación de la aplicación.

Comandos de reproducción del gate, desde raíz después de compilar el proyecto de pruebas:

~~~powershell
python -B -m unittest discover -s .github/workflows -p test_g8_certification.py -v
python -B .github/workflows/g8_certification.py run current --trx .github/workflows/TestResults/g8/g8-current.trx
# Ejecutar también éste aunque el comando anterior termine en 1:
python -B .github/workflows/g8_certification.py run historical --trx .github/workflows/TestResults/g8/g8-historical.trx
~~~

El modo verify permite auditar un TRX existente indicando su código de salida observado; CI siempre usa run, que obtiene el código del proceso y no permite reemplazarlo por argumento.

## Cuatro fallos vigentes que BLOQUEAN el release

| Test y ubicación | Error exacto / diagnóstico localizado | Acción futura, no implementada |
|---|---|---|
| Rc3Sprint0IncidentTests.Incident_sequence_with_healthy_simulated_dependencies_replies_five_times_and_hola_uses_no_ai, :32 | Assert.Contains: falta '1. Consultar productos'; recibe 'Dato real autorizado del negocio: S/ 25.0…' | Revisar expectativa de menú obligatorio frente al contrato actual; no excluir ni debilitar aquí |
| Rc3Sprint0IncidentTests.NF05_an_explicit_option_phrase_under_a_current_menu_must_select_without_ai, :86 | Assert.NotNull: decision.Interpretation es null después del turno anterior | Revisar precondición real de menú/pregunta vigente; no asumir defecto productivo sin reproducción adicional |
| Rc3Sprint4CommercialTests.Order_flow_recovers_notification_after_business_commit_without_losing_draft_or_creating_second_order, :175 | Assert.Throws: no IOException. Inyecta ORDER con artículos y CurrentStep nulo; F7-01 devuelve revisión sin crear/notificar | Preparar resumen válido antes de probar el fallo de notificación, conservando aserciones, sólo bajo autorización separada |
| Rc3Sprint4IntegrityTests.Web_and_bot_creation_share_inactive_service_rules(bot: False), :104 | Assert.IsType: esperaba ObjectResult exacto, recibe BadRequestObjectResult; no alcanza comprobaciones siguientes | Revisar contrato HTTP/tipo y contenido antes de corregir la prueba; no inferir creación de servicio inactivo |

Rutas de esta tabla bajo backend/NexFlow.Tests/. Son fallos de prueba **reproducidos**, no cuatro incidentes productivos demostrados. No están en la lista de incompatibilidades F3 autorizadas. El runner los mantiene rojos. No se modificó código productivo ni ninguna de esas pruebas.

## Artefactos y controles preservados

- Release: artifact rc3-release-unit-results con rc3-unit.trx, rc3-historical.trx, sus .g8.json y JUnit TS/browser anteriores; upload always, siete días. JSON incluye GITHUB_SHA, GITHUB_RUN_ID, GITHUB_RUN_ATTEMPT y GITHUB_JOB, resultados/códigos y hashes sin configuración.
- PostgreSQL/Firestore: rc3-isolated-test-results, unit.trx + historical.trx + .g8.json, integration.trx e identity.json existentes. Si falla el gate local, la integración posterior se omite por dependencia del job: eso **no** acredita integración aprobada.
- Redis: concierge-redis-test-results, redis.trx e identity.json sin cambios.
- Ambos RC3 usan el mismo runner; no hay dos listas de excepciones que puedan divergir. Servicios, límites de tiempo, versiones/credenciales ficticias del runner y permisos mínimos se mantienen. No credenciales de Autoflow/Firebase real ni de Oracle.

## GO/NO-GO y publicación manual

GO del gate sólo si actual ≥1491 sin fallos/omisiones, subconjunto Runtime ≥984, F3 v2 ≥12, histórico exacto 130/118/12 con identidades/motivos/hashes intactos, frontend y los contratos externos exigidos aprobados en el **mismo SHA** y artefactos completos. Ninguna mejora de cantidades compensa un error o ausencia.

**Estado presente: NO-GO de certificación**, por cuatro fallos actuales y ausencia de runs remotos. Adecuación CI lista para auditoría; no publicación/despliegue autorizado. Comercial/piloto no se habilitan por este documento.

Procedimiento del propietario, sin ejecución por Codex:

1. Auditar estos cambios y los cuatro bloqueos. Autorizar por separado cualquier corrección posterior; no agregar exclusiones al runner.
2. Publicar manualmente únicamente el diff aprobado y fijar el nuevo SHA. No usar 664ad0e como si incluyera G8. Comprobar remoto/árbol por el procedimiento del propietario; este encargo no ejecuta Git.
3. Desde Actions, lanzar manualmente las tres definiciones sobre esa misma revisión. Si se lanzan antes de corregir los cuatro fallos, RC3 debe terminar rojo; conservar también sus históricos.
4. Revisar head_sha, Run ID, run_attempt, jobs y pasos; descargar TRX, .g8.json, identity.json y JUnit. Cambios posteriores en main no heredan resultados. No bastan el nombre del run ni su badge.
5. Exigir los gates completos antes de solicitar activación; la autorización de despliegue sigue siendo otra decisión humana. No dispatch automático, migraciones, SSH, cambios de plan/backup Firebase o servicios reales desde esta tarea.

Enlaces para ejecución/revisión manual:

- [RC3 release checks](https://github.com/saintdeyb321/NexFlow/actions/workflows/rc3-release-checks.yml).
- [PostgreSQL + Firestore Emulator](https://github.com/saintdeyb321/NexFlow/actions/workflows/rc3-reservation-integration.yml).
- [Redis aislado](https://github.com/saintdeyb321/NexFlow/actions/workflows/concierge-redis-integration.yml).

**Detención para auditoría externa. No se publica ni se despliega nada.**
