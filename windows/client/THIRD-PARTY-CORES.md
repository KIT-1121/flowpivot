# Bundled third-party cores

FlowPivot invokes unmodified upstream binaries as separate processes. These binaries and their notices are in `cores/`. FlowPivot is not affiliated with or endorsed by the core projects.

| Component | Release | Upstream source, build scripts and dependency declarations | License in this package |
| --- | --- | --- | --- |
| Mihomo | v1.19.32 | https://github.com/MetaCubeX/mihomo/tree/v1.19.32 | `cores/Mihomo/GPL-3.0.txt` |
| Xray | v26.10.31 | https://github.com/XTLS/Xray-core/tree/v26.10.31 | `cores/Xray/LICENSE.txt` |
| sing-box (including its shipped libcronet.dll) | v1.14.4 | https://github.com/SagerNet/sing-box/tree/v1.14.4 | `cores/sing-box/LICENSE.txt` and `cores/sing-box/GPL-3.0.txt` |
| V2Fly | v5.53.0 | https://github.com/v2fly/v2ray-core/tree/v5.53.0 | `cores/V2Fly/LICENSE.txt` |

Builds stage the executables and runtime DLL from hash-pinned official release ZIPs. Tag links above identify the exact upstream versions; they are not a pre-publication audit of all corresponding source, build dependencies, or notices. This is for local testing. Before public distribution, verify all applicable notices (including those of libcronet and its dependencies), and make complete corresponding source available as required for the shipped builds.
