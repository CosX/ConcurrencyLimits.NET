namespace ConcurrencyLimits.Executors;

public class UncheckedTimeoutException : Exception
{
    public UncheckedTimeoutException() { }

    public UncheckedTimeoutException(string? message) : base(message) { }

    public UncheckedTimeoutException(Exception? cause) : base(null, cause) { }

    public UncheckedTimeoutException(string? message, Exception? cause) : base(message, cause) { }
}
