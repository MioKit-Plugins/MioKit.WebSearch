export function emptyConfiguration() {
  return {
    version: 1, sourceKind: 'Online', openMode: 'External', localFilePath: '', userDataFolder: '',
    mapLocalDirectory: true, localHostName: '', debugEnabled: false,
    runtime: {
      version: 1, currentNavigation: 'Current', newWindowMode: 'NewWindow', allowAutomaticPopups: false,
      trustedOrigins: [], mappings: [], scripts: [], permissions: [], fileDirectories: [], browserPermissions: [],
    },
  };
}
export function camelize(value) {
  if (Array.isArray(value)) return value.map(camelize);
  if (value && typeof value === 'object') return Object.fromEntries(Object.entries(value).map(([k, v]) => [k[0].toLowerCase() + k.slice(1), camelize(v)]));
  return value;
}
export function emptyDraft() {
  return { id: '', name: '', description: '', urlTemplate: '', querySourcePolicy: 'InputThenClipboard', configuration: emptyConfiguration(), effectiveDataDirectory: '', iconAction: 'Keep', iconSourcePath: '', hasCustomIcon: false, iconDataUrl: '', iconState: '' };
}
export const permissionOptions = [
  ['storage', '网站数据存储', '允许网页用 Mio.storage 读写此网站的键值数据；各可信窗口共用，与浏览器缓存、登录状态无关'],
  ['files.read', '文件读取', '读取已授权目录或你选择的文件'],
  ['files.write', '文件写入', '创建、修改、移动和删除已授权文件'],
  ['clipboard.read', '读取剪贴板', '读取系统剪贴板文本'],
  ['clipboard.write', '写入剪贴板', '替换系统剪贴板文本'],
  ['notifications', '桌面通知', '通过 MioKit 显示通知'],
];
export const browserPermissionOptions = [
  ['Camera', '摄像头'], ['Microphone', '麦克风'], ['Geolocation', '定位'],
  ['Notifications', '网页通知'], ['ClipboardRead', '读取剪贴板'], ['FileReadWrite', '访问本地文件'],
];

