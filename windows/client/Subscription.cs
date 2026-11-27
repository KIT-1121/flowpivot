using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

internal sealed class SourceRecord
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Url { get; set; }
    public string Body { get; set; }
    public string Override { get; set; }
    public string LastNode { get; set; }
    public SubscriptionQuota Quota { get; set; }
    public int RefreshHours { get; set; }
    public long NextRefreshUtcTicks { get; set; }
    internal int RefreshRevision;
    internal int NodeRevision;
    internal bool AutoRefreshFailed;
}
internal sealed class SubscriptionQuota {
    public long? Total { get; set; }
    public long? Used { get; set; }
    public long? Remaining { get; set; }
    internal static SubscriptionQuota Parse(string header) {
        if(string.IsNullOrWhiteSpace(header)||header.Length>4096)return null;
        var fields=new Dictionary<string,long>(StringComparer.OrdinalIgnoreCase);
        foreach(var part in header.Split(';')){
            var pair=part.Split(new[]{'='},2);
            if(pair.Length!=2)continue;
            string key=pair[0].Trim().ToLowerInvariant();
            if(key!="upload"&&key!="download"&&key!="total")continue;
            long number;
            if(fields.ContainsKey(key)||!long.TryParse(pair[1].Trim(),NumberStyles.None,CultureInfo.InvariantCulture,out number))return null;
            fields.Add(key,number);
        }
        long up,down,total;
        long? used=fields.TryGetValue("upload",out up)&&fields.TryGetValue("download",out down)&&up<=long.MaxValue-down?(long?)(up+down):null;
        long? limit=fields.TryGetValue("total",out total)&&total>0?(long?)total:null;
        return used.HasValue||limit.HasValue?new SubscriptionQuota{Used=used,Total=limit,Remaining=limit.HasValue&&used.HasValue?(long?)Math.Max(0,limit.Value-used.Value):null}:null;
    }
}
internal sealed class ProxyNode
{
    internal string Id, Name, Kind;
    internal YamlMappingNode Data;
    internal IDictionary<string,object> Native;
    internal string Engine;
    internal bool V2Compatible;
}
internal sealed class ParsedSource
{
    internal string Format;
    internal int Skipped;
    internal readonly List<ProxyNode> Nodes = new List<ProxyNode>();
}
internal sealed class SubscriptionResult {
    internal string Body;
    internal ParsedSource Data;
    internal SubscriptionQuota Quota;
}
internal static class Subscription
{
    internal const int MaxSize = 1600000;
    private static readonly HashSet<string> types = new HashSet<string>("ss ssr vmess vless trojan hysteria hysteria2 tuic socks5 http anytls wireguard snell mieru".Split(' '));
    private static readonly HashSet<string> forbidden = new HashSet<string>("dialer-proxy certificate private-key ech-key pkcs12 ssh-key ssh-private-key private-key-file certificate-file certificate-path client-fingerprint-file ca-file ca-path fingerprint-file path-to-key key-file private-key-path client-certificate-path ssh-key-file ssh-key-path fingerprint-path".Split(' '),StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> nodeKeys = new HashSet<string>("name type server port username password tls sni servername alpn fingerprint client-fingerprint skip-cert-verify name-cert-verify reality-opts ech-opts udp udp-over-tcp udp-over-tcp-version cipher alterId uuid flow packet-encoding encryption network ws-opts grpc-opts http-opts h2-opts xhttp-opts smux plugin plugin-opts plugin-args ip-version obfs obfs-password obfs-param protocol protocol-param auth auth-str up down ports hop-interval congestion-controller udp-relay-mode reduce-rtt heartbeat request-timeout disable-sni version persistent-keepalive public-key pre-shared-key ip ipv6 mtu reserved allowed-ips psk idle-session-check-interval idle-session-timeout min-idle-session headers fast-open tfo".Split(' '));
    internal static Uri HttpUri(string text, bool https) {
        Uri url;
        if (string.IsNullOrWhiteSpace(text) || text.Length > 4096 || !Uri.TryCreate(text,UriKind.Absolute,out url) ||
            (https ? url.Scheme != "https" : url.Scheme != "https" && url.Scheme != "http") || url.Host.Length == 0 || url.UserInfo.Length != 0)
            throw new InvalidOperationException(https ? "订阅链接必须是 HTTPS，且不能包含账号密码。" : "请输入有效的 HTTP(S) 地址。");
        return url;
    }
    internal static string Request(Uri url, IWebProxy proxy, int cap, out int status, bool redirect, int timeout = 9000) {
        return Request(url,proxy,cap,out status,redirect,timeout,null);
    }
    private static string Request(Uri url, IWebProxy proxy, int cap, out int status, bool redirect, int timeout, string accept, string userAgent = "FlowPivot/0.2", Action<string> userInfo=null) {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        for (int i=0;i<5;i++) {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Proxy = proxy;
            request.UserAgent = userAgent;
            if(accept!=null)request.Accept=accept;
            request.AllowAutoRedirect = false;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            request.Timeout = Math.Max(1, timeout-(int)timer.ElapsedMilliseconds);
            request.ReadWriteTimeout = request.Timeout;
using (var deadline = new System.Threading.Timer(_ => request.Abort(),null,request.Timeout,System.Threading.Timeout.Infinite)) {
                HttpWebResponse response;
                try { response = (HttpWebResponse)request.GetResponse(); }
                catch(WebException e) { response = e.Response as HttpWebResponse; if (response == null) throw; }
using (response) {
                    status = (int)response.StatusCode;
                    if(redirect && status >= 300 && status < 400 && response.Headers["Location"] != null) {
                        try {url=HttpUri(new Uri(url,response.Headers["Location"]).AbsoluteUri,true);}
                        catch(FormatException){throw new InvalidOperationException("下载失败：服务商返回的跳转地址无效。");}
                        catch(InvalidOperationException){throw new InvalidOperationException("下载失败：服务商跳转到了不安全或带用户信息的地址；只允许 HTTPS。");}
                        continue;
                    }
                    if(cap == 0 || status < 200 || status >= 300) return "";
                    if(userInfo!=null&&status>=200&&status<300){
                        string extra=response.Headers["subscription-userinfo"];
                        if(extra==null){
                            var key=response.Headers.AllKeys.FirstOrDefault(k=>k!=null&&k.EndsWith("-subscription-userinfo",StringComparison.OrdinalIgnoreCase));
                            if(key!=null)extra=response.Headers[key];
                        }
                        userInfo(extra);
                    }
using (var body=response.GetResponseStream()) using(var data=new MemoryStream()) {
                        var chunk=new byte[8192]; int n;
                        while ((n=body.Read(chunk,0,chunk.Length))>0) {
                            if (data.Length + n > cap) throw new InvalidOperationException("下载失败：响应超过大小限制。请索取精简的节点订阅。");
                            data.Write(chunk,0,n);
                        }
                        return new UTF8Encoding(false,true).GetString(data.ToArray()).TrimStart('\ufeff');
                    }
                }
            }
        }
        throw new InvalidOperationException("下载失败：订阅重定向次数过多。");
    }
    internal delegate string SubscriptionRequest(string userAgent,string accept,int timeout,out int status);
    internal static SubscriptionResult Download(string text,IWebProxy proxy,string patch) {
        var url=HttpUri(text,true);
        SubscriptionQuota parsed=null;
        try {
            var result=DownloadParsed((string agent,string accept,int timeout,out int status)=>{
                parsed=null;
                return Request(url,proxy,MaxSize,out status,true,timeout,accept,agent,header=>parsed=SubscriptionQuota.Parse(header));
            },patch);
            result.Quota=parsed;
            return result;
        } catch (WebException e) { throw new InvalidOperationException(DownloadError(e.Status)); }
          catch (IOException) { throw new InvalidOperationException("网络：读取订阅响应时连接中断，请检查网络或代理后重试。"); }
          catch (DecoderFallbackException) { throw new InvalidOperationException("格式：订阅响应不是有效的 UTF-8 文本，请向服务商索取 YAML 或节点订阅。"); }
    }
    internal static string DownloadError(WebExceptionStatus status) {
        switch(status){
            case WebExceptionStatus.NameResolutionFailure: return "网络：无法解析订阅服务器域名，请检查 DNS 或代理。";
            case WebExceptionStatus.ProxyNameResolutionFailure: return "网络：无法解析当前代理的地址，请检查代理设置。";
            case WebExceptionStatus.TrustFailure:
            case WebExceptionStatus.SecureChannelFailure: return "证书 / TLS：与订阅服务器建立安全连接失败，请检查服务商证书和系统时间。";
            case WebExceptionStatus.Timeout:
            case WebExceptionStatus.RequestCanceled: return "网络：下载订阅超时，请检查网络或当前代理后重试。";
            case WebExceptionStatus.ConnectFailure: return "网络：无法连接订阅服务器或当前代理。";
            default:return "网络：未能完整下载订阅，请检查网络或当前代理后重试。";
        }
    }
    internal static SubscriptionResult DownloadParsed(SubscriptionRequest request,string patch=null) {
        const int budget=15000;
        var timer=System.Diagnostics.Stopwatch.StartNew();
        var agents=new[]{"FlowPivot/0.2","clash-verge/v2.5.9","v2rayN"};
        string reason=null;
        for(int i=0;i<agents.Length;i++){
            int remaining=budget-(int)timer.ElapsedMilliseconds,code;
            if(remaining<=0)break;
            string body;
            try{
                body=request(agents[i],i==0?"application/yaml, text/yaml, application/x-yaml, application/json, text/plain, application/octet-stream;q=0.8, */*;q=0.5":"*/*",remaining,out code);
            }catch(WebException){if(reason==null)throw;break;}
            catch(DecoderFallbackException){if(reason==null)reason="格式：服务器响应不是有效的 UTF-8 文本。";continue;}
            if(code>=200&&code<300){
                ParsedSource data;
                try{data=Parse(body);}catch(InvalidOperationException e){if(reason==null||reason.StartsWith("服务端：HTTP 406",StringComparison.Ordinal))reason=e.Message;continue;}
                return new SubscriptionResult{Body=body,Data=ApplyOverride(data,patch)};
            }
            if(code==406){if(reason==null)reason="服务端：HTTP 406，未提供订阅配置。请尝试服务商的 Clash/Mihomo 专用链接或导入配置文件。";continue;}
            if(reason!=null)break;
            throw new InvalidOperationException(HttpFailure(code));
        }
        throw new InvalidOperationException(reason??"网络：下载订阅超时。");
    }
    private static string HttpFailure(int code) {
        string hint=code==401||code==403?"访问被拒绝，请在服务商后台检查权限或更新订阅链接。":
          code==404||code==410?"地址不存在或已失效，请复制新的订阅链接。":
          code==429?"请求过于频繁，请稍后再更新。":code>=500?"订阅服务异常，请稍后重试。":"未返回配置，请检查订阅地址。";
        return "服务端：HTTP "+code+"，"+hint;
    }
    internal static ParsedSource Parse(string body) {
        if(string.IsNullOrWhiteSpace(body))throw new InvalidOperationException("内容：响应或文件为空，没有可导入的配置。");
        if(body.Length>MaxSize)throw new InvalidOperationException("内容：配置超过大小限制。");
        return ParsePayload(body.Trim().TrimStart('\ufeff').Trim(),false);
    }
    private static ParsedSource ParsePayload(string compact,bool decoded) {
        if(compact.StartsWith("<",StringComparison.Ordinal))throw new InvalidOperationException("内容：收到网页或 XML，而不是订阅配置。请检查是否复制了登录页、分享页或遇到服务商验证。");
        if(compact.StartsWith("{",StringComparison.Ordinal)) {
            IDictionary<string,object> map;
            try{map=LocalData.Parse(compact);}
            catch(ArgumentException){throw new InvalidOperationException("格式：JSON 语法有误。");}
            catch(InvalidOperationException){throw new InvalidOperationException("格式：JSON 超过解析限制。");}
            if(map!=null&&!map.ContainsKey("outbounds"))throw new InvalidOperationException(map.ContainsKey("error")||map.ContainsKey("message")||map.ContainsKey("code")?"内容：收到 JSON 接口消息，不是节点配置。请在服务商后台检查订阅状态。":"格式：此 JSON 不含受支持的 outbounds。请导入 Clash/Mihomo YAML 或节点列表。");
            return NativeConfig.Parse(compact);
        }
        if(LooksLikeLinks(compact)) return ParseLinks(compact,"节点链接");
        if(!decoded) {
            try {
                string token=RemoveBase64Whitespace(compact);
                if(token.Length>=8&&token.All(c=>c>='A'&&c<='Z'||c>='a'&&c<='z'||c>='0'&&c<='9'||"+/=-_".IndexOf(c)>=0)){
                    string plain=Decode64(token).Trim().TrimStart('\ufeff').Trim();
                    if(LooksLikeLinks(plain))return ParseLinks(plain,"Base64 节点列表");
                    if(plain.StartsWith("<")||plain.StartsWith("{")||plain.StartsWith("proxies:")||plain.StartsWith("---")||plain.StartsWith("#"))
                        return ParsePayload(plain,true);
                }
            } catch (FormatException) {} catch (DecoderFallbackException) {}
        }
        if(compact.Length==0)throw new InvalidOperationException("内容：没有可导入的配置。");
        if(compact.IndexOf("://",StringComparison.Ordinal)>=0&&compact.Split('\n').Any(x=>LooksLikeLinks(x.Trim())))return ParseLinks(compact,"节点链接");
        return ParseYaml(compact);
    }
    private static string RemoveBase64Whitespace(string text){return !text.Any(char.IsWhiteSpace)?text:new string(text.Where(c=>!char.IsWhiteSpace(c)).ToArray());}
    private static string Decode64(string text) {
        string s=text.Replace('-','+').Replace('_','/');
        return new UTF8Encoding(false,true).GetString(Convert.FromBase64String(s.PadRight((s.Length+3)/4*4,'=')));
    }
    private static bool LooksLikeLinks(string text) {
        int mark=text.IndexOf("://",StringComparison.Ordinal);
        return mark>0 && mark<24 && text.Take(mark).All(c=>char.IsLetterOrDigit(c)||c=='-'||c=='+');
    }
    private static ParsedSource ParseLinks(string text, string format) {
        var result=new ParsedSource {Format=format};
        foreach(string raw in text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries)) {
            string line=raw.Trim(); if(line.Length==0) continue;
            if(result.Nodes.Count>=200){result.Skipped++;continue;}
            YamlMappingNode node=null;
            try {node=Link(line);} catch (Exception e) {if(!(e is FormatException || e is ArgumentException || e is InvalidOperationException))throw;}
            if(node==null || !Add(result,node))result.Skipped++;
        }
        if(result.Nodes.Count==0) {
            var schemes=text.Split('\n').Select(x=>x.Trim()).Where(LooksLikeLinks).Select(x=>x.Substring(0,x.IndexOf("://",StringComparison.Ordinal)).ToLowerInvariant()).ToArray();
            var known=new[]{"http","https","socks5","ss","vless","trojan","vmess","hy2","hysteria2","anytls","tuic","ssr","wireguard","v2rayn"};
            var kinds=known.Where(k=>schemes.Contains(k)).ToList();
            if(schemes.Any(x=>!known.Contains(x)))kinds.Add("其他");
            throw new InvalidOperationException("兼容性：识别到节点链接"+(kinds.Count>0?"（"+string.Join("、",kinds)+"）":"")+"，但参数、协议或传输方式无效或暂不支持。请尝试服务商的 Clash/Mihomo YAML。");
        }
        return result;
    }
    private static YamlMappingNode Link(string line) {
        if(line.StartsWith("vmess://",StringComparison.OrdinalIgnoreCase)) {
            var j=LocalData.Parse(Decode64(line.Substring(8))); var transport=LocalData.Text(j,"net");
            if(j==null || !new[]{"","tcp","ws","grpc"}.Contains(transport) || !new[]{"","none"}.Contains(LocalData.Text(j,"type")))return null;
            var map=new YamlMappingNode();
            LinkField(map,"name",LocalData.Text(j,"ps"));LinkField(map,"type","vmess");LinkField(map,"server",LocalData.Text(j,"add"));
            object port, aid; if(!j.TryGetValue("port",out port)) return null;
            LinkField(map,"port",Convert.ToString(port,CultureInfo.InvariantCulture));
            Guid uuid; if(!Guid.TryParse(LocalData.Text(j,"id"),out uuid))return null;
            LinkField(map,"uuid",uuid.ToString());LinkField(map,"alterId",j.TryGetValue("aid",out aid)?Convert.ToString(aid,CultureInfo.InvariantCulture):"0");
            LinkField(map,"cipher",LocalData.Text(j,"scy").Length==0?"auto":LocalData.Text(j,"scy"));
            if(LocalData.Text(j,"tls")=="tls")LinkField(map,"tls","true");
            LinkField(map,"servername",LocalData.Text(j,"sni"));
            return Transport(map,transport,LocalData.Text(j,"path"),LocalData.Text(j,"host"),LocalData.Text(j,"path"));
        }
        if(line.StartsWith("ss://",StringComparison.OrdinalIgnoreCase)&&line.Split('#')[0].IndexOf('@')<0)return LegacyShadowsocks(line);
        Uri url;
        if(!Uri.TryCreate(line,UriKind.Absolute,out url) || url.Host.Length==0 || url.Port<1 || (url.AbsolutePath!="/" && url.AbsolutePath!=""))return null;
        var type=url.Scheme=="hy2"?"hysteria2":url.Scheme;
        if(!new[]{"http","https","socks5","ss","vless","trojan","hysteria2","anytls"}.Contains(type))return null;
        var node=new YamlMappingNode();
        LinkField(node,"name",url.Fragment.Length>1?Uri.UnescapeDataString(url.Fragment.Substring(1)):url.Host);
        LinkField(node,"type",type=="https"?"http":type);LinkField(node,"server",url.IdnHost.Trim('[',']'));LinkField(node,"port",url.Port.ToString(CultureInfo.InvariantCulture));
        var args=System.Web.HttpUtility.ParseQueryString(url.Query); string auth=Uri.UnescapeDataString(url.UserInfo);
        if(type=="ss") {
            if(url.Query.Length>0 || auth.Length==0)return null;
            if(!auth.Contains(":"))auth=Decode64(auth);
            var pair=auth.Split(new[]{':'},2);if(pair.Length!=2||pair.Any(string.IsNullOrEmpty))return null;
            LinkField(node,"cipher",pair[0]);LinkField(node,"password",pair[1]);
        } else if(type=="http" || type=="https" || type=="socks5") {
            if(url.Query.Length>0)return null;
            if(type=="https")LinkField(node,"tls","true");
            var pair=auth.Split(new[]{':'},2);LinkField(node,"username",pair[0]);if(pair.Length==2)LinkField(node,"password",pair[1]);
        } else if(type=="hysteria2"||type=="anytls"){
            if(auth.Length==0||args.AllKeys.Any(k=>!(type=="hysteria2"?new[]{"sni","insecure","obfs","obfs-password"}:new[]{"sni","insecure"}).Contains(k)))return null;
            if(args["insecure"]!=null&&args["insecure"]!="0")return null;
            string obfs=args["obfs"];
            if((obfs==null)!=(args["obfs-password"]==null)||(obfs!=null&&(obfs!="salamander"||string.IsNullOrEmpty(args["obfs-password"]))))return null;
            LinkField(node,"password",auth);LinkField(node,"sni",args["sni"]);
            LinkField(node,"obfs",obfs);LinkField(node,"obfs-password",args["obfs-password"]);
        } else {
            if(auth.Length==0 || !new[]{null,"none"}.Contains(args["headerType"]) || !new[]{null,"none"}.Contains(args["encryption"]))return null;
            if(type=="vless"){Guid uuid;if(!Guid.TryParse(auth,out uuid))return null; LinkField(node,"uuid",uuid.ToString());LinkField(node,"flow",args["flow"]);}
            else LinkField(node,"password",auth);
            var security=args["security"];if(!new[]{null,"none","tls","reality"}.Contains(security))return null;
            if(security=="tls" || security=="reality" || type=="trojan") LinkField(node,"tls","true");
            LinkField(node,type=="vless"?"servername":"sni",args["sni"]);LinkField(node,"client-fingerprint",args["fp"]);
            if(security=="reality") {
                if(string.IsNullOrEmpty(args["pbk"]))return null;
                var entry=new YamlMappingNode();LinkField(entry,"public-key",args["pbk"]);LinkField(entry,"short-id",args["sid"]);node.Add("reality-opts",entry);
            }
            var allowed=new[]{"type","security","encryption","flow","headerType","sni","fp","pbk","sid","path","host","serviceName","spx","allowInsecure","udp"};
            if(args.AllKeys.Any(k=>!allowed.Contains(k))||args.AllKeys.Any(k=>{var values=args.GetValues(k);return values!=null&&values.Length>1;})||
                (args["spx"]!=null&&args["spx"]!=""&&args["spx"]!="/")||(args["allowInsecure"]!=null&&args["allowInsecure"]!="0")||
                (args["udp"]!=null&&!new[]{"0","1","true","false"}.Contains(args["udp"])))return null;
            if(args["udp"]!=null)LinkField(node,"udp",args["udp"]=="1"||args["udp"]=="true"?"true":"false");
            return Transport(node,args["type"],args["path"],args["host"],args["serviceName"]);
        }
        return node;
    }
    private static YamlMappingNode LegacyShadowsocks(string line){
        var parts=line.Substring(5).Split(new[]{'#'},2);
        string value=Decode64(parts[0]);int at=value.LastIndexOf('@');
        if(at<0)return null;
        var auth=value.Substring(0,at).Split(new[]{':'},2);
        if(auth.Length!=2||auth.Any(string.IsNullOrEmpty))return null;
        Uri target;
        if(!Uri.TryCreate("ss://"+value.Substring(at+1),UriKind.Absolute,out target)||target.UserInfo.Length>0||
           target.Query.Length>0||target.Fragment.Length>0||(target.AbsolutePath!=""&&target.AbsolutePath!="/")||target.Port<1)return null;
        var node=new YamlMappingNode();
        LinkField(node,"name",parts.Length==2&&parts[1].Length>0?Uri.UnescapeDataString(parts[1]):target.Host);
        LinkField(node,"type","ss");LinkField(node,"cipher",auth[0]);LinkField(node,"password",auth[1]);
        LinkField(node,"server",target.IdnHost.Trim('[',']'));LinkField(node,"port",target.Port.ToString(CultureInfo.InvariantCulture));
        return node;
    }
    private static void LinkField(YamlMappingNode node,string key,string val) { if(!string.IsNullOrEmpty(val))node.Add(key,val); }
    private static YamlMappingNode Transport(YamlMappingNode node,string transport,string path,string host,string grpc) {
        if(string.IsNullOrEmpty(transport)||transport=="tcp")return node;
        var entry=new YamlMappingNode();
        if(transport=="ws") {LinkField(entry,"path",path);if(!string.IsNullOrEmpty(host)){var headers=new YamlMappingNode();LinkField(headers,"Host",host);entry.Add("headers",headers);}node.Add("ws-opts",entry);}
        else if(transport=="grpc") {LinkField(entry,"grpc-service-name",grpc);node.Add("grpc-opts",entry);}
        else return null;
        node.Add("network",transport);return node;
    }
    private static ParsedSource ParseYaml(string input,bool nodesOnly=false) {
        var result=new ParsedSource {Format="Clash / Mihomo YAML"};bool providersOnly=false, noList=false;
        try {
            var parser=new Parser(new StringReader(input)); int depth=0,count=0,docs=0;
            while(parser.MoveNext()) {
                var e=parser.Current;
                if (++count>90000 || (e is DocumentStart && ++docs>1)) throw new FormatException();
                if(e is AnchorAlias) throw new FormatException();
                var node=e as NodeEvent; if(node!=null && (!node.Anchor.IsEmpty || !node.Tag.IsEmpty)) throw new FormatException();
                if((e is MappingStart || e is SequenceStart) && ++depth>24) throw new FormatException();
                if(e is MappingEnd || e is SequenceEnd) depth--;
            }
            var stream=new YamlStream();stream.Load(new StringReader(input));
            var map=stream.Documents.Single().RootNode as YamlMappingNode;
            var list=map==null?null:Child(map,"proxies") as YamlSequenceNode;
            providersOnly=map!=null&&Child(map,"proxy-providers")!=null;
            noList=list==null;
            if(nodesOnly&&(list==null||map.Children.Count!=1)) throw new FormatException();
            if(list!=null)foreach(var node in list.Children) {
                var entry=node as YamlMappingNode;
                if(entry==null || result.Nodes.Count>=200 || !Add(result,entry)) result.Skipped++;
            }
        } catch (Exception e) {
            if(!(e is YamlException || e is FormatException || e is InvalidOperationException || e is ArgumentException))throw;
            throw new InvalidOperationException(nodesOnly?"格式：请保存只含 proxies 列表的 YAML，不支持锚点、标签或多文档。":"格式：内容不是有效的订阅 YAML，或使用了不支持的锚点、标签或多文档。");
        }
        if(noList)throw new InvalidOperationException(providersOnly?"兼容性：此配置只有 proxy-providers，没有内联节点；流枢不会自动下载配置引用的其他订阅。请向服务商索取含 proxies 的版本。":"格式：未找到 Clash/Mihomo proxies、JSON outbounds 或受支持的节点列表。");
        if(result.Nodes.Count==0) throw new InvalidOperationException(result.Skipped>0?"兼容性：已识别 YAML 节点列表，但节点类型或参数无效或暂不支持。":"内容：订阅的 YAML 节点列表为空。");
        if(nodesOnly&&result.Skipped>0)throw new InvalidOperationException("有节点无效或已超过 200 个，请修正后保存。");
        return result;
    }
    private static bool Add(ParsedSource result,YamlMappingNode node) {
        string name=Scalar(node,"name"), kind=Scalar(node,"type"), server=Scalar(node,"server"); int port;
        if(string.IsNullOrWhiteSpace(name)||name.Length>180||name.Any(char.IsControl)||!types.Contains(kind) ||
            Uri.CheckHostName(server.Trim('[',']'))==UriHostNameType.Unknown || !int.TryParse(Scalar(node,"port"),out port)||port<1||port>65535 || !SafeKeys(node) || node.Children.Keys.Any(k=>!nodeKeys.Contains(((YamlScalarNode)k).Value)) ||
            (Child(node,"plugin")!=null && !new[]{"obfs","v2ray-plugin","shadow-tls"}.Contains(Scalar(node,"plugin")))) return false;
        string id="FP-"+(result.Nodes.Count+1).ToString("D3",CultureInfo.InvariantCulture);
        node.Children[new YamlScalarNode("name")]=new YamlScalarNode(id);
        result.Nodes.Add(new ProxyNode {Id=id,Name=name,Kind=kind,Data=node}); return true;
    }
    private static bool SafeKeys(YamlNode value) {
        var map=value as YamlMappingNode;
        if(map!=null) return map.Children.All(p=>p.Key is YamlScalarNode && !string.IsNullOrEmpty(((YamlScalarNode)p.Key).Value) && !forbidden.Contains(((YamlScalarNode)p.Key).Value) && SafeKeys(p.Value));
        var array=value as YamlSequenceNode; return array==null || array.Children.All(SafeKeys);
    }
    private static YamlNode Child(YamlMappingNode map,string name) { YamlNode n; return map.Children.TryGetValue(new YamlScalarNode(name),out n)?n:null; }
    private static string Scalar(YamlMappingNode map,string name) { var n=Child(map,name) as YamlScalarNode; return n==null?"":n.Value??""; }
    internal static string ProxiesYaml(IEnumerable<ProxyNode> nodes) {
        var root=new YamlMappingNode();root.Add("proxies",new YamlSequenceNode(nodes.Select(x=>(YamlNode)x.Data)));
        var output=new StringWriter(CultureInfo.InvariantCulture);new YamlStream(new YamlDocument(root)).Save(output,false);
        return output.ToString().TrimEnd().TrimEnd('.').TrimEnd() + "\n";
    }
    internal static bool SameNodeConfig(ProxyNode a,ProxyNode b) {
        if(a.Kind!=b.Kind||a.Engine!=b.Engine)return false;
        if(a.Data==null||b.Data==null)
            return a.Native!=null&&b.Native!=null&&SameJsonValue(a.Native,b.Native);
        var name=new YamlScalarNode("name");var left=a.Data.Children;var right=b.Data.Children;
        return left.Count==right.Count&&left.Where(x=>!x.Key.Equals(name)).All(x=>{
            YamlNode value;return right.TryGetValue(x.Key,out value)&&x.Value.Equals(value);
        });
    }
    private static bool SameJsonValue(object left,object right){
        var map=left as IDictionary<string,object>;
        if(map!=null){
            var other=right as IDictionary<string,object>;
            return other!=null&&map.Count==other.Count&&map.All(pair=>{
                object value;return other.TryGetValue(pair.Key,out value)&&SameJsonValue(pair.Value,value);
            });
        }
        var array=left as object[];
        if(array!=null){
            var other=right as object[];
            return other!=null&&array.Length==other.Length&&array.Zip(other,SameJsonValue).All(equal=>equal);
        }
        return object.Equals(left,right);
    }
    internal static string ExportYaml(ParsedSource source) {
        if(source.Nodes.Any(x=>x.Data==null))throw new InvalidOperationException("此订阅是原生 JSON，无法无损转换为 Clash/Mihomo YAML。");
        return ProxiesYaml(source.Nodes.Select(x=>{
            var copy=new YamlMappingNode(x.Data.Children);
            copy.Children[new YamlScalarNode("name")]=new YamlScalarNode(x.Name);
            return new ProxyNode{Data=copy};
        }));
    }
    internal static ParsedSource ParseEditedYaml(string input) {
        if(string.IsNullOrWhiteSpace(input)||input.Length>MaxSize)throw new InvalidOperationException("YAML 内容为空或超过大小限制。");
        return ParseYaml(input,true);
    }
    internal static ParsedSource WithOverride(string input,string patch) {
        return ApplyOverride(Parse(input),patch);
    }
    private static ParsedSource ApplyOverride(ParsedSource data,string patch) {
        if(string.IsNullOrWhiteSpace(patch))return data;
        if(patch.Length>12000)throw new InvalidOperationException("覆写内容过长。");
        if(data.Nodes.Any(n=>n.Data==null))throw new InvalidOperationException("本地覆写仅支持 Clash/Mihomo 节点。");
        IDictionary<string,object> spec;
        try{spec=LocalData.Parse(patch);}catch(ArgumentException){spec=null;}catch(InvalidOperationException){spec=null;}
        if(spec==null||spec.Keys.Any(k=>k!="prefix"&&k!="exclude"))throw new InvalidOperationException("覆写需为 JSON 对象，只支持 prefix 和 exclude。");
        object pv,ev;
        string prefix=spec.TryGetValue("prefix",out pv)?pv as string:"";
        var exclude=spec.TryGetValue("exclude",out ev)?ev as object[]:new object[0];
        if(prefix==null||prefix.Length>40||prefix.Any(char.IsControl)||exclude==null||exclude.Length>200||
            exclude.Any(v=>!(v is string)||((string)v).Length>180||((string)v).Any(char.IsControl)))
            throw new InvalidOperationException("prefix 须为 0–40 字的字符串，exclude 须为节点名称数组。");
        var names=new HashSet<string>(exclude.Cast<string>(),StringComparer.Ordinal);
        data.Nodes.RemoveAll(n=>names.Contains(n.Name));
        if(data.Nodes.Count==0)throw new InvalidOperationException("覆写排除了所有节点。");
        if(data.Nodes.Any(n=>prefix.Length+n.Name.Length>180))throw new InvalidOperationException("加前缀后节点名称超过 180 字。");
        foreach(var n in data.Nodes)n.Name=prefix+n.Name;
        return data;
    }
}
