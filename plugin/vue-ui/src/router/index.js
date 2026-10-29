import { createRouter, createWebHashHistory } from 'vue-router';

const routes = [
  {
    path: '/',
    component: () => import('@/layouts/AppLayout.vue'),
    redirect: '/settings/websites',
    children: [
      {
        path: 'settings/websites',
        name: 'Websites',
        component: () => import('@/views/WebsitesSettings.vue'),
        meta: {
          title: '网站入口',
          description: '固定网址与搜索模板，一键跳转',
        },
      },
      {
        path: 'settings/websites/new',
        name: 'WebsiteCreate',
        component: () => import('@/views/WebsiteEditorView.vue'),
        meta: {
          editor: true,
        },
      },
      {
        path: 'settings/websites/:id/edit',
        name: 'WebsiteEdit',
        component: () => import('@/views/WebsiteEditorView.vue'),
        meta: {
          editor: true,
        },
      },
      {
        path: 'settings/browser',
        name: 'Browser',
        component: () => import('@/views/BrowserSettings.vue'),
        meta: {
          title: '收藏夹',
          description: '浏览本机浏览器收藏并同步',
        },
      },
      {
        path: 'settings/guide',
        name: 'Guide',
        component: () => import('@/views/UsageGuideView.vue'),
        meta: {
          title: '使用说明',
          description: '网站入口、收藏夹、内置窗口与脚本',
        },
      },
    ],
  },
];

const router = createRouter({
  history: createWebHashHistory(),
  routes,
});

window.router = {
  push: (path) => router.push(path),
  replace: (path) => router.replace(path),
  back: () => router.back(),
  current: () => router.currentRoute.value.fullPath,
};

export default router;
