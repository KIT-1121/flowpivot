'use strict';
const english = new Map(`
流枢|FlowPivot
首页|Home
订阅|Subscriptions
节点|Nodes
连接|Connections
设置|Settings
检测|Checks
连通性检测|Reachability
IP 纯净度检测|IP Check
界面语言|Language
流枢 首页|FlowPivot Home
工作台|Navigation
页面内容|Page content
连接状态与流量|Connection status and traffic
实时网速与本次打开累计流量|Live speeds and session traffic
↓ 下载|↓ Down
↑ 上传|↑ Up
网速|Speed
流量|Traffic
未连接|Disconnected
已连接|Connected
关闭|Stop
启动|Start
连接中…|Connecting…
启动中…|Starting…
正在切换节点…|Switching node…
切换中…|Switching…
关闭中…|Stopping…
未选择节点|No node selected
启停控制|Proxy controls
代理控制|Proxy control
代理模式|Proxy mode
系统代理|System proxy
仅本地|Local only
复制本机代理地址|Copy local proxy address
代理链路：本机、内核、出口|Proxy route: local device, core, exit
本机|Local
代理内核|Proxy core
出口|Exit
导入文件|Import file
导入链接|Import link
导入订阅|Import subscription
订阅地址或节点链接|Subscription URL or node link
还没有订阅|No subscriptions
当前订阅|Active subscription
点击使用|Select to use
套餐流量已用比例|Plan usage
暂无套餐流量 · 可尝试更新|No plan data · Try refreshing
自动更新间隔|Auto-update interval
间隔（小时，1–720；填 0 关闭）|Interval in hours (1–720; enter 0 to turn off)
自动更新：关闭|Auto-update: off
上次自动更新失败|Last auto-update failed
只有网络订阅支持自动更新。|Only URL subscriptions support auto-update.
请输入 0–720 之间的整数小时，0 表示关闭。|Enter a whole number from 0 to 720 hours. Enter 0 to turn off.
已保存自动更新间隔|Auto-update interval saved
订阅已更新|Subscription updated
节点列表已更新，请重新选择。|The node list changed. Select a node again.
当前节点已变更或被移除，代理已断开。|The current node changed or was removed. The proxy has disconnected.
未能自动恢复系统代理。请检查 Windows 系统代理设置。|Could not restore the system proxy. Check the Windows proxy settings.
总流量|Total
已用|Used
剩余|Remaining
订阅操作|Subscription actions
更新|Update
编辑名称|Rename
替换订阅链接|Replace URL
移除|Remove
导出 YAML|Export YAML
编辑节点 YAML|Edit node YAML
本地覆写|Local override
编辑订阅|Edit subscription
订阅名称|Subscription name
新 HTTPS 链接（旧链接不显示）|New HTTPS URL (existing URL is hidden)
移除此订阅及其节点？|Remove this subscription and its nodes?
请选择订阅文件…|Choose a subscription file…
正在读取并识别订阅…|Reading subscription…
正在更新订阅…|Updating subscription…
订阅已导入|Subscription imported
导入 / 更新失败|Subscription import or update failed
知道了|Dismiss
YAML 已导出|YAML exported
保存|Save
存为副本|Save a copy
已保存覆写|Override saved
已保存本地副本|Local copy saved
仅编辑 proxies 节点。保存为本地副本，不修改原订阅。|Edit only proxies. Saving creates a local copy and leaves the original intact.
JSON 支持 prefix（节点名前缀）和 exclude（按名称排除）。留空清除，不执行脚本。|Use JSON prefix and exclude to rename or filter nodes. Leave blank to clear. Scripts are not run.
请先在「订阅」选择一个订阅|Select a subscription first
当前订阅的节点|Nodes in the active subscription
节点排序|Sort nodes
订阅顺序|Subscription order
延迟从低到高|Lowest latency
节点名称|Node name
测延迟|Test latency
测速中|Testing
准备测速…|Preparing test…
节点测速进度|Latency test progress
失败|Failed
缺少内核|Core missing
当前连接|Active
规则代理|Proxied
直连|Direct
目标|Destination
走向|Route
规则|Rule
协议|Protocol
上传|Upload
下载|Download
活动连接|Active connections
搜索连接目标|Search destinations
搜索目标|Search destinations
筛选走向|Filter by route
所有走向|All routes
连接排序|Sort connections
内核顺序|Core order
按目标|By destination
上传最多|Most uploaded
下载最多|Most downloaded
显示列|Columns
暂无活动连接|No active connections
没有匹配的连接|No matching connections
操作|Actions
断开|Disconnect
代理|Proxy
分流规则|Routing rules
暂无分流规则|No routing rules yet
从上到下匹配，第一条命中的规则生效。仅影响新连接。|Rules are checked from top to bottom. The first match applies to new connections only.
匹配方式|Match type
域名关键词|Domain keyword
域名正则|Domain regex
精确域名|Exact domain
IP 地址|IP address
匹配包含该词的域名。完整 IP 会按地址匹配。|Matches domains containing this text. A full IP is matched as an address.
仅匹配域名，支持基础正则。|Matches domains only. Basic regex is supported.
只匹配此域名，不含子域名。|Matches only this domain, not its subdomains.
填写 IPv4 或 IPv6，不带端口。|Enter an IPv4 or IPv6 address without a port.
匹配内容|Pattern
添加规则|Add rule
完成|Done
免责声明|Disclaimer
流枢是网络代理客户端，不提供代理节点或订阅服务。请仅使用你信任且有权使用的服务，并遵守适用法律、服务条款和网络管理规定。|FlowPivot is a proxy client and does not provide proxy servers or subscriptions. Use only services you trust and are authorized to use, and follow applicable laws, service terms, and network policies.
第三方节点可能查看连接元数据和未加密流量。流枢不保证匿名、隐私保护、网络连通或地区解锁；检测结果仅供参考。|Third-party proxies can see connection metadata and unencrypted traffic. FlowPivot does not guarantee anonymity, privacy, connectivity, or regional access. Diagnostic results are for reference only.
本软件按现状提供，不保证兼容性、稳定性或适用于特定用途。启用系统代理或 TUN 前，请确认允许更改设备的网络配置。|The software is provided as is, without guarantees of compatibility, stability, or fitness for a particular purpose. Before enabling system proxy or TUN, check that you are authorized to change the device's network settings.
取消编辑|Cancel edit
编辑|Edit
删除|Delete
分流规则已保存，新连接生效。|Routing rules saved. Changes apply to new connections.
无效或不支持的匹配内容。请输入域名、基础正则或不带端口的 IP。|Invalid or unsupported pattern. Enter a domain, basic regex, or IP address without a port.
找不到此分流规则。|This routing rule no longer exists.
已有相同的分流规则，请编辑原规则。|This routing rule already exists. Edit the existing rule instead.
最多保存 160 条分流规则。|You can save up to 160 routing rules.
最多保存 32 条正则规则。|You can save up to 32 regex rules.
目标走向|Destination route
不支持此走向。|Invalid route choice.
未能保存走向，原规则未更改。|Could not save the route. Original rules were kept.
修改走向失败，代理已停止。|Could not apply the route. The proxy has been stopped.
最多显示内核返回的前 200 条；筛选和排序仅作用于这些连接。|Showing up to 200 connections. Filters and sorting apply to those rows.
网站连通性|Website reachability
测连通性|Test sites
网站地址|Website URL
添加网站|Add site
站点|Site
状态|Status
用时|Time
已响应|Responded
服务器或代理错误|Server or proxy error
访问受限|Access restricted
未知响应|Unknown response
请求失败|Request failed
超时|Timed out
未检测|Not tested
网站可达不代表能登录、播放或地区解锁。|Reachability does not confirm sign-in, playback, or region access.
出口 IP 检测|Exit IP check
查出口 IP|Check IP
出口 IP|Exit IP
地区|Region
机构|Organization
风险|Risk
未知|Unknown
未评估（无风险数据）|Not assessed (no risk data)
尚未查询出口 IP|Exit IP not checked
数据来自 ipwho.is 和 proxycheck.io，仅供参考。|Data from ipwho.is and proxycheck.io is for reference only.
请先在首页启动代理|Start the proxy from Home first
基础设置|General settings
界面主题|Theme
玻璃|Glass
浅色|Light
深色|Dark
雾蓝|Mist Blue
暖纸|Warm Paper
墨蓝|Ink Blue
关闭窗口时|On window close
最小化到托盘|Minimize to tray
退出程序|Quit app
开机启动|Launch at sign-in
登录后打开流枢，不自动连接|Open FlowPivot without connecting
通用 JSON 首选核心|Preferred JSON core
仅在两者都兼容时生效|Only applies when both cores are compatible
延迟检测地址|Latency test URL
留空恢复默认|Leave blank to reset
已保存延迟检测地址|Latency test URL saved
本机代理端口|Local proxy port
停止代理后可修改|Stop the proxy to change this
已保存本机代理端口|Local proxy port saved
请先停止代理，再修改本机代理端口。|Stop the proxy before changing its local port.
请输入 1–65535 之间的整数端口。|Enter a whole-number port from 1 to 65535.
维护|Maintenance
软件更新|App updates
检查更新|Check updates
检查中…|Checking…
查看新版|View update
发现新版本|Update available
暂不更新|Not now
暂无可用更新。|No updates available.
将在浏览器中下载新版。安装或替换前请退出流枢。|Download in your browser. Quit FlowPivot before installing or replacing it.
已打开下载。安装或替换前请退出流枢。|Opened the download. Quit FlowPivot before installing or replacing it.
运行日志|Logs
查看日志|View logs
关闭日志|Close
存放目录|Log folder
刷新|Refresh
打开目录|Open folder
更换目录|Change folder
恢复默认|Reset to default
今日日志内容|Today's logs
请选择日志目录…|Choose a log folder…
正在读取日志…|Reading logs…
今天暂无日志。可在「打开目录」查看历史文件。|No logs today. Open the folder to see older logs.
未更改日志目录。|Log folder unchanged.
内置内核|Bundled cores
文件缺失，请重新安装或完整解压|Files missing. Reinstall or extract the full package.
内置内核文件缺失，请重新安装或完整解压流枢。|Core files missing. Reinstall or extract the full package.
TUN 为实验功能；仅支持 Mihomo，需以管理员身份启动客户端，并使用官方固定版核心。|TUN is experimental. It requires the bundled Mihomo core and administrator privileges.
取消|Cancel
确定|OK
请输入新的有效网址（最多 20 个自定义站点）。|Enter a new valid URL (up to 20 custom sites).
后台操作超时。|The operation timed out.
操作未完成。请检查核心、网络或本地文件权限。|The operation failed. Check the core, network, and local file permissions.
订阅文件过大。|Subscription file is too large.
最多保存 20 个订阅。|Up to 20 subscriptions can be saved.
请输入新的 HTTPS 订阅链接。|Enter a new HTTPS subscription URL.
请输入 1–70 个字符的订阅名称。|Enter a subscription name of 1–70 characters.
找不到该订阅。|Subscription not found.
找不到该节点。|Node not found.
请先选择订阅。|Select a subscription first.
请先选择节点。|Select a node first.
请先启动代理。|Start the proxy first.
代理已关闭。|The proxy is stopped.
节点切换失败，已保留原连接。|Node switch failed. The previous connection was kept.
暂时无法读取当前连接。|Unable to read active connections.
暂时无法断开该连接。|Unable to close this connection.
TUN 需要以管理员身份启动，并选择原生 Mihomo 节点。|TUN requires administrator privileges and a native Mihomo node.
更新信息已失效，请重新检查。|Update information expired. Check for updates again.
暂时无法读取内核连接统计。|Connection statistics are temporarily unavailable.
无法通过当前代理查询 IP 数据源。|Unable to query the IP data provider through the current proxy.
IP 数据源没有返回有效结果。|The IP data provider returned no valid result.
请先关闭当前代理，再修改正在使用的订阅。|Stop the proxy before editing the active subscription.
覆写排除了所有节点。|The override excluded every node.
覆写需为 JSON 对象，只支持 prefix 和 exclude。|Use a JSON object containing only prefix and exclude.
prefix 须为 0–40 字的字符串，exclude 须为节点名称数组。|prefix must be 0–40 characters and exclude must be an array of node names.
加前缀后节点名称超过 180 字。|A node name exceeds 180 characters after prefixing.
有节点无效或已超过 200 个，请修正后保存。|Some nodes are invalid or there are more than 200. Fix them before saving.
格式：请保存只含 proxies 列表的 YAML，不支持锚点、标签或多文档。|Format: save YAML containing only a proxies list, without anchors, tags, or multiple documents.
YAML 内容为空或超过大小限制。|YAML is empty or exceeds the size limit.
Base64 节点列表|Base64 node list
节点链接|Node links
内置核心缺失，请重新安装或完整解压流枢。|Bundled cores are missing. Reinstall or extract the full package.
请等待当前操作完成。|Wait for the current operation to finish.
订阅链接必须是 HTTPS，且不能包含账号密码。|Subscription URLs must use HTTPS and cannot contain embedded user credentials.
请输入有效的 HTTP(S) 地址。|Enter a valid HTTP(S) URL.
下载失败：服务商返回的跳转地址无效。|Download failed: the provider returned an invalid redirect.
下载失败：服务商跳转到了不安全或带用户信息的地址；只允许 HTTPS。|Download failed: redirect blocked. Only HTTPS URLs without embedded user information are allowed.
下载失败：响应超过大小限制。请索取精简的节点订阅。|Download failed: response exceeds the size limit. Request a smaller subscription from the provider.
下载失败：订阅重定向次数过多。|Download failed: too many redirects.
网络：无法解析订阅服务器域名，请检查 DNS 或代理。|Network: DNS could not resolve the subscription server. Check DNS or proxy settings.
网络：无法解析当前代理的地址，请检查代理设置。|Network: DNS could not resolve the proxy. Check proxy settings.
证书 / TLS：与订阅服务器建立安全连接失败，请检查服务商证书和系统时间。|TLS: a secure connection could not be established. Check the server certificate and system clock.
网络：下载订阅超时，请检查网络或当前代理后重试。|Network: download timed out. Check your connection or proxy.
网络：下载订阅超时。|Network: subscription download timed out.
网络：无法连接订阅服务器或当前代理。|Network: unable to connect to the subscription server or proxy.
网络：未能完整下载订阅，请检查网络或当前代理后重试。|Network: the download did not complete. Check your connection or proxy.
网络：读取订阅响应时连接中断，请检查网络或代理后重试。|Network: the connection closed while reading the subscription response.
内容：响应或文件为空，没有可导入的配置。|Content: the response or file is empty.
内容：配置超过大小限制。|Content: the configuration exceeds the size limit.
内容：收到网页或 XML，而不是订阅配置。请检查是否复制了登录页、分享页或遇到服务商验证。|Content: the provider returned a web page or XML, not a configuration. Check for a login, share page or verification step.
内容：收到 JSON 接口消息，不是节点配置。请在服务商后台检查订阅状态。|Content: the server returned a JSON message, not a configuration. Check the subscription with the provider.
格式：此 JSON 不含受支持的 outbounds。请导入 Clash/Mihomo YAML 或节点列表。|Format: this JSON contains no supported outbounds. Try Clash/Mihomo YAML or a node list.
格式：JSON 语法有误。|Format: invalid JSON syntax.
格式：JSON 超过解析限制。|Format: JSON exceeds the parser limits.
格式：服务器响应不是有效的 UTF-8 文本。|Format: the response is not valid UTF-8 text.
格式：订阅响应不是有效的 UTF-8 文本，请向服务商索取 YAML 或节点订阅。|Format: the response is not valid UTF-8. Ask the provider for YAML or a node list.
格式：内容不是有效的订阅 YAML，或使用了不支持的锚点、标签或多文档。|Format: invalid YAML or unsupported YAML anchors, tags, or multiple documents.
格式：未找到 Clash/Mihomo proxies、JSON outbounds 或受支持的节点列表。|Format: no Clash/Mihomo proxies, JSON outbounds, or supported node list was found.
兼容性：此配置只有 proxy-providers，没有内联节点；流枢不会自动下载配置引用的其他订阅。请向服务商索取含 proxies 的版本。|Compatibility: this file only references proxy providers. Ask for a configuration with inline proxies.
兼容性：已识别 YAML 节点列表，但节点类型或参数无效或暂不支持。|Compatibility: YAML nodes were found, but their types or parameters are invalid or unsupported.
内容：订阅的 YAML 节点列表为空。|Content: the YAML node list is empty.
只支持单一内核格式的 JSON outbounds 列表。|Only a single core format per JSON outbounds list is supported.
没有通过校验的代理出站。规则、监听器、直连和不支持的出站不会导入。|No supported proxy outbounds passed validation. Routing rules, listeners, direct and unsupported outbounds are not imported.
`.trim().split('\n').map(line=>line.split('|')));
const translationRules=[
  [/^(\d+) 个节点(?: · 跳过 (\d+) 条)?$/,(m,n,s)=>n+' nodes'+(s?' · '+s+' skipped':'')],
  [/^已测 (\d+) \/ (\d+)$/,(m,n,d)=>'Tested '+n+' / '+d],
  [/^已完成 (\d+) 个节点测速$/,(m,n)=>'Tested '+n+' nodes'],
  [/^本轮完成 (\d+) \/ (\d+) 个节点测速$/,(m,n,d)=>'Tested '+n+' / '+d+' nodes'],
  [/^当前版本 (\S+) → 新版本 (\S+)$/,(m,v,n)=>'Current '+v+' → New '+n],
  [/^当前版本 (\S+)$/,(m,v)=>'Current version '+v],
  [/^可更新至 (\S+)$/,(m,v)=>'Update available: '+v],
  [/^已复制 (\S+)$/,(m,v)=>'Copied '+v],
  [/^复制本机代理地址 (\S+)$/,(m,v)=>'Copy local proxy address '+v],
  [/^本机代理端口 (\d+) 已被占用。请关闭占用程序，或在设置中更换端口。$/,(m,p)=>'Local proxy port '+p+' is already in use. Close the other app or choose another port in Settings.'],
  [/^本机代理端口 (\d+) 无法监听。请检查端口是否被占用或保留，或在设置中更换端口。$/,(m,p)=>'Cannot listen on local proxy port '+p+'. Check whether it is in use or reserved, or choose another port in Settings.'],
  [/^(已响应|服务器或代理错误|访问受限|未知响应) · (\d+)$/,(m,label,code)=>english.get(label)+' · '+code],
  [/^移除 (.+)$/,(m,site)=>'Remove '+site],
  [/^proxycheck\.io(：[^·]+)?(?: · 风险 (\d+)\/100)?$/,(m,flags,score)=>'proxycheck.io'+(flags?': '+flags.slice(1).trim().replace('未见四类标记','No flags in four categories').replace('已返回项目未见标记','No flags in reported categories').replace('代理','Proxy').replace('托管网络','Hosting network'):'')+(score?' · Risk '+score+'/100':'')],
  [/^兼容性：识别到节点链接(?:（([a-zA-Z0-9、其他]+)）)?，/,(m,p)=>'Compatibility: node links detected'+(p?' ('+p.replaceAll('、',', ').replace('其他','other')+')':'')+', but their parameters, protocols or transports are invalid or unsupported. Try Clash/Mihomo YAML.'],
  [/^服务端：HTTP (\d+)/,(m,c)=>'Server: HTTP '+c+'. '+({401:'Check the subscription credentials.',403:'Access denied. Check the link with the provider.',404:'The subscription was not found.',406:'No configuration was provided. Try a Clash/Mihomo URL or import a file.',410:'The link is no longer available.',429:'Too many requests; try again later.'}[c]||(Number(c)>=500?'The service is unavailable; try again later.':'No configuration was returned.'))]
];
