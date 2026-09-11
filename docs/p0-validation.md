# P0：安装与 Explorer 故障边界

这是重设计开发分支，不是 v2 完成版。当前优先验证两个纵向闭环，P0 放行前不继续叠加 YAML/UI 功能。

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

- [ ] Windows CI：托管与 native 构建、故障注入、发布 ZIP 结构与无运行时验证。
- [ ] 干净 Windows 11（开发者模式关闭）：受信任签名身份包安装、右键调用、卸载。
- [ ] 安装后删除解压来源，程序与菜单仍正常。
- [ ] 获取用户卡死现场或等价复现的线程/转储；确认实际 Surrogate 承载位置。
- [ ] Application Verifier / ASan 取消竞态、10000 次菜单、句柄/内存无持续增长。
- [ ] 签名证书及分发信任方案确定；开发用 Register manifest 不能冒充正式安装。

P1/P2/P3 的 v2 YAML、预览、多选、子菜单、系统菜单事务重写尚未在本阶段声明完成。
