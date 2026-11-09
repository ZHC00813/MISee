using System.Diagnostics;
using System.Security.Cryptography;

namespace GreenFolder;

internal static class IntegrationTest
{
    public static void Run(string output)
    {
        var notes = new List<string>();
        string root = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(output)!, "update-experiment-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); notes.Add("PASS: " + name); }
        (string M, string C) Pair(string name)
        {
            string m = System.IO.Path.Combine(root, name, "母文件夹"), c = System.IO.Path.Combine(root, name, "子文件夹");
            Directory.CreateDirectory(m); Directory.CreateDirectory(c); return (m, c);
        }
        void Put(string dir, string name, string content, int day)
        {
            string path = System.IO.Path.Combine(dir, name); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content); File.SetLastWriteTimeUtc(path, new DateTime(2026, 1, day, 0, 0, 0, DateTimeKind.Utc));
        }
        void Binary(string dir, string name, int mib, int seed, int day)
        {
            byte[] block = new byte[1024 * 1024]; new Random(seed).NextBytes(block);
            string path = System.IO.Path.Combine(dir, name); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            using (var file = File.Create(path)) for (int i = 0; i < mib; i++) file.Write(block);
            File.SetLastWriteTimeUtc(path, new DateTime(2026, 1, day, 0, 0, 0, DateTimeKind.Utc));
        }
        string Hash(string file) { using var stream = File.OpenRead(file); return Convert.ToHexString(SHA256.HashData(stream)); }
        bool Monotonic(List<WorkProgress> states)
        {
            var values = states.Where(x => x.Fraction != null).Select(x => x.Fraction!.Value).ToArray();
            return values.Length > 0 && values.All(x => x >= 0 && x <= 1) && values.Zip(values.Skip(1)).All(x => x.First <= x.Second);
        }
        try
        {
            notes.Add("独立测试目录：" + root);
            notes.Add("只使用自行生成的资料，不读取用户实际母/子文件夹。实际数据规模约 177 MiB 加 1200 个小文件，另用 2 TiB 数值验证大容量进度和时间计算，不声称完成 1 TiB 实盘测试。");
            var pair = Pair("正常更新");
            Binary(pair.M, "更新.bin", 65, 1, 3); Binary(pair.C, "更新.bin", 64, 2, 1);
            Binary(pair.M, "新增.bin", 32, 3, 2);
            Binary(pair.M, "目录/复用.bin", 8, 4, 2); Binary(pair.C, "散落/改名.bin", 8, 4, 1);
            Put(pair.M, "子版较新.txt", "older", 1); Put(pair.C, "子版较新.txt", "new", 3);
            Put(pair.M, "同日期冲突.txt", "mother", 2); Put(pair.C, "同日期冲突.txt", "child", 2);
            Put(pair.C, "独有.txt", "extra", 1);
            for (int i = 0; i < 1200; i++) { Put(pair.M, $"小文件/{i:D4}.txt", "same" + i, 1); if (i % 3 != 0) Put(pair.C, $"小文件/{i:D4}.txt", "same" + i, 2); }
            Directory.CreateDirectory(System.IO.Path.Combine(pair.M, "空目录"));
            var before = Engine.Scan(pair.M, false);
            string oldHash = Hash(System.IO.Path.Combine(pair.C, "更新.bin"));
            var analyzeStates = new List<WorkProgress>();
            var timer = Stopwatch.StartNew();
            var plan = Engine.Analyze(pair.M, pair.C, "移到额外资料", detail: analyzeStates.Add);
            double analysisSeconds = timer.Elapsed.TotalSeconds;
            Check(plan.Items.Any(x => x.Action == "更新") && plan.Items.Any(x => x.Action == "新增") && plan.Items.Any(x => x.Action == "移动"), "自行生成的大小文件可完成对比并生成更新、补齐、移动计划");
            Check(Monotonic(analyzeStates) && analyzeStates.Last().Fraction == 1, "对比总进度单调递增，成功后达到 100%");
            var executeStates = new List<WorkProgress>(); timer.Restart();
            string record = Engine.Execute(plan, detail: executeStates.Add);
            double executionSeconds = timer.Elapsed.TotalSeconds;
            Check(Hash(System.IO.Path.Combine(pair.M, "更新.bin")) == Hash(System.IO.Path.Combine(pair.C, "更新.bin")), "65 MiB 文件成功覆盖更新");
            Check(Hash(System.IO.Path.Combine(record, "更新前备份", "更新.bin")) == oldHash, "覆盖更新前旧内容完整备份");
            Check(Hash(System.IO.Path.Combine(pair.M, "新增.bin")) == Hash(System.IO.Path.Combine(pair.C, "新增.bin")), "32 MiB 缺失文件成功补齐");
            Check(File.Exists(System.IO.Path.Combine(pair.C, "目录", "复用.bin")) && !File.Exists(System.IO.Path.Combine(pair.C, "散落", "改名.bin")), "相同内容成功移动并改名");
            Check(File.ReadAllText(System.IO.Path.Combine(pair.C, "子版较新.txt")) == "new", "保留较新的子版本");
            Check(File.ReadAllText(System.IO.Path.Combine(pair.C, "同日期冲突.txt")) == "child" && File.ReadAllText(System.IO.Path.Combine(record, "同日期冲突_母版本", "同日期冲突.txt")) == "mother", "同日期冲突双方内容保留");
            Check(Directory.GetFiles(System.IO.Path.Combine(pair.C, Engine.Extras), "独有.txt", SearchOption.AllDirectories).Length == 1, "子文件夹独有文件成功归档");
            Check(Directory.Exists(System.IO.Path.Combine(pair.C, "空目录")), "成功补齐空目录");
            Check(Directory.GetFiles(System.IO.Path.Combine(pair.C, "小文件")).Length == 1200, "1200 个小文件批量更新完成");
            var after = Engine.Scan(pair.M, false);
            Check(before.Item1.Count == after.Item1.Count && before.Item1.All(x => after.Item1[x.Key] == x.Value) && before.Item2.SetEquals(after.Item2), "母文件夹完全不变");
            Check(Monotonic(executeStates) && executeStates.Last().Fraction == 1, "执行全程总进度单调递增，成功后达到 100%");
            Check(executeStates.Any(x => x.Stage.Contains("重新校验")) && executeStates.Any(x => x.Stage.Contains("更新子文件夹")), "执行明确区分二次校验与实际更新");
            Check(executeStates.Any(x => x.CopiedBytes > 0 && x.Fraction < 1), "大文件复制途中可报告实际复制量与进度");
            Check(File.ReadAllText(System.IO.Path.Combine(record, "执行记录.txt")).Contains("本次执行完成"), "更新结束有明确完成日志");
            notes.Add($"真实临时资料运行：对比 {analysisSeconds:F2} 秒，执行（含完整复核）{executionSeconds:F2} 秒。更新记录：{record}");

            using (var cancel = new CancellationTokenSource())
            {
                bool stopped = false;
                try { Engine.Execute(plan, cancellation: cancel.Token, detail: x => { if (x.Stage.Contains("重新校验") && x.ReadBytes > 0) cancel.Cancel(); }); }
                catch (OperationCanceledException) { stopped = true; }
                Check(stopped, "执行前大文件二次校验可中断");
            }
            var interrupted = Pair("覆盖更新中断");
            Binary(interrupted.M, "a-update.bin", 16, 7, 3); Binary(interrupted.C, "a-update.bin", 8, 8, 1); Put(interrupted.M, "z-later.txt", "later", 2);
            string original = Hash(System.IO.Path.Combine(interrupted.C, "a-update.bin"));
            var interruptPlan = Engine.Analyze(interrupted.M, interrupted.C, "原处保留");
            using (var cancel = new CancellationTokenSource())
            {
                ExecutionInterruptedException? caught = null;
                try { Engine.Execute(interruptPlan, cancellation: cancel.Token, detail: x => {
                    // Slow only this test callback so cancellation deterministically occurs inside a copy,
                    // rather than after a fast local copy has already committed its completed update.
                    if (x.CopiedBytes > 9 * 1024 * 1024) cancel.Cancel();
                    else if (x.Stage.StartsWith("执行 2/2") || x.Stage == "执行：更新文件") Thread.Sleep(270);
                }); }
                catch (ExecutionInterruptedException ex) { caught = ex; }
                Check(caught != null, "覆盖大文件的复制途中可中断");
                Check(Hash(System.IO.Path.Combine(interrupted.C, "a-update.bin")) == original, "中断覆盖时原目标文件完整保留");
                Check(!Directory.EnumerateFiles(interrupted.C, "*.greenfolder-*", SearchOption.TopDirectoryOnly).Any(), "中断后清理未完成的覆盖临时文件");
                Check(!File.Exists(System.IO.Path.Combine(interrupted.C, "z-later.txt")), "中断后不再执行后续项目");
                Check(caught != null && Hash(System.IO.Path.Combine(caught.RecordPath, "更新前备份", "a-update.bin")) == original, "中断后保留已完成的旧版备份");
                Check(caught != null && File.ReadAllText(System.IO.Path.Combine(caught.RecordPath, "执行记录.txt")).Contains("已中断"), "中断日志和记录位置可追溯");
            }
            Engine.Execute(Engine.Analyze(interrupted.M, interrupted.C, "原处保留"));
            Check(Hash(System.IO.Path.Combine(interrupted.M, "a-update.bin")) == Hash(System.IO.Path.Combine(interrupted.C, "a-update.bin")) && File.Exists(System.IO.Path.Combine(interrupted.C, "z-later.txt")), "中断后重新对比可继续完成更新");

            var addition = Pair("新增文件中断"); Binary(addition.M, "new.bin", 16, 11, 1);
            var additionPlan = Engine.Analyze(addition.M, addition.C, "原处保留");
            using (var cancel = new CancellationTokenSource())
            {
                bool stopped = false;
                try { Engine.Execute(additionPlan, cancellation: cancel.Token, detail: x => {
                    if (x.CopiedBytes > 2 * 1024 * 1024) cancel.Cancel();
                    else if (x.Stage.StartsWith("执行 2/2") || x.Stage == "执行：更新文件") Thread.Sleep(270);
                }); }
                catch (ExecutionInterruptedException) { stopped = true; }
                Check(stopped && !File.Exists(System.IO.Path.Combine(addition.C, "new.bin")), "新增大文件中断后清理不完整目标文件");
            }
            var locked = Pair("源文件被占用"); Put(locked.M, "locked.txt", "locked", 1);
            var lockedPlan = Engine.Analyze(locked.M, locked.C, "原处保留");
            bool lockReported = false;
            using (var stream = new FileStream(System.IO.Path.Combine(locked.M, "locked.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                try { Engine.Execute(lockedPlan); } catch (IOException ex) { lockReported = ex.Message.Length > 0; }
            }
            Check(lockReported && !File.Exists(System.IO.Path.Combine(locked.C, "locked.txt")), "文件被占用时明确报错且不写入目标");
            Engine.Execute(Engine.Analyze(locked.M, locked.C, "原处保留"));
            Check(File.Exists(System.IO.Path.Combine(locked.C, "locked.txt")), "解除文件占用后可正常完成更新");

            WorkProgress? estimate = null;
            var meter = new WorkMeter(1000, null, x => estimate = x, () => 10);
            meter.Add(100, "测试", "测试", force: true);
            Check(estimate?.Fraction == .1 && estimate.Remaining == TimeSpan.FromSeconds(90), "预计剩余时间按已完成工作和耗时计算");
            var largeMeter = new WorkMeter(2L * 1024 * 1024 * 1024 * 1024, null, x => estimate = x, () => 10);
            largeMeter.Add(128L * 1024 * 1024, "测试", "2 TiB", force: true);
            Check(estimate?.Remaining?.TotalHours > 24 && estimate.Estimate.Contains("天"), "2 TiB 工作量进度与超过一天的剩余时间无溢出");
            var emptyPair = Pair("空文件夹"); var emptyStates = new List<WorkProgress>();
            Engine.Analyze(emptyPair.M, emptyPair.C, "原处保留", detail: emptyStates.Add);
            Check(emptyStates.Last().Fraction == 1, "空文件夹进度可正常结束");
            var space = new Plan(); space.M["a"] = new Stamp(200, 0, ""); space.C["a"] = new Stamp(100, 0, ""); space.M["b"] = new Stamp(200, 0, ""); space.C["b"] = new Stamp(100, 0, "");
            Check(Engine.RequiredSpace(space, new[] { new Change { Action = "更新", Path = "a" }, new Change { Action = "更新", Path = "b" } }) == 500 + 1024 * 1024, "空间预检查按备份和临时覆盖的峰值计算");
            notes.Add("全部独立更新实验通过。");
        }
        catch (Exception ex) { notes.Add(ex.ToString()); Environment.ExitCode = 1; }
        File.WriteAllLines(output, notes);
    }
}
