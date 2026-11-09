namespace GreenFolder;

public static class SizeRuleTest
{
    public static void Run(string output)
    {
        var notes = new List<string>();
        void Check(bool ok, string message) { if(!ok)throw new Exception(message);notes.Add("PASS: "+message); }
        string root=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(output)!,"size-rule-"+Guid.NewGuid().ToString("N"));
        string m=System.IO.Path.Combine(root,"master"),c=System.IO.Path.Combine(root,"child");
        Directory.CreateDirectory(m);Directory.CreateDirectory(c);
        try {
            foreach(var name in new[]{"newer.txt","older.txt","same-time.txt"}) {
                File.WriteAllText(System.IO.Path.Combine(m,name),"AAAA");File.WriteAllText(System.IO.Path.Combine(c,name),"BBBB");
                File.SetLastWriteTimeUtc(System.IO.Path.Combine(m,name),new DateTime(2026,1,2,0,0,0,DateTimeKind.Utc));
                File.SetLastWriteTimeUtc(System.IO.Path.Combine(c,name),new DateTime(2026,1,name=="newer.txt"?1:name=="older.txt"?3:2,0,0,0,DateTimeKind.Utc));
            }
            File.WriteAllText(System.IO.Path.Combine(m,"update.txt"),"longer");File.WriteAllText(System.IO.Path.Combine(c,"update.txt"),"old");
            File.SetLastWriteTimeUtc(System.IO.Path.Combine(m,"update.txt"),new DateTime(2026,1,3));File.SetLastWriteTimeUtc(System.IO.Path.Combine(c,"update.txt"),new DateTime(2026,1,1));
            File.WriteAllText(System.IO.Path.Combine(m,"missing.txt"),"AAAA");
            foreach(ComparisonKind kind in Enum.GetValues<ComparisonKind>()) {
                Engine.CloseIndexes();
                var plan=Engine.Analyze(m,c,"原处保留",new CompareOptions(kind,false,false,false));
                Check(plan.Items.Count==2&&plan.Items.Any(x=>x.Path=="update.txt"&&x.Action=="更新")&&plan.Items.Any(x=>x.Path=="missing.txt"&&x.Action=="新增"),kind+"：同路径同大小忽略时间和指纹，大小不同与路径缺失仍报告");
            }
            var full=Engine.Analyze(m,c,"原处保留",new CompareOptions(ComparisonKind.Quick,false,false,false));
            Engine.Execute(full,new ExecutionOptions(true,true));
            Check(File.ReadAllText(System.IO.Path.Combine(c,"newer.txt"))=="BBBB","同大小文件在完整复核更新后保持原内容");
            Check(File.ReadAllText(System.IO.Path.Combine(c,"update.txt"))=="longer"&&File.Exists(System.IO.Path.Combine(c,"missing.txt")),"完整复核可执行大小不同的更新和缺失补全");
            Check(File.ReadAllText(System.IO.Path.Combine(m,"newer.txt"))=="AAAA","母本保持只读");
        } catch(Exception ex){notes.Add(ex.ToString());Environment.ExitCode=1;}
        finally {Engine.CloseIndexes();File.WriteAllLines(output,notes);}
    }
}
