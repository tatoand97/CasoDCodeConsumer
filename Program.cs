using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.AI.Projects;
using Azure.Identity;
using CasoDCodeConsumer;
using CasoDCodeConsumer.Models;
using CasoDCodeConsumer.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables();

var settingsSection = builder.Configuration.GetSection(CasoDCodeConsumerSettings.SectionName);
var settings = settingsSection.Get<CasoDCodeConsumerSettings>() ?? new CasoDCodeConsumerSettings();

ConsoleTrace.Bootstrap("bootstrap validation started");
var projectEndpoint = settings.GetValidatedProjectEndpointUri();
ConsoleTrace.Validation("foundry endpoint validated");

var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
{
    ExcludeInteractiveBrowserCredential = true
});

var projectClient = new AIProjectClient(projectEndpoint, credential);
var agentValidationService = new AgentValidationService(projectClient);
var agentRunner = new AgentRunner(projectClient, settings);
var orderIdExtractor = new OrderIdExtractor();
var intentRouter = new IntentRouter(orderIdExtractor);
var outputValidators = new OutputValidators();

await agentValidationService.ValidateProjectAccessAsync(CancellationToken.None);
ConsoleTrace.Validation("project access validated");

var orderAgent = await agentValidationService.ValidateConfiguredAgentAsync(
    nameof(CasoDCodeConsumerSettings.OrderAgentId),
    settings.OrderAgentId,
    CancellationToken.None);
ConsoleTrace.Validation("order agent validated");

var refundAgent = await agentValidationService.ValidateConfiguredAgentAsync(
    nameof(CasoDCodeConsumerSettings.RefundAgentId),
    settings.RefundAgentId,
    CancellationToken.None);
ConsoleTrace.Validation("refund agent validated");

var clarifierAgent = await agentValidationService.ValidateConfiguredAgentAsync(
    nameof(CasoDCodeConsumerSettings.ClarifierAgentId),
    settings.ClarifierAgentId,
    CancellationToken.None);
ConsoleTrace.Validation("clarifier agent validated");

var resolvedAgents = new ResolvedAgentsState(orderAgent, refundAgent, clarifierAgent);
ConsoleTrace.Bootstrap("bootstrap validation completed");

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new HealthResponse(
    "ok",
    ToHealthAgentMetadata(resolvedAgents.OrderAgent),
    ToHealthAgentMetadata(resolvedAgents.RefundAgent),
    ToHealthAgentMetadata(resolvedAgents.ClarifierAgent))));

app.MapPost("/orchestrate", async (OrchestrateRequest? request, CancellationToken cancellationToken) =>
{
    ConsoleTrace.Request("request received");

    if (request is null || string.IsNullOrWhiteSpace(request.Prompt))
    {
        ConsoleTrace.Failure("request failed: prompt is required.");
        return Results.BadRequest(new { error = "Prompt is required." });
    }

    try
    {
        var prompt = request.Prompt.Trim();
        var decision = RoutePrompt(intentRouter, prompt);
        ConsoleTrace.Router($"routing completed: {decision.RouteKind}");

        var execution = decision.RouteKind switch
        {
            RouteKind.Order => await RunOrderBranchAsync(
                agentRunner,
                outputValidators,
                resolvedAgents.OrderAgent,
                prompt,
                decision.OrderId,
                cancellationToken),
            RouteKind.Refund => await RunRefundBranchAsync(agentRunner, outputValidators, resolvedAgents.RefundAgent, prompt, cancellationToken),
            RouteKind.Clarify => await RunClarifyBranchAsync(agentRunner, outputValidators, intentRouter, resolvedAgents.ClarifierAgent, prompt, decision, cancellationToken),
            RouteKind.Reject => new BranchExecution(BuildRejectResponse(decision)),
            _ => throw new InvalidOperationException($"Unsupported route: {decision.RouteKind}")
        };

        var finalResponse = BuildFinalResponse(decision, execution);
        ConsoleTrace.Final("response built successfully");

        return Results.Ok(new OrchestrateResponse(
            Prompt: prompt,
            Route: decision.RouteKind.ToString(),
            Response: finalResponse,
            OrderId: execution.OrderResult?.Id ?? execution.RefundResult?.OrderId ?? decision.OrderId,
            RefundReason: execution.RefundResult?.RefundReason ?? decision.RefundReason,
            Reason: decision.Reason));
    }
    catch (Exception exception)
    {
        ConsoleTrace.Failure($"request failed: {exception.Message}");
        return Results.Problem(
            title: "Orchestration failed",
            detail: exception.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.Run();

static RouteDecision RoutePrompt(IntentRouter router, string prompt)
{
    return router.Route(prompt);
}

static HealthAgentMetadata ToHealthAgentMetadata(ResolvedAgentIdentity agent)
{
    return new HealthAgentMetadata(agent.AgentId, agent.AgentName, agent.AgentVersion);
}

static async Task<BranchExecution> RunOrderBranchAsync(
    AgentRunner agentRunner,
    OutputValidators outputValidators,
    ResolvedAgentIdentity orderAgent,
    string prompt,
    string? requestedOrderId,
    CancellationToken cancellationToken)
{
    if (string.IsNullOrWhiteSpace(requestedOrderId))
    {
        throw new InvalidOperationException("Order route requires a resolved order ID.");
    }

    ConsoleTrace.Agent("Invoking OrderAgent");
    var rawResponse = await agentRunner.RunAsync(orderAgent, BuildOrderPrompt(prompt, requestedOrderId), cancellationToken);
    var orderResult = outputValidators.ValidateOrderResult(rawResponse);
    return new BranchExecution(null, orderResult, null, null);
}

static async Task<BranchExecution> RunRefundBranchAsync(
    AgentRunner agentRunner,
    OutputValidators outputValidators,
    ResolvedAgentIdentity refundAgent,
    string prompt,
    CancellationToken cancellationToken)
{
    ConsoleTrace.Agent("Invoking RefundAgent");
    var rawResponse = await agentRunner.RunAsync(refundAgent, prompt, cancellationToken);
    var refundResult = outputValidators.ValidateRefundResult(rawResponse);
    return new BranchExecution(null, null, refundResult, null);
}

static async Task<BranchExecution> RunClarifyBranchAsync(
    AgentRunner agentRunner,
    OutputValidators outputValidators,
    IntentRouter intentRouter,
    ResolvedAgentIdentity clarifierAgent,
    string prompt,
    RouteDecision decision,
    CancellationToken cancellationToken)
{
    ConsoleTrace.Agent("Invoking ClarifierAgent");
    var summary = intentRouter.BuildClarificationSummary(decision, prompt);
    var rawResponse = await agentRunner.RunAsync(clarifierAgent, summary, cancellationToken);
    var clarifierResult = outputValidators.ValidateClarifierResult(rawResponse);
    return new BranchExecution(null, null, null, clarifierResult);
}

static string BuildRejectResponse(RouteDecision decision)
{
    if (decision.Reason.Contains("Destructive", StringComparison.OrdinalIgnoreCase))
    {
        return "I can't help with destructive or unsupported order operations.";
    }

    return "I can only help with order status questions and refund requests.";
}

static string BuildFinalResponse(RouteDecision decision, BranchExecution execution)
{
    return decision.RouteKind switch
    {
        RouteKind.Order when execution.OrderResult is not null => BuildOrderResponse(execution.OrderResult),
        RouteKind.Refund when execution.RefundResult is not null => execution.RefundResult.Message,
        RouteKind.Clarify when execution.ClarifierResult is not null => execution.ClarifierResult.Question,
        RouteKind.Reject when execution.RejectResponse is not null => execution.RejectResponse,
        _ => throw new InvalidOperationException("The selected branch did not produce a final response.")
    };
}

static string BuildOrderPrompt(string prompt, string requestedOrderId)
{
    return string.Format(
        """
        Retrieve only structured order data for the requested order.
        Use your configured MCP tool if applicable.
        Return exactly one JSON object and nothing else.
        No markdown.
        No prose outside JSON.

        Required fields:
        - id
        - status
        - requiresAction

        Optional field:
        - reason

        Allowed status values:
        - Created
        - Confirmed
        - Packed
        - Shipped
        - Delivered
        - Cancelled
        - Unknown
        - NotFound

        If the order is not found, return:
        {{"id":"{1}","status":"NotFound","requiresAction":false,"reason":"Order not found"}}

        User request:
        {0}
        """,
        prompt,
        requestedOrderId);
}

static string BuildOrderResponse(OrderResult orderResult)
{
    var baseMessage = orderResult.Status switch
    {
        "Created" => $"Order {orderResult.Id} was created.",
        "Confirmed" => $"Order {orderResult.Id} is confirmed.",
        "Packed" => $"Order {orderResult.Id} is packed.",
        "Shipped" => $"Order {orderResult.Id} has been shipped.",
        "Delivered" => $"Order {orderResult.Id} was delivered.",
        "Cancelled" => $"Order {orderResult.Id} was cancelled.",
        "Unknown" => $"Order {orderResult.Id} has an unknown status.",
        "NotFound" => $"Order {orderResult.Id} was not found.",
        _ => $"Order {orderResult.Id} returned status {orderResult.Status}."
    };

    if (!string.IsNullOrWhiteSpace(orderResult.Reason))
    {
        baseMessage = $"{baseMessage} {EnsureSentence(orderResult.Reason)}";
    }

    if (orderResult.RequiresAction)
    {
        baseMessage = $"{baseMessage} Action is required.";
    }

    return baseMessage.Trim();
}

static string EnsureSentence(string value)
{
    var trimmed = value.Trim();
    if (trimmed.EndsWith(".", StringComparison.Ordinal) ||
        trimmed.EndsWith("!", StringComparison.Ordinal) ||
        trimmed.EndsWith("?", StringComparison.Ordinal))
    {
        return trimmed;
    }

    return $"{trimmed}.";
}

internal sealed record OrchestrateRequest(string Prompt);

internal sealed record OrchestrateResponse(
    string Prompt,
    string Route,
    string Response,
    string? OrderId,
    string? RefundReason,
    string Reason);

internal sealed record HealthResponse(
    string Status,
    HealthAgentMetadata OrderAgent,
    HealthAgentMetadata RefundAgent,
    HealthAgentMetadata ClarifierAgent);

internal sealed record HealthAgentMetadata(string Id, string Name, string Version);

internal sealed record BranchExecution(
    string? RejectResponse,
    OrderResult? OrderResult = null,
    RefundResult? RefundResult = null,
    ClarifierResult? ClarifierResult = null);
