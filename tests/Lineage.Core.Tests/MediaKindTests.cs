using Lineage.Core.Media;
using Lineage.Core.Model;

namespace Lineage.Core.Tests;

public class MediaKindTests
{
    [Theory]
    [InlineData("grandma.JPG", "", MediaKind.Photo)]
    [InlineData("interview.mp3", "PHOTO", MediaKind.Sound)]      // the file's own type wins
    [InlineData("reunion.MOV", "", MediaKind.Video)]
    [InlineData("will.pdf", "", MediaKind.Document)]
    [InlineData("scan", "PHOTO", MediaKind.Photo)]                // no extension: the GEDCOM type decides
    [InlineData("tape", "audio", MediaKind.Sound)]
    [InlineData("thing.xyz", "", MediaKind.Other)]
    public void KindsComeFromTheFileThenTheType(string file, string type, MediaKind kind) =>
        Assert.Equal(kind, MediaKinds.Of(new MediaLink { File = file, MediaType = type }));
}
