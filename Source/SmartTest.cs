using System.Diagnostics;
using System.Security.Cryptography;

namespace GreenFolder;

internal static class SmartTest
{
    public static void Run(string output)
    {
        var notes = new List<string>();
        string root = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(output)!, "smart-" + Guid.NewGuid().ToString("N"));
        string previous = Engine.IndexDirectory;
        Directory.CreateDirectory(root); Engine.CloseIndexes(); Engine.IndexDirectory = System.IO.Path.Combine(root, "本机索引");
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); notes.Add("PASS: " + name); }
        (string M, string C) Pair(string name)
        { Engine.CloseIndexes(); string m = System.IO.Path.Combine(root, name, "母"), c = System.IO.Path.Combine(root, name, "子"); Directory.CreateDirectory(m); Directory.CreateDirectory(c); return (m, c); }
        void Put(string dir, string rel, string data, int day = 1)
        { string path = System.IO.Path.Combine(dir, rel); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!); File.WriteAllText(path, data); File.SetLastWriteTimeUtc(path, new DateTime(2026,1,day,0,0,0,DateTimeKind.Utc)); }
        string Hash(string path) { using var s = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(s)); }
        string[] Signature(Plan p) => p.Items.Select(x => x.Action + "|" + x.Path + "|" + x.From).Order().ToArray();
        var quick = new CompareOptions(); var cached = new CompareOptions(ComparisonKind.CachedContent); var full = new CompareOptions(ComparisonKind.FullContent);
        try
        {
            var pair = Pair("局部操作");
            Put(pair.M, "nested\\update.txt", "newer", 3); Put(pair.C, "nested\\update.txt", "old");
            Put(pair.M, "same.txt", "same"); Put(pair.C, "same.txt", "same");
            Put(pair.M, "add.txt", "added-long"); Put(pair.M, "moved.txt", "move"); Put(pair.C, "old-name.txt", "move");
            Put(pair.M, "newer.txt", "older"); Put(pair.C, "newer.txt", "new", 3);
            Put(pair.C, "unique.txt", "keep-this-unique");
            var originalM = Directory.GetFiles(pair.M, "*", SearchOption.AllDirectories).ToDictionary(x => x, Hash);
            var p = Engine.Analyze(pair.M, pair.C, "原处保留", quick);
            Check(Signature(p).SequenceEqual(Signature(Engine.Analyze(pair.M, pair.C, "原处保留"))), "普通大小/时间变化的快速计划与完整内容计划一致");
            Check(p.Items.Any(x => x.Action == "移动" && x.From == "old-name.txt"), "只对同大小复用候选按需校验并识别改名");
            var noMoves = Engine.Analyze(pair.M, pair.C, "原处保留", quick with { ReuseMoves = false });
            Check(noMoves.Items.Any(x => x.Action == "新增" && x.Path == "moved.txt") && noMoves.Items.Any(x => x.Path == "old-name.txt" && x.Action == "保留"), "关闭复用后选择复制并原处保留旧路径");
            var work = new List<WorkProgress>();
            string record = Engine.Execute(p, new ExecutionOptions(), detail: work.Add);
            Check(File.ReadAllText(System.IO.Path.Combine(pair.C, "nested\\update.txt")) == "newer" && File.ReadAllText(System.IO.Path.Combine(pair.C, "add.txt")) == "added-long", "快速比对后完成局部新增和更新");
            Check(File.ReadAllText(System.IO.Path.Combine(record, "更新前备份\\nested\\update.txt")) == "old", "覆盖前备份并核验原版本");
            Check(File.ReadAllText(System.IO.Path.Combine(pair.C, "unique.txt")) == "keep-this-unique" && File.ReadAllText(System.IO.Path.Combine(pair.C, "newer.txt")) == "new", "独有资料和较新子版本保留");
            Check(originalM.All(x => Hash(x.Key) == x.Value), "母本内容始终未修改");
            Check(work.Any(x => x.Stage == "校验实际变更内容" && x.ReadBytes > 0) && work.Last().Fraction == 1, "实际写入内容校验有进度并正常结束");
            Check(File.ReadAllText(System.IO.Path.Combine(record, "执行记录.txt")).Contains("更新后内容校验：开启"), "日志记录快速方式和写入校验开关");
            var after = Engine.Analyze(pair.M, pair.C, "原处保留", quick);
            Check(after.Items.All(x => !x.Selected), "执行后再次快速比对没有重复更新");

            pair = Pair("目录时间不能代替文件变更");
            Put(pair.M, "a\\b\\file.txt", "AAAA"); Put(pair.C, "a\\b\\file.txt", "AAAA");
            Engine.Analyze(pair.M, pair.C, "原处保留", cached);
            var stable = Engine.Analyze(pair.M, pair.C, "原处保留", cached);
            for(int settling=0; settling<4 && (Engine.LastScan.EnumeratedFiles!=0||Engine.LastScan.ReadBytes!=0); settling++)
            {
                notes.Add("初始监控正在收敛，已安全回退："+Engine.LastScan);
                Thread.Sleep(250);
                stable=Engine.Analyze(pair.M,pair.C,"原处保留",cached);
            }
            Check(Engine.LastScan.EnumeratedFiles == 0 && Engine.LastScan.ReadBytes == 0 && stable.Items.Count == 0, "监控稳定后无变化比对跳过文件元数据读取与内容读取："+Engine.LastScan+"；差异="+stable.Items.Count);
            string folder = System.IO.Path.Combine(pair.M, "a"); DateTime folderTime = Directory.GetLastWriteTimeUtc(folder);
            Put(pair.M, "a\\b\\file.txt", "BBBBB", 3); Directory.SetLastWriteTimeUtc(folder, folderTime);
            Thread.Sleep(250);
            var changed = Engine.Analyze(pair.M, pair.C, "原处保留", cached);
            Check(changed.Items.Any(x => x.Action == "更新") && Directory.GetLastWriteTimeUtc(folder) == folderTime, "祖先文件夹时间未变时仍能发现深层文件内容更新");
            Check(Engine.LastScan.HashedFiles == 1 && Engine.LastScan.EnumeratedFiles < 2, "深层单文件修改只读取其新内容并复用另一边");
            Put(pair.M, "a\\b\\file.txt", "CCCCC", 3); Thread.Sleep(250);
            var sameStamp = Engine.Analyze(pair.M, pair.C, "原处保留", cached);
            Check(sameStamp.M["a\\b\\file.txt"].Hash == Hash(System.IO.Path.Combine(pair.M, "a\\b\\file.txt")) && Engine.LastScan.HashedFiles == 1, "在线变更通知使同大小同时间修改的旧指纹失效");
            Directory.Move(System.IO.Path.Combine(pair.M, "a"), System.IO.Path.Combine(pair.M, "renamed")); Thread.Sleep(250);
            var renamed = Engine.Analyze(pair.M, pair.C, "原处保留", cached);
            Check(renamed.M.ContainsKey("renamed\\b\\file.txt") && !renamed.M.ContainsKey("a\\b\\file.txt"), "目录改名刷新新子树并删除旧路径索引");
            Directory.Delete(System.IO.Path.Combine(pair.M, "renamed"), true); Thread.Sleep(250);
            var deleted = Engine.Analyze(pair.M, pair.C, "原处保留", cached);
            Check(deleted.M.Count == 0 && deleted.MD.Count == 0, "目录删除不会留下幽灵文件和目录");
            Put(pair.M, "replace\\old.txt", "old"); Thread.Sleep(250); Engine.Analyze(pair.M, pair.C, "原处保留", cached);
            Directory.Delete(System.IO.Path.Combine(pair.M, "replace"), true); Put(pair.M, "replace\\new.txt", "new"); Thread.Sleep(250);
            var replaced = Engine.Analyze(pair.M, pair.C, "原处保留", cached);
            Check(replaced.M.ContainsKey("replace\\new.txt") && !replaced.M.ContainsKey("replace\\old.txt"), "同名目录删除再建也会重查内容，不能因目录已在索引中而跳过");
            Engine.InvalidateIndex(pair.C);
            Engine.Analyze(pair.M, pair.C, "原处保留", cached);
            Check(Engine.LastScan.EnumeratedFiles == 1 && Engine.LastScan.HashedFiles == 1, "通知丢失时退回元数据扫描并拒绝信任旧指纹");

            pair = Pair("同时间内容冲突"); Put(pair.M, "a.txt", "AAAA"); Put(pair.C, "a.txt", "BBBB");
            var fastConflict = Engine.Analyze(pair.M, pair.C, "原处保留", quick);
            Check(fastConflict.Items.Count == 0 && Engine.LastScan.ReadBytes == 0, "明确快速模式边界：无指纹的同大小同时间文件按一致处理");
            bool refused = false;
            try { Engine.Execute(fastConflict, new ExecutionOptions(true)); } catch(IOException ex) { refused = ex.Message.Contains("内容冲突"); }
            Check(!refused, "完整复核遵守同路径同大小规则，不因两边内容不同停止");
            var fullConflict = Engine.Analyze(pair.M, pair.C, "原处保留", full);
            Check(fullConflict.Items.Count == 0 && Engine.LastScan.HashedFiles == 2, "完整内容模式重读指纹但同大小仍按一致处理");
            var cachedConflict = Engine.Analyze(pair.M, pair.C, "原处保留", quick);
            Check(cachedConflict.Items.Count == 0 && Engine.LastScan.ReadBytes == 0, "快速模式即使已有不同指纹也遵守同大小规则");
            record = Engine.Execute(cachedConflict, new ExecutionOptions());
            Check(File.ReadAllText(System.IO.Path.Combine(pair.C, "a.txt")) == "BBBB" && !Directory.Exists(System.IO.Path.Combine(record, "同日期冲突_母版本")), "同大小文件保持子本内容，不生成冲突副本");

            pair = Pair("持久索引与恢复"); Put(pair.M, "a.txt", "persist"); Put(pair.C, "a.txt", "persist");
            Engine.Analyze(pair.M, pair.C, "原处保留", cached); Engine.CloseIndexes();
            var restart = Engine.Analyze(pair.M, pair.C, "原处保留", cached);
            Check(restart.Items.Count == 0 && Engine.LastScan.HashedFiles == 0 && Engine.LastScan.HashHits == 2, "程序重开后可复用持久指纹，变更日志不可用时仅重扫元数据");
            Engine.Analyze(pair.M, pair.C, "原处保留", cached with { ReuseHashes = false });
            Check(Engine.LastScan.HashedFiles == 2, "关闭指纹缓存后按所选内容模式重新读取");
            Engine.CloseIndexes(); foreach(string f in Directory.GetFiles(Engine.IndexDirectory, "*.json")) File.WriteAllText(f, "{corrupt");
            Engine.Analyze(pair.M, pair.C, "原处保留", cached);
            Check(Engine.LastScan.HashedFiles == 2, "损坏的持久索引自动舍弃并重建");
            Engine.Analyze(pair.M, pair.C, "原处保留", quick with { TrackChanges = false });
            Check(Engine.LastScan.EnumeratedFiles == 2 && Engine.LastScan.ReadBytes == 0, "关闭实时索引仍采用快速元数据扫描");
            Put(pair.M, "locked.txt", "not-cached"); Thread.Sleep(250);
            using(var locked = new FileStream(System.IO.Path.Combine(pair.M, "locked.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                bool failed = false; try { Engine.Analyze(pair.M, pair.C, "原处保留", cached); } catch(IOException) { failed = true; }
                Check(failed, "索引刷新遇到被独占的文件时明确失败");
            }
            var retried = Engine.Analyze(pair.M, pair.C, "原处保留", cached);
            Check(retried.M.ContainsKey("locked.txt") && retried.M["locked.txt"].Hash.Length == 64, "刷新失败后不丢失待处理变更，重试会重建有效索引");
            foreach(string f in Directory.GetFiles(Engine.IndexDirectory, "*.json")) File.SetAttributes(f, FileAttributes.ReadOnly);
            try { var safe = Engine.Analyze(pair.M, pair.C, "原处保留", cached); Check(safe.M.ContainsKey("locked.txt"), "持久缓存文件只读时不影响比对结果"); }
            finally { foreach(string f in Directory.GetFiles(Engine.IndexDirectory, "*.json")) File.SetAttributes(f, FileAttributes.Normal); }

            pair = Pair("写入前后保护"); Put(pair.M, "a.txt", "AAAA");
            p = Engine.Analyze(pair.M, pair.C, "原处保留", cached); Put(pair.M, "a.txt", "BBBB");
            refused = false; try { Engine.Execute(p, new ExecutionOptions()); } catch(Exception ex) { refused = ex.ToString().Contains("源内容与比对指纹不同"); }
            Check(refused && !File.Exists(System.IO.Path.Combine(pair.C, "a.txt")), "写入校验检测同大小同时间源内容变化并清理未完成目标");
            Engine.CloseIndexes(); Put(pair.M, "a.txt", "new-version", 3); Put(pair.C, "a.txt", "old", 1);
            p = Engine.Analyze(pair.M, pair.C, "原处保留", quick); Put(pair.C, "a.txt", "newer-child", 4);
            refused = false; try { Engine.Execute(p, new ExecutionOptions()); } catch(Exception ex) { refused = ex.ToString().Contains("发生变化"); }
            Check(refused && File.ReadAllText(System.IO.Path.Combine(pair.C, "a.txt")) == "newer-child", "跳过整库复核仍拒绝覆盖对比后变新的子版本");
            Put(pair.M, "large.bin", new string('x', 4 * 1024 * 1024));
            p = Engine.Analyze(pair.M, pair.C, "原处保留", quick);
            using var cancel = new CancellationTokenSource(); refused = false;
            try { Engine.Execute(p, new ExecutionOptions(), cancellation: cancel.Token, detail: x => { if(x.CopiedBytes > 0) cancel.Cancel(); else if(x.Stage.Contains("更新子文件夹")) Thread.Sleep(270); }); } catch(ExecutionInterruptedException) { refused = true; }
            Check(refused && !File.Exists(System.IO.Path.Combine(pair.C, "large.bin")), "快速执行仍可中断并清理复制半成品");

            pair = Pair("读取量实测");
            byte[] data = new byte[32 * 1024 * 1024]; new Random(31).NextBytes(data);
            File.WriteAllBytes(System.IO.Path.Combine(pair.M, "large.bin"), data); File.WriteAllBytes(System.IO.Path.Combine(pair.C, "large.bin"), data);
            File.SetLastWriteTimeUtc(System.IO.Path.Combine(pair.M, "large.bin"), new DateTime(2026,1,1)); File.SetLastWriteTimeUtc(System.IO.Path.Combine(pair.C, "large.bin"), new DateTime(2026,1,1));
            for(int i=0;i<3000;i++) { Put(pair.M, $"{i/100:D2}\\{i:D4}.txt", "same"); Put(pair.C, $"{i/100:D2}\\{i:D4}.txt", "same"); }
            var watch = Stopwatch.StartNew(); Engine.Analyze(pair.M, pair.C, "原处保留", full); double fullSeconds = watch.Elapsed.TotalSeconds; long fullBytes = Engine.LastScan.ReadBytes;
            Engine.CloseIndexes(); watch.Restart(); Engine.Analyze(pair.M, pair.C, "原处保留", quick); double quickSeconds = watch.Elapsed.TotalSeconds; long quickBytes = Engine.LastScan.ReadBytes;
            watch.Restart(); Engine.Analyze(pair.M, pair.C, "原处保留", quick); double warmSeconds = watch.Elapsed.TotalSeconds;
            for(int settling=0;settling<4 && Engine.LastScan.EnumeratedFiles!=0;settling++) {Thread.Sleep(250);watch.Restart();Engine.Analyze(pair.M,pair.C,"原处保留",quick);warmSeconds=watch.Elapsed.TotalSeconds;}
            Check(fullBytes >= 64 * 1024 * 1024 && quickBytes == 0 && Engine.LastScan.EnumeratedFiles == 0, "6002 文件实验：完整比对读取至少 64MiB，快速零内容读取，再次比对零元数据读取");
            notes.Add($"实测（本机缓存会影响秒数，不外推 TB 耗时）：完整 {fullSeconds:F3}s / {fullBytes:N0} bytes；快速冷启动 {quickSeconds:F3}s / {quickBytes} bytes；再次增量 {warmSeconds:F3}s / {Engine.LastScan.EnumeratedFiles} 个元数据检查。");
            pair = Pair("母本包含缓存位置"); Put(pair.M, "a.txt", "read-only-source");
            string normalCache = Engine.IndexDirectory; Engine.IndexDirectory = System.IO.Path.Combine(pair.M, "cache-must-not-be-created");
            try { Engine.Analyze(pair.M, pair.C, "原处保留", quick); Check(!Directory.Exists(Engine.IndexDirectory), "所选母本包含缓存位置时禁写缓存，母本只读保护仍成立"); }
            finally { Engine.CloseIndexes(); Engine.IndexDirectory = normalCache; }
            notes.Add("全部智能索引与局部更新测试通过。独立测试目录：" + root);
        }
        catch(Exception ex) { notes.Add(ex.ToString()); Environment.ExitCode = 1; }
        finally { Engine.CloseIndexes(); Engine.IndexDirectory = previous; File.WriteAllLines(output, notes); }
    }
}

