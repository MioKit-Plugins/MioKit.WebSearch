# MioKit.WebSearch

MioKit 的网页搜索插件。通过 Command 搜索 Edge、Chrome、Firefox 的本机收藏夹，并让固定网址与带 `{query}` 的自定义搜索模板直接参与全局搜索。

产品说明见 [docs/产品说明.md](docs/产品说明.md)。

测试命令、自动化覆盖与真实宿主验收边界见 [docs/testing.md](docs/testing.md)。

内置窗口、本地 HTML、虚拟 host、脚本、权限和 JavaScript API 见 [docs/内置网页.md](docs/内置网页.md)。

网站编辑页支持选择本地图片作为图标（PNG、JPG/JPEG、WebP、ICO，最大 5 MB），保存后同步到列表、搜索结果和内置窗口。也可恢复自动 favicon；本地网页恢复为默认图标。图片由宿主持久保存，保存后移动或删除原文件不影响显示。

## 开发

```powershell
cd plugin\vue-ui
pnpm install --frozen-lockfile
pnpm build

cd ..\..
dotnet restore MioKit.WebSearch.slnx
dotnet test tests/MioKit.WebSearch.Tests.csproj -c Release
dotnet build MioKit.WebSearch.slnx -c Release
```

插件设置和网站使用 MioKit EAV 属性持久化；`browser-index.db` 仅保存可重建的浏览器索引缓存。

MioKit 依赖统一通过 nuget.org 的 `1.3.1` 包还原，版本由 `Directory.Build.props` 中的 `MioKitPackageVersion` 管理。构建无需 MioKit 宿主源码。Preview 工程已移除，网页窗口和完整用户流程在实际宿主中验收。

打包：

```powershell
.\pack.ps1 -PackageVersion 1.0.0
```

输出默认在 `nupkgs/`。推送 `v*` 标签（如 `v1.0.0`）后，GitHub Actions 会用同一脚本打包到 `artifacts/`，并把 `.nupkg` 挂到对应 GitHub Release（不自动发布到 NuGet）。
