import { defineConfig } from 'vitepress'

const guideSidebar = [
  {
    text: '开始使用',
    items: [
      { text: '首页', link: '/' },
      { text: '服务端部署', link: '/server-setup' },
      { text: 'Windows 客户端安装', link: '/client-setup' }
    ]
  },
  {
    text: '客户端与外设',
    items: [
      { text: '管理客户端安装', link: '/admin-client' },
      { text: 'Maimoller HID 系统菜单', link: '/maimoller' },
      { text: '离线更新器', link: '/updater' }
    ]
  },
  {
    text: '运维与管理',
    items: [
      { text: '管理者指南', link: '/admin-guide' },
      { text: 'RustDesk 远程控制桥接', link: '/rustdesk-bridge-admin-guide' },
      { text: '常见问题', link: '/faq' }
    ]
  }
]

export default defineConfig({
  title: 'VHDMount Wiki',
  description: 'VHDMount 安装与使用指南',
  cleanUrls: true,
  srcDir: '.',
  ignoreDeadLinks: true,
  themeConfig: {
    logo: '/favicon.ico',
    nav: [
      { text: '首页', link: '/' },
      { text: '服务端部署', link: '/server-setup' },
      { text: '客户端安装', link: '/client-setup' },
      { text: '管理客户端', link: '/admin-client' },
      { text: '管理者指南', link: '/admin-guide' },
      { text: 'RustDesk 桥接', link: '/rustdesk-bridge-admin-guide' },
      { text: '离线更新器', link: '/updater' },
      { text: '常见问题', link: '/faq' }
    ],
    sidebar: {
      '/': guideSidebar
    },
    outline: {
      level: [2, 3],
      label: '本页导航'
    },
    search: {
      provider: 'local'
    },
    docFooter: {
      prev: '上一页',
      next: '下一页'
    },
    lastUpdated: {
      text: '最后更新于'
    },
    footer: {
      message: 'VHD Mounter 文档站已迁移至 VitePress',
      copyright: 'Copyright © VHD Mounter Contributors'
    }
  }
})
