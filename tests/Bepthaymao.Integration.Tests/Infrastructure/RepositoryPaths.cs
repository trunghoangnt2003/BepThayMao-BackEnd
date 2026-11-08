namespace Bepthaymao.Integration.Tests.Infrastructure;

internal static class RepositoryPaths
{
    public static string Root
    {
        get
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Bepthaymao.slnx")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Không tìm thấy Bepthaymao.slnx.");
        }
    }
}
