using System.Diagnostics;

namespace GreenFolder;

public sealed record WorkProgress(string Stage, string Current, double? Fraction, TimeSpan? Remaining, long ReadBytes, long CopiedBytes, double ElapsedSeconds)
{
    public string Estimate => Remaining is { } time
        ? time.TotalHours >= 24 ? $"约 {time.TotalDays:F1} 天" : time.TotalHours >= 1 ? $"约 {(int)time.TotalHours} 小时 {time.Minutes} 分" : time.TotalMinutes >= 1 ? $"约 {(int)time.TotalMinutes} 分 {time.Seconds} 秒" : $"约 {Math.Ceiling(time.TotalSeconds):0} 秒"
        : "正在估算";
}

internal sealed class WorkMeter(long total, Action<string>? text, Action<WorkProgress>? detail, Func<double>? elapsed = null)
{
    readonly Stopwatch timer = Stopwatch.StartNew();
    long done, read, copied;
    double lastReport = -1;
    public const long ItemUnit = 4096;
    public void Add(long amount, string stage, string current, long readBytes = 0, long copiedBytes = 0, bool force = false)
    {
        done += amount; read += readBytes; copied += copiedBytes;
        double seconds = elapsed?.Invoke() ?? timer.Elapsed.TotalSeconds;
        if (!force && seconds - lastReport < .25) return;
        lastReport = seconds;
        double fraction = total == 0 ? 1 : Math.Clamp((double)done / total, 0, 1);
        double remaining = done == 0 ? double.NaN : seconds * Math.Max(0, total - done) / done;
        TimeSpan? eta = fraction >= 1 ? TimeSpan.Zero : seconds >= 1 && double.IsFinite(remaining) && remaining < TimeSpan.MaxValue.TotalSeconds ? TimeSpan.FromSeconds(remaining) : null;
        var state = new WorkProgress(stage, current, fraction, eta, read, copied, seconds);
        text?.Invoke($"{stage} · {fraction:P1} · 预计剩余 {state.Estimate} · {current}");
        detail?.Invoke(state);
    }
    public void Finish(string stage) { done = Math.Max(0, total); Add(0, stage, "完成", force: true); }
    public static void Counting(string current, Action<string>? text, Action<WorkProgress>? detail)
    {
        text?.Invoke("正在统计目录和总容量，暂无法估算… · " + current);
        detail?.Invoke(new WorkProgress("统计目录与容量", current, null, null, 0, 0, 0));
    }
}

public sealed class ExecutionInterruptedException(string recordPath, int completed)
    : OperationCanceledException($"整理已中断，已完成 {completed} 项；已完成操作保留。\n记录与备份：{recordPath}")
{
    public string RecordPath { get; } = recordPath;
    public int Completed { get; } = completed;
}
