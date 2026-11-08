using ValidationException = Bepthaymao.Application.Abstractions.Errors.ValidationException;
using Bepthaymao.Application.Abstractions.Messaging;
using Bepthaymao.Application.Abstractions.Persistence;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bepthaymao.Application.Tests.Abstractions;

public sealed class DecoratorPipelineTests
{
    [Fact]
    public async Task CommandValidatesExecutesAndSavesExactlyOnceWithoutLoggingPayload()
    {
        using ServiceProvider provider = CreateProvider();
        using IServiceScope scope = provider.CreateScope();
        TestState state = provider.GetRequiredService<TestState>();
        ICommandHandler<TestCommand, string> handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<TestCommand, string>>();
        Assert.Contains("Logging", handler.GetType().Name, StringComparison.Ordinal);

        string result = await handler.Handle(new TestCommand("06:00", "private-payload-value"), TestContext.Current.CancellationToken);

        Assert.Equal("done", result);
        Assert.Equal(["logging", "validation", "handler", "save", "logged"], state.Events);
        Assert.DoesNotContain(state.Logs, line => line.Contains("private-payload-value", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InvalidCommandStopsBeforeHandlerAndSaveWithCamelCaseErrors()
    {
        using ServiceProvider provider = CreateProvider();
        using IServiceScope scope = provider.CreateScope();
        TestState state = provider.GetRequiredService<TestState>();
        ICommandHandler<TestCommand, string> handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<TestCommand, string>>();

        ValidationException exception = await Assert.ThrowsAsync<ValidationException>(() =>
            handler.Handle(new TestCommand("", "private-payload-value"), TestContext.Current.CancellationToken));

        Assert.Equal(["Required"], exception.Errors["items[0].openTime"]);
        Assert.Equal(["logging", "validation"], state.Events);
    }

    [Fact]
    public async Task HandlerFailureDoesNotSaveAndPreservesException()
    {
        using ServiceProvider provider = CreateProvider();
        using IServiceScope scope = provider.CreateScope();
        TestState state = provider.GetRequiredService<TestState>();
        state.Failure = new InvalidOperationException("expected failure");
        ICommandHandler<TestCommand, string> handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<TestCommand, string>>();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new TestCommand("06:00", "private-payload-value"), TestContext.Current.CancellationToken));

        Assert.Same(state.Failure, exception);
        Assert.Equal(["logging", "validation", "handler"], state.Events);
    }

    [Fact]
    public async Task QueryValidatesAndExecutesWithoutUnitOfWork()
    {
        using ServiceProvider provider = CreateProvider();
        using IServiceScope scope = provider.CreateScope();
        TestState state = provider.GetRequiredService<TestState>();
        IQueryHandler<TestQuery, string> handler = scope.ServiceProvider.GetRequiredService<IQueryHandler<TestQuery, string>>();

        Assert.Equal("read", await handler.Handle(new TestQuery("valid"), TestContext.Current.CancellationToken));
        Assert.Equal(["logging", "validation", "query", "logged"], state.Events);
    }

    [Fact]
    public async Task InvalidQueryStopsBeforeHandler()
    {
        using ServiceProvider provider = CreateProvider();
        using IServiceScope scope = provider.CreateScope();
        IQueryHandler<TestQuery, string> handler = scope.ServiceProvider.GetRequiredService<IQueryHandler<TestQuery, string>>();
        ValidationException exception = await Assert.ThrowsAsync<ValidationException>(() =>
            handler.Handle(new TestQuery(""), TestContext.Current.CancellationToken));
        Assert.Contains("value", exception.Errors.Keys);
        Assert.Equal(["logging", "validation"], provider.GetRequiredService<TestState>().Events);
    }

    private static ServiceProvider CreateProvider()
    {
        ServiceCollection services = new();
        services.AddSingleton<TestState>();
        services.AddSingleton(typeof(ILogger<>), typeof(TestLogger<>));
        services.AddScoped<IUnitOfWork, TestUnitOfWork>();
        services.AddScoped<IValidator<TestCommand>, TestCommandValidator>();
        services.AddScoped<IValidator<TestQuery>, TestQueryValidator>();
        services.AddCqrsHandlers(typeof(DecoratorPipelineTests).Assembly);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}
