/** 产品内使用说明文案（与 docs/产品说明.md、内置网页.md 对齐） */

export const guideSections = [
  {
    id: 'quickstart',
    title: '快速上手',
    icon: 'Zap',
    paragraphs: [
      '网页快开让你在 MioKit 搜索框里直接打开常用网页、用模板搜索，或查找本机浏览器收藏夹。',
      '在搜索框输入「网页快开」并回车，即可打开本管理窗口。自定义网站始终参与全局搜索，无需先输入命令；浏览器收藏夹需要先附着到搜索框后再搜。',
    ],
    subsections: [
      {
        title: '你可以做什么',
        bullets: [
          '把常用控制台、文档站、邮箱等加成「固定网页」，按名称或别名一键打开',
          '用带 {query} 的地址做成「搜索模板」，在搜索框输入关键词后跳转到对应搜索页',
          '同步本机 Edge / Chrome / Firefox 的收藏夹，附着后按标题搜索并打开',
          '为网站设置别名、全局热键，或固定到搜索首页；也可从搜索结果中忽略某个网站',
        ],
      },
      {
        title: '建议的第一步',
        steps: [
          '打开「网站入口」，添加一两个最常用的固定网址（如 GitHub、公司文档）',
          '如需搜网页，再添加一条带 {query} 的搜索模板（如 Bing、内部搜索）',
          '到「收藏夹」点一次同步，确认本机浏览器数据能读到',
          '回到搜索框试搜：直接输入网站名；搜收藏夹则先用 bookmark-web 附着',
        ],
      },
    ],
  },
  {
    id: 'websites',
    title: '网站入口',
    icon: 'Globe2',
    paragraphs: [
      '「网站入口」用来管理自定义网站。地址必须以 http:// 或 https:// 开头。添加后会参与全局搜索，按名称、别名匹配。',
    ],
    subsections: [
      {
        title: '固定网页',
        paragraphs: [
          '填写完整网址，例如 https://github.com。匹配后回车直接打开该页，不会读取搜索框或剪贴板内容。',
          '适合常用控制台、文档站、内部系统入口——你只想「打开这个站」，而不是带着关键词去搜。',
        ],
      },
      {
        title: '搜索模板',
        paragraphs: [
          '在地址中加入 {query}，例如 https://www.bing.com/search?q={query}。执行时会用 URL 编码后的查询内容替换 {query}。',
          '只要搜索框有输入，每个未忽略的搜索模板都会贡献一条结果，标题形如「使用 xxx 搜索“…”」，并排在结果列表靠后。',
        ],
        bullets: [
          '查询来源可按网站设置：输入优先（空时用剪贴板）、仅输入、仅剪贴板',
          '用全局热键触发搜索模板时，始终用剪贴板文字替换 {query}，与上面的查询来源无关',
        ],
      },
      {
        title: '打开方式',
        paragraphs: [
          '在编辑网站时设置打开方式。默认用外部浏览器打开；改为「内置窗口」后，页面会在插件自带窗口里打开，并可进一步配置脚本、权限、本地数据目录等（见「内置窗口」一节）。列表页不展示打开方式，以免干扰浏览。',
        ],
      },
      {
        title: '搜索结果上的操作',
        paragraphs: ['在自定义网站的搜索结果上按 Alt，可以：'],
        bullets: [
          '固定 / 取消固定到搜索首页',
          '设置别名，用更短的名字命中',
          '绑定全局热键：固定页直接打开；搜索模板用剪贴板填充 {query}',
          '忽略搜索：该网站不再出现在结果中（可在编辑页取消忽略）',
        ],
      },
      {
        title: '网站图标',
        paragraphs: [
          '保存后会自动获取并缓存站点图标。编辑时可手动「获取图标」或「重新获取」。图标尚未就绪时，搜索结果先显示通用图标，就绪后自动更新。',
        ],
      },
    ],
  },
  {
    id: 'bookmarks',
    title: '收藏夹',
    icon: 'Bookmark',
    paragraphs: [
      '收藏夹不参与普通全局搜索，需要先附着到搜索框。附着后按标题包含匹配（不区分大小写），最多返回 50 条；不按网址搜索。',
    ],
    subsections: [
      {
        title: '怎么搜',
        paragraphs: ['任选一种方式附着后再输入关键词：'],
        bullets: [
          '输入命令 bookmark-web 或 favorites-web，再按宿主配置的命令触发键',
          '在搜索框输入 /，找到「浏览器收藏夹」并选中',
        ],
        steps: [
          '确认本机已安装 Edge、Chrome 或 Firefox，并在「收藏夹」页同步成功',
          '用上面任一方式附着搜索框',
          '输入收藏夹标题里的关键词，选中结果回车即可在默认浏览器中打开',
        ],
      },
      {
        title: '索引从哪来',
        paragraphs: [
          '插件读取本机已安装浏览器的本地数据（Edge / Chrome 用户目录、Firefox Profile）。启动时同步一次，之后约每 5 分钟自动同步；也可在「收藏夹」设置里手动同步。',
          '同步只建立本地索引，不会改写浏览器里的收藏夹。某个 Profile 失败时会保留上次完整索引，并在设置与错误提示中显示原因。',
        ],
      },
      {
        title: '默认浏览器',
        paragraphs: [
          '在「收藏夹」页点右上角设置按钮，可选择用哪个浏览器打开链接（Edge / Chrome / Firefox 或系统默认）。所选浏览器不可用时会回退到系统默认。',
        ],
      },
    ],
  },
  {
    id: 'embedded',
    title: '内置窗口',
    icon: 'PanelTop',
    paragraphs: [
      '默认用外部浏览器打开网站。把打开方式改为「内置窗口」后，页面在插件窗口中浏览，并可配置脚本、权限、本地数据目录与虚拟 host。',
      '保存会改变运行环境的配置后，已经打开的旧窗口不再接受新注入；需要「重新打开以应用」后，新配置才会生效。',
    ],
    subsections: [
      {
        title: '窗口与导航',
        bullets: [
          '每个网站节点保留一个主窗口；搜索模板的新查询会在该主窗口中导航',
          '可配置新窗口（target=_blank / window.open）是：新建内置窗、当前窗打开、外部浏览器，或阻止',
          '导航到非可信来源时，默认仍可浏览，但不注入服务和用户脚本',
        ],
      },
      {
        title: '适合什么场景',
        paragraphs: [
          '需要登录态常驻、页面内脚本自动化，或希望在 MioKit 内完成轻量工作台时，用内置窗口更合适。只是偶尔打开一下的站点，继续用外部浏览器即可。',
        ],
      },
    ],
  },
  {
    id: 'scripts',
    title: '脚本与权限',
    icon: 'Code2',
    paragraphs: [
      '仅内置窗口可配置用户脚本。生命周期支持四个阶段：文档创建、DOM 就绪、加载完成、窗口关闭前。每阶段可写多段脚本，按列表顺序执行；单段失败会记录错误并继续后续脚本。',
      '可信页面通过 window.Mio 调用能力。所有方法返回 Promise；失败时抛出带 code / message 的 Error。权限默认全部关闭，未授权的调用会失败。',
    ],
    subsections: [
      {
        title: '常用能力',
        bullets: [
          'storage：节点专属键值存储（需 storage 权限）',
          'files / dialogs：读写文件与选择器（需 files.read / files.write，以及目录授权）',
          'clipboard：读写文本（需对应剪贴板权限）',
          'notifications.show：桌面通知',
          'external.open：用外部浏览器打开链接（外部打开无需单独授权，仅限可信页）',
        ],
      },
      {
        title: '从哪看完整 API',
        paragraphs: [
          '在网站编辑页的脚本区域，右上角「API 速查」可查看可复制示例。下面是一段最小可用示例：',
        ],
      },
    ],
    code: `await Mio.ready;
await Mio.storage.set('preferences', { theme: 'dark' });
const preferences = await Mio.storage.get('preferences');
await Mio.notifications.show('保存完成', '本地工作台');
await Mio.external.open('https://example.com');`,
  },
  {
    id: 'examples',
    title: '使用示例',
    icon: 'ListChecks',
    subsections: [
      {
        title: '打开固定站点',
        steps: [
          '在「网站入口」新增网站，地址填 https://mail.example.com，名称填「公司邮箱」',
          '保存后，在搜索框输入「邮箱」或完整名称，回车打开',
          '若经常用，可在搜索结果上按 Alt，设别名或绑定热键',
        ],
      },
      {
        title: '用模板搜网页',
        steps: [
          '新增网站，地址填 https://www.bing.com/search?q={query}，名称填「Bing」',
          '在搜索框输入要搜的内容，选中「使用 Bing 搜索“…”」回车',
          '若常用剪贴板关键词，可把查询来源设为「仅使用剪贴板」，或给该网站设热键（热键始终读剪贴板）',
        ],
      },
      {
        title: '搜收藏夹',
        steps: [
          '打开「收藏夹」，确认本机浏览器已列出并能同步成功',
          '在搜索框输入 bookmark-web 并触发命令，附着「浏览器收藏夹」',
          '输入收藏夹标题中的关键词，选中结果打开',
        ],
      },
    ],
  },
];

export const guideToc = guideSections.map(({ id, title, icon }, index) => ({
  id,
  title,
  icon,
  index: index + 1,
}));
