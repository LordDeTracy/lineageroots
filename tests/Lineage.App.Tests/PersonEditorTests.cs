using Avalonia.Headless.XUnit;
using Lineage.App.ViewModels;
using Lineage.Core.Model;

namespace Lineage.App.Tests;

public class PersonEditorTests
{
    [AvaloniaFact]
    public async Task EditorSavesNamesTagsColourDnaAndLdsAsOneUndoableEdit()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        dialogs.Person = editor =>
        {
            editor.AddNameCommand.Execute(null);
            editor.AltNames[0].Given = "Tom";
            editor.AltNames[0].KindIndex = 3;               // nickname
            editor.NewTag = "Direct line";
            editor.AddTagCommand.Execute(null);
            editor.NewTag = "direct LINE";                  // same tag, different case: not added twice
            editor.AddTagCommand.Execute(null);
            editor.ColourIndex = 2;                         // Green
            editor.AddDnaCommand.Execute(null);
            editor.Dna[0].Kind = "Y-DNA";
            editor.Dna[0].Haplogroup = "R1b";
            editor.Ordinances.Single(o => o.Tag == "BAPL").Date = "1 Jun 1990";
            editor.Ordinances.Single(o => o.Tag == "BAPL").Temple = "LOGAN";
            Assert.True(editor.HasParents);
            editor.MotherRelation = 1;                      // adopted
            editor.Sealing.Status = "Completed";
            editor.Email = "tom@example.com";
            return true;
        };
        await vm.EditPersonCommand.ExecuteAsync(null);

        var me = vm.Tree.People[1];
        var nick = Assert.Single(me.Names.Skip(1));
        Assert.Equal(("Tom", "nickname"), (nick.Given, nick.Type));
        Assert.Equal(new[] { "Direct line" }, me.Tags);
        Assert.Equal("Green", me.Colour);
        Assert.Equal(("Y-DNA", "R1b"), (me.DnaTests[0].Kind, me.DnaTests[0].Haplogroup));
        Assert.Equal("LOGAN", me.Find("BAPL")!.Temple);
        var link = vm.Tree.Families[1].Children.Single(c => c.ChildId == 1);
        Assert.Equal(ParentRelation.Adopted, link.ToMother);
        Assert.Equal("Completed", link.Sealing!.Status);
        Assert.Equal("tom@example.com", me.Email);
        Assert.Contains("Y-DNA R1b", vm.Chips);
        Assert.Contains("Direct line", vm.Chips);
        Assert.True(vm.People.Single(r => r.Id == 1).HasColour);

        vm.UndoCommand.Execute(null);
        me = vm.Tree.People[1];
        Assert.Single(me.Names);
        Assert.Empty(me.Tags);
        Assert.Null(me.Find("BAPL"));
        Assert.Null(vm.Tree.Families[1].Children.Single(c => c.ChildId == 1).Sealing);
    }

    [AvaloniaFact]
    public async Task ClearingAnOrdinanceRemovesIt()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        dialogs.Person = e => { e.Ordinances[0].Date = "1990"; return true; };
        await vm.EditPersonCommand.ExecuteAsync(null);
        Assert.NotNull(vm.Tree.People[1].Find("BAPL"));
        dialogs.Person = e => { e.Ordinances[0].Date = ""; return true; };
        await vm.EditPersonCommand.ExecuteAsync(null);
        Assert.Null(vm.Tree.People[1].Find("BAPL"));
    }
}
