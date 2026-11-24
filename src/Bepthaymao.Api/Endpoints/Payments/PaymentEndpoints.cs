using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bepthaymao.Api.Security;
using Bepthaymao.Application.Payments;
using Microsoft.AspNetCore.Mvc;

namespace Bepthaymao.Api.Endpoints.Payments;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfirmPaymentRequest([property: MaxLength(500)] string? Note = null);

public static class PaymentEndpoints
{
    public static RouteGroupBuilder MapPaymentEndpoints(this RouteGroupBuilder v1)
    {
        v1.MapPost("/webhooks/sepay", async (HttpContext context, [FromServices] IConfiguration config,
            [FromServices] IPaymentOperations payments, CancellationToken token) =>
        {
            string expected = config["SePay:WebhookKey"] ?? "";
            string header = context.Request.Headers.Authorization.ToString();
            byte[] suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(header));
            byte[] expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes("Apikey " + expected));
            if (expected.Length < 16 || !CryptographicOperations.FixedTimeEquals(suppliedHash, expectedHash))
                return Results.Unauthorized();
            if (context.Request.ContentLength > 32768) return Results.StatusCode(413);
            if (context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit) limit.MaxRequestBodySize = 32768;
            using MemoryStream buffer = new();
            byte[] chunk = new byte[4096];
            int read;
            while ((read = await context.Request.Body.ReadAsync(chunk, token)) != 0)
            {
                if (buffer.Length + read > 32768) return Results.StatusCode(413);
                buffer.Write(chunk, 0, read);
            }
            string raw = Encoding.UTF8.GetString(buffer.ToArray());
            SePayTransfer? transfer;
            try { transfer = JsonSerializer.Deserialize<SePayTransfer>(raw, JsonSerializerOptions.Web); }
            catch (JsonException) { return Results.BadRequest(); }
            if (transfer is null) return Results.BadRequest();
            await payments.ReceiveAsync(transfer, raw, token);
            return Results.Ok(new { success = true });
        }).AllowAnonymous().WithName("receiveSepayWebhook").WithTags("Webhooks").WithSummary("Nhận giao dịch SePay")
            .WithDescription("Xác thực Apikey, ghi giao dịch bền vững trước khi xác nhận.")
            .Accepts<SePayTransfer>("application/json").Produces(200).Produces(401).Produces(400).Produces(413);
        v1.MapPost("/owner/orders/{orderId:guid}/confirm-payment", async (Guid orderId, ConfirmPaymentRequest request,
            [FromServices] IPaymentOperations payments, CancellationToken token) =>
            Results.Ok(await payments.ConfirmAsync(orderId, request.Note, token)))
            .RequireAuthorization(PolicyNames.OwnerOnly).WithName("confirmOrderPayment").WithTags("Owner")
            .Produces<Bepthaymao.Application.Orders.OrderDto>().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409)
            .WithSummary("Owner xác nhận thanh toán").WithDescription("Ghi chú tùy chọn; chỉ Owner được xác nhận, có audit.");
        v1.MapGet("/owner/payments", async (int? page, [FromServices] IPaymentOperations payments, CancellationToken token) =>
            Results.Ok(await payments.ListAsync(page ?? 1, token)))
            .RequireAuthorization(PolicyNames.OwnerOnly).WithName("listOwnerPayments").WithTags("Owner")
            .Produces<IReadOnlyList<PaymentView>>().ProducesProblem(401).ProducesProblem(403)
            .WithSummary("Sổ thanh toán của Owner").WithDescription("Giao dịch phân trang, không trả raw payload.");
        v1.MapGet("/manager/payments", async (int? page, [FromServices] IPaymentOperations payments, CancellationToken token) =>
            Results.Ok(await payments.ListAsync(page ?? 1, token)))
            .RequireAuthorization(PolicyNames.ManagerOnly).WithName("listManagerPayments").WithTags("Manager")
            .Produces<IReadOnlyList<PaymentView>>().ProducesProblem(401).ProducesProblem(403)
            .WithSummary("Sổ thanh toán cơ sở").WithDescription("Chỉ giao dịch được ánh xạ đúng cơ sở của Manager.");
        return v1;
    }
}
