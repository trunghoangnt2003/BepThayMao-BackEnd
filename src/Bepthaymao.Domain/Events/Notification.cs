namespace Bepthaymao.Domain.Events;

public enum NotificationType
{
    OrderNew,
    OrderCancelled,
    AbandonedAlert,
    TelegramMisconfigured
}

public enum NotificationChannel
{
    InApp,
    Telegram
}

public enum NotificationStatus
{
    Pending,
    Sent,
    Failed,
    Skipped
}

/// <summary>Thông báo vận hành in-app hoặc Telegram; không lưu tên, SĐT, địa chỉ khách hay chat_id.</summary>
public sealed class Notification
{
    public Guid Id { get; set; }
    public NotificationType Type { get; set; }
    public NotificationChannel Channel { get; set; }
    public NotificationStatus Status { get; set; }
    public int Attempts { get; set; }
    public Guid BranchId { get; set; }
    public Guid? OrderId { get; set; }
    public string? OrderCode { get; set; }
    public int? WaitedMinutes { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
}
