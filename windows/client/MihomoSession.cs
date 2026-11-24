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
    private static int ReservePort(out TcpListener listener,int port=0) {
        listener=new TcpListener(IPAddress.Loopback,port);
        try{listener.Start();}
        catch(SocketException e){
            listener.Stop();
            if(port==0)throw;
            if(e.SocketErrorCode==SocketError.AddressAlreadyInUse)
                throw new InvalidOperationException("本机代理端口 "+port+" 已被占用。请关闭占用程序，或在设置中更换端口。",e);
            throw new InvalidOperationException(PortUnavailable(port),e);
        }
        if(!SetHandleInformation(listener.Server.Handle,1,0)){listener.Stop();throw new InvalidOperationException("无法隔离本地端口句柄。");}
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
    private static string PortUnavailable(int port){return "本机代理端口 "+port+" 无法监听。请检查端口是否被占用或保留，或在设置中更换端口。";}
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
    private static bool AcceptsSocks(int port,byte auth=2) {
        using(var client=new TcpClient(AddressFamily.InterNetwork))try {
            var task=client.ConnectAsync(IPAddress.Loopback,port);if(!task.Wait(250))return false;
            using(var stream=client.GetStream()){stream.ReadTimeout=250;var hello=new byte[]{5,1,auth};stream.Write(hello,0,hello.Length);return stream.ReadByte()==5&&stream.ReadByte()==auth;}
        }catch(IOException){return false;}catch(AggregateException){return false;}
    }
    internal static bool IsPinned(string path){
        if(!File.Exists(path))return false;
        using(var file=File.OpenRead(path))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(file)).Replace("-","")=="04F8D7FC2B314771E1ECBF15951D59F0E1CB7914503C88CFB6972FF6A824E314";
    }
    private static bool TunUp(){return NetworkInterface.GetAllNetworkInterfaces().Any(n=>n.Name==TunName&&n.OperationalStatus==OperationalStatus.Up&&n.GetIPProperties().UnicastAddresses.Any(a=>a.Address.ToString()=="198.18.0.1"));}
    private const string Selector="FLOWPIVOT-SELECT";
    private string configBase, selectedNode;
    private string[] groupNodes;
    internal bool CanSelect {get;private set;}
    internal MihomoSession(string executable,string root,ParsedSource source,string selected,string engine="Mihomo",string enginePath=null,bool tun=false,int localPort=0,IEnumerable<DomainRoute> domainRoutes=null) {
        Version=Inspect(executable,CoreAsset.Pick("Mihomo"));
        var node=source.Nodes.SingleOrDefault(n=>n.Id==selected);
        if(node==null)throw new InvalidOperationException("请选择有效节点。");
        TcpListener a,b;
        CanSelect=node.Native==null;
        Port=ReservePort(out a,localPort);
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
            configBase=template+Subscription.ProxiesYaml(nodes);
            selectedNode=selected;
            if(CanSelect)groupNodes=source.Nodes.Select(n=>n.Id).ToArray();
            File.WriteAllText(config,Config(domainRoutes),new UTF8Encoding(false));
            a.Stop();b.Stop();
            tunRequested=tun;
            process=Launch(executable,"-d \""+folder+"\" -f \""+config+"\"");
            bool ready=false;
            for(int i=0;i<36 && Alive;i++) {
                try {if(LocalData.Text(Get("/version",700),"version").Length>0){ready=true;break;}} catch (WebException) {}
                Thread.Sleep(120);
            }
            if(!ready)throw new InvalidOperationException("Mihomo 未能启动。请检查订阅是否兼容、核心文件是否正确。");
            if(localPort!=0&&!AcceptsSocks(Port,0))throw new InvalidOperationException(PortUnavailable(Port));
            if(tun){
                for(int i=0;i<40 && Alive && !TunUp();i++)Thread.Sleep(150);
                if(!Alive||!TunUp())throw new InvalidOperationException("TUN 网卡未能就绪，已停止。系统代理模式仍可用。");
            }
            File.Delete(config);
        } catch {Dispose();throw;}
        finally {a.Stop();b.Stop();}
    }
    private string Config(IEnumerable<DomainRoute> domainRoutes) {
        string groups=CanSelect?"proxy-groups:\n  - name: "+Selector+"\n    type: select\n    proxies: ["+string.Join(", ",new[]{selectedNode}.Concat(groupNodes.Where(id=>id!=selectedNode)))+"]\n":"";
        return configBase+groups+TargetRouting.Yaml(domainRoutes,CanSelect?Selector:selectedNode);
    }
    internal void ApplyRoutes(IEnumerable<DomainRoute> domainRoutes) {
        var request=Controller("/configs",5000);request.Method="PUT";request.ContentType="application/json";
        var payload=Encoding.UTF8.GetBytes(LocalData.Json.Serialize(new{payload=Config(domainRoutes)}));request.ContentLength=payload.Length;
        try{
            using(var stream=request.GetRequestStream())stream.Write(payload,0,payload.Length);
            using(request.GetResponse()){}
        }catch(WebException e){if(e.Response!=null)e.Response.Dispose();throw;}
        if(CanSelect&&LocalData.Text(Get("/proxies/"+Selector,2000),"now")!=selectedNode)
            throw new InvalidOperationException("核心未保留当前节点。");
    }
    internal IDictionary<string,object> Get(string path,int timeout=3000) {
        var request=Controller(path,timeout);
        using(var response=request.GetResponse())using(var stream=response.GetResponseStream())using(var body=new MemoryStream()) {
            var buffer=new byte[8192];int read;
            while((read=stream.Read(buffer,0,buffer.Length))>0){if(body.Length+read>4000000)throw new IOException("Controller response overflow");body.Write(buffer,0,read);}
            return LocalData.Parse(Encoding.UTF8.GetString(body.ToArray()));
        }
    }
    internal void Select(string id){
        if(!CanSelect)throw new InvalidOperationException("当前核心不支持热切换。");
        var path="/proxies/"+Selector;
        var request=Controller(path,2000);request.Method="PUT";request.ContentType="application/json";
        var payload=Encoding.UTF8.GetBytes(LocalData.Json.Serialize(new{name=id}));request.ContentLength=payload.Length;
        using(var stream=request.GetRequestStream())stream.Write(payload,0,payload.Length);
        using(request.GetResponse()){}
        if(LocalData.Text(Get(path,2000),"now")!=id)throw new InvalidOperationException("核心未确认切换。");
        selectedNode=id;
    }
    internal long Delay(string id,Uri target) {
        return LocalData.Number(Get("/proxies/"+Uri.EscapeDataString(id)+"/delay?timeout=4200&url="+Uri.EscapeDataString(target.AbsoluteUri),5500),"delay");
    }
    internal void CloseConnection(string id) {
        Guid valid;
        if(!Guid.TryParseExact(id,"D",out valid))throw new InvalidOperationException("无效的连接编号。");
        if(!LocalData.Rows(Get("/connections"),"connections").Any(c=>LocalData.Text(c,"id")==id))return;
        var request=Controller("/connections/"+valid.ToString("D"),2500);request.Method="DELETE";
        try{using(request.GetResponse()){}}
        catch(WebException e){
            using(var response=e.Response as HttpWebResponse){
                if(response==null||response.StatusCode!=HttpStatusCode.NotFound)
                    throw new InvalidOperationException("暂时无法断开该连接。");
            }
        }
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
