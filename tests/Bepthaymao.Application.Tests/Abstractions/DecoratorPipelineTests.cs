using System.Reflection;

namespace Bepthaymao.Application.Tests.Abstractions;

public sealed class DecoratorPipelineTests
{
    [Theory]
    [InlineData("LoggingCommandHandlerDecorator`2")]
    [InlineData("ValidationCommandHandlerDecorator`2")]
    [InlineData("UnitOfWorkCommandHandlerDecorator`2")]
    [InlineData("LoggingQueryHandlerDecorator`2")]
    [InlineData("ValidationQueryHandlerDecorator`2")]
    public void ApplicationContainsEachCqrsPipelineDecorator(string typeName)
    {
        Type? decorator = Assembly.Load("Bepthaymao.Application")
            .GetType($"Bepthaymao.Application.Abstractions.Behaviors.{typeName}");

        Assert.NotNull(decorator);
    }
}
