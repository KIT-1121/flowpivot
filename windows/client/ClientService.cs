using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
    public string LogDirectory { get; set; }
    public bool CloseToTray { get; set; }
    public string DelayUrl { get; set; }
    public int LocalProxyPort { get; set; }
    public Dictionary<string,string> TargetRoutes { get; set; } // Read legacy profiles; no longer used to route.
    public List<DomainRoute> DomainRoutes { get; set; }
    public ClientModel(){CloseToTray=true;LocalProxyPort=ClientService.DefaultLocalProxyPort;}

}
internal sealed class ClientService : IDisposable
{
    internal const string DefaultDelayUrl="https://www.gstatic.com/generate_204";
    internal const int DefaultLocalProxyPort=7897;
    private readonly string root, store, cores;
    private readonly WindowsProxy system;
    private readonly ClientModel model;
    private readonly DiagnosticsLog log;
    private readonly Dictionary<string,ParsedSource> parsed=new Dictionary<string,ParsedSource>();
    private readonly Dictionary<string,IDictionary<string,object>> delays=new Dictionary<string,IDictionary<string,object>>();
    private MihomoSession session;
    private bool ownsSystem;
    private readonly bool allowStartup;
    private string activeMode,activeEngine;
    private long priorUp,priorDown,rateUp,rateDown,totalUp,totalDown,baseUp,baseDown;
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
        if(model.LocalProxyPort<1||model.LocalProxyPort>65535)model.LocalProxyPort=DefaultLocalProxyPort;
        model.DomainRoutes=TargetRouting.LoadDomainRoutes(model.DomainRoutes,model.TargetRoutes);
        model.TargetRoutes=null;
        log=new DiagnosticsLog(dir,model.LogDirectory);
        foreach(var source in model.Sources) parsed.Add(source.Id,Subscription.WithOverride(source.Body,source.Override));
        ParsedSource selected;
        if(model.Source!=null&&parsed.TryGetValue(model.Source,out selected)&&!selected.Nodes.Any(x=>x.Id==model.Node))
            model.Node=DefaultNode(selected);
        log.Write("应用启动 · "+AppUpdate.Version);
        if(warning.Length>0)log.Write("未能自动恢复系统代理",new InvalidOperationException());
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
    internal string LogDirectory {get{return log.DirectoryPath;}}
    internal object ChangeLogDirectory(string chosen){
        string previous=model.LogDirectory;
        model.LogDirectory=log.CheckDestination(chosen);
        try{Save();}catch{model.LogDirectory=previous;throw;}
        log.CustomDirectory=model.LogDirectory;
        log.Write(chosen==null?"已恢复默认日志目录":"已更换日志目录");
        return log.Read();
    }
    internal void RecordFileImportError(Exception error){log.Write("读取订阅文件失败",error);}
    internal object Run(string action,IDictionary<string,object> args,Action<object> progress=null) {
        string label;
        LogActions.TryGetValue(action,out label);
        try{
            var result=RunAction(action,args,progress);
            if(label!=null)log.Write(label+"完成"+(action=="start"?" · "+activeEngine+" / "+activeMode:""));
            return result;
        }catch(Exception e){if(label!=null)log.Write(label+"失败",e);throw;}
    }
    private static readonly Dictionary<string,string> LogActions=new Dictionary<string,string>{
        {"import","导入订阅"},{"refresh","更新订阅"},{"replace-url","替换订阅链接"},{"select-source","切换订阅"},
        {"select-node","选择节点"},{"remove","移除订阅"},{"start","启动代理"},{"stop","停止代理"},
        {"delay","节点测速"},{"check","连通性检测"},{"ip","IP 检测"},{"domain-route","修改分流规则"},{"local-proxy-port","设置本机代理端口"}
    };
    private object RunAction(string action,IDictionary<string,object> args,Action<object> progress) {
        if(action=="logs-read")return log.Read();
        if(session!=null && !session.Alive) {
            log.Write("内核意外退出",new InvalidOperationException());Stop();warning="内核意外退出，已停止代理。";
        }
        switch(action) {
            case "snapshot":return Snapshot();
            case "import":Import(args);break;
            case "select-source": {var s=Source(LocalData.Text(args,"id"));if(s.Id!=model.Source){Stop();model.Source=s.Id;model.Node=DefaultNode(parsed[s.Id]);Save();}break;}
            case "select-node": SelectNode(LocalData.Text(args,"id"));break;
            case "rename": Source(LocalData.Text(args,"id")).Name=Name(LocalData.Text(args,"name"));Save();break;
            case "remove": {var s=Source(LocalData.Text(args,"id"));if(s.Id==model.Source){Stop();model.Source=null;model.Node=null;}model.Sources.Remove(s);parsed.Remove(s.Id);delays.Remove(s.Id);Save();break;}
            case "refresh": Update(Source(LocalData.Text(args,"id")),null);break;
            case "replace-url": Update(Source(LocalData.Text(args,"id")),LocalData.Text(args,"url"));break;
            case "source-yaml": return Subscription.ExportYaml(parsed[Source(LocalData.Text(args,"id")).Id]);
            case "source-yaml-save": SaveYamlCopy(Source(LocalData.Text(args,"id")),LocalData.Text(args,"body"));break;
            case "source-override": return new {text=Source(LocalData.Text(args,"id")).Override??""};
            case "source-override-save": SaveOverride(Source(LocalData.Text(args,"id")),LocalData.Text(args,"body"));break;
            case "delay-url": {
                string previous=model.DelayUrl,text=LocalData.Text(args,"value").Trim();
                model.DelayUrl=text.Length==0?null:Subscription.HttpUri(text,false).AbsoluteUri;
                try{Save();}catch{model.DelayUrl=previous;throw;}
                break;
            }
            case "local-proxy-port": {
                if(session!=null)throw new InvalidOperationException("请先停止代理，再修改本机代理端口。");
                int port;
                if(!int.TryParse(LocalData.Text(args,"value").Trim(),NumberStyles.None,CultureInfo.InvariantCulture,out port)||port<1||port>65535)
                    throw new InvalidOperationException("请输入 1–65535 之间的整数端口。");
                int previous=model.LocalProxyPort;
                model.LocalProxyPort=port;
                try{Save();}catch{model.LocalProxyPort=previous;throw;}
                break;
            }
            case "connection-close":
                if(session==null)throw new InvalidOperationException("代理已关闭。");
                try{session.CloseConnection(LocalData.Text(args,"id"));}catch(WebException){throw new InvalidOperationException("暂时无法读取当前连接。");}
                break;
            case "domain-route": ChangeDomainRoute(args);break;
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
            case "delay": return Measure(model.DelayUrl??DefaultDelayUrl,progress);
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
        var fetched=url==null?new SubscriptionResult{Body=input,Data=Subscription.Parse(input)}:Subscription.Download(url,SessionOrSystemProxy(),null);
        var data=fetched.Data;
        var source=new SourceRecord{Body=fetched.Body,Url=url,Quota=fetched.Quota,Name=name.Length==0?"订阅 "+(model.Sources.Count+1):Name(name),Id=Guid.NewGuid().ToString("N")};
        var previousSource=model.Source;var previousNode=model.Node;
        if(model.Sources.Count==0){model.Source=source.Id;model.Node=DefaultNode(data);}
        model.Sources.Add(source);parsed.Add(source.Id,data);
        try {Save();} catch {model.Sources.Remove(source);parsed.Remove(source.Id);model.Source=previousSource;model.Node=previousNode;throw;}
    }
    private void Update(SourceRecord source,string newUrl) {
        string url=newUrl??source.Url;
        if(string.IsNullOrEmpty(url))throw new InvalidOperationException("请输入新的 HTTPS 订阅链接。");
        var fetched=Subscription.Download(url,SessionOrSystemProxy(),source.Override);
        string body=fetched.Body;var data=fetched.Data;
        bool nodesChanged=source.Body!=body;
        string previousBody=source.Body,previousUrl=source.Url,previousNode=model.Node;var previousQuota=source.Quota;
        if(nodesChanged&&source.Id==model.Source){
            Stop();
            var previous=parsed[source.Id].Nodes.FirstOrDefault(n=>n.Id==model.Node);
            model.Node=previous==null?DefaultNode(data):data.Nodes.Where(n=>n.Name==previous.Name&&n.Kind==previous.Kind).Select(n=>n.Id).FirstOrDefault()??DefaultNode(data);
        }
        source.Body=body;source.Url=url;source.Quota=fetched.Quota;
        try{Save();}catch{source.Body=previousBody;source.Url=previousUrl;source.Quota=previousQuota;model.Node=previousNode;throw;}
        if(nodesChanged){parsed[source.Id]=data;delays.Remove(source.Id);}
    }
    private void SaveYamlCopy(SourceRecord source,string body){
        if(model.Sources.Count>=20)throw new InvalidOperationException("最多保存 20 个订阅。");
        if(parsed[source.Id].Nodes[0].Data==null)throw new InvalidOperationException("原生 JSON 订阅不支持 YAML 编辑。");
        var data=Subscription.ParseEditedYaml(body);
        const string suffix="（本地副本）";
        int end=Math.Min(source.Name.Length,70-suffix.Length);
        if(end>0&&char.IsHighSurrogate(source.Name[end-1]))end--;
        var copy=new SourceRecord{Id=Guid.NewGuid().ToString("N"),Name=source.Name.Substring(0,end)+suffix,Body=body};
        model.Sources.Add(copy);parsed.Add(copy.Id,data);
        try{Save();}catch{model.Sources.Remove(copy);parsed.Remove(copy.Id);throw;}
    }
    private void SaveOverride(SourceRecord source,string body){
        if(source.Id==model.Source&&session!=null)throw new InvalidOperationException("请先关闭当前代理，再修改正在使用的订阅。");
        var data=Subscription.WithOverride(source.Body,body);
        var previous=source.Override;var node=model.Node;
        source.Override=string.IsNullOrWhiteSpace(body)?null:body;
        if(source.Id==model.Source){
            if(!data.Nodes.Any(n=>n.Id==model.Node))model.Node=DefaultNode(data);
        }
        try{Save();}catch{source.Override=previous;model.Node=node;throw;}
        parsed[source.Id]=data;delays.Remove(source.Id);
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
    private string DefaultNode(ParsedSource source) {
        return (source.Nodes.FirstOrDefault(x=>CorePath("Mihomo").Length>0&&CorePath(Engine(x)).Length>0)??source.Nodes[0]).Id;
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
        var picked=Selectable();var data=Active();
        var engine=ValidateEngine(data.Nodes.Single(n=>n.Id==picked),mode);
        OpenSession(data,picked,engine,mode);
        try {
            if(mode=="system"){system.Enable("127.0.0.1:"+session.Port);ownsSystem=true;}
            baseUp=baseDown=priorUp=priorDown=rateUp=rateDown=totalUp=totalDown=0;rates.Restart();warning="";
        } catch {Stop();throw;}
    }
    private string ValidateEngine(ProxyNode node,string mode){
        var engine=Engine(node);
        if(CorePath("Mihomo").Length==0||CorePath(engine).Length==0)throw new InvalidOperationException("内置核心缺失，请重新安装或完整解压流枢。");
        if(mode=="tun"&&(!Elevated()||engine!="Mihomo"))throw new InvalidOperationException("TUN 需要以管理员身份启动，并选择原生 Mihomo 节点。");
        if(mode=="tun"&&!MihomoSession.IsPinned(CorePath("Mihomo")))throw new InvalidOperationException("TUN 仅允许使用客户端固定校验版 Mihomo。请重新安装或完整解压 FlowPivot。");
        return engine;
    }
    private void OpenSession(ParsedSource data,string id,string engine,string mode){
        session=new MihomoSession(CorePath("Mihomo"),root,data,id,engine,CorePath(engine),mode=="tun",model.LocalProxyPort,model.DomainRoutes);
        activeMode=mode;activeEngine=engine;
    }
    private void ChangeDomainRoute(IDictionary<string,object> args){
        string id=LocalData.Text(args,"id"), operation=LocalData.Text(args,"operation");
        var next=new List<DomainRoute>(model.DomainRoutes);
        int index=id.Length==0?-1:next.FindIndex(r=>r.Id==id);
        if(id.Length!=0&&index<0)throw new InvalidOperationException("找不到此分流规则。");
        if(operation=="delete"&&index>=0)next.RemoveAt(index);
        else if(operation=="save"){
            string kind=LocalData.Text(args,"kind"), pattern=LocalData.Text(args,"pattern").Trim(), value=LocalData.Text(args,"value");
            if(!TargetRouting.ValidRoute(value))throw new InvalidOperationException("不支持此走向。");
            if(!TargetRouting.NormalizeRule(ref kind,ref pattern))throw new InvalidOperationException("无效或不支持的匹配内容。请输入域名、基础正则或不带端口的 IP。");
            if(next.Any(r=>r.Id!=id&&r.Kind==kind&&r.Pattern==pattern))throw new InvalidOperationException("已有相同的分流规则，请编辑原规则。");
            if(kind=="regex"&&next.Count(r=>r.Id!=id&&r.Kind=="regex")>=TargetRouting.RegexLimit)
                throw new InvalidOperationException("最多保存 32 条正则规则。");
            var rule=new DomainRoute{Id=index<0?Guid.NewGuid().ToString("N"):id,Kind=kind,Pattern=pattern,Value=value};
            if(index<0){
                if(next.Count>=TargetRouting.Limit)throw new InvalidOperationException("最多保存 160 条分流规则。");
                next.Add(rule);
            }else next[index]=rule;
        }else throw new InvalidOperationException("不支持此操作。");
        SaveRoutes(next);
    }
    private void SaveRoutes(List<DomainRoute> domains){
        var previousDomains=model.DomainRoutes;
        try{
            if(session!=null)session.ApplyRoutes(domains);
            model.DomainRoutes=domains;Save();
        }catch(Exception){
            model.DomainRoutes=previousDomains;
            if(session!=null){
                try{session.ApplyRoutes(previousDomains);}
                catch(Exception){
                    Stop();
                    warning="修改走向失败，代理已停止。";
                    throw new InvalidOperationException(warning);
                }
            }
            throw new InvalidOperationException("未能保存走向，原规则未更改。");
        }
    }
    private void SelectNode(string id){
        var data=Active();var next=data.Nodes.Find(n=>n.Id==id);
        if(next==null)throw new InvalidOperationException("找不到该节点。");
        var previous=model.Node;if(id==previous)return;
        if(session==null){model.Node=id;try{Save();}catch{model.Node=previous;throw;}return;}
        var mode=activeMode;var previousEngine=activeEngine;
        var engine=ValidateEngine(next,mode);
        bool live=session.CanSelect&&next.Native==null;
        try{
            if(live)session.Select(id);
            else{CloseSession();OpenSession(data,id,engine,mode);}
            model.Node=id;Save();warning="";
        }catch(Exception){
            model.Node=previous;
            try{
                if(live&&session!=null&&session.Alive)session.Select(previous);
                else{CloseSession();OpenSession(data,previous,previousEngine,mode);}
            }catch(Exception){
                Stop();warning="节点切换失败，未能恢复原连接。";
                throw new InvalidOperationException(warning);
            }
            throw new InvalidOperationException("节点切换失败，已保留原连接。");
        }
    }
    private void CloseSession(){
        if(session!=null){
            try{if(session.Alive){var stats=session.Get("/connections",1000);totalUp=baseUp+LocalData.Number(stats,"uploadTotal");totalDown=baseDown+LocalData.Number(stats,"downloadTotal");}}catch(WebException){}catch(IOException){}
            finally{var old=session;session=null;old.Dispose();}
        }
        priorUp=baseUp=totalUp;priorDown=baseDown=totalDown;rateUp=rateDown=0;rates.Restart();
    }
    private void Stop() {
        try {if(ownsSystem){system.Recover();ownsSystem=false;}}
        finally {CloseSession();}
    }
    private object Snapshot() {
        long up=totalUp,down=totalDown;
        var rows=new List<object>();var direct=0;var routed=0;
        if(session!=null)try {
            var connections=session.Get("/connections");
            totalUp=up=baseUp+LocalData.Number(connections,"uploadTotal");totalDown=down=baseDown+LocalData.Number(connections,"downloadTotal");
            var elapsed=rates.Elapsed.TotalSeconds;
            if(elapsed>=0.5){rateUp=(long)(Math.Max(0,up-priorUp)/elapsed);rateDown=(long)(Math.Max(0,down-priorDown)/elapsed);priorUp=up;priorDown=down;rates.Restart();}
            foreach(var connection in LocalData.Rows(connections,"connections")) {
                var meta=LocalData.Obj(connection,"metadata");var host=LocalData.Text(meta,"host");
                if(host.Length==0)host=LocalData.Text(meta,"destinationIP");
                string destinationPort=LocalData.Text(meta,"destinationPort");
                if(destinationPort.Length>0)host=(host.Contains(":")?"["+host+"]":host)+":"+destinationPort;
                object value;var chain=connection.TryGetValue("chains",out value)?value as object[]:null;
                bool isDirect=chain!=null && chain.Contains("DIRECT");if(isDirect)direct++;else routed++;
                Guid connectionId;
                var id=LocalData.Text(connection,"id");
                if(rows.Count<200&&Guid.TryParseExact(id,"D",out connectionId))
                {
                    rows.Add(new{id,host,route=isDirect?"直连":"规则代理",rule=LocalData.Text(connection,"rule"),network=LocalData.Text(meta,"network"),up=LocalData.Number(connection,"upload"),down=LocalData.Number(connection,"download")});
                }
            }
        }catch(WebException){warning="暂时无法读取内核连接统计。";}
        ParsedSource nodes=null; if(model.Source!=null)parsed.TryGetValue(model.Source,out nodes);
        IDictionary<string,object> nodeDelays=null;if(model.Source!=null)delays.TryGetValue(model.Source,out nodeDelays);
        return new { appVersion=AppUpdate.Version, online=session!=null, system=ownsSystem, canTun=Elevated(), mode=activeMode, engine=session==null?null:activeEngine, port=session==null?0:session.Port, localProxyPort=model.LocalProxyPort, warning, autoStart=StartupEnabled(), closeToTray=CloseToTray, delayUrl=model.DelayUrl??DefaultDelayUrl, preferred=model.Preferred??"Xray",
            cores=CoreAsset.Catalog.Select(c=>new{id=c.Id,version=CorePath(c.Id).Length>0?c.Version:""}).ToArray(), sources=model.Sources.Select(x=>new{ id=x.Id,name=x.Name,format=parsed[x.Id].Format,count=parsed[x.Id].Nodes.Count,skipped=parsed[x.Id].Skipped,refreshable=x.Url!=null,yaml=parsed[x.Id].Nodes[0].Data!=null,quota=x.Quota==null?null:new{total=x.Quota.Total,used=x.Quota.Used,remaining=x.Quota.Remaining} }).ToArray(),
            source=model.Source,node=model.Node,nodes=nodes==null?null:nodes.Nodes.Select(x=>new{id=x.Id,name=x.Name,kind=x.Kind,engine=Engine(x),ready=CorePath("Mihomo").Length>0&&CorePath(Engine(x)).Length>0,delay=nodeDelays!=null && nodeDelays.ContainsKey(x.Id)?nodeDelays[x.Id]:null}).ToArray(),
            traffic=new{up,down,rateUp,rateDown}, connections=new{direct,routed,total=direct+routed,rows},
            domainRoutes=model.DomainRoutes.Select(r=>new{id=r.Id,kind=r.Kind,pattern=r.Pattern,value=r.Value}).ToArray() };
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
    public void Dispose(){try{Stop();log.Write("应用退出");}catch(Exception e){log.Write("退出时停止代理失败",e);throw;}}
}
