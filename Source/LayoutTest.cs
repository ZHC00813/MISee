namespace GreenFolder;

public partial class MainForm
{
    public void EnableLayoutTest(string output)
    {
        testMode=true;Opacity=0;ShowInTaskbar=false;
        Shown+=async(_,_)=>{
            var notes=new List<string>();
            void Check(bool ok,string title) {if(!ok)throw new Exception("FAIL: "+title);notes.Add("PASS: "+title);}
            async Task Save(string suffix)
            {
                PerformLayout();FitWorkspace();await Task.Delay(120);
                using var bitmap=new Bitmap(Controls[0].Width,Controls[0].Height);Controls[0].DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));
                bitmap.Save(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(output)!,suffix+".png"));
            }
            bool Fits(Control parent,Control child) => child.Left>=0 && child.Top>=0 && child.Right<=parent.ClientSize.Width+1 && child.Bottom<=parent.ClientSize.Height+1;
            try
            {
                master.Text="F:\\文件\\项目设计资料\\标准资料库";child.Text="E:\\文件\\项目设计资料\\需要更新的资料";
                var samples=new List<Change> {
                    new(){Action="新增",Path="产品资料\\用户手册.pdf",Detail="补充母文件夹中的缺失文件"},
                    new(){Action="更新",Path="项目预算.xlsx",Detail="母版本较新，更新前备份原版本"},
                    new(){Action="移动",Path="合同\\采购合同.pdf",Detail="复用子文件夹中已有内容"},
                    new(){Action="冲突",Path="设计方案.docx",Detail="保留子版本，母版本另存供选择"},
                    new(){Action="保留",Path="个人备注.txt",Detail="子文件夹独有资料，原处保留",Selected=false}
                };
                plan=new Plan {Master=master.Text,Child=child.Text,Items=samples};BindRows(samples);
                Check(Font.SizeInPoints>=11.5F && grid.RowTemplate.Height>=46,"正文与表格行高已放大");
                Check(!advancedOpen&&!advancedHost.Visible,"默认仅显示常用设置");
                Check(execute.Enabled&&execute.Tone==ButtonTone.Primary&&preview.Tone==ButtonTone.Secondary,"生成计划后执行更新成为主要操作");
                Check(Fits(preview.Parent!,preview)&&Fits(advanced.Parent!,advanced)&&preview.Height>=Px(40),"开始比对和高级设置按钮完整显示，点击区域足够大");
                Check(!cancel.Enabled&&!records.Enabled,"未运行时中断灰显，没有记录时记录入口灰显");
                await Save("界面-差异预览");
                advanced.PerformClick();await Save("界面-高级设置");
                Check(advancedHost.Visible&&recheck.Visible&&quick.Visible,"展开后完整呈现校验、缓存和一键更新选项");
                Check(Fits(quick.Parent!,quick)&&Fits(recheck.Parent!,recheck),"高级设置内容没有被容器裁切");
                Check(recordsInChild.Visible&&Fits(recordsInChild.Parent!,recordsInChild),"记录位置选项完整显示");
                ClientSize=new Size(980,680);await Save("界面-紧凑窗口");
                Check(Fits(Controls[0],execute.Parent!.Parent!.Parent!),"紧凑窗口下底部任务区仍位于可见区域");
                Check(Fits(execute.Parent!,execute)&&Fits(cancel.Parent!,cancel),"紧凑窗口下更新与中断按钮均可见");
                Check(Fits(preview.Parent!,preview)&&Fits(advanced.Parent!,advanced),"紧凑窗口下常用设置按钮未被裁切");
                Check(workspace.Height>=body.ClientSize.Height,"空间不足时内容区域可滚动");
                advanced.PerformClick();ClientSize=new Size(1220,860);await Task.Delay(100);
                actionFilter.SelectedItem="冲突";Check(grid.Rows.Count==1&&Convert.ToString(grid.Rows[0].Cells[1].Value)=="冲突","冲突筛选显示正确的原始项目");
                actionFilter.SelectedIndex=0;search.Text="预算";Check(grid.Rows.Count==1,"中文路径搜索正确");search.Clear();
                ShowProgress(new WorkProgress("更新文件","项目资料\\视频.mp4",.42,TimeSpan.FromMinutes(8),8L*1024*1024*1024,3L*1024*1024*1024,30));
                status.Text="正在更新 · 项目资料\\视频.mp4";cancel.Enabled=true;execute.Enabled=false;
                await Save("界面-任务进度");
                Check(progressInfo.Text.Contains("42.0%")&&progressInfo.Text.Contains("8 分"),"任务进度集中显示百分比与预计剩余时间");
                notes.Add("布局、筛选与任务状态检查通过；未修改用户真实资料。未切换实际系统 DPI，真实缩放仍需在对应显示设置下验证。");
            }
            catch(Exception ex){notes.Add(ex.ToString());Environment.ExitCode=1;}
            finally{File.WriteAllLines(output,notes);Close();}
        };
    }
}
