using Microsoft.AspNetCore.Http.Metadata;
using Bepthaymao.Application.Abstractions.Errors;

namespace Bepthaymao.Api.Hosting;

internal static class DeclaredBodyLimit
{
    /// <summary>
    /// Endpoint có <see cref="IRequestSizeLimitMetadata"/> mà thân khai báo (Content-Length) lớn hơn giới hạn thì trả 413 ProblemDetails ngay.
    /// Binding IFormFile hỏng vì vượt MultipartBodyLengthLimit chỉ cho 400 do framework tự ghi, nên chặn sớm theo tiêu đề để Owner nhận 413 đúng nghĩa (T-02-10).
    /// Thân chunked không có Content-Length vẫn bị Kestrel/Traefik và WithFormOptions chặn (400, không phải 500).
    /// </summary>
    public static WebApplication UseDeclaredBodyLimit(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            long? limit = context.GetEndpoint()?.Metadata.GetMetadata<IRequestSizeLimitMetadata>()?.MaxRequestBodySize;
            if (limit is not null && context.Request.ContentLength > limit)
                throw new PayloadTooLargeException("Dữ liệu gửi lên lớn hơn giới hạn cho phép. Chọn ảnh nhỏ hơn.");
            await next(context);
        });
        return app;
    }
}
