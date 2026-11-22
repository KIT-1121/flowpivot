using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Principal;
using Microsoft.Win32;
using System.Threading.Tasks;

internal sealed class ClientModel
{
    public List<SourceRecord> Sources { get; set; }
    public string Source { get; set; }
    public string Node { get; set; }
    public string Preferred { get; set; }
    public bool CloseToTray { get; set; }

}
internal sealed class ClientService : IDisposable
{
    private readonly string root, store, cores;
    private readonly WindowsProxy system;
    private readonly ClientModel model;
    private readonly Dictionary<string,ParsedSource> parsed=new Dictionary<string,ParsedSource>();
    private readonly Dictionary<string,IDictionary<string,object>> delays=new Dictionary<string,IDictionary<string,object>>();
    private MihomoSession session;
    private bool ownsSystem;
    private readonly bool allowStartup;
    private string activeMode,activeEngine;
    private long priorUp,priorDown,rateUp,rateDown,totalUp,totalDown;
    private readonly Stopwatch rates=Stopwatch.StartNew();
    private string warning="";
    static ClientService(){ ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12; }
    internal ClientService(string dir,bool recover=true,string bundledRoot=null) {
        allowStartup=recover;root=dir;cores=bundledRoot??Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"cores");Directory.CreateDirectory(dir);store=Path.Combine(dir,"subscriptions.dat");system=new WindowsProxy(dir);
        if(recover)try {system.Recover();}catch(Exception){warning="上次系统代理未能自动恢复。请检查 Windows 系统代理设置。";}
        try {model=File.Exists(store)?LocalData.Json.Deserialize<ClientModel>(LocalData.Read(store)):null;}
        catch(Exception e){if(e is IOException || e is CryptographicException || e is ArgumentException || e is InvalidOperationException)throw new InvalidOperationException("不能读取当前用户的本地订阅库。已有数据未覆盖。");throw;}
        if(model==null)model=new ClientModel{Sources=new List<SourceRecord>()};
        if(model.Sources==null)throw new InvalidOperationException("本地订阅库损坏，已有数据未覆盖。");
        foreach(var source in model.Sources) parsed.Add(source.Id,Subscription.Parse(source.Body));
    }
    internal bool CloseToTray { get {return model.CloseToTray;} }
    internal string LocalProxyAddress(){
        if(session==null||!session.Alive)throw new InvalidOperationException("请先启动代理。");
        return "127.0.0.1:"+session.Port;
    }
    private void Save() {LocalData.Write(store,LocalData.Json.Serialize(model));}
    private SourceRecord Source(string id) {var s=model.Sources.Find(x=>x.Id==id);if(s==null)throw new InvalidOperationException("找不到该订阅。");return s;}
    private ParsedSource Active() {ParsedSource s;if(model.Source==null||!parsed.TryGetValue(model.Source,out s))throw new InvalidOperationException("请先选择订阅。");return s;}
    private string Selectable() {var active=Active(); if(!active.Nodes.Any(x=>x.Id==model.Node))throw new InvalidOperationException("请先选择节点。");return model.Node;}
    internal object Run(string action,IDictionary<string,object> args,Action<object> progress=null) {
        if(session!=null && !session.Alive) {Stop();warning="内核意外退出，已停止代理。";}
        switch(action) {
            case "snapshot":return Snapshot();
            case "import":Import(args);break;
            case "select-source": {var s=Source(LocalData.Text(args,"id"));if(s.Id!=model.Source){Stop();model.Source=s.Id;model.Node=null;Save();}break;}
            case "select-node": {var id=LocalData.Text(args,"id");if(!Active().Nodes.Any(x=>x.Id==id))throw new InvalidOperationException("找不到该节点。");if(id!=model.Node){Stop();model.Node=id;Save();}break;}
            case "rename": Source(LocalData.Text(args,"id")).Name=Name(LocalData.Text(args,"name"));Save();break;
            case "remove": {var s=Source(LocalData.Text(args,"id"));if(s.Id==model.Source){Stop();model.Source=null;model.Node=null;}model.Sources.Remove(s);parsed.Remove(s.Id);delays.Remove(s.Id);Save();break;}
            case "refresh": Update(Source(LocalData.Text(args,"id")),null);break;
            case "replace-url": Update(Source(LocalData.Text(args,"id")),LocalData.Text(args,"url"));break;
            case "preferred": {var requested=LocalData.Text(args,"value");if(requested!="Xray"&&requested!="V2Fly")throw new InvalidOperationException("不支持该选项。");model.Preferred=requested;Save();break;}
            case "close-behavior": {
                string value=LocalData.Text(args,"value");
                if(value!="exit"&&value!="tray")throw new InvalidOperationException("不支持此关闭行为。");
                bool previous=model.CloseToTray;model.CloseToTray=value=="tray";
                try{Save();}catch{model.CloseToTray=previous;throw;}
                break;
            }
            case "autostart": Startup(LocalData.Text(args,"value")=="on");break;
            case "start": Start(LocalData.Text(args,"mode"));break;
            case "stop": Stop();break;
            case "delay": return Measure(LocalData.Text(args,"url"),progress);
            case "check": return Check(args);
            case "ip": return Ip();
            default:throw new InvalidOperationException("不支持此操作。");
        }
        return Snapshot();
    }
    private static string Name(string value) {value=value.Trim();if(value.Length==0 || value.Length>70 || value.Any(char.IsControl))throw new InvalidOperationException("请输入 1–70 个字符的订阅名称。");return value;}
    private void Import(IDictionary<string,object> args) {
        if(model.Sources.Count>=20)throw new InvalidOperationException("最多保存 20 个订阅。");
        string input=LocalData.Text(args,"input").Trim(), name=LocalData.Text(args,"name").Trim();
        string url=input.StartsWith("https://",StringComparison.OrdinalIgnoreCase)&&!input.Any(char.IsWhiteSpace)&&LocalData.Text(args,"origin")!="file"?input:null;
        string body=url==null?input:Subscription.Download(url,SessionOrSystemProxy());
        var data=Subscription.Parse(body);
        var source=new SourceRecord{Body=body,Url=url,Name=name.Length==0?"订阅 "+(model.Sources.Count+1):Name(name),Id=Guid.NewGuid().ToString("N")};
        model.Sources.Add(source);parsed.Add(source.Id,data);try {Save();} catch {model.Sources.Remove(source);parsed.Remove(source.Id);throw;}
    }
    private void Update(SourceRecord source,string newUrl) {
        string url=newUrl??source.Url;
        if(string.IsNullOrEmpty(url))throw new InvalidOperationException("请输入新的 HTTPS 订阅链接。");
        string body=Subscription.Download(url,SessionOrSystemProxy());var data=Subscription.Parse(body);
        if(source.Id==model.Source){Stop();model.Node=null;}
        source.Body=body;source.Url=url;parsed[source.Id]=data;delays.Remove(source.Id);Save();
    }
    private string CorePath(string id) {
        var asset=CoreAsset.Pick(id);
        var folder=Path.Combine(cores,id);
        var path=Path.Combine(folder,asset.Exe);
        return File.Exists(path)&&(id!="sing-box"||File.Exists(Path.Combine(folder,"libcronet.dll")))?path:"";
    }
    private string Engine(ProxyNode node) {
        if(node.Engine==null)return "Mihomo";
        if(node.V2Compatible&&((model.Preferred=="V2Fly"&&CorePath("V2Fly").Length>0)||CorePath("Xray").Length==0&&CorePath("V2Fly").Length>0))return "V2Fly";
        return node.Engine;
    }
    private IWebProxy SessionOrSystemProxy(){return session==null?WebRequest.GetSystemWebProxy():session.Proxy;}
    private const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
    private static string LaunchCommand(){return "\""+System.Reflection.Assembly.GetEntryAssembly().Location+"\"";}
    private bool StartupEnabled(){
        if(!allowStartup)return false;
        try{using(var k=Registry.CurrentUser.OpenSubKey(RunKey))return k!=null&&string.Equals(k.GetValue("FlowPivot") as string,LaunchCommand(),StringComparison.OrdinalIgnoreCase);}catch(Exception){return false;}
    }
    private void Startup(bool value){
        if(!allowStartup)throw new InvalidOperationException("测试模式不能修改开机启动。");
        using(var k=Registry.CurrentUser.CreateSubKey(RunKey)){
            if(value)k.SetValue("FlowPivot",LaunchCommand(),RegistryValueKind.String);
            else if(string.Equals(k.GetValue("FlowPivot") as string,LaunchCommand(),StringComparison.OrdinalIgnoreCase))k.DeleteValue("FlowPivot",false);
        }
    }
    private static bool Elevated(){
        using(var user=WindowsIdentity.GetCurrent())return new WindowsPrincipal(user).IsInRole(WindowsBuiltInRole.Administrator);
    }
    private void Start(string mode) {
        if(mode!="system" && mode!="local" && mode!="tun")throw new InvalidOperationException("不支持该代理模式。");
        if(session!=null)throw new InvalidOperationException("代理已启动。");
        var picked=Selectable();
        var data=Active();var node=data.Nodes.Single(n=>n.Id==picked);
        activeEngine=Engine(node);
        if(mode=="tun" && (!Elevated()||activeEngine!="Mihomo"))throw new InvalidOperationException("TUN 需要以管理员身份启动，并选择原生 Mihomo 节点。");
        if(mode=="tun"&&!MihomoSession.IsPinned(CorePath("Mihomo")))throw new InvalidOperationException("TUN 仅允许使用客户端固定校验版 Mihomo。请重新安装或完整解压 FlowPivot。");
        session=new MihomoSession(CorePath("Mihomo"),root,data,picked,activeEngine,CorePath(activeEngine),mode=="tun");activeMode=mode;
        try {
            if(mode=="system"){system.Enable("127.0.0.1:"+session.Port);ownsSystem=true;}
            priorUp=priorDown=rateUp=rateDown=totalUp=totalDown=0;rates.Restart();warning="";
        } catch {Stop();throw;}
    }
    private void Stop() {
        try {if(ownsSystem){system.Recover();ownsSystem=false;}}
        finally {if(session!=null){
            try{if(session.Alive){var stats=session.Get("/connections",1000);totalUp=LocalData.Number(stats,"uploadTotal");totalDown=LocalData.Number(stats,"downloadTotal");}}catch(WebException){}catch(IOException){}
            finally{session.Dispose();session=null;}}rateUp=rateDown=0;}
    }
    private object Snapshot() {
        long up=totalUp,down=totalDown;
        var rows=new List<object>();var direct=0;var routed=0;
        if(session!=null)try {
            var connections=session.Get("/connections");
            totalUp=up=LocalData.Number(connections,"uploadTotal");totalDown=down=LocalData.Number(connections,"downloadTotal");
            var elapsed=rates.Elapsed.TotalSeconds;
            if(elapsed>=0.5){rateUp=(long)(Math.Max(0,up-priorUp)/elapsed);rateDown=(long)(Math.Max(0,down-priorDown)/elapsed);priorUp=up;priorDown=down;rates.Restart();}
            foreach(var connection in LocalData.Rows(connections,"connections")) {
                var meta=LocalData.Obj(connection,"metadata");var host=LocalData.Text(meta,"host");
                if(host.Length==0)host=LocalData.Text(meta,"destinationIP");
                if(LocalData.Text(meta,"destinationPort").Length>0)host+=":"+LocalData.Text(meta,"destinationPort");
                object value;var chain=connection.TryGetValue("chains",out value)?value as object[]:null;
                bool isDirect=chain!=null && chain.Contains("DIRECT");if(isDirect)direct++;else routed++;
                if(rows.Count<200)rows.Add(new{host,route=isDirect?"直连":"规则代理",rule=LocalData.Text(connection,"rule")});
            }
        }catch(WebException){warning="暂时无法读取内核连接统计。";}
        ParsedSource nodes=null; if(model.Source!=null)parsed.TryGetValue(model.Source,out nodes);
        IDictionary<string,object> nodeDelays=null;if(model.Source!=null)delays.TryGetValue(model.Source,out nodeDelays);
        return new { appVersion=AppUpdate.Version, online=session!=null, system=ownsSystem, canTun=Elevated(), mode=activeMode, engine=session==null?null:activeEngine, port=session==null?0:session.Port, warning, autoStart=StartupEnabled(), closeToTray=CloseToTray, preferred=model.Preferred??"Xray",
            cores=CoreAsset.Catalog.Select(c=>new{id=c.Id,version=CorePath(c.Id).Length>0?c.Version:""}).ToArray(), sources=model.Sources.Select(x=>new{ id=x.Id,name=x.Name,format=parsed[x.Id].Format,count=parsed[x.Id].Nodes.Count,skipped=parsed[x.Id].Skipped,refreshable=x.Url!=null }).ToArray(),
            source=model.Source,node=model.Node,nodes=nodes==null?null:nodes.Nodes.Select(x=>new{id=x.Id,name=x.Name,kind=x.Kind,engine=Engine(x),ready=CorePath("Mihomo").Length>0&&CorePath(Engine(x)).Length>0,delay=nodeDelays!=null && nodeDelays.ContainsKey(x.Id)?nodeDelays[x.Id]:null}).ToArray(),
            traffic=new{up,down,rateUp,rateDown}, connections=new{direct,routed,total=direct+routed,rows} };
    }
    private object Measure(string url,Action<object> progress) {
        var target=Subscription.HttpUri(url,false);var data=Active();
        bool native=data.Nodes[0].Native!=null;
        var batch=data.Nodes.Take(native?24:80).ToArray();
        var result=new System.Collections.Concurrent.ConcurrentDictionary<string,object>();
        if(progress!=null)progress(new {ids=batch.Select(n=>n.Id).ToArray()});
        Action<string,object> finish=(id,delay)=>{
            result[id]=delay;
            if(progress!=null)progress(new {id,delay});
        };
        if(native){
            // Each native node needs an isolated upstream process.
            Parallel.ForEach(batch,new ParallelOptions{MaxDegreeOfParallelism=4},n=>{
                string engine=Engine(n);object delay="failed";
                if(CorePath(engine).Length==0||CorePath("Mihomo").Length==0){finish(n.Id,"missing");return;}
                try{using(var probe=new MihomoSession(CorePath("Mihomo"),root,data,n.Id,engine,CorePath(engine))){var ms=probe.Delay(n.Id,target);if(ms>0)delay=ms;}}
                catch(Exception e){if(!(e is WebException||e is InvalidOperationException||e is IOException))throw;}
                finish(n.Id,delay);
            });
        } else {
            var probe=session??new MihomoSession(CorePath("Mihomo"),root,data,batch[0].Id);
            try {
                Parallel.ForEach(batch,new ParallelOptions{MaxDegreeOfParallelism=6},n=>{
                    object delay="failed";
                    try {var ms=probe.Delay(n.Id,target);if(ms>0)delay=ms;}
                    catch(WebException){}
                    finish(n.Id,delay);
                });
            } finally {if(probe!=session)probe.Dispose();}
        }
        delays[model.Source]=result;return Snapshot();
    }
    private object Check(IDictionary<string,object> args) {
        var s=session;if(s==null)throw new InvalidOperationException("请先启动代理。");
        object raw;var list=args.TryGetValue("urls",out raw)?raw as object[]:null;
        if(list==null || list.Length<1 || list.Length>23)throw new InvalidOperationException("检测站点数量有误。");
        var targets=list.Select(x=>Subscription.HttpUri(x as string,false)).ToArray();
        var results=new object[targets.Length];
        Parallel.For(0,targets.Length,new ParallelOptions{MaxDegreeOfParallelism=5},i=>{
            var clock=Stopwatch.StartNew();
            try {
                int status;Subscription.Request(targets[i],s.Proxy,0,out status,false,6500);
                // A 502/503/504 can also be generated by the proxy, not the target.
                results[i]=new{status,ms=clock.ElapsedMilliseconds,outcome=status>=500?"服务器或代理错误":status>=400?"访问受限":status>=200?"已响应":"未知响应"};
            } catch(WebException e){results[i]=new{ status=0,ms=clock.ElapsedMilliseconds,outcome=e.Status==WebExceptionStatus.Timeout?"超时":"请求失败" };}
        });return results;
    }
    private object Ip() {
        if(session==null)throw new InvalidOperationException("请先启动代理。");
        try {
            int code;var response=LocalData.Parse(Subscription.Request(new Uri("https://ipwho.is/"),session.Proxy,60000,out code,false));
            IPAddress address; if(code!=200 || !IPAddress.TryParse(LocalData.Text(response,"ip"),out address))throw new InvalidOperationException("IP 数据源没有返回有效结果。");
            var conn=LocalData.Obj(response,"connection");
            return new{ip=address.ToString(),country=LocalData.Text(response,"country"),city=LocalData.Text(response,"city"),isp=LocalData.Text(conn,"isp"),asn=LocalData.Number(conn,"asn"),org=LocalData.Text(conn,"org"),risk=Reputation(address,session.Proxy)};
        }catch(WebException){throw new InvalidOperationException("无法通过当前代理查询 IP 数据源。");}
    }
    internal static string DecodeRisk(IDictionary<string,object> row){
        var flags=LocalData.Obj(row,"detections");if(flags==null)return "未评估（无风险数据）";
        var positives=new List<string>();int count=0;
        string[] keys={"proxy","vpn","tor","hosting"}, labels={"代理","VPN","Tor","托管网络"};
        for(int i=0;i<keys.Length;i++){object v;if(flags.TryGetValue(keys[i],out v)&&v is bool){count++;if((bool)v)positives.Add(labels[i]);}}
        object raw;int score;string rating=flags.TryGetValue("risk",out raw)&&raw!=null&&int.TryParse(Convert.ToString(raw),out score)&&score>=0&&score<=100?" · 风险 "+score+"/100":"";
        if(count==0)return rating.Length>0?"proxycheck.io"+rating:"未评估（无风险数据）";
        return "proxycheck.io："+(positives.Count>0?string.Join(" / ",positives):count==keys.Length?"未见四类标记":"已返回项目未见标记")+rating;
    }
    private static string Reputation(IPAddress address,IWebProxy proxy){
        try{
            int status;var json=Subscription.Request(new Uri("https://proxycheck.io/v3/"+Uri.EscapeDataString(address.ToString())+"?tag=0"),proxy,60000,out status,false,6500);
            var response=status==200?LocalData.Parse(json):null;
            if(response!=null)foreach(var field in response){IPAddress other;if(IPAddress.TryParse(field.Key,out other)&&address.Equals(other))return DecodeRisk(field.Value as IDictionary<string,object>);}
            return "未评估（风险数据源不可用）";
        }catch(Exception e){if(e is WebException||e is InvalidOperationException||e is ArgumentException||e is IOException)return "未评估（风险数据源不可用）";throw;}
    }
    public void Dispose(){Stop();}
}
