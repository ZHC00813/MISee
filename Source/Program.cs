namespace GreenFolder;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length > 1 && args[0] == "--self-test") { SelfTest.Run(args[1]); return; }
        if (args.Length > 1 && args[0] == "--performance-test") { PerformanceTest.Run(args[1]); return; }
        if (args.Length > 1 && args[0] == "--integration-test") { IntegrationTest.Run(args[1]); return; }
        if (args.Length > 1 && args[0] == "--option-test") { OptionTest.Run(args[1]); return; }
        if (args.Length > 1 && args[0] == "--smart-test") { SmartTest.Run(args[1]); return; }
        ApplicationConfiguration.Initialize();
        var form = new MainForm();
        if (args.Length > 1 && args[0] == "--ui-test") form.EnableUiTest(args[1]);
        if (args.Length > 1 && args[0] == "--layout-test") form.EnableLayoutTest(args[1]);
        if (args.Length > 0 && args[0] == "--render") form.RenderPreview(args[1]);
        Application.Run(form);
    }
}
public partial class MainForm : Form
{
    readonly Color blue = Color.FromArgb(24, 90, 189);
    readonly TextBox master = new(), child = new();
    readonly ComboBox mode = new();
    readonly CheckBox recheck = new() { Text = "更新前重新校验整库（耗时较长）", Checked = false, AutoSize = true };
    readonly ComboBox comparison = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 265 };
    readonly CheckBox reuseHashes = new() { Text = "复用内容指纹缓存", Checked = true, AutoSize = true };
    readonly CheckBox trackChanges = new() { Text = "使用实时变更索引", Checked = true, AutoSize = true };
    readonly CheckBox reuseMoves = new() { Text = "按内容复用已有文件", Checked = true, AutoSize = true };
    readonly CheckBox verifyWrites = new() { Text = "校验本次写入内容", Checked = true, AutoSize = true };
    readonly ModernButton clearCache = new();
    readonly ToolTip tips = new();
    readonly DataGridView grid = new();
    readonly Label status = new(), summary = new();
    readonly ModernButton preview = new(), execute = new(), quick = new();
    readonly ProgressDisplay busyBar = new();
    readonly ModernButton cancel = new();
    readonly Label progressInfo = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    readonly System.Windows.Forms.Timer progressTimer = new() { Interval = 250 };
    CancellationTokenSource? scanCancellation;
    string? pendingProgress;
    WorkProgress? pendingWork;
    Plan? plan;
    bool busy;
    string? lastRecord;
    bool testMode;
    void Notify(string message, string title, MessageBoxIcon icon)
    {
        if (testMode) { if (icon == MessageBoxIcon.Warning) throw new Exception(title + ": " + message); return; }
        MessageBox.Show(this, message, title, MessageBoxButtons.OK, icon);
    }
    public void EnableUiTest(string output)
    {
        testMode = true; Opacity = 0; ShowInTaskbar = false;
        Shown += async (_, _) => {
            var notes = new List<string>();
            void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); notes.Add("PASS: " + name); }
            try {
                string root = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(output)!, "ui-flow-" + Guid.NewGuid().ToString("N"));
                string m = System.IO.Path.Combine(root, "母文件夹"), c = System.IO.Path.Combine(root, "子文件夹");
                Directory.CreateDirectory(m); Directory.CreateDirectory(c);
                File.WriteAllText(System.IO.Path.Combine(m, "更新.txt"), "new"); File.WriteAllText(System.IO.Path.Combine(c, "更新.txt"), "old");
                File.SetLastWriteTimeUtc(System.IO.Path.Combine(m, "更新.txt"), new DateTime(2026,1,3)); File.SetLastWriteTimeUtc(System.IO.Path.Combine(c, "更新.txt"), new DateTime(2026,1,1));
                File.WriteAllText(System.IO.Path.Combine(m, "新增.txt"), "added");
                byte[] sample = new byte[16 * 1024 * 1024]; new Random(23).NextBytes(sample);
                File.WriteAllBytes(System.IO.Path.Combine(m, "相同大文件.bin"), sample); File.WriteAllBytes(System.IO.Path.Combine(c, "相同大文件.bin"), sample);
                master.Text = m; child.Text = c; mode.SelectedIndex = 2;
                Check(!recheck.Checked && comparison.SelectedIndex == 0 && verifyWrites.Checked, "默认快速比对、跳过重复整库复核并校验实际写入内容");
                Check(!advancedHost.Visible && Font.SizeInPoints >= 11.5F && AutoScaleMode == AutoScaleMode.Dpi, "高级设置默认折叠，正文放大且启用 DPI 布局");
                advanced.PerformClick();
                Check(advancedHost.Visible, "高级设置按钮展开全部性能和校验选项");
                preview.PerformClick();
                Check(busy && cancel.Enabled, "点击对比后立即进入忙碌状态，中断按钮启用");
                var timeout = System.Diagnostics.Stopwatch.StartNew();
                while (busy) { if (timeout.Elapsed.TotalSeconds > 30) throw new TimeoutException("UI compare timeout"); await Task.Delay(20); }
                Check(plan != null && execute.Enabled && grid.DataSource != null, "实际对比按钮完成后生成清单并启用执行");
                var updateRow=plan!.Items.Single(x=>x.Action=="更新" && x.Path=="更新.txt");
                actionFilter.SelectedItem="更新";search.Text="更新.txt";
                Check(grid.Rows.Count==1 && ReferenceEquals(grid.Rows[0].DataBoundItem,updateRow),"按操作和路径筛选保留原计划对象");
                grid.CurrentCell=grid.Rows[0].Cells[0];grid.CurrentCell.Value=false;CommitGrid();
                Check(!updateRow.Selected,"筛选视图取消勾选能写回实际执行计划");
                grid.CurrentCell.Value=true;CommitGrid();search.Text="不存在的结果";
                Check(emptyState.Visible && grid.Rows.Count==0,"无匹配项时显示筛选空状态");
                search.Clear();actionFilter.SelectedIndex=0;
                Check(updateRow.Selected && grid.Rows.Count==plan.Items.Count,"清除筛选恢复全部差异，原勾选状态保留");
                var existingPlan = plan; recheck.Checked = true;
                Check(ReferenceEquals(existingPlan, plan) && execute.Enabled, "更改复核选项不使已有对比清单失效");
                recheck.Checked = true;
                Check(busyBar.Value == 1000 && progressInfo.Text.Contains("100.0%"), "对比完成后界面进度条达到 100%");
                execute.PerformClick();
                Check(busy && cancel.Enabled, "点击执行后立即进入忙碌状态，中断按钮启用");
                timeout.Restart();
                while (busy) { if (timeout.Elapsed.TotalSeconds > 30) throw new TimeoutException("UI execute timeout"); await Task.Delay(20); }
                Check(File.ReadAllText(System.IO.Path.Combine(c, "更新.txt")) == "new" && File.ReadAllText(System.IO.Path.Combine(c, "新增.txt")) == "added", "实际执行按钮完成覆盖更新和新增文件");
                Check(busyBar.Value == 1000 && progressInfo.Text.Contains("100.0%") && status.Text.Contains("整理完成"), "界面显示完成状态及 100% 进度");
                Check(lastRecord != null && File.Exists(System.IO.Path.Combine(lastRecord, "执行记录.txt")), "界面记录按钮获得本次日志位置");
                preview.PerformClick(); cancel.PerformClick();
                timeout.Restart();
                while (busy) { if (timeout.Elapsed.TotalSeconds > 30) throw new TimeoutException("UI interrupt timeout"); await Task.Delay(20); }
                Check(plan == null && status.Text.Contains("已中断") && !cancel.Enabled && preview.Enabled, "实际中断按钮停止对比并恢复界面可操作状态");
                File.WriteAllText(System.IO.Path.Combine(m, "中断后待更新.txt"), "pending");
                preview.PerformClick(); timeout.Restart();
                while (busy) { if (timeout.Elapsed.TotalSeconds > 30) throw new TimeoutException("UI second compare timeout"); await Task.Delay(20); }
                execute.PerformClick(); cancel.PerformClick(); timeout.Restart();
                while (busy) { if (timeout.Elapsed.TotalSeconds > 30) throw new TimeoutException("UI execute interrupt timeout"); await Task.Delay(20); }
                Check(status.Text.Contains("校验已中断") && !File.Exists(System.IO.Path.Combine(c, "中断后待更新.txt")), "实际中断按钮可在执行前复核阶段安全停止更新");
                File.WriteAllText(System.IO.Path.Combine(m, "再次新增.txt"), "later");
                recheck.Checked = false;
                quick.PerformClick(); timeout.Restart();
                while (busy) { if (timeout.Elapsed.TotalSeconds > 30) throw new TimeoutException("UI quick update timeout"); await Task.Delay(20); }
                Check(File.Exists(System.IO.Path.Combine(c, "再次新增.txt")) && status.Text.Contains("整理完成"), "一键更新按钮自动串联对比和实际更新");
                Check(lastRecord != null && File.ReadAllText(System.IO.Path.Combine(lastRecord, "执行记录.txt")).Contains("更新前重新校验：关闭"), "界面取消勾选后实际执行跳过整库复核，日志记录所选模式");
                using (var bitmap = new Bitmap(Controls[0].Width, Controls[0].Height)) {
                    Controls[0].DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    bitmap.Save(System.IO.Path.ChangeExtension(output, ".png"));
                }
                notes.Add("全部界面流程测试通过。测试目录：" + root);
            } catch (Exception ex) { notes.Add(ex.ToString()); Environment.ExitCode = 1; }
            finally { File.WriteAllLines(output, notes); Close(); }
        };
    }
    public void RenderPreview(string path)
    {
        master.Text = "D:\\MISeeDemo\\MasterLibrary"; child.Text = "D:\\MISeeDemo\\ChildLibrary";
        var samples=new List<Change> {
            new() { Action = "新增", Path = "项目说明.txt", Detail = "从母文件夹补充缺失文件" },
            new() { Action = "移动", Path = "合同\\采购合同.pdf", Detail = "使用子文件夹已有资料：采购合同.pdf" },
            new() { Action = "更新", Path = "预算.xlsx", Detail = "母文件夹较新；原文件先备份" },
            new() { Action = "保留", Path = "项目计划.docx", Detail = "子文件夹较新，保留现有版本", Selected = false },
            new() { Action = "保留", Path = "个人备注.txt", Detail = "子文件夹独有资料 · 原处保留", Selected = false },
            new() { Action = "冲突", Path = "设计资料\\方案说明.docx", Detail = "日期相同但内容不同；保留子版本，另存母版本" }
        };
        plan=new Plan { Master=master.Text,Child=child.Text,Items=samples };BindRows(samples);
        summary.Text = "共 6 项差异 · 待处理 4 项 · 保留 2 项";
        Opacity = 0; ShowInTaskbar = false;
        Shown += (_, _) => BeginInvoke(() => {
            PerformLayout(); Controls[0].PerformLayout();
            using var bitmap = new Bitmap(Controls[0].Width, Controls[0].Height);
            Controls[0].DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(path);
            BeginInvoke(Close);
        });
    }
    public MainForm() { InitializeVisuals(); }
    void InvalidatePlan() { plan = null; BindRows(new()); summary.Text = "设置已更改，请重新比对。"; UpdateActionState(); }
    void SetBusy(bool value)
    {
        busy = value; preview.Enabled = quick.Enabled = master.Enabled = child.Enabled = mode.Enabled = grid.Enabled = recheck.Enabled = !value;
        comparison.Enabled = reuseHashes.Enabled = trackChanges.Enabled = reuseMoves.Enabled = verifyWrites.Enabled = clearCache.Enabled = !value;
        if (master.Tag is Control mc) mc.Enabled = !value;
        if (child.Tag is Control cc) cc.Enabled = !value;
        UpdateActionState(); UseWaitCursor = false;
        if (value) {
            scanCancellation = new CancellationTokenSource();
            Interlocked.Exchange(ref pendingWork, null); Interlocked.Exchange(ref pendingProgress, null);
            cancel.Text = "中断"; cancel.Enabled = true; busyBar.Style = ProgressBarStyle.Marquee;
            busyBar.Value = 0;
            progressInfo.Text = "正在统计总容量 · 预计剩余时间：正在估算"; progressTimer.Start();
        }
        else {
            progressTimer.Stop(); var latest = Interlocked.Exchange(ref pendingWork, null); if (latest != null) ShowProgress(latest);
            Interlocked.Exchange(ref pendingProgress, null); busyBar.Style = ProgressBarStyle.Continuous;
            if (scanCancellation?.IsCancellationRequested == true && busyBar.Value < 1000) progressInfo.Text = "已中断 · " + progressInfo.Text;
            cancel.Enabled = false; cancel.Text = "中断"; scanCancellation?.Dispose(); scanCancellation = null;
        }
    }
    void ShowProgress(WorkProgress work)
    {
        busyBar.Style = work.Fraction == null ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
        if (work.Fraction is double fraction) busyBar.Value = Math.Clamp((int)(fraction * 1000), 0, 1000);
        string percent = work.Fraction is double f ? $"{Math.Floor(f * 1000) / 10:F1}%" : "统计中";
        progressInfo.Text = $"{percent}    预计剩余 {work.Estimate}    已读取 {work.ReadBytes / 1073741824d:F2} GiB    已复制 {work.CopiedBytes / 1073741824d:F2} GiB";
    }
    void TextProgress(string text) => Interlocked.Exchange(ref pendingProgress, text);
    void DetailProgress(WorkProgress work) => Interlocked.Exchange(ref pendingWork, work);
    async Task Analyze(bool immediate)
    {
        string m = master.Text.Trim().Trim('"'), c = child.Text.Trim().Trim('"'), setting = mode.Text;
        var options = new CompareOptions((ComparisonKind)comparison.SelectedIndex, reuseHashes.Checked, trackChanges.Checked, reuseMoves.Checked);
        SetBusy(true); status.Text = "正在检查变更索引和文件元数据…";
        try
        {
            var token = scanCancellation!.Token;
            plan = await Task.Run(() => Engine.Analyze(m, c, setting, options, TextProgress, token, DetailProgress));
            BindRows(plan.Items);
            if(plan.Items.Count == 0) { summary.Text="两边一致，无需更新。"; emptyState.Text="本次比对没有发现需要处理的差异。"; }
            status.Text = "比对完成 · 请核对勾选项目，再执行更新。";
            tips.SetToolTip(summary, Engine.LastScan.ToString()+"\n执行更新会处理全部勾选项，包含被筛选隐藏的项目。");
        }
        catch (OperationCanceledException) { plan = null; status.Text = "对比已中断，未执行文件整理。"; }
        catch (Exception ex) { plan = null; status.Text = "对比未完成。"; Notify(ex.Message, "无法完成对比", MessageBoxIcon.Warning); }
        finally { SetBusy(false); }
        if (immediate && plan != null && plan.Items.Any(x => x.Selected)) await Apply();
    }
    async Task Apply()
    {
        if (plan == null) return;
        CommitGrid();
        var current = plan;
        if (!current.Items.Any(x => x.Selected && x.Action != "保留")) { status.Text = "没有需要执行的勾选项。"; return; }
        bool revalidate = recheck.Checked;
        var options = new ExecutionOptions(revalidate, verifyWrites.Checked);
        SetBusy(true); status.Text = revalidate ? "正在重新校验文件夹，随后执行整理…" : "已跳过整库复核，正在检查待操作文件并执行更新…";
        if (!revalidate) progressInfo.Text = "准备执行更新 · 不进行整库内容复核";
        try
        {
            var token = scanCancellation!.Token;
            lastRecord = await Task.Run(() => Engine.Execute(current, options, TextProgress, token, DetailProgress));
            status.Text = "整理完成。备份与详细日志已保存，可点击“打开本次记录”。";
        }
        catch (ExecutionInterruptedException ex) { lastRecord = ex.RecordPath; status.Text = $"整理已中断 · 已完成 {ex.Completed} 项，日志和备份已保留。"; }
        catch (OperationCanceledException) { status.Text = "执行前校验已中断，尚未开始更新文件。"; }
        catch (Exception ex) { status.Text = "执行已停止，请查看提示并重新对比。"; Notify(ex.Message, "执行未完成", MessageBoxIcon.Warning); }
        finally { plan = null; SetBusy(false); }
    }
}
