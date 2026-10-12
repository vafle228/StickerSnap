using StickerSnap.Application.App;
using StickerSnap.Application.Media;
using StickerSnap.Application.Sources;

namespace StickerSnap.Tests.Application.Projects;

/// <summary>A <see cref="SourceImport"/> for tests of everything but making and replacing sources.</summary>
internal static class Imports
{
    public static SourceImport Unused()
    {
        var uploads = Substitute.For<IUploadStore>();
        return new SourceImport(
            new SourceFiles(Substitute.For<IHostInfo>(), uploads),
            Substitute.For<IMediaProbe>(),
            Substitute.For<IFileFingerprint>(),
            uploads);
    }
}
