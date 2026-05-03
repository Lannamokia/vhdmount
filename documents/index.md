# VHDMount 安装指南

本文档帮助用户完成 VHD Mounter 系统的完整部署，包括服务端、Windows 客户端和管理客户端三部分。

::: info 快速开始
如果你是第一次接触本项目，建议按下面顺序阅读：

1. [服务端部署](./server-setup)
2. [Windows 客户端安装](./client-setup)
3. [管理客户端安装](./admin-client)
4. [管理者指南](./admin-guide)
:::

## 页面入口

<div class="vp-doc">

| 页面 | 适用对象 | 说明 |
|------|----------|------|
| [服务端部署](./server-setup) | 管理员 | 服务端安装、初始化、HTTPS 建议 |
| [Windows 客户端安装](./client-setup) | 管理员 | 客户端安装、配置、Shell Launcher、加固 |
| [管理客户端安装](./admin-client) | 管理员 | Flutter 管理端安装与新功能入口 |
| [管理者指南](./admin-guide) | 管理员 | 机台审批、证书管理、部署管理、OTP 规则 |
| [Maimoller HID 系统菜单](./maimoller) | 与maimoller手台配套使用的用户 | HID 面板、系统菜单、按键映射 |
| [常见问题](./faq) | 所有人 | 常见故障与排查建议 |

</div>

---

## 系统概述

VHD Mounter 是一套 VHD 挂载管理系统，包含以下组件：

| 组件 | 说明 |
|------|------|
| **VHDSelectServer** | Node.js 管理服务端，负责机台注册、VHD 选择、日志收集 |
| **VHDMounter** | Windows 客户端，扫描并挂载 VHD 到 `M:\`并启动游戏 |
| **VHDMounter_Maimoller** | 增强版客户端，支持 Maimoller HID 系统菜单 |
| **管理客户端** | Flutter 跨平台应用，用于服务端初始化和管理 |

