using Telemetry.Core;
using Telemetry.Predict;
using Telemetry.Tests.Infrastructure;

namespace Telemetry.Tests.Predict;

/// <summary>Frame (C2 п.1) і класифікатор (C1 №9) — зона Вані.</summary>
public class PredictTrapTests
{
    [Fact]
    public void Frame_roundtrip_big_endian_ref01()
    {
        var bytes = TestData.Hex("00000000 03E8 00D7 FFFD 01 01");
        var f = Frame.Read(bytes);
        Assert.Equal(new Frame(0, 1000, 215, -3, 1, 1), f);
        var back = new byte[Frame.Size];
        f.WriteTo(back);
        Assert.Equal(bytes, back);
    }

    [Theory]
    [InlineData("1,2,3,4,8,0")]   // mode поза 0..7
    [InlineData("1,2,3,4,1,2")]   // critical не 0/1
    [InlineData("1,,3,4,1,0")]    // пропуск
    [InlineData("1,99999,3,4,1,0")] // поза int16
    public void Frame_validation_rejects_bad_input(string line) =>
        Assert.ThrowsAny<FormatException>(() => Frame.ParseCsv(line));

    [Fact(Skip = TestData.Pending)]
    public void Classifier_assigns_class_to_every_frame_including_session_start()
    {
        var frames = File.ReadLines(TestData.Path("data", "valid.csv")).Skip(1).Take(50).Select(Frame.ParseCsv).ToArray();
        var classes = new Classifier().Predict(frames);
        Assert.Equal(frames.Length, classes.Length);
        Assert.All(classes, c => Assert.InRange(c, 0, 2));
    }
}
