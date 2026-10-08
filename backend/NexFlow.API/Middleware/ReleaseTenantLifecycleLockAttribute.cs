namespace NexFlow.API.Middleware;

// These actions acquire their own short database lease before calling an external provider.
[AttributeUsage(AttributeTargets.Method)]
public sealed class ReleaseTenantLifecycleLockAttribute : Attribute;
