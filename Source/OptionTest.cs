namespace GreenFolder;

internal static class OptionTest
{
    public static void Run(string output)
    {
        var notes = new List<string>();
        string root = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(output)!, "option-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); notes.Add("PASS: " + name); }
        (string M, string C) Pair(string name)
        {
            string m = System.IO.Path.Combine(root, name, "母"), c = System.IO.Path.Combine(root, name, "子");
            Directory.CreateDirectory(m); Directory.CreateDirectory(c); return (m, c);
        }
        void Put(string dir, string name, string content, int day = 1)
        {
            string path = System.IO.Path.Combine(dir, name); File.WriteAllText(path, content);
            File.SetLastWriteTimeUtc(path, new DateTime(2026, 1, day, 0, 0, 0, DateTimeKind.Utc));
        }
        try
        {
            var pair = Pair("跳过复核");
            Put(pair.M, "same.txt", "same"); Put(pair.C, "same.txt", "same");
            Put(pair.M, "update.txt", "new", 3); Put(pair.C, "update.txt", "old", 1); Put(pair.M, "add.txt", "added");
            var plan = Engine.Analyze(pair.M, pair.C, "原处保留");
            var states = new List<WorkProgress>();
            bool refused = false;
            using (var locked = new FileStream(System.IO.Path.Combine(pair.M, "same.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                try { Engine.Execute(plan); } catch (IOException) { refused = true; }
                Check(refused, "旧调用签名默认仍进行整库复核，遇到无关文件占用会拒绝");
                Put(pair.C, "unrelated.txt", "keep");
                string record = Engine.Execute(plan, revalidate: false, detail: states.Add);
                Check(File.ReadAllText(System.IO.Path.Combine(pair.C, "update.txt")) == "new" && File.ReadAllText(System.IO.Path.Combine(pair.C, "add.txt")) == "added", "关闭整库复核后可直接更新并新增文件");
                Check(File.ReadAllText(System.IO.Path.Combine(record, "更新前备份", "update.txt")) == "old", "关闭复核仍保留覆盖前备份");
                Check(File.ReadAllText(System.IO.Path.Combine(record, "执行记录.txt")).Contains("更新前重新校验：关闭"), "日志明确记录复核关闭模式");
            }
            Check(states.All(x => x.ReadBytes == 0 && !x.Stage.Contains("重新校验")) && states.Last().Fraction == 1, "关闭复核不重复读取整库内容，直接更新且进度正常结束");
            Check(File.ReadAllText(System.IO.Path.Combine(pair.C, "unrelated.txt")) == "keep", "关闭复核不触碰对比后出现的无关子文件");
            var changed = Pair("待更新子文件变化"); Put(changed.M, "a.txt", "new", 3); Put(changed.C, "a.txt", "old");
            var stale = Engine.Analyze(changed.M, changed.C, "原处保留"); Put(changed.C, "a.txt", "CHANGED", 2);
            refused = false; try { Engine.Execute(stale, false); } catch (Exception ex) { refused = ex.Message.Contains("发生变化"); }
            Check(refused && File.ReadAllText(System.IO.Path.Combine(changed.C, "a.txt")) == "CHANGED", "关闭复核仍拒绝覆盖大小或时间已变化的目标");
            var source = Pair("源文件变化"); Put(source.M, "new.txt", "initial"); var sourcePlan = Engine.Analyze(source.M, source.C, "原处保留"); Put(source.M, "new.txt", "changed-size", 2);
            refused = false; try { Engine.Execute(sourcePlan, false); } catch (Exception ex) { refused = ex.Message.Contains("发生变化"); }
            Check(refused && !File.Exists(System.IO.Path.Combine(source.C, "new.txt")), "关闭复核仍拒绝复制已变化的源文件");
            var exists = Pair("新增目标被占用"); Put(exists.M, "a.txt", "source"); var existsPlan = Engine.Analyze(exists.M, exists.C, "原处保留"); Put(exists.C, "a.txt", "keep-this");
            refused = false; try { Engine.Execute(existsPlan, false); } catch (Exception ex) { refused = ex.Message.Contains("已被创建"); }
            Check(refused && File.ReadAllText(System.IO.Path.Combine(exists.C, "a.txt")) == "keep-this", "关闭复核仍拒绝覆盖对比后新出现的目标");
            var limit = Pair("模式边界"); Put(limit.M, "a.txt", "AAAA"); var limitPlan = Engine.Analyze(limit.M, limit.C, "原处保留"); Put(limit.M, "a.txt", "BBBB");
            Engine.Execute(limitPlan, false);
            Check(File.ReadAllText(System.IO.Path.Combine(limit.C, "a.txt")) == "BBBB", "确认并记录关闭复核的边界：同大小同时间的内容变化不保证被检测");
            var cancelled = Pair("复制中断"); File.WriteAllBytes(System.IO.Path.Combine(cancelled.M, "large.bin"), new byte[8 * 1024 * 1024]);
            var cancelPlan = Engine.Analyze(cancelled.M, cancelled.C, "原处保留");
            using var cancel = new CancellationTokenSource();
            refused = false;
            try { Engine.Execute(cancelPlan, false, cancellation: cancel.Token, detail: x => {
                if (x.CopiedBytes > 0) cancel.Cancel();
                else if (x.Stage.Contains("更新子文件夹")) Thread.Sleep(270);
            }); } catch (ExecutionInterruptedException) { refused = true; }
            Check(refused && !File.Exists(System.IO.Path.Combine(cancelled.C, "large.bin")), "关闭复核后仍能中断复制并清理半成品");
            notes.Add("全部可选复核测试通过。测试目录：" + root);
        }
        catch (Exception ex) { notes.Add(ex.ToString()); Environment.ExitCode = 1; }
        File.WriteAllLines(output, notes);
    }
}
