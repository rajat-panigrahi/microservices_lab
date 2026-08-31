namespace StrategyOps.BuildingBlocks.Domain;

/// <summary>
/// Small guard helpers so aggregates read as rules rather than as if-throw noise.
/// Every failure carries a stable code (see <see cref="DomainException"/>).
/// </summary>
public static class Guard
{
    public static string AgainstBlank(string? value, string code, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException(code, message);
        }

        return value.Trim();
    }

    public static decimal AgainstNonPositive(decimal value, string code, string message)
    {
        if (value <= 0)
        {
            throw new DomainException(code, message);
        }

        return value;
    }

    public static Guid AgainstEmpty(Guid value, string code, string message)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException(code, message);
        }

        return value;
    }

    public static void Against(bool condition, string code, string message)
    {
        if (condition)
        {
            throw new DomainException(code, message);
        }
    }
}
