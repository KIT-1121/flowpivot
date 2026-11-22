# FlowPivot Windows x64 离线预览封装

用 WinForms + Microsoft WebView2 把仓库中的静态预览页面封装为独立 Windows 窗口。Inno Setup 打包为当前用户安装程序。安装、卸载均不需要管理员权限，不包含内核、驱动、系统代理操作或真实网站检测。安装不会联网下载运行库。

## 构建

需要 Windows x64、PowerShell、.NET Framework 4.8（含系统 `csc.exe`）、Inno Setup 7.1.1，以及解压的微软 WebView2 SDK NuGet 包 `Microsoft.Web.WebView2` 1.0.3537.50。应从 Inno Setup 官网和 NuGet 获取依赖。启动应用另外需要已安装的 Microsoft Edge WebView2 Runtime。

在仓库根目录运行，输出目录须存在，暂存目录须不存在：

```powershell
.\windows\build.ps1 -Iscc 'C:\tools\InnoSetup\ISCC.exe' -Sdk 'C:\tools\WebView2Sdk' -Stage 'C:\build\FlowPivot-stage' -Output 'C:\build\out'
```

安装包可在测试账号或没有安装该预览的本机进行临时安装自检。测试会创建并卸载当前用户的临时安装，日志留在指定测试目录。

```powershell
.\windows\smoke-install.ps1 -Setup 'C:\build\out\FlowPivot-0.1.0-preview.1-windows-x64-setup.exe' -Scratch 'C:\build\FlowPivot-smoke'
```
