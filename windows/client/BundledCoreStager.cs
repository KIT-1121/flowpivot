using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;

internal static class BundledCoreStager {
    private static void Copy(ZipArchive zip,string name,string target){
        var entry=zip.Entries.SingleOrDefault(e=>e.Name==name);
        if(entry==null||entry.Length<=0||entry.Length>120*1024*1024)throw new InvalidDataException("Missing or invalid member: "+name);
        entry.ExtractToFile(target,false);
    }
    private static int Main(string[] args){
        try{
            foreach(var core in CoreAsset.Catalog){
                using(var input=File.OpenRead(Path.Combine(args[0],core.Archive)))using(var sha=SHA256.Create()){
                    var hash=BitConverter.ToString(sha.ComputeHash(input)).Replace("-","");
                    if(!String.Equals(hash,core.Hash,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException(core.Id+" ZIP SHA-256 mismatch.");
                    input.Position=0;
                    var folder=Directory.CreateDirectory(Path.Combine(args[1],core.Id)).FullName;
                    using(var zip=new ZipArchive(input,ZipArchiveMode.Read,true)){
                        Copy(zip,core.Exe,Path.Combine(folder,core.Exe));
                        if(core.Id=="sing-box")Copy(zip,"libcronet.dll",Path.Combine(folder,"libcronet.dll"));
                        if(core.Id=="Xray"||core.Id=="sing-box")Copy(zip,"LICENSE",Path.Combine(folder,"LICENSE.txt"));
                    }
                    if(core.Id=="Mihomo"||core.Id=="sing-box")File.Copy(Path.Combine(args[2],"Mihomo-LICENSE.txt"),Path.Combine(folder,"GPL-3.0.txt"));
                    if(core.Id=="V2Fly")File.Copy(Path.Combine(args[2],"V2Fly-LICENSE.txt"),Path.Combine(folder,"LICENSE.txt"));
                    Console.WriteLine("PASS: pinned official archive " + core.Repo + " " + core.Version);
                }
            }
            return 0;
        } catch(Exception e){Console.Error.WriteLine(e.Message);return 1;}
    }
}
