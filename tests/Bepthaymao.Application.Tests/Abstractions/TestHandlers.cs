using Bepthaymao.Application.Abstractions.Messaging;
using Bepthaymao.Application.Abstractions.Persistence;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Bepthaymao.Application.Tests.Abstractions;

public sealed class TestState
{
    public List<string> Events { get; } = [];
    public List<string> Logs { get; } = [];
    public InvalidOperationException? Failure { get; set; }
}

public sealed record TestCommand(string OpenTime, string Name) : ICommand<string>;
public sealed record TestQuery(string Value) : IQuery<string>;

public sealed class TestCommandHandler(TestState state) : ICommandHandler<TestCommand, string>
{
    public Task<string> Handle(TestCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        state.Events.Add("handler");
        if (state.Failure is not null)
        {
            throw state.Failure;
        }

        return Task.FromResult("done");
    }
}

public sealed class TestQueryHandler(TestState state) : IQueryHandler<TestQuery, string>
{
    public Task<string> Handle(TestQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        state.Events.Add("query");
        return Task.FromResult("read");
    }
}

public sealed class TestCommandValidator : AbstractValidator<TestCommand>
{
    public TestCommandValidator(TestState state)
    {
        RuleFor(command => command).Custom((command, context) =>
        {
            state.Events.Add("validation");
            if (string.IsNullOrWhiteSpace(command.OpenTime))
            {
                context.AddFailure("Items[0].OpenTime", "Required");
            }
        });
    }
}

public sealed class TestQueryValidator : AbstractValidator<TestQuery>
{
    public TestQueryValidator(TestState state)
    {
        RuleFor(query => query).Custom((query, context) =>
        {
            state.Events.Add("validation");
            if (string.IsNullOrEmpty(query.Value))
            {
                context.AddFailure("Value", "Required");
            }
        });
    }
}

public sealed class TestUnitOfWork(TestState state) : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        state.Events.Add("save");
        return Task.CompletedTask;
    }
}

public sealed class TestLogger<T>(TestState state) : ILogger<T>
{
    public IDisposable? BeginScope<TScope>(TScope scope) where TScope : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState logState,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        string message = formatter(logState, exception);
        state.Logs.Add(message);
        state.Events.Add(message.StartsWith("Handling ", StringComparison.Ordinal) ? "logging" : "logged");
    }
}
