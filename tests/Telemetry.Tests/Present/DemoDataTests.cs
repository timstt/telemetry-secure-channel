using System.Text.Json;
using Telemetry.Present;

namespace Telemetry.Tests.Present;

public class DemoDataTests
{
    [Fact]
    public void Every_section_of_the_overview_builds_from_the_repository_files()
    {
        using var doc = JsonDocument.Parse(DemoData.BuildJson());
        foreach (var section in doc.RootElement.EnumerateObject())
        {
            Assert.True(
                section.Value.GetProperty("ok").GetBoolean(),
                $"розділ {section.Name}: {(section.Value.TryGetProperty("error", out var e) ? e.GetString() : "")}");
        }
    }

    [Fact]
    public void Huffman_section_reflects_the_table_cost_and_the_cipher_order_finding()
    {
        using var doc = JsonDocument.Parse(DemoData.BuildJson());
        var h = doc.RootElement.GetProperty("huffman").GetProperty("data");
        Assert.Equal(2 * h.GetProperty("symbols").GetInt32(), h.GetProperty("tableBytes").GetInt32());
        Assert.True(h.GetProperty("huffmanFrames").GetInt32() < h.GetProperty("rawFrames").GetInt32());

        var order = h.GetProperty("order").EnumerateArray().ToArray();
        Assert.Equal(1.0, order[0].GetProperty("ratio").GetDouble(), 6);
        Assert.True(order[1].GetProperty("ratio").GetDouble() > 1.0);
        Assert.True(order[2].GetProperty("ratio").GetDouble() < 1.0);
    }

    [Fact]
    public void Ref04_payload_is_5568_bits_and_the_bare_stream_is_much_smaller()
    {
        using var doc = JsonDocument.Parse(DemoData.BuildJson());
        var r = doc.RootElement.GetProperty("huffman").GetProperty("data").GetProperty("ref04");
        Assert.Equal(64, r.GetProperty("frames").GetInt32());
        Assert.Equal(6144, r.GetProperty("rawBits").GetInt32());
        Assert.Equal(6856, r.GetProperty("rawPacketBits").GetInt32());
        Assert.Equal(3389, r.GetProperty("streamBits").GetInt32());
        Assert.Equal(5568, r.GetProperty("payloadBits").GetInt32());
        Assert.Equal(6168, r.GetProperty("packetBits").GetInt32());
        Assert.True(r.GetProperty("matchesReference").GetBoolean());
    }

    [Fact]
    public void Knapsack_dp_is_never_worse_than_greedy_on_session_windows()
    {
        using var doc = JsonDocument.Parse(DemoData.BuildJson());
        var k = doc.RootElement.GetProperty("knapsack").GetProperty("data");
        Assert.True(k.GetProperty("dpTotal").GetInt64() >= k.GetProperty("greedyTotal").GetInt64());
        foreach (var w in k.GetProperty("windows").EnumerateArray())
        {
            Assert.True(w.GetProperty("dp").GetInt32() >= w.GetProperty("greedy").GetInt32());
        }
    }

    [Fact]
    public void Tamper_demo_gives_the_codes_required_by_the_format()
    {
        using var doc = JsonDocument.Parse(DemoData.BuildJson());
        var checks = doc.RootElement.GetProperty("checks").GetProperty("data");
        Assert.All(checks.GetProperty("tamper").EnumerateArray(), t => Assert.True(t.GetProperty("ok").GetBoolean()));
        Assert.All(checks.GetProperty("refs").EnumerateArray(), r => Assert.True(r.GetProperty("ok").GetBoolean()));
    }

    [Fact]
    public void Markdown_tables_are_parsed_without_separators_and_with_bold_cells()
    {
        var tables = MarkdownTable.ParseAll("текст\n\n| a | b |\n|---|---:|\n| **x** | 1.5 |\n| y | 2 |\n\nщось\n| c |\n|---|\n| 3 |\n");
        Assert.Equal(2, tables.Count);
        Assert.Equal(new[] { "a", "b" }, tables[0][0]);
        Assert.Equal(new[] { "x", "1.5" }, tables[0][1]);
        Assert.Equal(1.5, MarkdownTable.Number("1.5"));
        Assert.Equal(7.25, MarkdownTable.Number("7.25 %"));
    }
}
