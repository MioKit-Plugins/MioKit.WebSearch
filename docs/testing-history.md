# 历史验证记录

本页保留移除 Preview 工程前的测试记录。下文的 Preview 路径、源码依赖和运行命令均为历史信息；当前测试入口见 [testing.md](testing.md)。

# 测试范围与验收边界

2026-10-25 打开方式连续切换审计：此前规则单测与若干单次保存检查没有验证同一网站的旧窗口、子窗口、当前配置和浏览器 Profile 在连续操作后是否一致。新增真实界面回归在修复前复现“外部 → 内置 → 外部 → 内置后再次打开仍复用已失效窗口”（`artifacts/browser-tests/20261025-154409/report.json`）；新增前端回归复现关闭旧窗口后保存仍残留重开提示。现在普通打开会替换失效的主/子窗口，等候 WebView2 停止；同一网站的打开与重开按顺序执行；编辑页使用服务端最新读回状态清除过期提示。

专项测试为在线与本地网页各一段连续用户流程：从新建开始配置脚本与 storage 权限，真实点击切换并保存三轮、实际加载网页、检查脚本调用结果及浏览器 localStorage、保留子窗口后切换、重叠打开/重开、手动关闭后保存、自定义目录往返、错误目录被拒绝后修正、从 SQLite 重新创建同一节点后再切换，最后删除并检查目录仍保留。在线内容来自测试自有回环 HTTP 服务，外部浏览器在进程启动边界检查地址，不启动用户浏览器。测试宿主现使用真实 RootNode 与挂载/卸载事件，所有新建与重新加载节点均须真实挂树；不能再仅设置单个节点的挂树标记或把脚本开始执行当作授权调用成功。报告脚本也会拒绝复用进程异常退出前的旧报告。

本轮最终结果：**337 个 .NET 用例、35 个前端用例、93 项真实 WebView2 检查通过**，完整 Release 构建成功。93 项包含上述两段较长的连续流程，不代表 93 个独立端到端流程。报告为 `artifacts/browser-tests/20261025-155842/report.json`（同目录 `report.html` 与在线/本地截图），.NET 结果为 `artifacts/test-results/mode-switching.trx`，前端结果为 `artifacts/mode-switch-frontend.log`。已查看两种网站的切换截图。真实外部浏览器登录与完整宿主进程重启仍在既定人工验收边界内。

2026-10-25 外部打开切换内置窗口的目录联接回归：数据目录校验以宿主提供的 `PluginDataPath` 为可信边界，不再因该根目录或其祖先的目录联接而误拒绝默认 Profile；根目录下的网站目录及自定义外部目录仍拒绝链接。新增四个真实目录联接用例，修复前两个合法宿主路径均复现“数据目录不能使用系统保留位置或链接路径”，修复后 **337 个 .NET 用例通过**（`artifacts/test-results/embedded-switch.trx`）。真实设置页从新增普通网页开始，完成外部保存、切换内置窗口保存和切回外部；额外经真实 WebView2 启动目录联接下的默认 Profile。完整窗口回归 **91/91 项检查通过**，Release 构建成功，报告为 `artifacts/browser-tests/20261025-153544/report.json`，已查看模式切换截图。

2026-10-25 新增网站保存后读回失败回归：收到保存成功结果后立即保留网站 ID；随后读回失败时保留草稿，并明确提示已保存、再次保存会更新同一网站。新增前端测试覆盖连续两次读回失败后的重试不重复创建、读回挂起时保留 ID、真正保存失败不接收未提交 ID，以及过期读回失败不影响其他编辑页。修复前新增回归检查失败，修复后 **34 个前端用例通过**。真实设置页额外在桥接边界拒绝一次 `GetWebsiteEditor`，保存仍走真实 C# 服务与 SQLite；实际点击重试并修改名称，验证两次提交的 ID 分别为空与首次成功 ID，内存节点和数据库均仅有一个网站。用户流程专项 **9/9 项检查通过**（8 段操作与 1 项整体检查），报告为 `artifacts/browser-tests/20261025-152354/report.json`，已查看失败提示截图。

该版本完整窗口回归 **90/90 项检查通过**，前端和 Release 构建成功。完整报告为 `artifacts/browser-tests/20261025-152419/report.json`（同目录 `report.html` 含截图）。

验收必须从用户可见入口开始，不能以规则单测或预先创建节点的编辑测试代替新增流程。当前 UI 用户流程从空列表开始，通过真实浏览器鼠标/键盘输入完成操作；节点经宿主 `MioStore`、事件总线、`BatchAccumulator` 和 `DapperMioDataProvider` 写入测试运行目录内的独立 SQLite，不能替换成直接返回成功的持久化 mock。

用户操作覆盖：首次新增在线网页（默认外部打开、不设数据目录、宿主数据路径经过目录联接）、重复保存不重复新增、从列表打开、错误网址拒绝且不覆盖已存值、改为搜索模板并读回中文说明、本地 HTML 新增并用内置窗口打开、取消删除、确认删除并关闭窗口且保留浏览器文件、关闭设置页并从数据库重新创建节点后查看、筛选与清空筛选，以及删除后回到空列表。首次写库失败使用真实 SQLite trigger 拒绝插入，验证不能提示成功或暴露节点、保留用户输入、不留下孤立属性，并可重试成功。

该失败测试曾在修复前明确报错 `first insert was rejected but the UI reported success`（`artifacts/browser-tests/20261025-115601/report.json`），随后发现失败回滚也会排队写入空图标属性；修复后用户流程专项 **8/8 项检查通过**（7 段连续操作与 1 项整体检查，`artifacts/browser-tests/20261025-115705/report.json`）。这些统计不等同于 8 个独立端到端流程。

最终回归：**333 个 .NET 用例、30 个前端用例、89 项真实 WebView2 检查通过**，Release 构建成功。真实窗口报告为 `artifacts/browser-tests/20261025-115742/report.json`（同目录 `report.html` 含截图），.NET 结果为 `artifacts/test-results/user-journeys.trx`。89 项包含上面的用户流程和已有窗口、脚本、权限、弹窗、设置页回归；不代表所有功能均已在完整宿主中端到端验收。

2026-10-25 外部浏览器数据目录回归：**333 个 .NET 用例、30 个前端用例、81 项真实 WebView2 断言全部通过**，前端和 Release 构建成功。外部打开不再解析或校验内置窗口专属目录和高级设置；保留的无效配置在切回内置窗口时拦截，失败不修改已存配置。新增真实设置页保存和读回、默认目录落于真实宿主 `plugin_data/<插件 Id>/data` 层级并启动网页、宿主/安装/数据根目录/共享 Profile/JS runtime/已有文件拒绝、外部模式不创建目录，以及模式切换后活动窗口继续占用旧目录的验证。已查看保存成功和目录校验截图。报告位于 `artifacts/browser-tests/20261025-112847/report.json`，.NET 结果位于 `artifacts/test-results/external-directory.trx`。本轮未实际启动系统外部浏览器，未覆盖完整宿主数据库重启持久化和原生权限人工交互；完整宿主验收边界仍见文末。

2026-10-25 权限提示与布局稳定性：**318 个 .NET 用例、30 个前端用例、70 项真实 WebView2 断言全部通过**，前端和 Release 构建成功。编辑页保留待重新打开状态，并提供直接重开操作；内置网页的权限/错误/弹窗提示改为独立浮层。保存按钮尺寸固定，短操作延迟显示加载反馈，错误提示不再插入表单，图标操作和滚动条预留空间，重复保存复用同一条提示。真实浏览器比较保存前后控件坐标、采集 `layout-shift`，并确认通知及刷新不改变原生网页视口；同时验证离开再返回恢复提示、重新打开后清除提示。前端覆盖快速加载不闪、卸载取消延迟反馈、重开失败与保存回读失败等状态。报告位于 `artifacts/browser-tests/20261025-111524/report.json`，.NET 结果位于 `artifacts/test-results/stable-layout.trx`；已实际查看权限编辑页和原生网页浮层截图。

2026-10-25 调试模式调整：按最终交互要求移除导航工具栏、地址栏和日志面板，调试开关仅控制开发者工具；普通和调试模式均无常驻状态文字。原外部打开按钮测试改为实际外部导航失败场景，检查原网页及桥接仍可使用。**318 个 .NET 用例、69 项真实 WebView2 断言全部通过**，前端和 Release 构建成功。结果位于 `artifacts/test-results/website-window-devtools.trx` 和 `artifacts/browser-tests/20261025-105737/report.json`。

2026-10-25 网页窗口布局调整：普通模式隐藏工具栏、地址、常驻状态和日志，网页铺满内容区域；调试模式保留这些控件，弹窗拦截与操作失败提示按需显示。实际查看了普通/调试模式的原生窗口截图；**318 个 .NET 用例、69 项真实 WebView2 断言全部通过**，前端和 Release 构建成功。报告位于 `artifacts/browser-tests/20261025-105235/report.json`，.NET 结果位于 `artifacts/test-results/website-window-layout.trx`。

2026-10-25 剪贴板搜索回归：**318 个 .NET 用例全部通过**。修复空输入提前退出导致剪贴板模板搜索动作缺失；覆盖默认/仅剪贴板策略、仅输入策略、空白内容、固定页/本地页/忽略节点过滤、URL 编码和剪贴板快照更新与清空。结果位于 `artifacts/test-results/clipboard-search.trx`；本次未重跑真实宿主搜索窗或 WebView2 集成测试。

2026-10-25 异常边界回归：**304 个 .NET 用例、69 项真实 WebView2 断言全部通过**，Release 构建成功。新增消息会话/来源检查失败、订阅回调失败、错误日志失败、通道清理失败仍取消关闭握手的测试；真实窗口验证外部打开失败、弹窗创建失败后重试、设置页初始化失败提示，以及初始化期间关闭窗口。报告位于 `artifacts/browser-tests/20261025-104312/report.json`，.NET 结果位于 `artifacts/test-results/exception-safety.trx`。这些结果覆盖已识别的异常边界，不代表所有宿主环境下均无崩溃风险。

2026-10-23：原有 152 个 .NET 用例通过后，补充功能交互、输入边界、前端异步回归及真实 WebView2 测试。用例数量不代表行覆盖率或完整宿主端到端覆盖率。

本次实际结果：**298 个 .NET 用例、22 个前端用例、65 项真实 WebView2 断言全部通过**。浏览器 Runtime 为 **155.0.4283.62**，运行目录 `artifacts/browser-tests/20261023-211129/`，测试进程退出码为 0。

## 运行

需要 Windows、.NET 10 SDK、Node.js、pnpm，以及 README 中说明的本地 MioKit 源码依赖。前端 VM 模块测试建议使用 Node.js 24：本轮使用 24.22.1，系统默认 20.19.2 在完整编辑器测试中发生原生进程崩溃，该运行明确记为失败。生成脚本测试也会调用 PATH 中的 `node`；缺少 Node 时测试会失败，不会静默跳过。

```powershell
# 仓库根目录
pnpm --dir plugin/vue-ui install --frozen-lockfile
pnpm --dir plugin/vue-ui test
pnpm --dir plugin/vue-ui build
dotnet test tests/MioKit.WebSearch.Tests.csproj -c Release --logger "trx;LogFileName=release-tests.trx" --results-directory artifacts/test-results
dotnet build MioKit.WebSearch.slnx -c Release
```

前端构建会清空并重建 `plugin/ui/dist`，必须完成后再执行 .NET 构建或测试，避免复制已经被替换的资源文件。

## 自动化覆盖

| 功能链路 | 已验证内容 | 主要测试文件 |
| --- | --- | --- |
| 空列表 → 新增 → 写库 → 编辑/打开 → 删除 | 实际鼠标/键盘操作、首次保存、失败与重试、目录联接环境下的外部新增、在线与本地网页、模式选择、数据库独立连接读回、重新创建节点与重开设置页、筛选与删除确认 | `preview/BrowserTests/BrowserTestRunner.UserJourneys.cs` |
| 全局搜索 → 网站结果 | 固定网站、别名、忽略搜索、三种查询来源、中文 URL 编码、结果节点和执行 payload、本地路径中的 `{query}`、取消和长文本高亮 | `WebsiteSearchInteractionTests.cs`、`WebsiteUrlResolverTests.cs` |
| 编辑配置 → 运行配置 | 旧节点默认值、完整配置往返、配置变化要求重开、版本、空列表/空元素、未知权限、映射冲突、本地 HTML、host 格式 | `WebsitePolicyValidationTests.cs`、`WebsitePolicyTests.cs`、`WebsiteConfigurationChangesTests.cs` |
| 导航 → 弹窗策略 | 四种打开模式 × 用户操作/自动弹窗开关、会话撤销优先级、非法协议、精确来源、非可信导航、本地目录边界 | `WebsiteNavigationPolicyTests.cs` |
| 浏览器目录 → 节点隔离 | 保留目录、尾部分隔符、大小写、已有文件、同目录/祖先/子目录冲突、合法兄弟目录、默认目录 | `WebsiteDataDirectoryPolicyTests.cs`、`WebsitePolicyTests.cs` |
| 文档会话 → 能力 → 回包 | 来源/权限检查、配置撤销、节点卸载、导航失效、异步旧结果丢弃、关闭握手 nonce、畸形消息 | `WebsiteCapabilityInteractionTests.cs`、`WebsiteCapabilitiesTests.cs`、`WebsiteBridgeTransportTests.cs` |
| 存储 → 多窗口/节点 | 同节点并发写入、跨节点隔离、删除/清空、空/中文键、JSON null、无效参数不损坏数据 | `WebsiteCapabilitiesTests.cs`、`WebsiteCapabilityInteractionTests.cs` |
| 文件 → 授权范围 | 读写权限分离、撤销目录授权、临时授权不能覆盖能力撤销、文件精确授权、窗口隔离、导航复用、路径穿越/目录联接、移动目标、非法 Base64 与覆盖失败不破坏原文件 | 同上及 `WebsitePolicyTests.cs` |
| C# 脚本生成 → 实际 JS bridge | 执行生成脚本及其内嵌 bridge，验证四阶段顺序、已就绪文档、单段失败继续、禁用脚本、关闭回执、iframe、额外来源、本地文件边界、临时 `about:blank`、全部 `Mio` API 参数和错误码、模板占位词不会二次替换用户代码 | `WebsiteScriptExecutionTests.cs`、`JavaScript/website-script.cjs` |
| 图标 → 保存/恢复/显示数据 | 导入与提交失败回滚、同域节点独立资产、删除原图后可用、恢复默认、缺失资产回退、ICO、取消、预览缓存、刷新合并 | `WebsiteIconServiceTests.cs`、`WebsiteRefreshTests.cs` 及现有 Favicon 测试 |
| 浏览器同步 → 索引查询 | Chromium/Firefox fixture、同步/停止/取消、查询参数与通配符、favicon 导入/网络响应上限/生命周期 | `BrowserIndex*Tests.cs`、`DirectSearchTests.cs`、`Favicon*Tests.cs` |
| Vue 编辑器 → 异步桥接 | 直接编译并执行真实 SFC setup，使用真实 Vue 响应式；验证加载/保存/重开/图标/目录选择的跨页面竞态、旧请求不得清除新请求的忙碌状态、加载时禁止保存、新建后回读和路由、草稿保留、脚本排序、权限切换、favicon 旧错误丢弃 | `plugin/vue-ui/tests/editor.test.js` |
| DTO → 前端字段 | camelCase/PascalCase、嵌套配置、默认值、显式 false/空值、自定义图标字段、草稿数组隔离 | `plugin/vue-ui/tests/configuration.test.js` |

后端单元测试使用内存 SDK Store、临时目录、测试数据库和模拟消息通道；生成脚本使用 Node VM 模拟浏览器事件及宿主响应。前端单元测试模拟路由、桥接、选择器和展示组件，不挂载实际 DOM。这些单元测试不读取用户浏览器配置、不写用户数据库、不打开外部浏览器或原生文件选择器；不能作为首次新增可用的证据，必须同时运行下述真实界面用户流程。

本轮回归修复了编辑器旧请求污染新网站、旧目录选择误加入新网站授权草稿、旧任务清除新任务状态、旧 URL 图标错误覆盖当前状态、权限错误跳错页、非法 host/空配置元素、数据根目录尾部分隔符绕过，以及配置撤销后的旧异步回包问题。

## 真实 WebView2 与构建后前端

```powershell
pwsh -File scripts/test-browser.ps1
# 已完成前端及 Release 构建时：
pwsh -File scripts/test-browser.ps1 -SkipBuild
# 只运行从空列表开始的用户流程（仍使用实际构建产物与真实 SQLite）：
pwsh -File scripts/test-browser.ps1 -SkipBuild -UserJourneysOnly
# 只运行在线/本地网页连续切换专项：
pwsh -File scripts/test-browser.ps1 -SkipBuild -ModeSwitchesOnly
```

入口先执行 Vite 构建，再构建并运行 Preview 的 `--website-tests` 模式。需要交互式 Windows 桌面和已安装的 WebView2 Runtime。测试会短暂打开真实窗口，运行结束自动关闭；失败返回非零退出码。网页、脚本及样式位于 `preview/BrowserTests/site/`，可以复制到测试节点作为本地网页使用；生命周期脚本分别为 `created.js`、`dom.js`、`loaded.js`、`closing.js`。

本轮测试复用实际 `WebsiteWindow`、`WebsiteWebView`、`WebsiteWindowService`、`WebsiteBridgeTransport`、C# 能力服务和 `PluginWebView`。设置页读取磁盘上的 `plugin/ui/dist/index.html` 及其 Vite 产物，真实挂载 DOM，不启动开发服务器，也不使用模拟网页桥接。读状态采用无用户手势的 CDP 探针；自动弹窗从磁盘脚本定时触发，避免测试探针改变用户激活状态。

报告位于 `artifacts/browser-tests/<运行时间>/report.json`、`report.html`，截图同目录；`latest.txt` 指向最近一次运行。JSON 记录实际浏览器版本、插件 DLL SHA-256、网页 fixture 与构建产物 SHA-256、逐项结果及桥接日志。每次使用独立的浏览器 Profile 和授权文件目录。

覆盖 HTTPS 虚拟站点与直接 `file://` 两种模式的静态 HTML/JS/CSS、四阶段脚本顺序和错误恢复、iframe 隔离、并发桥接与节点存储、真实文件读写/移动/删除和目录越界、默认能力拒绝、刷新保留数据、正常关闭握手及配置撤销后的刷新。HTTPS 模式还验证额外映射的 Allow/DenyCors/Deny、非可信映射导航与历史后退、自动弹窗拦截、阻止窗口模式、实际 opener/postMessage、共享 Environment/Profile、主窗口关闭后子窗口继续调用、主窗口重建、插件停止关闭全部窗口和过期弹窗按钮。生产设置页验证 DOM 修改、保存、列表刷新、回读，以及在脚本编辑器添加脚本、保存并由实际网站窗口执行。

实测修复：弹窗在继承来源的临时 `about:blank` 中，提前提供等待型 API，实际可信文档建立消息监听后再握手，避免页面早期脚本遇到 `Mio` 未定义或使用已失效的消息监听。原有来源、文档会话及权限检查仍在 C# 端执行；跳转到非可信来源时移除临时 API。同时修复配置撤销后残留弹窗按钮仍可打开地址或改变自动弹窗权限，导航和撤销都会清除旧提示。

真实窗口测试中的 SDK 节点使用宿主 `MioStore` 与真实持久化事件处理器，实际运行宿主的数据库 Schema、批量写入和 Dapper 数据提供器；数据库位于每次报告目录的 `host/journey-eav.db`，并使用独立连接和新节点实例核对保存结果。插件上下文与未使用的宿主设施仍由测试入口提供；设置页重开与节点重新加载不等同于完整宿主进程重启。外部打开的系统进程启动边界被记录，校验地址和系统默认打开参数，不实际启动用户的外部浏览器。设置页初始化会枚举已安装浏览器及 Profile 名称，但不触发收藏夹同步。原生选择器仅验证未授权时被拒绝；允许后的人机操作、真实登录、摄像头/麦克风等硬件权限和完整 Monaco 编辑仍需宿主验收。

本次脚本编辑使用 Monaco 运行时缺失时的真实 textarea 降级路径。退出时 Chromium 在 stderr 记录一次 `Failed to unregister class Chrome_WidgetWin_0, Error = 1412`，65 项断言均通过且进程正常退出；原始日志保留在 `artifacts/browser-test-stderr.log`。Release 构建仍有现有的 MessagePack 安全提示和 WindowsBase 引用冲突警告。

## 完整宿主验收：仍需执行

以下内容依赖完整 MioKit 宿主或人工交互，Preview 中的真实 WebView2 自动化不能替代。

| 场景 | 验收要求 |
| --- | --- |
| 网站新增、保存、重启宿主、删除 | 真实 EAV 持久化与重新挂树正确；保存失败保留原图标；删除清理自有图标而不删除网页/浏览器文件 |
| 登录与宿主导航入口 | 实际登录回跳、搜索入口激活窗口、查询模板更新导航 |
| 宿主生命周期 | 插件卸载、删除节点、宿主退出和配置保存后的完整宿主联动 |
| 原生权限与选择器 | 摄像头/麦克风/定位/通知/剪贴板/文件系统逐项验证允许及拒绝；选择器取消、导航期间返回、临时授权窗口隔离有效 |
| 图标和界面 | 主/子窗口与任务栏图标一致；快速换图后旧图不覆盖新图；深浅主题、布局、键盘焦点、Monaco 编辑与中文输入正常 |

Release 构建通过只证明编译及资源复制成功；上述真实宿主场景完成前，不将测试结果描述为“全部功能端到端验证通过”。
