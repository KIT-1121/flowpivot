using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

// Bundled in Setup only. Never invoked by the portable client.
internal static class UpgradeHelper {
    private static readonly string[] bundled={
        @"cores\Mihomo\mihomo-windows-amd64-compatible.exe", @"cores\Xray\xray.exe",
        @"cores\sing-box\sing-box.exe", @"cores\V2Fly\v2ray.exe"
    };
    private static bool Bundle(string root){
        return File.Exists(Path.Combine(root,"FlowPivot.exe"))&&File.Exists(Path.Combine(root,@"web\index.html"))&&File.Exists(Path.Combine(root,@"web\client.js"));
    }
    private static int Main(string[] args){
        if(args.Length!=1)return 2;
        try{
            int session=Process.GetCurrentProcess().SessionId;
            var roots=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if(args[0].Length!=0&&Bundle(args[0]))roots.Add(Path.GetFullPath(args[0]));
            var clients=new List<Process>();
            try{
                foreach(var p in Process.GetProcessesByName("FlowPivot")){
                    clients.Add(p);
                    if(p.HasExited||p.SessionId!=session)continue;
                    string path=p.MainModule.FileName;
                    if(!string.Equals(Path.GetFileName(path),"FlowPivot.exe",StringComparison.OrdinalIgnoreCase)||!Bundle(Path.GetDirectoryName(path)))continue;
                    roots.Add(Path.GetDirectoryName(path));
                    // Open and pin the verified process before requesting shutdown.
                    if(p.Handle==IntPtr.Zero)return 2;
                }
                if(UpgradeSignals.Signal())
                    for(int i=0;i<40&&UpgradeSignals.ClientRunning();i++)Thread.Sleep(200);
                foreach(var p in clients){
                    if(p.HasExited||p.SessionId!=session)continue;
                    string path=p.MainModule.FileName;
                    if(!string.Equals(Path.GetFileName(path),"FlowPivot.exe",StringComparison.OrdinalIgnoreCase)||!roots.Contains(Path.GetDirectoryName(path)))continue;
                    if(!Stop(p))return 2;
                }
            }finally{foreach(var p in clients)p.Dispose();}
            if(UpgradeSignals.ClientRunning())return 2;
            foreach(var core in bundled){
                foreach(var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(core))){
                    using(p){
                        if(p.HasExited||p.SessionId!=session)continue;
                        // Only stop exact paths in verified FlowPivot bundles.
                        string path;
                        try{path=p.MainModule.FileName;}catch(System.ComponentModel.Win32Exception){continue;}
                        foreach(var root in roots)
                            if(string.Equals(path,Path.Combine(root,core),StringComparison.OrdinalIgnoreCase)&&!Stop(p))return 2;
                    }
                }
            }
            var profile=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"FlowPivot");
            try{new WindowsProxy(profile).Recover();}
            catch(Exception){return 5;} // Setup can proceed without overwriting an unknown proxy configuration.
            return 0;
        }catch(Exception){return 2;}
    }
    private static bool Stop(Process process){
        if(process.HasExited)return true;
        if(process.Handle==IntPtr.Zero)return false;
        try{process.Kill();}catch(InvalidOperationException){return process.HasExited;}
        return process.WaitForExit(4000);
    }
}
