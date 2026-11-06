using Avalonia.Headless.XUnit;
using Lineage.Core.Export;
using Lineage.Core.Storage;

namespace Lineage.App.Tests;

public class BranchExportTests
{
    [AvaloniaFact]
    public async Task ABranchIsSavedAsAFamilyFileOfItsOwn()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        var george = vm.Tree.People.Values.Single(p => p.DisplayName == "George Whitfield");
        vm.NavigateCommand.Execute(george.Id);
        var path = Path.Combine(Path.GetTempPath(), $"lineage-branch-{Guid.NewGuid():N}.lineage");
        try
        {
            dialogs.SavePath = path;
            dialogs.ExportOptions = o =>
            {
                Assert.Equal(3, o.BranchOptions.Length);                      // no "whole tree" when splitting off
                o.BranchIndex = 1;                                             // descendants
                return true;
            };
            var before = vm.Tree.People.Count;
            await vm.SaveBranchCommand.ExecuteAsync(null);
            var branch = LineageStore.Load(path);
            Assert.Contains(branch.People.Values, p => p.DisplayName == "Thomas Edward Whitfield");
            Assert.True(branch.People.Count < before);
            Assert.Equal(george.Id, branch.HomePersonId);
            Assert.Equal(before, vm.Tree.People.Count);                       // the open tree is unchanged
        }
        finally { File.Delete(path); }
    }

    [AvaloniaFact]
    public async Task GedcomCanTakeJustAncestors()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        var thomas = vm.Tree.People.Values.Single(p => p.DisplayName == "Thomas Edward Whitfield");
        vm.NavigateCommand.Execute(thomas.Id);
        var path = Path.Combine(Path.GetTempPath(), $"lineage-branch-{Guid.NewGuid():N}.ged");
        try
        {
            dialogs.SavePath = path;
            dialogs.ExportOptions = o => { o.BranchIndex = (int)BranchKind.Ancestors; o.LivingIndex = 2; return true; };
            await vm.ExportGedcomCommand.ExecuteAsync(null);
            var ged = await File.ReadAllTextAsync(path);
            Assert.Contains("Whitfield/", ged);
            Assert.Contains("George", ged);
            Assert.DoesNotContain("Clara", ged);                              // a daughter, not an ancestor
        }
        finally { File.Delete(path); }
    }
}

public class Gedcom7ExportTests
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task GedcomSevenCanBeChosen()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        var path = Path.Combine(Path.GetTempPath(), $"lineage-g7-{Guid.NewGuid():N}.ged");
        try
        {
            dialogs.SavePath = path;
            dialogs.ExportOptions = o => { Assert.True(o.ShowGedcomVersion); o.GedcomVersionIndex = 1; o.LivingIndex = 2; return true; };
            await vm.ExportGedcomCommand.ExecuteAsync(null);
            Assert.Contains("2 VERS 7.0", await File.ReadAllTextAsync(path));
        }
        finally { File.Delete(path); }
    }
}
