using System.Text;
using System.Text.Json.Nodes;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Projects;
using StickerSnap.Core.Render;
using StickerSnap.Core.Validation;
using StickerSnap.Infrastructure.Projects;
using StickerSnap.Tests.Core.Projects;

namespace StickerSnap.Tests.Infrastructure.Projects;

public sealed class ProjectFileTests
{
    private static readonly ProjectFile Format = ProjectFile.Current;

    private static Project Read(byte[] content) => Format.Read(content, "cat.ssproj");

    private static string Write(Project project) => Encoding.UTF8.GetString(Format.Write(project));

    [Fact]
    public void Reads_the_sample_of_version_1()
    {
        var project = Read(SampleProjects.Bytes(SampleProjects.SamplePath));

        project.Should().BeEquivalentTo(SampleProjects.Sample, SampleProjects.ByValue);
    }

    [Fact]
    public void Writes_the_sample_back_letter_for_letter()
    {
        var text = Write(Read(SampleProjects.Bytes(SampleProjects.SamplePath)));

        text.ReplaceLineEndings("\n").Should().Be(SampleProjects.Text(SampleProjects.SamplePath).TrimEnd('\n'));
    }

    [Fact]
    public void Reads_back_what_it_wrote()
    {
        var project = SampleProjects.Sample with
        {
            Name = "Танцы",
            Scene = Scenes.Of(Scenes.Segment with { ExcludedFrames = [3, 4], LongSticker = true, AutoFit = false }),
        };

        Read(Format.Write(project)).Should().BeEquivalentTo(project, SampleProjects.ByValue);
    }

    [Fact]
    public void Reads_back_the_last_export_of_each_segment()
    {
        var sample = SampleProjects.Sample;
        var project = sample
            .WithExport(SampleExports.Of(sample.Scene.Segments[0], sample.Source))
            .WithExport(SampleExports.Of(sample.Scene.Segments[1], sample.Source) with
            {
                Validation = SampleExports.Green with { Preset = SegmentPreset.Emoji, Verdict = ValidationVerdict.Experimental },
            });

        var read = Read(Format.Write(project));

        read.Should().BeEquivalentTo(project, SampleProjects.ByValue);
        read.Exports["seg-1"].Ladder[^1].Rung.Concessions.Should().Equal(new Concession.Fps(30, 24));
    }

    [Fact]
    public void Writes_an_export_under_its_segment_s_id_in_the_words_of_the_contract()
    {
        var sample = SampleProjects.Sample;

        var text = Write(sample.WithExport(SampleExports.Of(sample.Scene.Segments[0], sample.Source)));

        var export = JsonNode.Parse(text)!["exports"]!["seg-1"]!;
        export["sceneRevision"]!.GetValue<int>().Should().Be(12);
        export["signature"]!.GetValue<string>().Should().StartWith("v1:");
        export["concessions"]!.ToJsonString().Should().Be("""[{"kind":"fps","from":30,"to":24}]""");
        export["validation"]!["verdict"]!.GetValue<string>().Should().Be("pass");
        export["segmentId"].Should().BeNull();
    }

    [Fact]
    public void Writes_in_the_words_of_the_contract_and_keeps_cyrillic_readable()
    {
        var text = Write(SampleProjects.Sample);

        text.Should().Contain("\"loop\": \"pingpong\"").And.Contain("\"mode\": \"crop\"").And.Contain("\"kind\": \"video\"")
            .And.Contain("\"name\": \"Кот и клавиатура\"").And.Contain("\"exports\": {}");
    }

    [Fact]
    public void Refuses_a_cut_off_file_as_invalid()
    {
        var read = () => Read(SampleProjects.Bytes("Projects/truncated.ssproj"));

        var error = read.Should().Throw<AppException>().Which;
        error.Code.Should().Be(ErrorCode.ProjectInvalid);
        error.Message.Should().StartWith("cat.ssproj is not a project");
        // JSON that does not parse has no property to point at: the line stands for it (counted from 0)
        error.Log.Should().Contain("LineNumber: 37");
    }

    [Fact]
    public void Names_the_property_of_a_wrong_type_in_the_log()
    {
        var read = () => Read(SampleProjects.Bytes("Projects/wrong-type.ssproj"));

        var error = read.Should().Throw<AppException>().Which;
        error.Code.Should().Be(ErrorCode.ProjectInvalid);
        error.Log.Should().StartWith("$.scene.segments[0].outFrame: ").And.NotContain("LineNumber");
    }

    [Fact]
    public void Refuses_a_file_of_a_newer_app()
    {
        var read = () => Read(SampleProjects.Bytes("Projects/too-new.ssproj"));

        read.Should().Throw<AppException>().Which.Code.Should().Be(ErrorCode.ProjectTooNew);
    }

    public static TheoryData<string, Action<JsonObject>, string> BrokenFiles => new()
    {
        { "no name", root => root.Remove("name"), "$:" },
        { "a null name", root => root["name"] = null, "$.name:" },
        { "an id that is not a GUID", root => root["id"] = "cat", "$.id:" },
        { "a schemaVersion in quotes", root => root["schemaVersion"] = "1", "$.schemaVersion:" },
        { "no schemaVersion", root => root.Remove("schemaVersion"), "$.schemaVersion:" },
        { "a schemaVersion of 0", root => root["schemaVersion"] = 0, "$.schemaVersion:" },
        { "exports as a list", root => root["exports"] = new JsonArray(), "$.exports:" },
        { "a fractional frame", root => Segment(root, 0)["inFrame"] = 1.5, "$.scene.segments[0].inFrame:" },
        { "a frame of no known mode", root => Segment(root, 0)["frame"]!["mode"] = "zoom", "$.scene.segments[0].frame" },
        { "a frame with no mode", root => Segment(root, 0)["frame"]!.AsObject().Remove("mode"), "$.scene.segments[0].frame" },
        { "a loop of no known kind", root => Segment(root, 1)["loop"] = "backwards", "$.scene.segments[1].loop:" },
        { "a loop as a number", root => Segment(root, 1)["loop"] = 1, "$.scene.segments[1].loop:" },
        { "a null layer", root => root["scene"]!["layers"]![0] = null, "$.scene.layers[0]:" },
        { "a null segment", root => root["scene"]!["segments"]![1] = null, "$.scene.segments[1]:" },
        { "no scene", root => root.Remove("scene"), "$:" },
    };

    [Theory]
    [MemberData(nameof(BrokenFiles))]
    public void Points_at_what_breaks_a_file(string because, Action<JsonObject> breakIt, string log)
    {
        var read = () => Read(Sample(breakIt));

        var error = read.Should().Throw<AppException>(because).Which;
        error.Code.Should().Be(ErrorCode.ProjectInvalid, because);
        error.Log.Should().StartWith(log, because);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("")]
    public void Refuses_what_is_not_a_json_object(string text)
    {
        var read = () => Read(Encoding.UTF8.GetBytes(text));

        read.Should().Throw<AppException>().Which.Code.Should().Be(ErrorCode.ProjectInvalid);
    }

    [Fact]
    public void Reads_a_frame_whose_mode_comes_last()
    {
        var project = Read(Sample(root =>
        {
            var frame = Segment(root, 0)["frame"]!.AsObject();
            frame.Remove("mode");
            frame["mode"] = "crop";
        }));

        project.Scene.Segments[0].Frame.Should().Be(Scenes.Segment.Frame);
    }

    [Fact]
    public void Skips_a_property_it_does_not_know()
    {
        var project = Read(Sample(root => root["color"] = "red"));

        project.Name.Should().Be(SampleProjects.Sample.Name);
    }

    [Fact]
    public void Leaves_a_segment_past_the_end_of_the_source_to_the_scene_rules()
    {
        // After the source is replaced with a shorter one, the saved scene runs past its end
        var project = Read(Sample(root => Segment(root, 0)["outFrame"] = 500));

        project.Scene.Segments[0].OutFrame.Should().Be(500);
    }

    [Fact]
    public void Brings_an_older_file_up_to_the_current_schema_before_reading_it()
    {
        var format = new ProjectFile(2, new Dictionary<int, ProjectFile.Migration>
        {
            [1] = root =>
            {
                root["name"] = $"{root["name"]} (v2)";
                return root;
            },
        });

        var project = format.Read(SampleProjects.Bytes(SampleProjects.SamplePath), "cat.ssproj");

        project.Name.Should().Be("Кот и клавиатура (v2)");
        Encoding.UTF8.GetString(format.Write(project)).Should().Contain("\"schemaVersion\": 2,");
    }

    [Fact]
    public void Wants_a_migration_for_every_older_schema()
    {
        var format = () => new ProjectFile(3, new Dictionary<int, ProjectFile.Migration> { [1] = root => root });

        format.Should().Throw<ArgumentException>().WithMessage("No migration from schema 2 to 3*");
    }

    private static JsonObject Segment(JsonObject root, int index) => root["scene"]!["segments"]![index]!.AsObject();

    private static byte[] Sample(Action<JsonObject> change)
    {
        var root = JsonNode.Parse(SampleProjects.Text(SampleProjects.SamplePath))!.AsObject();
        change(root);
        return Encoding.UTF8.GetBytes(root.ToJsonString());
    }
}
