using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

internal sealed partial class ClientWindow {
    private Panel caption;
    private Button minimizeCaption,maximizeCaption,closeCaption;
    private string captionTheme;
    private bool englishCaption;
    private string CaptionName {get{return englishCaption?"FlowPivot":"流枢";}}
    private void SetLanguage(string language){
        englishCaption=language=="en";
        Text=CaptionName;
        if(caption!=null){
            caption.AccessibleName=englishCaption?"FlowPivot title bar":"流枢标题栏";
            closeCaption.AccessibleName=englishCaption?"Close window":"关闭窗口";
            minimizeCaption.AccessibleName=englishCaption?"Minimize":"最小化";
            UpdateMaximizeLabel();
            caption.Invalidate();
        }
        if(tray!=null){tray.Text=TrayCaption;trayMenu.Items[0].Text=englishCaption?"Open FlowPivot":"打开流枢";trayMenu.Items[1].Text=englishCaption?"Quit":"退出";}
    }
    private void UpdateMaximizeLabel(){
        bool max=WindowState==FormWindowState.Maximized;
        maximizeCaption.Text=max?"❐":"□";
        maximizeCaption.AccessibleName=englishCaption?(max?"Restore":"Maximize"):(max?"还原":"最大化");
    }
    private static bool SystemCaption { get { return !Application.RenderWithVisualStyles||SystemInformation.HighContrast; } }
    protected override CreateParams CreateParams {
        get {
            var styles=base.CreateParams;
            // Keep the resizable Win32 frame, system menu and window state styles.
            if(!SystemCaption)styles.Style&=~0x00C00000; // WS_CAPTION
            return styles;
        }
    }
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] private struct WindowRect {public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd,out WindowRect rect);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hwnd);
    protected override void WndProc(ref Message m){
        const int ncCalcSize=0x0083, ncHitTest=0x0084, htClient=1, htTop=12;
        if(!SystemCaption&&m.Msg==ncCalcSize&&m.LParam!=IntPtr.Zero&&!IsZoomed(m.HWnd)){
            // Keep the native sides, bottom and system menu. Let the themed
            // client paint the old top-frame strip, except for the 1px rim.
            int top=Marshal.ReadInt32(m.LParam,4);
            base.WndProc(ref m);
            Marshal.WriteInt32(m.LParam,4,Math.Min(Marshal.ReadInt32(m.LParam,4),top+1));
            m.Result=IntPtr.Zero;
            return;
        }
        base.WndProc(ref m);
        if(!SystemCaption&&m.Msg==ncHitTest&&m.Result.ToInt32()==htClient&&!IsZoomed(m.HWnd)){
            WindowRect r;
            int y=unchecked((short)(m.LParam.ToInt64()>>16));
            if(GetWindowRect(m.HWnd,out r)&&y>=r.Top&&y<r.Top+CaptionUnit(5))
                m.Result=new IntPtr(htTop);
        }
    }
    private void ShowWindowMenu(){SendMessage(Handle,0x0112,new IntPtr(0xF100),new IntPtr(32));} // SC_KEYMENU (Alt+Space)
    private void ToggleMaximize(){WindowState=WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized;}
    private int CaptionUnit(int px){return Math.Max(1,(px*DeviceDpi+48)/96);}
    private void AddCaption(){
        if(SystemCaption)return;
        caption=new Panel{Dock=DockStyle.Top,Height=34,AccessibleName="流枢标题栏"};
        var titleFace=new Font("Microsoft YaHei UI",9f,FontStyle.Bold);
        var wordmarkFace=new Font("Segoe UI Semibold",9f);
        caption.Disposed+=(o,e)=>{titleFace.Dispose();wordmarkFace.Dispose();};
        caption.Paint+=(o,e)=>{
            int iconSize=CaptionUnit(19), titleLeft=CaptionUnit(39);
            if(Icon!=null)e.Graphics.DrawIcon(Icon,new Rectangle(CaptionUnit(11),(caption.Height-iconSize)/2,iconSize,iconSize));
            const TextFormatFlags flags=TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix;
            int available=caption.ClientSize.Width-titleLeft-minimizeCaption.Width-maximizeCaption.Width-closeCaption.Width-CaptionUnit(12);
            if(available>0)TextRenderer.DrawText(e.Graphics,CaptionName,englishCaption?wordmarkFace:titleFace,
                new Rectangle(titleLeft,0,available,caption.Height),caption.ForeColor,flags|TextFormatFlags.EndEllipsis);
        };
        caption.MouseDown+=(o,e)=>{
            if(e.Button==MouseButtons.Right){ShowWindowMenu();return;}
            if(e.Button!=MouseButtons.Left)return;
            if(e.X<30){ShowWindowMenu();return;}
            if(e.Clicks>1){ToggleMaximize();return;}
            ReleaseCapture();
            var point=caption.PointToScreen(e.Location);
            SendMessage(Handle,0x00A1,new IntPtr(2),new IntPtr((point.X&0xffff)|((point.Y&0xffff)<<16))); // HTCAPTION (native drag)
        };
        closeCaption=AddCaptionButton("关闭窗口","×",CloseFromCaption);
        maximizeCaption=AddCaptionButton("最大化","□",ToggleMaximize);
        minimizeCaption=AddCaptionButton("最小化","−",MinimizeWindow);
        Resize+=(o,e)=>UpdateMaximizeLabel();
        Controls.Add(caption);
        caption.SendToBack();
        ApplyCaptionTheme("paper");
    }
    private void MinimizeWindow(){
        // WinForms restores the active child with the window. Do not leave the
        // minimize button as that child, but keep it reachable by keyboard.
        if(minimizeCaption.Focused||ActiveControl==minimizeCaption)ActiveControl=view;
        WindowState=FormWindowState.Minimized;
    }
    private void CloseFromCaption(){
        // Restore focus to the content rather than to the window button when
        // Close hides the form and the user later reopens it from the tray.
        if(closeCaption.Focused||ActiveControl==closeCaption)ActiveControl=view;
        Close();
    }
    private sealed class CaptionButton : Button {
        protected override void OnPaint(PaintEventArgs e){
            // Draw without the WinForms flat-button focus/default frames.
            // Keep Button's keyboard, accessibility, and click handling.
            bool hover=ClientRectangle.Contains(PointToClient(MousePosition));
            bool pressed=hover&&Capture&&(MouseButtons&MouseButtons.Left)!=0;
            var fill=pressed?FlatAppearance.MouseDownBackColor:hover?FlatAppearance.MouseOverBackColor:BackColor;
            e.Graphics.Clear(fill.IsEmpty?BackColor:fill);
            if(Text=="−"){
                int width=Math.Max(12,(14*DeviceDpi+48)/96), height=Math.Max(2,(2*DeviceDpi+48)/96);
                using(var stroke=new SolidBrush(ForeColor))e.Graphics.FillRectangle(stroke,(Width-width)/2,(Height-height)/2,width,height);
            }else TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,ForeColor,
                TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine);
            if(Focused&&ShowFocusCues){
                int half=Math.Max(4,Width/10), y=Height*3/4;
                using(var pen=new Pen(ForeColor,Math.Max(1f,DeviceDpi/96f)))e.Graphics.DrawLine(pen,Width/2-half,y,Width/2+half,y);
            }
        }
    }
    private Button AddCaptionButton(string label,string glyph,Action action){
        var button=new CaptionButton{Text=glyph,AccessibleName=label,Dock=DockStyle.Right,Width=44,FlatStyle=FlatStyle.Flat,UseVisualStyleBackColor=false};
        button.FlatAppearance.BorderSize=0;
        button.Click+=(o,e)=>action();
        caption.Controls.Add(button);button.BringToFront();
        return button;
    }
    private void ApplyCaptionTheme(string theme){
        Color bg, hover;
        bool dark=theme=="dark"||theme=="ink";
        switch(theme){
            case "glass": bg=Color.FromArgb(241,243,247);hover=Color.FromArgb(222,228,237);break;
            case "light": bg=Color.FromArgb(243,245,245);hover=Color.FromArgb(220,233,231);break;
            case "dark": bg=Color.FromArgb(17,26,28);hover=Color.FromArgb(41,59,62);break;
            case "mist": bg=Color.FromArgb(225,232,237);hover=Color.FromArgb(205,219,229);break;
            case "paper": bg=Color.FromArgb(233,226,215);hover=Color.FromArgb(221,209,192);break;
            case "ink": bg=Color.FromArgb(23,29,39);hover=Color.FromArgb(43,56,74);break;
            default: throw new InvalidOperationException("不支持该主题。");
        }
        captionTheme=theme;
        var text=dark?Color.FromArgb(232,236,243):theme=="paper"?Color.FromArgb(59,49,39):Color.FromArgb(21,44,78);
        if(caption!=null){
            caption.BackColor=bg;caption.ForeColor=text;
            foreach(var button in new[]{minimizeCaption,maximizeCaption,closeCaption}){
                button.BackColor=bg;button.ForeColor=text;
                button.FlatAppearance.MouseOverBackColor=hover;
                button.FlatAppearance.MouseDownBackColor=ControlPaint.Dark(button.FlatAppearance.MouseOverBackColor,.12f);
            }
        }
        if(!SystemInformation.HighContrast){BackColor=bg;view.DefaultBackgroundColor=bg;}
    }
    private async Task TestCaption(CoreWebView2 core){
        if(captionTheme!="paper"||await core.ExecuteScriptAsync("document.documentElement.dataset.theme")!="\"paper\"")
            throw new InvalidOperationException("Fresh profiles should default to paper in WebView and the caption.");
        await core.ExecuteScriptAsync("document.querySelector('[data-route=settings]').click();");
        foreach(var option in new[]{"en","zh-CN"}){
            string expected=option=="en"?"FlowPivot":"流枢";
            await core.ExecuteScriptAsync("(()=>{const s=document.querySelector('#language');s.value='"+option+"';s.dispatchEvent(new Event('change',{bubbles:true}));})()");
            for(int i=0;i<30&&Text!=expected;i++)await Task.Delay(100);
            if(Text!=expected||CaptionName!=expected||await core.ExecuteScriptAsync("document.title")!=LocalData.Json.Serialize(expected))
                throw new InvalidOperationException("Single-language caption or page title did not update.");
            string disclaimer=option=="en"?"Disclaimer":"免责声明";
            const string disclaimerTest=@"(() => {
                const trigger=document.querySelector('[data-act=disclaimer-open]');
                const dialog=document.querySelector('#disclaimer-dialog');
                trigger.click();
                const title=dialog.open&&dialog.querySelector('h2').textContent;
                dialog.querySelector('[data-act=disclaimer-close]').click();
                return !dialog.open&&document.activeElement===trigger?title:null;
            })()";
            if(await core.ExecuteScriptAsync("document.querySelector('[data-act=disclaimer-open]').focus();"+disclaimerTest)!=LocalData.Json.Serialize(disclaimer))
                throw new InvalidOperationException("Localized disclaimer did not open, close, or restore focus.");
            if(caption!=null){
                using(var snapshot=new Bitmap(caption.Width,caption.Height)){
                    caption.DrawToBitmap(snapshot,new Rectangle(Point.Empty,caption.ClientSize));
                    snapshot.Save(report+".caption-"+option+".png",System.Drawing.Imaging.ImageFormat.Png);
                }
            }
        }
        if(caption==null)return; // Use the system controls in high contrast.
        WindowRect frame;
        var client=caption.PointToScreen(Point.Empty);
        if(!GetWindowRect(Handle,out frame)||client.Y-frame.Top<0||client.Y-frame.Top>1)
            throw new InvalidOperationException("An unthemed native band remains above the caption.");
        int hitX=client.X+caption.Width/2, hitY=frame.Top+CaptionUnit(2);
        if(SendMessage(Handle,0x0084,IntPtr.Zero,new IntPtr((hitX&0xffff)|((hitY&0xffff)<<16))).ToInt32()!=12)
            throw new InvalidOperationException("The top edge no longer supports resizing.");
        foreach(var theme in new[]{"light","dark","mist","paper","ink","glass"}){
            var previous=caption.BackColor;
            await core.ExecuteScriptAsync("(()=>{const s=document.querySelector('#theme');s.value='"+theme+"';s.dispatchEvent(new Event('change',{bubbles:true}));})()");
            for(int i=0;i<30&&captionTheme!=theme;i++)await Task.Delay(100);
            if(captionTheme!=theme||caption.BackColor==previous||view.Top<caption.Bottom)throw new InvalidOperationException("Caption theme or layout did not update.");
            if(minimizeCaption.FlatAppearance.MouseOverBackColor!=closeCaption.FlatAppearance.MouseOverBackColor||
                maximizeCaption.FlatAppearance.MouseDownBackColor!=closeCaption.FlatAppearance.MouseDownBackColor)
                throw new InvalidOperationException("Caption interaction colors do not match.");
            if(theme=="light"&&await core.ExecuteScriptAsync("getComputedStyle(document.querySelector('.side')).backgroundColor")!="\"rgb(234, 240, 240)\"")
                throw new InvalidOperationException("Light theme sidebar did not become light.");
        }
        using(var glyph=new Bitmap(minimizeCaption.Width,minimizeCaption.Height)){
            minimizeCaption.DrawToBitmap(glyph,new Rectangle(Point.Empty,minimizeCaption.ClientSize));
            int y=glyph.Height/2, pixels=0;
            for(int x=0;x<glyph.Width;x++)if(glyph.GetPixel(x,y).ToArgb()==minimizeCaption.ForeColor.ToArgb())pixels++;
            if(pixels<CaptionUnit(12))throw new InvalidOperationException("Minimize glyph is too small.");
        }
        using(var snapshot=new Bitmap(caption.Width,caption.Height)){
            caption.DrawToBitmap(snapshot,new Rectangle(Point.Empty,caption.ClientSize));
            snapshot.Save(report+".caption.png",System.Drawing.Imaging.ImageFormat.Png);
        }
        maximizeCaption.PerformClick();
        if(WindowState!=FormWindowState.Maximized)throw new InvalidOperationException("Caption did not maximize.");
        if(caption.PointToScreen(Point.Empty).Y<Screen.FromHandle(Handle).WorkingArea.Top-1)
            throw new InvalidOperationException("Maximized caption overlaps the monitor work area.");
        maximizeCaption.PerformClick();
        if(WindowState!=FormWindowState.Normal)throw new InvalidOperationException("Caption did not restore.");
        Activate();
        if(!closeCaption.TabStop||!closeCaption.Focus())throw new InvalidOperationException("Close button is not keyboard focusable.");
        closeCaption.NotifyDefault(true);
        try{TestCloseCaptionBorder();}
        finally{closeCaption.NotifyDefault(false);}
        if(!minimizeCaption.Focus())throw new InvalidOperationException("Could not focus minimize for the restore regression.");
        minimizeCaption.PerformClick();
        if(WindowState!=FormWindowState.Minimized)throw new InvalidOperationException("Caption did not minimize.");
        WindowState=FormWindowState.Normal;
        await Task.Delay(100);
        if(minimizeCaption.Focused||ActiveControl==minimizeCaption)
            throw new InvalidOperationException("Minimize retained the focus outline after restore.");
    }
    private void TestCloseCaptionBorder(){
        using(var snapshot=new Bitmap(closeCaption.Width,closeCaption.Height)){
            closeCaption.DrawToBitmap(snapshot,new Rectangle(Point.Empty,closeCaption.ClientSize));
            var fill=snapshot.GetPixel(0,0);
            for(int x=0;x<snapshot.Width;x++)
                for(int y=0;y<snapshot.Height;y++)
                    if((x<3||x>=snapshot.Width-3||y<3||y>=snapshot.Height-3)&&snapshot.GetPixel(x,y)!=fill)
                        throw new InvalidOperationException("Close button drew an outer focus/default frame.");
        }
    }
}
