using System.Diagnostics;
using System.Security.Cryptography;

namespace GreenFolder;

internal static class PerformanceTest
{
    // Original v0.2 matching algorithm, retained only for differential tests.
    static List<Change> Baseline(Plan p)
    {
        var rows = new List<Change>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string action, string path, string from = "") => rows.Add(new Change { Action = action, Path = path, From = from });
        foreach (var (rel, ms) in p.M.OrderBy(x => x.Key))
        {
            if (p.C.TryGetValue(rel, out var cs))
            {
                if (ms.Hash == cs.Hash) continue;
                Add(ms.Time > cs.Time ? "更新" : ms.Time < cs.Time ? "保留" : "冲突", rel);
            }
            else
            {
                var match = p.C.FirstOrDefault(x => !p.M.ContainsKey(x.Key) && !used.Contains(x.Key) && x.Value.Hash == ms.Hash);
                if (match.Key != null) { used.Add(match.Key); Add("移动", rel, match.Key); }
                else Add("新增", rel);
            }
        }
        foreach (var rel in p.C.Keys.Where(x => !p.M.ContainsKey(x) && !used.Contains(x)))
            Add(p.Mode == "原处保留" ? "保留" : p.Mode == "删除到回收站" ? "回收" : "归档", rel);
        return rows;
    }
    static IEnumerable<string> Signature(IEnumerable<Change> rows) => rows.Select(x => $"{x.Action}|{x.Path}|{x.From}");
    static (Dictionary<string, Stamp>, HashSet<string>) BaselineScan(string root)
    {
        var files = new Dictionary<string, Stamp>(StringComparer.OrdinalIgnoreCase);
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Walk(string dir)
        {
            foreach (string path in Directory.EnumerateFileSystemEntries(dir))
            {
                var attr = File.GetAttributes(path);
                if ((attr & FileAttributes.ReparsePoint) != 0) throw new IOException(path);
                string rel = System.IO.Path.GetRelativePath(root, path);
                if (Directory.Exists(path)) { dirs.Add(rel); Walk(path); }
                else
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var info = new FileInfo(path);
                    files[rel] = new Stamp(info.Length, info.LastWriteTimeUtc.Ticks, Convert.ToHexString(SHA256.HashData(stream)));
                }
            }
        }
        Walk(root); return (files, dirs);
    }
    public static void Run(string output)
    {
        var notes = new List<string>();
        string root = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(output)!, "performance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); notes.Add("PASS: " + name); }
        try
        {
            for (int seed = 0; seed < 60; seed++)
            {
                var random = new Random(seed);
                var p = new Plan { Mode = new[] { "原处保留", "删除到回收站", "移到额外资料" }[seed % 3] };
                for (int i = 0; i < 120; i++)
                {
                    int hash = random.Next(20);
                    if (random.Next(3) != 0) p.M["file" + i] = new Stamp(hash, random.Next(3), "hash" + hash);
                    hash = random.Next(20);
                    if (random.Next(3) != 0) p.C["FILE" + i] = new Stamp(hash, random.Next(3), "hash" + hash);
                    p.C["extra" + i] = new Stamp(hash, random.Next(3), "hash" + hash);
                }
                var expected = Baseline(p);
                Engine.BuildItems(p);
                Check(Signature(expected).SequenceEqual(Signature(p.Items)), "随机差异清单与旧版一致 " + seed);
            }
            string m = System.IO.Path.Combine(root, "master"), c = System.IO.Path.Combine(root, "child");
            Directory.CreateDirectory(m); Directory.CreateDirectory(c);
            File.WriteAllText(System.IO.Path.Combine(m, "same.txt"), "aaaa");
            File.WriteAllText(System.IO.Path.Combine(c, "same.txt"), "aaaa");
            var plan = Engine.Analyze(m, c, "原处保留");
            string changed = System.IO.Path.Combine(c, "same.txt");
            var time = File.GetLastWriteTimeUtc(changed);
            File.WriteAllText(changed, "bbbb"); File.SetLastWriteTimeUtc(changed, time);
            bool rejected = false;
            try { Engine.Execute(plan); } catch { rejected = true; }
            Check(rejected && !Directory.Exists(System.IO.Path.Combine(c, Engine.Records)), "同大小同时间但内容变化仍被执行前校验拦截");
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            rejected = false;
            try { Engine.Analyze(m, c, "原处保留", cancellation: cancel.Token); } catch (OperationCanceledException) { rejected = true; }
            Check(rejected, "支持取消对比");
            string large = System.IO.Path.Combine(m, "large.bin");
            byte[] content = new byte[2 * 1024 * 1024 + 17]; Random.Shared.NextBytes(content); File.WriteAllBytes(large, content);
            File.WriteAllBytes(System.IO.Path.Combine(m, "empty.bin"), []);
            var scan = Engine.Scan(m, false, s => { });
            Check(scan.Item1["large.bin"].Hash == Convert.ToHexString(SHA256.HashData(content)), "跨缓冲区大文件 SHA-256 正确");
            Check(scan.Item1["empty.bin"].Hash == Convert.ToHexString(SHA256.HashData(Array.Empty<byte>())), "空文件 SHA-256 正确");
            using var midCancel = new CancellationTokenSource();
            rejected = false;
            try { Engine.Scan(m, false, s => midCancel.Cancel(), midCancel.Token); } catch (OperationCanceledException) { rejected = true; }
            Check(rejected, "扫描途中支持取消");
            string bulk = System.IO.Path.Combine(m, "bulk.bin");
            using (var stream = File.Create(bulk))
                for (int i = 0; i < 64; i++) stream.Write(content, 0, 2 * 1024 * 1024);
            Directory.CreateDirectory(System.IO.Path.Combine(m, "empty-dir"));
            BaselineScan(m); Engine.Scan(m, false);
            var oldTimes = new List<double>(); var newTimes = new List<double>();
            for (int pass = 0; pass < 4; pass++)
            {
                (Dictionary<string, Stamp>, HashSet<string>) oldScan = (new(), new()), newScan = (new(), new());
                void Old() { var timer = Stopwatch.StartNew(); oldScan = BaselineScan(m); oldTimes.Add(timer.Elapsed.TotalMilliseconds); }
                void New() { var timer = Stopwatch.StartNew(); newScan = Engine.Scan(m, false); newTimes.Add(timer.Elapsed.TotalMilliseconds); }
                if (pass % 2 == 0) { Old(); New(); } else { New(); Old(); }
                Check(oldScan.Item1.Count == newScan.Item1.Count && oldScan.Item1.All(x => newScan.Item1.TryGetValue(x.Key, out var stamp) && x.Value == stamp) && oldScan.Item2.SetEquals(newScan.Item2), "新旧扫描内容与元数据一致 " + pass);
            }
            oldTimes.Sort(); newTimes.Sort();
            double oldMedian = (oldTimes[1] + oldTimes[2]) / 2, newMedian = (newTimes[1] + newTimes[2]) / 2;
            notes.Add($"本机临时资料热缓存扫描，约 130 MiB、4 次交错测试中位数：旧版 {oldMedian:F1} ms，新版 {newMedian:F1} ms，约 {oldMedian / newMedian:F2} 倍。不代表用户实际磁盘或冷缓存速度。");
            notes.Add("以下为内存中文件匹配基准，不含磁盘读取，不能代表整体加速。");
            foreach (int n in new[] { 2000, 8000, 16000 })
            {
                var p = new Plan { Mode = "移到额外资料" };
                for (int i = 0; i < n; i++) { p.M["master" + i] = new Stamp(i, 1, "m" + i); p.C["child" + i] = new Stamp(i, 1, "c" + i); }
                var timer = Stopwatch.StartNew(); var expected = Baseline(p); double oldMs = timer.Elapsed.TotalMilliseconds;
                timer.Restart(); Engine.BuildItems(p); double newMs = timer.Elapsed.TotalMilliseconds;
                Check(Signature(expected).SequenceEqual(Signature(p.Items)), $"{n:N0} + {n:N0} 文件匹配结果一致");
                notes.Add($"匹配阶段：母/子各 {n:N0} 文件，旧算法 {oldMs:F1} ms，新算法 {newMs:F1} ms，约 {oldMs / Math.Max(newMs, .001):F1} 倍。");
            }
            notes.Add("全部优化验证通过。");
        }
        catch (Exception ex) { notes.Add(ex.ToString()); Environment.ExitCode = 1; }
        File.WriteAllLines(output, notes);
    }
}
