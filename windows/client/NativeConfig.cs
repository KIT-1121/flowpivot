using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

internal static class NativeConfig {
    private static bool Fields(IDictionary<string,object> map,string allowed){return map!=null && map.Keys.All(k=>allowed.Split(' ').Contains(k));}
    private static bool Optional(IDictionary<string,object> map,string key,string fields){return !map.ContainsKey(key)||Fields(LocalData.Obj(map,key),fields);}
    private static bool Types(string value,string choices){return choices.Split(' ').Contains(value);}
    private static bool Host(IDictionary<string,object> m,string address,string port){return Uri.CheckHostName(LocalData.Text(m,address).Trim('[',']'))!=UriHostNameType.Unknown && LocalData.Number(m,port)>0&&LocalData.Number(m,port)<=65535;}
    private static readonly Regex safeName=new Regex(@"^[^\p{C}]{1,180}$");
    internal static ParsedSource Parse(string body){
        IDictionary<string,object> root;
        try{root=LocalData.Parse(body);}catch(ArgumentException){throw new InvalidOperationException("JSON 格式无效。");}
        var entries=LocalData.Rows(root,"outbounds").ToArray();
        bool sing=entries.Any(x=>x.ContainsKey("type")), v4=entries.Any(x=>x.ContainsKey("protocol"));
        if(sing==v4)throw new InvalidOperationException("只支持单一内核格式的 JSON outbounds 列表。");
        var result=new ParsedSource{Format=sing?"sing-box JSON":"Xray / V2Fly JSON"};
        foreach(var entry in entries){
            string kind=LocalData.Text(entry,sing?"type":"protocol");
            bool compatible=sing?Sing(entry,kind):V4(entry,kind);
            if(result.Nodes.Count>=200||!compatible){result.Skipped++;continue;}
            string id="FP-"+(result.Nodes.Count+1).ToString("D3"), tag=LocalData.Text(entry,"tag");
            entry["tag"]="upstream";
            result.Nodes.Add(new ProxyNode{Id=id,Name=safeName.IsMatch(tag)?tag:kind+" "+(result.Nodes.Count+1),Kind=kind,Native=entry,Engine=sing?"sing-box":"Xray",V2Compatible=!sing&&Types(kind,"vmess shadowsocks socks http trojan")&&V2Transport(LocalData.Obj(entry,"streamSettings"))});
        }
        if(result.Nodes.Count==0)throw new InvalidOperationException("没有通过校验的代理出站。规则、监听器、直连和不支持的出站不会导入。");
        return result;
    }
    private static bool V2Transport(IDictionary<string,object> stream){return stream==null||Types(LocalData.Text(stream,"network")," tcp ws grpc")&&Types(LocalData.Text(stream,"security")," none tls");}
    private static bool Sing(IDictionary<string,object> e,string kind){
        if(!Types(kind,"http socks shadowsocks vmess vless trojan hysteria2 tuic") || !Fields(e,"type tag server server_port username password version method uuid security alter_id flow packet_encoding network tls transport obfs congestion_control udp_relay_mode up_mbps down_mbps") || !Host(e,"server","server_port"))return false;
        if(!Optional(e,"tls","enabled server_name alpn insecure utls reality")||!Optional(e,"transport","type path headers service_name")||!Optional(e,"obfs","type password"))return false;
        var tls=LocalData.Obj(e,"tls");
        if(tls!=null&&(!Optional(tls,"utls","enabled fingerprint")||!Optional(tls,"reality","enabled public_key short_id")))return false;
        var trans=LocalData.Obj(e,"transport");
        return trans==null || (Types(LocalData.Text(trans,"type"),"ws grpc http httpupgrade") && Optional(trans,"headers","Host host"));
    }
    private static bool V4(IDictionary<string,object> e,string kind){
        if(!Types(kind,"http socks shadowsocks vmess vless trojan")||!Fields(e,"protocol tag settings streamSettings"))return false;
        string list=Types(kind,"vmess vless")?"vnext":"servers";
        var settings=LocalData.Obj(e,"settings");if(!Fields(settings,list))return false;
        var servers=LocalData.Rows(settings,list).ToArray(); if(servers.Length!=1)return false;
        var server=servers[0];
        if(!Fields(server,"address port users method password email level")||!Host(server,"address","port"))return false;
        if(server.ContainsKey("users")&&!LocalData.Rows(server,"users").All(u=>Fields(u,"id alterId security encryption flow user pass level")))return false;
        var stream=LocalData.Obj(e,"streamSettings");if(stream==null)return !e.ContainsKey("streamSettings");
        if(!Fields(stream,"network security tlsSettings realitySettings wsSettings grpcSettings tcpSettings rawSettings xhttpSettings httpupgradeSettings"))return false;
        if(!Types(LocalData.Text(stream,"network")," tcp raw ws grpc xhttp httpupgrade")||!Types(LocalData.Text(stream,"security")," none tls reality"))return false;
        if(!Optional(stream,"tlsSettings","serverName allowInsecure alpn fingerprint")||!Optional(stream,"realitySettings","serverName fingerprint publicKey password shortId spiderX")||!Optional(stream,"grpcSettings","serviceName authority multiMode")||!Optional(stream,"xhttpSettings","host path mode"))return false;
        foreach(string k in new[]{"wsSettings","httpupgradeSettings"}){
            if(!Optional(stream,k,"path host headers maxEarlyData earlyDataHeaderName"))return false;
            var sub=LocalData.Obj(stream,k);if(sub!=null&&!Optional(sub,"headers","Host host"))return false;
        }
        foreach(string k in new[]{"tcpSettings","rawSettings"}){var sub=LocalData.Obj(stream,k);if(stream.ContainsKey(k)&&(!Fields(sub,"header")||!Optional(sub,"header","type")))return false;}
        return true;
    }
    internal static string Build(ProxyNode node,string engine,int port,string user,string password){
        if(engine=="sing-box")return LocalData.Json.Serialize(new{log=new{disabled=true}, inbounds=new[]{new{ type="socks", tag="flowpivot",listen="127.0.0.1",listen_port=port,users=new[]{new{username=user,password}}}}, outbounds=new[]{node.Native}});
        return LocalData.Json.Serialize(new{log=new{loglevel="none"},inbounds=new[]{new{protocol="socks",tag="flowpivot",listen="127.0.0.1",port,settings=new{auth="password",accounts=new[]{new{user,pass=password}},udp=true}}},outbounds=new[]{node.Native}});
    }
}
