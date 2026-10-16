# 原生平铺标识符命名

BGCS 默认保留现有命名行为。只有明确选择词典分词时，才对全大写平铺词启用
最长前缀匹配。这只改变托管名称，不改变原生导出名、类型身份、布局或调用约定。

```json
{
  "api": {
    "naming": {
      "flatIdentifierSegmentation": "english",
      "functions": { "casing": "pascal" }
    }
  }
}
```

固定英文词典将 `CREATEWINDOW`、`DEPTHBUFFER`、`GETMOUSEPOS` 转换为
`CreateWindow`、`DepthBuffer`、`GetMousePos`。每个位置选取最长匹配词，未知片段
与数字完整保留在结果中。全小写和混合大小写词继续采用现有大小写边界。
精确的 `api.names`、`casing: "preserve"` 会绕过分词；未指定类别 casing 时也保持
现有行为。

项目词表使用 `flatIdentifierSegmentation: "custom"`，并提供
`flatIdentifierWords`，例如 `["create", "window", "mouse", "pos"]`。词表需要
1–4096 个仅含字母的词，每词最多 128 个 UTF-16 字符，忽略大小写后不得重复。
空词、重复词、标点与数字会导致验证失败。`none` 或 `english` 模式也不能同时
提供自定义词表。默认模式是 `none`。

分组配置与 `ManagedProjectionRequest` 使用同一个不可变 `ManagedNamingPolicy`。
每次投影拥有自己的自定义索引；英文索引从内嵌且校验固定摘要的版本化资源加载，
全进程只共享不可变索引，不搜索磁盘。生成缓存包含完整配置及后端程序集字节，
因此修改自定义词表或更新内置词典都会使旧命名缓存失效。
