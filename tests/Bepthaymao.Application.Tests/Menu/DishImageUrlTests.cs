using Bepthaymao.Application.Menu;
using Bepthaymao.Domain.Menu;

namespace Bepthaymao.Application.Tests.Menu;

public sealed class DishImageUrlTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://cdn.example.com/xoi.jpg")]
    [InlineData("http://localhost:4200/images/mon/xoi-ga.jpg")]
    [InlineData("/images/mon/xoi-ga.jpg")]
    public async Task ValidatorAcceptsHttpUrlsAndSitePaths(string? imageUrl)
    {
        var result = await new DishInputValidator().ValidateAsync(Input(imageUrl), TestContext.Current.CancellationToken);
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("images/mon/xoi-ga.jpg")]
    [InlineData("//evil.example.com/xoi.jpg")]
    [InlineData(@"/images\mon\xoi.jpg")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://example.com/xoi.jpg")]
    [InlineData("/images/mon/xôi gà.jpg")]
    public async Task ValidatorRejectsOtherSchemesProtocolRelativeAndMalformedPaths(string imageUrl)
    {
        var result = await new DishInputValidator().ValidateAsync(Input(imageUrl), TestContext.Current.CancellationToken);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(DishInputData.ImageUrl));
    }

    private static DishInputData Input(string? imageUrl) =>
        new("Xôi gà xé", null, 40000, imageUrl, DishCategory.XoiMan, false, 0);
}
