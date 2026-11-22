using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

internal sealed class ProxySettings
{
    public int Flags { get; set; }
    public string Server { get; set; }
    public string Bypass { get; set; }
    public string Pac { get; set; }
    internal bool EqualsConfig(ProxySettings other) { return Flags==other.Flags && Server==other.Server && Bypass==other.Bypass && Pac==other.Pac; }
}
internal sealed class ProxyLease
{
    public ProxySettings Previous { get; set; }
    public ProxySettings Applied { get; set; }
}
internal sealed class WindowsProxy
{
    [StructLayout(LayoutKind.Sequential)] private struct Option { public int Key; public IntPtr Value; }
    [StructLayout(LayoutKind.Sequential)] private struct List { public int Size; public IntPtr Connection; public int Count, Error; public IntPtr Options; }
    [DllImport("wininet.dll", EntryPoint="InternetQueryOptionW", SetLastError=true)] private static extern bool Query(IntPtr h, int key, ref List list, ref int size);
    [DllImport("wininet.dll", EntryPoint="InternetSetOptionW", SetLastError=true)] private static extern bool Set(IntPtr h, int key, ref List list, int size);
    [DllImport("wininet.dll", EntryPoint="InternetSetOptionW", SetLastError=true)] private static extern bool Notify(IntPtr h, int key, IntPtr buffer, int size);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr pointer);
    private const int PerConnection=75;
    private readonly string journal;
    internal WindowsProxy(string root) {journal=Path.Combine(root,"system-proxy.lease");}

    internal void Recover() {
        if(!File.Exists(journal)) return;
        ProxyLease lease=LocalData.Json.Deserialize<ProxyLease>(LocalData.Read(journal));
        if(lease==null || lease.Applied==null || lease.Previous==null) throw new InvalidOperationException("无法恢复之前的系统代理设置。");
        var current=Get();
        if(current.EqualsConfig(lease.Applied)) Apply(lease.Previous);
        else if(current.Server==lease.Applied.Server && (current.Flags&2)!=0)
            throw new InvalidOperationException("系统代理被其他应用修改，未覆盖现有设置。请在 Windows 系统代理设置里检查残留的本机代理。");
        File.Delete(journal);
    }
    internal void Enable(string endpoint) {
        Recover();
        var lease=new ProxyLease { Previous=Get(), Applied=new ProxySettings {Flags=3, Server=endpoint, Bypass="<local>", Pac=""} };
        LocalData.Write(journal,LocalData.Json.Serialize(lease));
        try {
            Apply(lease.Applied);
            if(!Get().EqualsConfig(lease.Applied)) throw new InvalidOperationException("系统策略不允许使用新代理，未启动系统代理。");
        } catch {
            var current=Get();
            if(current.Server==lease.Applied.Server) Apply(lease.Previous);
            File.Delete(journal); throw;
        }
    }
    internal static ProxySettings Get() {
        try {return Get(10);} catch(Win32Exception) {return Get(1);}
    }
    private static ProxySettings Get(int flags) {
        var keys=new[]{flags,2,3,4}; int bytes=Marshal.SizeOf(typeof(Option));
        var buffer=Marshal.AllocHGlobal(bytes*keys.Length);
        try {
            for(int i=0;i<keys.Length;i++) Marshal.StructureToPtr(new Option{Key=keys[i]},IntPtr.Add(buffer,bytes*i),false);
            var list=new List{Size=Marshal.SizeOf(typeof(List)),Count=keys.Length,Options=buffer};int size=list.Size;
            if(!Query(IntPtr.Zero,PerConnection,ref list,ref size)) throw new Win32Exception(Marshal.GetLastWin32Error());
            var fields=new List<string>();
            int mask=((Option)Marshal.PtrToStructure(buffer,typeof(Option))).Value.ToInt32();
            for(int i=1;i<keys.Length;i++) {
                var value=((Option)Marshal.PtrToStructure(IntPtr.Add(buffer,bytes*i),typeof(Option))).Value;
                fields.Add(value==IntPtr.Zero?"":Marshal.PtrToStringUni(value));
            }
            return new ProxySettings{Flags=mask,Server=fields[0],Bypass=fields[1],Pac=fields[2]};
        } finally {
            for(int i=1;i<keys.Length;i++) {var value=((Option)Marshal.PtrToStructure(IntPtr.Add(buffer,bytes*i),typeof(Option))).Value;if(value!=IntPtr.Zero)GlobalFree(value);}
            Marshal.FreeHGlobal(buffer);
        }
    }
    private static void Apply(ProxySettings profile) {
        int bytes=Marshal.SizeOf(typeof(Option)); var buffer=Marshal.AllocHGlobal(bytes*4);
        var values=new[]{new IntPtr(profile.Flags),Marshal.StringToHGlobalUni(profile.Server),Marshal.StringToHGlobalUni(profile.Bypass),Marshal.StringToHGlobalUni(profile.Pac)};
        try {
            for(int i=0;i<values.Length;i++) Marshal.StructureToPtr(new Option{Key=i+1,Value=values[i]},IntPtr.Add(buffer,bytes*i),false);
            var list=new List{Size=Marshal.SizeOf(typeof(List)),Count=values.Length,Options=buffer};
            if(!Set(IntPtr.Zero,PerConnection,ref list,list.Size)) throw new Win32Exception(Marshal.GetLastWin32Error());
            Notify(IntPtr.Zero,39,IntPtr.Zero,0);Notify(IntPtr.Zero,37,IntPtr.Zero,0);
        } finally {
            for(int i=1;i<values.Length;i++)Marshal.FreeHGlobal(values[i]);
            Marshal.FreeHGlobal(buffer);
        }
    }
}
