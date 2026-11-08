using System.Reflection;
using NetArchTest.Rules;

namespace Bepthaymao.Architecture.Tests;

public sealed class FrameworkConventionTests
{
    [Fact]
    public void EndpointsDoNotDependOnInfrastructureOrEfCore()
    {
        NetArchTest.Rules.TestResult result = Types.InAssembly(Assembly.Load("Bepthaymao.Api"))
            .That().ResideInNamespaceStartingWith("Bepthaymao.Api.Endpoints")
            .ShouldNot().HaveDependencyOnAny("Bepthaymao.Infrastructure", "Microsoft.EntityFrameworkCore")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void NoTypeDerivesFromControllerBase()
    {
        Assert.DoesNotContain(Assembly.Load("Bepthaymao.Api").GetTypes(), type =>
        {
            for (Type? parent = type.BaseType; parent is not null; parent = parent.BaseType)
            {
                if (parent.FullName == "Microsoft.AspNetCore.Mvc.ControllerBase")
                {
                    return true;
                }
            }

            return false;
        });
    }
}
