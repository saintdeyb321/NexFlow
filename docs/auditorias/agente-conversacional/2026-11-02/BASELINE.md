# Baseline

HEAD/main: `7c4782b35bd811cc26a502f2ae2b013e554f0238`. Staging vacío; dos planes nuevos del usuario conservados. Windows / PowerShell, proyecto SDK net10.0, xUnit 2.9.3, Moq 4.20.72; Firestore SDK fake existente, no Emulator real.

Build inicial en sandbox terminó exit 1 sin diagnóstico (0 errores impresos): no se interpreta como defecto de código. Repetición autorizada fuera de sandbox en ejecución. Evidencia de pruebas pendiente.

Build fuera de sandbox PASS, 21.69 s, 0 errores. Tres advertencias conocidas: CS8981 en refactor.cs/Designer y CS8602 FirestoreCatalogRepository.cs:228. No corregidas fuera del alcance.

`dotnet test backend/NexFlow.Tests/NexFlow.Tests.csproj --no-restore --no-build --filter "FullyQualifiedName~WhatsAppConversationalRepairTests|FullyQualifiedName~CtaF4LocalTests" --logger "trx;LogFileName=baseline.trx" --results-directory backend/TestResults/AgentStabilization20261102`: 42/42 PASS, 0 FAIL, 0 omitidas, ~5 s.

Pruebas nuevas, antes de cambios productivos: `--filter "FullyQualifiedName~AgentStabilizationTests"`, TRX reproduction.trx: 11 ejecutadas, 1 PASS, 10 FAIL, ~3 s. Causas: 2 variantes del matcher fallan; 2 consultas específicas responden catálogo sin aviso de stock; 4 variantes de intención de reserva no preparan borrador; 2 transcripciones se bloquean en G. No fueron fallos del runner ni de credenciales.

catalog-after.trx: 5/5 PASS tras primer parche. routing-after.trx: 12 PASS/3 FAIL de 15; dos fallos eran fixture nuevo que asumía orden de servicios contrario al listado real, corregido añadiendo un tercer servicio sintético; el otro descubrió que «con» se descartaba como ruido e identificaba una variante «sin». Se preservó ese calificador. transcript-after.trx: 15/15 PASS, cero omitidas.
