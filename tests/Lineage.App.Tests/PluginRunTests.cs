using Avalonia.Headless.XUnit;
using Lineage.Core.Model;
using Lineage.Core.Plugins;
using Lineage.Core.Reports;

namespace Lineage.App.Tests;

public sealed class UpperCaseSurnames : IToolPlugin
{
    public string Name => "Surnames in capitals";
    public string Description => "Writes every surname in capitals.";
    public string Run(FamilyTree tree, Person? current)
    {
        foreach (var n in tree.People.Values.SelectMany(p => p.Names)) n.Surname = n.Surname.ToUpperInvariant();
        return "Done";
    }
}

public sealed class OnePageReport : IReportPlugin
{
    public string Name => "One page";
    public string Description => "A page.";
    public ReportDocument Build(FamilyTree tree, Person? current) =>
        new() { Title = "One page", Blocks = { new Paragraph(current?.DisplayName ?? "") } };
}

public class PluginRunTests
{
    private static PluginInfo Info(IPlugin p, string kind) => new(p, "Test.dll", kind);

    [AvaloniaFact]
    public async Task AToolPluginRunIsOneUndoableStep()
    {
        var (vm, _) = await Demo.OpenAsync();
        vm.UsePlugins(new[] { Info(new UpperCaseSurnames(), "Tool") });
        var row = Assert.Single(vm.PluginTools);
        Assert.False(vm.HasPluginReports);

        await vm.RunPluginCommand.ExecuteAsync(row);
        Assert.All(vm.Tree.People.Values, p => Assert.Equal(p.Names[0].Surname.ToUpperInvariant(), p.Names[0].Surname));
        Assert.Equal("Done", vm.Status);

        vm.UndoCommand.Execute(null);
        Assert.Contains(vm.Tree.People.Values, p => p.Names[0].Surname == "Whitfield");
    }

    [AvaloniaFact]
    public async Task AReportPluginIsPreviewedAndSaved()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        vm.UsePlugins(new[] { Info(new OnePageReport(), "Report") });
        var path = Path.Combine(Path.GetTempPath(), $"lineage-plugin-{Guid.NewGuid():N}.pdf");
        try
        {
            dialogs.SavePath = path;
            await vm.RunPluginCommand.ExecuteAsync(Assert.Single(vm.PluginReports));
            Assert.Single(dialogs.Previews);
            Assert.True(File.Exists(path));
        }
        finally { File.Delete(path); }
    }
}
