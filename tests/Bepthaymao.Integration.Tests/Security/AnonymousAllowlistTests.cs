using Bepthaymao.Integration.Tests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Bepthaymao.Integration.Tests.Security;

public sealed class AnonymousAllowlistTests
{
    private static readonly string[] _expected = ["login", "refreshToken", "logout", "listPublicBranches", "/health/live", "/health/ready", "/openapi/{documentName}.json"];
    [Fact]
    public async Task AnonymousEndpointsAreExactlyTheReviewedAllowlist()
    {
        await using ApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        EndpointDataSource source = factory.Services.GetRequiredService<EndpointDataSource>();
        string[] anonymous = source.Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(endpoint => endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName ?? endpoint.RoutePattern.RawText!)
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(_expected.Order(StringComparer.Ordinal), anonymous);
    }
}
