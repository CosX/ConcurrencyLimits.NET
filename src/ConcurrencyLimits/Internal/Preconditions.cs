namespace ConcurrencyLimits.Internal;

public static class Preconditions
{
    public static void CheckArgument(bool expression, object errorMessage)
    {
        if (!expression)
        {
            throw new ArgumentException(Convert.ToString(errorMessage));
        }
    }

    public static void CheckState(bool expression, object errorMessage)
    {
        if (!expression)
        {
            throw new InvalidOperationException(Convert.ToString(errorMessage));
        }
    }
}
