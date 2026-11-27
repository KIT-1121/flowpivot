using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

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
    internal static byte[] Encrypt(string json) {return ProtectedData.Protect(Encoding.UTF8.GetBytes(json),null,DataProtectionScope.CurrentUser);}
    internal static string Decrypt(byte[] bytes) {return new UTF8Encoding(false,true).GetString(ProtectedData.Unprotect(bytes,null,DataProtectionScope.CurrentUser));}
    internal static string Read(string path) { return Decrypt(File.ReadAllBytes(path)); }
}
