using Bepthaymao.Application.Abstractions.Behaviors;
using AppValidationException = Bepthaymao.Application.Abstractions.Errors.ValidationException;
using Bepthaymao.Application.Abstractions.Messaging;
using Bepthaymao.Application.Abstractions.Persistence;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bepthaymao.Application.Tests.Abstractions;

public sealed class DecoratorPipelineTests
{
    [Fact]
    public async Task CommandPipelineRunsLoggingValidationUnitOfWorkAndHandler()
    {
        List<string> events = [];
        RecordingUnitOfWork unitOfWork = new(events);
        RecordingCommandHandler handler = new(events);
        ICommandHandler<TestCommand, string> pipeline = new LoggingCommandHandlerDecorator<TestCommand, string>(
            new ValidationCommandHandlerDecorator<TestCommand, string>(
                new UnitOfWorkCommandHandlerDecorator<TestCommand, string>(handler, unitOfWork),
                []),
            NullLogger<LoggingCommandHandlerDecorator<TestCommand, string>>.Instance);

        string result = await pipeline.Handle(new TestCommand(), TestContext.Current.CancellationToken);

        Assert.Equal("handled", result);
        Assert.Equal(["handler", "unit-of-work"], events);
    }

    [Fact]
    public async Task InvalidCommandThrowsCamelCaseValidationExceptionWithoutCallingHandler()
    {
        List<string> events = [];
        RecordingUnitOfWork unitOfWork = new(events);
        RecordingCommandHandler handler = new(events);
        ValidationCommandHandlerDecorator<TestCommand, string> validation =
            new(handler, [new TestCommandValidator()]);

        AppValidationException exception = await Assert.ThrowsAsync<AppValidationException>(() =>
            validation.Handle(new TestCommand { Items = [new TestItem()] }, TestContext.Current.CancellationToken));

        Assert.Contains("items[0].openTime", exception.Errors.Keys);
        Assert.Empty(events);
    }

    [Fact]
    public async Task HandlerFailureDoesNotSaveChangesAndEscapesUnchanged()
    {
        InvalidOperationException expected = new("handler failure");
        ThrowingCommandHandler handler = new(expected);
        RecordingUnitOfWork unitOfWork = new([]);
        UnitOfWorkCommandHandlerDecorator<TestCommand, string> unitOfWorkDecorator =
            new(handler, unitOfWork);

        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            unitOfWorkDecorator.Handle(new TestCommand(), TestContext.Current.CancellationToken));

        Assert.Same(expected, actual);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task QueryPipelineDoesNotUseUnitOfWorkAndDoesNotLogPayload()
    {
        RecordingLogger<LoggingQueryHandlerDecorator<TestQuery, string>> logger = new();
        RecordingQueryHandler handler = new();
        IQueryHandler<TestQuery, string> pipeline = new LoggingQueryHandlerDecorator<TestQuery, string>(
            new ValidationQueryHandlerDecorator<TestQuery, string>(handler, []), logger);

        string result = await pipeline.Handle(new TestQuery { SecretPayload = "do-not-log" },
            TestContext.Current.CancellationToken);

        Assert.Equal("query-result", result);
        Assert.Contains(logger.Messages, message => message.Contains(nameof(TestQuery), StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message => message.Contains("do-not-log", StringComparison.Ordinal));
        Assert.Equal(1, handler.CallCount);
    }

    private sealed class TestCommand : ICommand<string>
    {
        public string SecretPayload { get; init; } = "secret-command-payload";

        public List<TestItem> Items { get; init; } = [new() { OpenTime = "06:00" }];
    }

    private sealed class TestItem
    {
        public string OpenTime { get; init; } = "";
    }

    private sealed class TestQuery : IQuery<string>
    {
        public string SecretPayload { get; init; } = "secret-query-payload";
    }

    private sealed class RecordingCommandHandler(List<string> events) : ICommandHandler<TestCommand, string>
    {
        public Task<string> Handle(TestCommand command, CancellationToken cancellationToken)
        {
            events.Add("handler");
            return Task.FromResult("handled");
        }
    }

    private sealed class ThrowingCommandHandler(InvalidOperationException exception)
        : ICommandHandler<TestCommand, string>
    {
        public Task<string> Handle(TestCommand command, CancellationToken cancellationToken) => Task.FromException<string>(exception);
    }

    private sealed class RecordingQueryHandler : IQueryHandler<TestQuery, string>
    {
        public int CallCount { get; private set; }

        public Task<string> Handle(TestQuery query, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult("query-result");
        }
    }

    private sealed class RecordingUnitOfWork(List<string> events) : IUnitOfWork
    {
        public int SaveCount { get; private set; }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveCount++;
            events.Add("unit-of-work");
            return Task.CompletedTask;
        }
    }

    private sealed class TestCommandValidator : AbstractValidator<TestCommand>
    {
        public TestCommandValidator()
        {
            RuleFor(command => command.Items)
                .Must(_ => false)
                .OverridePropertyName("Items[0].OpenTime")
                .WithMessage("Giờ mở cửa là bắt buộc.");
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }
}
