using Lineage.Core.Model;
using Lineage.Core.Plugins;
using Lineage.Core.Reports;

namespace Lineage.Core.Tests;

/// <summary>A plugin as someone else would write one; the tests load this assembly from a plugins folder.</summary>
public sealed class SampleSurnameReport : IReportPlugin
{
    public string Name => "Surname count";
    public string Description => "How many people have each surname.";

    public ReportDocument Build(FamilyTree tree, Person? current)
    {
        var doc = new ReportDocument { Title = Name };
        foreach (var g in tree.People.Values.GroupBy(p => p.Names.FirstOrDefault()?.Surname ?? "").OrderBy(g => g.Key))
            doc.Blocks.Add(new Paragraph($"{g.Key}: {g.Count()}"));
        return doc;
    }
}

public abstract class NotAPlugin : IToolPlugin
{
    public string Name => "";
    public string Description => "";
    public abstract string Run(FamilyTree tree, Person? current);
}

public class PluginTests
{
    [Fact]
    public void PluginsAreFoundInTheFolder()
    {
        var folder = Directory.CreateTempSubdirectory("lineage-plugins-").FullName;
        try
        {
            File.Copy(typeof(SampleSurnameReport).Assembly.Location, Path.Combine(folder, "Sample.dll"));
            File.WriteAllText(Path.Combine(folder, "Broken.dll"), "not a library");
            var found = PluginHost.Find(folder, out var problems);

            var report = Assert.Single(found, p => p.Plugin.Name == "Surname count");
            Assert.Equal("Report", report.Kind);
            Assert.Equal("Sample.dll:Lineage.Core.Tests.SampleSurnameReport", report.Key);
            Assert.DoesNotContain(found, p => p.Plugin is NotAPlugin);
            Assert.Contains(problems, p => p.StartsWith("Broken.dll:"));

            var tree = new FamilyTree();
            var doc = ((IReportPlugin)report.Plugin).Build(tree, null);
            Assert.Equal("Surname count", doc.Title);
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    [Fact]
    public void AMissingFolderHasNoPlugins()
    {
        Assert.Empty(PluginHost.Find(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), out var problems));
        Assert.Empty(problems);
    }
}
