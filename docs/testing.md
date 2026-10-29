# 测试与宿主验收

插件依赖通过 nuget.org 的 MioKit `1.3.1` 包还原，版本集中在 `Directory.Build.props`。当前解决方案包含插件和 .NET 测试工程，不需要 MioKit 宿主源码。Preview 工程、对应浏览器测试脚本及 `MioKit.Runtime` 项目引用已移除。

2026-10-25 已验证 NuGet 依赖还原、完整解决方案 Release 构建和 337 个 .NET 用例通过，结果为 `artifacts/test-results/nuget-1.3.1.trx`。本次未重新执行真实宿主界面验收。

2026-10-26 修复 Debugger 默认数据目录误判：`Miokit.Debugger --path <插件输出目录>` 会把 `app_data` 放在插件目录内，不能将自动生成的网站 Profile 当成用户自定义目录检查保留位置。空字符串、空白和 null 均使用默认目录；只有显式填写的目录执行保留位置、文件和链接校验，所有网站仍执行目录重叠检查。新增 Debugger 布局下三轮打开方式切换和显式目录清空恢复默认的回归用例；修复前 3 个空值用例均复现原报错，修复后 Debug 配置的 345 个 .NET 用例通过（`artifacts/test-results/debugger-default-after.trx`）。本轮未执行真实 Debugger 界面验收。

## 运行

需要 Windows、.NET 10 SDK、Node.js 和 pnpm。前端 VM 模块测试建议使用 Node.js 24；本机 Node.js 20.19.2 曾发生原生进程崩溃，该运行不能记作通过。生成脚本测试也会调用 PATH 中的 `node`。

```powershell
# 仓库根目录
pnpm --dir plugin/vue-ui install --frozen-lockfile
pnpm --dir plugin/vue-ui test
pnpm --dir plugin/vue-ui build

dotnet restore MioKit.WebSearch.slnx
dotnet build MioKit.WebSearch.slnx -c Release
dotnet test tests/MioKit.WebSearch.Tests.csproj -c Release --no-build --logger "trx;LogFileName=release-tests.trx" --results-directory artifacts/test-results
```

前端构建会重建 `plugin/ui/dist`，应在 .NET 构建前完成，避免构建时复制已经替换的资源。

## 当前自动化范围

| 范围 | 检查内容 | 入口 |
| --- | --- | --- |
| 搜索与查询 | 固定网址、模板、剪贴板策略、URL 编码与调用参数 | `tests/WebsiteSearchInteractionTests.cs`、`tests/WebsiteUrlResolverTests.cs` |
| 配置与目录 | 默认值、配置变化、版本和权限校验、独立目录、保留位置、目录联接边界 | `tests/WebsitePolicyTests.cs`、`tests/WebsitePolicyValidationTests.cs`、`tests/WebsiteDataDirectoryPolicyTests.cs` |
| 导航、脚本与能力 | 弹窗决策、来源与会话、脚本阶段、权限撤销、节点存储、文件授权、异常回包 | `tests/WebsiteNavigationPolicyTests.cs`、`tests/WebsiteScriptExecutionTests.cs`、`tests/WebsiteCapabilitiesTests.cs`、`tests/WebsiteBridgeTransportTests.cs` |
| 图标与索引 | 图标导入回滚、缓存与网络上限、收藏夹解析、同步和停止 | `tests/WebsiteIconServiceTests.cs`、`tests/Favicon*Tests.cs`、`tests/BrowserIndex*Tests.cs` |
| 编辑器状态 | 加载/保存竞态、草稿保留、保存后读回失败、重开状态与提示清理 | `plugin/vue-ui/tests/editor.test.js` |
| 前端配置与文档 | 字段转换、默认值、数组隔离、延迟加载状态、脚本 API 类型与说明路由 | `plugin/vue-ui/tests/` |

.NET 测试使用内存 Store、临时目录和模拟消息通道；生成脚本由 Node VM 执行。前端测试编译真实 Vue SFC setup 并使用 Vue 响应式，但桥接、路由和展示组件是替代实现，不挂载真实 WebView2 DOM。这些检查不能证明真实界面和完整宿主流程已经通过。

## 实际宿主验收

安装测试包后，通过真实界面完成以下连续操作；验收中应保留同一网站及其窗口状态，不能每一步都重新创建测试数据。

1. 新建在线网页，按默认外部打开保存并从列表打开；再对本地 HTML 完成同样操作。
2. 在同一网站上反复执行“外部浏览器 → 内置窗口 → 外部浏览器 → 内置窗口”，保存后从列表和搜索入口打开，确认按当前配置执行。
3. 保留内置主窗口和子窗口再切换；重新打开后确认旧窗口已经停止，新窗口应用当前配置，不残留过期的重开提示。
4. 在默认目录和自定义目录之间切换，确认各自的浏览器数据保留；修改目录不会迁移旧数据。
5. 配置脚本与权限，切换后检查脚本实际调用成功，而非只检查脚本开始执行或页面出现。
6. 输入无效目录、网址或其他配置，确认失败不覆盖已保存值，并能修正后正常保存、打开。
7. 关闭并重新启动宿主，确认网站、打开方式、脚本、权限及数据目录重新加载正确。
8. 删除网站时确认主窗口和子窗口关闭，节点消失，浏览器目录和用户文件保留；同时验证取消删除。

真实登录、外部浏览器、原生文件选择器、摄像头/麦克风等硬件权限、插件卸载和宿主退出也需要在实际宿主中检查。

## 历史记录

移除 Preview 前的验证结果及报告路径保留在 [testing-history.md](testing-history.md)。这些结果只代表对应版本的历史验证，不代表当前 NuGet 构建已重新执行真实窗口测试。
