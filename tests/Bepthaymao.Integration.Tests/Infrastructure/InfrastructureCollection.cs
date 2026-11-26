namespace Bepthaymao.Integration.Tests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class SharedInfrastructure : ICollectionFixture<InfrastructureFixture>
{
    public const string Name = "Infrastructure";
}
