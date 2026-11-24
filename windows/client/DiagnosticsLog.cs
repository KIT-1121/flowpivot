using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;

// Only fixed event descriptions and exception categories are recorded. Never pass request data here.
internal sealed class DiagnosticsLog {
    private const int TailBytes=64*1024;
    private static readonly UTF8Encoding Utf8=new UTF8Encoding(false);
    internal readonly string DefaultDirectory;
    internal string CustomDirectory;
    internal string DirectoryPath {get{return CustomDirectory??DefaultDirectory;}}
    private bool writeFailed;
    internal DiagnosticsLog(string root,string custom){DefaultDirectory=Path.Combine(root,"logs");CustomDirectory=custom;}
    private static string DateName(DateTime local){return "FlowPivot-"+local.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)+".log";}
    internal void Write(string description,Exception error=null){
        try{
            var time=DateTimeOffset.Now;
            Directory.CreateDirectory(DirectoryPath);
            string category=error==null?"":error is WebException?" [网络·"+((WebException)error).Status+"]":" ["+error.GetType().Name+"]";
            File.AppendAllText(Path.Combine(DirectoryPath,DateName(time.DateTime)),time.ToString("HH:mm:ss zzz",CultureInfo.InvariantCulture)+" "+(error==null?"INFO ":"WARN ")+description+category+Environment.NewLine,Utf8);
            writeFailed=false;
        }catch(Exception e){if(!IsFileError(e))throw;writeFailed=true;}
    }
    internal static bool IsFileError(Exception e){return e is IOException||e is UnauthorizedAccessException||e is ArgumentException||e is NotSupportedException||e is System.Security.SecurityException;}
    internal string CheckDestination(string chosen){
        try{
            string destination=chosen==null?DefaultDirectory:Path.GetFullPath(chosen);
            if(destination.Length<3||!char.IsLetter(destination[0])||destination[1]!=':'||destination[2]!='\\'||new DriveInfo(destination).DriveType==DriveType.Network)
                throw new InvalidOperationException("请选择本机磁盘上的文件夹。");
            Directory.CreateDirectory(destination);
            string probe=Path.Combine(destination,".flowpivot-"+Guid.NewGuid().ToString("N")+".tmp");
            using(var file=new FileStream(probe,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.DeleteOnClose))file.WriteByte(0);
            return string.Equals(destination.TrimEnd('\\'),DefaultDirectory.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase)?null:destination;
        }catch(Exception e){if(!IsFileError(e))throw;throw new InvalidOperationException("无法写入该日志目录，请选择有写入权限的本机文件夹。");}
    }
    internal object Read(){
        string name=DateName(DateTime.Today), text="", warning=writeFailed?"最近一次日志写入失败，请检查目录和权限。":"";
        try{
            string path=Path.Combine(DirectoryPath,name);
            try{
                using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){
                    long start=Math.Max(0,stream.Length-TailBytes);
                    stream.Seek(start,SeekOrigin.Begin);
                    var bytes=new byte[TailBytes];int count=0,n;
                    while(count<bytes.Length&&(n=stream.Read(bytes,count,bytes.Length-count))>0)count+=n;
                    int first=start==0?0:Array.IndexOf(bytes,(byte)'\n',0,count)+1;
                    text=(start>0?"（仅显示今天最近的记录）\r\n":"")+Utf8.GetString(bytes,first,count-first);
                }
            }catch(FileNotFoundException){}catch(DirectoryNotFoundException){}
        }catch(Exception e){if(!IsFileError(e))throw;warning="无法读取日志，请检查目录和权限。";}
        return new{directory=DirectoryPath,isDefault=CustomDirectory==null,file=name,text,warning};
    }
}
