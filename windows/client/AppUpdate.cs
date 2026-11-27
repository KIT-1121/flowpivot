using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

internal static class AppUpdate {
    internal const string Version="0.2.0-alpha.27";
    private const string Repo="KIT-1121/flowpivot";
    internal sealed class Candidate {
        internal readonly string Version, Setup, Portable;
        internal Candidate(string version,string setup,string portable){Version=version;Setup=setup;Portable=portable;}
    }
    private static readonly Regex TagPattern=new Regex(@"^v?(0|[1-9][0-9]{0,3})\.(0|[1-9][0-9]{0,3})\.(0|[1-9][0-9]{0,3})(?:-(alpha|beta|rc)\.(0|[1-9][0-9]{0,4}))?\z",RegexOptions.CultureInvariant);
    private static long Rank(Match m){
        var stages=new[]{"alpha","beta","rc",""};
        return (((long)int.Parse(m.Groups[1].Value)*10000+int.Parse(m.Groups[2].Value))*10000+int.Parse(m.Groups[3].Value))*400000+
            Array.IndexOf(stages,m.Groups[4].Value)*100000+(m.Groups[5].Success?int.Parse(m.Groups[5].Value):0);
    }
    internal static Candidate Check(){
        try {
            int status;
            var json=Subscription.Request(new Uri("https://api.github.com/repos/"+Repo+"/releases?per_page=30"),WebRequest.GetSystemWebProxy(),2*1024*1024,out status,false,7000);
            if(status!=200)throw new InvalidOperationException("GitHub 返回 HTTP "+status+"。请稍后再试。");
            return Select(LocalData.Json.DeserializeObject(json) as object[],Version);
        } catch(WebException){throw new InvalidOperationException("连不上 GitHub，暂时无法检查更新。");}
    }
    internal static Candidate Select(object[] list,string current){
        var installed=TagPattern.Match(current);
        if(list==null||!installed.Success)throw new InvalidOperationException("无法读取软件更新信息。");
        Candidate best=null;long latest=Rank(installed);
        foreach(var release in list.OfType<IDictionary<string,object>>()){
            object draft,preview;
            if(!release.TryGetValue("draft",out draft)||!(draft is bool)||(bool)draft||!release.TryGetValue("prerelease",out preview)||!(preview is bool))continue;
            var tag=LocalData.Text(release,"tag_name");var match=TagPattern.Match(tag);
            if(!match.Success||(!installed.Groups[4].Success&&((bool)preview||match.Groups[4].Success)))continue;
            var rank=Rank(match);if(rank<=latest)continue;
            var normalized=tag.TrimStart('v');
            var setup=Asset(release,tag,"FlowPivot-"+normalized+"-windows-x64-setup.exe");
            var portable=Asset(release,tag,"FlowPivot-"+normalized+"-windows-x64-portable.zip");
            if(setup==null||portable==null)continue;
            best=new Candidate(normalized,setup,portable);latest=rank;
        }
        return best;
    }
    private static string Asset(IDictionary<string,object> release,string tag,string name){
        string url="https://github.com/"+Repo+"/releases/download/"+tag+"/"+name;
        return LocalData.Rows(release,"assets").Any(row=>LocalData.Text(row,"name")==name&&LocalData.Text(row,"state")=="uploaded"&&LocalData.Number(row,"size")>0&&LocalData.Text(row,"browser_download_url")==url)?url:null;
    }
}
