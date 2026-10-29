export function pickField(item, camelKey, pascalKey, fallback = '') {
  return item?.[camelKey] ?? item?.[pascalKey] ?? fallback;
}

export function resolveWebsite(item) {
  return {
    Id: String(pickField(item, 'id', 'Id')),
    Name: String(pickField(item, 'name', 'Name')),
    Description: String(pickField(item, 'description', 'Description')),
    UrlTemplate: String(pickField(item, 'urlTemplate', 'UrlTemplate')),
    QuerySourcePolicy: String(pickField(item, 'querySourcePolicy', 'QuerySourcePolicy', 'InputThenClipboard')),
    IsSearchTemplate: Boolean(pickField(item, 'isSearchTemplate', 'IsSearchTemplate', false)),
    SourceKind: String(pickField(item, 'sourceKind', 'SourceKind', 'Online')),
    OpenMode: String(pickField(item, 'openMode', 'OpenMode', 'External')),
    IconState: String(pickField(item, 'iconState', 'IconState', '暂无图标')),
    FaviconDataUrl: String(pickField(item, 'faviconDataUrl', 'FaviconDataUrl')),
    HasCustomIcon: Boolean(pickField(item, 'hasCustomIcon', 'HasCustomIcon', false)),
    IconDataUrl: String(pickField(item, 'iconDataUrl', 'IconDataUrl')),
  };
}

export function resolveBrowser(item) {
  return {
    Id: String(pickField(item, 'id', 'Id')),
    Name: String(pickField(item, 'name', 'Name')),
    IsAvailable: Boolean(pickField(item, 'isAvailable', 'IsAvailable', false)),
    Path: String(pickField(item, 'path', 'Path')),
  };
}

export function resolveProfile(item) {
  return {
    Browser: String(pickField(item, 'browser', 'Browser')),
    Name: String(pickField(item, 'name', 'Name')),
    Path: String(pickField(item, 'path', 'Path')),
  };
}

export const policyLabels = {
  InputThenClipboard: '输入优先',
  InputOnly: '仅输入',
  ClipboardOnly: '仅剪贴板',
};

export function websiteTypeLabel(site) {
  if (site.SourceKind === 'LocalFile') return '本地网页';
  if (site.IsSearchTemplate) return '搜索入口';
  return '固定网址';
}
