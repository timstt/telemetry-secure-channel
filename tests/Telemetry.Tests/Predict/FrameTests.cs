using Telemetry.Core;
using Telemetry.Tests.Infrastructure;

namespace Telemetry.Tests.Predict;

/// <summary>Кадр: 12 байтів big-endian і валідація CSV (C2 п.1).</summary>
public class FrameTests
{
    [Fact]
    public void Extreme_values_roundtrip()
    {
        var f = new Frame(uint.MaxValue, short.MinValue, short.MaxValue, -1, 7, 0);
        Assert.Equal(f, Frame.Read(f.ToBytes()));
        Assert.Equal(TestData.Hex("FFFFFFFF 8000 7FFF FFFF 07 00"), f.ToBytes());
    }

    [Fact]
    public void Read_rejects_invalid_mode_and_critical()
    {
        Assert.Throws<FormatException>(() => Frame.Read(TestData.Hex("00000000 0000 0000 0000 08 00")));
        Assert.Throws<FormatException>(() => Frame.Read(TestData.Hex("00000000 0000 0000 0000 00 02")));
        Assert.Throws<ArgumentException>(() => Frame.Read(new byte[11]));
        Assert.Throws<ArgumentException>(() => new Frame().WriteTo(new byte[11]));
    }

    [Fact]
    public void Serialize_and_read_all_are_inverse()
    {
        Frame[] frames = [new(1, 2, 3, 4, 5, 1), new(6, -7, 8, -9, 0, 0)];
        var bytes = Frame.Serialize(frames);
        Assert.Equal(24, bytes.Length);
        Assert.Equal(frames, Frame.ReadAll(bytes));
        Assert.Empty(Frame.ReadAll([]));
        Assert.Throws<FormatException>(() => Frame.ReadAll(new byte[13]));
    }

    [Fact]
    public void ParseCsv_accepts_label_column_and_spaces()
    {
        Assert.Equal(new Frame(53, 1371, 153, -137, 3, 1), Frame.ParseCsv("53,1371,153,-137,3,1,1"));
        Assert.Equal(new Frame(0, -5, 0, 0, 0, 0), Frame.ParseCsv(" 0, -5,0,0,0,0"));
        Assert.Equal(2, Frame.ParseLabel("1,2,3,4,1,0,2"));
        Assert.True(new Frame(0, 0, 0, 0, 7, 1).IsValid);
        Assert.False(new Frame(0, 0, 0, 0, 8, 1).IsValid);
    }

    [Theory]
    [InlineData("1,2,3,4,1")]           // замало полів
    [InlineData("1,2,3,4,1,0,1,9")]     // забагато полів
    [InlineData("-1,2,3,4,1,0")]        // від'ємний uint32
    [InlineData("4294967296,2,3,4,1,0")] // поза uint32
    [InlineData("1,2,x,4,1,0")]         // нечисло
    [InlineData("1,2,3,4,1,0,")]        // порожня мітка не вадить кадру, але ParseLabel відхилить
    public void ParseCsv_or_label_rejects_bad_rows(string line) =>
        Assert.ThrowsAny<FormatException>(() =>
        {
            Frame.ParseCsv(line);
            Frame.ParseLabel(line);
        });

    [Theory]
    [InlineData("1,2,3,4,1,0")]
    [InlineData("1,2,3,4,1,0,3")]
    public void ParseLabel_requires_label_in_range(string line) =>
        Assert.Throws<FormatException>(() => Frame.ParseLabel(line));

    [Fact]
    public void All_data_files_parse_without_errors()
    {
        foreach (var name in new[] { "train.csv", "valid.csv", "session.csv" })
        {
            var lines = File.ReadLines(TestData.Path("data", name)).Skip(1).Where(l => l.Length > 0).ToList();
            Assert.All(lines, l => Frame.ParseCsv(l));
        }
    }
}
