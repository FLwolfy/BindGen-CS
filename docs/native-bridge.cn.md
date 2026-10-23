# 原生桥接

BGCS 从中立原生事实规划 C/C++ 桥接，只输出源码和要求。消费项目负责把这些源码编译、链接并提供原生模块。

## 调用与存储

默认 `pointerBridge` 将按值记录输入转换为类型化指针，记录结果转换为类型化输出存储。标量和普通指针本身不要求增加桥接。显式 `direct` 必须具有完整适用的 ABI 计划，大小相同不足以放行。

原生桥内部使用真实源语言完成原调用。C++ 对象遵循构造、析构和所有权语义，不作任意字节复制。桥接导出属于一个明确模块；清单记录原声明到物理导出的映射，不猜二进制文件名或链接命令。

C helper、固定可变参数 adapter、自定义类型和 callable lowering 进入相同类型化计划。源码片段明确声明依赖、摘要和契约。生成 include 采用逻辑输入身份，不回退工作区绝对路径。

## C++

已支持范围包括构造、析构、方法、运算符、显式模板实例、原生继承 cast、标准库值协议、recipe 和 shim。string/vector/span/array、map/set、optional/variant/expected、path/chrono、unique/shared pointer 各有自己的生命周期规则。`vector<bool>` 不能当作普通连续 bool 数组。

C++ 异常在原生边界内部按 status/error/result 协议捕获。allocator 和释放函数明确；转换失败必须清理已经创建的中间对象。

## 回调

反向回调有自己的调用计划。aggregate 参数或结果可能需要 native adapter、精确的 user-data 关联和结果输出参数。raw 回调每次调用都必须初始化结果通道，包括失败路径。若配置无法保证该行为，友好 delegate 包装会给出省略原因，不能擅自返回零值。

call-scoped adapter 不能被 native 保留。长期上下文需要真实注册 handle，以及明确的 unregister/join 协议，保证不再开始新调用且原生帧全部退出。取消请求不能替代该保证。上下文、thunk、buffer 和 module lease 持续到协议确认静止。

## 验证与消费

frontend 复核生成桥头的签名；verifier 检查名称、参数/结果、存储偏移和回调通道，并通过稳定身份合并语义。C/C++ 静态断言由消费项目编译检查，BGCS 不执行它们。

[源码要求](native-source-inputs.cn.md)说明逻辑根映射、模块/导出绑定和输入摘要。能力报告说明 raw/friendly 结果与消费项目仍需承担的工作。
