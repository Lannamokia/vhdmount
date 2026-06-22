# 离线更新器使用指南

`Updater.exe` 是 VHDMounter 的离线更新工具，用于在没有网络连接或不允许机台直接访问公网的环境下，通过 USB 设备向机台下发已签名的更新包。

## 适用场景

- 发布 `VHDMounter.exe` / `VHDMounter_Maimoller.exe` 新版本
- 推送 VHD 数据补丁
- 机台处于内网或无法直接访问管理服务端更新接口的环境

## 前置条件

- 机台已部署 `Updater.exe`（通常与 `VHDMounter.exe` 位于同一目录）
- 已使用 `VHDMountAdminTools.exe` 或 Flutter 管理客户端离线工具生成签名密钥对
- 已使用离线工具生成 `manifest.json` 与 `manifest.sig`
- 准备一个卷标为 `NX_INS` 的 USB 存储设备

## 更新包结构

离线更新只需要两个文件：

| 文件 | 说明 |
|------|------|
| `manifest.json` | 更新清单，包含版本、文件列表、SHA-256 哈希 |
| `manifest.sig` | 对清单的 RSA-PSS SHA-256 签名 |

实际待更新的文件按清单中 `path` 字段的相对路径，与清单文件放在同一目录或子目录中。

### 清单示例

```json
{
  "version": "2026.06.22.120000",
  "minVersion": "1.7.0",
  "type": "app-update",
  "signer": "your-key-id",
  "createdAt": "2026-06-22T12:00:00Z",
  "expiresAt": "2026-06-25T12:00:00Z",
  "files": [
    {
      "path": "VHDMounter.exe",
      "target": "VHDMounter.exe",
      "size": 12345678,
      "sha256": "abcdef1234567890..."
    }
  ]
}
```

### 关键字段说明

| 字段 | 说明 |
|------|------|
| `version` | 本次更新版本号，更新成功后会写入 `update_done.flag` |
| `minVersion` | 最低兼容版本。机台当前版本高于此值时，Updater 会拒绝更新 |
| `type` | 必须为 `app-update`；`vhd-data` 类型不由 `Updater.exe` 处理 |
| `createdAt` / `expiresAt` | 清单有效期。`expiresAt` 缺失时按 `createdAt + 3 天` 兜底 |
| `files` | 待更新文件列表。`path` 为源文件相对路径，`target` 为目标路径（相对更新器目录或绝对路径） |

## 准备更新 USB

1. 将 USB 设备格式化为常见文件系统（FAT32/exFAT/NTFS 均可）
2. 将 USB 卷标设置为 **`NX_INS`**
3. 在 USB 根目录新建 `updates/` 子目录，或直接把更新文件放在根目录
4. 放入 `manifest.json`、`manifest.sig` 以及清单中引用的所有文件

支持两种放置方式：

```
NX_INS/
├── manifest.json
├── manifest.sig
└── VHDMounter.exe
```

或：

```
NX_INS/
└── updates/
    ├── manifest.json
    ├── manifest.sig
    └── VHDMounter.exe
```

## 机台端触发更新

Updater 的触发方式由主程序 `VHDMounter.exe` 决定。常见模式：

- **自动检测**：主程序启动或轮询时发现 `NX_INS` 设备，自动调用 `Updater.exe`
- **手动调用**：以管理员身份运行 `Updater.exe` 并传入参数

### 命令行参数

```powershell
Updater.exe --manifest <manifest路径> [--pid <主程序进程ID>]
```

| 参数 | 说明 |
|------|------|
| `--manifest` | 指定 `manifest.json` 的完整路径 |
| `--pid` | 可选。指定后主程序进程退出后 Updater 才开始替换，避免文件占用 |

示例：

```powershell
Updater.exe --manifest "D:\updates\manifest.json" --pid 1234
```

## 更新流程

1. **管理员权限自提升**：若未以管理员运行，Updater 会自动 `runas` 拉起自身
2. **清单验签**：使用同目录下的 `trusted_keys.pem` 验证 `manifest.sig`
3. **字段校验**：校验 `version`、`minVersion`、`type`、`createdAt`、`expiresAt`
4. **有效期检查**：过期清单拒绝执行
5. **版本兼容性检查**：读取 `update_done.flag`，若当前版本高于或等于 `minVersion` 则跳过
6. **等待主程序退出**：若指定 `--pid`，等待目标进程结束
7. **逐文件校验与替换**：
   - 校验文件大小和 SHA-256
   - 先拷贝为 `.staged` 临时文件
   - 调用 `MoveFileEx` 原子替换目标文件
   - 若目标文件被占用，则标记 `MOVEFILE_DELAY_UNTIL_REBOOT`，下次重启生效
8. **写入更新标记**：将 `version` 写入 `update_done.flag`
9. **重新拉起主程序**：优先启动 `VHDMounter_Maimoller.exe`，否则启动 `VHDMounter.exe`

## 日志与排障

Updater 会在同目录下生成 `updater.log`，超过 10 MB 自动循环覆盖。

### 常见退出码

| 退出码 | 含义 |
|--------|------|
| `0` | 成功（含跳过更新） |
| `1` | 管理员权限自提升失败 |
| `2` | 未指定 `--manifest` |
| `3` | 缺少 `trusted_keys.pem` |
| `4` | 缺少 `manifest.sig` |
| `5` | 清单签名验证失败 |
| `6` | 清单 `type` 不是 `app-update` |
| `7` | 文件哈希校验失败 |
| `8` | 清单日期解析失败 |
| `9` | 清单已过期 |
| `10` | 当前版本高于 `minVersion`，拒绝降级/重复更新 |

### 常见问题

**Q: 插入 USB 后主程序没有触发更新**

- 确认 USB 卷标为 `NX_INS`
- 确认 `manifest.json` 和 `manifest.sig` 放在根目录或 `updates/` 子目录
- 查看主程序日志中是否有 `UPDATE` 相关扫描记录

**Q: 更新提示签名验证失败**

- 确认机台目录存在 `trusted_keys.pem`
- 确认清单由该 `trusted_keys.pem` 中任意一把公钥对应的私钥签名
- 确认 `manifest.sig` 与 `manifest.json` 同名且位于同一目录

**Q: 某些文件提示“延迟到重启”**

- 目标文件正被占用，Updater 已注册 `PendingFileRenameOperations`
- 重启 Windows 后完成替换

**Q: 更新成功后主程序没有自动启动**

- 检查 `updater.log` 是否有“拉起主程序失败”
- 确认同目录下存在 `VHDMounter.exe` 或 `VHDMounter_Maimoller.exe`

**Q: 如何重新触发已经更新过的版本？**

- 修改清单 `version` 字段为一个新版本号
- 或删除机台目录下的 `update_done.flag`（不建议生产环境操作）

## UWF 与写保护注意事项

若机台启用了 UWF（Unified Write Filter）或类似写保护：

- 确保 `update_done.flag` 和待替换文件所在目录在 UWF 例外列表中
- 否则更新在重启后会被还原
- 对于标记为 `DELAY_UNTIL_REBOOT` 的替换，更需确保注册表和文件系统的持久化

## 安全建议

- 私钥仅用于签名更新清单，必须妥善保管
- `trusted_keys.pem` 可随客户端一起分发，但只应包含你信任的公钥
- 清单设置合理的 `expiresAt`，过期后即使 USB 遗失也不会被恶意利用
- 建议 `minVersion` 填写待更新目标版本，避免重复或错误版本更新
