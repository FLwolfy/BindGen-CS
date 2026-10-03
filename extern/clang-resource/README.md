# Clang builtin resource headers

`Headers.zip` contains the unmodified, platform independent builtin headers from
LLVM/Clang **20.1.8**. BGCS.CppAst uses the Clang **20** native parser and embeds
this same-series bundle. Host driver discovery supplies SDK and standard library
paths; it must not supply another Clang release's builtin headers.

Source: [official LLVM APT repository](https://apt.llvm.org/bookworm/pool/main/l/llvm-toolchain-20/),
`libclang-common-20-dev_20.1.8~++20250708063551+0c9f909b7976-1~exp1~20250708183702.136_amd64.deb`.
Only `/usr/lib/llvm-20/lib/clang/20/include/` regular files are included, with the
`include/` relative prefix and deterministic ZIP timestamps. The Debian package
architecture does not limit the headers: the bundle includes the generated
ARM, x86, WebAssembly and other target builtin headers.

- Source package SHA-256: `8e2b5e6530df9835c571db4c12a278f43863d3401ae7008dd9739e4b9c62bc2e`
- ZIP SHA-256: `16927be0948f2e287d2f20e34e8f8ce0ee287b833ee04cb7cd671fdf4a7c2b84`
- License: Apache-2.0 WITH LLVM-exception; see `LICENSE.txt`.

The parser extracts the immutable embedded bundle into a content-addressed user
cache. No host installation, PATH change or Web SDK dependency is required.
When updating the parser's LLVM major release, update this bundle and run the
host SDK, standard library and cross-target parser tests together.
