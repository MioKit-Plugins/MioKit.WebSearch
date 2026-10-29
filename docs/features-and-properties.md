# MioKit.WebSearch Feature 与属性清单

## 持久化节点

| 节点 | Feature | 持久化属性 |
| --- | --- | --- |
| `WebSearchSettingsNode` | `IWebSearchSettingsFeature` | `DefaultBrowserIdProperty` |
| `BookmarkSearchScope` | `ISearchScopeFeature` | MioKit 内置 `UserSearchCommandsProperty` |
| `WebsiteSearchGroup` | `ISearchableFeature` | 名称、描述 |
| `WebsiteNode` | `IWebsiteFeature`、`IIconProviderFeature`、`IHotkeyFeature`、`IAliasNameFeature`、`IUseFeature`、`IPinnedFeature`、`IIgnoreSearchFeature`、`IResultActionProviderFeature` | 名称、描述、`UrlTemplateProperty`、`QuerySourcePolicyProperty`、`CustomIconIdProperty`；以及内置别名、热键、固定、忽略、最近使用属性 |

`FaviconFileNameProperty` 仍保留在 EAV 定义中（已发布不可删），但运行时不再读写；自动图标按 origin 缓存在插件数据目录。`CustomIconIdProperty` 的稳定 Guid 为 `A6A4D3FC-9720-4421-BE77-E749285A5E58`，默认空，仅保存宿主用户图标资产 ID，并加入网站预加载列表。

含 `{query}` 的搜索模板在快捷键触发时直接使用调用快照中的剪贴板文本替换 `{query}`。搜索框执行仍按 `QuerySourcePolicy` 解析。

配置与自定义网站都保存在 MioKit EAV 数据库中。插件不创建 JSON 配置文件。

### 内置网页扩展

`WebsiteNode` 额外实现 `IEmbeddedWebsiteFeature`，通过源生成访问器读写以下属性；身份常量集中在 `WebSearchConst`，保留原有节点类型和已发布属性 Id。

| 属性 | 用途 |
| --- | --- |
| `SourceKindProperty` / `OpenModeProperty` | 在线或本地入口、外部或内置打开；旧节点默认 Online / External |
| `LocalFilePathProperty` | 本地 HTML 路径 |
| `UserDataFolderProperty` | 自定义浏览器数据目录；空值使用节点独立目录 |
| `MapLocalDirectoryProperty` / `LocalHostNameProperty` | 入口目录映射与稳定 host，默认开启映射 |
| `DebugEnabledProperty` | 运行时 Debug，默认关闭 |
| `RuntimeOptionsProperty` | 带版本的 JSON 配置：导航、弹窗、映射、脚本、服务权限、文件授权和可信来源 |
| `WebsiteStorageJsonProperty` | 网页专属 JSON 键值数据，按节点串行修改 |

仅搜索需要的入口、打开方式和本地路径加入预加载。脚本及运行配置在编辑或打开时异步加载；不把大段脚本放进搜索热路径。

节点键值数据与浏览器数据目录分开；目录映射、临时文件选择授权及窗口实例不会创建额外网站节点。使用说明见 [内置网页.md](内置网页.md)。

## 搜索边界

- `BookmarkSearchScope` 的内置 Command 是 `bookmark-web`、`favorites-web`。
- `WebsiteSearchGroup` 不是附加面板，不注册 Command，始终参与全局搜索。
- 收藏夹通过插件私有 `browser-index.db` 参数化 SQL `LIKE` 查询。
- 固定网站仅用 `TryMatch` 匹配名称与别名，不匹配描述或 URL。
- `{query}` 搜索模板按查询来源生成搜索动作：输入优先在空输入时使用剪贴板，仅输入不读取剪贴板，仅剪贴板忽略输入；实际查询文字为空白时不显示。`IgnoreSearch` 为 true 的节点不出现。仅有剪贴板文字时不匹配固定网站或本地网页。
- 收藏夹路径不调用 `ITextMatcher`、`SearchHelper` 或拼音匹配。

## 图标

网站通过 `WebsiteIconService` 统一解析图标，编辑预览、列表、搜索结果及内置主窗口/弹窗采用同一优先级：自定义图片 → 在线 favicon → MioKit 默认图标。本地网页也支持自定义图片，恢复后使用默认图标。

- 自定义图片通过插件 DI 注入的 `IPluginIconService.ImportUserAsync` 保存为 `Persistent` 资产；网站之间使用独立 ID，同域网站不会互相覆盖。宿主复制图片内容，不保存原图片路径。
- 支持 PNG、JPG/JPEG、WebP、ICO，单文件不超过 5 MB。`PickWebsiteIcon` 只校验并生成 PNG 缩略预览，编辑草稿中的临时路径不持久化；`SaveWebsite` 的 `IconAction` 默认 `Keep`，`Replace` 在校验后导入，`Reset` 清除引用。
- 保存成功后才删除旧资产；导入或保存失败清理新资产并保留原图标。恢复默认和删除网站清理网站拥有的自定义图标，保存与删除共用串行保护。
- 自定义资产缺失或损坏时回退，设置页提示重新选择。预览按资产 ID、文件大小和修改时间缓存，后台解码，取消或过期请求不会覆盖新列表/窗口。
- `WebsiteDto.IconDataUrl` 是最终图标，`HasCustomIcon` 表示是否配置自定义资产，`IconState` 表示来源或失效状态；`FaviconDataUrl` 仍只表示自动 favicon。
- 网站窗口通过宿主原语设置插件 OwnerId 和 AUMID，顺序应用插件兜底与网站图标；随 `IconRevision` 更新并在关闭时取消、释放 lease，避免插件 Behavior 覆盖网站图标。

网站的自动图标与收藏夹结果 `BookmarkResultNode` 继续共用 `FaviconService` 的 origin 缓存：

1. 文件名为 `sha256(origin.AbsoluteUri).png`，位于插件私有数据目录 `favicons/`。
2. 网站经插件 DI 的 `IPluginIconService.OpenFileAsync` 返回独立 lease；收藏夹保留现有读取流程。
3. 缓存不存在时返回 MioKit 通用默认图标，并登记 `Watch` + 后台 `RequestRefresh`。
4. 收藏夹同步后优先从 Chromium `Favicons` / Firefox `favicons.sqlite` 导入，没有再 HTTP 解析 `<link rel="icon">`。

网络获取与浏览器库导入都不在 `SearchAsync` 热路径中执行。

当前使用的 `1.3.1` NuGet 依赖已包含插件图标 DI 与用户图片导入 API；实际运行宿主也需提供这些能力。
