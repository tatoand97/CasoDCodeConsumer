using System;

namespace CasoDCodeConsumer;

public sealed class CasoDCodeConsumerSettings
{
    public const string SectionName = "CasoDCodeConsumer";

    public string ProjectEndpoint { get; set; } = string.Empty;

    public string ModelDeploymentName { get; set; } = string.Empty;

    public string OrderAgentId { get; set; } = string.Empty;

    public string RefundAgentId { get; set; } = string.Empty;

    public string ClarifierAgentId { get; set; } = string.Empty;

    public int ResponsesTimeoutSeconds { get; set; } = 60;

    public int ResponsesMaxBackoffSeconds { get; set; } = 8;

    public Uri GetValidatedProjectEndpointUri()
    {
        if (string.IsNullOrWhiteSpace(ProjectEndpoint))
        {
            throw new InvalidOperationException("CasoDCodeConsumer.ProjectEndpoint is required.");
        }

        if (!Uri.TryCreate(ProjectEndpoint, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("CasoDCodeConsumer.ProjectEndpoint must be a valid absolute URI.");
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("CasoDCodeConsumer.ProjectEndpoint must use HTTPS.");
        }

        if (!uri.AbsoluteUri.Contains("/api/projects/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("CasoDCodeConsumer.ProjectEndpoint must contain '/api/projects/'.");
        }

        if (string.IsNullOrWhiteSpace(ModelDeploymentName))
        {
            throw new InvalidOperationException("CasoDCodeConsumer.ModelDeploymentName is required.");
        }

        ValidateAgentReference(OrderAgentId, nameof(OrderAgentId));
        ValidateAgentReference(RefundAgentId, nameof(RefundAgentId));
        ValidateAgentReference(ClarifierAgentId, nameof(ClarifierAgentId));

        if (ResponsesTimeoutSeconds <= 0)
        {
            throw new InvalidOperationException("CasoDCodeConsumer.ResponsesTimeoutSeconds must be greater than zero.");
        }

        if (ResponsesMaxBackoffSeconds <= 0)
        {
            throw new InvalidOperationException("CasoDCodeConsumer.ResponsesMaxBackoffSeconds must be greater than zero.");
        }

        return uri;
    }

    private static void ValidateAgentReference(string configuredAgentId, string settingName)
    {
        if (string.IsNullOrWhiteSpace(configuredAgentId))
        {
            throw new InvalidOperationException($"CasoDCodeConsumer.{settingName} is required.");
        }

        var parts = configuredAgentId.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
        {
            throw new InvalidOperationException(
                $"CasoDCodeConsumer.{settingName} must use the format '<AgentName>:<Version>'.");
        }
    }
}
