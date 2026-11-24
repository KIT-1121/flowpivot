using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

internal sealed class DomainRoute {
    public string Id {get;set;}
    public string Kind {get;set;}
    public string Pattern {get;set;}
    public string Value {get;set;}
}
internal static class TargetRouting {
    internal const int Limit=160; // Preserve up to 128 legacy exact rules + 32 domain rules.
    internal const int RegexLimit=32;
    private static string MatchPattern(string pattern){
        // An unanchored literal already means "contains"; accept the familiar equivalent, too.
        var contains=Regex.Match(pattern,@"\A\^?\.\*([a-zA-Z0-9_-]+)\.\*\$?\z");
        return contains.Success?contains.Groups[1].Value:pattern;
    }
    internal static bool ValidPattern(string kind,string pattern) {
        if(string.IsNullOrEmpty(pattern)||pattern.Length>128)return false;
        if(kind=="keyword")return Regex.IsMatch(pattern,@"\A[a-zA-Z0-9._-]+\z");
        if(kind!="regex")return false;
        // The pinned engine has no match timeout. Allow one unbounded repetition,
        // no repeated groups, and bound the hostname length in the emitted rule.
        pattern=MatchPattern(pattern);
        bool inClass=false,group=false,atom=false;
        int groups=0, alternatives=0, optional=0, repeats=0;
        for(int i=0;i<pattern.Length;i++){
            char c=pattern[i];
            if(c=='\\'){
                if(++i==pattern.Length||"dDwW.-".IndexOf(pattern[i])<0)return false;
                atom=!inClass;continue;
            }
            if(!char.IsLetterOrDigit(c)&&"._-[]()?:^$|*+".IndexOf(c)<0||c>127)return false;
            if(inClass){
                if(c==']'){inClass=false;atom=true;}
                else if(c=='[')return false;
                continue;
            }
            if(c=='['){inClass=true;atom=false;continue;}
            if(c=='('){
                if(group||++groups>3)return false;
                group=true;atom=false;
                if(i+1<pattern.Length&&pattern[i+1]=='?'){
                    if(i+2>=pattern.Length||pattern[i+2]!=':')return false;
                    i+=2;
                }
                continue;
            }
            if(c==')'){if(!group)return false;group=false;atom=false;continue;}
            if(c=='|'){if(++alternatives>6)return false;atom=false;continue;}
            if(c=='*'||c=='+'||c=='?'){
                if(!atom||(c=='?'?++optional>2:++repeats>1))return false;
                atom=false;continue;
            }
            atom=c!='^'&&c!='$';
        }
        if(inClass||group)return false;
        try{new Regex(pattern);return true;}catch(ArgumentException){return false;}
    }
    private static string Key(string kind,string pattern) { return kind+":"+pattern; }
    internal static bool NormalizeRule(ref string kind,ref string pattern) {
        pattern=(pattern??"").Trim();
        if(kind=="keyword"){
            IPAddress candidate;
            if(IPAddress.TryParse(pattern,out candidate))kind="ip";
        }
        if(kind=="exact"||kind=="ip"){
            pattern=Target(pattern);
            IPAddress address;
            return Editable(pattern)&&(IPAddress.TryParse(pattern,out address)==(kind=="ip"));
        }
        if(!ValidPattern(kind,pattern))return false;
        if(kind=="keyword")pattern=pattern.ToLowerInvariant();
        return true;
    }
    internal static List<DomainRoute> LoadDomainRoutes(IEnumerable<DomainRoute> saved,IDictionary<string,string> legacy=null) {
        var seen=new HashSet<string>(StringComparer.Ordinal);
        var ids=new HashSet<Guid>();
        var current=new List<DomainRoute>();
        int regexCount=0;
        foreach(var rule in saved??Enumerable.Empty<DomainRoute>()){
            Guid id;
            if(rule==null||!Guid.TryParseExact(rule.Id,"N",out id)||ids.Contains(id)||!ValidRoute(rule.Value))continue;
            string kind=rule.Kind,pattern=rule.Pattern;
            if(!NormalizeRule(ref kind,ref pattern)||(kind=="regex"&&regexCount==RegexLimit)||!seen.Add(Key(kind,pattern)))continue;
            if(kind=="regex")regexCount++;
            ids.Add(id);
            current.Add(new DomainRoute{Id=id.ToString("N"),Kind=kind,Pattern=pattern,Value=rule.Value});
            if(current.Count==Limit)break;
        }
        var migrated=new List<DomainRoute>();
        if(legacy!=null)foreach(var old in legacy.OrderBy(r=>r.Key,StringComparer.Ordinal)){
            if(migrated.Count+current.Count>=Limit)break;
            if(!ValidRoute(old.Value))continue;
            string pattern=Target(old.Key);
            IPAddress ip;
            string kind=IPAddress.TryParse(pattern,out ip)?"ip":"exact";
            // If a saved keyword was a literal IP, keep that rule rather than
            // reviving a contradictory per-row override for the same address.
            if(!NormalizeRule(ref kind,ref pattern)||!seen.Add(Key(kind,pattern)))continue;
            migrated.Add(new DomainRoute{Id=Guid.NewGuid().ToString("N"),Kind=kind,Pattern=pattern,Value=old.Value});
        }
        return migrated.Concat(current).ToList();
    }
    internal static bool ValidRoute(string value) { return value=="direct"||value=="proxy"; }
    // This is an exact hostname or IP, never a URL, suffix rule, CIDR or raw YAML.
    internal static string Target(string raw) {
        if(string.IsNullOrEmpty(raw)||raw.Length>254||raw.Any(c=>char.IsControl(c)||char.IsWhiteSpace(c)))return "";
        IPAddress address;
        if(raw.IndexOf(':')>=0){
            if(raw.IndexOfAny(new[]{'%','[',']'})>=0||!IPAddress.TryParse(raw,out address)||address.AddressFamily!=AddressFamily.InterNetworkV6)return "";
            if(address.IsIPv4MappedToIPv6)address=address.MapToIPv4();
            return address.ToString();
        }
        if(IPAddress.TryParse(raw,out address))
            return address.AddressFamily==AddressFamily.InterNetwork&&address.ToString()==raw?raw:"";
        string dns;
        try{dns=new IdnMapping{UseStd3AsciiRules=true}.GetAscii(raw.TrimEnd('.')).ToLowerInvariant();}catch(ArgumentException){return "";}
        return dns.Length<=253&&dns.Split('.').All(label=>Regex.IsMatch(label,@"\A[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\z"))?dns:"";
    }
    internal static bool Editable(string normalized) {
        if(string.IsNullOrEmpty(normalized)||normalized=="localhost"||normalized.EndsWith(".localhost",StringComparison.Ordinal))return false;
        IPAddress address;
        if(!IPAddress.TryParse(normalized,out address))return true;
        if(IPAddress.IsLoopback(address)||address.Equals(IPAddress.Any)||address.Equals(IPAddress.IPv6Any))return false;
        var ip=address.GetAddressBytes();
        return !(ip.Length==4&&ip[0]==198&&ip[1]==18); // Do not persist this client's TUN fake-IP aliases.
    }
    internal static string Yaml(IEnumerable<DomainRoute> rules,string outbound) {
        const string local="rules:\n  - DOMAIN-SUFFIX,localhost,DIRECT\n  - IP-CIDR,127.0.0.0/8,DIRECT,no-resolve\n  - IP-CIDR6,::1/128,DIRECT,no-resolve\n";
        var clauses=new List<string>{local};
        foreach(var rule in LoadDomainRoutes(rules)){
            var route=rule.Value=="direct"?"DIRECT":outbound;
            string clause;
            if(rule.Kind=="ip"){
                var ip=IPAddress.Parse(rule.Pattern);
                bool ipv4=ip.AddressFamily==AddressFamily.InterNetwork;
                clauses.Add("  - "+(ipv4?"IP-CIDR,":"IP-CIDR6,")+rule.Pattern+(ipv4?"/32,":"/128,")+route+",no-resolve\n");
                continue;
            }
            if(rule.Kind=="exact")clause="DOMAIN,"+rule.Pattern;
            else if(rule.Kind=="keyword")clause="DOMAIN-KEYWORD,"+rule.Pattern;
            else clause=
                @"DOMAIN-REGEX,\A(?![a-z0-9._-]{255})(?=[a-z0-9._-]+\z).*?(?:"+MatchPattern(rule.Pattern)+")";
            clauses.Add("  - '"+clause+","+route+"'\n");
        }
        clauses.Add("  - MATCH,"+outbound+"\n");
        return string.Concat(clauses);
    }
}
