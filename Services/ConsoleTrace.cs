using System;

namespace CasoDCodeConsumer.Services;

public static class ConsoleTrace
{
    public static void Config(string message) => Write("CONFIG", message);

    public static void Validation(string message) => Write("VALIDATION", message);

    public static void Reconcile(string message) => Write("RECONCILE", message);

    public static void Router(string message) => Write("ROUTER", message);

    public static void Agent(string message) => Write("AGENT", message);

    public static void Final(string message) => Write("FINAL", message);

    private static void Write(string area, string message)
    {
        Console.WriteLine($"[{area}] {message}");
    }
}
