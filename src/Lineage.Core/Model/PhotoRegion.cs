namespace Lineage.Core.Model;

/// <summary>
/// A person tagged in a photo: the rectangle around them, in coordinates normalised to the picture
/// (0..1, origin top-left). Normalised values keep the tag in the right place however large the
/// photo is shown, and survive a re-scan at a different resolution.
/// </summary>
public sealed class PhotoRegion
{
    public long PersonId { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    public PhotoRegion Clone() => (PhotoRegion)MemberwiseClone();
}
