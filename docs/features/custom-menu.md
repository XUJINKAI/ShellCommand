# 自定义菜单

配置规则见 [完整 v2 设计](../redesign-v2.md) 第 6–7 节和 [Schema](../contracts/config.schema.json)。

本地定义按 ID 完整覆盖全局定义，不深合并；disabled 定义仍可屏蔽全局项。本地顺序在前。默认动作适用于背景；显式 context/selection 条件和祖先条件决定选择项适用性。

支持 run、script、open、copy 和两层自定义 items 分组。空分组隐藏；首尾及相邻分隔线消除；合并超过 100 个节点时给出诊断并退化为设置入口，不静默截断命令。

Core 不做 I/O。exists 仅匹配已准备的直接子项名称；事实未知时遵守三值逻辑。列表变量仅能独占 args 元素或 copy.values，each 的 item 变量逐项冻结。相对资源始终相对定义它的配置文件，包含文件保留自己的来源路径。
