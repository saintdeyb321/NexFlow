namespace NexFlow.Application.Common;

public sealed class ArtifactDependencyException(string message, Exception? innerException = null) : Exception(message, innerException);
