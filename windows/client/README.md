# 流枢 FlowPivot · Windows x64 功能预览

不是网页演示。需要 Windows 10/11 x64、.NET Framework 4.8 和 Microsoft Edge WebView2 Runtime。安装包和免安装 ZIP 都内置固定版本的 Mihomo、Xray、sing-box 和 V2Fly，按订阅和首选核心自动选择。设置只展示随软件附带的版本，用户无需另装或替换。启动后后台检查软件更新；若有匹配新版，选择「更新」会打开浏览器下载对应的安装包或便携 ZIP，「暂不更新」不下载、不关闭客户端。也可在设置手动检查。安装或打开界面不会自动接管网络。

1. 「订阅」导入 HTTPS 地址、文件或单条节点链接。点整张卡片选订阅；右键可更新、改名、替换地址、移除。
2. 「节点」整行选择。可用 URL 测延迟，实时查看进度和逐节点结果，按延迟排序时在本轮结束后重排；完成提示短暂显示，不占用列表空间。切换订阅或节点会停止当前会话，回首页手动启动。
3. 「首页」选择「系统代理」或「仅本地」，然后启停。代理运行时，点首页示意图中的本机地址即可复制当前监听地址；端口重启后可能变化。「仅本地」监听回环端口，不更改 Windows 设置。连接页和侧栏来自内核实时统计，不含应用绕过代理的流量。停止后本次流量保留到下一次启动；不统计长期用量。
4. 「检测」自定义网站后测 HTTP 可达性；IP 页通过当前代理查询 ipwho.is（地区、ISP、ASN）和 proxycheck.io（已知代理、VPN、Tor、托管与风险信号）。没有信号不等于确认干净；无数据会显示未评估。

5. 「设置」可在玻璃、浅色、深色之间切换并记住选择；玻璃主题使用窗口内半透明材质，不透出桌面。这里的「关闭窗口时」默认为退出程序。选择「最小化到托盘」后，点 × 不会停止代理；双击 Windows 通知区域的流枢图标可打开窗口，右键可退出并停止代理。

## 兼容范围

若导入时报 HTTP 406，表示订阅下载请求被拒绝，还未进入配置解析。可尝试服务商提供的 Clash/Mihomo 专用订阅，或在服务商处下载配置文件后导入。

- Clash / Mihomo YAML `proxies`、支持的纯节点链接与 Base64 列表 → Mihomo。
- sing-box JSON 中通过白名单检查的 `outbounds` → sing-box。
- Xray / V2Fly 共用的 V4 形状 JSON `outbounds` → 选用设置中首选且已安装的兼容内核；Xray 特有传输不会降级给 V2Fly。V2Fly `jsonv5` 独有字段未接入。
- 只导入节点/出站，不执行订阅自带规则、入站、脚本、外部规则集、代理链或任意本地文件引用。奇异参数可能被明确跳过，不承诺所有 URI 或私有格式。连接规则为内置回环直连、其他流量匹配所选节点。每订阅最多 200 节点；单次测速至多 80 个 Mihomo 节点 / 24 个其他内核节点。
- 其他内核作为被鉴权的回环 SOCKS 出站，与 Mihomo 统一入口和统计共同运行。并非四种完全独立的入站实现。
- TUN 是实验功能，限以管理员身份手动启动应用、使用固定校验版 Mihomo 与原生节点。会创建虚拟网卡并改变路由/DNS；优先用系统代理。不保证与其他隧道、网络切换或防火墙兼容，不作为泄漏防护或断网保护。关闭时先由核心撤销 TUN；如果遭遇异常断网，先退出应用再按 Windows 网络状态恢复。
- 安装软件不会上传数据；导入/刷新会请求用户给出的订阅服务，软件更新检查访问 GitHub Releases API（不发送订阅内容），检测按手动操作才访问对应的外部地址。订阅保存在 `%LOCALAPPDATA%\FlowPivot` 并由当前用户的 DPAPI 加密。免安装意为解压运行，也使用此用户目录；卸载保留订阅数据。

## 构建 / 测试

Windows PowerShell、.NET Framework 4.8、Inno Setup 7.1.1，WebView2 SDK `1.0.3537.50` 和 YamlDotNet `16.3.0` 的已解压官方包。`-YamlLicense` 指向同一版本的官方授权文本。

```powershell
.\windows\client\build.ps1 -Iscc C:\tools\ISCC.exe -WebViewSdk C:\tools\WebView2 -YamlSdk C:\tools\YamlDotNet -YamlLicense C:\tools\YamlDotNet-LICENSE.txt -CoreArchiveDir C:\tools\official-core-zips -Stage C:\build\stage -Output C:\build\out
```

`-CoreArchiveDir` 提供 `CoreAsset.cs` 列出的四个原名官方 ZIP，SHA-256 不符时构建失败。详见 `THIRD-PARTY-CORES.md`。

`LoopbackTest.cs` 可与后端源文件编译成独立测试程序，以 `<暂存目录中的 cores 子目录> <新建临时配置目录>` 运行。`--updates` 单测发布筛选；`--check-live` 可单独验证实际 GitHub 元数据请求。默认不触碰 Windows 代理、也不访问外网；`--system-proxy` 是单独的显式启用测试，不建议在正在使用代理或 PAC 时执行。`FlowPivot.exe --self-test <已存在目录中的报告路径>` 用独立测试配置验证生产 WebView2 UI 和原生桥接，覆盖设置中的四个内置版本和更新入口、两项检测页面、添加/删除自定义站点。`node windows/client/smoke-ui.cjs <暂存目录>` 用 Playwright 单独验证页面交互及布局；这不是核心端到端测试。

## 发布约定

使用 `v<版本>` GitHub Release（版本从 `AppUpdate.cs` 获取，也接受不带 `v` 的 tag），上传同版 `FlowPivot-<版本>-windows-x64-setup.exe` 和 `FlowPivot-<版本>-windows-x64-portable.zip`。稳定版不提醒预发布，测试版可以收到后续测试版或稳定版。检查只请求最近 30 条发布，忽略草稿、缺少两种包的发布、低版本和其他地址的下载资产。发现新版后仍由用户决定是否打开下载，应用不运行安装器也不静默替换文件。对外发布前核对第三方许可和对应源代码。
