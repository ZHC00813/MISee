using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace GreenFolder;

public enum ComparisonKind { Quick, CachedContent, FullContent }
public sealed record CompareOptions(ComparisonKind Kind = ComparisonKind.Quick, bool ReuseHashes = true, bool TrackChanges = true, bool ReuseMoves = true);
public sealed record ExecutionOptions(bool Revalidate = false, bool VerifyWrites = true, bool RecordsInChild = true);
public sealed class ScanStatistics
{
    public long EnumeratedFiles, HashedFiles, ReadBytes, HashHits, ReusedEntries, ChangedPaths;
    public string Method = "";
    public override string ToString() => $"{Method} · 元数据检查 {EnumeratedFiles:N0} · 复用目录项 {ReusedEntries:N0} · 读取内容 {ReadBytes / 1048576d:F1} MiB · 指纹命中 {HashHits:N0}";
}

public static partial class Engine
{
    public static ScanStatistics LastScan { get; private set; } = new();
    internal static string IndexDirectory = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MISee", "Index-v1");
    static readonly Dictionary<string, IndexSession> sessions = new(StringComparer.OrdinalIgnoreCase);
    public static void CloseIndexes() { foreach (var s in sessions.Values) s.Dispose(); sessions.Clear(); }
    internal static void InvalidateIndex(string root) { if (sessions.TryGetValue(System.IO.Path.GetFullPath(root), out var s)) s.Lost = true; }
    public static void ClearIndexes() { CloseIndexes(); if (Directory.Exists(IndexDirectory)) foreach (var f in Directory.EnumerateFiles(IndexDirectory, "*.json")) File.Delete(f); }
    static IndexSession Session(string root, bool child)
    {
        if (sessions.TryGetValue(root, out var prior) && !prior.Matches(root, child)) { prior.Dispose(); sessions.Remove(root); }
        if (!sessions.TryGetValue(root, out var s))
        {
            sessions[root] = s = new IndexSession(root, child);
        }
        return s;
    }
    sealed record Journal(ulong Id, long Next);
    sealed class DiskIndex
    {
        public int Version { get; set; } = 1;
        public string Identity { get; set; } = "";
        public Dictionary<string, Stamp> Files { get; set; } = new();
        public HashSet<string> Dirs { get; set; } = new();
        public Journal? Journal { get; set; }
    }
    sealed class IndexSession : IDisposable
    {
        readonly string root, identity, cacheFile;
        readonly bool child;
        readonly object gate = new();
        readonly HashSet<string> dirty = new(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> structural = new(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> touched = new(StringComparer.OrdinalIgnoreCase);
        readonly FileSystemWatcher? watcher;
        Dictionary<string, Stamp> hashes = new(StringComparer.OrdinalIgnoreCase);
        Inventory? inventory;
        Journal? journal;
        long generation;
        DateTime censusUtc;
        public bool Lost;
        public bool Matches(string path, bool role) => role == child && Identity(path) == identity;
        public IndexSession(string root, bool child)
        {
            this.root = root; this.child = child;
            identity = Identity(root);
            cacheFile = System.IO.Path.Combine(IndexDirectory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToUpperInvariant() + "|" + child))) + ".json");
            try
            {
                watcher = new FileSystemWatcher(root) { IncludeSubdirectories = true, InternalBufferSize = 64 * 1024,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime | NotifyFilters.Attributes };
                watcher.Changed += (_, e) => Mark(e.FullPath);
                watcher.Created += (_, e) => Mark(e.FullPath, true);
                watcher.Deleted += (_, e) => Mark(e.FullPath, true);
                watcher.Renamed += (_, e) => { Mark(e.OldFullPath, true); Mark(e.FullPath, true); };
                watcher.Error += (_, _) => { lock (gate) { Lost = true; generation++; } };
                watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { Lost = true; }
            try
            {
                var disk = JsonSerializer.Deserialize<DiskIndex>(File.ReadAllText(cacheFile));
                if (disk?.Version == 1 && disk.Identity == identity)
                {
                    if (disk.Files == null || disk.Dirs == null || disk.Files.Any(x => x.Value == null || !ValidRelative(x.Key) || x.Value.Size < 0 || !ValidHash(x.Value.Hash)) || disk.Dirs.Any(x => !ValidRelative(x))) return;
                    hashes = new(disk.Files, StringComparer.OrdinalIgnoreCase);
                    journal = disk.Journal;
                    // Only an unchanged NTFS journal can justify reusing a disk catalog after app restart.
                    if (journal != null && journal == QueryJournal(root))
                    {
                        inventory = new(root, child, hashes.Select(x => (x.Key, x.Value.Size, x.Value.Time)).ToList(), new(disk.Dirs, StringComparer.OrdinalIgnoreCase));
                        censusUtc = DateTime.UtcNow;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { }
        }
        void Mark(string full, bool structure = false)
        {
            string rel = System.IO.Path.GetRelativePath(root, full);
            string top = rel.Split(System.IO.Path.DirectorySeparatorChar)[0];
            if (child && Managed(top) && File.Exists(System.IO.Path.Combine(root, top, Marker))) return;
            lock (gate)
            {
                if (rel == ".") { Lost = true; generation++; return; }
                dirty.Add(rel);
                if (structure) structural.Add(rel);
                if (structure || !Directory.Exists(full)) touched.Add(rel);
                generation++; if (dirty.Count > 20000) Lost = true;
            }
        }
        public Inventory Read(CompareOptions options, ScanStatistics stats, Action<string>? text, Action<WorkProgress>? detail, CancellationToken token)
        {
            string[] changes;
            HashSet<string> structures;
            bool full;
            long start;
            lock (gate)
            {
                full = options.Kind == ComparisonKind.FullContent || !options.TrackChanges || watcher == null || Lost || inventory == null || DateTime.UtcNow - censusUtc > TimeSpan.FromMinutes(10);
                changes = dirty.ToArray(); dirty.Clear();
                structures = new(structural, StringComparer.OrdinalIgnoreCase); structural.Clear();
                foreach (string rel in touched)
                {
                    hashes.Remove(rel);
                    if (inventory?.Dirs.Contains(rel) == true)
                        foreach (string key in hashes.Keys.Where(x => x.StartsWith(rel + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)).ToArray()) hashes.Remove(key);
                }
                touched.Clear();
                if (Lost) hashes.Clear();
                start = generation;
            }
            Journal? before = QueryJournal(root);
            Inventory next;
            if (full)
            {
                next = Enumerate(root, child, text, detail, token);
                stats.EnumeratedFiles += next.Files.Count;
                stats.Method += (stats.Method.Length == 0 ? "" : " / ") + "元数据扫描";
            }
            else
            {
                token.ThrowIfCancellationRequested();
                var files = inventory!.Files.ToDictionary(x => x.Path, x => (x.Size, x.Time), StringComparer.OrdinalIgnoreCase);
                var dirs = new HashSet<string>(inventory.Dirs, StringComparer.OrdinalIgnoreCase);
                foreach (string rel in changes.OrderBy(x => x.Length))
                {
                    token.ThrowIfCancellationRequested();
                    CheckPath(root, rel);
                    string path = System.IO.Path.Combine(root, rel);
                    if (Directory.Exists(path) && dirs.Contains(rel) && !structures.Contains(rel)) continue; // child events carry the actual changes
                    files.Remove(rel);
                    if (dirs.Contains(rel))
                    {
                        foreach (var key in files.Keys.Where(x => x.StartsWith(rel + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)).ToArray()) files.Remove(key);
                        dirs.RemoveWhere(x => x.Equals(rel, StringComparison.OrdinalIgnoreCase) || x.StartsWith(rel + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
                    }
                    if (Directory.Exists(path))
                    {
                        if (!rel.Contains(System.IO.Path.DirectorySeparatorChar) && Managed(rel))
                        {
                            if (child && File.Exists(System.IO.Path.Combine(path, Marker))) continue;
                            throw new IOException("文件夹中存在程序保留名称：" + path);
                        }
                        var part = Enumerate(root, child, text, detail, token, rel);
                        foreach (var file in part.Files) files[file.Path] = (file.Size, file.Time);
                        dirs.UnionWith(part.Dirs); stats.EnumeratedFiles += part.Files.Count;
                    }
                    else if (File.Exists(path))
                    {
                        if (!rel.Contains(System.IO.Path.DirectorySeparatorChar) && Managed(rel)) throw new IOException("文件夹中存在程序保留名称：" + path);
                        var info = new FileInfo(path); files[rel] = (info.Length, info.LastWriteTimeUtc.Ticks); stats.EnumeratedFiles++;
                    }
                }
                next = new(root, child, files.Select(x => (x.Key, x.Value.Size, x.Value.Time)).ToList(), dirs);
                stats.ChangedPaths += changes.Length;
                stats.ReusedEntries += Math.Max(0, inventory.Files.Count - changes.Length);
                stats.Method += (stats.Method.Length == 0 ? "" : " / ") + "实时变更索引";
            }
            Journal? after = QueryJournal(root);
            lock (gate)
            {
                inventory = next; journal = before != null && before == after ? after : null;
                if (full) censusUtc = DateTime.UtcNow;
                Lost = generation != start;
            }
            return next;
        }
        public string Cached(string rel, long size, long time, bool enabled)
        {
            lock (gate) return enabled && hashes.TryGetValue(rel, out var s) && s.Size == size && s.Time == time ? s.Hash : "";
        }
        public void Remember(string rel, Stamp stamp) { lock (gate) hashes[rel] = stamp; }
        public void Save(Inventory snapshot)
        {
            try
            {
                Directory.CreateDirectory(IndexDirectory);
                var disk = new DiskIndex { Identity = identity, Journal = Lost ? null : journal, Dirs = snapshot.Dirs,
                    Files = snapshot.Files.ToDictionary(x => x.Path, x => new Stamp(x.Size, x.Time, Cached(x.Path, x.Size, x.Time, true)), StringComparer.OrdinalIgnoreCase) };
                string temp = cacheFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try { File.WriteAllText(temp, JsonSerializer.Serialize(disk)); File.Move(temp, cacheFile, true); }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Cache persistence must not interrupt sync. */ }
        }
        public void Dispose() => watcher?.Dispose();
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool GetVolumeInformation(string root, StringBuilder? name, uint nameSize, out uint serial, out uint maxComponent, out uint flags, StringBuilder fileSystem, uint fsSize);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(SafeFileHandle h, uint code, IntPtr input, uint inputSize, byte[] output, uint outputSize, out uint returned, IntPtr overlapped);
    static string Identity(string root)
    {
        var fs = new StringBuilder(64);
        bool known = GetVolumeInformation(System.IO.Path.GetPathRoot(root)!, null, 0, out uint serial, out _, out _, fs, 64);
        return root.ToUpperInvariant() + "|" + new DirectoryInfo(root).CreationTimeUtc.Ticks + "|" + (known ? serial + "|" + fs : "network");
    }
    static bool ValidRelative(string value) => !string.IsNullOrWhiteSpace(value) && !System.IO.Path.IsPathRooted(value) && !value.Split('\\', '/').Any(x => x is "." or "..");
    static bool ValidHash(string value) => value != null && (value.Length == 0 || value.Length == 64 && value.All(char.IsAsciiHexDigit));
    static Journal? QueryJournal(string root)
    {
        string volume = System.IO.Path.GetPathRoot(root)!;
        if (volume.Length != 3) return null;
        try
        {
            if (!new DriveInfo(volume).DriveFormat.Equals("NTFS", StringComparison.OrdinalIgnoreCase)) return null;
            using var h = CreateFile(@"\\.\" + volume[..2], 0x80000000, 7, IntPtr.Zero, 3, 0, IntPtr.Zero);
            byte[] data = new byte[128];
            return !h.IsInvalid && DeviceIoControl(h, 0x000900f4, IntPtr.Zero, 0, data, (uint)data.Length, out uint count, IntPtr.Zero) && count >= 56
                ? new Journal(BitConverter.ToUInt64(data, 0), BitConverter.ToInt64(data, 16)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
    public static Plan Analyze(string master, string child, string mode, CompareOptions options, Action<string>? progress = null, CancellationToken cancellation = default, Action<WorkProgress>? detail = null)
    {
        try { return AnalyzeIndexed(master, child, mode, options, progress, cancellation, detail); }
        catch
        {
            foreach (var entry in sessions.Values) entry.Lost = true;
            throw;
        }
    }
    static Plan AnalyzeIndexed(string master, string child, string mode, CompareOptions options, Action<string>? progress, CancellationToken cancellation, Action<WorkProgress>? detail)
    {
        Validate(master, child);
        if (sessions.Count >= 6) CloseIndexes();
        // Let queued filesystem notifications settle before taking a new generation.
        if (options.TrackChanges && cancellation.WaitHandle.WaitOne(150)) cancellation.ThrowIfCancellationRequested();
        var p = new Plan { Master = System.IO.Path.GetFullPath(master), Child = System.IO.Path.GetFullPath(child), Mode = mode, QuickComparison = options.Kind == ComparisonKind.Quick };
        var stats = LastScan = new ScanStatistics();
        var sm = Session(p.Master, false); var sc = Session(p.Child, true);
        var mi = sm.Read(options, stats, progress, detail, cancellation); var ci = sc.Read(options, stats, progress, detail, cancellation);
        p.MD = mi.Dirs; p.CD = ci.Dirs;
        p.M = mi.Files.ToDictionary(x => x.Path, x => new Stamp(x.Size, x.Time, sm.Cached(x.Path, x.Size, x.Time, options.ReuseHashes && options.Kind != ComparisonKind.FullContent)), StringComparer.OrdinalIgnoreCase);
        p.C = ci.Files.ToDictionary(x => x.Path, x => new Stamp(x.Size, x.Time, sc.Cached(x.Path, x.Size, x.Time, options.ReuseHashes && options.Kind != ComparisonKind.FullContent)), StringComparer.OrdinalIgnoreCase);
        var needM = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var needC = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (options.Kind != ComparisonKind.Quick)
        { foreach (var x in p.M.Where(x => x.Value.Hash.Length == 0)) needM.Add(x.Key); foreach (var x in p.C.Where(x => x.Value.Hash.Length == 0)) needC.Add(x.Key); }
        if (options.ReuseMoves)
        {
            var missingSizes = p.M.Where(x => !p.C.ContainsKey(x.Key)).Select(x => x.Value.Size).ToHashSet();
            var extras = p.C.Where(x => !p.M.ContainsKey(x.Key) && missingSizes.Contains(x.Value.Size)).ToArray();
            var candidateSizes = extras.Select(x => x.Value.Size).ToHashSet();
            foreach (var x in extras.Where(x => x.Value.Hash.Length == 0)) needC.Add(x.Key);
            foreach (var x in p.M.Where(x => !p.C.ContainsKey(x.Key) && candidateSizes.Contains(x.Value.Size) && x.Value.Hash.Length == 0)) needM.Add(x.Key);
        }
        var meter = new WorkMeter(needM.Sum(x => p.M[x].Size + WorkMeter.ItemUnit) + needC.Sum(x => p.C[x].Size + WorkMeter.ItemUnit) + 1, progress, detail);
        void HashSome(Inventory inv, Dictionary<string, Stamp> target, HashSet<string> needed, IndexSession index)
        {
            stats.HashHits += target.Values.Count(x => x.Hash.Length > 0);
            var subset = new Inventory(inv.Root, inv.Child, inv.Files.Where(x => needed.Contains(x.Path)).ToList(), inv.Dirs);
            var (hashed, _) = HashInventory(subset, meter, "按需校验内容", cancellation);
            foreach (var x in hashed) { target[x.Key] = x.Value; index.Remember(x.Key, x.Value); stats.HashedFiles++; stats.ReadBytes += x.Value.Size; }
        }
        HashSome(mi, p.M, needM, sm); HashSome(ci, p.C, needC, sc);
        if (!options.ReuseMoves) // Exclude extras from the content-reuse lookup without discarding cached fingerprints.
        {
            var saved = p.C.Where(x => !p.M.ContainsKey(x.Key)).ToDictionary(x => x.Key, x => x.Value);
            foreach (var x in saved) p.C[x.Key] = x.Value with { Hash = "" };
            BuildItems(p, cancellation);
            foreach (var x in saved) p.C[x.Key] = x.Value;
        }
        else BuildItems(p, cancellation);
        cancellation.ThrowIfCancellationRequested();
        // Never write even a cache inside either selected tree.
        if (!Full(IndexDirectory).StartsWith(Full(p.Master), StringComparison.OrdinalIgnoreCase) && !Full(IndexDirectory).StartsWith(Full(p.Child), StringComparison.OrdinalIgnoreCase))
        { sm.Save(mi); sc.Save(ci); }
        p.ScanDescription = options.Kind + " · " + stats;
        meter.Finish("对比完成"); progress?.Invoke(stats.ToString());
        return p;
    }
}
