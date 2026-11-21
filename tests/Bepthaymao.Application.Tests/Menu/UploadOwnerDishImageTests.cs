using Bepthaymao.Application.Abstractions.Errors;
using Bepthaymao.Application.Abstractions.Security;
using Bepthaymao.Application.Auth;
using Bepthaymao.Application.Menu;

namespace Bepthaymao.Application.Tests.Menu;

public sealed class UploadOwnerDishImageTests
{
    [Fact]
    public async Task RejectsOversizeBeforeCallingProcessor()
    {
        FakeProcessor processor = new();
        UploadOwnerDishImageCommandHandler handler = new(new FakeUser(Guid.NewGuid()), processor, new FakeStorage());

        PayloadTooLargeException error = await Assert.ThrowsAsync<PayloadTooLargeException>(() =>
            handler.Handle(new(new MemoryStream(), DishImageRules.MaxBytes + 1), TestContext.Current.CancellationToken));

        Assert.Contains("2 MB", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, processor.Calls);
    }

    [Fact]
    public async Task AcceptsExactlyMaxBytes()
    {
        FakeProcessor processor = new();
        UploadOwnerDishImageCommandHandler handler = new(new FakeUser(Guid.NewGuid()), processor, new FakeStorage());

        UploadedImageDto dto = await handler.Handle(new(new MemoryStream(), DishImageRules.MaxBytes), TestContext.Current.CancellationToken);

        Assert.Equal("/media/mon/fake.webp", dto.ImageUrl);
        Assert.Equal(1, processor.Calls);
    }

    [Fact]
    public async Task RequiresOwnerId()
    {
        FakeProcessor processor = new();
        UploadOwnerDishImageCommandHandler handler = new(new FakeUser(null), processor, new FakeStorage());

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new(new MemoryStream(), 10), TestContext.Current.CancellationToken));

        Assert.Equal(0, processor.Calls);
    }

    private sealed class FakeUser(Guid? ownerId) : ICurrentUser
    {
        public Guid? UserId => ownerId;
        public UserRole? Role => null;
        public Guid? OwnerId => ownerId;
        public Guid? BranchId => null;
    }

    private sealed class FakeProcessor : IDishImageProcessor
    {
        public int Calls { get; private set; }

        public byte[] ToWebp(Stream content)
        {
            Calls++;
            return [1, 2, 3];
        }
    }

    private sealed class FakeStorage : IDishImageStorage
    {
        public Task<string> SaveWebpAsync(byte[] webp, CancellationToken cancellationToken) => Task.FromResult("/media/mon/fake.webp");
    }
}
