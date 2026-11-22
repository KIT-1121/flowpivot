using System;
using System.Linq;

internal sealed class CoreAsset {
    internal readonly string Id, Repo, Version, Archive, Hash, Exe, Pattern;
    private CoreAsset(string id,string repo,string version,string archive,string hash,string exe,string pattern){Id=id;Repo=repo;Version=version;Archive=archive;Hash=hash;Exe=exe;Pattern=pattern;}
    internal static readonly CoreAsset[] Catalog={
        new CoreAsset("Mihomo","MetaCubeX/mihomo","v1.19.32","mihomo-windows-amd64-compatible-v1.19.32.zip","974a4d7ad69aed27aa2e8f91d61113573c14dadb14562c63e58effabf59816f0","mihomo-windows-amd64-compatible.exe",@"Mihomo\s+(?:Meta\s+)?(v[\d.]+[-.\w]*)\s+windows\s+amd64"),
        new CoreAsset("Xray","XTLS/Xray-core","v26.10.31","Xray-windows-64.zip","2e04a9995a6f2ad1b28c0bb25c708ecb2695e43a1c9c7889c4b6ac2b48cf7a6a","xray.exe",@"Xray\s+([\d.]+[-.\w]*)"),
        new CoreAsset("sing-box","SagerNet/sing-box","v1.14.4","sing-box-1.14.4-windows-amd64.zip","99185d75d314a07dbba4a130c8ad986cbeb629a07927975636b341ca61f991df","sing-box.exe",@"sing-box version\s+([\d.]+[-.\w]*)"),
        new CoreAsset("V2Fly","v2fly/v2ray-core","v5.53.0","v2ray-windows-64.zip","46ee170d031ea3be79cc583e61e6335a612e4c87925deed2a7179b0a5fb79f60","v2ray.exe",@"V2Ray\s+([\d.]+[-.\w]*)")
    };
    internal static CoreAsset Pick(string id){var asset=Catalog.FirstOrDefault(c=>c.Id==id);if(asset==null)throw new InvalidOperationException("未识别核心类型。");return asset;}
}
