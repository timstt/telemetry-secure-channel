using System.Text.Json.Nodes;
using Telemetry.Present;

namespace Telemetry.Tests.Present;

public class LiveRunTests
{
    private const string GoodJson = "{\"Scenarios\":[],\"Runs\":[],\"Seeds\":[1],\"ElapsedSeconds\":1.0,\"TimeLimitSeconds\":120}";

    private static void WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(10);
        }

        Assert.True(condition(), "умова не виконалась вчасно");
    }

    [Fact]
    public void Successful_run_replaces_data_and_clears_error()
    {
        var live = new LiveRun(() => GoodJson);
        Assert.False(live.HasData);
        Assert.True(live.RunNow());

        var state = live.State();
        Assert.True((bool)state["hasData"]!);
        Assert.False((bool)state["running"]!);
        Assert.Null(state["error"]);
        Assert.Equal("живий прогін", (string)state["source"]!);
        Assert.Equal(1, (int)state["runs"]!);
    }

    [Fact]
    public void Failed_run_keeps_previous_results_and_reports_error()
    {
        var ok = true;
        var live = new LiveRun(() => ok ? GoodJson : throw new InvalidOperationException("канал недоступний"));
        Assert.True(live.RunNow());

        ok = false;
        Assert.True(live.TryStart());
        WaitUntil(() => !(bool)live.State()["running"]!);

        var state = live.State();
        Assert.True((bool)state["hasData"]!);
        Assert.NotNull(state["results"]);
        Assert.Contains("канал недоступний", (string)state["error"]!, StringComparison.Ordinal);
        Assert.Equal(1, (int)state["runs"]!);
    }

    [Fact]
    public void Unexpected_exception_does_not_leave_the_page_stuck_in_running()
    {
        var live = new LiveRun(() => throw new FormatException("збій"));
        Assert.False(live.RunNow());

        var state = live.State();
        Assert.False((bool)state["running"]!);
        Assert.False((bool)state["hasData"]!);
        Assert.NotNull(state["error"]);
    }

    [Fact]
    public void Second_start_is_refused_while_a_run_is_in_progress()
    {
        using var release = new ManualResetEventSlim();
        var live = new LiveRun(() =>
        {
            release.Wait(TimeSpan.FromSeconds(5));
            return GoodJson;
        });

        Assert.True(live.TryStart());
        Assert.False(live.TryStart());
        release.Set();
        WaitUntil(() => !(bool)live.State()["running"]!);
        Assert.True(live.HasData);
        Assert.True(live.TryStart());
        WaitUntil(() => !(bool)live.State()["running"]!);
    }

    [Fact]
    public void Timeout_frees_the_button_but_keeps_data_and_blocks_a_parallel_run()
    {
        using var release = new ManualResetEventSlim();
        var calls = 0;
        var live = new LiveRun(
            () =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    return GoodJson;
                }

                release.Wait(TimeSpan.FromSeconds(5));
                return GoodJson;
            },
            timeoutSeconds: 0.2);

        Assert.True(live.RunNow());
        Assert.True(live.TryStart());
        WaitUntil(() => !(bool)live.State()["running"]!);

        var state = live.State();
        Assert.Contains("не вклався", (string)state["error"]!, StringComparison.Ordinal);
        Assert.True((bool)state["hasData"]!);
        Assert.False(live.TryStart());

        release.Set();
        WaitUntil(() => live.Worker is { IsCompleted: true });
        var afterTimeout = live.State();
        Assert.Equal(1, (int)afterTimeout["runs"]!);
        Assert.Contains("не вклався", (string)afterTimeout["error"]!, StringComparison.Ordinal);
        Assert.True(live.TryStart());
        WaitUntil(() => !(bool)live.State()["running"]!);
    }

    [Fact]
    public void Completed_event_receives_the_new_results()
    {
        var live = new LiveRun(() => GoodJson);
        string? received = null;
        live.Completed += json => received = json;
        live.RunNow();
        Assert.Equal(GoodJson, received);
        Assert.NotNull(JsonNode.Parse(received!));
    }
}
