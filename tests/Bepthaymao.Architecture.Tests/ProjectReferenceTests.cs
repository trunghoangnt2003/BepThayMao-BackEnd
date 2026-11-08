using System.Xml.Linq;

namespace Bepthaymao.Architecture.Tests;

public sealed class ProjectReferenceTests
{
    [Fact]
    public void ProjectReferencesFollowLayerRules()
    {
        string root = FindRepositoryRoot();
        Dictionary<string, string[]> allowed = new(StringComparer.Ordinal)
        {
            ["Bepthaymao.Domain"] = [],
            ["Bepthaymao.Application"] = ["Bepthaymao.Domain"],
            ["Bepthaymao.Infrastructure"] = ["Bepthaymao.Application", "Bepthaymao.Domain"],
            ["Bepthaymao.Api"] = ["Bepthaymao.Application", "Bepthaymao.Infrastructure"],
            ["Bepthaymao.Worker"] = ["Bepthaymao.Application", "Bepthaymao.Infrastructure"],
        };

        foreach ((string project, string[] dependencies) in allowed)
        {
            string path = Path.Combine(root, "src", project, project + ".csproj");
            XDocument document = XDocument.Load(path);
            string[] references = document.Descendants("ProjectReference")
                .Select(reference => Path.GetFileNameWithoutExtension(reference.Attribute("Include")!.Value))
                .ToArray();
            Assert.Equal(dependencies.Order(StringComparer.Ordinal), references.Order(StringComparer.Ordinal));

            if (project is "Bepthaymao.Domain" or "Bepthaymao.Application")
            {
                Assert.DoesNotContain(document.Descendants("PackageReference"),
                    reference => reference.Attribute("Include")!.Value.Contains("EntityFramework", StringComparison.Ordinal));
            }
        }
    }

    private static string FindRepositoryRoot()
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
