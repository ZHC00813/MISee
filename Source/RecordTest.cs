namespace GreenFolder;

public static class RecordTest
{
    public static void Run(string output)
    {
        var notes=new List<string>();
        void Check(bool ok,string message){if(!ok)throw new Exception(message);notes.Add("PASS: "+message);}
        string root=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(output)!,"record-test-"+Guid.NewGuid().ToString("N"));
        string prior=Engine.LocalRecordsDirectory;
        Engine.LocalRecordsDirectory=System.IO.Path.Combine(root,"local");
        try {
            foreach(bool inside in new[]{true,false}) {
                string m=System.IO.Path.Combine(root,inside?"inside-m":"local-m"),c=System.IO.Path.Combine(root,inside?"inside-c":"local-c");
                Directory.CreateDirectory(m);Directory.CreateDirectory(c);
                File.WriteAllText(System.IO.Path.Combine(m,"update.txt"),"new version");File.WriteAllText(System.IO.Path.Combine(c,"update.txt"),"old");
                File.SetLastWriteTimeUtc(System.IO.Path.Combine(m,"update.txt"),new DateTime(2026,1,3));File.SetLastWriteTimeUtc(System.IO.Path.Combine(c,"update.txt"),new DateTime(2026,1,1));
                File.WriteAllText(System.IO.Path.Combine(m,"conflict.txt"),"mother");File.WriteAllText(System.IO.Path.Combine(c,"conflict.txt"),"child");
                File.SetLastWriteTimeUtc(System.IO.Path.Combine(m,"conflict.txt"),new DateTime(2026,1,1));File.SetLastWriteTimeUtc(System.IO.Path.Combine(c,"conflict.txt"),new DateTime(2026,1,1));
                var plan=Engine.Analyze(m,c,"原处保留");
                string record=Engine.Execute(plan,new ExecutionOptions(false,true,inside));
                Check(record.StartsWith(inside?System.IO.Path.Combine(c,Engine.Records):Engine.LocalRecordsDirectory),inside+"：记录使用指定位置");
                Check(Directory.Exists(System.IO.Path.Combine(c,Engine.Records))==inside,inside+"：子目录不产生未请求的记录目录");
                Check(File.Exists(System.IO.Path.Combine(record,"执行记录.txt"))&&File.Exists(System.IO.Path.Combine(record,"操作计划.json")),inside+"：日志和计划完整保存");
                Check(File.ReadAllText(System.IO.Path.Combine(record,"更新前备份","update.txt"))=="old"&&File.ReadAllText(System.IO.Path.Combine(c,"update.txt"))=="new version",inside+"：更新前备份与实际写入正确");
                Check(File.ReadAllText(System.IO.Path.Combine(record,"同日期冲突_母版本","conflict.txt"))=="mother"&&File.ReadAllText(System.IO.Path.Combine(c,"conflict.txt"))=="child",inside+"：冲突保留双方");
                Check(!Engine.Analyze(m,c,"原处保留").Items.Any(x=>x.Path.StartsWith(Engine.Records)),inside+"：再次比对不包含记录");
                File.WriteAllText(System.IO.Path.Combine(m,"later.txt"),"new");
                var next=Engine.Analyze(m,c,"原处保留");
                string nextRecord=Engine.Execute(next,new ExecutionOptions(false,true,false));
                Check(Directory.Exists(System.IO.Path.Combine(c,Engine.Records))==inside&&nextRecord.StartsWith(Engine.LocalRecordsDirectory),inside+"：切换关闭后保留旧记录，新记录转存本机");
                Check(File.ReadAllText(System.IO.Path.Combine(m,"update.txt"))=="new version"&&!Directory.Exists(System.IO.Path.Combine(m,Engine.Records)),inside+"：母本未写入记录");
                Engine.LocalRecordsDirectory=System.IO.Path.Combine(c,"bad-records");
                bool refused=false;try{Engine.Execute(Engine.Analyze(m,c,"原处保留"),new ExecutionOptions(false,true,false));}catch(IOException){refused=true;}
                Check(refused&&!Directory.Exists(Engine.LocalRecordsDirectory),inside+"：拒绝与资料目录重叠的本机记录位置");
                Engine.LocalRecordsDirectory=System.IO.Path.Combine(root,"local");
            }
            Check(new ExecutionOptions().RecordsInChild,"默认保留旧版目录内记录行为");
        }catch(Exception ex){notes.Add(ex.ToString());Environment.ExitCode=1;}
        finally{Engine.CloseIndexes();Engine.LocalRecordsDirectory=prior;File.WriteAllLines(output,notes);}
    }
}
