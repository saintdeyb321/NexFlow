# NexFlow RC-3 — memoria de progreso

Actualización: 2026-10-19 (America/Lima).
Antes de cada sesión leer `AGENTS.md`, `NexFlow_PLAN_MAESTRO_CORRECCION_MVP_RC3.md` y este registro.

## Estado

- Sprint actual: **0**. Diagnóstico/reproducción local finalizados; revisión solicitada. Forense operacional pendiente.
- HEAD de referencia: `67016bdb37c054711e57e0ac3c048b9999eedae6`.
- Estado inicial: sin cambios versionados ni staged; plan maestro nuevo/no versionado, conservado intacto. ZIP no disponible en raíz; GitHub no consultado.
- **Próximo sprint autorizado: ninguno.** Sprint 1 propuesto; requiere autorización explícita. No avanzar automáticamente.
- Veredicto: **NO-GO para despliegue/producción**. No hay una causa operacional única demostrada.

## Evidencia y decisiones

- Ver diagnóstico/matriz/archivos de código y propuesta de parche mínimo en `INCIDENTE_CAUSA_RAIZ.md`.
- Reproducidos NF-02 (perfil antes del try), NF-03 (identidad compartida de avisos), NF-04 (fallo parcial oculta Inbox) y NF-05 (frase de selección no determinista).
- NF-01: predicado FIFO comprobado con adaptador SQLite; no valida locks ni concurrencia PostgreSQL.
- Secuencia exacta con intérprete/transporte simulados sanos: cinco respuestas, Hola? sin IA, cero operaciones comerciales. No reproduce el silencio operacional.
- NF-06: Processed significa turno finalizado, no entrega; preservar alertas existentes y UnknownDelivery sin reenvío.
- No se modificó código de aplicación. No duplicar mecanismos ni aplicar aún los parches sugeridos.

## Archivos creados por Sprint 0

- `backend/NexFlow.Tests/Rc3Sprint0IncidentTests.cs` (5 casos).
- `frontend/tests/rc3-inbox-browser.test.mjs` (3 casos) y `rc3-inbox-preview.server.mjs`.
- `frontend/tests/fixtures/rc3-inbox-api.ts`, `rc3-inbox-preview.tsx`, `rc3-inbox-preview.html`.
- `INCIDENTE_CAUSA_RAIZ.md` y este registro.

## Validación

- Baseline backend antes de reproducciones: **500/500**.
- Backend con pruebas nuevas: **502 aprobadas / 3 fallidas / 0 omitidas, total 505**. Rojas: NF-02, NF-03 (Sprint 1), NF-05 (Sprint 2); sin debilitar ni omitir expectativas.
- Navegador Inbox: **1 aprobada / 2 fallidas / 0 omitidas**. Fallan ambos errores parciales NF-04 (Sprint 2), conservando datos en caché.
- Build backend correcto; build frontend correcto; lint frontend correcto.
- Advertencias conocidas: CS8981 x2 y CS8602 x1 al recompilar backend; chunk JS >500 kB en frontend. No corregidas por estar fuera de alcance.
- Comandos exactos y límites de los fakes registrados en el diagnóstico. Ninguna dependencia instalada.

## Pendientes antes de un GO de despliegue

Imagen realmente activa durante el incidente; salida agregada SQL/logs del intervalo UTC; recepción webhook y estados outbound/Evolution; modo/licencia; errores HTTP de Inbox; contraste ZIP/remoto si se aporta evidencia.
No ejecutar Oracle ni publicar. No reinyectar Outbox PDF histórico ni UnknownDelivery.
**Operaciones externas ejecutadas: ninguna.** Sin commit, push, deploy, secretos, migraciones o infraestructura modificados.
