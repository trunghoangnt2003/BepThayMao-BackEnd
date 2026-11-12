using System.Globalization;
using System.Text.RegularExpressions;
using FluentValidation;

namespace Bepthaymao.Application.Branches;

/// <summary>Năm trường mà cả Chủ sở hữu lẫn Quản lý cùng được sửa trên một cơ sở.</summary>
public interface IBranchSettingsFields
{
    string Phone { get; }
    string OpenTime { get; }
    string CloseTime { get; }
    int AbandonedThresholdMinutes { get; }
    IReadOnlyList<string> ZaloRecipientIds { get; }
}

/// <summary>Thân <c>BranchSettings</c> của hợp đồng: đúng năm trường Quản lý được sửa.</summary>
public sealed record BranchSettingsData(string Phone, string OpenTime, string CloseTime, int AbandonedThresholdMinutes,
    IReadOnlyList<string> ZaloRecipientIds) : IBranchSettingsFields;

internal static partial class BranchSettingsPatterns
{
    public static string StripPhone(string phone) => string.Concat(phone.Where(character => !char.IsWhiteSpace(character) && character != '.'));

    public static bool IsTime(string? value) => TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    public static TimeOnly ParseTime(string value) => TimeOnly.ParseExact(value, "HH:mm", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^(\+84|0)[0-9]{8,10}$", RegexOptions.CultureInvariant)]
    public static partial Regex PhonePattern();
}

/// <summary>Luật dùng chung cho năm trường thông số; tạo cơ sở, Owner sửa cơ sở và Quản lý sửa thông số đều dùng lại.</summary>
internal sealed class BranchSettingsRules<T> : AbstractValidator<T> where T : IBranchSettingsFields
{
    public BranchSettingsRules()
    {
        RuleFor(settings => settings.Phone).Must(phone => phone is not null && BranchSettingsPatterns.PhonePattern().IsMatch(BranchSettingsPatterns.StripPhone(phone)))
            .WithMessage("Nhập số điện thoại hợp lệ, ví dụ 0901 234 567.");
        RuleFor(settings => settings.OpenTime).Must(BranchSettingsPatterns.IsTime).WithMessage("Nhập giờ mở theo dạng HH:mm.");
        RuleFor(settings => settings.CloseTime).Must(BranchSettingsPatterns.IsTime).WithMessage("Nhập giờ đóng theo dạng HH:mm.");
        RuleFor(settings => settings).Custom((settings, context) =>
        {
            if (BranchSettingsPatterns.IsTime(settings.OpenTime) && BranchSettingsPatterns.IsTime(settings.CloseTime)
                && string.CompareOrdinal(settings.OpenTime, settings.CloseTime) >= 0)
                context.AddFailure("OpenTime", "Giờ đóng phải sau giờ mở trong cùng một ngày.");
        });
        RuleFor(settings => settings.AbandonedThresholdMinutes).InclusiveBetween(1, 1440)
            .WithMessage("Nhập số phút là số nguyên từ 1 đến 1440.");
        RuleFor(settings => settings.ZaloRecipientIds).Must(ids => ids is not null && ids.Count <= 20
                && ids.All(id => id is not null && id.Trim().Length is >= 1 and <= 64)
                && ids.Select(id => id.Trim()).Distinct(StringComparer.Ordinal).Count() == ids.Count)
            .WithMessage("Nhập tối đa 20 ID Zalo không trùng, mỗi ID từ 1 đến 64 ký tự.");
    }
}
