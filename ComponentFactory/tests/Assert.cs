namespace FactoryChecks;

internal static class Assert
{
    public static int PassedCount { get; private set; }

    public static void True(bool condition, string description)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"FAIL: {description}");
        }

        PassedCount++;
        Console.WriteLine($"PASS: {description}");
    }

    public static async Task<TException> ThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
