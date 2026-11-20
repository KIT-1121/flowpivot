# 流枢 FlowPivot · 离线交互预览

FlowPivot 是一个多内核代理客户端项目。**当前版本是离线界面预览，不是可用的代理客户端。** 不需要安装、不需要订阅凭据，不会接管系统代理或 TUN，也不会运行内核或请求外部站点。

## 体验

从 [Releases](https://github.com/KIT-1121/flowpivot/releases) 下载 `flowpivot-v0.1.0-preview.1-web.zip`，完整解压，用 Chrome 或 Edge 打开 `index.html`。从源码目录打开 `index.html` 也可以。HTML、JS、CSS 需要放在同一目录。

- **订阅**：体验样本导入和本地文件格式识别；可整卡选择，支持右键更新样本、编辑名称和移除。
- **节点**：整行选择，不重绘页面；可模拟测延迟，按订阅顺序、延迟或名称排序。
- **首页和连接**：模拟启停、系统代理/TUN 模式、连接统计与流量显示。
- **检测**：连通性检测可添加和移除自定义网站，地址保存在当前浏览器；测速、连通性及 IP 数据均为模拟。
- **设置**：主题、模拟开机启动及 Mihomo、Xray、sing-box、V2Fly 的独立模拟版本管理。

## 验证

在安装了 Playwright 和 Chromium 的 Node.js 环境中执行 `node smoke.cjs`。已有浏览器时，可设置 `FLOWPIVOT_BROWSER` 指定 Chrome 可执行文件；`FLOWPIVOT_PLAYWRIGHT` 可指定预装 Playwright 模块的路径。测试覆盖七个页面、320–1440 px 的五种视口、节点切换与延迟排序、自定义检测站点持久化，并检查没有 HTTP(S) 请求或浏览器错误。

## 发布范围

Web 预览 ZIP 不区分 32/64 位，不是 Windows 原生免安装版。Windows 安装版和原生免安装版、Android APK、macOS、Linux 安装包均未发布；以之后正式接入真实代理、并为各平台独立构建验证后的 Release 为准。
