namespace CasoDCodeConsumer.Models;

public sealed record RouteDecision(
    RouteKind RouteKind,
    string? OrderId,
    string? RefundReason,
    string Reason);
