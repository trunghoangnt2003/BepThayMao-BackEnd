using System.Reflection;

namespace Bepthaymao.Application.Tests.Abstractions;

public sealed class CqrsRegistrationTests
{
    [Fact]
    public void ApplicationOwnsTheCqrsContractsAndRegistration()
    {
        Assembly application = Assembly.Load("Bepthaymao.Application");

        Assert.NotNull(application.GetType("Bepthaymao.Application.Abstractions.Messaging.ICommand`1"));
        Assert.NotNull(application.GetType("Bepthaymao.Application.Abstractions.Messaging.IQuery`1"));
        Assert.NotNull(application.GetType("Bepthaymao.Application.Abstractions.Messaging.ICommandHandler`2"));
        Assert.NotNull(application.GetType("Bepthaymao.Application.Abstractions.Messaging.IQueryHandler`2"));
        Assert.NotNull(application.GetType("Bepthaymao.Application.Abstractions.Messaging.CqrsRegistration"));
    }
}
