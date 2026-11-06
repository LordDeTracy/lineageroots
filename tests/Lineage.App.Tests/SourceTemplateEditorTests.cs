using Avalonia.Headless.XUnit;
using Lineage.App.ViewModels;
using Lineage.Core.Model;

namespace Lineage.App.Tests;

public class SourceTemplateEditorTests
{
    [AvaloniaFact]
    public void ATemplateFillsTheTitleAndKeepsSharedFields()
    {
        var tree = new FamilyTree();
        var source = new Source();
        var editor = new SourceEditorViewModel(tree, source, isNew: true);
        Assert.False(editor.HasTemplate);
        Assert.False(editor.IsValid);

        editor.TemplateOption = SourceEditorViewModel.TemplateOptions.First(o => o.Template?.Id == "census-uk");
        Assert.True(editor.HasTemplate);
        void Set(string key, string value) => editor.Fields.First(f => f.Field.Key == key).Value = value;
        Set("Year", "1881");
        Set("Parish", "Leeds");
        Set("County", "Yorkshire");
        Assert.Equal("1881 census, Leeds, Yorkshire", editor.SourceTitle);           // the title follows the form
        Assert.StartsWith("1881 census of England and Wales, Leeds, Yorkshire, p. 12", editor.PreviewFull);
        Assert.True(editor.IsValid);

        // A title the user writes is theirs.
        editor.SourceTitle = "Leeds census";
        Set("Year", "1891");
        Assert.Equal("Leeds census", editor.SourceTitle);

        // Switching template keeps fields with the same name.
        editor.TemplateOption = SourceEditorViewModel.TemplateOptions.First(o => o.Template?.Id == "census-us");
        Assert.Equal("1891", editor.Fields.First(f => f.Field.Key == "Year").Value);
        editor.TemplateOption = SourceEditorViewModel.TemplateOptions.First(o => o.Template?.Id == "census-uk");

        editor.Apply(source);
        Assert.Equal("census-uk", source.Template);
        Assert.Equal("Leeds", source.Fields["Parish"]);
        Assert.False(source.Fields.ContainsKey("Piece"));                            // empty boxes aren't kept

        // Back to free-form: the template's fields go.
        editor.TemplateOption = SourceEditorViewModel.TemplateOptions[0];
        editor.Apply(source);
        Assert.Equal("", source.Template);
        Assert.Empty(source.Fields);
    }
}

public class LinkCitationEditorTests
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ALinkFillsTheTemplate()
    {
        var editor = new SourceEditorViewModel(new FamilyTree(), new Source(), isNew: true) { ShowLinkTools = true };
        editor.Link = "https://www.familysearch.org/ark:/61903/1:1:NKTG-5ZQ";
        editor.FillFromLinkCommand.Execute(null);
        Assert.Equal("website", editor.TemplateOption.Template?.Id);
        Assert.Equal("FamilySearch", editor.Fields.First(f => f.Field.Key == "Website").Value);
        Assert.Contains("FamilySearch record NKTG-5ZQ", editor.PreviewFull);
        Assert.True(editor.HasLinkMessage);
    }
}
