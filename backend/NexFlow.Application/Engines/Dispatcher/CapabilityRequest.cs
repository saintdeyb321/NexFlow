using System.Collections.Generic;

namespace NexFlow.Application.Engines.Dispatcher;

public record CapabilityRequest(
    string ModuleCode,
    string CapabilityCode,
    Dictionary<string, string> Parameters
);