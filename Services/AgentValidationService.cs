using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using CasoDCodeConsumer.Models;

namespace CasoDCodeConsumer.Services;

public sealed class AgentValidationService
{
    private readonly AIProjectClient _projectClient;

    public AgentValidationService(AIProjectClient projectClient)
    {
        _projectClient = projectClient;
    }

    public async Task ValidateProjectAccessAsync(CancellationToken cancellationToken)
    {
        await foreach (var _ in _projectClient.Agents.GetAgentsAsync(
                           kind: null,
                           limit: 1,
                           order: null,
                           after: null,
                           before: null,
                           cancellationToken))
        {
            break;
        }
    }

    public async Task<ResolvedAgentIdentity> ValidateConfiguredAgentAsync(
        string logicalAgentName,
        string configuredAgentId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(logicalAgentName))
        {
            throw new InvalidOperationException("Logical agent name must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(configuredAgentId))
        {
            throw new InvalidOperationException($"{logicalAgentName} must not be empty.");
        }

        var parts = configuredAgentId.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
        {
            throw new InvalidOperationException($"{logicalAgentName} must use the format '<AgentName>:<Version>'.");
        }

        var agentVersion = await _projectClient.Agents.GetAgentVersionAsync(
            agentName: parts[0],
            agentVersion: parts[1],
            cancellationToken: cancellationToken);

        _ = _projectClient.OpenAI.GetProjectResponsesClientForAgent(
            new AgentReference(agentVersion.Value.Name, agentVersion.Value.Version),
            null);

        return new ResolvedAgentIdentity(
            AgentId: agentVersion.Value.Id,
            AgentName: agentVersion.Value.Name,
            AgentVersion: agentVersion.Value.Version,
            ResponseClientAgentName: agentVersion.Value.Name);
    }
}
