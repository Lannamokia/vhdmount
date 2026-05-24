part of '../app.dart';

/// RustDesk 命名管道交互密钥（RustDeskClientSharedSecret）录入子页面
/// （任务 17.3 / 决策点 1 / Requirement 13.1）。
///
/// 顶部"录入新版本"按钮 → [_BridgeSecretInputDialog]，3 tab：hex / base64 / binary file。
/// 列表显示 secretVersion / createdAt / activatedAt / createdByUserId / auditNote，
/// 当前 active 高亮。
///
/// **不**展示任何字节级信息（明文 secret 仅在 TPM 包裹后下行机台）。
class BridgeSecretView extends StatefulWidget {
  const BridgeSecretView({
    super.key,
    required this.controller,
    this.embedInParentScroll = false,
  });

  final AppController controller;
  final bool embedInParentScroll;

  @override
  State<BridgeSecretView> createState() => _BridgeSecretViewState();
}

class _BridgeSecretViewState extends State<BridgeSecretView> {
  bool _initialLoaded = false;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) async {
      if (!_initialLoaded) {
        _initialLoaded = true;
        try {
          await widget.controller.loadBridgeSecretVersions();
        } catch (_) {}
      }
    });
  }

  @override
  Widget build(BuildContext context) {
    final controller = widget.controller;
    final versions = List<BridgeSecretVersionMetadata>.from(controller.bridgeSecretVersions)
      ..sort((a, b) => b.secretVersion.compareTo(a.secretVersion));

    final emptyState = Center(
      child: SizedBox(
        width: 460,
        child: const InfoPanel(
          title: '尚未录入任何 RustDeskClientSharedSecret 版本',
          body: Text(
            '点击右上角“录入新版本”输入 32 字节的共享密钥（hex / base64 / 二进制文件三选一）。\n'
            '录入成功后机台会立即拉取并热轮换到新版本，旧版本帧会被服务端拒收。',
          ),
          icon: Icons.key_rounded,
          color: AppPalette.coral,
        ),
      ),
    );

    final list = ListView.separated(
      itemCount: versions.length,
      shrinkWrap: widget.embedInParentScroll,
      physics: widget.embedInParentScroll
          ? const NeverScrollableScrollPhysics()
          : null,
      padding: EdgeInsets.zero,
      separatorBuilder: (_, _) => const SizedBox(height: 12),
      itemBuilder: (context, index) {
        final v = versions[index];
        return _BridgeSecretVersionCard(version: v);
      },
    );

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: <Widget>[
        PageHeader(
          eyebrow: 'RustDesk 远程控制',
          title: '命名管道交互密钥',
          subtitle: 'RustDeskClientSharedSecret 版本元数据。每次录入会生成一个新版本号 + 自动激活；明文仅在 TPM 包裹后下发机台。',
          actions: <Widget>[
            FilledButton.icon(
              onPressed: () => _showInputDialog(context, controller),
              icon: const Icon(Icons.add_rounded),
              label: const Text('录入新版本'),
            ),
            OutlinedButton.icon(
              onPressed: () async {
                try {
                  await controller.loadBridgeSecretVersions();
                } catch (_) {}
              },
              icon: const Icon(Icons.refresh_rounded),
              label: const Text('刷新'),
            ),
          ],
        ),
        const SizedBox(height: 16),
        if (controller.errorMessage != null) ...<Widget>[
          ErrorBanner(message: controller.errorMessage!),
          const SizedBox(height: 12),
        ],
        if (versions.isEmpty)
          widget.embedInParentScroll ? emptyState : Expanded(child: emptyState)
        else if (widget.embedInParentScroll)
          list
        else
          Expanded(child: list),
      ],
    );
  }

  Future<void> _showInputDialog(
    BuildContext context, AppController controller) async {
    final result = await showDialog<_BridgeSecretInput>(
      context: context,
      builder: (_) => const _BridgeSecretInputDialog(),
    );
    if (result == null) return;
    try {
      await controller.uploadBridgeSecret(
        format: result.format,
        rawBytes: result.rawBytes,
        auditNote: result.auditNote,
      );
      if (!context.mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('新版本已录入并激活')),
      );
    } catch (error) {
      if (!context.mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(describeError(error))),
      );
    }
  }
}

class _BridgeSecretVersionCard extends StatelessWidget {
  const _BridgeSecretVersionCard({required this.version});

  final BridgeSecretVersionMetadata version;

  @override
  Widget build(BuildContext context) {
    final accent = version.isActive ? AppPalette.mint : AppPalette.muted;
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(18),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: <Widget>[
            Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: <Widget>[
                AccentIconBadge(
                  icon: version.isActive
                      ? Icons.verified_user_rounded
                      : Icons.history_toggle_off_rounded,
                  color: accent,
                ),
                const SizedBox(width: 14),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: <Widget>[
                      Text(
                        '版本 v${version.secretVersion}',
                        style: Theme.of(context).textTheme.titleLarge,
                      ),
                      const SizedBox(height: 4),
                      Text(
                        '创建于 ${version.localizedCreatedAt}',
                        style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                              color: AppPalette.muted,
                            ),
                      ),
                    ],
                  ),
                ),
                StatusChip(
                  label: version.isActive ? '当前激活' : '已弃用',
                  color: accent,
                ),
              ],
            ),
            const SizedBox(height: 12),
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: <Widget>[
                StatusChip(
                  label: '激活时间 ${version.localizedActivatedAt}',
                  color: version.isActive ? AppPalette.mint : AppPalette.muted,
                ),
                if (version.createdByUserId != null && version.createdByUserId!.isNotEmpty)
                  StatusChip(
                    label: '创建者 ${version.createdByUserId}',
                    color: AppPalette.sky,
                  ),
              ],
            ),
            if (version.auditNote != null && version.auditNote!.isNotEmpty)
              Padding(
                padding: const EdgeInsets.only(top: 10),
                child: Text('审计备注: ${version.auditNote}'),
              ),
          ],
        ),
      ),
    );
  }
}

/// Bridge secret 录入对话框（决策点 1）。
///
/// 三 tab：hex / base64 / binary。每种 tab 实时校验解码后是否为正好 32 字节。
class _BridgeSecretInputDialog extends StatefulWidget {
  const _BridgeSecretInputDialog();

  @override
  State<_BridgeSecretInputDialog> createState() =>
      _BridgeSecretInputDialogState();
}

class _BridgeSecretInputDialogState extends State<_BridgeSecretInputDialog>
    with SingleTickerProviderStateMixin {
  late final TabController _tabController;
  final TextEditingController _hexCtl = TextEditingController();
  final TextEditingController _base64Ctl = TextEditingController();
  final TextEditingController _auditNoteCtl = TextEditingController();
  List<int>? _binaryBytes;
  String? _binaryFileName;

  @override
  void initState() {
    super.initState();
    _tabController = TabController(length: 3, vsync: this);
    _tabController.addListener(() {
      // 切 tab 时重绘以更新提交按钮 enable 状态
      setState(() {});
    });
    _hexCtl.addListener(() => setState(() {}));
    _base64Ctl.addListener(() => setState(() {}));
  }

  @override
  void dispose() {
    _tabController.dispose();
    _hexCtl.dispose();
    _base64Ctl.dispose();
    _auditNoteCtl.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return AlertDialog(
      title: const Text('录入新 RustDeskClientSharedSecret'),
      content: SizedBox(
        width: 520,
        height: 360,
        child: Column(
          children: <Widget>[
            TabBar(
              controller: _tabController,
              labelColor: AppPalette.mintDeep,
              unselectedLabelColor: AppPalette.muted,
              tabs: const <Tab>[
                Tab(text: '十六进制'),
                Tab(text: 'Base64'),
                Tab(text: '二进制文件'),
              ],
            ),
            const SizedBox(height: 12),
            Expanded(
              child: TabBarView(
                controller: _tabController,
                children: <Widget>[
                  _buildHexTab(),
                  _buildBase64Tab(),
                  _buildBinaryTab(),
                ],
              ),
            ),
            const SizedBox(height: 8),
            TextField(
              controller: _auditNoteCtl,
              maxLines: 2,
              decoration: const InputDecoration(
                labelText: '审计备注（可选）',
              ),
            ),
          ],
        ),
      ),
      actions: <Widget>[
        TextButton(
          onPressed: () => Navigator.of(context).pop(null),
          child: const Text('取消'),
        ),
        FilledButton(
          onPressed: _isSubmitEnabled() ? _onSubmit : null,
          child: const Text('提交并激活'),
        ),
      ],
    );
  }

  Widget _buildHexTab() {
    final bytes = _decodeHex(_hexCtl.text);
    final ok = bytes != null && bytes.length == 32;
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 8),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          TextField(
            controller: _hexCtl,
            maxLength: 64,
            inputFormatters: <TextInputFormatter>[
              FilteringTextInputFormatter.allow(RegExp(r'[0-9a-fA-F]')),
              LengthLimitingTextInputFormatter(64),
            ],
            decoration: InputDecoration(
              labelText: '64 位十六进制（32 字节）',
              hintText: '例如 ababababababababababababababababababababababababababababababab',
              suffixIcon: ok
                  ? const Icon(Icons.check_circle_rounded,
                      color: AppPalette.mintDeep)
                  : null,
            ),
          ),
          const SizedBox(height: 8),
          Text(
            ok ? '已解码 32 字节，校验通过' : '请输入 64 位 hex 字符',
            style: TextStyle(
              color: ok ? AppPalette.mintDeep : AppPalette.muted,
              fontSize: 12,
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildBase64Tab() {
    final bytes = _decodeBase64(_base64Ctl.text);
    final ok = bytes != null && bytes.length == 32;
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 8),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          TextField(
            controller: _base64Ctl,
            maxLength: 48,
            decoration: InputDecoration(
              labelText: 'Base64（解码后 32 字节，约 43-44 字符）',
              hintText: '例如 q6urq6urq6urq6urq6urq6urq6urq6urq6urq6urq6s=',
              suffixIcon: ok
                  ? const Icon(Icons.check_circle_rounded,
                      color: AppPalette.mintDeep)
                  : null,
            ),
          ),
          const SizedBox(height: 8),
          Text(
            ok ? '已解码 32 字节，校验通过' : '请粘贴标准 base64 字符串',
            style: TextStyle(
              color: ok ? AppPalette.mintDeep : AppPalette.muted,
              fontSize: 12,
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildBinaryTab() {
    final ok = _binaryBytes != null && _binaryBytes!.length == 32;
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 8),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          OutlinedButton.icon(
            onPressed: _pickBinaryFile,
            icon: const Icon(Icons.file_open_rounded),
            label: Text(_binaryFileName ?? '选择 32 字节二进制文件'),
          ),
          const SizedBox(height: 12),
          if (_binaryBytes != null)
            Text(
              ok
                  ? '文件大小校验通过（32 字节）'
                  : '文件大小不正确：${_binaryBytes!.length} 字节，需要 32 字节',
              style: TextStyle(
                color: ok ? AppPalette.mintDeep : AppPalette.coralDeep,
              ),
            ),
        ],
      ),
    );
  }

  Future<void> _pickBinaryFile() async {
    try {
      final result = await FilePicker.platform.pickFiles(
        type: FileType.any,
        allowMultiple: false,
        withData: true,
      );
      if (result == null || result.files.isEmpty) return;
      final file = result.files.first;
      final bytes = file.bytes ?? await _readAllBytes(file.path);
      if (bytes == null) return;
      setState(() {
        _binaryFileName = file.name;
        _binaryBytes = List<int>.from(bytes);
      });
    } catch (error) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('读取文件失败: $error')),
      );
    }
  }

  Future<List<int>?> _readAllBytes(String? path) async {
    if (path == null) return null;
    try {
      final file = File(path);
      return await file.readAsBytes();
    } catch (_) {
      return null;
    }
  }

  bool _isSubmitEnabled() {
    switch (_tabController.index) {
      case 0:
        return _decodeHex(_hexCtl.text)?.length == 32;
      case 1:
        return _decodeBase64(_base64Ctl.text)?.length == 32;
      case 2:
        return _binaryBytes != null && _binaryBytes!.length == 32;
    }
    return false;
  }

  void _onSubmit() {
    List<int>? bytes;
    BridgeSecretInputFormat format;
    switch (_tabController.index) {
      case 0:
        bytes = _decodeHex(_hexCtl.text);
        format = BridgeSecretInputFormat.hex;
        break;
      case 1:
        bytes = _decodeBase64(_base64Ctl.text);
        format = BridgeSecretInputFormat.base64;
        break;
      case 2:
        bytes = _binaryBytes;
        format = BridgeSecretInputFormat.binary;
        break;
      default:
        return;
    }
    if (bytes == null || bytes.length != 32) return;

    final auditNote = _auditNoteCtl.text.trim();
    Navigator.of(context).pop(
      _BridgeSecretInput(
        format: format,
        rawBytes: bytes,
        auditNote: auditNote.isEmpty ? null : auditNote,
      ),
    );
  }

  static List<int>? _decodeHex(String input) {
    final s = input.trim().replaceAll(' ', '');
    if (s.length != 64) return null;
    if (!RegExp(r'^[0-9a-fA-F]{64}$').hasMatch(s)) return null;
    final bytes = <int>[];
    for (var i = 0; i < s.length; i += 2) {
      bytes.add(int.parse(s.substring(i, i + 2), radix: 16));
    }
    return bytes;
  }

  static List<int>? _decodeBase64(String input) {
    final s = input.trim();
    if (s.isEmpty) return null;
    try {
      return base64Decode(s);
    } catch (_) {
      return null;
    }
  }
}

class _BridgeSecretInput {
  const _BridgeSecretInput({
    required this.format,
    required this.rawBytes,
    this.auditNote,
  });

  final BridgeSecretInputFormat format;
  final List<int> rawBytes;
  final String? auditNote;
}
