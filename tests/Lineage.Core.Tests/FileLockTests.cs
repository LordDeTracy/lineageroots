using System.Text;
using Lineage.Core.Editing;
using Lineage.Core.Model;
using Lineage.Core.Storage;

namespace Lineage.Core.Tests;

/// <summary>Password-protected files and the change history saved in the file.</summary>
public class FileLockTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"lineage-lock-{Guid.NewGuid():N}.lineage");

    public void Dispose() { try { File.Delete(_path); } catch (IOException) { } }

    // Fewer key-stretching rounds than real files, so the tests stay quick.
    private static FileLock Lock(string password, string hint = "") => FileLock.Create(password, hint, iterations: 1000);

    [Fact]
    public void LockedFileRoundTripsAndKeepsItsPassword()
    {
        var tree = RoundTripTests.SampleTree();
        tree.Lock = Lock("correct horse", "the stable");
        LineageStore.Save(tree, _path);

        Assert.True(FileLock.IsLocked(_path));
        Assert.Equal("the stable", FileLock.HintOf(_path));
        var back = LineageStore.Load(_path, "correct horse");
        Assert.Equal(tree.People.Count, back.People.Count);
        Assert.Equal("John Henry Doe", back.People[1].DisplayName);
        Assert.Equal("Test", back.Name);
        Assert.NotNull(back.Lock);

        // Saving what was opened keeps the same password.
        back.Name = "Again";
        LineageStore.Save(back, _path);
        Assert.Equal("Again", LineageStore.Load(_path, "correct horse").Name);
    }

    [Fact]
    public void NoNamesAreReadableInALockedFile()
    {
        var tree = RoundTripTests.SampleTree();
        tree.Lock = Lock("pw");
        LineageStore.Save(tree, _path);
        var raw = Encoding.UTF8.GetString(File.ReadAllBytes(_path));
        Assert.DoesNotContain("Doe", raw);
        Assert.DoesNotContain("Parish Register", raw);
        Assert.False(FileLock.IsLocked(Path.Combine(Path.GetTempPath(), "no-such-file.lineage")));
    }

    [Fact]
    public void WrongMissingOrDamagedPasswordIsRefused()
    {
        var tree = RoundTripTests.SampleTree();
        tree.Lock = Lock("Secret");
        LineageStore.Save(tree, _path);
        Assert.Throws<PasswordNeededException>(() => LineageStore.Load(_path));
        Assert.Throws<WrongPasswordException>(() => LineageStore.Load(_path, "secret"));   // case matters

        var bytes = File.ReadAllBytes(_path);
        bytes[bytes.Length / 2] ^= 0x40;
        File.WriteAllBytes(_path, bytes);
        Assert.Throws<WrongPasswordException>(() => LineageStore.Load(_path, "Secret"));
    }

    [Fact]
    public void RemovingTheLockSavesAnOrdinaryFile()
    {
        var tree = RoundTripTests.SampleTree();
        tree.Lock = Lock("pw");
        LineageStore.Save(tree, _path);
        var back = LineageStore.Load(_path, "pw");
        back.Lock = null;
        LineageStore.Save(back, _path);
        Assert.False(FileLock.IsLocked(_path));
        Assert.Equal(tree.People.Count, LineageStore.Load(_path).People.Count);
    }

    [Fact]
    public void EmptyPasswordIsNotAllowed() =>
        Assert.Throws<ArgumentException>(() => FileLock.Create(""));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HistoryIsSavedAndCanBeUndoneAfterReopening(bool locked)
    {
        var tree = RoundTripTests.SampleTree();
        if (locked) tree.Lock = Lock("pw");
        var history = new UndoHistory(tree) { Author = () => "Ann" };
        var john = tree.People[1];
        var edit = history.Begin("rename John", new[] { john });
        john.PrimaryName.Given = "Jack";
        edit.Commit();
        var add = history.Begin("add a child", Array.Empty<Entity>());
        tree.AddPerson(new Person { Names = { new Name { Given = "Baby", Surname = "Doe" } } });
        add.Commit();
        LineageStore.Save(tree, _path);

        var back = LineageStore.Load(_path, locked ? "pw" : null);
        Assert.Equal(2, back.History.Count);
        var rename = back.History[0];
        Assert.Equal("Ann", rename.Who);
        Assert.Equal("rename John", rename.Label);
        Assert.Equal(new ChangedRecord("Person", 1, "Jack Doe", "Changed"), rename.Records.Single());
        Assert.Equal("Added", back.History[1].Records.Single().How);

        var reopened = new UndoHistory(back);
        Assert.Equal("add a child", reopened.UndoLabel);
        Assert.Equal(2, reopened.UndoFrom(back.History[0]));
        Assert.Equal("John Henry", back.People[1].PrimaryName.Given);
        Assert.Equal(tree.People.Count - 1, back.People.Count);
        Assert.Empty(back.History);
        Assert.Equal("rename John", reopened.Redo());
        Assert.Equal("Jack", back.People[1].PrimaryName.Given);
    }

    [Fact]
    public void OldChangesKeepOnlyASummary()
    {
        var tree = RoundTripTests.SampleTree();
        var history = new UndoHistory(tree);
        var john = tree.People[1];
        for (var i = 0; i < UndoHistory.Limit + 5; i++)
        {
            var edit = history.Begin($"edit {i}", new[] { john });
            john.Note = $"note {i}";
            edit.Commit();
        }
        Assert.Equal(UndoHistory.Limit + 5, tree.History.Count);
        Assert.Equal(UndoHistory.Limit, tree.History.Count(c => c.CanUndo));
        Assert.False(tree.History[0].CanUndo);
        Assert.Equal("edit 0", tree.History[0].Label);
        Assert.Equal(UndoHistory.Limit, history.UndoFrom(tree.History[0]));   // stops where the snapshots run out
        Assert.Equal("note 4", tree.People[1].Note);
    }

    [Fact]
    public void AnEditThatChangesNothingIsNotRecorded()
    {
        var tree = RoundTripTests.SampleTree();
        var history = new UndoHistory(tree);
        history.Begin("nothing", new[] { tree.People[1] }).Commit();
        Assert.Empty(tree.History);
    }
}
