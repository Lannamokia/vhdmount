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
    final nameController = TextEditingController();
    final name = await showDialog<String>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('添加认证器'),
        content: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 360),
          child: TextField(
            controller: nameController,
            decoration: const InputDecoration(
              labelText: '密钥名称',
              hintText: '例如：Google Authenticator',
            ),
            autofocus: true,
            onSubmitted: (value) => Navigator.of(context).pop(value.trim()),
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
            child: const Text('创建'),
          ),
        ],
      ),
    );
    nameController.dispose();

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

    try {
      final result = await widget.controller.createTotpKey(
        name: '${_biometricService!.displayName} (${Platform.localHostname})',
        type: 'biometric',
        platform: _biometricService!.platformIdentifier,
      );

      await _biometricService!.bind(
        totpSecret: result.totpSecret,
        keyId: result.id,
      );

      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text('${_biometricService!.displayName} 已绑定成功。'),
        ),
      );
    } catch (error) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(describeError(error))),
      );
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
