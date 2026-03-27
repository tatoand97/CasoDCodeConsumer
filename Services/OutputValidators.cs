using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using CasoDCodeConsumer.Models;

namespace CasoDCodeConsumer.Services;

public sealed class OutputValidators
{
    private static readonly HashSet<string> AllowedOrderStatuses =
    [
        "Created",
        "Confirmed",
        "Packed",
        "Shipped",
        "Delivered",
        "Cancelled",
        "Unknown",
        "NotFound"
    ];

    public OrderResult ValidateOrderResult(string rawJson)
    {
        var root = ParseRootObject(rawJson);
        EnsureAllowedProperties(root, new HashSet<string> { "id", "status", "requiresAction", "reason" }, rawJson);

        var id = ReadRequiredString(root, "id", rawJson);
        var status = ReadRequiredString(root, "status", rawJson);
        var requiresAction = ReadRequiredBoolean(root, "requiresAction", rawJson);
        var reason = ReadOptionalString(root, "reason", rawJson);

        if (!AllowedOrderStatuses.Contains(status))
        {
            throw BuildValidationException($"OrderResult.status '{status}' is not allowed.", rawJson);
        }

        return new OrderResult(id, status, requiresAction, reason);
    }

    public RefundResult ValidateRefundResult(string rawJson)
    {
        var root = ParseRootObject(rawJson);
        EnsureAllowedProperties(root, new HashSet<string> { "status", "message", "orderId", "refundReason" }, rawJson);

        var status = ReadRequiredString(root, "status", rawJson);
        var message = ReadRequiredString(root, "message", rawJson);
        var orderId = ReadOptionalString(root, "orderId", rawJson);
        var refundReason = ReadOptionalString(root, "refundReason", rawJson);

        if (status is not ("accepted" or "needsMoreInfo" or "notAllowed" or "pending"))
        {
            throw BuildValidationException($"RefundResult.status '{status}' is not allowed.", rawJson);
        }

        return new RefundResult(status, message, orderId, refundReason);
    }

    public ClarifierResult ValidateClarifierResult(string rawJson)
    {
        var root = ParseRootObject(rawJson);
        EnsureAllowedProperties(root, new HashSet<string> { "question" }, rawJson);

        var question = ReadRequiredString(root, "question", rawJson);
        return new ClarifierResult(question);
    }

    private static JsonElement ParseRootObject(string rawJson)
    {
        try
        {
            using var document = JsonDocument.Parse(rawJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw BuildValidationException("JSON root must be an object.", rawJson);
            }

            return document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw BuildValidationException($"Invalid JSON: {exception.Message}", rawJson);
        }
    }

    private static void EnsureAllowedProperties(JsonElement root, IReadOnlySet<string> allowedProperties, string rawJson)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (!allowedProperties.Contains(property.Name))
            {
                throw BuildValidationException($"Unexpected property '{property.Name}'.", rawJson);
            }
        }
    }

    private static string ReadRequiredString(JsonElement root, string propertyName, string rawJson)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            throw BuildValidationException($"Required property '{propertyName}' is missing.", rawJson);
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            throw BuildValidationException($"Property '{propertyName}' must be a string.", rawJson);
        }

        var value = property.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw BuildValidationException($"Property '{propertyName}' must not be empty.", rawJson);
        }

        return value;
    }

    private static string? ReadOptionalString(JsonElement root, string propertyName, string rawJson)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            throw BuildValidationException($"Property '{propertyName}' must be a string when present.", rawJson);
        }

        var value = property.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw BuildValidationException($"Property '{propertyName}' must not be empty when present.", rawJson);
        }

        return value;
    }

    private static bool ReadRequiredBoolean(JsonElement root, string propertyName, string rawJson)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            throw BuildValidationException($"Required property '{propertyName}' is missing.", rawJson);
        }

        if (property.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            throw BuildValidationException($"Property '{propertyName}' must be a boolean.", rawJson);
        }

        return property.GetBoolean();
    }

    private static InvalidOperationException BuildValidationException(string message, string rawJson)
    {
        return new InvalidOperationException($"{message} Raw response: {Condense(rawJson)}");
    }

    private static string Condense(string rawJson)
    {
        var condensed = Regex.Replace(rawJson ?? string.Empty, @"\s+", " ").Trim();
        if (condensed.Length > 240)
        {
            condensed = condensed[..240] + "...";
        }

        return condensed;
    }
}
