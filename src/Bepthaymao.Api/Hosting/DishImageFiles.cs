using Bepthaymao.Infrastructure.Media;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace Bepthaymao.Api.Hosting;

internal static class DishImageFiles
{
    /// <summary>Phục vụ tĩnh ảnh món đã tải ở /media/mon: chỉ .webp, tên GUID nên cache một năm immutable, không liệt kê thư mục.</summary>
    public static WebApplication UseDishImageFiles(this WebApplication app)
    {
        DishImageStorageOptions storage = app.Services.GetRequiredService<DishImageStorageOptions>();
        if (BuildTimeDocs.IsActive) return app;
        Directory.CreateDirectory(storage.RootPath);
        FileExtensionContentTypeProvider contentTypes = new();
        contentTypes.Mappings.Clear();
        contentTypes.Mappings[".webp"] = "image/webp";
        app.UseStaticFiles(new StaticFileOptions
        {
            RequestPath = "/media/mon",
            FileProvider = new PhysicalFileProvider(storage.RootPath),
            ContentTypeProvider = contentTypes,
            ServeUnknownFileTypes = false,
            OnPrepareResponse = context =>
            {
                context.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
                context.Context.Response.Headers.XContentTypeOptions = "nosniff";
            },
        });
        return app;
    }
}
