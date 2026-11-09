namespace GreenFolder;

public partial class MainForm
{
    readonly ModernButton advanced = new(), records = new();
    readonly ModernButton selectAll = new(), selectNone = new(), invertSelection = new();
    readonly TextBox search = new() { PlaceholderText = "搜索文件或路径" };
    readonly ComboBox actionFilter = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly Label emptyState = new();
    readonly Panel body = new() { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty };
    readonly TableLayoutPanel workspace = new() { Dock = DockStyle.Top, ColumnCount = 1, RowCount = 5, Margin = Padding.Empty };
    readonly Panel advancedHost = new() { Dock = DockStyle.Fill, Margin = Padding.Empty };
    List<Change> displayedItems = new();
    bool advancedOpen, rebinding;
    int Px(int logical) => (int)Math.Round(logical * DeviceDpi / 96d);
    Label Caption(string text) => new() { Text=text,AutoSize=false,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,ForeColor=UiTheme.Muted,Font=new Font(Font.FontFamily,10.5F) };
    void InitializeVisuals()
    {
        Text="MISee · 文件夹比对与更新 v0.9"; AutoScaleDimensions=new SizeF(96,96); AutoScaleMode=AutoScaleMode.Dpi;
        Font=new Font("Microsoft YaHei UI",11.5F,FontStyle.Regular,GraphicsUnit.Point);
        ForeColor=UiTheme.Ink; BackColor=UiTheme.Canvas; ClientSize=new Size(1220,860); MinimumSize=new Size(960,680);
        StartPosition=FormStartPosition.CenterScreen;
        var root=new TableLayoutPanel { Dock=DockStyle.Fill,Padding=new Padding(28,20,28,20),ColumnCount=1,RowCount=2,BackColor=UiTheme.Canvas };
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute,134)); Controls.Add(root);
        root.Controls.Add(body,0,0); body.Controls.Add(workspace);
        foreach(int h in new[]{68,160,80,0}) workspace.RowStyles.Add(new RowStyle(SizeType.Absolute,h));
        workspace.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        body.SizeChanged+=(_,_)=>FitWorkspace();
        DpiChanged+=(_,_)=>BeginInvoke(()=>{workspace.RowStyles[3].Height=advancedOpen?Px(150):0;FitWorkspace();});

        var header=new Panel { Dock=DockStyle.Fill,Margin=Padding.Empty };
        var logo=new RoundedPanel { Size=new Size(42,42),Location=new Point(0,3),BackColor=UiTheme.Accent,BorderColor=UiTheme.Accent,Radius=10 };
        logo.Controls.Add(new Label {Text="M",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,ForeColor=Color.White,BackColor=Color.Transparent,Font=new Font("Segoe UI",19,FontStyle.Bold)});
        header.Controls.Add(logo);
        header.Controls.Add(new Label { Text="MISee",AutoSize=true,Location=new Point(56,-1),Font=new Font("Segoe UI",23,FontStyle.Bold),ForeColor=UiTheme.Ink });
        header.Controls.Add(new Label { Text="文件夹比对与局部更新",AutoSize=true,Location=new Point(57,36),Font=new Font(Font.FontFamily,10.5F),ForeColor=UiTheme.Muted });
        var protection=new Label { Text="v0.9  ·  母本只读  ·  覆盖前备份",AutoSize=false,Anchor=AnchorStyles.Top|AnchorStyles.Right,Size=new Size(370,42),TextAlign=ContentAlignment.MiddleRight,ForeColor=UiTheme.Muted,Font=new Font(Font.FontFamily,10.5F) };
        header.Controls.Add(protection); header.SizeChanged+=(_,_)=>protection.Left=header.ClientSize.Width-protection.Width;
        workspace.Controls.Add(header,0,0);

        var folders=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=new Padding(0,0,0,16) };
        folders.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50)); folders.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,36)); folders.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        folders.Controls.Add(FolderCard("母文件夹",master,true),0,0);
        folders.Controls.Add(new Label { Text="→",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,ForeColor=Color.FromArgb(148,163,184),Font=new Font("Segoe UI",20) },1,0);
        folders.Controls.Add(FolderCard("子文件夹",child,false),2,0); workspace.Controls.Add(folders,0,1);

        var settings=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=4,RowCount=1,Margin=new Padding(0,0,0,14) };
        settings.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        foreach(float weight in new[]{42f,27f,15f,16f}) settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,weight));
        comparison.Items.AddRange(new object[]{"快速：同路径同大小视为相同","缓存内容：只读变化或未缓存的文件","完整内容：重新读取所有文件"}); comparison.SelectedIndex=0;
        mode.DropDownStyle=ComboBoxStyle.DropDownList; mode.Items.AddRange(new object[]{"移到额外资料","删除到回收站","原处保留"}); mode.SelectedIndex=2;
        settings.Controls.Add(Field("比对方式",comparison),0,0); settings.Controls.Add(Field("子文件夹独有资料",mode),1,0);
        SetupButton(advanced,"高级设置 ＋",()=>ToggleAdvanced()); advanced.Dock=DockStyle.Fill; advanced.Margin=new Padding(0,24,12,0); settings.Controls.Add(advanced,2,0);
        SetupButton(preview,"开始比对",async()=>await Analyze(false)); preview.Dock=DockStyle.Fill; preview.Margin=new Padding(0,24,0,0); settings.Controls.Add(preview,3,0);
        workspace.Controls.Add(settings,0,2);

        advancedHost.Padding=new Padding(0,0,0,12);
        var options=new RoundedPanel { Dock=DockStyle.Fill,Padding=new Padding(16,10,16,10),Margin=Padding.Empty };
        var optionRows=new FlowLayoutPanel { Dock=DockStyle.Fill,WrapContents=true };
        reuseHashes.Text="复用文件指纹缓存"; trackChanges.Text="仅刷新有变化的部分"; reuseMoves.Text="按内容复用已有文件";
        foreach(var option in new[]{reuseHashes,trackChanges,reuseMoves,recheck,verifyWrites,recordsInChild}) { option.Margin=new Padding(0,5,24,9); optionRows.Controls.Add(option); }
        optionRows.SetFlowBreak(reuseMoves,true); optionRows.SetFlowBreak(recordsInChild,true);
        tips.SetToolTip(recordsInChild,"取消勾选后，不在子文件夹创建新的 .MISee记录；日志与备份改存本机 %LOCALAPPDATA%\\MISee\\Records。已有记录不删除，额外资料归档按原设置执行。");
        recordsInChild.CheckedChanged+=(_,_)=>{if(!busy)status.Text=recordsInChild.Checked?"记录和备份保存在子文件夹内。":"记录和备份改存本机，可在完成后点击“打开记录”。";};
        SetupButton(clearCache,"重建本机索引",()=>{try{Engine.ClearIndexes();InvalidatePlan();status.Text="本机索引已清理，下次比对重新收集。";}catch(Exception ex){Notify(ex.Message,"无法清理索引",MessageBoxIcon.Warning);}});
        SetupButton(quick,"一键比对并更新",async()=>await Analyze(true));
        clearCache.Size=new Size(145,34); quick.Size=new Size(172,34); clearCache.Margin=new Padding(0,0,12,0); quick.Margin=Padding.Empty;
        optionRows.Controls.Add(clearCache); optionRows.Controls.Add(quick); options.Controls.Add(optionRows); advancedHost.Controls.Add(options);
        workspace.Controls.Add(advancedHost,0,3); advancedHost.Visible=false;
        tips.SetToolTip(comparison,"快速：同路径、同大小、同时间按一致处理，有双方指纹时同时利用指纹。\n缓存内容：复用未变指纹，重读没有有效指纹的内容。\n完整内容：不信任缓存，逐个读取，可发现同大小同时间的内容差异。");
        tips.SetToolTip(trackChanges,"利用程序打开期间收到的变更通知，跳过未变部分。通知失效、重开程序或超过元数据复查间隔时，必要时重新检查文件属性。");
        tips.SetToolTip(recheck,"开启后，执行前完整重读两边所有内容，TB 级资料可能耗时数小时。关闭时仍检查待操作文件的大小、时间和路径。");
        tips.SetToolTip(verifyWrites,"只核验本次写入和备份涉及的内容，不重读无关资料。");
        recheck.CheckedChanged+=(_,_)=>{if(!busy)status.Text=recheck.Checked?"更新前将完整复核两边内容。":"更新前跳过整库复核，保留逐项检查。";};

        var results=new RoundedPanel { Dock=DockStyle.Fill,Padding=new Padding(18,12,18,12),Margin=new Padding(0,0,0,0) };
        var resultLayout=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Margin=Padding.Empty };
        resultLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,68)); resultLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,44)); resultLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var resultHead=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=Padding.Empty };
        resultHead.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); resultHead.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,140)); resultHead.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,212));
        var title=new Panel { Dock=DockStyle.Fill,Margin=Padding.Empty };
        title.Controls.Add(new Label { Text="差异预览",AutoSize=true,Font=new Font(Font.FontFamily,15,FontStyle.Bold),Location=new Point(0,0) });
        summary.Text="选好文件夹后，开始第一次比对。"; summary.Location=new Point(0,33); summary.Height=26; summary.Anchor=AnchorStyles.Left|AnchorStyles.Right|AnchorStyles.Top; summary.ForeColor=UiTheme.Muted; summary.Font=new Font(Font.FontFamily,10.5F); summary.AutoEllipsis=true;
        title.Controls.Add(summary); title.SizeChanged+=(_,_)=>summary.Width=title.ClientSize.Width; resultHead.Controls.Add(title,0,0);
        actionFilter.Items.AddRange(new object[]{"全部操作","新增","更新","移动","冲突","保留","归档","回收","目录"}); actionFilter.SelectedIndex=0;
        actionFilter.Dock=DockStyle.Top; actionFilter.Margin=new Padding(0,9,12,0); resultHead.Controls.Add(actionFilter,1,0);
        var searchFrame=new RoundedPanel {Dock=DockStyle.Top,Height=34,Radius=6,Padding=new Padding(9,4,7,2),Margin=new Padding(0,8,0,0)};
        var searchLayout=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};
        searchLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));searchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,50));searchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        var searchCaption=Caption("搜索");searchCaption.Margin=Padding.Empty;searchLayout.Controls.Add(searchCaption,0,0);search.BorderStyle=BorderStyle.None;search.Dock=DockStyle.Fill;search.Margin=Padding.Empty;searchLayout.Controls.Add(search,1,0);searchFrame.Controls.Add(searchLayout);resultHead.Controls.Add(searchFrame,2,0);
        resultLayout.Controls.Add(resultHead,0,0);
        var selectionBar=new FlowLayoutPanel { Dock=DockStyle.Fill,Margin=Padding.Empty,WrapContents=false };
        SetupButton(selectAll,"全选",()=>SelectVisible(1));
        SetupButton(selectNone,"全不选",()=>SelectVisible(0));
        SetupButton(invertSelection,"反选",()=>SelectVisible(-1));
        foreach(var button in new[]{selectAll,selectNone,invertSelection}) { button.Size=new Size(88,34);button.Margin=new Padding(0,0,10,8);selectionBar.Controls.Add(button); }
        selectionBar.Controls.Add(new Label { Text="仅作用于当前显示的项目",AutoSize=true,ForeColor=UiTheme.Muted,Margin=new Padding(4,7,0,0),Font=new Font(Font.FontFamily,10F) });
        resultLayout.Controls.Add(selectionBar,0,1);
        var resultBody=new Panel { Dock=DockStyle.Fill,Margin=Padding.Empty };
        ConfigureGrid(); resultBody.Controls.Add(grid);
        emptyState.Text="把两个文件夹放到上方，开始查看需要更新的内容。"; emptyState.Dock=DockStyle.Fill; emptyState.TextAlign=ContentAlignment.MiddleCenter; emptyState.ForeColor=UiTheme.Muted; emptyState.BackColor=Color.White;
        resultBody.Controls.Add(emptyState); resultLayout.Controls.Add(resultBody,0,2); results.Controls.Add(resultLayout); workspace.Controls.Add(results,0,4);
        actionFilter.SelectedIndexChanged+=(_,_)=>FilterRows(); search.TextChanged+=(_,_)=>FilterRows();

        var task=new RoundedPanel { Dock=DockStyle.Fill,Padding=new Padding(18,14,18,12),Margin=new Padding(0,16,0,0) };
        var taskLayout=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Margin=Padding.Empty };
        taskLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,44)); taskLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,12)); taskLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var taskTop=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=4,RowCount=1,Margin=Padding.Empty };
        taskTop.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); foreach(int w in new[]{104,136,164})taskTop.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,w));
        status.Text="准备就绪"; status.Dock=DockStyle.Fill; status.TextAlign=ContentAlignment.MiddleLeft; status.AutoEllipsis=true; status.Margin=new Padding(0,0,12,0); status.Font=new Font(Font.FontFamily,11.5F,FontStyle.Bold);
        taskTop.Controls.Add(status,0,0);
        SetupButton(cancel,"中断",()=>{scanCancellation?.Cancel();cancel.Enabled=false;cancel.Text="正在中断";status.Text="正在等待当前磁盘操作安全结束…";}); cancel.Enabled=false; ((ModernButton)cancel).Tone=ButtonTone.Danger;
        SetupButton(records,"打开记录",()=>{if(lastRecord!=null)System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(lastRecord){UseShellExecute=true});}); records.Enabled=false;
        SetupButton(execute,"执行更新",async()=>await Apply()); execute.Enabled=false;
        foreach(var b in new Button[]{cancel,records,execute}) { b.Dock=DockStyle.Fill; b.Margin=new Padding(10,0,0,0); taskTop.Controls.Add(b); }
        taskLayout.Controls.Add(taskTop,0,0);
        busyBar.Dock=DockStyle.Fill; busyBar.Margin=new Padding(0,6,0,0); busyBar.Minimum=0; busyBar.Maximum=1000; busyBar.Style=ProgressBarStyle.Continuous;
        taskLayout.Controls.Add(busyBar,0,1);
        progressInfo.Text="开始后显示进度与预计剩余时间"; progressInfo.ForeColor=UiTheme.Muted; progressInfo.Font=new Font(Font.FontFamily,10.5F); progressInfo.Margin=new Padding(0,5,0,0);
        taskLayout.Controls.Add(progressInfo,0,2); task.Controls.Add(taskLayout); root.Controls.Add(task,0,1);

        master.TextChanged+=(_,_)=>InvalidatePlan(); child.TextChanged+=(_,_)=>InvalidatePlan(); mode.SelectedIndexChanged+=(_,_)=>InvalidatePlan(); comparison.SelectedIndexChanged+=(_,_)=>InvalidatePlan();
        reuseHashes.CheckedChanged+=(_,_)=>InvalidatePlan();trackChanges.CheckedChanged+=(_,_)=>InvalidatePlan();reuseMoves.CheckedChanged+=(_,_)=>InvalidatePlan();
        status.TextChanged+=(_,_)=>tips.SetToolTip(status,status.Text);
        progressTimer.Tick+=(_,_)=>{
            var text=Interlocked.Exchange(ref pendingProgress,null);var work=Interlocked.Exchange(ref pendingWork,null);if(!busy)return;
            if(work!=null)ShowProgress(work);
            if(scanCancellation?.IsCancellationRequested!=true) { if(work!=null)status.Text=work.Stage+" · "+work.Current;else if(text!=null)status.Text=text; }
        };
        FormClosed+=(_,_)=>{progressTimer.Dispose();scanCancellation?.Dispose();tips.Dispose();Engine.CloseIndexes();};
        FormClosing+=(_,e)=>{if(busy){e.Cancel=true;scanCancellation?.Cancel();cancel.Enabled=false;cancel.Text="正在中断";status.Text="正在安全停止，停止后可关闭窗口。";}};
        Shown+=(_,_)=>FitWorkspace(); UpdateActionState();
    }
    Control Field(string caption,ComboBox input)
    {
        var p=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=new Padding(0,0,16,0) };
        p.RowStyles.Add(new RowStyle(SizeType.Absolute,24));p.RowStyles.Add(new RowStyle(SizeType.Percent,100));p.Controls.Add(Caption(caption),0,0);
        input.Dock=DockStyle.Top;input.Margin=new Padding(0,2,0,0);p.Controls.Add(input,0,1);return p;
    }
    void FitWorkspace()
    {
        if(body.ClientSize.Height<=0)return;
        int minimum=(int)workspace.RowStyles.Cast<RowStyle>().Take(4).Sum(x=>x.Height)+Px(220);
        workspace.Height=Math.Max(body.ClientSize.Height,minimum);
    }
    void ToggleAdvanced()
    {
        advancedOpen=!advancedOpen;advancedHost.Visible=advancedOpen;workspace.RowStyles[3].Height=advancedOpen?Px(150):0;advanced.Text=advancedOpen?"收起设置 −":"高级设置 ＋";FitWorkspace();
    }
    Panel FolderCard(string heading,TextBox box,bool source)
    {
        var panel=new RoundedPanel { Dock=DockStyle.Fill,Padding=new Padding(18,12,18,12),Margin=Padding.Empty,AllowDrop=true };
        var rows=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Margin=Padding.Empty };
        rows.RowStyles.Add(new RowStyle(SizeType.Absolute,32));rows.RowStyles.Add(new RowStyle(SizeType.Absolute,36));rows.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var head=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=Padding.Empty };
        head.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,100));head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));head.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,88));
        var role=Caption(heading);role.ForeColor=UiTheme.Ink;head.Controls.Add(role,0,0);
        var badge=Caption(source?"只读来源":"更新目标");badge.ForeColor=source?UiTheme.Accent:Color.FromArgb(15,118,110);badge.TextAlign=ContentAlignment.MiddleRight;badge.Margin=new Padding(0,0,12,0);head.Controls.Add(badge,1,0);
        var browse=new ModernButton { Text="浏览…",Dock=DockStyle.Fill,Margin=Padding.Empty,Font=new Font(Font.FontFamily,10.5F) };head.Controls.Add(browse,2,0);rows.Controls.Add(head,0,0);
        var name=new Label { Text=source?"选择作为标准的文件夹":"选择需要更新的文件夹",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,Font=new Font(Font.FontFamily,14,FontStyle.Bold),AutoEllipsis=true,Margin=Padding.Empty };
        rows.Controls.Add(name,0,1);
        var path=new Panel { Dock=DockStyle.Fill,Padding=new Padding(0,2,0,0),BackColor=Color.White,Margin=Padding.Empty };
        box.Multiline=true;box.WordWrap=true;box.AcceptsReturn=false;box.BorderStyle=BorderStyle.None;box.Dock=DockStyle.Fill;box.BackColor=Color.White;box.PlaceholderText="拖入文件夹，或在这里粘贴路径";box.Font=new Font(Font.FontFamily,10.5F);box.ForeColor=UiTheme.Muted;box.Tag=panel;
        path.Controls.Add(box);rows.Controls.Add(path,0,2);panel.Controls.Add(rows);
        box.TextChanged+=(_,_)=>{
            string value=box.Text.Trim().Trim('"');try{string leaf=System.IO.Path.GetFileName(value.TrimEnd('\\','/'));name.Text=value.Length==0?(source?"选择作为标准的文件夹":"选择需要更新的文件夹"):leaf.Length==0?value:leaf;}catch(ArgumentException){name.Text="请检查文件夹路径";}
            tips.SetToolTip(box,value);tips.SetToolTip(name,value);
        };
        browse.Click+=(_,_)=>{using var dialog=new FolderBrowserDialog { Description=heading,UseDescriptionForTitle=true };if(dialog.ShowDialog(this)==DialogResult.OK)box.Text=dialog.SelectedPath;};
        void Register(Control control)
        {
            control.AllowDrop=true;control.DragEnter+=(_,e)=>e.Effect=!busy&&e.Data?.GetDataPresent(DataFormats.FileDrop)==true?DragDropEffects.Copy:DragDropEffects.None;
            control.DragDrop+=(_,e)=>{if(!busy&&e.Data?.GetData(DataFormats.FileDrop)is string[] paths&&paths.Length==1&&Directory.Exists(paths[0]))box.Text=paths[0];};
            foreach(Control nested in control.Controls)Register(nested);
        }
        Register(panel);return panel;
    }
    void SetupButton(Button button,string text,Action action)
    {
        button.Text=text;button.Size=new Size(140,42);button.Margin=new Padding(0,0,12,0);button.Font=Font;
        button.Click+=(_,_)=>action();
    }
    void ConfigureGrid()
    {
        grid.Dock=DockStyle.Fill;grid.BackgroundColor=Color.White;grid.BorderStyle=BorderStyle.None;grid.AutoGenerateColumns=false;grid.AllowUserToAddRows=false;grid.AllowUserToDeleteRows=false;
        grid.RowHeadersVisible=false;grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;grid.MultiSelect=false;grid.EnableHeadersVisualStyles=false;
        grid.CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal;grid.ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.None;
        grid.ColumnHeadersDefaultCellStyle=new DataGridViewCellStyle { BackColor=Color.FromArgb(248,250,252),ForeColor=UiTheme.Muted,Font=new Font(Font.FontFamily,10.5F),Padding=new Padding(10,0,4,0),SelectionBackColor=Color.FromArgb(248,250,252) };
        grid.ColumnHeadersHeight=40;grid.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing;grid.RowTemplate.Height=46;grid.AllowUserToResizeRows=false;
        grid.GridColor=Color.FromArgb(238,242,247);grid.DefaultCellStyle=new DataGridViewCellStyle { BackColor=Color.White,ForeColor=UiTheme.Ink,SelectionBackColor=Color.FromArgb(239,246,255),SelectionForeColor=UiTheme.Ink,Padding=new Padding(10,0,4,0),Font=Font };
        grid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName="Selected",HeaderText="选中",Width=60 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName="Action",HeaderText="操作",Width=94,ReadOnly=true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName="Path",HeaderText="文件 / 相对路径",AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill,FillWeight=58,ReadOnly=true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName="Detail",HeaderText="处理说明",AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill,FillWeight=42,ReadOnly=true });
        grid.DataError+=(_,e)=>e.ThrowException=false;
        grid.CurrentCellDirtyStateChanged+=(_,_)=>{if(grid.IsCurrentCellDirty)grid.CommitEdit(DataGridViewDataErrorContexts.Commit);};
        grid.CellValueChanged+=(_,_)=>{if(!rebinding)UpdateActionState();};
        grid.CellToolTipTextNeeded+=(_,e)=>{if(e.RowIndex>=0&&e.ColumnIndex>=0)e.ToolTipText=Convert.ToString(grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value);};
        grid.CellPainting+=(_,e)=>{
            if(e.RowIndex<0||e.ColumnIndex!=1)return;
            e.PaintBackground(e.ClipBounds,true);string value=Convert.ToString(e.Value)??"";
            var (ink,fill)=value switch {
                "新增"=>(Color.FromArgb(15,118,110),Color.FromArgb(229,246,241)),
                "更新"=>(UiTheme.Accent,Color.FromArgb(233,240,255)),
                "移动"=>(Color.FromArgb(109,65,181),Color.FromArgb(242,235,255)),
                "冲突"=>(Color.FromArgb(154,87,12),Color.FromArgb(255,245,219)),
                "回收"=>(Color.FromArgb(190,46,62),Color.FromArgb(255,235,238)),
                _=>(UiTheme.Muted,Color.FromArgb(241,244,248)) };
            var rect=new RectangleF(e.CellBounds.Left+Px(8),e.CellBounds.Top+(e.CellBounds.Height-Px(28))/2f,Px(60),Px(28));
            e.Graphics!.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var shape=UiTheme.Round(rect,Px(6));using var brush=new SolidBrush(fill);e.Graphics.FillPath(brush,shape);
            TextRenderer.DrawText(e.Graphics,value,grid.Font,Rectangle.Round(rect),ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);e.Handled=true;
        };
    }
    void BindRows(List<Change> rows) { displayedItems=rows;FilterRows(); }
    void CommitGrid()
    {
        grid.EndEdit();if(grid.DataSource!=null&&BindingContext!=null)BindingContext[grid.DataSource]?.EndCurrentEdit();
    }
    void FilterRows()
    {
        if(rebinding)return;CommitGrid();string kind=actionFilter.Text,term=search.Text.Trim();
        var rows=displayedItems.Where(x=>(kind=="全部操作"||kind=="目录"&&(x.Action is "建目录" or "清空目录")||x.Action==kind)&&(term.Length==0||x.Path.Contains(term,StringComparison.OrdinalIgnoreCase)||x.Detail.Contains(term,StringComparison.OrdinalIgnoreCase))).ToList();
        rebinding=true;try{grid.DataSource=new System.ComponentModel.BindingList<Change>(rows);}finally{rebinding=false;}
        emptyState.Visible=rows.Count==0;if(emptyState.Visible){emptyState.Text=displayedItems.Count==0?"选好两个文件夹，开始查看需要更新的内容。":"没有符合筛选条件的项目。";emptyState.BringToFront();}
        if(displayedItems.Count>0)summary.Text=$"共 {displayedItems.Count:N0} 项差异 · 显示 {rows.Count:N0} 项 · 勾选 {displayedItems.Count(x=>x.Selected&&x.Action!="保留"):N0} 项";
        UpdateActionState();
    }
    void SelectVisible(int selection)
    {
        if(busy)return;
        CommitGrid();rebinding=true;
        try { foreach(DataGridViewRow row in grid.Rows) if(row.DataBoundItem is Change item) item.Selected=selection<0?!item.Selected:selection==1; }
        finally { rebinding=false; }
        grid.Refresh();UpdateActionState();
    }
    void UpdateActionState()
    {
        int selected=plan?.Items.Count(x=>x.Selected&&x.Action!="保留")??0;
        if(plan!=null&&displayedItems.Count>0)summary.Text=$"共 {displayedItems.Count:N0} 项差异 · 显示 {grid.Rows.Count:N0} 项 · 勾选 {selected:N0} 项";
        execute.Enabled=!busy&&plan!=null&&selected>0;execute.Text=selected>0?$"执行更新（{selected:N0}）":"执行更新";
        ((ModernButton)execute).Tone=ButtonTone.Primary;((ModernButton)preview).Tone=plan==null?ButtonTone.Primary:ButtonTone.Secondary;
        selectAll.Enabled=selectNone.Enabled=invertSelection.Enabled=!busy&&grid.Rows.Count>0;
        records.Enabled=!busy&&lastRecord!=null;advanced.Enabled=!busy;actionFilter.Enabled=search.Enabled=!busy;
    }
}

