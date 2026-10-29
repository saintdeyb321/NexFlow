# NexFlow — Cierre técnico G8/H06 y preparación del piloto DEMO

Fecha: 2026-10-29. Repositorio: `saintdeyb321/NexFlow`.

## 1. Fuente auditada

- Commit publicado: `5eada67ea0769f7d7f3b50fec9c679a319647601` (`CORRECCIÓN FOCALIZADA DE GATES RC3/G8`).
- Padre: `e34ff9c5087810355b5f504b6ad4cc60ce2d5553`.
- Diff publicado: 11 archivos (nueve clases productivas, `ComposedConversationTests.cs` y `G8FocusedRegressionTests.cs`).
- El cierre local reporta 1711/1711 pruebas actuales y 130 históricas: 101 PASS/29 FAIL, pero **estos resultados locales no se ejecutaron independientemente en esta auditoría**.
- No hay workflows GitHub Actions para el commit `5eada67` al cierre de esta revisión.

## 2. Juicio técnico

Las correcciones publicadas tienen correspondencia directa con las regresiones detectadas en los runs RC3 anteriores: `¿Puedo reservar?` informativo, invitación a preparar un borrador, contradicciones, importes verificados y presentación, estado de preferencia horaria. El diff del backend es limitado al alcance. Esto no certifica la comprensión de Gemini/Groq reales ni la naturalidad conversacional. Las pruebas históricas H06/H19 usan fixtures controlados.

**H06 es una incompatibilidad de seguridad deliberada, no una razón para restaurar consentimiento débil.** Su conversación histórica esperaba una reserva tras `De acuerdo.`; la política vigente no permite que un simple acuse confirme una transacción. En cambio, aceptar una invitación vigente para *preparar* una reserva sí puede iniciar un borrador sin efectos comerciales.

El script G8 del commit `5eada67` mantiene `(130, 102, 28)` y no reconoce H06. También las pruebas del validador `test_g8_certification.py` fijan los 28 fallos previos. Deben actualizarse juntos, sin tocar los cuatro archivos inmutables (dos JSON de corpus y dos evaluadores C#), sus hashes, ni el filtro de las 130 identidades.

## 3. Parche preparado y comprobado

Archivo: `NexFlow_G8_H06_consent_v2.patch`.

- `g8_certification.py`: añade únicamente H06 con `H06/4/A: ReservationEffects`; cambia el conteo histórico exigido a `130/101/29`.
- `test_g8_certification.py`: sincroniza los conteos sintéticos, el número de incompatibilidades y añade una prueba de rechazo si cambia el motivo de H06.
- No altera la verificación `test_exit_code == 1`, la identidad exacta de los casos, los hashes fijados, los motivos de las otras 28 incompatibilidades ni los requisitos de la suite actual.
- Los hashes Git blob de ambas versiones originales del parche coinciden con las versiones publicadas en `5eada67` (G8: `994794b5e62e4196721a5309fc60b257d3d5bea9`; tests: `88868ce249080147540bbbb3dae54227328fd720`).
- `git apply --check` en copia exacta de las fuentes: PASS.
- `python -m unittest -v test_g8_certification` sobre las fuentes corregidas y los corpus originales: **9/9 PASS**. Sin .NET ni servicios externos.

**Límite importante:** no se ejecutaron con el parche las 1711 pruebas .NET ni las 130 históricas reales en esta auditoría; se requiere validar el nuevo SHA en las gates oficiales.

## 4. Cómo aplicarlo localmente

Conservar cualquier cambio local. Revisar el estado y no aplicar si los dos archivos ya fueron editados manualmente.

```powershell
cd C:\Proyectos\NexFlow
git status --short
$patch = "$env:USERPROFILE\Downloads\NexFlow_G8_H06_consent_v2.patch"
git apply --check $patch
git apply $patch
python -m unittest discover -s .github/workflows -p test_g8_certification.py -v
git diff --check
git diff -- .github/workflows/g8_certification.py .github/workflows/test_g8_certification.py
```

Si la carpeta de descarga es diferente, corregir `$patch` a la ruta real. La aplicación del parche **no crea commit ni hace push**.

Solicitar al propietario revisión del diff y, con su autorización, registrar/push un único commit. Congelar su SHA nuevo. No modificar corpus históricos ni cambiar reglas de negocio para hacer pasar H06.

## 5. Certificación antes de Oracle

Ejecutar los cuatro workflows oficiales manualmente **sobre el mismo SHA nuevo**: `rc3-release-checks.yml`, `rc3-reservation-integration.yml`, `concierge-redis-integration.yml`, `agent-runtime-arm64-certification.yml`. Para ARM64, `release_sha` debe ser exactamente ese SHA y el ref seleccionado debe coincidir. Confirmar `head_sha`, conclusiones, run IDs y artefactos. La batería histórica **debe terminar con fallo real 101/29**, pero su *validador* G8 debe declarar ese resultado como la incompatibilidad esperada, no como un fallo de gate.

No reutilizar los verdes previos sobre `e34ff9c` como certificación del nuevo SHA. No desplegar con gates fallidas.

Después: respaldo reciente de PostgreSQL, inventario del Docker Compose efectivo en Oracle, imagen ARM64 etiquetada con el SHA, comprobación de salud y cambio **solo de `nexflow_api`**, sin reiniciar PostgreSQL, Redis, Evolution, n8n ni Caddy. La versión activa debe conservarse identificada para reversión evaluada, **no automática**. No ejecutar migraciones sin revisión. El piloto es exclusivamente para el negocio DEMO con datos ficticios.

## 6. Simulación real de aceptación

Evaluar por WhatsApp, observando texto y efectos persistidos: consulta `¿Puedo reservar?` sin borrador prematuro; aceptar una invitación solo para iniciar borrador; H06 con `De acuerdo.` sin confirmación y `Confirmar` una sola vez; `En la de conce`; catálogo global 5 frente a sede con 4, con explicación; `Editar → La sede → Sanjeronimo` conservando datos válidos; referencias `La 1`; `Hoy a las 8pm` / `¿Y a las 7?`; pedido coloquial; consulta de precio en moneda mixta; reenvío duplicado y transferencia humana. El modelo real y la redacción contextual general siguen sin certificar.

**Veredicto al cierre:** GO para aplicar el parche y preparar nuevo SHA; NO-GO para desplegar hasta que las cuatro certificaciones del SHA actualizado pasen.
