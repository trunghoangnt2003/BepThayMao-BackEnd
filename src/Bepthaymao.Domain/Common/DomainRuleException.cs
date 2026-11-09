namespace Bepthaymao.Domain.Common;

public sealed class DomainRuleException(string rule, string message) : Exception(message)
{
    public string Rule { get; } = rule;
}
