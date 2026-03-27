namespace CasoDCodeConsumer.Models;

public sealed record ResolvedAgentsState(
    ResolvedAgentIdentity OrderAgent,
    ResolvedAgentIdentity RefundAgent,
    ResolvedAgentIdentity ClarifierAgent);
