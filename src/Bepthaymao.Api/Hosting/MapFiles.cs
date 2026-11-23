using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace Bepthaymao.Api.Hosting;

internal static class MapFiles
{
    /// <summary>Tệp PMTiles và glyph tự host; static middleware hỗ trợ Range, không liệt kê thư mục.</summary>
    public static WebApplication UseMapFiles(this WebApplication app)
    {
        if (BuildTimeDocs.IsActive) return app;
        string root = Path.GetFullPath(app.Configuration["Maps:RootPath"] ?? Path.Combine(app.Environment.ContentRootPath, "maps"));
        if (Directory.Exists(root))
        {
            PhysicalFileProvider files = new(root);
            app.Lifetime.ApplicationStopped.Register(files.Dispose);
            FileExtensionContentTypeProvider types = new();
            types.Mappings.Clear();
            types.Mappings[".pmtiles"] = "application/vnd.pmtiles";
            types.Mappings[".pbf"] = "application/x-protobuf";
            app.UseStaticFiles(new StaticFileOptions
            {
                RequestPath = "/maps",
                FileProvider = files,
                ContentTypeProvider = types,
                OnPrepareResponse = context =>
                {
                    context.Context.Response.Headers.CacheControl = "public, max-age=3600";
                    context.Context.Response.Headers.XContentTypeOptions = "nosniff";
                },
            });
        }
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/maps")) context.Response.StatusCode = StatusCodes.Status404NotFound;
            else await next(context);
        });
        return app;
    }
}
