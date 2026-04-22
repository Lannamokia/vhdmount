# VHD Mounter 安装指南

本文档帮助用户完成 VHD Mounter 系统的完整部署，包括服务端、Windows 客户端和管理客户端三部分。

---

## 系统概述

VHD Mounter 是一套 VHD 挂载管理系统，包含以下组件：

| 组件 | 说明 |
|------|------|
| **VHDSelectServer** | Node.js 管理服务端，负责机台注册、VHD 选择、日志收集 |
| **VHDMounter** | Windows 客户端，扫描并挂载 VHD 到 `M:\` |
| **VHDMounter_Maimoller** | 增强版客户端，支持 Maimoller HID 系统菜单 |
| **管理客户端** | Flutter 跨平台应用，用于服务端初始化和管理 |

---

## 快速导航

- [服务端部署](server-setup)
- [Windows 客户端安装](client-setup)
- [管理客户端安装](admin-client)
- [常见问题](faq)
