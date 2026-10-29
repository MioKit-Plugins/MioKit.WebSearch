/**
 * Monaco extraLib：内置网页脚本中的 window.Mio / 注入参数 Mio。
 * 与 WebsiteScriptBuilder 注入的 API 及 docs/内置网页.md 对齐。
 */
export function getMioScriptTypeDefinitions() {
  return `
/** 文件列表项（Mio.files.list） */
interface MioFileEntry {
  path: string;
  name: string;
  isDirectory: boolean;
}

/**
 * 调用失败时抛出的 Error，额外带 code。
 * 常见：permission.denied、path.invalid、path.denied、document.expired、
 * service.invalid、operation.invalid、url.invalid、operation.failed
 */
interface MioError extends Error {
  code: string;
}

/** 节点专属 JSON 键值；需要 storage 权限 */
interface MioStorage {
  /** 读取键值；不存在时为 null/undefined */
  get(key: string): Promise<any>;
  /** 写入任意可 JSON 序列化的值 */
  set(key: string, value: any): Promise<void>;
  remove(key: string): Promise<void>;
  keys(): Promise<string[]>;
  clear(): Promise<void>;
}

/**
 * 已授权路径下的文件操作。
 * 读操作需 files.read；写/删/建目录/移动需 files.write。
 */
interface MioFiles {
  readText(path: string): Promise<string>;
  readBase64(path: string): Promise<string>;
  writeText(path: string, content: string): Promise<void>;
  writeBase64(path: string, content: string): Promise<void>;
  list(path: string): Promise<MioFileEntry[]>;
  createDirectory(path: string): Promise<void>;
  move(path: string, target: string): Promise<void>;
  /** 删除单个文件，不提供递归删目录 */
  delete(path: string): Promise<void>;
}

/**
 * 原生文件/目录选择器；随 files.read / files.write 一并可用。
 * 返回本地绝对路径；取消选择时为 null/undefined。
 */
interface MioDialogs {
  openFile(): Promise<string | null | undefined>;
  saveFile(name?: string): Promise<string | null | undefined>;
  /**
   * @param write true 表示请求读写目录（需 files.write）；false 仅读取（需 files.read）
   */
  openDirectory(write?: boolean): Promise<string | null | undefined>;
}

interface MioClipboard {
  /** 需 clipboard.read */
  readText(): Promise<string | null | undefined>;
  /** 需 clipboard.write */
  writeText(text: string): Promise<void>;
}

interface MioNotifications {
  /** 需 notifications 权限；title 可选 */
  show(message: string, title?: string): Promise<string>;
}

interface MioExternal {
  /** 仅允许 http/https；可信页面可调用，无需单独授权 */
  open(url: string): Promise<void>;
}

/**
 * 内置窗口注入的宿主 API。脚本作用域内可直接使用 Mio。
 * 所有方法返回 Promise；失败抛出带 code 的 Error。
 * 请先 await Mio.ready，并在「权限」页开启对应能力。
 */
interface MioApi {
  /** 文档会话与服务通道就绪 */
  ready: Promise<void>;
  storage: MioStorage;
  files: MioFiles;
  dialogs: MioDialogs;
  clipboard: MioClipboard;
  notifications: MioNotifications;
  external: MioExternal;
}

declare const Mio: MioApi;
`;
}
