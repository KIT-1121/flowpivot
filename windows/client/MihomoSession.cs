using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

internal sealed class MihomoSession : IDisposable
{
    private Process process,sidecar;
    private bool disposed,tunRequested;
    private const string TunName="FlowPivot-TUN";
    private readonly string folder, secret;
    private readonly int api;
    private readonly IntPtr job;
    internal readonly int Port;
    internal readonly string Version;
    internal bool Alive { get {return !disposed && process!=null && !process.HasExited && (sidecar==null || !sidecar.HasExited);} }
    internal IWebProxy Proxy { get {return new WebProxy("http://127.0.0.1:"+Port);}}

    [StructLayout(LayoutKind.Sequential)] private struct Limits {
        public long processTime, jobTime; public uint flags;
        public UIntPtr minMemory,maxMemory; public uint activeLimit; public UIntPtr affinity;
        public uint priority,schedule;
        public ulong readOps,writeOps,otherOps,readBytes,writeBytes,otherBytes;
        public UIntPtr perProcessMemory,perJobMemory,peakProcessMemory,peakJobMemory;
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr CreateJobObject(IntPtr attrs,string name);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool SetInformationJobObject(IntPtr job,int cls,ref Limits value,uint length);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool SetHandleInformation(IntPtr handle,uint mask,uint flags);
    private static int ReservePort(out TcpListener listener) {
        listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
        if(!SetHandleInformation(listener.Server.Handle,1,0)){listener.Stop();throw new InvalidOperationException("无法隔离本地端口句柄。");}
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
    private static string NewSecret() {var bytes=new byte[32];using(var rng=RandomNumberGenerator.Create())rng.GetBytes(bytes);return BitConverter.ToString(bytes).Replace("-","");}
    internal static string Inspect(string executable,CoreAsset asset) {
        if (!File.Exists(executable)) throw new InvalidOperationException("内置 "+asset.Id+" 内核文件缺失。请重新安装或完整解压 FlowPivot。");
        using(var file=File.OpenRead(executable))using(var pe=new BinaryReader(file)){
            if(file.Length<64||pe.ReadUInt16()!=0x5a4d)throw new InvalidOperationException("不是有效的 Windows EXE。");
            file.Position=60;int offset=pe.ReadInt32();
            if(offset<64||offset>file.Length-6)throw new InvalidOperationException("不是有效的 Windows EXE。");
            file.Position=offset;if(pe.ReadUInt32()!=0x4550||pe.ReadUInt16()!=0x8664)throw new InvalidOperationException("内核不是 Windows x64 版本，请重新安装 FlowPivot。");
        }
        var info=new ProcessStartInfo(executable,asset.Id=="Mihomo"?"-v":"version") {WorkingDirectory=Path.GetDirectoryName(executable),UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        using(var proc=Process.Start(info)) {
            var output=proc.StandardOutput.ReadToEndAsync();proc.StandardError.ReadToEndAsync();
            if(!proc.WaitForExit(4000)){proc.Kill();throw new InvalidOperationException("内核版本检查超时。");}
            if(!output.Wait(1000))throw new InvalidOperationException("无法读取内核版本。");
            var label=Regex.Match(output.Result,asset.Pattern,RegexOptions.IgnoreCase);
            if(!label.Success || proc.ExitCode!=0)throw new InvalidOperationException("未识别为 "+asset.Id+" Windows x64 核心，或缺少其文件依赖。");
            return "v"+label.Groups[1].Value.TrimStart('v','V');
        }
    }
    private Process Launch(string path,string args) {
        var running=Process.Start(new ProcessStartInfo(path,args){WorkingDirectory=folder,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true});
        if(running==null)throw new InvalidOperationException("无法启动内核。");
        try {
            if(!AssignProcessToJobObject(job,running.Handle))throw new InvalidOperationException("无法管理代理内核进程，已取消启动。");
            // Drain output; never write subscription details or API secrets to logs.
            running.BeginOutputReadLine();running.BeginErrorReadLine();return running;
        }catch{try{if(!running.HasExited)running.Kill();}finally{running.Dispose();}throw;}
    }
    private static bool AcceptsSocks(int port) {
        using(var client=new TcpClient(AddressFamily.InterNetwork))try {
            var task=client.ConnectAsync(IPAddress.Loopback,port);if(!task.Wait(250))return false;
            using(var stream=client.GetStream()){stream.ReadTimeout=250;var hello=new byte[]{5,1,2};stream.Write(hello,0,hello.Length);return stream.ReadByte()==5&&stream.ReadByte()==2;}
        }catch(IOException){return false;}catch(AggregateException){return false;}
    }
    internal static bool IsPinned(string path){
        if(!File.Exists(path))return false;
        using(var file=File.OpenRead(path))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(file)).Replace("-","")=="04F8D7FC2B314771E1ECBF15951D59F0E1CB7914503C88CFB6972FF6A824E314";
    }
    private static bool TunUp(){return NetworkInterface.GetAllNetworkInterfaces().Any(n=>n.Name==TunName&&n.OperationalStatus==OperationalStatus.Up&&n.GetIPProperties().UnicastAddresses.Any(a=>a.Address.ToString()=="198.18.0.1"));}
    internal MihomoSession(string executable,string root,ParsedSource source,string selected,string engine="Mihomo",string enginePath=null,bool tun=false) {
        Version=Inspect(executable,CoreAsset.Pick("Mihomo"));
        var node=source.Nodes.SingleOrDefault(n=>n.Id==selected);
        if(node==null)throw new InvalidOperationException("请选择有效节点。");
        TcpListener a,b;
        Port=ReservePort(out a);
        try {api=ReservePort(out b);} catch {a.Stop();throw;}
        folder=Path.Combine(root,"runtime-"+Guid.NewGuid().ToString("N"));
        secret=NewSecret();
        try {
            Directory.CreateDirectory(folder);
            job=CreateJobObject(IntPtr.Zero,null);
            var limits=new Limits{flags=0x2000};
            if(job==IntPtr.Zero || !SetInformationJobObject(job,9,ref limits,(uint)Marshal.SizeOf(typeof(Limits)))) throw new InvalidOperationException("无法保护代理内核进程，已取消启动。");
            var nodes=source.Nodes.AsEnumerable();
            if(node.Native!=null){
                if(!File.Exists(enginePath))throw new InvalidOperationException("内置 "+engine+" 内核文件缺失。请重新安装或完整解压 FlowPivot。");
                TcpListener upstream;int port=ReservePort(out upstream);upstream.Stop();
                string password=NewSecret().Substring(0,32), user="flowpivot";
                var nativeFile=Path.Combine(folder,"outbound.json");
                File.WriteAllText(nativeFile,NativeConfig.Build(node,engine,port,user,password),new UTF8Encoding(false));
                sidecar=Launch(enginePath,"run -c \""+nativeFile+"\"");
                bool listening=false;
                for(int i=0;i<40&&!sidecar.HasExited;i++){if(AcceptsSocks(port)){listening=true;break;}Thread.Sleep(90);}
                if(!listening)throw new InvalidOperationException(engine+" 未能启动，请检查核心及此节点的版本兼容性。");
                File.Delete(nativeFile);
                var proxy=new YamlDotNet.RepresentationModel.YamlMappingNode();
                proxy.Add("name",node.Id);proxy.Add("type","socks5");proxy.Add("server","127.0.0.1");proxy.Add("port",port.ToString());proxy.Add("username",user);proxy.Add("password",password);if(engine!="sing-box")proxy.Add("udp","true");
                nodes=new[]{new ProxyNode{Data=proxy}};
            }
            var config=Path.Combine(folder,"config.yaml");
            string template="mixed-port: "+Port+"\nallow-lan: false\nbind-address: 127.0.0.1\nmode: rule\nlog-level: silent\nfind-process-mode: off\nexternal-controller: 127.0.0.1:"+api+"\nsecret: '"+secret+"'\ntun: {enable: false}\nprofile: {store-selected: false, store-fake-ip: false}\n";
            if(tun){
                if(TunUp())throw new InvalidOperationException("检测到已有 FlowPivot TUN。请先退出原连接或重启 Windows。");
                template=template.Replace("tun: {enable: false}","tun: {enable: true, device: "+TunName+", inet4-address: [198.18.0.1/30], stack: mixed, auto-route: true, auto-detect-interface: true, strict-route: true, dns-hijack: [any:53, tcp://any:53]}\n"+
                    "dns: {enable: true, enhanced-mode: fake-ip, fake-ip-range: 198.18.0.1/16, default-nameserver: [1.1.1.1], nameserver: [https://1.1.1.1/dns-query], proxy-server-nameserver: [https://1.1.1.1/dns-query]}");
            }
            template+=Subscription.ProxiesYaml(nodes)+"rules:\n  - DOMAIN,localhost,DIRECT\n  - IP-CIDR,127.0.0.0/8,DIRECT,no-resolve\n  - IP-CIDR6,::1/128,DIRECT,no-resolve\n  - MATCH,"+selected+"\n";
            File.WriteAllText(config,template,new UTF8Encoding(false));
            a.Stop();b.Stop();
            tunRequested=tun;
            process=Launch(executable,"-d \""+folder+"\" -f \""+config+"\"");
            bool ready=false;
            for(int i=0;i<36 && Alive;i++) {
                try {if(LocalData.Text(Get("/version",700),"version").Length>0){ready=true;break;}} catch (WebException) {}
                Thread.Sleep(120);
            }
            if(!ready)throw new InvalidOperationException("Mihomo 未能启动。请检查订阅是否兼容、核心文件是否正确。");
            if(tun){
                for(int i=0;i<40 && Alive && !TunUp();i++)Thread.Sleep(150);
                if(!Alive||!TunUp())throw new InvalidOperationException("TUN 网卡未能就绪，已停止。系统代理模式仍可用。");
            }
            File.Delete(config);
        } catch {Dispose();throw;}
        finally {a.Stop();b.Stop();}
    }
    internal IDictionary<string,object> Get(string path,int timeout=3000) {
        var request=Controller(path,timeout);
        using(var response=request.GetResponse())using(var stream=response.GetResponseStream())using(var body=new MemoryStream()) {
            var buffer=new byte[8192];int read;
            while((read=stream.Read(buffer,0,buffer.Length))>0){if(body.Length+read>4000000)throw new IOException("Controller response overflow");body.Write(buffer,0,read);}
            return LocalData.Parse(Encoding.UTF8.GetString(body.ToArray()));
        }
    }
    internal long Delay(string id,Uri target) {
        return LocalData.Number(Get("/proxies/"+Uri.EscapeDataString(id)+"/delay?timeout=4200&url="+Uri.EscapeDataString(target.AbsoluteUri),5500),"delay");
    }
    private HttpWebRequest Controller(string path,int timeout){
        var request=(HttpWebRequest)WebRequest.Create("http://127.0.0.1:"+api+path);
        request.Proxy=null;request.AllowAutoRedirect=false;request.Timeout=timeout;request.ReadWriteTimeout=timeout;
        request.Headers["Authorization"]="Bearer "+secret;return request;
    }
    private void DisableTun(){
        if(process==null||process.HasExited)return;
        try {
            var request=Controller("/configs",2500);request.Method="PATCH";request.ContentType="application/json";
            byte[] payload=Encoding.UTF8.GetBytes("{\"tun\":{\"enable\":false}}");request.ContentLength=payload.Length;
            using(var outstream=request.GetRequestStream())outstream.Write(payload,0,payload.Length);
            using(request.GetResponse()){}
        }catch(WebException){}catch(IOException){}
    }
    public void Dispose() {
        if(disposed)return;disposed=true;
        if(tunRequested)DisableTun();
        if(job!=IntPtr.Zero)CloseHandle(job);
        foreach(var child in new[]{process,sidecar})if(child!=null) {
            try {if(!child.HasExited && !child.WaitForExit(1000)){child.Kill();child.WaitForExit(1000);}}catch(InvalidOperationException){}finally{child.Dispose();}
        }
        if(folder!=null && Directory.Exists(folder)) {
            try {foreach(var name in new[]{"config.yaml","outbound.json","cache.db","cache.db-journal"})File.Delete(Path.Combine(folder,name));if(!Directory.EnumerateFileSystemEntries(folder).Any())Directory.Delete(folder);} catch(IOException){} catch(UnauthorizedAccessException){}
        }
    }
}
