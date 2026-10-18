# 宿主、目标与私有 SDK 扩展

[文档](README.cn.md) · [架构](architecture.cn.md) · [普通扩展](extensibility.cn.md)

宿主是运行 BGCS/worker 的机器；目标是头文件和生成绑定采用的 CPU/platform/ABI。
`BindingHost` 是本地生成协调器，不是服务器。省略目标时按宿主解析，Linux GNU 和 musl
保持区别；显式 `targets[].id` 可以选择不同目标。

## 谁提供什么

| 输入/层 | 职责 |
| --- | --- |
| 运行宿主 | 选择匹配的 parser/worker 资源 |
| 目标请求/provider | 映射目标身份、triple、字节序及能力描述 |
| 用户 SDK/header 输入 | 提供真实 sysroot、系统/C++ 头文件、宏和必要参数 |
| Clang frontend | 按目标输入观察声明、类型、布局和编译器事实 |
| Analysis/Cpp2C/CSharp | 判断 ABI/语义资格，生成 C ABI bridge 与托管源码 |
| 消费者构建 | 编译、链接、打包、签名、部署及实机调用 |

provider 不替用户下载 SDK，也不通过目标名称证明布局或按值调用。Clang builtin headers
只补充 parser 的编译器支持文件，不能替代 NDK、Windows SDK、libc 或 C++ 标准库。
显式 SDK 输入具有权威性，不能偷偷混入另一套宿主头文件。

## Windows → Android ARM64

以下是解析输入示意，需替换为自己实际安装的 NDK 路径和 API level：

```json
{
  "schemaVersion": 1,
  "library": { "id": "mobile", "nativeName": "mobile" },
  "source": { "language": "c", "headers": ["include/mobile.h"] },
  "targets": [{
    "id": "android-arm64",
    "triple": "aarch64-linux-android24",
    "settings": {
      "sysRoot": "C:/Android/ndk/your-version/toolchains/llvm/prebuilt/windows-x86_64/sysroot",
      "systemIncludeDirectories": [
        "C:/Android/ndk/your-version/toolchains/llvm/prebuilt/windows-x86_64/sysroot/usr/include",
        "C:/Android/ndk/your-version/toolchains/llvm/prebuilt/windows-x86_64/sysroot/usr/include/aarch64-linux-android"
      ]
    }
  }],
  "api": { "namespace": "Mobile.Bindings", "name": "MobileApi" }
}
```

Windows worker 读取 Android headers 并生成 Android ARM64 事实，不切换成 Android parser
二进制。C++ 还需准确的 NDK libc++ 头文件及参数。生成器不编译应用；Android 原生编译、
链接、runtime 选择、部署和设备验证由消费者负责。跨目标解析成功不认证实机调用。
公开 `AndroidNdkDiscovery.Resolve(ndkRoot, architecture, apiLevel)` 可从明确已安装 NDK
构造只读描述，但不会安装 SDK 或执行原生编译。

## 私有或特殊 SDK

普通插件只引用 `BGCS.Contracts`，实现 `INativeTargetProvider.TryResolve`，经
`IBindingPlugin` / `ExtensionPort.NativeTargetProvider` 注册会话工厂，或在 SDK 通过
`AddTargetProvider` 注册。未知 ID 返回 false；多个 provider 认领同一 ID 必须拒绝。
不要共享可变会话实例。

完整范例见 [CustomTargetPlugin](../examples/CustomTargetPlugin/README.md)：私有 SDK
描述、真实 header、插件 manifest 与来源指纹各自明确。CLI/工作区通过
`extensions.plugins` 读取锁定 manifest；SDK 可直接组合工厂。扩展读取的外部输入需
声明并提供 `ICacheFingerprintProvider` 指纹，否则不能声称缓存可复用。

未知平台须明确能力并选择已有的受支持 runtime 模型；目标 provider 不是任意新 runtime
或 ABI 分类器框架。它不能让未知按值结构、特殊寄存器分类、异常/线程协议自动安全，
也不能自动提供专有 SDK。Switch 等专有平台只能在合法私有 SDK、实际 ABI 与 runtime
支持成立后由项目自行集成，不承诺零配置支持。

动态/静态能力不决定模块名。生成器保留 `library.nativeName`；消费者应按该名称注册或
链接实际模块。`__Internal` 不从静态能力推断。Wasm 静态 adapter 与不可阻塞清理协议
不等于桌面动态加载；见[外部编译](native-metadata.cn.md#外部编译)。

## 验证边界

必需 CI 是 Windows 完整生成器、Linux/macOS smoke 和一个完整产品安装任务，原各宿主
性能预算保持不变。AOT/Wasm 应用构建/调用是可选手动私有检查；目标事实、能力拒绝、
源码生成仍属核心功能。没有相应实机证据时，不能把目标描述升级为平台认证。

工程组织以[架构](architecture.cn.md)及
[工程依赖策略](../eng/architecture/project-dependencies.json)为准：十一个产品工程，
Contracts/Runtime 独立，ordinary lowering 不依赖 frontend；测试和 examples 独立组织。
