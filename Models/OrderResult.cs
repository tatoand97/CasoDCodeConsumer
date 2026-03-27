namespace CasoDCodeConsumer.Models;

public sealed record OrderResult(
    string Id,
    string Status,
    bool RequiresAction,
    string? Reason);
