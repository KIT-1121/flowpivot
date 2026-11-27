using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

internal sealed partial class ClientWindow : Form {
    private const string Host="flowpivot-client.example";
    private const string Home="https://"+Host+"/index.html";
    private readonly string root,report;
    private readonly WebView2 view=new WebView2();
    private ClientService service;
    private bool busy,importing,measuring,closing,checkingUpdate,closeToTray=true,exitRequested;
    private NotifyIcon tray;
    private ContextMenuStrip trayMenu;
    private Icon connectedTrayIcon;
    private bool trayConnected;
    private AppUpdate.Candidate offeredUpdate;
    private readonly System.Windows.Forms.Timer subscriptionTimer=new System.Windows.Forms.Timer{Interval=5000};
    private EventWaitHandle upgradeExit;
    private System.Windows.Forms.Timer upgradeTimer;
    private Task<Action> subscriptionDownload;
    private TaskCompletionSource<bool> importTestGate;
    private TaskCompletionSource<bool> importTestStarted;
    private int exitCode;

    [STAThread] private static int Main(string[] args) {
        bool test=args.Length==2&&args[0]=="--self-test";
        if(args.Length!=0&&!test)return 2;
        using(var mutex=new Mutex(false,test?@"Local\FlowPivot.Windows.UI.SelfTest":UpgradeSignals.ClientMutex)) {
            bool acquired;
            try{acquired=mutex.WaitOne(0);}catch(AbandonedMutexException){acquired=true;}
            if(!acquired){if(!test)MessageBox.Show("流枢已打开。","FlowPivot");return 3;}
            try {
                Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                using(var window=new ClientWindow(test?Path.GetFullPath(args[1]):null)){Application.Run(window);return window.exitCode;}
            } finally {mutex.ReleaseMutex();}
        }
    }
    private ClientWindow(string test) {
        report=test;
        root=test==null?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"FlowPivot"):test+".profile";
        Text=CaptionName;StartPosition=FormStartPosition.CenterScreen;MinimumSize=new Size(430,550);ClientSize=new Size(1150,760);
        Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        view.Dock=DockStyle.Fill;Controls.Add(view);
        AddCaption();
        if(test==null){
            upgradeExit=new EventWaitHandle(false,EventResetMode.AutoReset,UpgradeSignals.ExitEvent);
            upgradeTimer=new System.Windows.Forms.Timer{Interval=200};
            upgradeTimer.Tick+=(o,e)=>{if(upgradeExit.WaitOne(0)){upgradeTimer.Stop();ExitApplication();}};
        }
        Shown+=async (o,e)=>{if(upgradeTimer!=null)upgradeTimer.Start();await Boot();};
        FormClosing+=OnClosing;
        FormClosed+=(o,e)=>{subscriptionTimer.Dispose();if(upgradeTimer!=null){upgradeTimer.Dispose();upgradeExit.Dispose();}};
        subscriptionTimer.Tick+=RefreshSubscriptions;
    }
    private static bool Local(string raw) {
        Uri u;return Uri.TryCreate(raw,UriKind.Absolute,out u)&&u.Host==Host&&u.Scheme=="https"&&u.IsDefaultPort&&u.UserInfo.Length==0;
    }
    private async Task Boot(){
        busy=true;
        try {
            if(!await OpenSubscriptionLibrary()){busy=false;ExitApplication();return;}
            closeToTray=service.CloseToTray;
            var env=await CoreWebView2Environment.CreateAsync(null,Path.Combine(root,"WebView2"));
            view.AllowExternalDrop=true;
            await view.EnsureCoreWebView2Async(env);var core=view.CoreWebView2;
            if(closing||exitRequested){busy=false;ExitApplication();return;}
            core.Settings.AreHostObjectsAllowed=false;core.Settings.AreDevToolsEnabled=false;
            core.Settings.AreDefaultContextMenusEnabled=false;core.Settings.IsWebMessageEnabled=true;core.Settings.IsStatusBarEnabled=false;
            core.NewWindowRequested+=(o,e)=>e.Handled=true;
            core.DownloadStarting+=(o,e)=>e.Cancel=true;
            core.NavigationStarting+=(o,e)=>e.Cancel=!Local(e.Uri)||new Uri(e.Uri).AbsolutePath!="/index.html";
            core.FrameNavigationStarting+=(o,e)=>e.Cancel=true;
            core.PermissionRequested+=(o,e)=>e.State=CoreWebView2PermissionState.Deny;
            core.SetVirtualHostNameToFolderMapping(Host,Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"web"),CoreWebView2HostResourceAccessKind.DenyCors);
            core.AddWebResourceRequestedFilter("*",CoreWebView2WebResourceContext.All);
            core.WebResourceRequested+=(o,e)=>{if(!Local(e.Request.Uri))e.Response=env.CreateWebResourceResponse(Stream.Null,403,"Blocked","Content-Type: text/plain");};
            core.WebMessageReceived+=OnMessage;
            busy=false;core.Navigate(Home);
            if(report==null)subscriptionTimer.Start();
            if(report!=null) {
                for(int i=0;i<60;i++) {
                    await Task.Delay(200);
                    if(await core.ExecuteScriptAsync("document.querySelector('#power')?.textContent==='启动' && document.body.dataset.ready==='yes'")=="true"){
                        const string uiScript=@"(() => {
                            if(document.querySelectorAll('#nav button[data-route]').length!==7||document.querySelector('#nav a[href], #nav [title]')) return false;
                            const homeLink=document.querySelector('[data-route=home]');
                            if(getComputedStyle(homeLink).display!=='flex'||!homeLink.querySelector('use')||!document.querySelector('.route-diagram svg')) return false;
                            document.querySelector('[data-route=sources]').click();
                            if(document.querySelector('h1').textContent!=='订阅' || !document.querySelector('[data-act=import-file]')) return false;
                            document.querySelector('[data-route=settings]').click();
                            if(document.querySelectorAll('.core-row [data-bundled=true]').length!==4||document.querySelector('.core-panel button')||!document.querySelector('[data-act=app-check]')||document.querySelectorAll('#theme option').length!==6) return false;
                            const select=document.querySelector('#preferred'), label=select.labels[0];
                            if(select.getBoundingClientRect().width>172||label.closest('.row-main').getBoundingClientRect().width<105) return false;
                            document.querySelector('#checks-toggle').click();
                            document.querySelector('[data-route=reach]').click();
                            const url=document.querySelector('#site-url');
                            url.value='flowpivot-test.invalid';
                            document.querySelector('#site-form').requestSubmit();
                            if(!document.querySelector('.check-table').textContent.includes('flowpivot-test.invalid')) return false;
                            document.querySelector('[data-act=site-remove]').click();
                            document.querySelector('[data-route=ip]').click();
                            if(document.querySelector('h1').textContent!=='IP 纯净度检测') return false;
                            document.querySelector('[data-route=connections]').click();
                            return document.querySelector('#metric-total').textContent==='0';
                        })()";
                        if(await core.ExecuteScriptAsync(uiScript)!="true")throw new InvalidOperationException("生产 UI 自检失败。");
                        await TestCaption(core);
                        await TestLogs(core);
                        await TestCloseBehavior(core);
                        await TestBackgroundImport(core);
                        await TestTrayIndicator(core);
                        File.WriteAllText(report,"PASS: production WebView2, native bridge, background import with other operations, YAML drop, tray icon reflects real connection status, paper default, caption themes, minimize/restore without retained focus, frameless focused/default close button, Chinese and English captions and disclaimers, log viewer, settings, diagnostics and tray restore.");
                        Close(); // Hide once more, then invoke the tray's real exit command.
                        trayMenu.Items[1].PerformClick();return;
                    }
                }
                throw new InvalidOperationException("原生桥接自检超时。");
            }
        }catch(Exception e){
            exitCode=1;busy=false;
            string message=e is WebView2RuntimeNotFoundException?"请先安装 Microsoft Edge WebView2 Runtime。":e is InvalidOperationException?e.Message:"流枢无法启动。请检查系统组件及本地配置读写权限。";
            if(report!=null)File.WriteAllText(report,"FAIL: "+e.GetType().Name+": "+message);else MessageBox.Show(this,message,Text,MessageBoxButtons.OK,MessageBoxIcon.Error);
            ExitApplication();
        }
    }
    private async Task<bool> OpenSubscriptionLibrary(){
        bool reset=false;
        while(!closing&&!exitRequested){
            string problem;
            try{
                service=await Task.Run(()=>new ClientService(root,report==null,downloader:report==null?null:(Func<string,System.Net.IWebProxy,string,SubscriptionResult>)TestImportDownload,resetStore:reset));
                return !closing&&!exitRequested;
            }catch(SubscriptionStoreException e){
                if(report!=null)throw;
                new DiagnosticsLog(root,null).Write("本地订阅库启动读取失败",e.InnerException);
                problem=e.Message;
            }catch(Exception e){
                if(!reset||(!DiagnosticsLog.IsFileError(e)&&!(e is System.Security.Cryptography.CryptographicException)))throw;
                new DiagnosticsLog(root,null).Write("无法保留旧订阅库并新建",e);
                problem="暂时无法备份或新建订阅库。请检查当前 Windows 用户、磁盘空间和目录权限。";
            }
            if(closing||exitRequested)return false;
            var choice=ChooseLibraryRecovery(problem);
            if(choice==DialogResult.Cancel)return false;
            reset=choice==DialogResult.Yes;
            if(reset&&MessageBox.Show(this,"旧订阅库将保留在本地，不会删除。新订阅库为空，需要重新导入订阅；原有分流规则及其他库内设置也需要重新配置。\r\n\r\n确定继续吗？","保留旧库并新建",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)
                reset=false;
        }
        return false;
    }
    private SubscriptionResult TestImportDownload(string url,System.Net.IWebProxy proxy,string patch){
        if(importTestGate==null||url!="https://flowpivot-test.invalid/slow")
            throw new InvalidOperationException("Unexpected self-test download.");
        importTestStarted.TrySetResult(true);
        if(!importTestGate.Task.Wait(10000))throw new InvalidOperationException("Import fixture timed out.");
        const string yaml="proxies:\n  - {name: Fixture, type: http, server: 127.0.0.1, port: 18080}";
        return new SubscriptionResult{Body=yaml,Data=Subscription.Parse(yaml)};
    }
    private async Task TestBackgroundImport(CoreWebView2 core){
        importTestGate=new TaskCompletionSource<bool>();
        importTestStarted=new TaskCompletionSource<bool>();
        try{
            await core.ExecuteScriptAsync("window.importResult=null;void nativeCall('import',{input:'https://flowpivot-test.invalid/slow'}).then(v=>window.importResult=v,()=>window.importResult=false);");
            if(await Task.WhenAny(importTestStarted.Task,Task.Delay(3000))!=importTestStarted.Task)
                throw new InvalidOperationException("Background import did not reach the fake downloader.");
            await core.ExecuteScriptAsync("void mutate('preferred',{value:'V2Fly'},false).then(v=>window.testPreference=v?.preferred);");
            for(int i=0;i<30&&await core.ExecuteScriptAsync("window.testPreference==='V2Fly'")!="true";i++)await Task.Delay(100);
            if(await core.ExecuteScriptAsync("window.testPreference==='V2Fly' && window.importResult===null")!="true")
                throw new InvalidOperationException("Background download blocked a command or completed prematurely.");
        }finally{importTestGate.TrySetResult(true);}
        for(int i=0;i<35&&await core.ExecuteScriptAsync("window.importResult===true")!="true";i++)await Task.Delay(100);
        if(await core.ExecuteScriptAsync("window.importResult===true")!="true")
            throw new InvalidOperationException("Background import did not commit.");
        await core.ExecuteScriptAsync(@"document.querySelector('[data-route=sources]').click();
            const dropped=new DataTransfer();
            dropped.items.add(new File(['proxies:\n  - {name: Drop, type: http, server: 127.0.0.1, port: 18081}'],'Dropped.yaml'));
            document.querySelector('#page').dispatchEvent(new DragEvent('drop',{dataTransfer:dropped,bubbles:true,cancelable:true}));");
        for(int i=0;i<50;i++){
            if(await core.ExecuteScriptAsync("[...document.querySelectorAll('.source-select')].some(b=>b.textContent==='Dropped')")=="true")return;
            await Task.Delay(100);
        }
        throw new InvalidOperationException("Dropped YAML did not reach the real subscription library.");
    }
    private DialogResult ChooseLibraryRecovery(string problem){
        using(var dialog=new Form{Text="订阅库恢复",StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,
            MinimizeBox=false,MaximizeBox=false,ShowInTaskbar=false,AutoScaleMode=AutoScaleMode.Font,ClientSize=new Size(542,168)}){
            var message=new Label{Dock=DockStyle.Fill,Padding=new Padding(18),Text=problem+
                "\r\n\r\n建议先重试。如果仍无法读取，可保留旧库并新建，再重新导入订阅。"};
            var actions=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=53,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(12,5,12,5)};
            var exit=new Button{Text="退出",AutoSize=true,DialogResult=DialogResult.Cancel};
            var fresh=new Button{Text="保留旧库并新建",AutoSize=true,DialogResult=DialogResult.Yes};
            var retry=new Button{Text="重试",AutoSize=true,DialogResult=DialogResult.Retry,TabIndex=0};
            actions.Controls.AddRange(new Control[]{exit,fresh,retry});
            dialog.Controls.Add(message);dialog.Controls.Add(actions);
            dialog.AcceptButton=retry;dialog.CancelButton=exit;dialog.ActiveControl=retry;
            var result=dialog.ShowDialog(this);
            return result==DialogResult.Retry||result==DialogResult.Yes?result:DialogResult.Cancel;
        }
    }
    private async Task TestCloseBehavior(CoreWebView2 core){
        if(!closeToTray)throw new InvalidOperationException("Tray is not the default.");
        const string setBehavior=@"value => {
            document.querySelector('[data-route=settings]').click();
            const select=document.querySelector('#close-behavior');
            if(!select || select.disabled) return false;
            select.value=value;select.dispatchEvent(new Event('change',{bubbles:true}));return true;
        }";
        if(await core.ExecuteScriptAsync("document.querySelector('[data-route=settings]').click();document.querySelector('#close-behavior').value")!="\"tray\"")throw new InvalidOperationException("Tray default not shown.");
        foreach(var value in new[]{"exit","tray"}){
            if(await core.ExecuteScriptAsync("("+setBehavior+")('"+value+"')")!="true")throw new InvalidOperationException("Close setting missing.");
            for(int i=0;i<40&&(busy||(closeToTray!=(value=="tray")));i++)await Task.Delay(100);
            using(var saved=new ClientService(root,false)){
                if(saved.CloseToTray!=(value=="tray"))throw new InvalidOperationException("Close setting not saved.");
            }
        }
        if(closeCaption==null)Close();
        else{
            // The test window can be inactive after programmatic minimize/restore.
            closeCaption.Select();
            if(ActiveControl!=closeCaption)throw new InvalidOperationException("Could not select Close for the tray regression.");
            closeCaption.PerformClick();
        }
        if(IsDisposed||Visible||tray==null||!tray.Visible)throw new InvalidOperationException("Window did not hide to tray.");
        trayMenu.Items[0].PerformClick();
        if(!Visible||tray.Visible)throw new InvalidOperationException("Window did not restore from tray.");
        if(closeCaption!=null&&(closeCaption.Focused||ActiveControl==closeCaption))
            throw new InvalidOperationException("Close retained focus after tray restore.");
    }
    private void RestoreWindow(){
        Show();
        if(WindowState==FormWindowState.Minimized)WindowState=FormWindowState.Normal;
        Activate();
        if(tray!=null)tray.Visible=trayConnected;
    }
    private void ExitApplication(){exitRequested=true;Close();}
    private async void RefreshSubscriptions(object sender,EventArgs args){
        if(busy||closing)return;
        if(subscriptionDownload==null){subscriptionDownload=service.FetchDueSubscription();return;}
        if(!subscriptionDownload.IsCompleted)return;
        busy=true;
        try{
            var apply=await subscriptionDownload;
            await Task.Run(apply);
        }finally{
            subscriptionDownload=null;busy=false;
            SyncTrayConnection();
            if(closing)BeginInvoke((Action)Close);
        }
    }
    private void EnsureTray(){
        if(tray==null){
            trayMenu=new ContextMenuStrip();
            trayMenu.Items.Add(englishCaption?"Open FlowPivot":"打开流枢",null,(o,e)=>RestoreWindow());
            trayMenu.Items.Add(englishCaption?"Quit":"退出",null,(o,e)=>ExitApplication());
            tray=new NotifyIcon{Icon=Icon,Text=CaptionName,ContextMenuStrip=trayMenu};
            tray.MouseDoubleClick+=(o,e)=>{if(e.Button==MouseButtons.Left)RestoreWindow();};
        }
    }
    private string TrayCaption { get {return !trayConnected?CaptionName:englishCaption?"FlowPivot · Connected":"流枢 · 已连接";} }
    private void SyncTrayConnection(){
        bool connected=service!=null&&service.IsConnected;
        if(connected==trayConnected)return;
        trayConnected=connected;
        if(connected)EnsureTray();
        if(tray==null)return;
        if(connected&&connectedTrayIcon==null)connectedTrayIcon=CreateConnectedTrayIcon();
        tray.Icon=connected?connectedTrayIcon:Icon;
        tray.Text=TrayCaption;
        tray.Visible=connected||!Visible;
    }
    private void HideToTray(){
        EnsureTray();tray.Visible=true;Hide();
    }
    private void OnClosing(object sender,FormClosingEventArgs args){
        if(args.CloseReason==CloseReason.UserClosing&&closeToTray&&!exitRequested&&!closing){
            args.Cancel=true;HideToTray();return;
        }
        if(busy){args.Cancel=true;closing=true;return;}
        subscriptionTimer.Stop();
        if(tray!=null){tray.Visible=false;tray.Dispose();trayMenu.Dispose();tray=null;trayMenu=null;}
        if(connectedTrayIcon!=null){connectedTrayIcon.Dispose();connectedTrayIcon=null;}
        if(service!=null)try{service.Dispose();}catch(Exception){if(report==null)MessageBox.Show(this,"未能恢复系统代理。请检查 Windows 的代理设置。",Text);}
    }
    private async void OnMessage(object sender,CoreWebView2WebMessageReceivedEventArgs e){
        if(!Local(e.Source)||new Uri(e.Source).AbsolutePath!="/index.html")return;
        int id=0;
        try{
            var message=LocalData.Parse(e.WebMessageAsJson);id=(int)LocalData.Number(message,"id");if(id<1)return;
            var action=LocalData.Text(message,"action");var args=LocalData.Obj(message,"args")??new Dictionary<string,object>();
            if(action=="language"){
                string value=LocalData.Text(args,"value");
                if(value!="zh-CN"&&value!="en")return;
                SetLanguage(value);Reply(id,true,null);return;
            }
            if(action=="theme") {
                string value=LocalData.Text(args,"value");
                ApplyCaptionTheme(value);Reply(id,true,null);return;
            }
            if(action=="app-check") {
                if(checkingUpdate){Reply(id,false,"正在检查更新。");return;}
                checkingUpdate=true;
                try {
                    // Self-test never contacts external services. Production checks are independent of proxy actions.
                    offeredUpdate=report==null?await Task.Run(()=>AppUpdate.Check()):null;
                    Reply(id,true,offeredUpdate==null?null:new {version=offeredUpdate.Version});
                } finally {checkingUpdate=false;}
                return;
            }
            if(action=="app-download"){
                if(report!=null||offeredUpdate==null||offeredUpdate.Version!=LocalData.Text(args,"version"))
                    throw new InvalidOperationException("更新信息已失效，请重新检查。");
                bool installed=File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"unins000.exe"));
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(installed?offeredUpdate.Setup:offeredUpdate.Portable){UseShellExecute=true});
                Reply(id,true,null);return;
            }
            if(action=="import"||action=="import-file"||action=="import-drop"){
                await ImportInBackground(id,action,args);return;
            }
            if(action=="delay"){await DelayInBackground(id);return;}
            if(busy){Reply(id,false,"请等待当前操作完成。");return;}
            if(action=="copy-proxy"){
                var address=service.LocalProxyAddress();
                CopyText(address);
                Reply(id,true,address);return;
            }
            if(action=="copy-source-url"){
                // Subscription URLs may contain credentials. Keep them in the native
                // host and clipboard only; the renderer receives an acknowledgement.
                CopyText(service.SubscriptionUrl(LocalData.Text(args,"id")));
                Reply(id,true,true);return;
            }
            busy=true;
            try{
                if(action=="logs-choose") {
                    using(var dialog=new FolderBrowserDialog{Description="选择流枢日志存放目录",SelectedPath=service.LogDirectory,ShowNewFolderButton=true}){
                        if(dialog.ShowDialog(this)!=DialogResult.OK){Reply(id,true,null);return;}
                        string chosen=dialog.SelectedPath;
                        Reply(id,true,await Task.Run(()=>service.ChangeLogDirectory(chosen)));return;
                    }
                }
                if(action=="logs-default") {Reply(id,true,await Task.Run(()=>service.ChangeLogDirectory(null)));return;}
                if(action=="logs-folder") {
                    var directory=service.LogDirectory;
                    if(report==null){
                        await Task.Run(()=>Directory.CreateDirectory(directory));
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(directory){UseShellExecute=true});
                    }
                    Reply(id,true,null);return;
                }
                if(action=="source-export") {
                    string yaml=(string)await Task.Run(()=>service.Run("source-yaml",args));
                    using(var dialog=new SaveFileDialog{Title=englishCaption?"Export YAML (contains private node credentials)":"导出 YAML（含节点凭据，请勿公开）",FileName="FlowPivot-nodes.yaml",Filter=englishCaption?"YAML files|*.yaml;*.yml":"YAML 文件|*.yaml;*.yml",DefaultExt="yaml",AddExtension=true,OverwritePrompt=true,RestoreDirectory=true}){
                        if(dialog.ShowDialog(this)!=DialogResult.OK){Reply(id,true,null);return;}
                        string path=dialog.FileName;
                        await Task.Run(()=>File.WriteAllText(path,yaml,new UTF8Encoding(false)));
                        Reply(id,true,true);return;
                    }
                }
                if(action=="start"&&LocalData.Text(args,"mode")=="tun"&&MessageBox.Show(this,"实验性 TUN 将改变 Windows 路由与 DNS，可能短时间中断网络。确定继续？",Text,MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes){Reply(id,true,null);return;}
                var result=await Task.Run(()=>service.Run(action,args));
                if(action=="close-behavior")closeToTray=service.CloseToTray;
                Reply(id,true,result);
            }finally{busy=false;SyncTrayConnection();if(closing)BeginInvoke((Action)Close);}
        }catch(Exception error){
            if(id>0)Reply(id,false,error is InvalidOperationException?error.Message:"操作未完成。请检查核心、网络或本地文件权限。");
        }
    }
    private bool CanFinishBackgroundTask {get{return !closing&&!exitRequested&&!IsDisposed;}}
    private async Task DelayInBackground(int id){
        if(measuring)throw new InvalidOperationException("已有测速正在运行。");
        measuring=true;
        try{
            while(busy&&CanFinishBackgroundTask)await Task.Delay(50);
            if(!CanFinishBackgroundTask)return;
            var apply=await service.PrepareDelay(body=>{
                try{BeginInvoke((Action)(()=>{if(CanFinishBackgroundTask)Post(new{id,progress=body});}));}
                catch(InvalidOperationException){/* The window closed while probing. */}
            });
            while(busy&&CanFinishBackgroundTask)await Task.Delay(50);
            if(!CanFinishBackgroundTask)return;
            bool applied=apply();
            if(applied)service.RecordDelayResult();
            Reply(id,true,applied);
        }catch(Exception e){
            if(CanFinishBackgroundTask){service.RecordDelayResult(e);throw;}
        }finally{measuring=false;}
    }
    private async Task ImportInBackground(int id,string action,IDictionary<string,object> args){
        if(importing)throw new InvalidOperationException("有订阅正在导入，请稍后再试。");
        importing=true;
        try{
            Func<IDictionary<string,object>> read=()=>args;
            if(action=="import-file"){
                using(var dialog=new OpenFileDialog{Title=englishCaption?"Import subscription file":"导入订阅文件",Filter=englishCaption?"Subscription files|*.yaml;*.yml;*.json;*.txt|All files|*.*":"订阅文件|*.yaml;*.yml;*.json;*.txt|所有文件|*.*",CheckFileExists=true}){
                    if(dialog.ShowDialog(this)!=DialogResult.OK){Reply(id,true,null);return;}
                    string file=dialog.FileName;
                    read=()=>ReadFile(file);
                }
                Post(new{id,progress=new{stage="reading"}});
            }else if(action=="import-drop"){
                // Dropped content is always local input, never a URL to download.
                args["origin"]="file";
            }
            while(busy&&CanFinishBackgroundTask)await Task.Delay(50);
            if(!CanFinishBackgroundTask)return;
            var apply=await service.PrepareImport(read);
            if(busy)Post(new{id,progress=new{stage="saving"}});
            while(busy&&CanFinishBackgroundTask)await Task.Delay(50);
            if(!CanFinishBackgroundTask)return;
            // Commit on the serialized UI dispatcher. No other service task is active.
            apply();
            service.RecordImportResult();
            Reply(id,true,true);
        }catch(Exception e){
            if(CanFinishBackgroundTask){service.RecordImportResult(e);throw;}
        }finally{importing=false;}
    }
    private static void CopyText(string text){
        try{Clipboard.SetText(text,TextDataFormat.UnicodeText);}
        catch(System.Runtime.InteropServices.ExternalException){throw new InvalidOperationException("剪贴板正忙，请稍后重试。");}
    }
    private async Task TestLogs(CoreWebView2 core){
        await core.ExecuteScriptAsync("document.querySelector('[data-route=settings]').click();document.querySelector('[data-act=logs-view]').click();");
        for(int i=0;i<30;i++){
            if(await core.ExecuteScriptAsync("document.querySelector('#logs-dialog').open && document.querySelector('#logs-text').textContent.includes('应用启动')")=="true")break;
            if(i==29)throw new InvalidOperationException("默认日志未在应用内显示。");
            await Task.Delay(100);
        }
        const string privateMarker="FLOWPIVOT_TEST_REDACTION_MARKER";
        string chosen=Path.Combine(root,"自定义 日志");
        await Task.Run(()=>{
            service.ChangeLogDirectory(chosen);
            if(service.LogDirectory!=chosen)throw new InvalidOperationException("自定义日志路径未生效。");
            try{service.Run("import",new Dictionary<string,object>{{"input",privateMarker}});}catch(InvalidOperationException){}
            service.RecordFileImportError(new IOException(privateMarker));
            using(var reloaded=new ClientService(root,false)){
                if(reloaded.LogDirectory!=chosen)throw new InvalidOperationException("自定义路径没有持久化。");
                string invalid=Path.Combine(root,"not-a-directory");
                File.WriteAllText(invalid,"");
                try{reloaded.ChangeLogDirectory(invalid);throw new Exception("Unexpected path acceptance.");}catch(InvalidOperationException){}
                if(reloaded.LogDirectory!=chosen)throw new InvalidOperationException("无效目录不应覆盖现有目录。");
            }
            string file=File.ReadAllText(Directory.GetFiles(chosen,"FlowPivot-*.log")[0]);
            if(!file.Contains("读取订阅文件失败")||file.Contains(privateMarker))throw new InvalidOperationException("日志脱敏测试失败。");
        });
        await core.ExecuteScriptAsync("document.querySelector('[data-act=logs-refresh]').click();");
        for(int i=0;i<30;i++){
            if(await core.ExecuteScriptAsync("!document.querySelector('[data-act=logs-default]').hidden && document.querySelector('#logs-text').textContent.includes('已更换日志目录')")=="true")break;
            if(i==29)throw new InvalidOperationException("自定义日志未能刷新。");
            await Task.Delay(100);
        }
        await core.ExecuteScriptAsync("document.querySelector('[data-act=logs-default]').click();");
        for(int i=0;i<30;i++){
            if(await core.ExecuteScriptAsync("document.querySelector('#logs-text').textContent.includes('已恢复默认日志目录')")=="true")break;
            if(i==29)throw new InvalidOperationException("未能恢复默认日志目录。");
            await Task.Delay(100);
        }
        await core.ExecuteScriptAsync("document.querySelector('[data-act=logs-close]').click();");
    }
    private static IDictionary<string,object> ReadFile(string path){
        using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){
            if(file.Length>Subscription.MaxSize)throw new InvalidOperationException("订阅文件过大。");
            using(var reader=new StreamReader(file,new UTF8Encoding(false,true),true))return new Dictionary<string,object>{{"input",reader.ReadToEnd()},{"name",Path.GetFileNameWithoutExtension(path).Substring(0,Math.Min(64,Path.GetFileNameWithoutExtension(path).Length))},{"origin","file"}};
        }
    }
    private void Reply(int id,bool ok,object body){Post(new{id,ok,body});}
    private void Post(object message){if(!IsDisposed&&view.CoreWebView2!=null)view.CoreWebView2.PostWebMessageAsJson(LocalData.Json.Serialize(message));}
}
