# Native shim example

From this directory, after building or installing the `bindgen-cs` tool:

```bash
bindgen-cs bridge bridge.json

# Windows
bindgen-cs native-build GeneratedBridge/bridge.manifest.json --output Consumer/bin/Debug/net9.0/widget_bridge.dll

# Linux
bindgen-cs native-build GeneratedBridge/bridge.manifest.json --output Consumer/bin/Debug/net9.0/libwidget_bridge.so

# macOS
bindgen-cs native-build GeneratedBridge/bridge.manifest.json --output Consumer/bin/Debug/net9.0/libwidget_bridge.dylib

dotnet run --project Consumer/Consumer.csproj
```

Run only the native-build command for the current platform. The program prints `42`. See the [C++ extension cookbook](../../docs/cpp-extension-cookbook.md) for package layout, lifetime rules, and Windows/macOS/Linux notes.
