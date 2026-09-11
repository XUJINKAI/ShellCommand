> **v2 重设计契约（feat/redesign-v2）**：本分支按 [已确认设计](../redesign-v2.md) 分阶段重写；下文是重设计前的历史基线，不再对新实现构成兼容性要求。仅接受 YAML v2，不导入或迁移旧配置。实施与验证进度见 [P0](../p0-validation.md)。

# Broker IPC Contract

## Purpose

Explorer DLL 与 ShellCommand App 通过本机 IPC 访问 Broker。

Explorer path 的协议必须：

- 极小；
- 无网络；
- bounded；
- versioned；
- native C++ 易实现；
- 不依赖 CLR/JSON/YAML parser；
- 对 malformed input 安全。

V11.0 使用 **Named Pipe + length-prefixed binary protocol**。

## Pipe

逻辑名称：

```text
\\.\pipe\ShellCommand11.<UserIdentity>
```

实现可用当前用户 SID 的安全 hash 代替原文 SID，但必须做到每用户隔离。

ACL：

- 当前用户：允许；
- SYSTEM：允许；
- 其他普通用户：拒绝。

不允许 remote pipe access。

## Framing

每个 frame 使用 little-endian 固定头：

```text
offset size  field
0      4     magic = ASCII "SC11"
4      2     protocolVersion = 1
6      2     messageType
8      4     requestId
12     4     payloadLength
```

Header 总长 16 bytes。

限制：

```text
max payload = 256 KiB
```

接收方必须先验证 magic/version/type/length，再分配 payload。

## String Encoding

所有 string：

```text
uint32 byteLength
UTF-8 bytes
```

- 不带 NUL；
- 必须是合法 UTF-8；
- 单 string 最大 32 KiB bytes；
- Windows 路径允许 Unicode。

## Message Types

```text
1  PingRequest
2  PingResponse
10 ResolveMenuRequest
11 ResolveMenuResponse
20 InvokeRequest
21 InvokeAccepted
30 OpenAppRequest
31 OpenAppAccepted
```

未知 type 返回错误或直接关闭连接，不尝试猜测。

## ResolveMenuRequest

Payload：

```text
string workingDirectory
```

要求：

- absolute filesystem directory path；
- Broker 重新规范化；
- 不信任 Explorer 传入路径；
- 不允许 payload 携带 YAML 或 command line。

## ResolveMenuResponse

```text
uint8 status
uint16 itemCount
MenuItem[itemCount]
```

status：

```text
0 OK
1 NO_COMMANDS
2 INVALID_REQUEST
3 BUSY
4 INTERNAL_ERROR
5 UNSUPPORTED_VERSION
```

### MenuItem

```text
uint8 kind       // 0 Action, 1 Separator
uint8 flags      // bit0 Enabled; other bits must be 0 in V1
uint16 reserved  // must be 0
byte[16] token   // ActionToken; zero for Separator
string title
string iconRef
```

限制：

```text
itemCount <= 100
```

`title` 对 Separator 必须为空。

V11 正常解析得到的 Action 都应 enabled；保留 Enabled 位仅用于 built-in/future state，不用它表达 Match false。

## ActionToken

- 128-bit opaque random token；
- Broker 生成；
- Explorer 不解析；
- 绑定当前 Broker session 中的 immutable action plan 和 working directory；
- 默认 TTL 2 minutes；
- token 一次或多次调用语义由 Broker 防重复策略决定，V11 普通命令允许单次成功 accept；
- Broker 重启后旧 token 失效。

Broker 在菜单打开与点击之间异常重启时，旧 token 执行失败是允许的安全失败；不得尝试执行可能已经变化的“同索引命令”。

## InvokeRequest

```text
byte[16] token
```

不重复传输 command line。

## InvokeAccepted

```text
uint8 status
```

```text
0 ACCEPTED
1 TOKEN_NOT_FOUND
2 TOKEN_EXPIRED
3 BUSY
4 INTERNAL_ERROR
```

`ACCEPTED` 表示 Broker 已接管动作，不表示子进程执行成功。

Explorer 收到 ACCEPTED 后立即返回 `Invoke`。

## Explorer Time Budget

总 IPC hard deadline 见 `patterns/ipc-and-time-budget.md`。

协议本身禁止：

- Broker 让 Explorer 无限等待；
- chunked streaming menu；
- 回调 Explorer；
- network proxy；
- negotiation loops。

V11 每次 `EnumSubCommands` 最多一个 Resolve round-trip。

## Connection Strategy

V11 推荐短连接：

```text
connect
write one request
read one response
close
```

原因：

- 简化 COM object 生命周期；
- Broker 重启恢复简单；
- 不在 Explorer DLL 维护复杂连接状态。

若性能测量证明短连接成本显著，未来可在不改变 wire message semantics 的前提下增加连接复用。

## Malformed Data

Explorer 收到以下情况必须立即 fail invisible：

- wrong magic；
- wrong version；
- length overflow；
- invalid UTF-8；
- itemCount > limit；
- token missing；
- trailing malformed bytes；
- Broker closes early。

不能弹 MessageBox。

Broker 对 App/Explorer 请求同样做完整验证。
