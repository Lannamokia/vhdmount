# VitePress 文档站

`documents/` 已从 Jekyll 切换到 VitePress。

## 本地开发

```powershell
cd documents
npm install
npm run docs:dev
```

默认预览地址：

- `http://127.0.0.1:4000`

## 生成静态站点

```powershell
cd documents
npm run docs:build
```

构建产物输出到：

- `documents/.vitepress/dist/`

## 说明

- 文档站已完全切换到 VitePress，Jekyll 运行依赖已移除
- 站点导航、侧栏和页内大纲统一由 `.vitepress/config.mts` 管理
- 自定义前端组件位于 `.vitepress/theme/components/`
