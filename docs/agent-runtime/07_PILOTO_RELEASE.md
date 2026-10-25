# F7 — Piloto y release: preparación suspendida por P1

Referencia local: `85d349ac5690fbc3dcd3d97b72cf0c3a45e2ff23`, leída en `.git/HEAD` y `.git/refs/heads/main`. No se ejecutó Git. Esa lectura no certifica árbol limpio, remoto ni imagen activa en Oracle. La prueba añadida y estos informes son cambios locales posteriores a esa referencia, sin nuevo SHA publicado.

## Gate de parada activado

**NO-GO técnico local. No activar ni desplegar esta candidata.** Durante la auditoría de consentimiento se reprodujo F7-01, P1: después de revocarse la licencia durante un envío, el borrador conserva productos pero pierde la huella del resumen; restaurada la licencia y actualizado el precio, un nuevo envío crea la solicitud con importes que no se volvieron a mostrar.

La instrucción F7 exige documentar un P0/P1 nuevo y detenerse sin corregirlo. Por ello este documento registra una preparación **incompleta**, no un runbook ejecutable aprobado. El diagnóstico, la prueba y su resultado exacto están en `08_CIERRE_AGENT_RUNTIME_4.md`. No se modificó código productivo.

| Decisión | Estado y motivo |
|---|---|
| GO técnico local | **NO-GO**: F7-01 reproducido; regresión general F7 pendiente |
| GO para preparar piloto | **PENDIENTE**: esta preparación queda suspendida; versión operativa y rollback no verificados |
| GO para activar piloto | **NO-GO**: P1 abierto, autorización, aislamiento, respaldos y métricas operativas pendientes |
| GO comercial | **NO-GO**: además faltan corpus independiente, revisión humana, proveedores/canal y costos reales |

## Primer paso seguro y reanudación

El primer paso seguro es auditar F7-01 y autorizar por separado su corrección mínima. No se requiere acceder a Oracle para reproducirlo. Conservar la prueba roja y la evidencia; no debilitar expectativas para obtener verde.

Después del arreglo autorizado: ejecutar la prueba hasta verde, cubrir revocación/restauración, resumen inexistente y recuperación de un efecto ya persistido; repetir build, contrato vigente, históricos originales completos, F3 v2 y regresiones pertinentes. Congelar una nueva candidata y vincular evidencia al nuevo SHA/árbol. No atribuirle resultados de otro commit.

Sólo después, el operador podrá aportar evidencia sanitizada de la imagen API activa: referencia, ID/digest, arquitectura y etiqueta de revisión si existe. Una etiqueta no sustituye al digest ni prueba por sí sola la correspondencia de fuentes. No volcar variables de entorno, Compose interpolado íntegro, teléfonos, mensajes ni claves al informe.

## Preflight operativo aún pendiente

No se ejecutó ni se certificó ninguna de estas operaciones:

1. Comparar la candidata congelada con la versión realmente activa y revisar incompatibilidades de contratos, contexto y esquema. No asumir ausencia de migraciones sólo porque F7 no las modifica.
2. Revisar Dockerfile, contexto de construcción y archivos/overrides de Compose efectivos. Preparar entonces comandos exactos con sus rutas y nombre real del servicio; no inventar que el servicio se llama `api`.
3. Construir Linux ARM64 con versiones/digests de base y dependencias identificados, registrar digest y arquitectura; verificar usuario no root, bibliotecas nativas y codecs mediante la prueba existente, si corresponde. ARM64 y SkiaSharp no quedan aprobados por un build .NET de Windows.
4. Confirmar salud autorizada de PostgreSQL, Redis, Firestore, Evolution, n8n y Gotenberg; workers, cola, Outbox y conectividad. No reiniciar servicios compartidos para probar salud.
5. Confirmar backup y restauración **aislada** comprobable de los almacenes durables, alcance y fecha del respaldo, acceso restringido y procedimiento de restauración; no restaurar sobre producción para ensayar.
6. Preparar rollback exclusivamente del binario/imagen API compatible, con digest anterior retenido, configuración compatible y control de escritores. Un binario antiguo que ignore referencias conversacionales nuevas puede perder garantías: no dar por segura la convivencia de escritores.
7. Con autorización separada, reemplazar sólo el servicio API y comprobar salud y pruebas autorizadas. No ejecutar `docker compose down`, `prune`, migrator, borrado de volúmenes ni recreación automática de dependencias.

**Rollback:** no borra ni revierte reservas/pedidos ya confirmados, notificaciones ni mensajes enviados. No reprocesar DeadLetters ni reenviar UnknownDelivery para simular una reversión. Si no existe rollback compatible demostrado, detener la activación.

## Piloto propuesto, no ejecutado

Workspace aislado autorizado, contactos de prueba consentidos y presupuesto aprobado. Mismo runtime para clínica multisede, hotel y tienda; ninguna expectativa privada en este repositorio.

| Etapa | Evidencia exigida antes de avanzar |
|---|---|
| Información | Perfil público, sedes/horarios, servicios/productos, precios, FAQ y referencias correctos; revocación sin lectura restringida; sin datos de otro workspace |
| Reservas | Primero consultas/borradores sin creación. Después, sólo con autorización explícita, reserva aislada con resumen vigente, UTC/hora local, persistencia y efecto único |
| Pedidos | Solicitud ficticia consentida PendingReview, importes revisados, aviso durable único y recuperación; sin cobro, stock confirmado ni recogida garantizada |
| Control humano | Takeover suprime bot pendiente; devolución de control por mecanismo existente. Un transporte ya iniciado no se puede retirar físicamente |
| Proveedores | Groq/Gemini y fallback, 429, timeout, JSON inválido y presupuesto; fallos inducidos únicamente en entorno aislado, sin afectar otros negocios |
| Calidad y carga | Evaluador independiente, corpus privado de al menos 30 conversaciones inéditas y multitur, rúbrica humana, dos o más workspaces y carga acotada autorizada |

Rúbrica humana separada: comprensión, continuidad, orientación, presentación y operaciones. Puntuar claridad/utilidad y registrar referencias correctas, repeticiones, borrador conservado, hechos respaldados, tarea terminada o derivación segura. Una suite de mocks no demuestra calidad de Gemini/Groq ni entrega WhatsApp.

## Observabilidad, costos y parada

Antes de activar, verificar recolección/retención de los Meters y logs sanitizados existentes: HTTP por proveedor/tarea/rol, fallback, usage conocido/desconocido, presupuesto, latencia de fases/cola, aclaraciones repetidas, lecturas/escrituras de contexto y efectos durables únicos. No interpretar BusinessResult como número de pedidos nuevos ni sumar duraciones anidadas. Sin exporter operativo verificado no afirmar monitorización activa.

Costo por conversación = costo de tokens realmente informados por modelo/intento a tarifa verificada + costos atribuibles de infraestructura/almacenamiento/canal. Separar usage desconocido; no valorarlo como cero ni sumar Total a Input+Output. Multiplicar el promedio de una muestra representativa por 1.000 e indicar ventana, mezcla local/IA/fallback y cobertura. Tarifas, consumo y costos reales siguen pendientes. Definir presupuesto total de piloto, alarma y límite de participantes antes de ejecutarlo; no cambiar cuotas comerciales existentes.

Parada inmediata ante cruce de workspace, operación sin consentimiento vigente, duplicación, bot después de takeover, pérdida de contexto con efecto indebido, invención comercial, canal gravemente degradado, costo sin control o rollback incompatible. F7-01 ya activa esta condición. Se requiere auditoría humana antes de reanudar; no hay autorización de despliegue.
