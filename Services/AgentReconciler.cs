using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using CasoDCodeConsumer.Models;
using System.ClientModel;

namespace CasoDCodeConsumer.Services;

public sealed class AgentReconciler
{
    private readonly AIProjectClient _projectClient;

    public AgentReconciler(AIProjectClient projectClient)
    {
        _projectClient = projectClient;
    }

    public async Task<AgentReconcileResult> ReconcileAsync(
        string agentName,
        PromptAgentDefinition targetDefinition,
        CancellationToken cancellationToken)
    {
        var targetHash = ComputeStructuralHash(targetDefinition);
        AgentVersion? latestVersion = null;

        try
        {
            _ = await _projectClient.Agents.GetAgentAsync(agentName, cancellationToken);
            latestVersion = await GetLatestVersionAsync(agentName, cancellationToken);
        }
        catch (ClientResultException exception) when (exception.Status == 404)
        {
            latestVersion = null;
        }

        if (latestVersion is not null)
        {
            var existingHash = ComputeStructuralHash(latestVersion.Definition);
            if (string.Equals(existingHash, targetHash, StringComparison.Ordinal))
            {
                return new AgentReconcileResult(
                    "Unchanged",
                    new ResolvedAgentIdentity(
                        latestVersion.Id,
                        latestVersion.Name,
                        latestVersion.Version,
                        latestVersion.Name));
            }
        }

        var options = new AgentVersionCreationOptions(targetDefinition)
        {
            Description = "Managed by CasoDCodeConsumer",
            Metadata =
            {
                ["managedBy"] = "CasoDCodeConsumer",
                ["structuralHash"] = targetHash
            }
        };

        var created = await _projectClient.Agents.CreateAgentVersionAsync(
            agentName: agentName,
            options: options,
            foundryFeatures: null,
            cancellationToken: cancellationToken);

        return new AgentReconcileResult(
            latestVersion is null ? "Created" : "Updated",
            new ResolvedAgentIdentity(
                created.Value.Id,
                created.Value.Name,
                created.Value.Version,
                created.Value.Name));
    }

    private async Task<AgentVersion?> GetLatestVersionAsync(string agentName, CancellationToken cancellationToken)
    {
        AgentVersion? latest = null;

        await foreach (var version in _projectClient.Agents.GetAgentVersionsAsync(
                           agentName: agentName,
                           limit: null,
                           order: null,
                           after: null,
                           before: null,
                           cancellationToken))
        {
            if (latest is null || CompareVersion(version.Version, latest.Version) > 0)
            {
                latest = version;
            }
        }

        return latest;
    }

    private static int CompareVersion(string left, string right)
    {
        if (int.TryParse(left, out var leftNumber) && int.TryParse(right, out var rightNumber))
        {
            return leftNumber.CompareTo(rightNumber);
        }

        return StringComparer.OrdinalIgnoreCase.Compare(left, right);
    }

    private static string ComputeStructuralHash(AgentDefinition definition)
    {
        var promptDefinition = definition as PromptAgentDefinition;
        var payload = JsonSerializer.Serialize(new
        {
            kind = promptDefinition is null ? definition.GetType().Name : "prompt",
            model = promptDefinition?.Model,
            instructions = promptDefinition?.Instructions,
            tools = promptDefinition?.Tools.Select(tool => tool.GetType().Name).OrderBy(name => name).ToArray() ?? Array.Empty<string>()
        });

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes);
    }
}

public sealed record AgentReconcileResult(string Action, ResolvedAgentIdentity Identity);
