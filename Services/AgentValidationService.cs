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

        var resolvedAgent = await ResolveAgentVersionByIdAsync(configuredAgentId, cancellationToken);
        if (resolvedAgent is null)
        {
            throw new InvalidOperationException(
                $"{logicalAgentName} '{configuredAgentId}' was not found or is not accessible in the configured Foundry project.");
        }

        _ = _projectClient.OpenAI.GetProjectResponsesClientForAgent(
            new AgentReference(resolvedAgent.AgentName, resolvedAgent.AgentVersion),
            null);

        return resolvedAgent;
    }

    private async Task<ResolvedAgentIdentity?> ResolveAgentVersionByIdAsync(
        string configuredAgentId,
        CancellationToken cancellationToken)
    {
        await foreach (var agent in _projectClient.Agents.GetAgentsAsync(limit: 100, cancellationToken: cancellationToken))
        {
            await foreach (var agentVersion in _projectClient.Agents.GetAgentVersionsAsync(
                               agent.Name,
                               limit: 100,
                               cancellationToken: cancellationToken))
            {
                if (!string.Equals(agentVersion.Id, configuredAgentId, StringComparison.Ordinal))
                {
                    continue;
                }

                return new ResolvedAgentIdentity(
                    AgentId: agentVersion.Id,
                    AgentName: agentVersion.Name,
                    AgentVersion: agentVersion.Version);
            }
        }

        return null;
    }
}
