using System.Reflection;
using NetArchTest.Rules;

namespace Bepthaymao.Architecture.Tests;

public sealed class LayerDependencyTests
{
    [Theory]
    [InlineData("Bepthaymao.Domain", "Bepthaymao.Application")]
    [InlineData("Bepthaymao.Domain", "Bepthaymao.Infrastructure")]
    [InlineData("Bepthaymao.Domain", "Microsoft.EntityFrameworkCore")]
    [InlineData("Bepthaymao.Application", "Bepthaymao.Infrastructure")]
    [InlineData("Bepthaymao.Application", "Microsoft.AspNetCore")]
    [InlineData("Bepthaymao.Infrastructure", "Bepthaymao.Api")]
    [InlineData("Bepthaymao.Infrastructure", "Bepthaymao.Worker")]
    public void AssemblyDoesNotDependOnForbiddenLayer(string assemblyName, string forbiddenNamespace)
    {
        NetArchTest.Rules.TestResult result = Types.InAssembly(Assembly.Load(assemblyName))
            .ShouldNot().HaveDependencyOn(forbiddenNamespace).GetResult();

        Assert.True(result.IsSuccessful,
            $"{assemblyName} tham chiếu {forbiddenNamespace}: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Theory]
    [InlineData("Bepthaymao.Domain")]
    [InlineData("Bepthaymao.Application")]
    [InlineData("Bepthaymao.Infrastructure")]
    [InlineData("Bepthaymao.Api")]
    [InlineData("Bepthaymao.Worker")]
    public void AssemblyDoesNotReferenceMediatR(string assemblyName)
    {
        Assert.DoesNotContain(Assembly.Load(assemblyName).GetReferencedAssemblies(),
            reference => reference.Name?.StartsWith("MediatR", StringComparison.Ordinal) is true);
    }
}
