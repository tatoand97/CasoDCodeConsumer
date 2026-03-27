namespace CasoDCodeConsumer.Models;

public sealed record ResolvedAgentIdentity(
    string AgentId,
    string AgentName,
    string AgentVersion,
    string ResponseClientAgentName);
