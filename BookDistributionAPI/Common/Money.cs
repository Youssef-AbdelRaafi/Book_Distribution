namespace BookDistributionAPI.Common;

public static class Money
{
    public const int DecimalPlaces = 3;

    public static bool HasSupportedPrecision(decimal amount)
    {
        return decimal.Round(amount, DecimalPlaces, MidpointRounding.AwayFromZero) == amount;
    }

    public static void RequireSupportedPrecision(decimal amount, string fieldName)
    {
        if (!HasSupportedPrecision(amount))
        {
            throw new InvalidOperationException(
                $"{fieldName} must not have more than {DecimalPlaces} decimal places.");
        }
    }
}
