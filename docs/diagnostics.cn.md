# 诊断说明

`bindgen-cs explain` 列出稳定的 BGCS 诊断目录。`bindgen-cs explain CODE`
输出原因与处理方法；增加 `--json` 获得结构化字段。完整的
[生成参考](generated/diagnostic-reference.cn.md)与可执行命令使用同一份已审核目录。

具体诊断还包含本次请求的源码位置、目标、阶段或编译器详细消息，需要与目录说明
一起阅读。Clang 消息通过 `BGCS-FRONTEND-CLANG` 转发，目录说明不能替代原始解析
消息。未生成的易用包装和不支持的对象宏会明确报告；正确的 raw/native 事实是否保留，
按对应结果说明判断。

新增诊断时，在 `eng/diagnostics/catalog.json` 中补充中英文，再运行
`python scripts/generate-diagnostic-docs.py`。`--check` 校验生产代码诊断 ID 与所有
生成参考。测试还将实际编译的目录和 CLI JSON 与审核源比较；新增生产诊断但没有
可执行解释，会导致验收失败。
