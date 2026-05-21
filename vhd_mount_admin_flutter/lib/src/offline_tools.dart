part of '../app.dart';

/// 离线工具页面容器，包含标签页切换。
/// 标签页：密钥生成、清单打包、证书包、软件部署打包器。
class OfflineToolsView extends StatefulWidget {
  const OfflineToolsView({
    super.key,
    required this.controller,
    this.embedInParentScroll = false,
  });

  final AppController controller;
  final bool embedInParentScroll;

  @override
  State<OfflineToolsView> createState() => _OfflineToolsViewState();
}

class _OfflineToolsViewState extends State<OfflineToolsView>
    with SingleTickerProviderStateMixin {
  late final TabController _tabController;

  static const _tabs = <String>['密钥生成', '清单打包', '证书包', '软件部署打包器'];

  @override
  void initState() {
    super.initState();
    _tabController = TabController(length: _tabs.length, vsync: this);
  }

  @override
  void dispose() {
    _tabController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: <Widget>[
        PageHeader(
          eyebrow: 'Offline Tools',
          title: '离线工具',
          subtitle: '密钥生成、清单打包与签名、证书包生成、软件部署打包。',
        ),
        const SizedBox(height: 16),
        Material(
          color: Colors.transparent,
          child: TabBar(
            controller: _tabController,
            isScrollable: true,
            tabAlignment: TabAlignment.start,
            indicatorSize: TabBarIndicatorSize.label,
            dividerColor: AppPalette.border.withValues(alpha: 0.5),
            labelStyle: Theme.of(context).textTheme.labelLarge?.copyWith(
              fontWeight: FontWeight.w600,
            ),
            unselectedLabelStyle: Theme.of(context).textTheme.labelLarge,
            tabs: _tabs
                .map((label) => Tab(text: label))
                .toList(growable: false),
          ),
        ),
        const SizedBox(height: 16),
        Expanded(
          child: TabBarView(
            controller: _tabController,
            children: <Widget>[
              // 密钥生成
              _KeyGeneratorPage(controller: widget.controller),
              // 清单打包
              ManifestPackagerPage(controller: widget.controller),
              // 证书包生成
              CertificateGeneratorPage(controller: widget.controller),
              // 软件部署打包器 — 复用现有 LocalPackagerDialog 内容
              _EmbeddedLocalPackager(controller: widget.controller),
            ],
          ),
        ),
      ],
    );
  }
}

/// 密钥生成页面，调用 KeyGeneratorService 生成 RSA 3072 位签名密钥对。
class _KeyGeneratorPage extends StatefulWidget {
  const _KeyGeneratorPage({required this.controller});

  final AppController controller;

  @override
  State<_KeyGeneratorPage> createState() => _KeyGeneratorPageState();
}

class _KeyGeneratorPageState extends State<_KeyGeneratorPage>
    with AutomaticKeepAliveClientMixin {
  final TextEditingController _keyIdController = TextEditingController();
  final TextEditingController _outputDirController = TextEditingController();

  bool _isGenerating = false;
  double _progress = 0.0;
  String _step = '';
  KeyGeneratorResult? _result;
  int? _bgOpIndex;

  @override
  bool get wantKeepAlive => true;

  @override
  void initState() {
    super.initState();
    _checkBackgroundResult();
  }

  /// 检查 controller 中是否有已完成的密钥生成后台操作结果。
  void _checkBackgroundResult() {
    for (int i = widget.controller.backgroundOperations.length - 1;
        i >= 0;
        i--) {
      final op = widget.controller.backgroundOperations[i];
      if (op.type == BackgroundOperationType.keyGeneration &&
          op.status != BackgroundOperationStatus.running) {
        // 恢复已完成/失败的结果
        setState(() {
          _isGenerating = false;
          if (op.status == BackgroundOperationStatus.completed) {
            // 结果消息已存储，直接显示
            _result = null; // 无法恢复完整 result 对象，用 _bgOpIndex 显示
            _bgOpIndex = i;
          } else {
            _bgOpIndex = i;
          }
        });
        break;
      }
      if (op.type == BackgroundOperationType.keyGeneration &&
          op.status == BackgroundOperationStatus.running) {
        // 操作仍在运行中
        setState(() {
          _isGenerating = true;
          _progress = op.progress;
          _step = op.step;
          _bgOpIndex = i;
        });
        // 监听 controller 变化以更新进度
        widget.controller.addListener(_onControllerChanged);
        break;
      }
    }
  }

  void _onControllerChanged() {
    if (_bgOpIndex == null || !mounted) return;
    final idx = _bgOpIndex!;
    if (idx >= widget.controller.backgroundOperations.length) return;
    final op = widget.controller.backgroundOperations[idx];
    if (op.type != BackgroundOperationType.keyGeneration) return;

    setState(() {
      _progress = op.progress;
      _step = op.step;
      if (op.status != BackgroundOperationStatus.running) {
        _isGenerating = false;
        widget.controller.removeListener(_onControllerChanged);
      }
    });
  }

  @override
  void dispose() {
    widget.controller.removeListener(_onControllerChanged);
    _keyIdController.dispose();
    _outputDirController.dispose();
    super.dispose();
  }

  Future<void> _pickOutputDir() async {
    final result = await FilePicker.platform.getDirectoryPath();
    if (result != null && result.isNotEmpty) {
      setState(() {
        _outputDirController.text = result;
      });
    }
  }

  Future<void> _generate() async {
    final outputDir = _outputDirController.text.trim();
    if (outputDir.isEmpty) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('请选择输出目录')),
      );
      return;
    }

    // 清除之前的后台操作结果
    if (_bgOpIndex != null) {
      widget.controller.dismissBackgroundOperation(_bgOpIndex!);
      _bgOpIndex = null;
    }

    final opIndex = widget.controller.startBackgroundOperation(
      BackgroundOperationType.keyGeneration,
      '准备开始...',
    );
    _bgOpIndex = opIndex;

    setState(() {
      _isGenerating = true;
      _progress = 0.0;
      _step = '准备开始...';
      _result = null;
    });

    // 监听 controller 变化（用于从导航返回时恢复状态）
    widget.controller.addListener(_onControllerChanged);

    try {
      final service = KeyGeneratorService();
      final keyId = _keyIdController.text.trim();
      final result = await service.generate(
        keyId: keyId.isEmpty ? null : keyId,
        outputDir: outputDir,
        onProgress: (progress, step) {
          widget.controller.updateBackgroundOperationProgress(
            opIndex,
            progress,
            step,
          );
          if (mounted) {
            setState(() {
              _progress = progress;
              _step = step;
            });
          }
        },
      );

      final resultMessage = '私钥: ${result.privateKeyPath}\n'
          '公钥: ${result.publicKeyPath}\n'
          '可信密钥: ${result.trustedKeysPath}';
      widget.controller.completeBackgroundOperation(opIndex, resultMessage);

      if (mounted) {
        widget.controller.removeListener(_onControllerChanged);
        setState(() {
          _isGenerating = false;
          _result = result;
        });
      }
    } catch (error) {
      widget.controller.failBackgroundOperation(
        opIndex,
        describeError(error),
      );

      if (mounted) {
        widget.controller.removeListener(_onControllerChanged);
        setState(() {
          _isGenerating = false;
        });
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(describeError(error))),
        );
      }
    }
  }

  Widget _buildPathPicker({
    required String label,
    required TextEditingController controller,
    required VoidCallback onPick,
  }) {
    return LayoutBuilder(
      builder: (context, constraints) {
        final narrow = constraints.maxWidth < 430;
        final field = TextField(
          controller: controller,
          decoration: InputDecoration(
            labelText: label,
            hintText: '选择目录',
          ),
          readOnly: true,
        );
        final button = OutlinedButton.icon(
          onPressed: _isGenerating ? null : onPick,
          icon: const Icon(Icons.folder_open_rounded),
          label: const Text('浏览'),
        );

        if (narrow) {
          return Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: <Widget>[
              field,
              const SizedBox(height: 10),
              Align(alignment: Alignment.centerLeft, child: button),
            ],
          );
        }

        return Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: <Widget>[
            Expanded(child: field),
            const SizedBox(width: 10),
            button,
          ],
        );
      },
    );
  }

  @override
  Widget build(BuildContext context) {
    super.build(context);
    final theme = Theme.of(context);

    return SingleChildScrollView(
      padding: const EdgeInsets.symmetric(horizontal: 4),
      child: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 640),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: <Widget>[
            TextField(
              controller: _keyIdController,
              decoration: const InputDecoration(
                labelText: '密钥标识符（可选）',
                hintText: '留空使用默认 update-key-{yyyyMMdd}',
              ),
              enabled: !_isGenerating,
            ),
            const SizedBox(height: 12),
            _buildPathPicker(
              label: '输出目录',
              controller: _outputDirController,
              onPick: _pickOutputDir,
            ),
            const SizedBox(height: 16),
            Text(
              '生成 RSA 3072 位密钥对，输出 PKCS#8 私钥 PEM、SPKI 公钥 PEM，'
              '并将公钥追加到 trusted_keys.pem。',
              style: theme.textTheme.bodySmall?.copyWith(
                color: AppPalette.muted,
              ),
            ),
            if (_isGenerating) ...<Widget>[
              const SizedBox(height: 16),
              LinearProgressIndicator(value: _progress),
              const SizedBox(height: 8),
              Text(_step, style: theme.textTheme.bodySmall),
            ],
            if (_result != null) ...<Widget>[
              const SizedBox(height: 16),
              Container(
                padding: const EdgeInsets.all(12),
                decoration: BoxDecoration(
                  color: AppPalette.mint.withValues(alpha: 0.1),
                  borderRadius: BorderRadius.circular(12),
                  border: Border.all(
                    color: AppPalette.mint.withValues(alpha: 0.3),
                  ),
                ),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: <Widget>[
                    Text(
                      '密钥生成完成',
                      style: theme.textTheme.titleSmall?.copyWith(
                        color: AppPalette.mintDeep,
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                    const SizedBox(height: 8),
                    _OutputPathRow(
                      label: '私钥',
                      path: _result!.privateKeyPath,
                    ),
                    const SizedBox(height: 4),
                    _OutputPathRow(
                      label: '公钥',
                      path: _result!.publicKeyPath,
                    ),
                    const SizedBox(height: 4),
                    _OutputPathRow(
                      label: '可信密钥',
                      path: _result!.trustedKeysPath,
                    ),
                  ],
                ),
              ),
            ] else if (_bgOpIndex != null &&
                _bgOpIndex! < widget.controller.backgroundOperations.length &&
                !_isGenerating) ...<Widget>[
              // 从后台操作恢复的结果（用户导航离开后返回时显示）
              _buildBackgroundOperationResult(theme),
            ],
            const SizedBox(height: 20),
            Row(
              children: <Widget>[
                if (!_isGenerating && _result == null && !_hasBackgroundResult)
                  FilledButton.icon(
                    onPressed: _generate,
                    icon: const Icon(Icons.vpn_key_rounded),
                    label: const Text('生成'),
                  ),
                if (!_isGenerating && (_result != null || _hasBackgroundResult))
                  FilledButton.icon(
                    onPressed: () {
                      if (_bgOpIndex != null) {
                        widget.controller
                            .dismissBackgroundOperation(_bgOpIndex!);
                        _bgOpIndex = null;
                      }
                      setState(() {
                        _result = null;
                      });
                    },
                    icon: const Icon(Icons.refresh_rounded),
                    label: const Text('重新生成'),
                  ),
              ],
            ),
            const SizedBox(height: 24),
          ],
        ),
      ),
    );
  }

  bool get _hasBackgroundResult {
    if (_bgOpIndex == null) return false;
    if (_bgOpIndex! >= widget.controller.backgroundOperations.length) {
      return false;
    }
    final op = widget.controller.backgroundOperations[_bgOpIndex!];
    return op.type == BackgroundOperationType.keyGeneration &&
        op.status != BackgroundOperationStatus.running;
  }

  Widget _buildBackgroundOperationResult(ThemeData theme) {
    final op = widget.controller.backgroundOperations[_bgOpIndex!];
    final isSuccess = op.status == BackgroundOperationStatus.completed;
    return Container(
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: isSuccess
            ? AppPalette.mint.withValues(alpha: 0.1)
            : AppPalette.danger.withValues(alpha: 0.1),
        borderRadius: BorderRadius.circular(12),
        border: Border.all(
          color: isSuccess
              ? AppPalette.mint.withValues(alpha: 0.3)
              : AppPalette.danger.withValues(alpha: 0.3),
        ),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          Text(
            isSuccess ? '密钥生成完成' : '密钥生成失败',
            style: theme.textTheme.titleSmall?.copyWith(
              color: isSuccess ? AppPalette.mintDeep : AppPalette.danger,
              fontWeight: FontWeight.w600,
            ),
          ),
          if (op.resultMessage != null) ...[
            const SizedBox(height: 8),
            SelectableText(
              op.resultMessage!,
              style: theme.textTheme.bodySmall?.copyWith(
                color: isSuccess ? AppPalette.mintDeep : AppPalette.danger,
                fontFamily: 'monospace',
              ),
            ),
          ],
        ],
      ),
    );
  }
}

/// 输出路径行，用于显示生成结果中的文件路径。
class _OutputPathRow extends StatelessWidget {
  const _OutputPathRow({required this.label, required this.path});

  final String label;
  final String path;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: <Widget>[
        SizedBox(
          width: 64,
          child: Text(
            '$label：',
            style: theme.textTheme.bodySmall?.copyWith(
              color: AppPalette.muted,
            ),
          ),
        ),
        Expanded(
          child: SelectableText(
            path,
            style: theme.textTheme.bodySmall?.copyWith(
              color: AppPalette.mintDeep,
              fontFamily: 'monospace',
            ),
          ),
        ),
      ],
    );
  }
}

/// 嵌入式本地打包器，复用 LocalPackagerDialog 的核心逻辑但以页面形式展示。
class _EmbeddedLocalPackager extends StatefulWidget {
  const _EmbeddedLocalPackager({required this.controller});

  final AppController controller;

  @override
  State<_EmbeddedLocalPackager> createState() => _EmbeddedLocalPackagerState();
}

class _EmbeddedLocalPackagerState extends State<_EmbeddedLocalPackager>
    with AutomaticKeepAliveClientMixin {
  final TextEditingController _nameController = TextEditingController(
    text: '配套工具',
  );
  final TextEditingController _versionController = TextEditingController(
    text: '1.0.0',
  );
  final TextEditingController _signerController = TextEditingController(
    text: 'admin',
  );
  final TextEditingController _installScriptController =
      TextEditingController();
  final TextEditingController _uninstallScriptController =
      TextEditingController();
  final TextEditingController _payloadDirController = TextEditingController();
  final TextEditingController _privateKeyController = TextEditingController();
  final TextEditingController _outputDirController = TextEditingController();
  final TextEditingController _targetPathController = TextEditingController();

  String _type = 'software-deploy';
  bool _isPacking = false;
  bool _requiresAdmin = false;
  double _progress = 0.0;
  String _step = '';
  String? _resultMessage;
  bool _resultIsError = false;
  int? _bgOpIndex;

  @override
  bool get wantKeepAlive => true;

  @override
  void initState() {
    super.initState();
    _checkBackgroundResult();
  }

  void _checkBackgroundResult() {
    for (int i = widget.controller.backgroundOperations.length - 1;
        i >= 0;
        i--) {
      final op = widget.controller.backgroundOperations[i];
      if (op.type == BackgroundOperationType.deploymentPackaging &&
          op.status != BackgroundOperationStatus.running) {
        setState(() {
          _isPacking = false;
          _resultMessage = op.resultMessage;
          _resultIsError = op.isError;
          _bgOpIndex = i;
        });
        break;
      }
      if (op.type == BackgroundOperationType.deploymentPackaging &&
          op.status == BackgroundOperationStatus.running) {
        setState(() {
          _isPacking = true;
          _progress = op.progress;
          _step = op.step;
          _bgOpIndex = i;
        });
        widget.controller.addListener(_onControllerChanged);
        break;
      }
    }
  }

  void _onControllerChanged() {
    if (_bgOpIndex == null || !mounted) return;
    final idx = _bgOpIndex!;
    if (idx >= widget.controller.backgroundOperations.length) return;
    final op = widget.controller.backgroundOperations[idx];
    if (op.type != BackgroundOperationType.deploymentPackaging) return;

    setState(() {
      _progress = op.progress;
      _step = op.step;
      if (op.status != BackgroundOperationStatus.running) {
        _isPacking = false;
        _resultMessage = op.resultMessage;
        _resultIsError = op.isError;
        widget.controller.removeListener(_onControllerChanged);
      }
    });
  }

  @override
  void dispose() {
    widget.controller.removeListener(_onControllerChanged);
    _nameController.dispose();
    _versionController.dispose();
    _signerController.dispose();
    _installScriptController.dispose();
    _uninstallScriptController.dispose();
    _payloadDirController.dispose();
    _privateKeyController.dispose();
    _outputDirController.dispose();
    _targetPathController.dispose();
    super.dispose();
  }

  Future<void> _pickInstallScript() async {
    final result = await FilePicker.platform.pickFiles(
      type: FileType.custom,
      allowedExtensions: <String>['ps1'],
      allowMultiple: false,
      withData: false,
    );
    if (result != null &&
        result.files.isNotEmpty &&
        result.files.first.path != null) {
      setState(() {
        _installScriptController.text = result.files.first.path!;
      });
    }
  }

  Future<void> _pickUninstallScript() async {
    final result = await FilePicker.platform.pickFiles(
      type: FileType.custom,
      allowedExtensions: <String>['ps1'],
      allowMultiple: false,
      withData: false,
    );
    if (result != null &&
        result.files.isNotEmpty &&
        result.files.first.path != null) {
      setState(() {
        _uninstallScriptController.text = result.files.first.path!;
      });
    }
  }

  Future<void> _pickPrivateKey() async {
    final result = await FilePicker.platform.pickFiles(
      type: FileType.custom,
      allowedExtensions: <String>['pem'],
      allowMultiple: false,
      withData: false,
    );
    if (result != null &&
        result.files.isNotEmpty &&
        result.files.first.path != null) {
      setState(() {
        _privateKeyController.text = result.files.first.path!;
      });
    }
  }

  Future<void> _pickPayloadDir() async {
    final result = await FilePicker.platform.getDirectoryPath();
    if (result != null && result.isNotEmpty) {
      setState(() {
        _payloadDirController.text = result;
      });
    }
  }

  Future<void> _pickOutputDir() async {
    final result = await FilePicker.platform.getDirectoryPath();
    if (result != null && result.isNotEmpty) {
      setState(() {
        _outputDirController.text = result;
      });
    }
  }

  Future<void> _pack() async {
    final name = _nameController.text.trim();
    final version = _versionController.text.trim();
    final signer = _signerController.text.trim();
    final installScriptPath = _installScriptController.text.trim();
    final uninstallScriptPath = _uninstallScriptController.text.trim();
    final payloadDir = _payloadDirController.text.trim();
    final privateKeyPath = _privateKeyController.text.trim();
    final outputDir = _outputDirController.text.trim();
    final targetPath = _targetPathController.text.trim();

    if (name.isEmpty || version.isEmpty || signer.isEmpty) {
      setState(() {
        _resultMessage = '包名称、版本号和签名者不能为空';
        _resultIsError = true;
      });
      return;
    }
    if (privateKeyPath.isEmpty) {
      setState(() {
        _resultMessage = '请选择私钥文件';
        _resultIsError = true;
      });
      return;
    }
    if (_type == 'software-deploy' && installScriptPath.isEmpty) {
      setState(() {
        _resultMessage = 'software-deploy 类型必须提供安装脚本';
        _resultIsError = true;
      });
      return;
    }
    if (_type == 'software-deploy' && uninstallScriptPath.isEmpty) {
      setState(() {
        _resultMessage = 'software-deploy 类型必须提供卸载脚本';
        _resultIsError = true;
      });
      return;
    }
    if (_type == 'file-deploy') {
      if (targetPath.isEmpty) {
        setState(() {
          _resultMessage = 'file-deploy 类型必须填写机台目标部署路径';
          _resultIsError = true;
        });
        return;
      }
      if (payloadDir.isEmpty) {
        setState(() {
          _resultMessage = 'file-deploy 类型必须选择文件负载目录';
          _resultIsError = true;
        });
        return;
      }
    }
    if (outputDir.isEmpty) {
      setState(() {
        _resultMessage = '请选择输出目录';
        _resultIsError = true;
      });
      return;
    }

    setState(() {
      _isPacking = true;
      _progress = 0.0;
      _step = '准备开始...';
      _resultMessage = null;
      _resultIsError = false;
    });

    // 清除之前的后台操作结果
    if (_bgOpIndex != null) {
      widget.controller.dismissBackgroundOperation(_bgOpIndex!);
      _bgOpIndex = null;
    }

    final opIndex = widget.controller.startBackgroundOperation(
      BackgroundOperationType.deploymentPackaging,
      '准备开始...',
    );
    _bgOpIndex = opIndex;

    widget.controller.addListener(_onControllerChanged);

    try {
      final packager = DeploymentPackager();
      final result = await packager.packAndSign(
        type: _type,
        installScriptPath: installScriptPath.isEmpty ? null : installScriptPath,
        uninstallScriptPath:
            uninstallScriptPath.isEmpty ? null : uninstallScriptPath,
        payloadDir: payloadDir.isEmpty ? null : payloadDir,
        name: name,
        version: version,
        signer: signer,
        targetPath: targetPath.isEmpty ? null : targetPath,
        requiresAdmin: _requiresAdmin,
        privateKeyPath: privateKeyPath,
        outputDir: outputDir,
        onProgress: (progress, step) {
          widget.controller.updateBackgroundOperationProgress(
            opIndex,
            progress,
            step,
          );
          if (mounted) {
            setState(() {
              _progress = progress;
              _step = step;
            });
          }
        },
      );

      final resultMessage = '打包完成：\n${result.zipPath}\n${result.sigPath}';
      widget.controller.completeBackgroundOperation(opIndex, resultMessage);

      if (mounted) {
        widget.controller.removeListener(_onControllerChanged);
        setState(() {
          _isPacking = false;
          _resultMessage = resultMessage;
          _resultIsError = false;
        });
      }
    } catch (error) {
      final errorMsg = '打包失败：$error';
      widget.controller.failBackgroundOperation(opIndex, errorMsg);

      if (mounted) {
        widget.controller.removeListener(_onControllerChanged);
        setState(() {
          _isPacking = false;
          _resultMessage = errorMsg;
          _resultIsError = true;
        });
      }
    }
  }

  Widget _buildPathPicker({
    required String label,
    required TextEditingController controller,
    required VoidCallback onPick,
    bool isDirectory = false,
  }) {
    return LayoutBuilder(
      builder: (context, constraints) {
        final narrow = constraints.maxWidth < 430;
        final field = TextField(
          controller: controller,
          decoration: InputDecoration(
            labelText: label,
            hintText: isDirectory ? '选择目录' : '选择文件',
          ),
          readOnly: true,
        );
        final button = OutlinedButton.icon(
          onPressed: _isPacking ? null : onPick,
          icon: Icon(
            isDirectory ? Icons.folder_open_rounded : Icons.file_open_rounded,
          ),
          label: Text(isDirectory ? '浏览' : '选择'),
        );

        if (narrow) {
          return Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: <Widget>[
              field,
              const SizedBox(height: 10),
              Align(alignment: Alignment.centerLeft, child: button),
            ],
          );
        }

        return Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: <Widget>[
            Expanded(child: field),
            const SizedBox(width: 10),
            button,
          ],
        );
      },
    );
  }

  @override
  Widget build(BuildContext context) {
    super.build(context);
    final theme = Theme.of(context);

    return SingleChildScrollView(
      padding: const EdgeInsets.symmetric(horizontal: 4),
      child: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 640),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: <Widget>[
            SizedBox(
              width: double.infinity,
              child: DropdownMenu<String>(
                initialSelection: _type,
                label: const Text('包类型'),
                enabled: !_isPacking,
                dropdownMenuEntries: const <DropdownMenuEntry<String>>[
                  DropdownMenuEntry<String>(
                    value: 'software-deploy',
                    label: '软件部署包（含安装/卸载脚本）',
                  ),
                  DropdownMenuEntry<String>(
                    value: 'file-deploy',
                    label: '文件部署包（直接解压）',
                  ),
                ],
                onSelected: _isPacking
                    ? null
                    : (value) {
                        if (value != null) {
                          setState(() {
                            _type = value;
                          });
                        }
                      },
              ),
            ),
            const SizedBox(height: 12),
            _buildPathPicker(
              label: '安装脚本',
              controller: _installScriptController,
              onPick: _pickInstallScript,
            ),
            const SizedBox(height: 12),
            _buildPathPicker(
              label: '卸载脚本',
              controller: _uninstallScriptController,
              onPick: _pickUninstallScript,
            ),
            const SizedBox(height: 12),
            _buildPathPicker(
              label: '文件负载目录',
              controller: _payloadDirController,
              onPick: _pickPayloadDir,
              isDirectory: true,
            ),
            const SizedBox(height: 12),
            TextField(
              controller: _targetPathController,
              decoration: const InputDecoration(
                labelText: '目标部署路径',
                hintText: '机台端解压目标目录（file-deploy 必填）',
              ),
              enabled: !_isPacking,
            ),
            const SizedBox(height: 8),
            Row(
              children: [
                Switch(
                  value: _requiresAdmin,
                  onChanged: _isPacking
                      ? null
                      : (value) {
                          setState(() {
                            _requiresAdmin = value;
                          });
                        },
                ),
                const Text('需要管理员权限'),
              ],
            ),
            const SizedBox(height: 12),
            TextField(
              controller: _nameController,
              decoration: const InputDecoration(labelText: '包名称'),
              enabled: !_isPacking,
            ),
            const SizedBox(height: 12),
            TextField(
              controller: _versionController,
              decoration: const InputDecoration(labelText: '版本号'),
              enabled: !_isPacking,
            ),
            const SizedBox(height: 12),
            TextField(
              controller: _signerController,
              decoration: const InputDecoration(labelText: '签名者'),
              enabled: !_isPacking,
            ),
            const SizedBox(height: 12),
            _buildPathPicker(
              label: '私钥文件',
              controller: _privateKeyController,
              onPick: _pickPrivateKey,
            ),
            const SizedBox(height: 12),
            _buildPathPicker(
              label: '输出目录',
              controller: _outputDirController,
              onPick: _pickOutputDir,
              isDirectory: true,
            ),
            const SizedBox(height: 16),
            Text(
              'software-deploy 类型必须同时包含 install.ps1 和 uninstall.ps1；'
              'file-deploy 类型不含脚本，只解压文件。'
              '输出文件为 name-version.zip 和 .zip.sig，'
              '可手动上传到服务端。',
              style: theme.textTheme.bodySmall?.copyWith(
                color: AppPalette.muted,
              ),
            ),
            if (_isPacking) ...<Widget>[
              const SizedBox(height: 16),
              LinearProgressIndicator(value: _progress),
              const SizedBox(height: 8),
              Text(_step, style: theme.textTheme.bodySmall),
            ],
            if (_resultMessage != null) ...<Widget>[
              const SizedBox(height: 16),
              Container(
                padding: const EdgeInsets.all(12),
                decoration: BoxDecoration(
                  color: _resultIsError
                      ? AppPalette.danger.withValues(alpha: 0.1)
                      : AppPalette.mint.withValues(alpha: 0.1),
                  borderRadius: BorderRadius.circular(12),
                  border: Border.all(
                    color: _resultIsError
                        ? AppPalette.danger.withValues(alpha: 0.3)
                        : AppPalette.mint.withValues(alpha: 0.3),
                  ),
                ),
                child: Text(
                  _resultMessage!,
                  style: TextStyle(
                    color: _resultIsError
                        ? AppPalette.danger
                        : AppPalette.mintDeep,
                  ),
                ),
              ),
            ],
            const SizedBox(height: 20),
            Row(
              children: <Widget>[
                if (!_isPacking && _resultMessage == null)
                  FilledButton.icon(
                    onPressed: _pack,
                    icon: const Icon(Icons.build_circle_rounded),
                    label: const Text('打包并签名'),
                  ),
                if (!_isPacking && _resultMessage != null)
                  FilledButton.icon(
                    onPressed: () {
                      setState(() {
                        _resultMessage = null;
                      });
                    },
                    icon: const Icon(Icons.refresh_rounded),
                    label: const Text('重新打包'),
                  ),
              ],
            ),
            const SizedBox(height: 24),
          ],
        ),
      ),
    );
  }
}

/// 证书包生成页面，生成自签名 X.509 证书并导出 PFX/PEM/trust.json/client-config.ini。
class CertificateGeneratorPage extends StatefulWidget {
  const CertificateGeneratorPage({super.key, required this.controller});

  final AppController controller;

  @override
  State<CertificateGeneratorPage> createState() =>
      _CertificateGeneratorPageState();
}

class _CertificateGeneratorPageState extends State<CertificateGeneratorPage>
    with AutomaticKeepAliveClientMixin {
  final TextEditingController _bundleNameController = TextEditingController();
  final TextEditingController _subjectCNController = TextEditingController();
  final TextEditingController _pfxPasswordController = TextEditingController();
  final TextEditingController _validDaysController = TextEditingController(
    text: '365',
  );
  final TextEditingController _outputDirController = TextEditingController();

  bool _obscurePassword = true;
  bool _isGenerating = false;
  double _progress = 0.0;
  String _step = '';
  String? _resultMessage;
  bool _resultIsError = false;
  int? _bgOpIndex;

  @override
  bool get wantKeepAlive => true;

  @override
  void initState() {
    super.initState();
    _checkBackgroundResult();
  }

  void _checkBackgroundResult() {
    for (int i = widget.controller.backgroundOperations.length - 1;
        i >= 0;
        i--) {
      final op = widget.controller.backgroundOperations[i];
      if (op.type == BackgroundOperationType.certificateGeneration &&
          op.status != BackgroundOperationStatus.running) {
        setState(() {
          _isGenerating = false;
          _resultMessage = op.resultMessage;
          _resultIsError = op.isError;
          _bgOpIndex = i;
        });
        break;
      }
      if (op.type == BackgroundOperationType.certificateGeneration &&
          op.status == BackgroundOperationStatus.running) {
        setState(() {
          _isGenerating = true;
          _progress = op.progress;
          _step = op.step;
          _bgOpIndex = i;
        });
        widget.controller.addListener(_onControllerChanged);
        break;
      }
    }
  }

  void _onControllerChanged() {
    if (_bgOpIndex == null || !mounted) return;
    final idx = _bgOpIndex!;
    if (idx >= widget.controller.backgroundOperations.length) return;
    final op = widget.controller.backgroundOperations[idx];
    if (op.type != BackgroundOperationType.certificateGeneration) return;

    setState(() {
      _progress = op.progress;
      _step = op.step;
      if (op.status != BackgroundOperationStatus.running) {
        _isGenerating = false;
        _resultMessage = op.resultMessage;
        _resultIsError = op.isError;
        widget.controller.removeListener(_onControllerChanged);
      }
    });
  }

  @override
  void dispose() {
    widget.controller.removeListener(_onControllerChanged);
    _bundleNameController.dispose();
    _subjectCNController.dispose();
    _pfxPasswordController.dispose();
    _validDaysController.dispose();
    _outputDirController.dispose();
    super.dispose();
  }

  Future<void> _pickOutputDir() async {
    final result = await FilePicker.platform.getDirectoryPath();
    if (result != null && result.isNotEmpty) {
      setState(() {
        _outputDirController.text = result;
      });
    }
  }

  Future<void> _generate() async {
    final bundleName = _bundleNameController.text.trim();
    final subjectCN = _subjectCNController.text.trim();
    final pfxPassword = _pfxPasswordController.text;
    final validDaysText = _validDaysController.text.trim();
    final outputDir = _outputDirController.text.trim();

    // 客户端基本校验
    if (pfxPassword.isEmpty) {
      setState(() {
        _resultMessage = 'PFX 密码不能为空';
        _resultIsError = true;
      });
      return;
    }
    if (validDaysText.isEmpty) {
      setState(() {
        _resultMessage = '有效天数不能为空';
        _resultIsError = true;
      });
      return;
    }
    final validDays = int.tryParse(validDaysText);
    if (validDays == null) {
      setState(() {
        _resultMessage = '有效天数必须为整数';
        _resultIsError = true;
      });
      return;
    }
    if (outputDir.isEmpty) {
      setState(() {
        _resultMessage = '请选择输出目录';
        _resultIsError = true;
      });
      return;
    }

    setState(() {
      _isGenerating = true;
      _progress = 0.0;
      _step = '准备开始...';
      _resultMessage = null;
      _resultIsError = false;
    });

    // 清除之前的后台操作结果
    if (_bgOpIndex != null) {
      widget.controller.dismissBackgroundOperation(_bgOpIndex!);
      _bgOpIndex = null;
    }

    final opIndex = widget.controller.startBackgroundOperation(
      BackgroundOperationType.certificateGeneration,
      '准备开始...',
    );
    _bgOpIndex = opIndex;

    widget.controller.addListener(_onControllerChanged);

    try {
      final service = CertificateGeneratorService();
      final result = await service.generate(
        bundleName: bundleName.isEmpty ? null : bundleName,
        subjectCN: subjectCN.isEmpty ? null : subjectCN,
        pfxPassword: pfxPassword,
        validDays: validDays,
        outputDir: outputDir,
        onProgress: (progress, step) {
          widget.controller.updateBackgroundOperationProgress(
            opIndex,
            progress,
            step,
          );
          if (mounted) {
            setState(() {
              _progress = progress;
              _step = step;
            });
          }
        },
      );

      final resultMessage = '证书包生成完成：\n'
          '• PFX: ${result.pfxPath}\n'
          '• PEM: ${result.pemPath}\n'
          '• Trust JSON: ${result.trustJsonPath}\n'
          '• Client Config: ${result.clientConfigPath}';
      widget.controller.completeBackgroundOperation(opIndex, resultMessage);

      if (mounted) {
        widget.controller.removeListener(_onControllerChanged);
        setState(() {
          _isGenerating = false;
          _resultMessage = resultMessage;
          _resultIsError = false;
        });
      }
    } catch (error) {
      final errorMsg = '证书生成失败：$error';
      widget.controller.failBackgroundOperation(opIndex, errorMsg);

      if (mounted) {
        widget.controller.removeListener(_onControllerChanged);
        setState(() {
          _isGenerating = false;
          _resultMessage = errorMsg;
          _resultIsError = true;
        });
      }
    }
  }

  Widget _buildOutputDirPicker() {
    return LayoutBuilder(
      builder: (context, constraints) {
        final narrow = constraints.maxWidth < 430;
        final field = TextField(
          controller: _outputDirController,
          decoration: const InputDecoration(
            labelText: '输出目录',
            hintText: '选择目录',
          ),
          readOnly: true,
        );
        final button = OutlinedButton.icon(
          onPressed: _isGenerating ? null : _pickOutputDir,
          icon: const Icon(Icons.folder_open_rounded),
          label: const Text('浏览'),
        );

        if (narrow) {
          return Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: <Widget>[
              field,
              const SizedBox(height: 10),
              Align(alignment: Alignment.centerLeft, child: button),
            ],
          );
        }

        return Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: <Widget>[
            Expanded(child: field),
            const SizedBox(width: 10),
            button,
          ],
        );
      },
    );
  }

  @override
  Widget build(BuildContext context) {
    super.build(context);
    final theme = Theme.of(context);

    return SingleChildScrollView(
      padding: const EdgeInsets.symmetric(horizontal: 4),
      child: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 640),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: <Widget>[
            TextField(
              controller: _bundleNameController,
              decoration: const InputDecoration(
                labelText: '包名称（可选）',
                hintText: '默认: machine-registration',
              ),
              enabled: !_isGenerating,
            ),
            const SizedBox(height: 12),
            TextField(
              controller: _subjectCNController,
              decoration: const InputDecoration(
                labelText: '证书主题 CN（可选）',
                hintText: '默认: VHDMount Machine Registration',
              ),
              enabled: !_isGenerating,
            ),
            const SizedBox(height: 12),
            TextField(
              controller: _pfxPasswordController,
              decoration: InputDecoration(
                labelText: 'PFX 密码',
                hintText: '至少 8 个字符',
                suffixIcon: IconButton(
                  icon: Icon(
                    _obscurePassword
                        ? Icons.visibility_off_rounded
                        : Icons.visibility_rounded,
                  ),
                  onPressed: () {
                    setState(() {
                      _obscurePassword = !_obscurePassword;
                    });
                  },
                ),
              ),
              obscureText: _obscurePassword,
              enabled: !_isGenerating,
            ),
            const SizedBox(height: 12),
            TextField(
              controller: _validDaysController,
              decoration: const InputDecoration(
                labelText: '有效天数',
                hintText: '1-3650',
              ),
              keyboardType: TextInputType.number,
              inputFormatters: <TextInputFormatter>[
                FilteringTextInputFormatter.digitsOnly,
              ],
              enabled: !_isGenerating,
            ),
            const SizedBox(height: 12),
            _buildOutputDirPicker(),
            const SizedBox(height: 16),
            Text(
              '生成自签名 X.509 证书包，输出 PFX（受密码保护）、PEM 证书、'
              'trust.json（服务端信任配置）和 client-config.ini（客户端配置片段）。',
              style: theme.textTheme.bodySmall?.copyWith(
                color: AppPalette.muted,
              ),
            ),
            if (_isGenerating) ...<Widget>[
              const SizedBox(height: 16),
              LinearProgressIndicator(value: _progress),
              const SizedBox(height: 8),
              Text(_step, style: theme.textTheme.bodySmall),
            ],
            if (_resultMessage != null) ...<Widget>[
              const SizedBox(height: 16),
              Container(
                padding: const EdgeInsets.all(12),
                decoration: BoxDecoration(
                  color: _resultIsError
                      ? AppPalette.danger.withValues(alpha: 0.1)
                      : AppPalette.mint.withValues(alpha: 0.1),
                  borderRadius: BorderRadius.circular(12),
                  border: Border.all(
                    color: _resultIsError
                        ? AppPalette.danger.withValues(alpha: 0.3)
                        : AppPalette.mint.withValues(alpha: 0.3),
                  ),
                ),
                child: Text(
                  _resultMessage!,
                  style: TextStyle(
                    color: _resultIsError
                        ? AppPalette.danger
                        : AppPalette.mintDeep,
                  ),
                ),
              ),
            ],
            const SizedBox(height: 20),
            Row(
              children: <Widget>[
                if (!_isGenerating && _resultMessage == null)
                  FilledButton.icon(
                    onPressed: _generate,
                    icon: const Icon(Icons.workspace_premium_rounded),
                    label: const Text('生成'),
                  ),
                if (!_isGenerating && _resultMessage != null)
                  FilledButton.icon(
                    onPressed: () {
                      setState(() {
                        _resultMessage = null;
                      });
                    },
                    icon: const Icon(Icons.refresh_rounded),
                    label: const Text('重新生成'),
                  ),
              ],
            ),
            const SizedBox(height: 24),
          ],
        ),
      ),
    );
  }
}

/// 清单打包页面，扫描 payload 目录、生成 manifest.json 并用 RSA-PSS 签名。
class ManifestPackagerPage extends StatefulWidget {
  const ManifestPackagerPage({super.key, required this.controller});

  final AppController controller;

  @override
  State<ManifestPackagerPage> createState() => _ManifestPackagerPageState();
}

class _ManifestPackagerPageState extends State<ManifestPackagerPage>
    with AutomaticKeepAliveClientMixin {
  final TextEditingController _versionController = TextEditingController();
  final TextEditingController _minVersionController = TextEditingController();
  final TextEditingController _signerController = TextEditingController(
    text: 'admin',
  );
  final TextEditingController _privateKeyController = TextEditingController();
  final TextEditingController _payloadDirController = TextEditingController();
  final TextEditingController _outputDirController = TextEditingController();

  String _type = 'app-update';
  bool _isPacking = false;
  double _progress = 0.0;
  String _step = '';
  String? _resultMessage;
  bool _resultIsError = false;
  int? _bgOpIndex;

  @override
  bool get wantKeepAlive => true;

  @override
  void initState() {
    super.initState();
    _checkBackgroundResult();
  }

  void _checkBackgroundResult() {
    for (int i = widget.controller.backgroundOperations.length - 1;
        i >= 0;
        i--) {
      final op = widget.controller.backgroundOperations[i];
      if (op.type == BackgroundOperationType.manifestPackaging &&
          op.status != BackgroundOperationStatus.running) {
        setState(() {
          _isPacking = false;
          _resultMessage = op.resultMessage;
          _resultIsError = op.isError;
          _bgOpIndex = i;
        });
        break;
      }
      if (op.type == BackgroundOperationType.manifestPackaging &&
          op.status == BackgroundOperationStatus.running) {
        setState(() {
          _isPacking = true;
          _progress = op.progress;
          _step = op.step;
          _bgOpIndex = i;
        });
        widget.controller.addListener(_onControllerChanged);
        break;
      }
    }
  }

  void _onControllerChanged() {
    if (_bgOpIndex == null || !mounted) return;
    final idx = _bgOpIndex!;
    if (idx >= widget.controller.backgroundOperations.length) return;
    final op = widget.controller.backgroundOperations[idx];
    if (op.type != BackgroundOperationType.manifestPackaging) return;

    setState(() {
      _progress = op.progress;
      _step = op.step;
      if (op.status != BackgroundOperationStatus.running) {
        _isPacking = false;
        _resultMessage = op.resultMessage;
        _resultIsError = op.isError;
        widget.controller.removeListener(_onControllerChanged);
      }
    });
  }

  @override
  void dispose() {
    widget.controller.removeListener(_onControllerChanged);
    _versionController.dispose();
    _minVersionController.dispose();
    _signerController.dispose();
    _privateKeyController.dispose();
    _payloadDirController.dispose();
    _outputDirController.dispose();
    super.dispose();
  }

  Future<void> _pickPrivateKey() async {
    final result = await FilePicker.platform.pickFiles(
      type: FileType.custom,
      allowedExtensions: <String>['pem'],
      allowMultiple: false,
      withData: false,
    );
    if (result != null &&
        result.files.isNotEmpty &&
        result.files.first.path != null) {
      setState(() {
        _privateKeyController.text = result.files.first.path!;
      });
    }
  }

  Future<void> _pickPayloadDir() async {
    final result = await FilePicker.platform.getDirectoryPath();
    if (result != null && result.isNotEmpty) {
      setState(() {
        _payloadDirController.text = result;
      });
    }
  }

  Future<void> _pickOutputDir() async {
    final result = await FilePicker.platform.getDirectoryPath();
    if (result != null && result.isNotEmpty) {
      setState(() {
        _outputDirController.text = result;
      });
    }
  }

  Future<void> _packageAndSign() async {
    final version = _versionController.text.trim();
    final minVersion = _minVersionController.text.trim();
    final signer = _signerController.text.trim();
    final privateKeyPath = _privateKeyController.text.trim();
    final payloadDir = _payloadDirController.text.trim();
    final outputDir = _outputDirController.text.trim();

    if (version.isEmpty) {
      setState(() {
        _resultMessage = '版本号不能为空';
        _resultIsError = true;
      });
      return;
    }
    if (minVersion.isEmpty) {
      setState(() {
        _resultMessage = '最小版本号不能为空';
        _resultIsError = true;
      });
      return;
    }
    if (signer.isEmpty) {
      setState(() {
        _resultMessage = '签名者不能为空';
        _resultIsError = true;
      });
      return;
    }
    if (privateKeyPath.isEmpty) {
      setState(() {
        _resultMessage = '请选择私钥文件';
        _resultIsError = true;
      });
      return;
    }
    if (payloadDir.isEmpty) {
      setState(() {
        _resultMessage = '请选择 Payload 目录';
        _resultIsError = true;
      });
      return;
    }
    if (outputDir.isEmpty) {
      setState(() {
        _resultMessage = '请选择输出目录';
        _resultIsError = true;
      });
      return;
    }

    // 清除之前的后台操作结果
    if (_bgOpIndex != null) {
      widget.controller.dismissBackgroundOperation(_bgOpIndex!);
      _bgOpIndex = null;
    }

    final opIndex = widget.controller.startBackgroundOperation(
      BackgroundOperationType.manifestPackaging,
      '准备开始...',
    );
    _bgOpIndex = opIndex;

    setState(() {
      _isPacking = true;
      _progress = 0.0;
      _step = '准备开始...';
      _resultMessage = null;
      _resultIsError = false;
    });

    widget.controller.addListener(_onControllerChanged);

    try {
      final packager = ManifestPackagerService();
      final result = await packager.packageAndSign(
        type: _type,
        payloadDir: payloadDir,
        outputDir: outputDir,
        privateKeyPath: privateKeyPath,
        version: version,
        minVersion: minVersion,
        signer: signer,
        onProgress: (progress, step) {
          widget.controller.updateBackgroundOperationProgress(
            opIndex,
            progress,
            step,
          );
          if (mounted) {
            setState(() {
              _progress = progress;
              _step = step;
            });
          }
        },
      );

      final resultMessage = '打包完成：\n'
          '清单文件: ${result.manifestPath}\n'
          '签名文件: ${result.signaturePath}\n'
          '文件数量: ${result.fileCount}\n'
          '总大小: ${_formatBytes(result.totalBytes)}';
      widget.controller.completeBackgroundOperation(opIndex, resultMessage);

      if (mounted) {
        widget.controller.removeListener(_onControllerChanged);
        setState(() {
          _isPacking = false;
          _resultMessage = resultMessage;
          _resultIsError = false;
        });
      }
    } catch (error) {
      final errorMsg = '打包失败：$error';
      widget.controller.failBackgroundOperation(opIndex, errorMsg);

      if (mounted) {
        widget.controller.removeListener(_onControllerChanged);
        setState(() {
          _isPacking = false;
          _resultMessage = errorMsg;
          _resultIsError = true;
        });
      }
    }
  }

  String _formatBytes(int bytes) {
    if (bytes < 1024) return '$bytes B';
    if (bytes < 1024 * 1024) {
      return '${(bytes / 1024).toStringAsFixed(1)} KB';
    }
    if (bytes < 1024 * 1024 * 1024) {
      return '${(bytes / (1024 * 1024)).toStringAsFixed(1)} MB';
    }
    return '${(bytes / (1024 * 1024 * 1024)).toStringAsFixed(2)} GB';
  }

  Widget _buildPathPicker({
    required String label,
    required TextEditingController controller,
    required VoidCallback onPick,
    bool isDirectory = false,
  }) {
    return LayoutBuilder(
      builder: (context, constraints) {
        final narrow = constraints.maxWidth < 430;
        final field = TextField(
          controller: controller,
          decoration: InputDecoration(
            labelText: label,
            hintText: isDirectory ? '选择目录' : '选择文件',
          ),
          readOnly: true,
        );
        final button = OutlinedButton.icon(
          onPressed: _isPacking ? null : onPick,
          icon: Icon(
            isDirectory ? Icons.folder_open_rounded : Icons.file_open_rounded,
          ),
          label: Text(isDirectory ? '浏览' : '选择'),
        );

        if (narrow) {
          return Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: <Widget>[
              field,
              const SizedBox(height: 10),
              Align(alignment: Alignment.centerLeft, child: button),
            ],
          );
        }

        return Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: <Widget>[
            Expanded(child: field),
            const SizedBox(width: 10),
            button,
          ],
        );
      },
    );
  }

  @override
  Widget build(BuildContext context) {
    super.build(context);
    final theme = Theme.of(context);

    return SingleChildScrollView(
      padding: const EdgeInsets.symmetric(horizontal: 4),
      child: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 640),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: <Widget>[
            SizedBox(
              width: double.infinity,
              child: DropdownMenu<String>(
                initialSelection: _type,
                label: const Text('清单类型'),
                enabled: !_isPacking,
                dropdownMenuEntries: const <DropdownMenuEntry<String>>[
                  DropdownMenuEntry<String>(
                    value: 'app-update',
                    label: '应用更新 (app-update)',
                  ),
                  DropdownMenuEntry<String>(
                    value: 'vhd-data',
                    label: 'VHD 数据 (vhd-data)',
                  ),
                ],
                onSelected: _isPacking
                    ? null
                    : (value) {
                        if (value != null) {
                          setState(() {
                            _type = value;
                          });
                        }
                      },
              ),
            ),
            const SizedBox(height: 12),
            TextField(
              controller: _versionController,
              decoration: const InputDecoration(
                labelText: '版本号',
                hintText: '例如 2024.06.15.120000',
              ),
              enabled: !_isPacking,
            ),
            const SizedBox(height: 12),
            TextField(
              controller: _minVersionController,
              decoration: const InputDecoration(
                labelText: '最小版本号',
                hintText: '例如 1.7.0',
              ),
              enabled: !_isPacking,
            ),
            const SizedBox(height: 12),
            TextField(
              controller: _signerController,
              decoration: const InputDecoration(labelText: '签名者'),
              enabled: !_isPacking,
            ),
            const SizedBox(height: 12),
            _buildPathPicker(
              label: '私钥文件 (.pem)',
              controller: _privateKeyController,
              onPick: _pickPrivateKey,
            ),
            const SizedBox(height: 12),
            _buildPathPicker(
              label: 'Payload 目录',
              controller: _payloadDirController,
              onPick: _pickPayloadDir,
              isDirectory: true,
            ),
            const SizedBox(height: 12),
            _buildPathPicker(
              label: '输出目录',
              controller: _outputDirController,
              onPick: _pickOutputDir,
              isDirectory: true,
            ),
            const SizedBox(height: 16),
            Text(
              'app-update 类型最大 payload 为 1 GB；超过限制请使用 vhd-data 类型。'
              '输出文件为 manifest.json 和 manifest.sig，'
              '可放入 USB 设备供 Updater 使用。',
              style: theme.textTheme.bodySmall?.copyWith(
                color: AppPalette.muted,
              ),
            ),
            if (_isPacking) ...<Widget>[
              const SizedBox(height: 16),
              LinearProgressIndicator(value: _progress),
              const SizedBox(height: 8),
              Text(_step, style: theme.textTheme.bodySmall),
            ],
            if (_resultMessage != null) ...<Widget>[
              const SizedBox(height: 16),
              Container(
                padding: const EdgeInsets.all(12),
                decoration: BoxDecoration(
                  color: _resultIsError
                      ? AppPalette.danger.withValues(alpha: 0.1)
                      : AppPalette.mint.withValues(alpha: 0.1),
                  borderRadius: BorderRadius.circular(12),
                  border: Border.all(
                    color: _resultIsError
                        ? AppPalette.danger.withValues(alpha: 0.3)
                        : AppPalette.mint.withValues(alpha: 0.3),
                  ),
                ),
                child: Text(
                  _resultMessage!,
                  style: TextStyle(
                    color: _resultIsError
                        ? AppPalette.danger
                        : AppPalette.mintDeep,
                  ),
                ),
              ),
            ],
            const SizedBox(height: 20),
            Row(
              children: <Widget>[
                if (!_isPacking && _resultMessage == null)
                  FilledButton.icon(
                    onPressed: _packageAndSign,
                    icon: const Icon(Icons.inventory_2_rounded),
                    label: const Text('打包并签名'),
                  ),
                if (!_isPacking && _resultMessage != null)
                  FilledButton.icon(
                    onPressed: () {
                      setState(() {
                        _resultMessage = null;
                      });
                    },
                    icon: const Icon(Icons.refresh_rounded),
                    label: const Text('重新打包'),
                  ),
              ],
            ),
            const SizedBox(height: 24),
          ],
        ),
      ),
    );
  }
}
