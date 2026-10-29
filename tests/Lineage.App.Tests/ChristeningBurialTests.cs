using Avalonia.Headless.XUnit;

namespace Lineage.App.Tests;

public class ChristeningBurialTests
{
    [AvaloniaFact]
    public async Task AChristeningOrBurialCanStandInForBirthOrDeath()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        // A foundling: no birth or parents known, only the day she was christened and where she was buried.
        dialogs.Person = editor =>
        {
            (editor.Given, editor.Surname) = ("Hope", "Foundling");
            editor.BirthKindIndex = 1;
            (editor.BirthDate, editor.BirthPlace) = ("3 May 1850", "St Anne's, Leeds");
            editor.DeathKindIndex = 1;
            (editor.DeathDate, editor.DeathPlace) = ("1921", "Leeds");
            return true;
        };
        await vm.AddPersonCommand.ExecuteAsync(null);
        var hope = vm.Tree.People.Values.Single(p => p.PrimaryName.Given == "Hope");
        Assert.Null(hope.Find("BIRT"));
        Assert.Equal(("3 MAY 1850", "St Anne's, Leeds"), (hope.Find("CHR")!.Date.ToGedcom(), hope.Find("CHR")!.Place));
        Assert.Equal("1921", hope.Find("BURI")!.Date.ToGedcom());
        Assert.Equal("1850–1921", hope.Lifespan);

        // Adding her opened her page; editing her again shows the christening and burial, not empty birth and death.
        dialogs.Person = editor =>
        {
            Assert.Equal((1, 1, "St Anne's, Leeds"), (editor.BirthKindIndex, editor.DeathKindIndex, editor.BirthPlace));
            return false;
        };
        await vm.EditPersonCommand.ExecuteAsync(null);
    }
}
