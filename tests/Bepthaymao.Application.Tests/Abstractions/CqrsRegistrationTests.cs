using Bepthaymao.Application.Abstractions.Messaging;
using Bepthaymao.Application.Abstractions.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bepthaymao.Application.Tests.Abstractions;

public sealed class CqrsRegistrationTests
{
    [Fact]
    public async Task AddCqrsHandlersResolvesTheLoggingDecoratorAroundACommandHandler()
    {
        RegistrationHandler.Events.Clear();
        ServiceCollection services = new();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddScoped<IUnitOfWork, RegistrationUnitOfWork>();
        services.AddCqrsHandlers(typeof(CqrsRegistrationTests).Assembly);

        await using ServiceProvider provider = services.BuildServiceProvider();
        ICommandHandler<RegistrationCommand, string> handler =
            provider.GetRequiredService<ICommandHandler<RegistrationCommand, string>>();

        string result = await handler.Handle(new RegistrationCommand(), TestContext.Current.CancellationToken);

        Assert.Equal("registered", result);
        Assert.Equal(["handler", "unit-of-work"], RegistrationHandler.Events);
        Assert.Contains("LoggingCommandHandlerDecorator", handler.GetType().FullName, StringComparison.Ordinal);
    }

    public sealed class RegistrationCommand : ICommand<string>
    {
    }

    public sealed class RegistrationHandler : ICommandHandler<RegistrationCommand, string>
    {
        public static List<string> Events { get; } = [];

        public Task<string> Handle(RegistrationCommand command, CancellationToken cancellationToken)
        {
            Events.Add("handler");
            return Task.FromResult("registered");
        }
    }

    private sealed class RegistrationUnitOfWork : IUnitOfWork
    {
        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            RegistrationHandler.Events.Add("unit-of-work");
            return Task.CompletedTask;
        }
    }
}
