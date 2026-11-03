# Cuotas y capacidad

## Hechos y supuestos

| Dato | Resultado |
|---|---|
| Cuota efectiva por cuenta/proyecto, modelos disponibles y precios | **UNKNOWN**. No se consultaron paneles/secretos/proveedores; las cifras del plan no se adoptan como límites efectivos |
| HTTP real a Groq/Gemini y envíos reales WhatsApp | **0**; transportes interceptados |
| Contrato del intérprete completo / piloto informativo | **8121 / 6656 caracteres**, reducción1465 (~18 %), fixture medido por `Read_only_pilot_prompt_omits_unused_order_edit_grammar_but_keeps_evidence_and_consent_rules`. No son tokens |
| Precisión NLU y latencia/tokens reales | **NOT_RUN**. No derivar de tiempos de tests ni JSON inyectado |
| Nuevas llamadas de composición IA | **0**, no se activó composición |
| Simulador | **7/7 PASS**, 144 escenarios **UNKNOWN**, 0 llamadas reales |

Se omite gramática de edición de pedidos cuando no está habilitada, manteniendo evidencia/cancelación/consentimiento. Navegación exacta y listas vigentes conservan rutas locales; el set congelado admite 0–1 invocaciones al intérprete por turno aislado. No representa HTTP de clientes reales.

## Admisión global (opt-in apagado)

`AiQuotaOptions.Enabled=false` por defecto. Implementación `FirestoreAiRequestAdmission`, registrada como `IAiRequestAdmission`, usa Firestore existente sin migraciones ni appsettings modificados.

- Documento transaccional por proveedor/pool lógico: `ai-provider-budgets/SHA256(provider:key)`. Modelos con cuota compartida deben estar en un mismo `Models`; no duplicar pools para multiplicarla. Key es etiqueta no secreta.
- Requiere `Provider`, `Key`, `Models`, `ResetTimeZone`, `RequestsPerMinute/Day`, `TokensPerMinute/Day`, `TokenReservationPerAttempt`, `SafetyMargin`, `WorkspaceShare`. Ausencia/valor inválido/identidad desconocida → rechazo cerrado sin HTTP, nunca ilimitado.
- RPM/TPM móviles60s; RPD/TPD por día en zona de reset verificada del pool. LastSeen impide retroceso. Margen y share duro por tenant reservan cuota para otros negocios; identidades ofuscadas. Límite1000 identidades/día y RPM configurada≤1000 para acotar documento.
- Reserva por intento exige al menos bytes UTF-8 de payload + salida máxima +2048. **No es tokenización ni uso real**: requiere calibrar cota por modelo, overhead oculto y tareas antes de activar. No está certificada para multimodalidad.
- Cada HTTP, incluidos fallback/modelo alternativo, requiere admisión propia. Rechazo no escribe consumo; transacciones reintentadas no duplican reserva lógica. Timeout/red/entrega incierta conserva reserva; no hay reembolso automático.
- 429 persiste Retry-After máximo observado. Sin espera de red en transacción. Fallback sólo usa transporte con presupuesto independiente. UNKNOWN/configuración no apta se comunica como indisponibilidad, no agotamiento real.
- Gemini tiene salida1024 en JSON/texto/chat; MIME JSON exclusivo del primer modo. No activa generación IA de respuestas.

### Cuenta pequeña: limitación demostrada

`Conservative_small_account_share_can_admit_zero_requests_and_never_bypasses_its_limit`: valores **sintéticos** RPM30/TPM8000, margen30 %, share10 %, reserva10000 → ninguna admisión. Reserva3200 que cubre el payload del fixture tampoco cabe en el share de560 TPM. Se rechaza sin saltarse límites. Antes de activar debe demostrarse al menos una llamada admisible por workspace objetivo; no copiar estos números a producción ni reducir evidencia para forzar admisión. Préstamo dinámico de shares queda fuera de esta corrección.

La admisión añade lectura/escritura por intento más cooldown; contención de un documento compartido, costo Firestore, precisión de relojes y throughput real son PENDIENTES. El fake comprueba lógica transaccional, no capacidad distribuida real.

## Observabilidad y simulación

AiTelemetry conserva intentos, duración, resultado/fallback, tokens sólo si se reportan; añade modelo y resultado de admisión. Lee sólo valores numéricos de `x-ratelimit-remaining-requests/tokens`, sin cuerpos/headers secretos. Workspace ofuscado en logs. Cabeceras opcionales no definen cuota. Aclaraciones/abandono y valoración humana necesitan ensayo real; no se inventan métricas.

```powershell
python -B -m unittest discover -s tools/ai -p test_capacity_simulator.py -v
python -B tools/ai/capacity_simulator.py --config tools/ai/capacity_assumptions.example.json --output backend/TestResults/NaturalFreeTier20261102/capacity
```

Salidas ignoradas: `capacity.csv`, `capacity.md`, `assumptions.json`. Cuadrícula10/20/30 negocios ×5/10/20 conversaciones/negocio/día ×0/1/2/4 interpretaciones ×0/1 composición ×2 proveedores =144 filas, incluyendo cargas100/200/400/600 conversaciones.

Supuestos:12 mensajes/conversación; entrada P50/P95 1800/2700, salida150/300; retry5 %, fallback10 %, pico20 % del día en10 min, margen30 %. Decimal+ceil evita subcontar intentos. Reporta RPM/RPD/TPM/TPD y tokens P50/P95, no latencia real. Ambos proveedores tienen límites null/UNKNOWN: no sumarlos como capacidad garantizada. Costo sólo con modo pagado y precios explícitos de entrada/salida.

## Gate externo

Propietario debe verificar en paneles cuenta/proyecto, modelos, RPM/RPD/TPM/TPD, reset, costos y política de datos; aportar valores no secretos. Después autorizar ensayo sintético acotado para calibrar tokens/latencia/share/costo Firestore. Sin claves por chat ni activación global. **Capacidad free tier del piloto: BLOCKED / UNKNOWN**.
