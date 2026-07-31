part of '../app.dart';

/// "RustDesk 上报记录"子页面：作为 [RustDeskRemoteControlView] 的第三个 Tab。
///
/// 数据流：
///   - 机台通过命名管道把 (RustDesk ID, 密码, 类型, secretVersion) 给到
///     `BridgeServerHost`；
///   - `ReportUploader.UploadAsync` 用 wrap key K 做 AES-256-GCM 加密后
///     `POST /api/machines/:machineId/rustdesk/report`；
///   - 服务端解密落盘到 `rustdesk_reports`（每机台一行 upsert）；
///   - 本页通过 `GET /api/security/rustdesk-reports` 拉摘要列表（**不**含明文）；
///   - 用户在卡片上点击「读取明文」时弹一个填写原因的对话框，再 `GET
///     /api/security/rustdesk-reports/:machineId/plaintext?reason=...`，弹出
///     可复制的 RustDesk ID + 明文密码。该路径 OTP step-up + 审计与 EVHD
///     明文读取（任务 14.5 / Requirement 15.9）完全同构。
class RustDeskReportsView extends StatefulWidget {
  const RustDeskReportsView({
    super.key,
    required this.controller,
    this.embedInParentScroll = false,
  });

  final AppController controller;
  final bool embedInParentScroll;

  @override
  State<RustDeskReportsView> createState() => _RustDeskReportsViewState();
}

class _RustDeskReportsViewState extends State<RustDeskReportsView> {
  bool _initialLoaded = false;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) async {
      if (!_initialLoaded) {
        _initialLoaded = true;
        try {
          await widget.controller.loadRustDeskReports();
        } catch (_) {
          // 失败状态由 AppController.errorMessage 在外层 banner 显示
        }
      }
    });
  }

  @override
  Widget build(BuildContext context) {
    final controller = widget.controller;
    final reports = List<RustDeskReportSummary>.from(controller.rustDeskReports)
      ..sort((a, b) {
        // reported_at 越新越靠前；同时间按 machineId 字典序兜底
        final cmp = b.reportedAt.compareTo(a.reportedAt);
        if (cmp != 0) return cmp;
        return a.machineId.compareTo(b.machineId);
      });

    final emptyState = Center(
      child: SizedBox(
        width: 480,
        child: const InfoPanel(
          title: '暂无机台 RustDesk 上报记录',
          body: Text(
            '机台启动 RustDesk 客户端 + 拿到设备 ID / 密码后会自动上报。如果机台已经在线但仍未出现，'
            '请确认它的 RustDeskClientSharedSecret 版本与服务端激活版本一致，且日志中没有 wrap_key 相关错误。',
          ),
          icon: Icons.cloud_upload_rounded,
          color: AppPalette.sky,
        ),
      ),
    );

    final list = ListView.separated(
      itemCount: reports.length,
      shrinkWrap: widget.embedInParentScroll,
      physics: widget.embedInParentScroll
          ? const NeverScrollableScrollPhysics()
          : null,
      padding: EdgeInsets.zero,
      separatorBuilder: (_, _) => const SizedBox(height: 12),
      itemBuilder: (context, index) {
        final r = reports[index];
        return _RustDeskReportCard(
          report: r,
          onReveal: () => _onReveal(context, controller, r),
        );
      },
    );

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: <Widget>[
        PageHeader(
          eyebrow: 'RustDesk 远程控制',
          title: '上报记录',
          subtitle: '机台主动上报的 RustDesk ID 与密码（每机台保留最近一条）。明文需 OTP 二次验证后才会下发，并写入审计。',
          actions: <Widget>[
            OutlinedButton.icon(
              onPressed: () async {
                try {
                  await controller.loadRustDeskReports();
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
        if (reports.isEmpty)
          widget.embedInParentScroll ? emptyState : Expanded(child: emptyState)
        else if (widget.embedInParentScroll)
          list
        else
          Expanded(child: list),
      ],
    );
  }

  Future<void> _onReveal(
    BuildContext context,
    AppController controller,
    RustDeskReportSummary report,
  ) async {
    final reason = await showSingleInputDialog(
      context,
      title: '读取 RustDesk 明文',
      label: '查询原因',
      initialValue: 'support investigation',
    );
    if (reason == null || reason.trim().isEmpty) return;

    try {
      final plaintext = await controller.readRustDeskReportPlaintext(
        report.machineId,
        reason.trim(),
      );
      if (!context.mounted) return;
      await showDialog<void>(
        context: context,
        builder: (context) => _RustDeskReportPlaintextDialog(plaintext: plaintext),
      );
    } catch (error) {
      if (!context.mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(describeError(error))),
      );
    }
  }
}

class _RustDeskReportCard extends StatelessWidget {
  const _RustDeskReportCard({
    required this.report,
    required this.onReveal,
  });

  final RustDeskReportSummary report;
  final VoidCallback onReveal;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final accent = report.hasPassword ? AppPalette.coral : AppPalette.muted;
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
                  icon: report.hasPassword
                      ? Icons.cast_for_education_rounded
                      : Icons.devices_other_rounded,
                  color: accent,
                ),
                const SizedBox(width: 14),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: <Widget>[
                      Text(
                        report.machineId,
                        style: theme.textTheme.titleLarge,
                      ),
                      const SizedBox(height: 4),
                      Text(
                        'RustDesk ID ${report.rustDeskId}',
                        style: theme.textTheme.bodyMedium?.copyWith(
                          color: AppPalette.muted,
                        ),
                      ),
                    ],
                  ),
                ),
                StatusChip(
                  label: report.passwordKindLabel,
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
                  label: '上报于 ${report.localizedReportedAt}',
                  color: AppPalette.sky,
                ),
                if (report.secretVersion != null)
                  StatusChip(
                    label: '密钥版本 v${report.secretVersion}',
                    color: AppPalette.mint,
                  ),
                if (report.passwordHashPrefix != null)
                  StatusChip(
                    label: '密码指纹 ${report.passwordHashPrefix!}',
                    color: AppPalette.muted,
                  ),
              ],
            ),
            if (report.lastWrapKeyId != null && report.lastWrapKeyId!.isNotEmpty)
              Padding(
                padding: const EdgeInsets.only(top: 10),
                child: Text(
                  '最近一次 wrap_key: ${report.lastWrapKeyId}',
                  style: theme.textTheme.bodySmall?.copyWith(
                    color: AppPalette.muted,
                  ),
                ),
              ),
            const SizedBox(height: 14),
            Wrap(
              spacing: 12,
              runSpacing: 8,
              children: <Widget>[
                FilledButton.icon(
                  onPressed: report.hasPassword ? onReveal : null,
                  icon: const Icon(Icons.lock_open_rounded),
                  label: Text(
                    report.hasPassword ? '读取明文' : '密码未上报',
                  ),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

class _RustDeskReportPlaintextDialog extends StatelessWidget {
  const _RustDeskReportPlaintextDialog({required this.plaintext});

  final RustDeskReportPlaintext plaintext;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return AlertDialog(
      title: const Text('RustDesk 明文'),
      content: SizedBox(
        width: 460,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: <Widget>[
            _LabeledCopyableField(
              label: '机台 ID',
              value: plaintext.machineId,
            ),
            const SizedBox(height: 12),
            _LabeledCopyableField(
              label: 'RustDesk ID',
              value: plaintext.rustDeskId,
            ),
            const SizedBox(height: 12),
            _LabeledCopyableField(
              label: '密码（${_passwordKindLabel(plaintext.passwordKind)}）',
              value: plaintext.passwordPlaintext.isEmpty
                  ? '（未上报）'
                  : plaintext.passwordPlaintext,
              monospace: true,
            ),
            const SizedBox(height: 16),
            Text(
              '上报时间: ${plaintext.reportedAt.isEmpty
                  ? '未知'
                  : formatAuditTimestamp(plaintext.reportedAt)}',
              style: theme.textTheme.bodySmall?.copyWith(
                color: AppPalette.muted,
              ),
            ),
            if (plaintext.secretVersion != null)
              Text(
                '密钥版本: v${plaintext.secretVersion}',
                style: theme.textTheme.bodySmall?.copyWith(
                  color: AppPalette.muted,
                ),
              ),
          ],
        ),
      ),
      actions: <Widget>[
        TextButton(
          onPressed: () => Navigator.of(context).pop(),
          child: const Text('关闭'),
        ),
      ],
    );
  }

  static String _passwordKindLabel(String kind) {
    switch (kind) {
      case 'temporary':
        return '临时';
      case 'permanent':
        return '永久';
      case 'preset':
        return '预设';
      case 'absent':
        return '未设置';
      default:
        return kind;
    }
  }
}

class _LabeledCopyableField extends StatelessWidget {
  const _LabeledCopyableField({
    required this.label,
    required this.value,
    this.monospace = false,
  });

  final String label;
  final String value;
  final bool monospace;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: <Widget>[
        Text(
          label,
          style: theme.textTheme.labelMedium?.copyWith(
            color: AppPalette.muted,
          ),
        ),
        const SizedBox(height: 4),
        Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: <Widget>[
            Expanded(
              child: SelectableText(
                value,
                style: monospace
                    ? theme.textTheme.bodyLarge?.copyWith(
                        fontFeatures: const <FontFeature>[
                          FontFeature.tabularFigures(),
                        ],
                        fontFamily: 'monospace',
                      )
                    : theme.textTheme.bodyLarge,
              ),
            ),
            IconButton(
              tooltip: '复制',
              icon: const Icon(Icons.copy_rounded),
              onPressed: () async {
                await Clipboard.setData(ClipboardData(text: value));
                if (!context.mounted) return;
                ScaffoldMessenger.of(context).showSnackBar(
                  SnackBar(content: Text('已复制 $label')),
                );
              },
            ),
          ],
        ),
      ],
    );
  }
}
