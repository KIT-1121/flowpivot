using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

internal static class LoopbackTest {
  private static IDictionary<string,object> Args(params string[] pairs) {
    var d=new Dictionary<string,object>(); for(int i=0;i<pairs.Length;i+=2)d.Add(pairs[i],pairs[i+1]);return d;
  }
  private static IDictionary<string,object> Call(ClientService s,string a,IDictionary<string,object> p) {return LocalData.Parse(LocalData.Json.Serialize(s.Run(a,p)));}
  private static void Assert(bool v,string stage) {if(!v)throw new Exception(stage);Console.WriteLine("PASS: "+stage);}
  private static TcpClient Hold(int proxy,string target){
    var client=new TcpClient("127.0.0.1",proxy);
    try{var stream=client.GetStream();stream.ReadTimeout=1600;var bytes=System.Text.Encoding.ASCII.GetBytes("GET http://"+target+"/hold HTTP/1.1\r\nHost: "+target+"\r\n\r\n");stream.Write(bytes,0,bytes.Length);if(stream.ReadByte()!='H')throw new IOException("Connection was not held");return client;}
    catch{client.Dispose();throw;}
  }
  private static int Main(string[] a) { try{return Test(a);}catch(Exception e){Console.Error.WriteLine(e.GetType().Name+": "+e.Message+"\n"+e.StackTrace);return 1;} }
  private static object Release(string tag,bool preview=false,bool draft=false,bool missing=false,bool wrongHost=false) {
    string url="https://github.com/KIT-1121/flowpivot/releases/download/"+tag+"/";
    var items=new List<object>();
    foreach(var suffix in new[]{"setup.exe","portable.zip"}){
      if(missing&&suffix=="portable.zip")continue;
      string name="FlowPivot-"+tag.TrimStart('v')+"-windows-x64-"+suffix;
      items.Add(new Dictionary<string,object>{{"name",name},{"browser_download_url",(wrongHost?"https://wrong.invalid/":url)+name},{"state","uploaded"},{"size",123}});
    }
    return new Dictionary<string,object>{{"tag_name",tag},{"prerelease",preview},{"draft",draft},{"assets",items.ToArray()}};
  }
  private static int Updates(){
    Func<string,object[],AppUpdate.Candidate> select=(v,releases)=>AppUpdate.Select(LocalData.Json.DeserializeObject(LocalData.Json.Serialize(releases)) as object[],v);
    Assert(select("0.2.0-alpha.3",new[]{Release("v0.2.0-alpha.2",true),Release("v0.2.0-alpha.3",true)})==null,"no downgrade or same version offered");
    var preview=select("0.2.0-alpha.3",new[]{Release("v0.2.0-alpha.9",true),Release("v0.2.0-alpha.10",true)});
    Assert(preview.Version=="0.2.0-alpha.10"&&preview.Setup.EndsWith("-setup.exe")&&preview.Portable.EndsWith("-portable.zip"),"numeric prerelease comparison, exact Windows assets");
    Assert(select("0.2.0-alpha.3",new[]{Release("v0.2.0"),Release("0.2.0-rc.2",true)}).Version=="0.2.0","stable wins over same-base prerelease");
    Assert(select("0.2.0",new[]{Release("v0.3.0-alpha.1",true),Release("v0.3.0-alpha.2"),Release("v0.3.0",true),Release("v0.2.1")}).Version=="0.2.1","stable ignores previews");
    Assert(select("0.2.0",new[]{Release("v0.4.0",false,true),Release("v0.3.0",false,false,true),Release("v0.5.0",false,false,false,true),Release("v0.7.0/elsewhere")})==null,"ignore draft, incomplete, malformed and offsite releases");
    return 0;
  }
  private static int Test(string[] a) {
    if(a.Length==1&&a[0]=="--updates")return Updates();
    if(a.Length==1&&a[0]=="--check-live") {var update=AppUpdate.Check();Console.WriteLine(update==null?"PASS: live GitHub check; no eligible release":"PASS: live GitHub check; release "+update.Version);return 0;}
    if(a.Length!=2&&!(a.Length==3&&a[2]=="--system-proxy"))throw new ArgumentException("Usage: <cores directory> <fresh profile> [--system-proxy]");
    Directory.CreateDirectory(a[1]);
    var saved=Path.Combine(a[1],"subscriptions.dat");
    if(File.Exists(saved))throw new InvalidOperationException("A fresh test profile is required.");
    LocalData.Write(saved,"{\"Sources\":[],\"Cores\":{\"Mihomo\":{\"Path\":\"C:\\nonexistent.exe\",\"Version\":\"v0.0.0\"}}}");
    foreach(var core in CoreAsset.Catalog){
      string path=Path.Combine(a[0],core.Id,core.Exe);
      Assert(MihomoSession.Inspect(path,core)==core.Version,"bundled pin and x64 runtime: "+core.Id);
    }
    var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;
    int seen=0;var release=new ManualResetEventSlim(false);
    var accept=Task.Run(()=>{try{while(true){var client=listener.AcceptTcpClient();Task.Run(()=>{try{using(client)using(var stream=client.GetStream())using(var reader=new StreamReader(stream)){
        string line=reader.ReadLine(); if(line==null)return; string header; do{header=reader.ReadLine();}while(!String.IsNullOrEmpty(header));
        if(line.StartsWith("CONNECT example.invalid:")){
          var tunnel=System.Text.Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n");stream.Write(tunnel,0,tunnel.Length);
          line=reader.ReadLine();if(line==null)return;do{header=reader.ReadLine();}while(!String.IsNullOrEmpty(header));
        } else if(!line.Contains("example.invalid")&&!line.Contains("/hold"))return;
        if(line.Contains("/hold")){var ready=System.Text.Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 10000\r\n\r\nready");stream.Write(ready,0,ready.Length);release.Wait(4500);return;}
        Thread.Sleep(25);
        Interlocked.Increment(ref seen);
        var bytes=System.Text.Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 5\r\nConnection: close\r\n\r\nhello");stream.Write(bytes,0,bytes.Length);
      }}catch(IOException){} });}}catch(SocketException){} });
    try{
      using(var service=new ClientService(a[1],false,a[0])){
        var s=Call(service,"import",Args("input","proxies:\n  - {name: Test, type: http, server: 127.0.0.1, port: "+port+"}\n","origin","file"));
        var id=LocalData.Text(LocalData.Rows(s,"sources").Single(),"id");
        Call(service,"select-source",Args("id",id));Call(service,"select-node",Args("id","FP-001"));
        Assert(LocalData.Rows(s,"cores").All(c=>LocalData.Text(c,"version")==CoreAsset.Pick(LocalData.Text(c,"id")).Version),"legacy core overrides ignored; all bundled versions ready");
        try {s=Call(service,"start",Args("mode","local"));}catch(Exception e){Console.Error.WriteLine(e);throw;}
        Assert(Convert.ToBoolean(s["online"]),"real Mihomo starts");
        Assert(service.LocalProxyAddress()=="127.0.0.1:"+LocalData.Number(s,"port"),"copy target matches live listening port");
        int status;
        var response=Subscription.Request(new Uri("http://example.invalid/ping"),new WebProxy("http://127.0.0.1:"+LocalData.Number(s,"port")),30,out status,false);
        Assert(status==200&&response=="hello"&&seen==1,"actual request through core and fixture");
        var checks=service.Run("check",new Dictionary<string,object>{{"urls",new object[]{"http://example.invalid/diagnostic"}}});
        Assert(LocalData.Json.Serialize(checks).Contains("\"status\":200")&&seen==2,"routed diagnostic");
        var progress=new System.Collections.Concurrent.ConcurrentQueue<string>();
        var delaySnapshot=service.Run("delay",Args("url","http://example.invalid/latency"),msg=>progress.Enqueue(LocalData.Json.Serialize(msg)));
        s=LocalData.Parse(LocalData.Json.Serialize(delaySnapshot));
        var packets=progress.Select(LocalData.Parse).ToArray();
        var delay=LocalData.Number(LocalData.Rows(s,"nodes").Single(),"delay");
        Assert(delay>0,"core delay");
        Assert(packets.Length==2&&(packets[0]["ids"] as object[]).SequenceEqual(new[]{"FP-001"})&&LocalData.Text(packets[1],"id")=="FP-001"&&LocalData.Number(packets[1],"delay")==delay,"real batch and per-node progress before final snapshot");
        s=Call(service,"snapshot",Args());Assert(LocalData.Number(LocalData.Obj(s,"traffic"),"down")>0,"controller traffic counters");
        using(var direct=Hold((int)LocalData.Number(s,"port"),"127.0.0.1:"+port))
        using(var routed=Hold((int)LocalData.Number(s,"port"),"example.invalid")){
          var connections=LocalData.Obj(Call(service,"snapshot",Args()),"connections");
          Assert(LocalData.Number(connections,"direct")>=1&&LocalData.Number(connections,"routed")>=1,"live connection rows distinguish direct and routed requests");
        }
        release.Set();
        Assert(!Convert.ToBoolean(Call(service,"stop",Args())["online"]),"stop");
        Assert(Subscription.Parse(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("http://127.0.0.1:"+port+"#Test"))).Nodes.Count==1,"Base64 subscribe parser");
        Assert(ClientService.DecodeRisk(LocalData.Parse("{\"detections\":{\"hosting\":true,\"proxy\":false,\"risk\":21}}")).Contains("21/100"),"actual risk fields, no invented score");
        Assert(ClientService.DecodeRisk(LocalData.Parse("{\"detections\":{\"hosting\":null,\"risk\":null}}")).StartsWith("未评估"),"missing risk data does not read as clean");
        if(a.Contains("--system-proxy")) {
          var previous=WindowsProxy.Get();
          if((previous.Flags&2)!=0||(previous.Flags&4)!=0){Console.WriteLine("SKIP: an existing manual proxy or PAC is active");}else{
            try{s=Call(service,"start",Args("mode","system"));Assert(WindowsProxy.Get().Server=="127.0.0.1:"+LocalData.Number(s,"port"),"system proxy is actually applied");}finally{Call(service,"stop",Args());}
            Assert(previous.EqualsConfig(WindowsProxy.Get()),"system proxy restored on stop");
          }
        }
        {
          for(int i=0;i<3;i++){
            var engine=new[]{"Xray","sing-box","V2Fly"}[i];
            string input=i==1?"{\"outbounds\":[{\"type\":\"direct\"},{\"type\":\"http\",\"tag\":\"Test\",\"server\":\"127.0.0.1\",\"server_port\":"+port+"}]}"
              :"{\"outbounds\":[{\"protocol\":\"freedom\"},{\"protocol\":\"http\",\"tag\":\"Test\",\"settings\":{\"servers\":[{\"address\":\"127.0.0.1\",\"port\":"+port+"}]}}]}";
            s=Call(service,"import",Args("input",input,"origin","file"));
            var next=LocalData.Text(LocalData.Rows(s,"sources").Last(),"id");
            Call(service,"select-source",Args("id",next));Call(service,"select-node",Args("id","FP-001"));
            if(i==2)Call(service,"preferred",Args("value","V2Fly"));
            s=Call(service,"start",Args("mode","local"));
            Assert(LocalData.Text(s,"engine")==engine,"automatic selected engine: "+engine);
            var prior=seen;
            response=Subscription.Request(new Uri("http://example.invalid/native"),new WebProxy("http://127.0.0.1:"+LocalData.Number(s,"port")),30,out status,false);
            Assert(status==200&&response=="hello"&&seen==prior+1,"real traversal: "+engine);
            s=Call(service,"delay",Args("url","http://example.invalid/latency"));
            Assert(LocalData.Number(LocalData.Rows(s,"nodes").Single(),"delay")>0,"measured native node: "+engine);
            Call(service,"stop",Args());
          }
        }
      }
      using(var service=new ClientService(a[1],false,a[0]))Assert(LocalData.Text(Call(service,"snapshot",Args()),"node")=="FP-001","DPAPI persistence");
    }finally{release.Set();listener.Stop();accept.Wait(2000);}
    return 0;
  }
}
