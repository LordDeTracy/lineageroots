using Lineage.App.Services;

namespace Lineage.App.Tests;

public class PortableTests
{
    [Fact]
    public void PortableModeIsAFolderBesideTheProgram()
    {
        if (Portable.IsOn) return;                       // never disturb a real portable setup
        try
        {
            Assert.Contains("on", Portable.Switch(true));
            Assert.True(Portable.IsOn);
            Assert.Contains("off", Portable.Switch(false));
            Assert.False(Portable.IsOn);                  // only settings in it: removed
        }
        finally
        {
            if (Directory.Exists(Portable.DataFolder)) Directory.Delete(Portable.DataFolder, true);
        }
    }
}
