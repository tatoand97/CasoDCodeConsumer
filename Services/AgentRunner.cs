using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using CasoDCodeConsumer.Models;
using OpenAI.Responses;

namespace CasoDCodeConsumer.Services;

public sealed class AgentRunner
{
    private readonly AIProjectClient _projectClient;
    private readonly CasoDCodeConsumerSettings _settings;

    public AgentRunner(AIProjectClient projectClient, CasoDCodeConsumerSettings settings)
    {
        _projectClient = projectClient;
        _settings = settings;
    }

    public async Task<string> RunAsync(ResolvedAgentIdentity agent, string prompt, CancellationToken cancellationToken)
    {
        var conversation = await _projectClient.OpenAI.GetProjectConversationsClient().CreateProjectConversationAsync(
            new ProjectConversationCreationOptions(),
            cancellationToken);

        var agentReference = new AgentReference(agent.AgentName, agent.AgentVersion);
        var responsesClient = _projectClient.OpenAI.GetProjectResponsesClientForAgent(agentReference, conversation.Value.Id);

        var response = await responsesClient.CreateResponseAsync(
            userInputText: prompt,
            previousResponseId: null,
            cancellationToken: cancellationToken);

        var finalResponse = await WaitForTerminalResponseAsync(
            responsesClient,
            agentReference,
            conversation.Value.Id,
            response.Value,
            cancellationToken);

        return finalResponse.GetOutputText();
    }

    private async Task<ResponseResult> WaitForTerminalResponseAsync(
        ProjectResponsesClient responsesClient,
        AgentReference agentReference,
        string conversationId,
        ResponseResult currentResponse,
        CancellationToken cancellationToken)
    {
        if (TryEnsureTerminal(currentResponse, out var completedResponse))
        {
            return completedResponse;
        }

        var timeout = TimeSpan.FromSeconds(_settings.ResponsesTimeoutSeconds);
        var maxBackoff = TimeSpan.FromSeconds(_settings.ResponsesMaxBackoffSeconds);
        var startedAt = DateTimeOffset.UtcNow;
        var delay = TimeSpan.FromSeconds(1);

        while (DateTimeOffset.UtcNow - startedAt < timeout)
        {
            await Task.Delay(delay, cancellationToken);

            var refreshed = await RefreshResponseAsync(
                responsesClient,
                agentReference,
                conversationId,
                currentResponse.Id,
                cancellationToken);

            if (refreshed is not null)
            {
                currentResponse = refreshed;
                if (TryEnsureTerminal(currentResponse, out completedResponse))
                {
                    return completedResponse;
                }
            }

            var nextDelaySeconds = Math.Min(delay.TotalSeconds * 2, maxBackoff.TotalSeconds);
            delay = TimeSpan.FromSeconds(Math.Max(1, nextDelaySeconds));
        }

        throw new InvalidOperationException(
            $"Response {currentResponse.Id} did not reach a terminal state within {_settings.ResponsesTimeoutSeconds} seconds.");
    }

    private static bool TryEnsureTerminal(ResponseResult response, out ResponseResult completedResponse)
    {
        completedResponse = response;
        return response.Status switch
        {
            ResponseStatus.Completed => true,
            ResponseStatus.Failed => throw BuildTerminalFailure(response),
            ResponseStatus.Incomplete => throw BuildTerminalFailure(response),
            ResponseStatus.Cancelled => throw BuildTerminalFailure(response),
            _ => false
        };
    }

    private static InvalidOperationException BuildTerminalFailure(ResponseResult response)
    {
        var reason = response.Error?.Message;
        if (string.IsNullOrWhiteSpace(reason) && response.IncompleteStatusDetails is not null)
        {
            reason = response.IncompleteStatusDetails.ToString();
        }

        return new InvalidOperationException(
            $"Response {response.Id} finished with status '{response.Status}'. {reason}".Trim());
    }

    private static async Task<ResponseResult?> RefreshResponseAsync(
        ProjectResponsesClient responsesClient,
        AgentReference agentReference,
        string conversationId,
        string responseId,
        CancellationToken cancellationToken)
    {
        await foreach (var response in responsesClient.GetProjectResponsesAsync(
                           agent: agentReference,
                           conversationId: conversationId,
                           limit: 50,
                           order: null,
                           after: null,
                           before: null,
                           cancellationToken))
        {
            if (string.Equals(response.Id, responseId, StringComparison.Ordinal))
            {
                return response;
            }
        }

        return null;
    }
}
