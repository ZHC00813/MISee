namespace GreenFolder;
public static class SelfTest
{
    public static void Run(string output)
    {
        var notes = new List<string>();
        string root = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(output)!, "test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string m = System.IO.Path.Combine(root, "母"), c = System.IO.Path.Combine(root, "子");
        Directory.CreateDirectory(m); Directory.CreateDirectory(c);
        void Put(string dir, string path, string content, int day)
        {
            string f = System.IO.Path.Combine(dir, path); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(f)!);
            File.WriteAllText(f, content); File.SetLastWriteTimeUtc(f, new DateTime(2026, 1, day, 0, 0, 0, DateTimeKind.Utc));
        }
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); notes.Add("PASS: " + name); }
        try
        {
            Put(m, "相同.txt", "same", 1); Put(c, "相同.txt", "same", 2);
            Put(m, "更新.txt", "newer", 3); Put(c, "更新.txt", "old", 1);
            Put(m, "保留.txt", "older", 1); Put(c, "保留.txt", "new", 3);
            Put(m, "冲突.txt", "mother", 2); Put(c, "冲突.txt", "child", 2);
            Put(m, "目录/移动.txt", "move", 1); Put(c, "散落/旧名字.txt", "move", 2);
            Put(m, "新增.txt", "add", 1); Put(c, "额外.txt", "extra", 1);
            Directory.CreateDirectory(System.IO.Path.Combine(m, "空目录"));
            Directory.CreateDirectory(System.IO.Path.Combine(c, "独有空目录"));
            var before = Engine.Scan(m, false);
            var p = Engine.Analyze(m, c, "移到额外资料");
            Check(!p.Items.Any(x => x.Path == "相同.txt"), "相同内容保持不动，忽略日期差异");
            Check(p.Items.Single(x => x.Action == "移动").From == "散落\\旧名字.txt", "按内容复用，允许改名和移动");
            string record = Engine.Execute(p);
            Check(File.ReadAllText(System.IO.Path.Combine(c, "更新.txt")) == "newer", "母版本较新时更新");
            Check(File.ReadAllText(System.IO.Path.Combine(c, "保留.txt")) == "new", "子版本较新时保留");
            Check(File.ReadAllText(System.IO.Path.Combine(record, "更新前备份/更新.txt")) == "old", "覆盖前备份旧版本");
            Check(File.ReadAllText(System.IO.Path.Combine(record, "同日期冲突_母版本/冲突.txt")) == "mother" && File.ReadAllText(System.IO.Path.Combine(c, "冲突.txt")) == "child", "同日期冲突保留双方");
            Check(File.Exists(System.IO.Path.Combine(c, "目录/移动.txt")) && !File.Exists(System.IO.Path.Combine(c, "散落/旧名字.txt")), "移动执行");
            Check(Directory.Exists(System.IO.Path.Combine(c, "空目录")), "补齐空目录");
            Check(Directory.GetFiles(System.IO.Path.Combine(c, Engine.Extras), "额外.txt", SearchOption.AllDirectories).Length == 1, "归档独有文件");
            Check(Directory.GetDirectories(System.IO.Path.Combine(c, Engine.Extras), "独有空目录", SearchOption.AllDirectories).Length == 1, "归档独有空目录");
            Check(Engine.Scan(m, false).Item1.All(x => before.Item1[x.Key] == x.Value), "母文件夹未改变");
            var next = Engine.Analyze(m, c, "原处保留");
            Check(!next.Items.Any(x => x.Path.StartsWith(Engine.Extras) || x.Path.StartsWith(Engine.Records)), "程序记录和归档不参与重复对比");
            Put(c, "新额外.txt", "keep", 1);
            Check(Engine.Analyze(m, c, "原处保留").Items.Any(x => x.Path == "新额外.txt" && x.Action == "保留"), "独有资料原处保留");
            Check(Engine.Analyze(m, c, "删除到回收站").Items.Any(x => x.Path == "新额外.txt" && x.Action == "回收"), "删除模式计划正确");
            var stale = Engine.Analyze(m, c, "原处保留"); Put(c, "变更.txt", "changed", 1);
            bool rejected = false; try { Engine.Execute(stale); } catch { rejected = true; } Check(rejected, "执行前拒绝过期计划");
            rejected = false; try { Engine.Validate(m, System.IO.Path.Combine(m, "空目录")); } catch { rejected = true; } Check(rejected, "拒绝嵌套文件夹");
            Put(m, "类型冲突", "file", 1); Directory.CreateDirectory(System.IO.Path.Combine(c, "类型冲突"));
            rejected = false; try { Engine.Analyze(m, c, "原处保留"); } catch { rejected = true; } Check(rejected, "文件与目录冲突被拦截");
            notes.Add("全部测试通过。");
        }
        catch (Exception ex) { notes.Add(ex.ToString()); Environment.ExitCode = 1; }
        File.WriteAllLines(output, notes);
    }
}
