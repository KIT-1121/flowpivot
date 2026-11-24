# 流枢 FlowPivot

FlowPivot 是一个多内核代理客户端项目。Windows x64 功能版位于 [`windows/client/`](windows/client/README.md)，支持真实核心转发，能力与验证范围见该目录说明。

浏览器直接打开 `index.html` 的版本仍是离线界面预览，不会运行内核、调用系统代理或访问外部站点。

## 体验

下载或克隆本仓库源码，用 Chrome 或 Edge 打开 `index.html`。如果下载的是 ZIP，请先完整解压。HTML、JS、CSS（包括共享的 `visuals.js`）需要放在同一目录。

- **订阅**：体验样本导入和本地文件格式识别；可整卡选择，支持右键更新样本、编辑名称和移除。
- **节点**：整行选择，不重绘页面；可模拟测延迟，按订阅顺序、延迟或名称排序。
- **首页和连接**：模拟启停、系统代理/TUN 模式、连接统计与流量显示。
- **检测**：连通性检测可添加和移除自定义网站，地址保存在当前浏览器；测速、连通性及 IP 数据均为模拟。
- **设置**：主题、模拟开机启动及 Mihomo、Xray、sing-box、V2Fly 的独立模拟版本管理。

## 验证

在安装了 Playwright 和 Chromium 的 Node.js 环境中执行 `node smoke.cjs`。已有浏览器时，可设置 `FLOWPIVOT_BROWSER` 指定 Chrome 可执行文件；`FLOWPIVOT_PLAYWRIGHT` 可指定预装 Playwright 模块的路径。测试覆盖七个页面、320–1440 px 的五种视口、节点切换与延迟排序、自定义检测站点持久化，并检查没有 HTTP(S) 请求或浏览器错误。

## 发布范围

### Windows x64 · 0.2.0-alpha.19

本机代理默认固定在 `127.0.0.1:7897`。重新启动、重新连接和切换节点不会改变端口。停止代理后，可在「设置 → 本机代理端口」自定义；端口被占用时会提示，不会自动改用随机端口。

Git 使用 HTTPS 远程地址时，可在仓库目录配置一次。运行 Git 命令时需启动流枢，若自定义了端口请相应替换。

```powershell
git config http.proxy http://127.0.0.1:7897
```

Windows x64 安装包与免安装 ZIP 为预览版；公开分发状态见 [GitHub Releases](https://github.com/KIT-1121/flowpivot/releases)。本版本不包含 Android、Windows 32 位、macOS 或 Linux 安装包。
