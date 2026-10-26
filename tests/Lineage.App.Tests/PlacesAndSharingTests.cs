using Avalonia.Headless.XUnit;
using Lineage.App.ViewModels;
using Lineage.Core.Import.Gedcom;

namespace Lineage.App.Tests;

public class PlacesAndSharingTests
{
    [AvaloniaFact]
    public async Task PlacesCanGetGpsBeRenamedAndStandardised()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        vm.Section = AppSection.Places;
        Assert.Contains(vm.PlaceList, p => p.Name == "Harrow");
        Assert.Contains(vm.PlaceSuggestions, s => s.Suggestion.Names.Contains("Harrow"));   // "Harrow" vs "Harrow, Middlesex, England"

        vm.SelectedPlace = vm.PlaceList.Single(p => p.Name == "Leeds, Yorkshire, England");
        Assert.Single(vm.PlaceEvents);                     // the marriage
        vm.PlaceCoordinates = "53.8008, -1.5491";
        Assert.NotNull(vm.Tree.Families[2].Marriage!.Location);

        dialogs.PromptAnswer = "Leeds, West Yorkshire, England";
        await vm.RenamePlaceCommand.ExecuteAsync(null);
        Assert.Equal("Leeds, West Yorkshire, England", vm.Tree.Families[2].Marriage!.Place);
        Assert.NotNull(vm.Tree.Families[2].Marriage!.Location);   // GPS kept

        vm.ApplyAllPlaceSuggestionsCommand.Execute(null);
        Assert.DoesNotContain(vm.Tree.People.Values, p => p.Birth?.Place == "Harrow");
        vm.UndoCommand.Execute(null);
        Assert.Contains(vm.Tree.People.Values, p => p.Birth?.Place == "Harrow");
    }

    [AvaloniaFact]
    public async Task GedcomExportCanHideLivingPeople()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        // Clara (born 1872, no death) counts as dead by now; make someone clearly living.
        dialogs.Person = e => { e.Given = "Young"; e.BirthDate = "2001"; return true; };
        await vm.AddPersonCommand.ExecuteAsync(null);
        var path = Path.Combine(Path.GetTempPath(), $"lineage-export-{Guid.NewGuid():N}.ged");
        dialogs.SavePath = path;
        dialogs.ExportOptions = o => { Assert.Equal(1, o.LivingCount); o.LivingIndex = 0; return true; };
        await vm.ExportGedcomCommand.ExecuteAsync(null);
        var text = await File.ReadAllTextAsync(path);
        Assert.DoesNotContain("Young", text);
        Assert.Contains("Living", text);
        Assert.Contains("Thomas Edward", text);
        File.Delete(path);
    }

    [AvaloniaFact]
    public async Task ComparingWithAnotherTreeCopiesChosenPeopleIn()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        // A cousin's file: the same Thomas, plus a sister we don't have.
        var other = Demo.Gedcom.Replace("0 TRLR", """
            0 @I9@ INDI
            1 NAME Susan /Whitfield/
            1 SEX F
            1 BIRT
            2 DATE 1846
            1 FAMC @F1@
            0 TRLR
            """).Replace("1 CHIL @I1@\n", "1 CHIL @I1@\n1 CHIL @I9@\n");
        var path = Path.Combine(Path.GetTempPath(), $"lineage-cousin-{Guid.NewGuid():N}.ged");
        await File.WriteAllTextAsync(path, other);
        Assert.Contains(GedcomImporter.ImportFile(path).People.Values, p => p.DisplayName == "Susan Whitfield");
        dialogs.OpenPath = path;
        dialogs.CompareTrees = c =>
        {
            Assert.Equal(6, c.Result.Matches.Count);
            var susan = Assert.Single(c.OnlyTheirs);
            susan.Chosen = true;
            return true;
        };
        await vm.CompareTreesCommand.ExecuteAsync(null);
        File.Delete(path);

        var copied = Assert.Single(vm.Tree.People.Values, p => p.DisplayName == "Susan Whitfield");
        Assert.Equal(vm.Tree.People[1].ParentFamilyIds[0], copied.ParentFamilyIds[0]);   // Thomas's sister
        vm.UndoCommand.Execute(null);
        Assert.DoesNotContain(vm.Tree.People.Values, p => p.DisplayName == "Susan Whitfield");
    }
}
