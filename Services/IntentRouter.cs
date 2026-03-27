using System;
using System.Linq;
using System.Text.RegularExpressions;
using CasoDCodeConsumer.Models;

namespace CasoDCodeConsumer.Services;

public sealed class IntentRouter
{
    private static readonly string[] DestructiveVerbs = ["delete", "remove", "purge", "erase"];
    private static readonly string[] RefundKeywords = ["refund", "reimbursement", "money back"];
    private static readonly string[] OrderKeywords =
    [
        "where is",
        "status",
        "detail",
        "details",
        "shipment",
        "shipping",
        "shipped",
        "delivery",
        "delivered",
        "track",
        "tracking",
        "order"
    ];

    private static readonly string[] RefundReasonHints =
    [
        "damaged",
        "broken",
        "wrong item",
        "defective",
        "late",
        "cancel",
        "didn't arrive",
        "did not arrive",
        "missing"
    ];

    private readonly OrderIdExtractor _orderIdExtractor;

    public IntentRouter(OrderIdExtractor orderIdExtractor)
    {
        _orderIdExtractor = orderIdExtractor;
    }

    public RouteDecision Route(string prompt)
    {
        var normalizedPrompt = Normalize(prompt);
        var orderId = _orderIdExtractor.Extract(prompt);
        var refundReason = ExtractRefundReason(prompt, normalizedPrompt);

        if (IsReject(normalizedPrompt, orderId))
        {
            return new RouteDecision(
                RouteKind.Reject,
                orderId,
                refundReason,
                "Destructive or unsupported order operation detected.");
        }

        var isRefundIntent = IsRefundIntent(normalizedPrompt);
        if (isRefundIntent)
        {
            if (!string.IsNullOrWhiteSpace(orderId))
            {
                return new RouteDecision(
                    RouteKind.Refund,
                    orderId,
                    refundReason,
                    "Refund intent detected with an order reference.");
            }

            return new RouteDecision(
                RouteKind.Clarify,
                null,
                refundReason,
                "Refund intent detected but the order ID is missing.");
        }

        var isOrderIntent = IsOrderIntent(normalizedPrompt);
        if (isOrderIntent && !string.IsNullOrWhiteSpace(orderId))
        {
            return new RouteDecision(
                RouteKind.Order,
                orderId,
                null,
                "Order status intent detected with an order reference.");
        }

        var isInDomain = normalizedPrompt.Contains("order", StringComparison.Ordinal) ||
                         !string.IsNullOrWhiteSpace(orderId) ||
                         normalizedPrompt.Contains("refund", StringComparison.Ordinal) ||
                         normalizedPrompt.Contains("return", StringComparison.Ordinal);

        if (isInDomain)
        {
            return new RouteDecision(
                RouteKind.Clarify,
                orderId,
                refundReason,
                string.IsNullOrWhiteSpace(orderId)
                    ? "In-domain request detected but the order ID is missing or the intent is ambiguous."
                    : "In-domain request detected but the intent is ambiguous.");
        }

        return new RouteDecision(
            RouteKind.Reject,
            orderId,
            refundReason,
            "Prompt is outside order and refund support.");
    }

    public string BuildClarificationSummary(RouteDecision decision, string originalPrompt)
    {
        return decision.RouteKind switch
        {
            RouteKind.Clarify when decision.Reason.Contains("Refund intent", StringComparison.OrdinalIgnoreCase) =>
                $"The user wants help with a refund but did not provide a usable order ID. Original prompt: {originalPrompt}",
            RouteKind.Clarify when !string.IsNullOrWhiteSpace(decision.OrderId) =>
                $"The user mentioned order {decision.OrderId} but the exact need is ambiguous. Original prompt: {originalPrompt}",
            _ =>
                $"The user needs help with an order or refund but key information is missing. Original prompt: {originalPrompt}"
        };
    }

    private static string Normalize(string prompt)
    {
        return Regex.Replace(prompt ?? string.Empty, @"\s+", " ").Trim().ToLowerInvariant();
    }

    private static bool IsReject(string normalizedPrompt, string? orderId)
    {
        var hasDestructiveVerb = DestructiveVerbs.Any(verb => Regex.IsMatch(normalizedPrompt, $@"\b{Regex.Escape(verb)}\b"));
        if (!hasDestructiveVerb)
        {
            return false;
        }

        var isMassTarget =
            normalizedPrompt.Contains("all orders", StringComparison.Ordinal) ||
            normalizedPrompt.Contains("every order", StringComparison.Ordinal) ||
            normalizedPrompt.Contains("every orders", StringComparison.Ordinal) ||
            Regex.IsMatch(normalizedPrompt, @"\borders\b");

        return isMassTarget || !string.IsNullOrWhiteSpace(orderId);
    }

    private static bool IsRefundIntent(string normalizedPrompt)
    {
        if (RefundKeywords.Any(keyword => normalizedPrompt.Contains(keyword, StringComparison.Ordinal)))
        {
            return true;
        }

        if (!Regex.IsMatch(normalizedPrompt, @"\breturn\b"))
        {
            return false;
        }

        if (normalizedPrompt.Contains("return the order status", StringComparison.Ordinal) ||
            normalizedPrompt.Contains("return order status", StringComparison.Ordinal))
        {
            return false;
        }

        return normalizedPrompt.Contains("because", StringComparison.Ordinal) ||
               normalizedPrompt.Contains("since", StringComparison.Ordinal) ||
               normalizedPrompt.Contains("due to", StringComparison.Ordinal) ||
               RefundReasonHints.Any(hint => normalizedPrompt.Contains(hint, StringComparison.Ordinal));
    }

    private static bool IsOrderIntent(string normalizedPrompt)
    {
        if (normalizedPrompt.Contains("return the order status", StringComparison.Ordinal) ||
            normalizedPrompt.Contains("return order status", StringComparison.Ordinal))
        {
            return true;
        }

        return OrderKeywords.Any(keyword => normalizedPrompt.Contains(keyword, StringComparison.Ordinal));
    }

    private static string? ExtractRefundReason(string originalPrompt, string normalizedPrompt)
    {
        foreach (var pattern in new[]
                 {
                     @"\bbecause\s+(?<reason>.+?)(?:[.?!]|$)",
                     @"\bsince\s+(?<reason>.+?)(?:[.?!]|$)",
                     @"\bdue to\s+(?<reason>.+?)(?:[.?!]|$)"
                 })
        {
            var match = Regex.Match(originalPrompt, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (match.Success)
            {
                return CleanReason(match.Groups["reason"].Value);
            }
        }

        if (Regex.IsMatch(normalizedPrompt, @"\b(refund|return)\b"))
        {
            var match = Regex.Match(originalPrompt, @"\bfor\s+(?<reason>.+?)(?:[.?!]|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (match.Success)
            {
                var candidate = CleanReason(match.Groups["reason"].Value);
                if (!string.IsNullOrWhiteSpace(candidate) &&
                    !candidate.StartsWith("order ", StringComparison.OrdinalIgnoreCase) &&
                    !candidate.StartsWith("ORD", StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static string? CleanReason(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = Regex.Replace(value, @"\s+", " ").Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }
}
