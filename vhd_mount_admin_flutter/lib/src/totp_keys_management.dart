part of '../app.dart';

/// TOTP 密钥管理区域组件，显示在设置页面。
/// 列出所有活跃密钥，提供添加认证器、绑定生物识别和注销操作。
class TotpKeysManagementSection extends StatefulWidget {
  const TotpKeysManagementSection({
    super.key,
    required this.controller,
  });

  final AppController controller;

  @override
  State<TotpKeysManagementSection> createState() =>
      _TotpKeysManagementSectionState();
}

class _TotpKeysManagementSectionState
    extends State<TotpKeysManagementSection> {
  bool _isLoading = false;
  BiometricOtpService? _biometricService;
  bool _biometricAvailable = false;

  @override
  void initState() {
    super.initState();
    _initBiometric();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) _loadKeys();
    });
  }

  Future<void> _initBiometric() async {
    final service = createBiometricOtpService();
    if (service == null) return;
    final available = await service.isAvailable();
    if (mounted && available) {
      setState(() {
        _biometricService = service;
        _biometricAvailable = true;
      });
    }
  }

  Future<void> _loadKeys() async {
    setState(() => _isLoading = true);
    try {
      await widget.controller.loadTotpKeys();
    } catch (_) {
      // Error is shown via controller.errorMessage
    }
    if (mounted) {
      setState(() => _isLoading = false);
    }
  }

  Future<void> _addAuthenticator() async {
    final name = await _promptForKeyName(
      title: '添加认证器',
      hintText: '例如：Google Authenticator',
      submitLabel: '创建',
    );
    if (name == null || name.isEmpty) return;

    try {
      final result = await widget.controller.createTotpKey(
        name: name,
        type: 'authenticator',
      );
      if (!mounted) return;
      await _showNewKeyDialog(result);
    } catch (error) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(describeError(error))),
      );
    }
  }

  /// 弹出"输入密钥名称"对话框，返回 trim 后的名称；用户取消返回 null。
  Future<String?> _promptForKeyName({
    required String title,
    required String hintText,
    required String submitLabel,
    String? initialValue,
  }) async {
    final nameController = TextEditingController(text: initialValue ?? '');
    try {
      return await showDialog<String>(
        context: context,
        builder: (context) => AlertDialog(
          title: Text(title),
          content: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 360),
            child: TextField(
              controller: nameController,
              decoration: InputDecoration(
                labelText: '密钥名称',
                hintText: hintText,
              ),
              autofocus: true,
              maxLength: 128,
              onSubmitted: (value) =>
                  Navigator.of(context).pop(value.trim()),
            ),
          ),
          actions: <Widget>[
            TextButton(
              onPressed: () => Navigator.of(context).pop(null),
              child: const Text('取消'),
            ),
            FilledButton(
              onPressed: () =>
                  Navigator.of(context).pop(nameController.text.trim()),
              child: Text(submitLabel),
            ),
          ],
        ),
      );
    } finally {
      nameController.dispose();
    }
  }

  Future<void> _showNewKeyDialog(TotpKeyCreationResult result) async {
    final otpauthUrl = normalizeOtpauthUrl(result.otpauthUrl);
    final isMobile = Platform.isIOS || Platform.isAndroid;

    await showDialog<void>(
      context: context,
      barrierDismissible: false,
      builder: (context) => AlertDialog(
        title: const Text('绑定新认证器'),
        content: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 420),
          child: SingleChildScrollView(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: <Widget>[
                const Text('请使用验证器应用扫描下方二维码，或手动输入密钥。'),
                const SizedBox(height: 16),
                if (otpauthUrl.isNotEmpty)
                  Container(
                    padding: const EdgeInsets.all(16),
                    decoration: BoxDecoration(
                      color: Colors.white,
                      borderRadius: BorderRadius.circular(18),
                      border: Border.all(
                        color: AppPalette.mint.withValues(alpha: 0.2),
                      ),
                    ),
                    child: QrImageView(
                      data: otpauthUrl,
                      version: QrVersions.auto,
                      size: 200,
                      backgroundColor: Colors.white,
                      eyeStyle: const QrEyeStyle(
                        eyeShape: QrEyeShape.square,
                        color: AppPalette.ink,
                      ),
                      dataModuleStyle: const QrDataModuleStyle(
                        dataModuleShape: QrDataModuleShape.square,
                        color: AppPalette.mintDeep,
                      ),
                    ),
                  ),
                const SizedBox(height: 16),
                if (isMobile)
                  FilledButton.tonalIcon(
                    onPressed: () async {
                      final success = await launchOtpauthUrl(
                        secret: result.totpSecret,
                        account: 'admin',
                        issuer: 'VHDMountServer',
                      );
                      if (!context.mounted) return;
                      if (!success) {
                        ScaffoldMessenger.of(context).showSnackBar(
                          const SnackBar(
                            content: Text('无法打开系统验证器，请使用扫码或手动绑定。'),
                          ),
                        );
                      }
                    },
                    icon: const Icon(Icons.open_in_new_rounded),
                    label: Text(
                      Platform.isIOS ? '绑定到 iCloud 密码' : '绑定到验证器',
                    ),
                  ),
                const SizedBox(height: 12),
                Container(
                  width: double.infinity,
                  padding: const EdgeInsets.all(12),
                  decoration: BoxDecoration(
                    color: AppPalette.canvasWarm,
                    borderRadius: BorderRadius.circular(12),
                  ),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: <Widget>[
                      Text(
                        '密钥（Secret）',
                        style: Theme.of(context).textTheme.bodySmall,
                      ),
                      const SizedBox(height: 4),
                      SelectableText(
                        result.totpSecret,
                        style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                          fontFamily: 'monospace',
                          fontWeight: FontWeight.w600,
                        ),
                      ),
                    ],
                  ),
                ),
              ],
            ),
          ),
        ),
        actions: <Widget>[
          FilledButton(
            onPressed: () => Navigator.of(context).pop(),
            child: const Text('已完成绑定'),
          ),
        ],
      ),
    );
  }

  Future<void> _bindBiometric() async {
    if (_biometricService == null) return;

    // 1) 防御性检查：如果设备上已经存有绑定，先确认要不要覆盖
    if (await _biometricService!.isBound()) {
      if (!mounted) return;
      final overwrite = await showConfirmDialog(
        context,
        title: '已存在生物识别绑定',
        message:
            '当前设备已经绑定过 ${_biometricService!.displayName}。\n继续将覆盖现有绑定，并在服务端创建一条新的密钥（旧密钥可在列表中手动注销）。\n\n确认继续吗？',
        confirmLabel: '继续绑定',
      );
      if (overwrite != true) return;
    }

    // 2) 让用户为这台设备的生物识别密钥起名
    if (!mounted) return;
    final defaultName = _defaultBiometricKeyName();
    final name = await _promptForKeyName(
      title: '为生物识别密钥命名',
      hintText: '例如：${_biometricService!.displayName} (我的笔记本)',
      submitLabel: '继续',
      initialValue: defaultName,
    );
    if (name == null || name.isEmpty) return;

    // 3) 服务端先注册一条 biometric 类型密钥
    TotpKeyCreationResult? created;
    try {
      created = await widget.controller.createTotpKey(
        name: name,
        type: 'biometric',
        platform: _biometricService!.platformIdentifier,
      );
    } catch (error) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(describeError(error))),
      );
      return;
    }

    // 4) 本地通过生物识别认证后写入 secret
    try {
      await _biometricService!.bind(
        totpSecret: created.totpSecret,
        keyId: created.id,
      );
    } on BiometricBindCancelledException {
      // 用户取消：删除刚创建的服务端密钥，避免成为孤儿
      await _rollbackOrphanKey(created.id);
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('已取消生物识别绑定。')),
      );
      return;
    } catch (error) {
      // 其它错误（存储写入失败等）：同样回滚
      await _rollbackOrphanKey(created.id);
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(describeError(error))),
      );
      return;
    }

    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text('"$name" 已绑定成功。')),
    );
  }

  /// 生成默认的生物识别密钥名称：`<显示名> (<主机名>)`。
  /// 主机名读取失败（如某些平台抛异常）时退化为只用显示名。
  String _defaultBiometricKeyName() {
    final displayName = _biometricService!.displayName;
    String? host;
    try {
      host = Platform.localHostname;
    } catch (_) {
      host = null;
    }
    if (host == null || host.isEmpty || host == 'localhost') {
      return displayName;
    }
    return '$displayName ($host)';
  }

  /// 删除服务端那条已经创建、但本地绑定失败的"孤儿"密钥。
  /// 任何错误都吞掉：服务端如果删不掉，用户也能在列表里手动注销。
  Future<void> _rollbackOrphanKey(String keyId) async {
    try {
      await widget.controller.deleteTotpKey(keyId);
    } catch (_) {
      // 静默忽略；密钥仍可在列表中手动注销
    }
  }

  Future<void> _revokeKey(TotpKeyRecord key) async {
    final confirmed = await showConfirmDialog(
      context,
      title: '注销密钥',
      message: '确定要注销密钥"${key.name}"吗？注销后将无法使用该密钥进行 OTP 验证。',
      confirmLabel: '注销',
    );
    if (confirmed != true) return;

    try {
      await widget.controller.deleteTotpKey(key.id);
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('密钥"${key.name}"已注销。')),
      );
    } catch (error) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(describeError(error))),
      );
    }
  }

  IconData _typeIcon(TotpKeyRecord key) {
    if (key.isAuthenticator) return Icons.key_rounded;
    switch (key.platform) {
      case 'windows-hello':
        return Icons.fingerprint_rounded;
      case 'face-id':
        return Icons.face_rounded;
      case 'android-biometric':
        return Icons.fingerprint_rounded;
      default:
        return Icons.fingerprint_rounded;
    }
  }

  Color _typeColor(TotpKeyRecord key) {
    return key.isAuthenticator ? AppPalette.sky : AppPalette.mint;
  }

  @override
  Widget build(BuildContext context) {
    final keys = widget.controller.totpKeys;

    return SectionPanel(
      title: 'TOTP 密钥管理',
      subtitle: '管理已绑定的认证器和生物识别密钥。',
      icon: Icons.security_rounded,
      color: AppPalette.coral,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          Wrap(
            spacing: 12,
            runSpacing: 12,
            children: <Widget>[
              FilledButton.icon(
                onPressed: _addAuthenticator,
                icon: const Icon(Icons.add_rounded),
                label: const Text('添加认证器'),
              ),
              if (_biometricAvailable)
                FilledButton.tonalIcon(
                  onPressed: _bindBiometric,
                  icon: Icon(_biometricService!.icon),
                  label: Text('绑定${_biometricService!.displayName}'),
                ),
            ],
          ),
          const SizedBox(height: 16),
          if (_isLoading)
            const Center(
              child: Padding(
                padding: EdgeInsets.all(24),
                child: CircularProgressIndicator(),
              ),
            )
          else if (keys.isEmpty)
            const InfoPanel(
              title: '暂无 TOTP 密钥',
              body: Text('点击"添加认证器"创建第一个 TOTP 密钥。'),
              icon: Icons.vpn_key_off_rounded,
              color: AppPalette.sun,
            )
          else
            ...keys.map((key) => _buildKeyCard(key)),
        ],
      ),
    );
  }

  Widget _buildKeyCard(TotpKeyRecord key) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 12),
      child: Container(
        width: double.infinity,
        padding: const EdgeInsets.all(16),
        decoration: BoxDecoration(
          color: Colors.white.withValues(alpha: 0.78),
          borderRadius: BorderRadius.circular(18),
          border: Border.all(
            color: _typeColor(key).withValues(alpha: 0.2),
          ),
        ),
        child: LayoutBuilder(
          builder: (context, constraints) {
            final narrow = constraints.maxWidth < 460;
            if (narrow) {
              return Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: <Widget>[
                  _buildKeyHeader(key),
                  const SizedBox(height: 8),
                  _buildKeyDetails(key),
                  const SizedBox(height: 12),
                  _buildRevokeButton(key),
                ],
              );
            }
            return Row(
              crossAxisAlignment: CrossAxisAlignment.center,
              children: <Widget>[
                AccentIconBadge(
                  icon: _typeIcon(key),
                  color: _typeColor(key),
                  size: 42,
                ),
                const SizedBox(width: 14),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: <Widget>[
                      Text(
                        key.name,
                        style: Theme.of(context).textTheme.titleMedium,
                        overflow: TextOverflow.ellipsis,
                      ),
                      const SizedBox(height: 4),
                      Wrap(
                        spacing: 12,
                        runSpacing: 4,
                        children: <Widget>[
                          StatusChip(
                            label: key.displayType,
                            color: _typeColor(key),
                          ),
                          if (key.isBiometric && key.platform != null)
                            StatusChip(
                              label: key.displayPlatform,
                              color: AppPalette.mint,
                            ),
                        ],
                      ),
                      const SizedBox(height: 4),
                      Text(
                        '创建: ${key.localizedCreatedAt}  ·  最后使用: ${key.localizedLastUsedAt}',
                        style: Theme.of(context).textTheme.bodySmall,
                      ),
                    ],
                  ),
                ),
                const SizedBox(width: 12),
                _buildRevokeButton(key),
              ],
            );
          },
        ),
      ),
    );
  }

  Widget _buildKeyHeader(TotpKeyRecord key) {
    return Row(
      children: <Widget>[
        AccentIconBadge(
          icon: _typeIcon(key),
          color: _typeColor(key),
          size: 42,
        ),
        const SizedBox(width: 12),
        Expanded(
          child: Text(
            key.name,
            style: Theme.of(context).textTheme.titleMedium,
            overflow: TextOverflow.ellipsis,
          ),
        ),
      ],
    );
  }

  Widget _buildKeyDetails(TotpKeyRecord key) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: <Widget>[
        Wrap(
          spacing: 8,
          runSpacing: 4,
          children: <Widget>[
            StatusChip(label: key.displayType, color: _typeColor(key)),
            if (key.isBiometric && key.platform != null)
              StatusChip(label: key.displayPlatform, color: AppPalette.mint),
          ],
        ),
        const SizedBox(height: 4),
        Text(
          '创建: ${key.localizedCreatedAt}',
          style: Theme.of(context).textTheme.bodySmall,
        ),
        Text(
          '最后使用: ${key.localizedLastUsedAt}',
          style: Theme.of(context).textTheme.bodySmall,
        ),
      ],
    );
  }

  Widget _buildRevokeButton(TotpKeyRecord key) {
    return OutlinedButton.icon(
      onPressed: () => _revokeKey(key),
      icon: const Icon(Icons.delete_outline_rounded, size: 18),
      label: const Text('注销'),
      style: OutlinedButton.styleFrom(
        foregroundColor: AppPalette.danger,
        side: BorderSide(color: AppPalette.danger.withValues(alpha: 0.4)),
      ),
    );
  }
}
