# 验证记录与正式发布验收

此分支已实现 v2 配置、隔离准备、内存菜单快照、多选/子菜单、编辑预览、独立执行与可恢复菜单管理。按用户后续指示，人工 Windows 11 检查是正式发布验收，不阻塞后续编码。

## 本阶段契约

- 所有托管发布入口 `PublishSingleFile=true`、`SelfContained=false`，依赖 .NET 10 Desktop Runtime x64。少文件 ZIP 允许 native DLL 与身份资源独立存在。
- 固定数据根 `%LOCALAPPDATA%\ShellCommand11`，完整运行资源复制到 `runner/<内容构建标识>/`；配置从不从下载目录发现或迁移。
- 注册和自启动只引用 runner。安装失败恢复之前的自有注册与启动记录；卸载入口不依赖 Broker、YAML 或来源目录。
- IPC 版本在本分支成套更新，无旧协议适配；命名空间为 SID + SessionId。
- 原生请求的绝对 30ms 截止覆盖工作入队、连接、编码、读写、解析；Windows 调度耗时单独测量，不承诺整个系统菜单 30ms。
- 请求最多 8 个工作线程。超时只放弃结果；I/O、OVERLAPPED、事件、管道、缓冲区及 DLL 引用保留到 Windows 确认完成。取消回收在工作线程发生，绝不在菜单线程无限等待。
- 原生验证程序用真实命名管道注入延迟、半包、断连、错误请求 ID、超长数据，并检查反复打开后的句柄和 pending 配额。

## 放行条件

自动化结果与人工结果必须分别记录。CI 编译/测试成功不等于 Explorer/Surrogate 实机验证成功。

- [x] Windows CI：托管与 native 构建、故障注入、发布 ZIP 结构与无运行时验证（见下方记录）。
- [ ] 干净 Windows 11（开发者模式关闭）：受信任签名身份包安装、右键调用、卸载。
- [ ] 安装后删除解压来源，程序与菜单仍正常。
- [ ] 获取用户卡死现场或等价复现的线程/转储；确认实际 Surrogate 承载位置。
- [x] ASan 原生模拟取消竞态及 10000 次请求回收。
- [ ] 实际 Explorer 10000 次菜单、Application Verifier、端到端性能实机验证。
- [ ] 签名证书及分发信任方案确定；开发用 Register manifest 不能冒充正式安装。

自动化覆盖配置到执行计划、真实 IPC、原生故障边界以及打包后执行器的参数、工作目录、环境变量和输出排空。实际 Explorer 行为仍需要人工验收。

## 签名构建

普通 CI 附件为未签名开发包，不应分发为正式版。打包后使用 `packaging/scripts/Sign-Identity.ps1 -Directory <stage> -CertificateThumbprint <thumbprint> -TimestampUrl <RFC3161 URL>` 生成并签名身份包，然后调用 Create-Zip。私钥仅从当前用户证书存储读取，不写入仓库；脚本不安装信任证书、不改变开发者模式。证书 Subject 必须匹配 manifest Publisher。正式证书和目标机器信任必须由发布方提供。

外部目录核验使用 Windows `GetPackagePathByFullName2(..., PackagePathType_EffectiveExternal)`，不查询或修改内部注册数据库。签名身份包的生成和 ExternalLocation 注册依据 [Microsoft 官方说明](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/grant-identity-to-nonpackaged-apps)。

## 干净 Win11 测试入口

`packaging/scripts/Smoke-Install.ps1 -PackageDirectory <signed-package-directory>` 在无现有 ShellCommand 注册的测试用户中运行：正式安装 → 删除下载副本 → 从 runner 做真实 IPC/注册检查 → 卸载 → 验证配置哨兵仍在。它要求受信任签名包，不自动导入证书。该脚本仍不能代替真实 Explorer 菜单、Surrogate 加载位置与用户卡死场景的人工观察。

## 自动化证据（2026-09-11）

[Windows CI 运行 34563502105](https://github.com/XUJINKAI/ShellCommand/actions/runs/34563502105) 已通过托管测试、原生故障注入、ASan、两入口无运行时单文件发布、native DLL 编译和 ZIP 校验。首次原生测试的 10000 次无 Broker 请求前后句柄数为 123 → 123。这不是 Explorer 中 10000 次实际菜单的测量，也没有据此宣称 p95/p99 达标。

当前环境没有干净 Windows 11 桌面、发布签名证书或用户卡死现场转储。以上历史 CI 仅证明当时的 P0 代码；v2 完整实现的最新验证结果以本分支 PR 中链接的 CI 为准。未执行的人工项目保持未勾选，不据此停止开发。
