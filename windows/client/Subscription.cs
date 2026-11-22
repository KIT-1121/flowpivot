using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

internal static class LocalData
{
    internal static JavaScriptSerializer Json { get { return new JavaScriptSerializer { MaxJsonLength = 40000000, RecursionLimit = 48 }; } }
    internal static string Text(IDictionary<string,object> d, string key) { object v; return d != null && d.TryGetValue(key,out v) ? v as string ?? "" : ""; }
    internal static IDictionary<string,object> Obj(IDictionary<string,object> d, string key) { object v; return d != null && d.TryGetValue(key,out v) ? v as IDictionary<string,object> : null; }
    internal static IEnumerable<IDictionary<string,object>> Rows(IDictionary<string,object> d, string key) {
        object v; var a = d != null && d.TryGetValue(key,out v) ? v as object[] : null;
        return a == null ? Enumerable.Empty<IDictionary<string,object>>() : a.OfType<IDictionary<string,object>>();
    }
    internal static long Number(IDictionary<string,object> d, string key) { object v; long n; return d != null && d.TryGetValue(key,out v) && long.TryParse(Convert.ToString(v,CultureInfo.InvariantCulture),out n) ? n : 0; }
    internal static IDictionary<string,object> Parse(string json) { return Json.DeserializeObject(json) as IDictionary<string,object>; }
    internal static void Write(string path, string json) {
        string temp = path + ".tmp";
        File.WriteAllBytes(temp, ProtectedData.Protect(Encoding.UTF8.GetBytes(json),null,DataProtectionScope.CurrentUser));
        if (File.Exists(path)) File.Replace(temp,path,null); else File.Move(temp,path);
    }
    internal static string Read(string path) { return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path),null,DataProtectionScope.CurrentUser)); }
}

internal sealed class SourceRecord
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Url { get; set; }
    public string Body { get; set; }
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
internal static class Subscription
{
    internal const int MaxSize = 1600000;
    private static readonly HashSet<string> types = new HashSet<string>("ss ssr vmess vless trojan hysteria hysteria2 tuic socks5 http anytls wireguard snell mieru".Split(' '));
    private static readonly HashSet<string> forbidden = new HashSet<string>("dialer-proxy certificate private-key ech-key pkcs12 ssh-key ssh-private-key private-key-file certificate-file certificate-path client-fingerprint-file ca-file ca-path fingerprint-file path-to-key key-file private-key-path client-certificate-path ssh-key-file ssh-key-path fingerprint-path".Split(' '),StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> nodeKeys = new HashSet<string>("name type server port username password tls sni servername alpn fingerprint client-fingerprint skip-cert-verify name-cert-verify reality-opts ech-opts udp udp-over-tcp udp-over-tcp-version cipher alterId uuid flow packet-encoding encryption network ws-opts grpc-opts http-opts h2-opts xhttp-opts smux plugin plugin-opts plugin-args ip-version obfs obfs-param protocol protocol-param auth auth-str up down ports hop-interval congestion-controller udp-relay-mode reduce-rtt heartbeat request-timeout disable-sni version persistent-keepalive public-key pre-shared-key ip ipv6 mtu reserved allowed-ips psk idle-session-check-interval idle-session-timeout min-idle-session headers fast-open tfo".Split(' '));
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
    private static string Request(Uri url, IWebProxy proxy, int cap, out int status, bool redirect, int timeout, string accept) {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        for (int i=0;i<5;i++) {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Proxy = proxy;
            request.UserAgent = "FlowPivot/0.2";
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
                        url = HttpUri(new Uri(url,response.Headers["Location"]).AbsoluteUri,true); continue;
                    }
                    if(cap == 0 || status >= 400) return "";
                    using (var body=response.GetResponseStream()) using(var data=new MemoryStream()) {
                        var chunk=new byte[8192]; int n;
                        while ((n=body.Read(chunk,0,chunk.Length))>0) {
                            if (data.Length + n > cap) throw new InvalidOperationException("响应超过大小限制。");
                            data.Write(chunk,0,n);
                        }
                        return new UTF8Encoding(false,true).GetString(data.ToArray()).TrimStart('\ufeff');
                    }
                }
            }
        }
        throw new InvalidOperationException("订阅重定向次数过多。");
    }
    internal static string Download(string text,IWebProxy proxy) {
        int code;
        try {
            string body=Request(HttpUri(text,true),proxy,MaxSize,out code,true,15000,
                "application/yaml, text/yaml, application/x-yaml, application/json, text/plain, application/octet-stream;q=0.8, */*;q=0.5");
            if(code==406) throw new InvalidOperationException("订阅服务器返回 HTTP 406，未提供可下载的订阅。请试用服务商提供的 Clash/Mihomo 专用订阅链接，或下载配置后从文件导入。");
            if(code < 200 || code >= 300) throw new InvalidOperationException("订阅服务器返回 HTTP " + code + "。");
            return body;
        } catch (WebException) { throw new InvalidOperationException("订阅下载失败（连接或证书错误、或请求超时）。"); }
    }
    internal static ParsedSource Parse(string body) {
        if(string.IsNullOrWhiteSpace(body) || body.Length > MaxSize) throw new InvalidOperationException("内容为空或超过大小限制。");
        if(body.TrimStart().StartsWith("{")) return NativeConfig.Parse(body);
        string compact = body.Trim();
        if(LooksLikeLinks(compact)) return ParseLinks(compact,"节点链接");
        if(!compact.Any(char.IsWhiteSpace)) {
            try {
                string decoded=Decode64(compact);
                if(LooksLikeLinks(decoded.Trim())) return ParseLinks(decoded,"Base64 节点列表");
            } catch (FormatException) {} catch (DecoderFallbackException) {}
        }
        return ParseYaml(body);
    }
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
            try {node=Link(line);} catch (Exception e) {if(!(e is FormatException || e is ArgumentException || e is DecoderFallbackException))throw;}
            if(node==null || !Add(result,node))result.Skipped++;
        }
        if(result.Nodes.Count==0) throw new InvalidOperationException("未识别可用节点。请导入含 proxies 的 Clash/Mihomo YAML，或支持的 HTTP/SOCKS5/VLESS/Trojan/SS/VMess 节点链接。");
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
        Uri url;
        if(!Uri.TryCreate(line,UriKind.Absolute,out url) || url.Host.Length==0 || url.Port<1 || (url.AbsolutePath!="/" && url.AbsolutePath!=""))return null;
        var type=url.Scheme; if(!new[]{"http","socks5","ss","vless","trojan"}.Contains(type))return null;
        var node=new YamlMappingNode();
        LinkField(node,"name",url.Fragment.Length>1?Uri.UnescapeDataString(url.Fragment.Substring(1)):url.Host);
        LinkField(node,"type",type);LinkField(node,"server",url.IdnHost.Trim('[',']'));LinkField(node,"port",url.Port.ToString(CultureInfo.InvariantCulture));
        var args=System.Web.HttpUtility.ParseQueryString(url.Query); string auth=Uri.UnescapeDataString(url.UserInfo);
        if(type=="ss") {
            if(url.Query.Length>0 || auth.Length==0)return null;
            if(!auth.Contains(":"))auth=Decode64(auth);
            var pair=auth.Split(new[]{':'},2);if(pair.Length!=2||pair.Any(string.IsNullOrEmpty))return null;
            LinkField(node,"cipher",pair[0]);LinkField(node,"password",pair[1]);
        } else if(type=="http" || type=="socks5") {
            if(url.Query.Length>0)return null;
            var pair=auth.Split(new[]{':'},2);LinkField(node,"username",pair[0]);if(pair.Length==2)LinkField(node,"password",pair[1]);
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
            var allowed=new[]{"type","security","encryption","flow","headerType","sni","fp","pbk","sid","path","host","serviceName"};
            if(args.AllKeys.Any(k=>!allowed.Contains(k)))return null;
            return Transport(node,args["type"],args["path"],args["host"],args["serviceName"]);
        }
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
    private static ParsedSource ParseYaml(string input) {
        var result=new ParsedSource {Format="Clash / Mihomo YAML"};
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
            if(list==null) throw new FormatException();
            foreach(var node in list.Children) {
                var entry=node as YamlMappingNode;
                if(entry==null || result.Nodes.Count>=200 || !Add(result,entry)) result.Skipped++;
            }
        } catch (Exception e) {
            if(!(e is YamlException || e is FormatException || e is InvalidOperationException || e is ArgumentException))throw;
            throw new InvalidOperationException("请导入含 proxies 列表的 Clash/Mihomo YAML。不支持 YAML 锚点、显式标签或多文档，也不会执行订阅中的规则或其他配置。");
        }
        if(result.Nodes.Count==0) throw new InvalidOperationException("没有可用节点。请检查节点类型、名称、服务器和端口。");
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
}
