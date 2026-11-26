using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Bepthaymao.Domain.Branches;
using Bepthaymao.Domain.Orders;
using Bepthaymao.Infrastructure.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bepthaymao.Infrastructure.Notifications;

public sealed class TelegramOptions
{
    public string Mode { get; set; } = "Fake";
    public string? BotToken { get; set; }
    public string ApiBaseUrl { get; set; } = "https://api.telegram.org";
}

public sealed record TelegramSentMessage(long ChatId, string Text, DateTimeOffset SentAt);

public sealed class FakeTelegramRecorder
{
    private readonly ConcurrentQueue<TelegramSentMessage> _messages = new();
    public IReadOnlyList<TelegramSentMessage> Messages => [.. _messages];
    public void Record(long chatId, string text, DateTimeOffset at) => _messages.Enqueue(new(chatId, text, at));
    public void Clear() => _messages.Clear();
}

public sealed class TelegramPermanentException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed class TelegramTransientException(string message, Exception? inner = null)
    : TransientDeliveryException(message, inner);

public interface ITelegramSender
{
    Task SendHtmlAsync(long chatId, string htmlText, CancellationToken cancellationToken);
}

internal sealed partial class FakeTelegramSender(FakeTelegramRecorder recorder, TimeProvider clock, ILogger<FakeTelegramSender> logger) : ITelegramSender
{
    public Task SendHtmlAsync(long chatId, string htmlText, CancellationToken cancellationToken)
    {
        recorder.Record(chatId, htmlText, clock.GetUtcNow());
        LogFakeSent(logger, htmlText.Length);
        return Task.CompletedTask;
    }

    [LoggerMessage(LogLevel.Information, "Telegram Fake sent message ({Length} chars)")]
    private static partial void LogFakeSent(ILogger logger, int length);
}

internal sealed partial class LiveTelegramSender(
    IOptions<TelegramOptions> options,
    ILogger<LiveTelegramSender> logger) : ITelegramSender, IDisposable
{
    private readonly HttpClient _httpClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
    })
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    public void Dispose() => _httpClient.Dispose();

    public async Task SendHtmlAsync(long chatId, string htmlText, CancellationToken cancellationToken)
    {
        TelegramOptions config = options.Value;
        if (string.IsNullOrWhiteSpace(config.BotToken))
            throw new TelegramPermanentException(401, "Thiếu Telegram BotToken.");

        string baseUrl = config.ApiBaseUrl.TrimEnd('/');
        string endpoint = $"{baseUrl}/bot{config.BotToken}/sendMessage";
        object payload = new
        {
            chat_id = chatId,
            text = htmlText,
            parse_mode = "HTML",
            disable_web_page_preview = true
        };

        try
        {
            using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(endpoint, payload, cancellationToken);
            if (response.IsSuccessStatusCode) return;

            int code = (int)response.StatusCode;
            if (code is 400 or 401 or 403 or 404)
            {
                LogPermanentError(logger, code);
                throw new TelegramPermanentException(code, $"Telegram API lỗi cấu hình ({code}).");
            }
            LogTransientError(logger, code);
            throw new TelegramTransientException($"Telegram API tạm lỗi ({code}).");
        }
        catch (TelegramPermanentException) { throw; }
        catch (TelegramTransientException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Không log message gốc của HttpRequestException vì có thể chứa URL/BotToken.
            LogNetworkError(logger, ex.GetType().Name);
            throw new TelegramTransientException("Lỗi mạng khi gọi Telegram API.");
        }
    }

    [LoggerMessage(LogLevel.Warning, "Telegram API returned permanent error status {StatusCode}")]
    private static partial void LogPermanentError(ILogger logger, int statusCode);

    [LoggerMessage(LogLevel.Warning, "Telegram API returned transient error status {StatusCode}")]
    private static partial void LogTransientError(ILogger logger, int statusCode);

    [LoggerMessage(LogLevel.Warning, "Telegram API network error ({ErrorType})")]
    private static partial void LogNetworkError(ILogger logger, string errorType);
}

public static class TelegramMessageFormatter
{
    public static string FormatOrderPaid(Order order, Branch branch, bool restoredFromExpiry, bool latePayment)
    {
        StringBuilder sb = new();
        sb.Append("<b>ĐƠN MỚI ĐÃ THANH TOÁN — #").Append(WebUtility.HtmlEncode(order.Code)).Append("</b>\n");
        if (restoredFromExpiry || latePayment)
            sb.Append("<i>(Khôi phục / thanh toán muộn)</i>\n");
        if (order.IsNextDay)
            sb.Append("<i>(Giao ngày hôm sau)</i>\n");
        sb.Append("Cơ sở: ").Append(WebUtility.HtmlEncode(branch.Name)).Append('\n');
        sb.Append("Khách: ").Append(WebUtility.HtmlEncode(order.CustomerName))
            .Append(" — ").Append(WebUtility.HtmlEncode(order.CustomerPhone)).Append('\n');
        sb.Append("Giao tới: ").Append(WebUtility.HtmlEncode(order.DeliveryAddress)).Append('\n');
        if (!string.IsNullOrWhiteSpace(order.Note))
            sb.Append("Ghi chú: ").Append(WebUtility.HtmlEncode(order.Note)).Append('\n');
        sb.Append("Món:\n");
        foreach (OrderItem item in order.Items)
        {
            sb.Append("• ").Append(WebUtility.HtmlEncode(item.DishName))
                .Append(" x").Append(item.Quantity)
                .Append(" (").Append(FormatVnd(item.LineTotal)).Append(")\n");
        }
        sb.Append("<b>Tổng tiền món: ").Append(FormatVnd(order.ItemsTotal)).Append("</b>");
        return sb.ToString();
    }

    public static string FormatOrderCancelled(Order order, Branch branch, string fromStatus)
    {
        StringBuilder sb = new();
        sb.Append("<b>ĐƠN ĐÃ HỦY — #").Append(WebUtility.HtmlEncode(order.Code)).Append("</b>\n");
        sb.Append("Cơ sở: ").Append(WebUtility.HtmlEncode(branch.Name)).Append('\n');
        sb.Append("Trạng thái trước: ").Append(WebUtility.HtmlEncode(fromStatus)).Append('\n');
        string reason = string.IsNullOrWhiteSpace(order.CancelReason) ? "Không có lý do" : order.CancelReason;
        sb.Append("Lý do: ").Append(WebUtility.HtmlEncode(reason));
        return sb.ToString();
    }

    public static string FormatOrderAbandoned(Order order, Branch branch, int waitedMinutes)
    {
        StringBuilder sb = new();
        sb.Append("<b>CẢNH BÁO ĐƠN CHỜ NHẬN QUÁ LÂU — #").Append(WebUtility.HtmlEncode(order.Code)).Append("</b>\n");
        sb.Append("Cơ sở: ").Append(WebUtility.HtmlEncode(branch.Name)).Append('\n');
        sb.Append("Đã chờ: ").Append(waitedMinutes).Append(" phút (đã thanh toán, chưa nhận đơn).");
        return sb.ToString();
    }

    private static string FormatVnd(long amount) =>
        string.Format(CultureInfo.GetCultureInfo("vi-VN"), "{0:N0} đ", amount);
}
