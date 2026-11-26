using System;
using System.Collections.Generic;
using System.Diagnostics;
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
  private static void RefreshRegression(string cores,string root,int localPort,int first,int second){
    var now=new DateTime(2026,1,1,0,0,0,DateTimeKind.Utc);
    string one="  - {name: Test, type: http, server: 127.0.0.1, port: "+first+"}\n";
    string two="  - {name: Alternate, type: http, server: 127.0.0.1, port: "+second+"}\n";
    string original="proxies:\n"+one+two, body=original;
    const string revision="subscriptionDisconnectRevision";
    bool fail=false;int requests=0;
    Func<string,IWebProxy,string,SubscriptionResult> fetch=(url,proxy,patch)=>{
      Interlocked.Increment(ref requests);
      if(fail)throw new InvalidOperationException("Fixture network failure");
      return new SubscriptionResult{Body=body,Data=Subscription.WithOverride(body,patch),Quota=new SubscriptionQuota{Total=100,Used=25,Remaining=75}};
    };
    using(var service=new ClientService(root,false,cores,()=>now,fetch)){
      var s=Call(service,"import",Args("input","https://subscription.invalid/example"));
      string id=LocalData.Text(LocalData.Rows(s,"sources").Single(),"id");
      s=Call(service,"import",Args("input","https://other.invalid/example"));
      string other=LocalData.Text(LocalData.Rows(s,"sources").Last(),"id");
      Call(service,"local-proxy-port",Args("value",localPort.ToString()));
      Call(service,"start",Args("mode","local"));
      Func<string> request=()=>{int status;return Subscription.Request(new Uri("http://example.invalid/ping"),new WebProxy("http://127.0.0.1:"+localPort),30,out status,false);};
      Assert(request()=="hello","refresh fixture forwards through original node");
      body=original+"\n# changed metadata\n";
      using(var held=Hold(localPort,"example.invalid")){
        var before=LocalData.Rows(LocalData.Obj(Call(service,"snapshot",Args()),"connections"),"rows").Select(r=>LocalData.Text(r,"id")).ToArray();
        s=Call(service,"refresh",Args("id",id));
        Assert(Convert.ToBoolean(s["online"])&&LocalData.Number(s,revision)==0&&LocalData.Number(s,"port")==localPort&&before.Length>0&&
          LocalData.Rows(LocalData.Obj(s,"connections"),"rows").Any(r=>before.Contains(LocalData.Text(r,"id"))),"metadata-only refresh keeps an existing connection and listening port");
      }
      body="proxies:\n"+two+one.Replace("name: Test","name: Renamed");
      s=Call(service,"refresh",Args("id",id));
      Assert(Convert.ToBoolean(s["online"])&&LocalData.Number(s,revision)==0&&LocalData.Text(s,"node")=="FP-002"&&LocalData.Text(s,"runningNodeName")=="Renamed"&&request()=="hello","rename and reorder retain the live upstream and map the saved selection");
      body="proxies:\n"+two.Replace("port: "+second,"port: "+first)+one.Replace("name: Test","name: Renamed");
      s=Call(service,"refresh",Args("id",id));
      Assert(Convert.ToBoolean(s["online"])&&LocalData.Number(s,revision)==0&&LocalData.Text(s,"runningNodeName")=="Renamed"&&request()=="hello","another node changing does not signal a disconnect or steal the selection");
      body="proxies:\n"+two;
      s=Call(service,"refresh",Args("id",other));
      Assert(Convert.ToBoolean(s["online"])&&request()=="hello","refreshing an inactive subscription leaves current forwarding alone");
      fail=true;bool rejected=false;
      try{Call(service,"refresh",Args("id",id));}catch(InvalidOperationException){rejected=true;}
      Assert(rejected&&request()=="hello","failed manual refresh preserves forwarding");
      fail=false;body="broken";
      rejected=false;try{Call(service,"refresh",Args("id",id));}catch(InvalidOperationException){rejected=true;}
      Assert(rejected&&request()=="hello","invalid refreshed content preserves forwarding");
      body="proxies:\n"+two;
      rejected=false;
      using(var locked=new FileStream(Path.Combine(root,"subscriptions.dat"),FileMode.Open,FileAccess.Read,FileShare.Read)){
        try{Call(service,"refresh",Args("id",id));}
        catch(IOException){rejected=true;}
      }
      s=Call(service,"snapshot",Args());
      Assert(rejected&&request()=="hello"&&LocalData.Number(s,revision)==0&&LocalData.Text(s,"runningNodeName")=="Renamed","failed persistence retains the old node, forwarding and disconnect revision");
      body=original;
      Call(service,"refresh-interval",Args("id",id,"value","2"));
      Assert(service.FetchDueSubscription()==null,"automatic refresh waits for configured interval");
      now=now.AddHours(2);
      var apply=service.FetchDueSubscription().Result;
      apply();
      s=Call(service,"snapshot",Args());
      Assert(Convert.ToBoolean(s["online"])&&LocalData.Number(s,revision)==0&&LocalData.Text(s,"node")=="FP-001"&&request()=="hello"&&service.FetchDueSubscription()==null,"due automatic refresh runs once, keeps connection and advances schedule");
      using(var reopened=new ClientService(root,false,cores,()=>now,fetch)){
        var record=LocalData.Rows(Call(reopened,"snapshot",Args()),"sources").First();
        Assert(LocalData.Number(record,"refreshHours")==2&&reopened.FetchDueSubscription()==null,"interval and next due time survive reload");
      }
      now=now.AddHours(2);fail=true;service.FetchDueSubscription().Result();
      s=Call(service,"snapshot",Args());
      Assert(Convert.ToBoolean(LocalData.Rows(s,"sources").First()["autoRefreshFailed"])&&service.FetchDueSubscription()==null&&request()=="hello","failed automatic refresh backs off and retains live session");
      now=now.AddHours(2);fail=false;body="proxies:\n"+two;
      apply=service.FetchDueSubscription().Result;
      Call(service,"refresh-interval",Args("id",id,"value","0"));apply();
      s=Call(service,"snapshot",Args());
      Assert(LocalData.Rows(s,"nodes").Count()==2&&service.FetchDueSubscription()==null,"disabling automatic update discards an in-flight stale result");
      Func<bool> listening=()=>{
        try{using(var probe=new TcpClient("127.0.0.1",localPort))return true;}
        catch(SocketException){return false;}
      };
      body="proxies:\n"+two.Replace("name: Alternate","name: Test");
      s=Call(service,"refresh",Args("id",id));
      Assert(!Convert.ToBoolean(s["online"])&&LocalData.Number(s,revision)==1&&!listening(),"manual refresh of a changed endpoint stops forwarding and signals once");
      s=Call(service,"snapshot",Args());
      Assert(LocalData.Number(s,revision)==1&&!Convert.ToBoolean(s["online"]),"snapshot does not repeat the event or reconnect automatically");
      s=Call(service,"start",Args("mode","local"));
      Assert(Convert.ToBoolean(s["online"])&&LocalData.Number(s,"port")==localPort&&request()=="other","manual start uses the updated endpoint on the same port");
      Call(service,"refresh-interval",Args("id",id,"value","1"));
      now=now.AddHours(1);body="proxies:\n"+one.Replace("name: Test","name: Replacement");
      service.FetchDueSubscription().Result();
      s=Call(service,"snapshot",Args());
      Assert(!Convert.ToBoolean(s["online"])&&LocalData.Number(s,revision)==2&&!listening()&&service.FetchDueSubscription()==null,"automatic refresh removing the running node disconnects without reconnecting");
      Assert(requests>=8,"fixture exercised manual and automatic downloads");
    }
  }
  private static void DomainDirect(string cores,string root,IPEndPoint destination,int upstreamPort){
    // Test-only host mappings keep the entire exercise local without touching Windows DNS.
    const string matching="api.baidu.test", other="api.other.test";
    var work=Path.Combine(root,"domain-fixture");
    Directory.CreateDirectory(work);
    var portProbe=new TcpListener(IPAddress.Loopback,0);
    portProbe.Start();
    var port=((IPEndPoint)portProbe.LocalEndpoint).Port;
    portProbe.Stop();
    var config=Path.Combine(work,"config.yaml");
    var routes=new[]{
      new DomainRoute{Id=Guid.NewGuid().ToString("N"),Kind="keyword",Pattern="baidu",Value="direct"},
      new DomainRoute{Id=Guid.NewGuid().ToString("N"),Kind="regex",Pattern=@"(^|\.)other\.test$",Value="direct"}
    };
    const string miss="notother.test";
    File.WriteAllText(config,"mixed-port: "+port+"\nallow-lan: false\nmode: rule\nlog-level: silent\n"+
      "hosts: {"+matching+": "+destination.Address+", "+other+": "+destination.Address+", "+miss+": "+destination.Address+"}\n"+
      "proxies:\n  - {name: UPSTREAM, type: http, server: 127.0.0.1, port: "+upstreamPort+"}\n"+
      TargetRouting.Yaml(routes,"UPSTREAM"));
    var core=CoreAsset.Pick("Mihomo");
    var start=new ProcessStartInfo(Path.Combine(cores,core.Id,core.Exe),"-d \""+work+"\" -f \""+config+"\""){
      UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=work
    };
    using(var process=Process.Start(start)){
      try{
        bool ready=false;
        for(int i=0;i<40&&!process.HasExited;i++){
          try{using(var probe=new TcpClient("127.0.0.1",port)){ready=true;break;}}catch(SocketException){Thread.Sleep(80);}
        }
        Assert(ready,"isolated Mihomo with temporary host mappings starts");
        var proxy=new WebProxy("http://127.0.0.1:"+port);
        Func<string,string> request=host=>{
          int status;
          return Subscription.Request(new Uri("http://"+host+":"+destination.Port+"/route-test"),proxy,30,out status,false);
        };
        Assert(request(matching)=="local"&&request(other)=="local"&&request(miss)=="other","domain keyword and regex bypass upstream; a nonmatch uses upstream");
      }finally{if(!process.HasExited)process.Kill();process.WaitForExit(5000);}
    }
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
  private static int SubscriptionResponses(){
    var left=new ProxyNode{Kind="fixture",Native=LocalData.Parse("{\"settings\":{\"a\":1,\"b\":[2,3]},\"tag\":\"upstream\"}")};
    var reordered=new ProxyNode{Kind="fixture",Native=LocalData.Parse("{\"tag\":\"upstream\",\"settings\":{\"b\":[2,3],\"a\":1}}")};
    var changed=new ProxyNode{Kind="fixture",Native=LocalData.Parse("{\"tag\":\"upstream\",\"settings\":{\"b\":[3,2],\"a\":1}}")};
    Assert(Subscription.SameNodeConfig(left,reordered)&&!Subscription.SameNodeConfig(left,changed),"native JSON key order is immaterial but array order is retained");
    const string yaml="proxies:\n  - {name: Fixture, type: http, server: 127.0.0.1, port: 18080}\n";
    var quota=SubscriptionQuota.Parse("upload=9; download=11; total=80; expire=99");
    Assert(quota.Total==80&&quota.Used==20&&quota.Remaining==60,"subscription quota splits total, used and remaining");
    var savedQuota=LocalData.Json.Deserialize<SourceRecord>(LocalData.Json.Serialize(new SourceRecord{Quota=quota})).Quota;
    Assert(savedQuota.Total==80&&savedQuota.Used==20&&savedQuota.Remaining==60,"quota survives profile serialization");
    Assert(SubscriptionQuota.Parse("upload=9; download=11; total=12").Remaining==0&&SubscriptionQuota.Parse("upload=9; download=11; total=0").Remaining==null&&
      SubscriptionQuota.Parse("upload=9; total=80").Used==null&&SubscriptionQuota.Parse("upload=0; upload=5; total=80")==null&&
      SubscriptionQuota.Parse("upload=9223372036854775807; download=1; total=80").Used==null,"quota handles missing, unlimited, duplicate, exceeded and overflowing values");
    var agents=new List<string>();
    var accepts=new List<string>();
    var timeouts=new List<int>();
    var imported=Subscription.DownloadParsed((string agent,string accept,int timeout,out int status)=>{
      agents.Add(agent);accepts.Add(accept);timeouts.Add(timeout);
      status=agents.Count==1?406:200;
      return status==200?yaml:"";
    });
    Assert(agents.ToArray().SequenceEqual(new[]{"FlowPivot/0.2","clash-verge/v2.5.9"}) &&
        accepts[0].Contains("application/yaml") && accepts[1]=="*/*" &&
        timeouts.Count==2 && timeouts[1]>0 && timeouts[1]<=timeouts[0] &&
        imported.Data.Nodes.Count==1,"406 triggers compatible retry and returns usable YAML");
    foreach(int firstStatus in new[]{200,401,403,406,429,500}){
      int count=0;
      try{
        imported=Subscription.DownloadParsed((string agent,string accept,int timeout,out int status)=>{
          count++;status=firstStatus;return status==200?yaml:"";
        });
        Assert(firstStatus==200&&count==1&&imported.Body==yaml,"200 with usable configuration does not retry");
      }catch(InvalidOperationException e){
        Assert(firstStatus!=200&&count==(firstStatus==406?3:1)&&
            e.Message.Contains("HTTP "+firstStatus),"HTTP "+firstStatus+" returns a bounded, sanitized error");
      }
    }
    int fallback=0;
    imported=Subscription.DownloadParsed((string agent,string accept,int timeout,out int status)=>{
      status=200;fallback++;
      return fallback==1?"<html>fixture</html>":fallback==2?"{\"message\":\"fixture\"}":yaml;
    });
    Assert(fallback==3&&imported.Data.Nodes.Count==1,"HTTP 200 without usable nodes also negotiates compatible formats");
    string links="ss://"+Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("aes-256-gcm:fixture@localhost:8042"))+"#Legacy\n"+
      "hysteria2://fixture@localhost:443/?sni=localhost&obfs=salamander&obfs-password=fixture#Hy2\n"+
      "anytls://fixture@localhost:443/#AnyTLS\n";
    string wrapped=Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(links),Base64FormattingOptions.InsertLineBreaks).TrimEnd('=').Replace('+','-').Replace('/','_');
    Assert(Subscription.Parse(wrapped).Nodes.Select(x=>x.Kind).SequenceEqual(new[]{"ss","hysteria2","anytls"}),"URL-safe wrapped Base64 and extended node formats");
    foreach(var sample in new[]{new[]{"<html>secret-marker</html>","网页"},new[]{"{\"error\":\"secret-marker\"}","接口消息"},new[]{"proxy-providers: {}","proxy-providers"},new[]{"vless://invalid@localhost:80","兼容性"},new[]{"proxies: []","为空"}}){
      try{Subscription.Parse(sample[0]);throw new Exception("Expected parse failure");}
      catch(InvalidOperationException e){Assert(e.Message.Contains(sample[1])&&!e.Message.Contains("secret-marker"),"sanitized parse diagnosis: "+sample[1]);}
    }
    Assert(Subscription.DownloadError(WebExceptionStatus.NameResolutionFailure).Contains("DNS")&&Subscription.DownloadError(WebExceptionStatus.Timeout).Contains("超时"),"differentiate DNS and timeout");
    int failures=0;
    try{
      Subscription.DownloadParsed((string agent,string accept,int timeout,out int status)=>{
        failures++;throw new WebException("Fixture",WebExceptionStatus.TrustFailure);
      });
    }catch(WebException e){Assert(failures==1&&e.Status==WebExceptionStatus.TrustFailure,"certificate failure does not retry");return 0;}
    throw new Exception("Expected a certificate failure");
  }
  private static int StoreRecovery(string root){
    Directory.CreateDirectory(root);
    string path=Path.Combine(root,"subscriptions.dat"), backup=path+".bak";
    var store=new SubscriptionStore(root);
    var damage=new byte[]{1,2,3,4};
    var model=new ClientModel{Sources=new List<SourceRecord>{
      new SourceRecord{Id="one",Name="Fixture",Body="proxies:\n  - {name: Fixture, type: http, server: 127.0.0.1, port: 18080}"}},Source="one",Node="FP-001",LocalProxyPort=17899};
    var parsed=new Dictionary<string,ParsedSource>();
    bool restored;
    Assert(store.Load(parsed,false,out restored).Sources.Count==0&&!restored&&!File.Exists(path),"empty first launch does not create or reset files");
    LocalData.Write(path,LocalData.Json.Serialize(model)); // alpha.22 file format
    Assert(store.Load(parsed,false,out restored).LocalProxyPort==17899&&!restored&&parsed.Count==1,"legacy encrypted profile loads unchanged");
    Assert(!store.PreserveAndReset()&&File.Exists(path)&&!File.Exists(backup),"reset refuses a readable profile");
    using(var locked=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.None)){
      var unlocked=Task.Run(()=>{Thread.Sleep(450);locked.Dispose();});
      Assert(store.Load(new Dictionary<string,ParsedSource>(),true,out restored).LocalProxyPort==17899&&!restored,"boot retries a temporarily locked file without resetting it");
      unlocked.Wait();
    }
    var before=File.ReadAllBytes(path);
    model.LocalProxyPort=17901;store.Write(model);
    Assert(File.ReadAllBytes(backup).SequenceEqual(before)&&LocalData.Read(path).Contains("17901"),"atomic save retains the preceding encrypted file");
    File.WriteAllBytes(path,damage);
    parsed.Clear();
    Assert(store.Load(parsed,false,out restored).LocalProxyPort==17899&&restored&&parsed.Count==1,"valid encrypted backup restores settings and subscriptions");
    Assert(File.ReadAllBytes(path).SequenceEqual(before)&&File.ReadAllBytes(backup).SequenceEqual(before)&&
      Directory.GetFiles(root,"subscriptions.dat.recovery-*").Select(File.ReadAllBytes).Any(b=>b.SequenceEqual(damage)),"backup remains valid and the unreadable original is archived byte for byte");
    var invalid=LocalData.Encrypt("{\"Sources\":null}");
    File.WriteAllBytes(backup,invalid);
    File.WriteAllBytes(path,damage);
    bool refused=false;
    try{store.Load(new Dictionary<string,ParsedSource>(),false,out restored);}catch(SubscriptionStoreException){refused=true;}
    Assert(refused&&File.ReadAllBytes(path).SequenceEqual(damage)&&File.ReadAllBytes(backup).SequenceEqual(invalid),"unreadable files do not silently reset, including an invalid decrypted backup");
    using(var locked=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){
      refused=false;
      try{store.PreserveAndReset();}catch(IOException){refused=true;}
      Assert(refused&&File.ReadAllBytes(path).SequenceEqual(damage),"failed reset leaves the original intact");
    }
    Assert(store.PreserveAndReset(),"confirmed reset can initialize a new encrypted library");
    Assert(store.Load(new Dictionary<string,ParsedSource>(),false,out restored).Sources.Count==0&&!restored&&
      Directory.GetFiles(root,"subscriptions.dat.recovery-*").Select(File.ReadAllBytes).Any(b=>b.SequenceEqual(invalid))&&
      File.ReadAllBytes(backup).SequenceEqual(invalid),"confirmed reset archives both originals and fresh profile reopens");
    store.Write(model);
    using(var reopened=new ClientService(root,false)){
      Assert(LocalData.Rows(Call(reopened,"snapshot",Args()),"sources").Count()==1,"store remains usable across app restarts after reimport");
    }
    File.Delete(path);
    Assert(store.Load(new Dictionary<string,ParsedSource>(),false,out restored).Sources.Count==0&&restored&&File.Exists(path),"missing primary recovers from the previous save");
    Assert(!Directory.GetFiles(root,"subscriptions.dat.*.tmp").Any(),"recovery leaves no temporary writes");
    return 0;
  }
  private static int NodeMemory(string root){
    string first="  - {name: One, type: http, server: 127.0.0.1, port: 18080}\n";
    string second="  - {name: Two, type: http, server: 127.0.0.1, port: 18081}\n";
    string body="proxies:\n"+first+second;
    Func<string,IWebProxy,string,SubscriptionResult> download=(url,proxy,patch)=>new SubscriptionResult{Body=body,Data=Subscription.WithOverride(body,patch)};
    string a,b;
    using(var service=new ClientService(root,false,downloader:download)){
      var s=Call(service,"import",Args("input","https://a.invalid/sub","name","A"));
      a=LocalData.Text(LocalData.Rows(s,"sources").Single(),"id");
      s=Call(service,"import",Args("input","https://b.invalid/sub","name","B"));
      b=LocalData.Text(LocalData.Rows(s,"sources").Last(),"id");
      Call(service,"select-node",Args("id","FP-002"));
      s=Call(service,"select-source",Args("id",b));
      Assert(LocalData.Text(s,"node")=="FP-001","a never-selected subscription starts with its default node");
      Call(service,"select-node",Args("id","FP-002"));
      s=Call(service,"select-source",Args("id",a));
      Assert(LocalData.Text(s,"node")=="FP-002","switching back remembers the previous node of subscription A");
      body="proxies:\n"+second+first;
      s=Call(service,"refresh",Args("id",b));
      Assert(LocalData.Text(s,"node")=="FP-002","refreshing subscription B does not alter selection in A");
      s=Call(service,"select-source",Args("id",b));
      Assert(LocalData.Text(s,"node")=="FP-001"&&LocalData.Text(LocalData.Rows(s,"nodes").First(),"name")=="Two","inactive refresh remaps the recorded endpoint despite changed positional IDs");
    }
    using(var service=new ClientService(root,false,downloader:download)){
      var s=Call(service,"snapshot",Args());
      Assert(LocalData.Text(s,"source")==b&&LocalData.Text(s,"node")=="FP-001","active node and subscription survive restart");
      s=Call(service,"select-source",Args("id",a));
      Assert(LocalData.Text(s,"node")=="FP-002","inactive selection survives restart as well");
      body="proxies:\n"+first;
      Call(service,"refresh",Args("id",b));
      s=Call(service,"select-source",Args("id",b));
      Assert(LocalData.Text(s,"node")=="FP-001"&&LocalData.Text(LocalData.Rows(s,"nodes").Single(),"name")=="One","removed remembered node falls back to a valid default");
    }
    var legacyRoot=Path.Combine(root,"legacy");
    Directory.CreateDirectory(legacyRoot);
    LocalData.Write(Path.Combine(legacyRoot,"subscriptions.dat"),LocalData.Json.Serialize(new ClientModel{Source="a",Node="FP-002",
      Sources=new List<SourceRecord>{
        new SourceRecord{Id="a",Name="A",Body="proxies:\n"+first+second},
        new SourceRecord{Id="b",Name="B",Body="proxies:\n"+first+second}
      }}));
    using(var legacy=new ClientService(legacyRoot,false)){
      Call(legacy,"select-source",Args("id","b"));
      Assert(LocalData.Text(Call(legacy,"select-source",Args("id","a")),"node")=="FP-002","upgrade keeps the last active selection from a legacy profile");
    }
    return 0;
  }
  private static int Test(string[] a) {
    if(a.Length==2&&a[0]=="--copy-source-url"){
      const string uri="https://fixture.invalid/sub?example=%2B%2F%3D&kind=sample";
      const string updated="https://fixture.invalid/updated?example=a%26b";
      const string yaml="proxies:\n  - {name: Fixture, type: http, server: 127.0.0.1, port: 18080}";
      int requests=0;
      using(var s=new ClientService(a[1],false,downloader:(url,proxy,patch)=>{
        requests++;return new SubscriptionResult{Body=yaml,Data=Subscription.Parse(yaml)};
      })){
        var state=Call(s,"import",Args("input",uri));
        string remote=LocalData.Text(LocalData.Rows(state,"sources").Single(),"id");
        state=Call(s,"import",Args("input",yaml,"origin","file"));
        string file=LocalData.Text(LocalData.Rows(state,"sources").Last(),"id");
        Assert(s.SubscriptionUrl(remote)==uri&&requests==1,"retrieve the exact URL including encoded query without downloading again");
        bool rejected=false;
        try{s.SubscriptionUrl(file);}catch(InvalidOperationException){rejected=true;}
        Assert(rejected,"local subscription file cannot be copied as a link");
        state=Call(s,"replace-url",Args("id",remote,"url",updated));
        Assert(s.SubscriptionUrl(remote)==updated&&requests==2,"copy retrieves the saved replacement URL");
        var serialized=LocalData.Json.Serialize(state);
        var logs=LocalData.Json.Serialize(s.Run("logs-read",Args()));
        Assert(!serialized.Contains("fixture.invalid")&&!logs.Contains("fixture.invalid"),"snapshot and logs do not disclose the subscription URL");
        using(var reopened=new ClientService(a[1],false))
          Assert(reopened.SubscriptionUrl(remote)==updated,"copy uses the persisted URL after restart");
      }
      return 0;
    }
    if(a.Length==2&&a[0]=="--store-recovery")return StoreRecovery(a[1]);
    if(a.Length==2&&a[0]=="--node-memory")return NodeMemory(a[1]);
    if(a.Length==1&&a[0]=="--updates")return Updates();
    if(a.Length==1&&a[0]=="--subscription-responses")return SubscriptionResponses();
    if(a.Length==1&&a[0]=="--target-routes"){
      foreach(var invalid in new[]{"localhost","foo.localhost","127.0.0.5","::1","::ffff:127.0.0.1","198.18.5.5","0.0.0.0","foo,bar","a\n- MATCH,DIRECT","https://example.invalid","1.2.3.4/24","test.invalid:443"})
        Assert(!TargetRouting.Editable(TargetRouting.Target(invalid)),"reject ambiguous, unsafe or protected targets");
      Assert(TargetRouting.Target("Example.Invalid.")=="example.invalid"&&TargetRouting.Target("例子.test")=="xn--fsqu00a.test","normalize exact DNS and IDN names");
      var rules=TargetRouting.LoadDomainRoutes(null,new Dictionary<string,string>{{"Example.Invalid","direct"},{"2001:db8::2","proxy"},{"192.0.2.30","direct"},{"ignored,direct","proxy"},{"keep.invalid","bogus"}});
      var yaml=TargetRouting.Yaml(rules,"FLOWPIVOT-SELECT");
      Assert(rules.Count==3&&yaml.Contains("DOMAIN,example.invalid,DIRECT")&&yaml.Contains("IP-CIDR6,2001:db8::2/128,FLOWPIVOT-SELECT,no-resolve")&&
        yaml.Contains("IP-CIDR,192.0.2.30/32,DIRECT,no-resolve")&&yaml.EndsWith("MATCH,FLOWPIVOT-SELECT\n"),"safe exact rules precede default and retain loopback protection");
      foreach(var invalid in new[]{"(a+)+$","a*a*$","(?=baidu)","(baidu)\\1","[abc","a,b","baidu\n- MATCH,DIRECT","a''b","a{1,99}"})
        Assert(!TargetRouting.ValidPattern("regex",invalid),"reject unsupported regex or rule syntax");
      foreach(var valid in new[]{@"(^|\.)baidu\.com$","baidu",".*baidu.*",@"cdn-[a-z]+\.baidu\.com$"})
        Assert(TargetRouting.ValidPattern("regex",valid),"accept common domain regex");
      Assert(TargetRouting.ValidPattern("keyword","BaIDu")&&!TargetRouting.ValidPattern("keyword","https://baidu.com"),"keywords take hostname text");
      var domainYaml=TargetRouting.Yaml(rules.Concat(new[]{
        new DomainRoute{Id=Guid.NewGuid().ToString("N"),Kind="keyword",Pattern="baidu",Value="direct"},
        new DomainRoute{Id=Guid.NewGuid().ToString("N"),Kind="regex",Pattern=@"(^|\.)baidu\.com$",Value="proxy"}
      }),"FLOWPIVOT-SELECT");
      int exactIndex=domainYaml.IndexOf("DOMAIN,example.invalid",StringComparison.Ordinal);
      int keywordIndex=domainYaml.IndexOf("DOMAIN-KEYWORD,baidu,DIRECT",StringComparison.Ordinal);
      int regexIndex=domainYaml.IndexOf("DOMAIN-REGEX,",StringComparison.Ordinal);
      Assert(exactIndex>=0&&keywordIndex>exactIndex&&regexIndex>keywordIndex&&domainYaml.EndsWith("MATCH,FLOWPIVOT-SELECT\n"),"domain rules preserve precedence and map direct to DIRECT");
      var oldKeyword=new DomainRoute{Id=Guid.NewGuid().ToString("N"),Kind="keyword",Pattern="192.0.2.30",Value="direct"};
      var merged=TargetRouting.LoadDomainRoutes(new[]{oldKeyword},new Dictionary<string,string>{{oldKeyword.Pattern,"proxy"},{"old.invalid","proxy"}});
      Assert(merged.Count==2&&merged[0].Kind=="exact"&&merged[1].Id==oldKeyword.Id&&merged[1].Kind=="ip"&&merged[1].Value=="direct","migrate old targets and keep an explicit IP intent over the conflicting old row rule");
      foreach(var input in new[]{"192.0.2.30:8098","192.0.2.30/24","[2001:db8::2]:8098","::1","0.0.0.0","198.18.1.2"}){
        string kind="ip",pattern=input;
        Assert(!TargetRouting.NormalizeRule(ref kind,ref pattern),"IP rules reject ports, CIDRs and protected addresses");
      }
      return 0;
    }
    if(a.Length==1&&a[0]=="--check-live") {var update=AppUpdate.Check();Console.WriteLine(update==null?"PASS: live GitHub check; no eligible release":"PASS: live GitHub check; release "+update.Version);return 0;}
    if(a.Length!=2&&!(a.Length==3&&a[2]=="--system-proxy"))throw new ArgumentException("Usage: <cores directory> <fresh profile> [--system-proxy]");
    Directory.CreateDirectory(a[1]);
    var saved=Path.Combine(a[1],"subscriptions.dat");
    if(File.Exists(saved))throw new InvalidOperationException("A fresh test profile is required.");
    var upgrade=Path.Combine(a[1],"legacy-routes");
    Directory.CreateDirectory(upgrade);
    LocalData.Write(Path.Combine(upgrade,"subscriptions.dat"),LocalData.Json.Serialize(new ClientModel{
      Sources=new List<SourceRecord>(),TargetRoutes=new Dictionary<string,string>{{"192.0.2.30","proxy"},{"old.invalid","direct"}},
      DomainRoutes=new List<DomainRoute>{new DomainRoute{Id=Guid.NewGuid().ToString("N"),Kind="keyword",Pattern="192.0.2.30",Value="direct"}}
    }));
    using(var oldProfile=new ClientService(upgrade,false,a[0])){
      var migrated=LocalData.Rows(Call(oldProfile,"snapshot",Args()),"domainRoutes").ToArray();
      var ipRule=migrated.Single(r=>LocalData.Text(r,"kind")=="ip");
      Assert(migrated.Length==2&&LocalData.Text(ipRule,"value")=="direct","encrypted old profile opens with one effective rule for the conflicting IP");
      Call(oldProfile,"domain-route",Args("operation","delete","id",LocalData.Text(ipRule,"id")));
    }
    using(var reopened=new ClientService(upgrade,false,a[0])){
      var remaining=LocalData.Rows(Call(reopened,"snapshot",Args()),"domainRoutes").Single();
      Assert(LocalData.Text(remaining,"pattern")=="old.invalid"&&LocalData.Text(remaining,"kind")=="exact","deleted migrated IP cannot reappear from the old override");
    }
    LocalData.Write(saved,"{\"Sources\":[],\"Cores\":{\"Mihomo\":{\"Path\":\"C:\\nonexistent.exe\",\"Version\":\"v0.0.0\"}}}");
    foreach(var core in CoreAsset.Catalog){
      string path=Path.Combine(a[0],core.Id,core.Exe);
      Assert(MihomoSession.Inspect(path,core)==core.Version,"bundled pin and x64 runtime: "+core.Id);
    }
    var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;
    var alternate=new TcpListener(IPAddress.Loopback,0);alternate.Start();int alternatePort=((IPEndPoint)alternate.LocalEndpoint).Port;
    var proxyReservation=new TcpListener(IPAddress.Loopback,0);proxyReservation.Start();
    int fixedPort=((IPEndPoint)proxyReservation.LocalEndpoint).Port;proxyReservation.Stop();
    int seen=0;var release=new ManualResetEventSlim(false);
    Func<TcpListener,string,Task> serve=(server,text)=>Task.Run(()=>{try{while(true){var client=server.AcceptTcpClient();Task.Run(()=>{try{using(client)using(var stream=client.GetStream())using(var reader=new StreamReader(stream)){
        if(!IPAddress.IsLoopback(((IPEndPoint)client.Client.LocalEndPoint).Address)&&!((IPEndPoint)client.Client.LocalEndPoint).Address.Equals(((IPEndPoint)client.Client.RemoteEndPoint).Address))return;
        string line=reader.ReadLine(); if(line==null)return; string header; do{header=reader.ReadLine();}while(!String.IsNullOrEmpty(header));
        if(line.StartsWith("CONNECT ")){
          var tunnel=System.Text.Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n");stream.Write(tunnel,0,tunnel.Length);
          line=reader.ReadLine();if(line==null)return;do{header=reader.ReadLine();}while(!String.IsNullOrEmpty(header));
        } else if(!line.Contains("example.invalid")&&!line.Contains("/hold")&&!line.Contains("/route-test"))return;
        if(line.Contains("/hold")){var ready=System.Text.Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 10000\r\n\r\nready");stream.Write(ready,0,ready.Length);release.Wait(4500);return;}
        Thread.Sleep(25);
        Interlocked.Increment(ref seen);
        var bytes=System.Text.Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 5\r\nConnection: close\r\n\r\n"+text);stream.Write(bytes,0,bytes.Length);
      }}catch(IOException){} });}}catch(SocketException){} });
    var accept=serve(listener,"hello");var acceptAlternate=serve(alternate,"other");
    try{
      using(var service=new ClientService(a[1],false,a[0])){
        Assert(service.CloseToTray,"old profile without close preference defaults to tray");
        Assert(LocalData.Number(Call(service,"snapshot",Args()),"localProxyPort")==7897,"old profile defaults to a fixed local proxy port");
        Call(service,"close-behavior",Args("value","exit"));
        string yaml="proxies:\n  - {name: Test, type: http, server: 127.0.0.1, port: "+port+"}\n  - {name: Alternate, type: http, server: 127.0.0.1, port: "+alternatePort+"}\n";
        var s=Call(service,"import",Args("input",yaml,"origin","file"));
        var id=LocalData.Text(LocalData.Rows(s,"sources").Single(),"id");
        Assert(LocalData.Text(s,"source")==id&&LocalData.Text(s,"node")=="FP-001"&&!Convert.ToBoolean(s["online"])&&LocalData.Rows(s,"nodes").Count()==2,"first import selects subscription and first node without starting");
        using(var savedService=new ClientService(a[1],false,a[0]))Assert(LocalData.Text(Call(savedService,"snapshot",Args()),"source")==id&&!savedService.CloseToTray,"first selection and explicit exit are persisted");
        s=Call(service,"import",Args("input",yaml,"origin","file"));
        Assert(LocalData.Text(s,"source")==id,"later imports do not steal selection");
        string otherId=LocalData.Text(LocalData.Rows(s,"sources").Last(),"id");
        s=Call(service,"select-source",Args("id",otherId));
        Assert(LocalData.Text(s,"source")==otherId&&LocalData.Text(s,"node")=="FP-001"&&!Convert.ToBoolean(s["online"]),"switching subscriptions selects a node without starting");
        s=Call(service,"select-source",Args("id",id));
        Assert(!LocalData.Rows(s,"domainRoutes").Any(),"legacy profiles open without domain rules");
        s=Call(service,"domain-route",Args("operation","save","kind","keyword","pattern","INVALID","value","proxy"));
        var keywordId=LocalData.Text(LocalData.Rows(s,"domainRoutes").Single(),"id");
        using(var reopened=new ClientService(a[1],false,a[0]))
          Assert(LocalData.Text(LocalData.Rows(Call(reopened,"snapshot",Args()),"domainRoutes").Single(),"pattern")=="invalid","offline domain rules survive reopening");
        string address="http://example.invalid/latency";
        s=Call(service,"delay-url",Args("value",address));Assert(LocalData.Text(s,"delayUrl")==address,"latency URL is saved");
        bool rejected=false;
        try{Call(service,"delay-url",Args("value","file:///bad"));}catch(InvalidOperationException){rejected=true;}
        Assert(rejected,"non-HTTP latency URL rejected");
        var exported=(string)service.Run("source-yaml",Args("id",id));
        Assert(Subscription.ParseEditedYaml(exported).Nodes.Select(n=>n.Name).SequenceEqual(new[]{"Test","Alternate"}),"export keeps original names and round trips");
        s=Call(service,"source-yaml-save",Args("id",id,"body",exported.Replace("Alternate","Edited")));
        var copy=LocalData.Rows(s,"sources").Last();
        Assert(LocalData.Text(s,"source")==id&&!Convert.ToBoolean(copy["refreshable"]),"YAML edits create a local copy without changing the original");
        string copyId=LocalData.Text(copy,"id");
        s=Call(service,"source-override-save",Args("id",copyId,"body","{\"prefix\":\"Local \",\"exclude\":[\"Test\"]}"));
        Assert(LocalData.Number(LocalData.Rows(s,"sources").Last(),"count")==1&&Subscription.Parse((string)service.Run("source-yaml",Args("id",copyId))).Nodes.Single().Name=="Local Edited","local override changes only the copy's effective nodes");
        foreach(var invalid in new[]{"{\"script\":\"x\"}","{\"exclude\":[\"Test\",\"Edited\"]}"}){
          rejected=false;try{Call(service,"source-override-save",Args("id",copyId,"body",invalid));}catch(InvalidOperationException){rejected=true;}
          Assert(rejected,"unsupported or empty-node override is rejected");
        }
        using(var savedService=new ClientService(a[1],false,a[0])){
          Assert(LocalData.Text(Call(savedService,"snapshot",Args()),"delayUrl")==address,"saved latency URL survives reopening");
          Assert(Subscription.Parse((string)savedService.Run("source-yaml",Args("id",copyId))).Nodes.Single().Name=="Local Edited","valid override persisted");
        }
        Call(service,"remove",Args("id",copyId));
        Assert(!Convert.ToBoolean(Call(service,"select-node",Args("id","FP-001"))["online"]),"offline selection does not start proxy");
        Assert(LocalData.Rows(s,"cores").All(c=>LocalData.Text(c,"version")==CoreAsset.Pick(LocalData.Text(c,"id")).Version),"legacy core overrides ignored; all bundled versions ready");
        foreach(var invalidPort in new[]{"","0","65536","-1","1.5","abc"}){
          rejected=false;try{Call(service,"local-proxy-port",Args("value",invalidPort));}
          catch(InvalidOperationException e){rejected=e.Message.Contains("1–65535");}
          Assert(rejected&&LocalData.Number(Call(service,"snapshot",Args()),"localProxyPort")==7897,"invalid proxy port rejected: "+invalidPort);
        }
        Call(service,"local-proxy-port",Args("value",port.ToString()));
        rejected=false;
        try{Call(service,"start",Args("mode","local"));}
        catch(InvalidOperationException e){rejected=e.Message.Contains(port.ToString())&&e.Message.Contains("占用");}
        s=Call(service,"snapshot",Args());
        Assert(rejected&&!Convert.ToBoolean(s["online"])&&LocalData.Number(s,"port")==0&&LocalData.Number(s,"localProxyPort")==port,"busy port fails explicitly without fallback");
        s=Call(service,"local-proxy-port",Args("value",fixedPort.ToString()));
        Assert(LocalData.Number(s,"localProxyPort")==fixedPort,"new fixed port saved offline");
        try {s=Call(service,"start",Args("mode","local"));}catch(Exception e){Console.Error.WriteLine(e);throw;}
        Assert(Convert.ToBoolean(s["online"])&&LocalData.Number(s,"port")==fixedPort,"real Mihomo starts on the configured port");
        rejected=false;try{Call(service,"local-proxy-port",Args("value",alternatePort.ToString()));}catch(InvalidOperationException e){rejected=e.Message.Contains("请先停止代理");}
        Assert(rejected&&LocalData.Number(Call(service,"snapshot",Args()),"localProxyPort")==fixedPort,"online port edits are rejected");
        Assert(service.LocalProxyAddress()=="127.0.0.1:"+LocalData.Number(s,"port"),"copy target matches live listening port");
        int status;
        var response=Subscription.Request(new Uri("http://example.invalid/ping"),new WebProxy("http://127.0.0.1:"+LocalData.Number(s,"port")),30,out status,false);
        Assert(status==200&&response=="hello"&&seen==1,"actual request through core and fixture");
        var checks=service.Run("check",new Dictionary<string,object>{{"urls",new object[]{"http://example.invalid/diagnostic"}}});
        Assert(LocalData.Json.Serialize(checks).Contains("\"status\":200")&&seen==2,"routed diagnostic");
        var progress=new System.Collections.Concurrent.ConcurrentQueue<string>();
        var delaySnapshot=service.Run("delay",Args(),msg=>progress.Enqueue(LocalData.Json.Serialize(msg)));
        s=LocalData.Parse(LocalData.Json.Serialize(delaySnapshot));
        var packets=progress.Select(LocalData.Parse).ToArray();
        var delay=LocalData.Number(LocalData.Rows(s,"nodes").First(),"delay");
        Assert(delay>0,"core delay");
        Assert(packets.Length==3&&(packets[0]["ids"] as object[]).SequenceEqual(new[]{"FP-001","FP-002"})&&packets.Skip(1).Any(p=>LocalData.Text(p,"id")=="FP-001"&&LocalData.Number(p,"delay")==delay),"real batch and per-node progress before final snapshot");
        s=Call(service,"snapshot",Args());Assert(LocalData.Number(LocalData.Obj(s,"traffic"),"down")>0,"controller traffic counters");
        using(var direct=Hold((int)LocalData.Number(s,"port"),"127.0.0.1:"+port))
        using(var routed=Hold((int)LocalData.Number(s,"port"),"example.invalid")){
          var connections=LocalData.Obj(Call(service,"snapshot",Args()),"connections");
          Assert(LocalData.Number(connections,"direct")>=1&&LocalData.Number(connections,"routed")>=1,"live connection rows distinguish direct and routed requests");
          var keep=LocalData.Rows(connections,"rows").First(r=>LocalData.Text(r,"route")=="直连");
          var drop=LocalData.Rows(connections,"rows").First(r=>LocalData.Text(r,"route")=="规则代理");
          string dropId=LocalData.Text(drop,"id"),keepId=LocalData.Text(keep,"id");
          Assert(!keep.ContainsKey("target")&&!drop.ContainsKey("target"),"connection snapshots expose only read-only route data");
          Assert(LocalData.Text(drop,"rule")=="DomainKeyword","bundled Mihomo matches a persisted domain keyword");
          rejected=false;
          try{Call(service,"connection-route",Args("target","example.invalid","value","default"));}catch(InvalidOperationException){rejected=true;}
          Assert(rejected,"legacy per-connection route mutation is unavailable");
          Call(service,"connection-close",Args("id",dropId));
          bool closed=false;
          for(int i=0;i<25;i++){
            s=Call(service,"snapshot",Args());
            var keys=LocalData.Rows(LocalData.Obj(s,"connections"),"rows").Select(r=>LocalData.Text(r,"id")).ToArray();
            if(!keys.Contains(dropId)){closed=keys.Contains(keepId);break;}Thread.Sleep(40);
          }
          Assert(closed&&Convert.ToBoolean(s["online"]),"closing a live connection preserves the other connection and proxy session");
          Assert(LocalData.Text(LocalData.Rows(s,"domainRoutes").Single(),"id")==keywordId,"disconnect does not change saved routing rules");
          Call(service,"connection-close",Args("id",dropId));
          rejected=false;try{Call(service,"connection-close",Args("id","../configs"));}catch(InvalidOperationException){rejected=true;}
          Assert(rejected,"arbitrary connection paths are rejected");
        }
        release.Set();
        release.Reset();
        try{
          int proxyPort=(int)LocalData.Number(s,"port");
          var domainState=Call(service,"domain-route",Args("operation","save","kind","regex","pattern",@"(^|\.)example\.invalid$","value","proxy"));
          var regexId=LocalData.Text(LocalData.Rows(domainState,"domainRoutes").Last(),"id");
          Func<string,string> matchedRule=host=>{
            var prior=LocalData.Rows(LocalData.Obj(Call(service,"snapshot",Args()),"connections"),"rows")
              .Select(r=>LocalData.Text(r,"id")).ToArray();
            using(var held=Hold(proxyPort,host)){
              var rows=LocalData.Rows(LocalData.Obj(Call(service,"snapshot",Args()),"connections"),"rows");
              return LocalData.Text(rows.First(r=>LocalData.Text(r,"host").StartsWith(host+":",StringComparison.Ordinal)&&!prior.Contains(LocalData.Text(r,"id"))),"rule");
            }
          };
          Assert(matchedRule("example.invalid")=="DomainKeyword","first matching domain rule takes priority");
          Call(service,"domain-route",Args("operation","delete","id",keywordId));
          Assert(matchedRule("sub.example.invalid")=="DomainRegex"&&matchedRule("notexample.invalid")=="Match","bundled Mihomo applies regex to new connections and respects boundaries");
          var exact=Call(service,"domain-route",Args("operation","save","kind","exact","pattern","Example.Invalid.","value","proxy"));
          string exactId=LocalData.Text(LocalData.Rows(exact,"domainRoutes").Last(),"id");
          Assert(matchedRule("example.invalid")=="DomainRegex","the first match wins, even over a later exact rule");
          using(var locked=new FileStream(saved,FileMode.Open,FileAccess.Read,FileShare.Read)){
            bool failed=false;
            try{Call(service,"domain-route",Args("operation","save","id",regexId,"kind","regex","pattern","nomatch","value","direct"));}
            catch(InvalidOperationException){failed=true;}
            Assert(failed&&matchedRule("example.invalid")=="DomainRegex","failed domain rule save restores running configuration");
          }
          Call(service,"domain-route",Args("operation","delete","id",regexId));
          Assert(matchedRule("example.invalid")=="Domain","removing the first rule exposes the next match");
          Call(service,"domain-route",Args("operation","delete","id",exactId));
          Assert(matchedRule("example.invalid")=="Match","removing all matching rules restores fallback");
        }finally{release.Set();}
        int listening=(int)LocalData.Number(s,"port");
        s=Call(service,"select-node",Args("id","FP-002"));
        Assert(Convert.ToBoolean(s["online"])&&LocalData.Text(s,"mode")=="local"&&LocalData.Number(s,"port")==listening&&LocalData.Text(s,"node")=="FP-002","Mihomo live switch keeps mode and port");
        response=Subscription.Request(new Uri("http://example.invalid/switched"),new WebProxy("http://127.0.0.1:"+listening),30,out status,false);
        Assert(status==200&&response=="other","new requests really use the other Mihomo node");
        TcpListener local=null;
        foreach(var ip in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
          .Where(n=>n.OperationalStatus==System.Net.NetworkInformation.OperationalStatus.Up)
          .SelectMany(n=>n.GetIPProperties().UnicastAddresses).Select(x=>x.Address)
          .Where(ip=>ip.AddressFamily==AddressFamily.InterNetwork&&TargetRouting.Editable(ip.ToString()))){
          var candidate=new TcpListener(ip,0);
          try{candidate.Start();local=candidate;break;}catch(SocketException){candidate.Stop();}
        }
        if(local!=null) {
          var directServer=serve(local,"local");
          try{
            var target=((IPEndPoint)local.LocalEndpoint).Address.ToString();
            var uri=new Uri("http://"+target+":"+((IPEndPoint)local.LocalEndpoint).Port+"/route-test");
            var proxy=new WebProxy("http://127.0.0.1:"+listening);
            var initial=Subscription.Request(uri,proxy,30,out status,false);
            Assert(initial=="other","default traffic uses the currently selected proxy (HTTP "+status+", fixture="+(initial=="local"?"direct":initial=="hello"?"wrong upstream":"none")+")");
            var directState=Call(service,"domain-route",Args("operation","save","kind","keyword","pattern",target,"value","direct"));
            var directRule=LocalData.Rows(directState,"domainRoutes").Single();
            string ruleId=LocalData.Text(directRule,"id");
            Assert(LocalData.Text(directRule,"kind")=="ip","literal IP entered as keyword becomes an exact IP rule");
            Assert(Subscription.Request(uri,proxy,30,out status,false)=="local","saved IP rule really bypasses the proxy");
            using(var reopened=new ClientService(a[1],false,a[0]))
              Assert(LocalData.Text(LocalData.Rows(Call(reopened,"snapshot",Args()),"domainRoutes").Single(),"value")=="direct","IP rule survives profile reopen");
            rejected=false;
            try{Call(service,"domain-route",Args("operation","save","kind","ip","pattern",target,"value","proxy"));}catch(InvalidOperationException){rejected=true;}
            Assert(rejected,"duplicate exact IP is rejected rather than adding a contradictory rule");
            using(var locked=new FileStream(saved,FileMode.Open,FileAccess.Read,FileShare.Read)){
              bool failed=false;try{Call(service,"domain-route",Args("operation","save","id",ruleId,"kind","ip","pattern",target,"value","proxy"));}catch(InvalidOperationException){failed=true;}
              Assert(failed&&Subscription.Request(uri,proxy,30,out status,false)=="local","failed save restores the live direct route");
            }
            Call(service,"domain-route",Args("operation","save","id",ruleId,"kind","ip","pattern",target,"value","proxy"));
            Assert(Subscription.Request(uri,proxy,30,out status,false)=="other","saving proxy restores real upstream traversal without changing node or port");
            Call(service,"domain-route",Args("operation","delete","id",ruleId));
            Assert(!LocalData.Rows(Call(service,"snapshot",Args()),"domainRoutes").Any()&&Subscription.Request(uri,proxy,30,out status,false)=="other","removing the IP rule restores the default proxy");
            DomainDirect(a[0],a[1],(IPEndPoint)local.LocalEndpoint,alternatePort);
          }finally{local.Stop();directServer.Wait(1500);}
        }else Console.WriteLine("SKIP: no local non-loopback IPv4 address for real DIRECT fixture");
        // Block the atomic save without touching the existing profile. The core must
        // return to the last saved node, not leave the UI and routing out of sync.
        using(var locked=new FileStream(saved,FileMode.Open,FileAccess.Read,FileShare.Read)){
          bool failed=false;
          try{Call(service,"select-node",Args("id","FP-001"));}catch(InvalidOperationException){failed=true;}
          s=Call(service,"snapshot",Args());
          Assert(failed&&Convert.ToBoolean(s["online"])&&LocalData.Text(s,"node")=="FP-002","failed switch save rolls the live selector back");
          response=Subscription.Request(new Uri("http://example.invalid/rollback"),new WebProxy("http://127.0.0.1:"+listening),30,out status,false);
          Assert(response=="other","rollback restores actual routing");
        }
        Assert(!Convert.ToBoolean(Call(service,"stop",Args())["online"]),"stop");
        Assert(LocalData.Number(Call(service,"start",Args("mode","local")),"port")==fixedPort,"stop and start reuse the same port");
        Call(service,"stop",Args());
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
            int begin=input.IndexOf("},{",StringComparison.Ordinal)+2;
            var other=input.Substring(begin,input.Length-begin-2).Replace("Test","Alternate").Replace(":"+port,":"+alternatePort);
            s=Call(service,"import",Args("input",input.Substring(0,input.Length-2)+","+other+"]}","origin","file"));
            var next=LocalData.Text(LocalData.Rows(s,"sources").Last(),"id");
            Call(service,"select-source",Args("id",next));Call(service,"select-node",Args("id","FP-001"));
            if(i==2)Call(service,"preferred",Args("value","V2Fly"));
            s=Call(service,"start",Args("mode","local"));
            Assert(LocalData.Text(s,"engine")==engine,"automatic selected engine: "+engine);
            var prior=seen;
            response=Subscription.Request(new Uri("http://example.invalid/native"),new WebProxy("http://127.0.0.1:"+LocalData.Number(s,"port")),30,out status,false);
            Assert(status==200&&response=="hello"&&seen==prior+1,"real traversal: "+engine);
            s=Call(service,"delay",Args("url","http://example.invalid/latency"));
            Assert(LocalData.Number(LocalData.Rows(s,"nodes").First(),"delay")>0,"measured native node: "+engine);
            listening=(int)LocalData.Number(s,"port");
            s=Call(service,"select-node",Args("id","FP-002"));
            Assert(Convert.ToBoolean(s["online"])&&LocalData.Text(s,"mode")=="local"&&LocalData.Number(s,"port")==listening&&LocalData.Text(s,"engine")==engine,"switch keeps native engine and stable port: "+engine);
            response=Subscription.Request(new Uri("http://example.invalid/switched"),new WebProxy("http://127.0.0.1:"+listening),30,out status,false);
            Assert(status==200&&response=="other","new requests really traverse the second node: "+engine);
            Call(service,"select-node",Args("id","FP-001"));
            Call(service,"stop",Args());
          }
        }
      }
      using(var service=new ClientService(a[1],false,a[0])){
        var s=Call(service,"snapshot",Args());
        Assert(LocalData.Text(s,"node")=="FP-001","DPAPI persistence");
        Assert(LocalData.Number(s,"localProxyPort")==fixedPort&&LocalData.Number(Call(service,"start",Args("mode","local")),"port")==fixedPort,"reopening profile restores the live listening port");
      }
      release.Reset();
      RefreshRegression(a[0],Path.Combine(a[1],"refresh-regression"),fixedPort,port,alternatePort);
    }finally{release.Set();listener.Stop();alternate.Stop();Task.WaitAll(new[]{accept,acceptAlternate},2000);}
    return 0;
  }
}
