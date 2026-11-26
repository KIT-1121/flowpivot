using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

internal sealed class SubscriptionStoreException : InvalidOperationException {
    internal SubscriptionStoreException(Exception cause)
        : base(cause is CryptographicException
            ? "Windows 无法解密本地订阅库。原文件未覆盖。"
            : "本地订阅库无法读取或校验。原文件未覆盖。",cause) {}
}

internal sealed class SubscriptionStore {
    internal const string Restored="已从上一次保存的备份恢复订阅。无法读取的原文件已保留。";
    internal const string Reset="旧订阅库已保留，请重新导入订阅。";
    private readonly string path;
    private string Backup {get{return path+".bak";}}
    internal SubscriptionStore(string root){path=Path.Combine(root,"subscriptions.dat");}

    private static bool Expected(Exception error){
        return DiagnosticsLog.IsFileError(error)||error is CryptographicException||error is InvalidDataException||error is InvalidOperationException||error is FormatException;
    }
    private static bool Temporary(Exception error){
        return error is CryptographicException||error is IOException;
    }
    private string ArchivePath(){
        return path+".recovery-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss",CultureInfo.InvariantCulture)+"-"+Guid.NewGuid().ToString("N");
    }
    private static ClientModel Empty(){return new ClientModel{Sources=new List<SourceRecord>()};}
    private static byte[] ReadFile(string file){
        try{return File.ReadAllBytes(file);}catch(FileNotFoundException){return null;}
    }
    private static ClientModel Decode(byte[] bytes,Dictionary<string,ParsedSource> parsed){
        var model=LocalData.Json.Deserialize<ClientModel>(LocalData.Decrypt(bytes));
        if(model==null||model.Sources==null||model.Sources.Count>20)throw new InvalidDataException();
        var checkedSources=new Dictionary<string,ParsedSource>();
        foreach(var source in model.Sources){
            if(source==null||string.IsNullOrWhiteSpace(source.Id)||string.IsNullOrWhiteSpace(source.Name)||checkedSources.ContainsKey(source.Id))
                throw new InvalidDataException();
            checkedSources.Add(source.Id,Subscription.WithOverride(source.Body,source.Override));
        }
        foreach(var entry in checkedSources)parsed.Add(entry.Key,entry.Value);
        return model;
    }
    internal ClientModel Load(Dictionary<string,ParsedSource> parsed,bool retry,out bool restored){
        restored=false;
        Exception primaryFailure=null;
        // Give Windows a short time to make the user profile/DPAPI available at logon.
        for(int i=0;;i++){
            try{
                var bytes=ReadFile(path);
                if(bytes!=null)return Decode(bytes,parsed);
                break;
            }catch(Exception e){
                if(!Expected(e))throw;
                primaryFailure=e;
                if(!retry||!Temporary(e)||i>=3)break;
                Thread.Sleep(250*(i+1));
            }
        }
        try{
            var bytes=ReadFile(Backup);
            if(bytes!=null){
                var model=Decode(bytes,parsed);
                // Do not rotate the damaged primary into the known-good backup.
                Commit(bytes,ArchivePath());
                restored=true;
                return model;
            }
        }catch(Exception e){
            if(!Expected(e))throw;
            throw new SubscriptionStoreException(primaryFailure??e);
        }
        if(primaryFailure!=null)throw new SubscriptionStoreException(primaryFailure);
        return Empty();
    }
    internal void Write(ClientModel model){Commit(LocalData.Encrypt(LocalData.Json.Serialize(model)),Backup);}
    internal bool PreserveAndReset(){
        // Retry once before archiving: a now-readable primary or backup must not be reset.
        try{bool restored;Load(new Dictionary<string,ParsedSource>(),false,out restored);return false;}
        catch(SubscriptionStoreException){}
        var bytes=LocalData.Encrypt(LocalData.Json.Serialize(Empty()));
        Decode(bytes,new Dictionary<string,ParsedSource>());
        string archive=ArchivePath();
        try{File.Copy(Backup,archive+".bak");}catch(FileNotFoundException){}
        Commit(bytes,archive);
        return true;
    }
    private void Commit(byte[] bytes,string previous){
        string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{
            using(var writer=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){
                writer.Write(bytes,0,bytes.Length);
                writer.Flush(true);
            }
            if(File.Exists(path))File.Replace(temp,path,previous);else File.Move(temp,path);
        }finally{
            try{File.Delete(temp);}catch(Exception e){if(!DiagnosticsLog.IsFileError(e))throw;}
        }
    }
}
