namespace CasoDCodeConsumer.Models;

public sealed record RefundResult(
    string Status,
    string Message,
    string? OrderId,
    string? RefundReason);
