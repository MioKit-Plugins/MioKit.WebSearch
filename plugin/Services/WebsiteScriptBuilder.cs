using System.Reflection;
using System.Text.Json;

namespace WebSearch.Services;

public static class WebsiteScriptBuilder
{
    private static string Resource(string name)
    {
        using var stream = typeof(WebsiteScriptBuilder).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("缺少网页桥接资源：" + name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
    public static string Build(WebsiteConfiguration config, string nodeId, string onlineUrl)
    {
        var origins = config.Runtime.TrustedOrigins.Select(WebsitePolicy.Origin)
            .Append(WebsitePolicy.Origin(WebsitePolicy.Entry(config, nodeId, onlineUrl))).Where(x => x is not null).Distinct();
        var localRoot = config.SourceKind == "LocalFile" && !config.MapLocalDirectory
            ? new Uri(Path.GetDirectoryName(Path.GetFullPath(config.LocalFilePath))! + Path.DirectorySeparatorChar).AbsoluteUri : "";
        var scripts = config.Runtime.Scripts.Where(x => x.Enabled).Select(x => new { name = x.Name, stage = x.Stage, code = x.Code });
        var template = """
        (function bootstrap() {
          if (window.top !== window.self) return;
          const trustedOrigins = ORIGINS;
          const localRoot = LOCALROOT;
          const isTrusted = () => trustedOrigins.includes(location.origin.toLowerCase()) ||
            (localRoot && location.protocol === 'file:' && location.href.toLowerCase().startsWith(localRoot.toLowerCase()));
          // WebView2 can run the popup's init script on its inherited-origin about:blank.
          // Publish Mio before the page's own scripts, but wait for the real trusted
          // document before starting the bridge handshake or lifecycle scripts.
          const provisional = location.href === 'about:blank' && !!window.opener &&
            trustedOrigins.includes(String(window.origin || '').toLowerCase());
          if (!isTrusted()) {
            // A WebView2 popup can initially execute its init script on a provisional
            // about:blank document that is then populated with the requested page.
            if (location.href === 'about:blank') {
              if (provisional && !window.__mioPendingReady) {
                let resolve;
                const ready = new Promise(done => { resolve = done; });
                window.__mioPendingReady = { resolve };
                const groups = {
                  storage:['get','set','remove','keys','clear'],
                  files:['readText','readBase64','writeText','writeBase64','list','createDirectory','move','delete'],
                  dialogs:['openFile','saveFile','openDirectory'], clipboard:['readText','writeText'],
                  notifications:['show'], external:['open']
                };
                window.Mio = Object.freeze({ready:ready.then(api => api.ready),
                  ...Object.fromEntries(Object.entries(groups).map(([group, methods]) => [group,
                    Object.freeze(Object.fromEntries(methods.map(method => [method,
                      (...args) => ready.then(api => api[group][method](...args))])))
                  ]))
                });
              }
              document.addEventListener('readystatechange', bootstrap);
              document.addEventListener('DOMContentLoaded', bootstrap);
              window.addEventListener('load', bootstrap);
            } else if (window.__mioPendingReady) {
              delete window.__mioPendingReady;
              delete window.Mio;
              document.removeEventListener('readystatechange', bootstrap);
              document.removeEventListener('DOMContentLoaded', bootstrap);
              window.removeEventListener('load', bootstrap);
            }
            return;
          }
          if (window.__mioInstalled) return;
          window.__mioInstalled = true;
          document.removeEventListener('readystatechange', bootstrap);
          document.removeEventListener('DOMContentLoaded', bootstrap);
          window.removeEventListener('load', bootstrap);
          BRIDGE
          const pending = window.__mioPendingReady;
          const documentReady = pending ? new Promise(resolve => {
            let retry;
            let attempts = 0;
            const cleanup = () => {
              clearTimeout(retry);
              chrome.webview.removeEventListener('message', receive);
            };
            const receive = event => {
              let message;
              try { message = typeof event.data === 'string' ? JSON.parse(event.data) : event.data; } catch { return; }
              if (message?.type !== 'mio-host-ready' || !isTrusted()) return;
              cleanup();
              resolve();
            };
            const connect = () => {
              if (++attempts > 100) { cleanup(); return; }
              clearTimeout(retry);
              retry = setTimeout(connect, 100);
              if (!isTrusted()) { cleanup(); return; }
              // JS location and WebView2's message Source can settle at different times.
              // The host acknowledges only after both sources and the document session pass validation.
              chrome.webview.postMessage(JSON.stringify({type:'mio-bootstrap'}));
            };
            chrome.webview.addEventListener('message', receive);
            connect();
          }) : Promise.resolve();
          const service = documentReady.then(() => window.DotnetBridge.getService('WebsiteCapabilities'));
          const invoke = async (group, operation, args = {}) => {
            const result = await (await service).Invoke(group, operation, JSON.stringify(args));
            if (!result.success) { const error = new Error(result.message); error.code = result.code; throw error; }
            return result.value;
          };
          window.Mio = Object.freeze({
            ready: service.then(() => undefined),
            storage: Object.freeze({
              get: key => invoke('storage', 'get', {key}),
              set: (key, value) => invoke('storage', 'set', {key, value}),
              remove: key => invoke('storage', 'remove', {key}),
              keys: () => invoke('storage', 'keys'),
              clear: () => invoke('storage', 'clear')
            }),
            files: Object.freeze({
              readText: path => invoke('files', 'readText', {path}),
              readBase64: path => invoke('files', 'readBase64', {path}),
              writeText: (path, content) => invoke('files', 'writeText', {path, content}),
              writeBase64: (path, content) => invoke('files', 'writeBase64', {path, content}),
              list: path => invoke('files', 'list', {path}),
              createDirectory: path => invoke('files', 'createDirectory', {path}),
              move: (path, target) => invoke('files', 'move', {path, target}),
              delete: path => invoke('files', 'delete', {path})
            }),
            dialogs: Object.freeze({
              openFile: () => invoke('dialogs', 'openFile'),
              saveFile: name => invoke('dialogs', 'saveFile', {name}),
              openDirectory: (write = false) => invoke('dialogs', 'openDirectory', {write})
            }),
            clipboard: Object.freeze({
              readText: () => invoke('clipboard', 'readText'),
              writeText: text => invoke('clipboard', 'writeText', {text})
            }),
            notifications: Object.freeze({show: (message, title = '') => invoke('notifications', 'show', {message, title})}),
            external: Object.freeze({open: url => invoke('external', 'open', {url})})
          });
          if (pending) { delete window.__mioPendingReady; pending.resolve(window.Mio); }
          const scripts = SCRIPTS;
          const run = async stage => {
            for (const item of scripts.filter(x => x.stage === stage)) {
              try {
                await new (Object.getPrototypeOf(async function(){}).constructor)('Mio', item.code)(window.Mio);
              } catch (e) {
                console.error('[Mio script]', item.name, e);
                chrome.webview.postMessage(JSON.stringify({type:'mio-script-error', message:item.name + ': ' + String(e)}));
              }
            }
          };
          const created = pending ? service.then(() => run('DocumentCreated')) : run('DocumentCreated');
          let dom;
          const onDom = () => { dom = created.then(() => run('DomReady')); };
          const onLoad = () => { (dom || created).then(() => run('Loaded')); };
          if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', onDom, {once:true}); else onDom();
          if (document.readyState === 'complete') onLoad(); else window.addEventListener('load', onLoad, {once:true});
          window.__mioClose = async nonce => {
            try { await run('Closing'); } finally {
              chrome.webview.postMessage(JSON.stringify({type:'mio-lifecycle-close',nonce}));
            }
          };
        })();
        """;
        var replacements = new Dictionary<string, string>
        {
            ["ORIGINS"] = JsonSerializer.Serialize(origins),
            ["LOCALROOT"] = JsonSerializer.Serialize(localRoot),
            ["SCRIPTS"] = JsonSerializer.Serialize(scripts),
            ["BRIDGE"] = Resource("WebSearch.Bridge.js")
        };
        return System.Text.RegularExpressions.Regex.Replace(template, @"\b(?:ORIGINS|LOCALROOT|SCRIPTS|BRIDGE)\b",
            match => replacements[match.Value]);
    }
}

