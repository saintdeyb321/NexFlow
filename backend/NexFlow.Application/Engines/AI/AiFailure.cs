namespace NexFlow.Application.Engines.AI;

public enum AiFailureKind { None, RateLimit, Timeout, Unavailable, Configuration, InvalidResponse, Rejected }
public sealed class AiProviderConfigurationException(string provider) : Exception($"{provider}: falta una configuración válida de clave/modelo.");
public sealed class AiInvalidResponseException(string provider) : Exception($"{provider}: respuesta de IA inválida.");
