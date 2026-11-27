using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

internal sealed partial class ClientWindow {
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);

    private Icon CreateConnectedTrayIcon(){
        using(var bitmap=new Bitmap(32,32,System.Drawing.Imaging.PixelFormat.Format32bppArgb)){
            using(var image=new Icon(Icon,32,32))
            using(var g=Graphics.FromImage(bitmap))
            using(var status=new SolidBrush(Color.FromArgb(0,144,82)))
            using(var check=new Pen(Color.White,1.8f){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round}){
                g.SmoothingMode=SmoothingMode.AntiAlias;
                g.DrawIcon(image,new Rectangle(0,0,32,32));
                g.FillEllipse(Brushes.White,15,15,17,17);
                g.FillEllipse(status,17,17,13,13);
                g.DrawLines(check,new[]{new PointF(20,23),new PointF(22.4f,25.4f),new PointF(27,21)});
            }
            IntPtr handle=bitmap.GetHicon();
            try {using(var temporary=Icon.FromHandle(handle))return (Icon)temporary.Clone();}
            finally {DestroyIcon(handle);}
        }
    }

    private async Task TestTrayIndicator(CoreWebView2 core){
        int port;
        var probe=new TcpListener(IPAddress.Loopback,0);
        probe.Start();
        try{port=((IPEndPoint)probe.LocalEndpoint).Port;}finally{probe.Stop();}
        await core.ExecuteScriptAsync("window.trayTest=null;void nativeCall('local-proxy-port',{value:'"+port+
            "'}).then(()=>nativeCall('start',{mode:'local'})).then(s=>window.trayTest=s.online,()=>window.trayTest=false);");
        for(int i=0;i<90&&await core.ExecuteScriptAsync("window.trayTest===null")=="true";i++)await Task.Delay(100);
        if(await core.ExecuteScriptAsync("window.trayTest===true")!="true"||!trayConnected||tray==null||!tray.Visible||
            tray.Icon!=connectedTrayIcon||!tray.Text.Contains("已连接"))
            throw new InvalidOperationException("A real local connection did not show the connected tray icon.");
        using(var bitmap=tray.Icon.ToBitmap()){
            var p=bitmap.GetPixel(23,19);
            if(p.G<=p.R||p.G<=p.B)throw new InvalidOperationException("Tray badge is not green.");
        }
        HideToTray();RestoreWindow();
        if(!tray.Visible||tray.Icon!=connectedTrayIcon)throw new InvalidOperationException("Restoring the window lost the connected tray icon.");
        await core.ExecuteScriptAsync("window.trayTest=null;void nativeCall('stop').then(s=>window.trayTest=!s.online,()=>window.trayTest=false);");
        for(int i=0;i<60&&await core.ExecuteScriptAsync("window.trayTest===null")=="true";i++)await Task.Delay(100);
        if(await core.ExecuteScriptAsync("window.trayTest===true")!="true"||trayConnected||tray.Icon!=Icon||tray.Visible)
            throw new InvalidOperationException("Stopping the proxy did not restore the original tray icon.");
    }
}
