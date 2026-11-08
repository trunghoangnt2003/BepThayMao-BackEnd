using System.Reflection;

namespace Bepthaymao.Api.Hosting;

internal static class BuildTimeDocs
{
    public static bool IsActive => Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";
}
