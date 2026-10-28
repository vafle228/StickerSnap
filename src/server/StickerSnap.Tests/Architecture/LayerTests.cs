using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using StickerSnap.Api;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace StickerSnap.Tests.Architecture;

/// <summary>
/// Clean Architecture: dependencies point inwards, Api → Infrastructure → Application → Core.
/// A rule over a layer with no types yet passes, so the rules hold from the first class on.
/// </summary>
public sealed class LayerTests
{
    private static readonly ArchUnitNET.Domain.Architecture Server = new ArchLoader()
        .LoadAssemblies(
            typeof(Program).Assembly,
            typeof(global::StickerSnap.Infrastructure.DependencyInjection).Assembly,
            typeof(global::StickerSnap.Application.DependencyInjection).Assembly,
            System.Reflection.Assembly.Load("StickerSnap.Core"))
        .Build();

    private static readonly IObjectProvider<IType> Core = Types().That().ResideInAssembly("StickerSnap.Core").As("Core");
    private static readonly IObjectProvider<IType> Application =
        Types().That().ResideInAssembly(typeof(global::StickerSnap.Application.DependencyInjection).Assembly).As("Application");
    private static readonly IObjectProvider<IType> Infrastructure =
        Types().That().ResideInAssembly(typeof(global::StickerSnap.Infrastructure.DependencyInjection).Assembly).As("Infrastructure");
    private static readonly IObjectProvider<IType> Api = Types().That().ResideInAssembly(typeof(Program).Assembly).As("Api");

    [Fact]
    public void Core_depends_on_no_other_layer_and_no_framework()
    {
        Types().That().Are(Core).Should().NotDependOnAny(Application)
            .AndShould().NotDependOnAny(Infrastructure)
            .AndShould().NotDependOnAny(Api)
            .AndShould().NotDependOnAny(Types().That().ResideInNamespaceMatching(@"^(Microsoft|CliWrap)(\..*)?$"))
            .WithoutRequiringPositiveResults()
            .Check(Server);
    }

    [Fact]
    public void Application_knows_only_the_core()
    {
        Types().That().Are(Application).Should().NotDependOnAny(Infrastructure)
            .AndShould().NotDependOnAny(Api)
            .AndShould().NotDependOnAny(Types().That().ResideInNamespaceMatching(@"^(Microsoft\.AspNetCore|CliWrap)(\..*)?$"))
            .WithoutRequiringPositiveResults()
            .Check(Server);
    }

    /// <summary>
    /// The use cases talk to a media engine through the ports of Application/Media; which engine
    /// it is (FFmpeg today) only the Infrastructure knows, so another one can replace it there.
    /// </summary>
    [Fact]
    public void Core_and_application_do_not_name_a_media_engine()
    {
        Types().That().Are(Core).Or().Are(Application)
            .Should().NotHaveNameMatching(@"(?i)ff(mpeg|probe)")
            .AndShould().NotResideInNamespaceMatching(@"(?i)ff(mpeg|probe)")
            .WithoutRequiringPositiveResults()
            .Check(Server);
    }

    /// <summary>
    /// The use cases download videos by link through IVideoSites; which downloader does it (yt-dlp
    /// today) only the Infrastructure knows, so another one can replace it there.
    /// </summary>
    [Fact]
    public void Core_and_application_do_not_name_a_video_downloader()
    {
        Types().That().Are(Core).Or().Are(Application)
            .Should().NotHaveNameMatching(@"(?i)(yt-?dlp|youtube-?dl)")
            .AndShould().NotResideInNamespaceMatching(@"(?i)(yt-?dlp|youtube-?dl)")
            .WithoutRequiringPositiveResults()
            .Check(Server);
    }

    /// <summary>
    /// The use cases reach the messenger and the cloud service through IStickerPublisher and
    /// IDeviceLinking; how they are carried (HTTP today) only the Infrastructure knows.
    /// </summary>
    [Fact]
    public void Core_and_application_do_not_speak_http()
    {
        Types().That().Are(Core).Or().Are(Application)
            .Should().NotDependOnAny(Types().That().ResideInNamespaceMatching(@"^System\.Net\.Http(\..*)?$"))
            .WithoutRequiringPositiveResults()
            .Check(Server);
    }

    [Fact]
    public void Infrastructure_does_not_know_the_api()
    {
        Types().That().Are(Infrastructure).Should().NotDependOnAny(Api)
            .AndShould().NotDependOnAny(Types().That().ResideInNamespaceMatching(@"^Microsoft\.AspNetCore(\..*)?$"))
            .WithoutRequiringPositiveResults()
            .Check(Server);
    }

    [Fact]
    public void Only_the_composition_root_of_the_api_knows_the_infrastructure()
    {
        Types().That().Are(Api).And().AreNot(typeof(Program)).Should().NotDependOnAny(Infrastructure)
            .WithoutRequiringPositiveResults()
            .Check(Server);
    }

    /// <summary>
    /// A use case is registered and consumed through its own interface, <c>IFooService</c> for
    /// <c>FooService</c>, so a test or another engine can stand in for it.
    /// </summary>
    [Fact]
    public void Application_services_implement_their_own_interface()
    {
        var services = Server.Classes
            .Where(type => Application.GetObjects(Server).Contains(type) && type.Name.EndsWith("Service", StringComparison.Ordinal) && !type.IsAbstract.GetValueOrDefault());
        services.Where(type => !type.ImplementedInterfaces.Any(i => i.Name == "I" + type.Name))
            .Select(type => type.FullName).Should().BeEmpty("each service implements I<ClassName>");
    }

    [Fact]
    public void Nothing_outside_the_application_names_a_concrete_service()
    {
        Types().That().Are(Infrastructure).Or().Are(Api)
            .Should().NotDependOnAny(Classes().That().Are(Application).And().HaveNameEndingWith("Service"))
            .WithoutRequiringPositiveResults()
            .Check(Server);
    }
}
