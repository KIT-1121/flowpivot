using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

[assembly: AssemblyTitle("FlowPivot – Offline Preview")]
[assembly: AssemblyProduct("FlowPivot Preview")]
[assembly: AssemblyVersion("0.1.0.1")]
[assembly: AssemblyFileVersion("0.1.0.1")]

internal sealed class PreviewWindow : Form
{
    private const string Host = "flowpivot-preview.example";
    private const string Home = "https://" + Host + "/index.html";
    private readonly WebView2 view = new WebView2();
    private readonly string report;
    private bool completed;

    [STAThread]
    private static int Main(string[] args)
    {
        bool testing = args.Length == 2 && args[0] == "--self-test";
        if (args.Length != 0 && !testing) return 2;
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using (var window = new PreviewWindow(testing ? Path.GetFullPath(args[1]) : null))
        {
            Application.Run(window);
            return window.completed ? 0 : 1;
        }
    }

    private PreviewWindow(string reportPath)
    {
        report = reportPath;
        Text = "流枢 FlowPivot · 离线预览";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(460, 560);
        ClientSize = new Size(1220, 780);
        var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (icon != null) Icon = icon;
        view.Dock = DockStyle.Fill;
        Controls.Add(view);
        Shown += async (sender, args) => await OpenPreview();
    }

    private static bool IsLocal(string value)
    {
        Uri uri;
        return Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.Scheme == "https" &&
            uri.IsDefaultPort && uri.Host == Host && uri.UserInfo.Length == 0;
    }

    private async Task OpenPreview()
    {
        try
        {
            string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "web");
            if (!File.Exists(Path.Combine(folder, "index.html"))) throw new IOException("缺少本地预览文件，请重新安装。");
            string profile = report == null
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlowPivot Preview", "WebView2")
                : report + ".profile";
            var environment = await CoreWebView2Environment.CreateAsync(null, profile);
            await view.EnsureCoreWebView2Async(environment);
            var core = view.CoreWebView2;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsWebMessageEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.NewWindowRequested += (sender, args) => args.Handled = true;
            core.DownloadStarting += (sender, args) => args.Cancel = true;
            core.NavigationStarting += (sender, args) => args.Cancel = !IsLocal(args.Uri);
            core.FrameNavigationStarting += (sender, args) => args.Cancel = true;
            core.PermissionRequested += (sender, args) => args.State = CoreWebView2PermissionState.Deny;
            core.SetVirtualHostNameToFolderMapping(Host, folder, CoreWebView2HostResourceAccessKind.DenyCors);
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (sender, args) => {
                if (!IsLocal(args.Request.Uri))
                    args.Response = environment.CreateWebResourceResponse(Stream.Null, 403, "Blocked", "Content-Type: text/plain");
            };
            var loaded = new TaskCompletionSource<bool>();
            core.NavigationCompleted += (sender, args) => loaded.TrySetResult(args.IsSuccess);
            core.Navigate(Home);
            if (await Task.WhenAny(loaded.Task, Task.Delay(12000)) != loaded.Task || !await loaded.Task)
                throw new IOException("无法打开本地预览页面。");
            completed = true;
            if (report != null)
            {
                string result = await core.ExecuteScriptAsync("(document.querySelector('h1')?.textContent==='首页' && document.querySelector('#power')?.disabled===true && document.querySelector('[data-route=nodes]')!==null)");
                if (result != "true") throw new IOException("预览自检失败：未加载应用交互。");
                File.WriteAllText(report, "PASS: installed local HTML, application JavaScript, Home controls and navigation.");
                Close();
            }
        }
        catch (Exception error)
        {
            completed = false;
            string hint = error is WebView2RuntimeNotFoundException
                ? "未找到 Microsoft Edge WebView2 Runtime。请安装微软官方 WebView2 Runtime 后重试。"
                : "无法启动 FlowPivot 预览。\n" + error.Message;
            if (report == null) MessageBox.Show(this, hint, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            else File.WriteAllText(report, "FAIL: " + error.GetType().Name + " – " + error.Message);
            Close();
        }
    }
}
