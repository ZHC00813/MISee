using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.VisualBasic.FileIO;
using System.Buffers;
using System.Diagnostics;

namespace GreenFolder;

public record Stamp(long Size, long Time, string Hash);
public class Change
{
    public bool Selected { get; set; } = true;
    public string Action { get; set; } = "";
    public string Path { get; set; } = "";
    public string Detail { get; set; } = "";
    public string From { get; set; } = "";
}
public class Plan
{
    public string Master = "", Child = "";
    public Dictionary<string, Stamp> M = new(StringComparer.OrdinalIgnoreCase), C = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> MD = new(StringComparer.OrdinalIgnoreCase), CD = new(StringComparer.OrdinalIgnoreCase);
    public List<Change> Items = new();
    public string Mode = "";
    public bool QuickComparison;
    public string ScanDescription = "完整内容比对";
}
public static partial class Engine
{
    public const string Records = ".MISee记录", Extras = "额外资料（MISee）", Marker = ".greenfolder-owned";
    static bool Managed(string name) => name.Equals(Records, StringComparison.OrdinalIgnoreCase) || name.Equals(Extras, StringComparison.OrdinalIgnoreCase) || name.Equals(".绿序记录", StringComparison.OrdinalIgnoreCase) || name.Equals("额外资料（绿序）", StringComparison.OrdinalIgnoreCase);
    static string Full(string p) => System.IO.Path.GetFullPath(p).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
    public static void Validate(string m, string c)
    {
        if (!Directory.Exists(m) || !Directory.Exists(c)) throw new Exception("请先选择两个有效的文件夹。");
        if (Full(m).StartsWith(Full(c), StringComparison.OrdinalIgnoreCase) || Full(c).StartsWith(Full(m), StringComparison.OrdinalIgnoreCase))
            throw new Exception("母文件夹和子文件夹不能相同，也不能互相包含。");
    }
    sealed record Inventory(string Root, bool Child, List<(string Path, long Size, long Time)> Files, HashSet<string> Dirs)
    {
        public long Work => Files.Sum(x => x.Size + WorkMeter.ItemUnit);
    }
    static Inventory Enumerate(string root, bool child, Action<string>? progress, Action<WorkProgress>? detail, CancellationToken cancellation, string? subtree = null)
    {
        var files = new List<(string Path, long Size, long Time)>();
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var timer = Stopwatch.StartNew();
        long lastReport = -1000;
        cancellation.ThrowIfCancellationRequested();
        WorkMeter.Counting(root, progress, detail);
        void Walk(string dir)
        {
            foreach (var entry in new DirectoryInfo(dir).EnumerateFileSystemInfos())
            {
                cancellation.ThrowIfCancellationRequested();
                string p = entry.FullName;
                var attr = entry.Attributes;
                if ((attr & FileAttributes.ReparsePoint) != 0) throw new Exception("暂不处理符号链接或联接点：" + p);
                string rel = System.IO.Path.GetRelativePath(root, p);
                if (!rel.Contains(System.IO.Path.DirectorySeparatorChar) && Managed(rel))
                {
                    if (child && (attr & FileAttributes.Directory) != 0 && File.Exists(System.IO.Path.Combine(p, Marker))) continue;
                    throw new Exception("文件夹中存在程序保留名称，请先改名：" + p);
                }
                if (timer.ElapsedMilliseconds - lastReport >= 250)
                { lastReport = timer.ElapsedMilliseconds; WorkMeter.Counting($"{(child ? "子" : "母")}文件夹 · {files.Count:N0} 个文件 · {rel}", progress, detail); }
                if ((attr & FileAttributes.Directory) != 0) { dirs.Add(rel); Walk(p); }
                else
                {
                    files.Add((rel, ((FileInfo)entry).Length, entry.LastWriteTimeUtc.Ticks));
                }
            }
        }
        string start = subtree == null ? root : System.IO.Path.Combine(root, subtree);
        if ((File.GetAttributes(start) & FileAttributes.ReparsePoint) != 0) throw new IOException("暂不处理符号链接或联接点：" + start);
        if (subtree != null) dirs.Add(subtree);
        Walk(start); return new Inventory(root, child, files, dirs);
    }
    static (Dictionary<string, Stamp>, HashSet<string>) HashInventory(Inventory inventory, WorkMeter meter, string phase, CancellationToken cancellation)
    {
        var files = new Dictionary<string, Stamp>(StringComparer.OrdinalIgnoreCase);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(256 * 1024);
        try
        {
            foreach (var item in inventory.Files)
            {
                    cancellation.ThrowIfCancellationRequested();
                    string p = System.IO.Path.Combine(inventory.Root, item.Path);
                    using var stream = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan);
                    var info = new FileInfo(p);
                    long size = info.Length, time = info.LastWriteTimeUtc.Ticks;
                    if (size != item.Size || time != item.Time) throw new IOException("统计后文件发生变化，请重新分析：" + p);
                    string current = $"{(inventory.Child ? "子" : "母")}文件夹 · {item.Path}";
                    meter.Add(0, phase, current, force: true);
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    int count;
                    while ((count = stream.Read(buffer, 0, buffer.Length)) != 0)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        hash.AppendData(buffer, 0, count);
                        meter.Add(count, phase, current, readBytes: count);
                    }
                    info.Refresh();
                    if (info.Length != size || info.LastWriteTimeUtc.Ticks != time)
                        throw new IOException("校验期间文件发生变化，请重新分析：" + p);
                    files[item.Path] = new Stamp(size, time, Convert.ToHexString(hash.GetHashAndReset()));
                    meter.Add(WorkMeter.ItemUnit, phase, current);
            }
            return (files, inventory.Dirs);
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }
    public static (Dictionary<string, Stamp>, HashSet<string>) Scan(string root, bool child, Action<string>? progress = null, CancellationToken cancellation = default, Action<WorkProgress>? detail = null)
    {
        var inventory = Enumerate(root, child, progress, detail, cancellation);
        var meter = new WorkMeter(inventory.Work, progress, detail);
        var result = HashInventory(inventory, meter, "内容校验", cancellation);
        cancellation.ThrowIfCancellationRequested(); meter.Finish("内容校验完成"); return result;
    }
    public static Plan Analyze(string master, string child, string mode, Action<string>? progress = null, CancellationToken cancellation = default, Action<WorkProgress>? detail = null)
    {
        Validate(master, child);
        var p = new Plan { Master = System.IO.Path.GetFullPath(master), Child = System.IO.Path.GetFullPath(child), Mode = mode };
        var masterFiles = Enumerate(p.Master, false, progress, detail, cancellation);
        var childFiles = Enumerate(p.Child, true, progress, detail, cancellation);
        var meter = new WorkMeter(masterFiles.Work + childFiles.Work + 1, progress, detail);
        (p.M, p.MD) = HashInventory(masterFiles, meter, "对比：内容校验", cancellation);
        (p.C, p.CD) = HashInventory(childFiles, meter, "对比：内容校验", cancellation);
        progress?.Invoke("正在建立内容索引并生成差异清单…");
        meter.Add(0, "对比：生成差异清单", "建立内容索引", force: true);
        BuildItems(p, cancellation);
        cancellation.ThrowIfCancellationRequested(); meter.Finish("对比完成");
        return p;
    }
    internal static void BuildItems(Plan p, CancellationToken cancellation = default)
    {
        if (p.M.Keys.Any(p.CD.Contains) || p.MD.Any(p.C.ContainsKey)) throw new Exception("存在文件与文件夹同名冲突。请先手动调整名称，再重新对比。");
        void Add(string a, string path, string detail, string from = "") => p.Items.Add(new Change { Action = a, Path = path, Detail = detail, From = from, Selected = a != "保留" });
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var reusable = new Dictionary<(long Size, string Hash), Queue<string>>();
        foreach (var (rel, stamp) in p.C)
        {
            cancellation.ThrowIfCancellationRequested();
            if (p.M.ContainsKey(rel) || stamp.Hash.Length == 0) continue;
            var key = (stamp.Size, stamp.Hash);
            if (!reusable.TryGetValue(key, out var queue)) reusable[key] = queue = new Queue<string>();
            queue.Enqueue(rel);
        }
        foreach (string dir in p.MD.OrderBy(x => x.Length)) if (!p.CD.Contains(dir)) Add("建目录", dir, "补齐母文件夹中的目录");
        foreach (var (rel, ms) in p.M.OrderBy(x => x.Key))
        {
            cancellation.ThrowIfCancellationRequested();
            if (p.C.TryGetValue(rel, out var cs))
            {
                if (ms.Size == cs.Size) continue; // Same relative path and byte length are considered identical.
                if (ms.Time > cs.Time) Add("更新", rel, "母文件夹较新；原文件先备份");
                else if (ms.Time < cs.Time) Add("保留", rel, "子文件夹较新，保留现有版本");
                else Add("冲突", rel, "日期相同但内容不同；保留子版本，将母版本另存到记录目录");
            }
            else
            {
                if (reusable.TryGetValue((ms.Size, ms.Hash), out var matches) && matches.TryDequeue(out var match))
                { used.Add(match); Add("移动", rel, "使用子文件夹已有资料：" + match, match); }
                else Add("新增", rel, "从母文件夹补充缺失文件");
            }
        }
        foreach (var rel in p.C.Keys.Where(x => !p.M.ContainsKey(x) && !used.Contains(x)))
            Add(p.Mode == "原处保留" ? "保留" : p.Mode == "删除到回收站" ? "回收" : "归档", rel, "子文件夹独有资料 · " + p.Mode);
        // Remove only directories that will be empty; files stay governed by their individual rows.
        if (p.Mode != "原处保留") foreach (var rel in p.CD.Where(x => !p.MD.Contains(x)).OrderByDescending(x => x.Length))
            Add("清空目录", rel, "仅在目录已空时处理；独有空目录也遵循额外资料设置");
    }
    static bool Same(Dictionary<string, Stamp> a, Dictionary<string, Stamp> b) => a.Count == b.Count && a.All(x => b.TryGetValue(x.Key, out var s) && s.Size == x.Value.Size && s.Time == x.Value.Time && (x.Value.Hash.Length == 0 || s.Hash == x.Value.Hash));
    static string EnsureOwned(string root, string name)
    {
        string dir = System.IO.Path.Combine(root, name);
        if (File.Exists(dir) || Directory.Exists(dir) && !File.Exists(System.IO.Path.Combine(dir, Marker))) throw new Exception("保留目录被其他资料占用：" + dir);
        Directory.CreateDirectory(dir); File.WriteAllText(System.IO.Path.Combine(dir, Marker), "MISee v0.6"); return dir;
    }
    static string Copy(string from, string to, WorkMeter meter, string current, CancellationToken cancellation, bool verify = false, string expected = "")
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(to)!);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);
        bool created = false;
        try
        {
            cancellation.ThrowIfCancellationRequested();
            using var source = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan);
            var timestamp = File.GetLastWriteTimeUtc(from);
            long sourceLength = source.Length;
            using var hash = verify ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256) : null;
            using (var destination = new FileStream(to, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.SequentialScan))
            {
                created = true;
                int count;
                while ((count = source.Read(buffer, 0, buffer.Length)) != 0)
                {
                    cancellation.ThrowIfCancellationRequested();
                    destination.Write(buffer, 0, count);
                    hash?.AppendData(buffer, 0, count);
                    meter.Add(count, "执行：更新文件", current, copiedBytes: count);
                }
                destination.Flush();
            }
            cancellation.ThrowIfCancellationRequested();
            string content = hash == null ? "" : Convert.ToHexString(hash.GetHashAndReset());
            if (source.Length != sourceLength || File.GetLastWriteTimeUtc(from) != timestamp) throw new IOException("复制期间源文件发生变化：" + from);
            if (verify && expected.Length > 0 && expected != content) throw new IOException("源内容与比对指纹不同，请重新对比：" + from);
            if (verify && ReadHash(to, meter, current, cancellation) != content) throw new IOException("复制后内容校验失败：" + to);
            File.SetLastWriteTimeUtc(to, timestamp);
            return content;
        }
        catch
        {
            if (created) { try { File.Delete(to); } catch (Exception cleanup) { throw new IOException("复制中断后无法清理未完成文件：" + to, cleanup); } }
            throw;
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }
    static void Move(string from, string to)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(to)!); File.Move(from, to, false);
    }
    static long CopyBytes(Plan p, Change row) => row.Action switch
    {
        "新增" or "冲突" => p.M[row.Path].Size,
        "更新" => p.M[row.Path].Size + p.C[row.Path].Size,
        _ => 0
    };
    internal static long RequiredSpace(Plan p, IEnumerable<Change> rows)
    {
        long extra = 0, peak = 0;
        foreach (var row in rows)
        {
            peak = Math.Max(peak, extra + CopyBytes(p, row));
            extra += row.Action is "新增" or "冲突" or "更新" ? p.M[row.Path].Size : 0;
        }
        return peak + 1024 * 1024;
    }
    public static string Execute(Plan p, Action<string>? progress = null, CancellationToken cancellation = default, Action<WorkProgress>? detail = null)
        => Execute(p, true, progress, cancellation, detail);
    static void CheckPath(string root, string relative)
    {
        string full = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, relative));
        if (!full.StartsWith(Full(root), StringComparison.OrdinalIgnoreCase)) throw new IOException("路径超出所选文件夹：" + relative);
        string current = System.IO.Path.GetFullPath(root);
        void Check(string path)
        {
            try { if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("路径已变为符号链接或联接点，请重新对比：" + path); }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        Check(current);
        foreach (string part in relative.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries)) { current = System.IO.Path.Combine(current, part); Check(current); }
    }
    static void CheckStamp(string root, string relative, Dictionary<string, Stamp> expected)
    {
        CheckPath(root, relative);
        var info = new FileInfo(System.IO.Path.Combine(root, relative));
        if (!expected.TryGetValue(relative, out var stamp) || !info.Exists || info.Length != stamp.Size || info.LastWriteTimeUtc.Ticks != stamp.Time)
            throw new IOException("待操作文件在对比后发生变化，请重新对比：" + info.FullName);
    }
    static void CheckRow(Plan p, Change row)
    {
        CheckPath(p.Child, row.Path);
        if (row.Action is "新增" or "更新" or "冲突") CheckStamp(p.Master, row.Path, p.M);
        if (row.Action is "更新" or "归档" or "回收") CheckStamp(p.Child, row.Path, p.C);
        if (row.Action == "移动") CheckStamp(p.Child, row.From, p.C);
        if (row.Action is "新增" or "移动")
        {
            string target = System.IO.Path.Combine(p.Child, row.Path);
            if (File.Exists(target) || Directory.Exists(target)) throw new IOException("缺失目标在对比后已被创建，请重新对比：" + target);
        }
    }
    public static string Execute(Plan p, bool revalidate, Action<string>? progress = null, CancellationToken cancellation = default, Action<WorkProgress>? detail = null)
        => ExecuteCore(p, revalidate, false, true, progress, cancellation, detail);
    public static string Execute(Plan p, ExecutionOptions options, Action<string>? progress = null, CancellationToken cancellation = default, Action<WorkProgress>? detail = null)
        => ExecuteCore(p, options.Revalidate, options.VerifyWrites, options.RecordsInChild, progress, cancellation, detail);
    internal static string LocalRecordsDirectory = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MISee", "Records");
    static string ExecuteCore(Plan p, bool revalidate, bool verify, bool recordsInChild, Action<string>? progress, CancellationToken cancellation, Action<WorkProgress>? detail)
    {
        Validate(p.Master, p.Child);
        cancellation.ThrowIfCancellationRequested();
        string externalRoot = System.IO.Path.GetFullPath(LocalRecordsDirectory);
        if (!recordsInChild)
        {
            if (new[]{p.Master,p.Child}.Any(root => Full(externalRoot).StartsWith(Full(root),StringComparison.OrdinalIgnoreCase) || Full(root).StartsWith(Full(externalRoot),StringComparison.OrdinalIgnoreCase)))
                throw new IOException("本机记录目录与所选文件夹重叠，请更换文件夹或开启在子文件夹内保存记录。");
            CheckPath(System.IO.Path.GetPathRoot(externalRoot)!, System.IO.Path.GetRelativePath(System.IO.Path.GetPathRoot(externalRoot)!, externalRoot));
        }
        var rows = p.Items.Where(x => x.Selected && x.Action != "保留").ToArray();
        string driveRoot = System.IO.Path.GetPathRoot(p.Child)!;
        if (!driveRoot.StartsWith("\\\\", StringComparison.Ordinal))
        {
            var drive = new DriveInfo(driveRoot);
            long required = RequiredSpace(p, rows);
            if (drive.IsReady && drive.AvailableFreeSpace < required)
                throw new IOException($"子文件夹所在磁盘空间不足。更新与备份至少需要约 {required / 1073741824d:F2} GiB，可用 {drive.AvailableFreeSpace / 1073741824d:F2} GiB。");
        }
        long executionWork = rows.Sum(x => CopyBytes(p, x) * (verify ? 2 : 1) + (verify && x.Action == "移动" ? p.M[x.Path].Size : 0) + WorkMeter.ItemUnit) + 1;
        if (!recordsInChild)
        {
            long backupBytes = rows.Sum(x => x.Action == "更新" ? p.C[x.Path].Size : x.Action == "冲突" ? p.M[x.Path].Size : 0);
            var backupDrive = new DriveInfo(System.IO.Path.GetPathRoot(externalRoot)!);
            if (backupDrive.IsReady && backupDrive.AvailableFreeSpace < backupBytes)
                throw new IOException("本机记录所在磁盘空间不足，无法保存更新前备份。");
        }
        WorkMeter meter;
        if (revalidate)
        {
            var masterFiles = Enumerate(p.Master, false, progress, detail, cancellation);
            var childFiles = Enumerate(p.Child, true, progress, detail, cancellation);
            meter = new WorkMeter(masterFiles.Work + childFiles.Work + executionWork, progress, detail);
            var (mf, md) = HashInventory(masterFiles, meter, "执行 1/2：重新校验（尚未更新）", cancellation);
            var (cf, cd) = HashInventory(childFiles, meter, "执行 1/2：重新校验（尚未更新）", cancellation);
            if (!Same(p.M, mf) || !Same(p.C, cf) || !p.MD.SetEquals(md) || !p.CD.SetEquals(cd)) throw new Exception("文件夹在对比后发生了变化，请重新分析。");
        }
        else
        {
            meter = new WorkMeter(executionWork, progress, detail);
            meter.Add(0, "执行：已跳过整库复核", "仅检查待操作文件的大小、修改时间与路径", force: true);
        }
        cancellation.ThrowIfCancellationRequested();
        string id = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6];
        string record = System.IO.Path.Combine(recordsInChild ? EnsureOwned(p.Child, Records) : externalRoot, id);
        Directory.CreateDirectory(record);
        File.WriteAllText(System.IO.Path.Combine(record, "操作计划.json"), JsonSerializer.Serialize(p.Items, new JsonSerializerOptions { WriteIndented = true }));
        string extra = System.IO.Path.Combine(p.Child, Extras, id);
        if (p.Mode == "移到额外资料") { EnsureOwned(p.Child, Extras); Directory.CreateDirectory(extra); }
        using var log = new StreamWriter(System.IO.Path.Combine(record, "执行记录.txt"), false) { AutoFlush = true };
        log.WriteLine($"母文件夹：{p.Master}\n子文件夹：{p.Child}\n开始时间：{DateTime.Now:O}");
        log.WriteLine("记录位置：" + (recordsInChild ? "子文件夹内" : "本机应用数据目录") + " | " + record);
        log.WriteLine(revalidate ? "更新前重新校验：开启（完整内容）" : "更新前重新校验：关闭（跳过整库内容复核，仅检查待操作文件的大小、修改时间与路径）");
        log.WriteLine("比对方式：" + p.ScanDescription + "\n更新后内容校验：" + (verify ? "开启，仅检查实际操作目标" : "关闭"));
        int completed = 0;
        void Interrupt()
        {
            log.WriteLine($"已中断 | 已完成 {completed}/{rows.Length} 项 | {DateTime.Now:O}");
            throw new ExecutionInterruptedException(record, completed);
        }
        foreach (var row in rows)
        {
            if (cancellation.IsCancellationRequested) Interrupt();
            string dst = System.IO.Path.Combine(p.Child, row.Path), src = System.IO.Path.Combine(p.Master, row.Path);
            string current = $"{completed + 1}/{rows.Length} · {row.Action} · {row.Path}";
            string updatePhase = revalidate ? "执行 2/2：更新子文件夹" : "执行：更新子文件夹";
            meter.Add(0, updatePhase, current, force: true);
            try
            {
                cancellation.ThrowIfCancellationRequested();
                CheckRow(p, row);
                log.WriteLine("开始 | " + row.Action + " | " + row.Path);
                switch (row.Action)
                {
                    case "建目录": Directory.CreateDirectory(dst); break;
                    case "新增":
                        RememberCopied(p.Master, row.Path, p.M[row.Path], Copy(src, dst, meter, current, cancellation, verify, p.M[row.Path].Hash));
                        break;
                    case "更新":
                        Copy(dst, System.IO.Path.Combine(record, "更新前备份", row.Path), meter, "备份旧版 · " + current, cancellation, verify, p.C[row.Path].Hash);
                        string tmp = dst + ".greenfolder-" + id;
                        try { string copiedHash = Copy(src, tmp, meter, current, cancellation, verify, p.M[row.Path].Hash); cancellation.ThrowIfCancellationRequested(); File.Move(tmp, dst, true); RememberCopied(p.Master, row.Path, p.M[row.Path], copiedHash); } finally { if (File.Exists(tmp)) File.Delete(tmp); }
                        break;
                    case "移动":
                        string moveSource = System.IO.Path.Combine(p.Child, row.From);
                        string before = verify ? ReadHash(moveSource, meter, current, cancellation) : "";
                        if (verify && p.M[row.Path].Hash.Length > 0 && before != p.M[row.Path].Hash) throw new IOException("待复用文件内容已变化，请重新对比：" + row.From);
                        cancellation.ThrowIfCancellationRequested();
                        Move(moveSource, dst); // Same-volume rename retains the verified data; no cancellable work after the move commits.
                        break;
                    case "归档": Move(dst, System.IO.Path.Combine(extra, row.Path)); break;
                    case "回收": FileSystem.DeleteFile(dst, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin); break;
                    case "冲突": Copy(src, System.IO.Path.Combine(record, "同日期冲突_母版本", row.Path), meter, current, cancellation, verify, p.M[row.Path].Hash); break;
                    case "清空目录":
                        if (Directory.Exists(dst) && !Directory.EnumerateFileSystemEntries(dst).Any())
                        {
                            if (p.Mode == "移到额外资料") Directory.CreateDirectory(System.IO.Path.Combine(extra, row.Path));
                            if (p.Mode == "删除到回收站") FileSystem.DeleteDirectory(dst, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                            else Directory.Delete(dst);
                        }
                        break;
                }
                log.WriteLine("完成 | " + row.Action + " | " + row.Path);
                completed++;
                meter.Add(WorkMeter.ItemUnit, updatePhase, $"已完成 {completed}/{rows.Length} 项", force: true);
            }
            catch (OperationCanceledException) { Interrupt(); }
            catch (Exception ex) { log.WriteLine("失败 | " + ex.Message); throw new Exception("执行已停止，已完成的操作不会自动回退。\n" + row.Path + "：" + ex.Message + "\n记录与备份：" + record, ex); }
        }
        log.WriteLine("本次执行完成。"); meter.Finish("整理完成"); return record;
    }
    static string ReadHash(string path, WorkMeter meter, string current, CancellationToken token)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 256 * 1024, FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(256 * 1024);
        try
        {
            int count;
            meter.Add(0, "校验实际变更内容", current, force: true);
            while ((count = stream.Read(buffer, 0, buffer.Length)) > 0) { token.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, count); meter.Add(count, "校验实际变更内容", current, readBytes: count); }
            token.ThrowIfCancellationRequested(); return Convert.ToHexString(hash.GetHashAndReset());
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }
    static void RememberCopied(string root, string rel, Stamp stamp, string hash)
    {
        if (hash.Length > 0 && sessions.TryGetValue(root, out var s)) s.Remember(rel, stamp with { Hash = hash });
    }
}
