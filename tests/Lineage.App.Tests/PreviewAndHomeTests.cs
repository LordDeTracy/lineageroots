using Avalonia.Headless.XUnit;

namespace Lineage.App.Tests;

public class PreviewAndHomeTests
{
    [AvaloniaFact]
    public async Task AReportIsPreviewedBeforeItIsSaved()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        var path = Path.Combine(Path.GetTempPath(), $"lineage-preview-{Guid.NewGuid():N}.pdf");
        try
        {
            dialogs.SavePath = path;
            await vm.IndividualSummaryCommand.ExecuteAsync(null);
            var preview = Assert.Single(dialogs.Previews);
            Assert.NotEmpty(preview.PageImages);
            Assert.True(File.Exists(path));
        }
        finally { File.Delete(path); }
    }

    [AvaloniaFact]
    public async Task TheHomePersonCanBeChosenByName()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        dialogs.PromptAnswer = "George Whitfield (1815–)";
        await vm.ChooseHomePersonCommand.ExecuteAsync(null);
        Assert.Equal("George Whitfield", vm.Tree.Person(vm.Tree.HomePersonId)!.DisplayName);
    }
}
