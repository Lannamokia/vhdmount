part of '../app.dart';

enum _LogViewMode { sessions, entries }

class MachineLogsView extends StatefulWidget {
  const MachineLogsView({
    super.key,
    required this.controller,
    this.embedInParentScroll = false,
  });

  final AppController controller;
  final bool embedInParentScroll;

  @override
  State<MachineLogsView> createState() => _MachineLogsViewState();
}

class _MachineLogsViewState extends State<MachineLogsView> {
  late final TextEditingController _componentController;
  late final TextEditingController _eventKeyController;
  late final TextEditingController _queryController;
  late final TextEditingController _fromController;
  late final TextEditingController _toController;

  String? _selectedMachineId;
  String? _selectedSessionId;
  String? _selectedLevel;
  _LogViewMode _viewMode = _LogViewMode.sessions;

  @override
  void initState() {
    super.initState();
    _componentController = TextEditingController();
    _eventKeyController = TextEditingController();
    _queryController = TextEditingController();
    _fromController = TextEditingController();
    _toController = TextEditingController();
    _syncFromController(forceText: true);
  }

  @override
  void didUpdateWidget(covariant MachineLogsView oldWidget) {
    super.didUpdateWidget(oldWidget);
    _syncFromController();
  }

  @override
  void dispose() {
    _componentController.dispose();
    _eventKeyController.dispose();
    _queryController.dispose();
    _fromController.dispose();
    _toController.dispose();
    super.dispose();
  }

  void _syncFromController({bool forceText = false}) {
    final controller = widget.controller;
    _selectedMachineId = controller.machineLogSelectedMachineId;
    _selectedSessionId = controller.machineLogSelectedSessionId;
    _selectedLevel = controller.machineLogLevel;

    if (forceText ||
        _componentController.text != (controller.machineLogComponent ?? '')) {
      _componentController.text = controller.machineLogComponent ?? '';
    }
    if (forceText ||
        _eventKeyController.text != (controller.machineLogEventKey ?? '')) {
      _eventKeyController.text = controller.machineLogEventKey ?? '';
    }
    if (forceText ||
        _queryController.text != (controller.machineLogQuery ?? '')) {
      _queryController.text = controller.machineLogQuery ?? '';
    }
    if (forceText ||
        _fromController.text != (controller.machineLogFrom ?? '')) {
      _fromController.text = controller.machineLogFrom ?? '';
    }
    if (forceText || _toController.text != (controller.machineLogTo ?? '')) {
      _toController.text = controller.machineLogTo ?? '';
    }
  }

  String? _normalizedText(TextEditingController controller) {
    final value = controller.text.trim();
    return value.isEmpty ? null : value;
  }

  Future<void> _handleMachineChanged(String? machineId) async {
    setState(() {
      _selectedMachineId = machineId;
      _selectedSessionId = null;
      _viewMode = _LogViewMode.sessions;
    });

    try {
      await widget.controller.loadMachineLogSessions(
        machineId: _selectedMachineId,
        from: _normalizedText(_fromController),
        to: _normalizedText(_toController),
        loadEntries: false,
      );
      setState(() {
        _selectedSessionId = widget.controller.machineLogSelectedSessionId;
      });
      await _applyFilters(reloadSessions: false);
    } catch (error) {
      if (!mounted) {
        return;
      }
      ScaffoldMessenger.of(
        context,
      ).showSnackBar(SnackBar(content: Text(describeError(error))));
    }
  }

  void _handleSessionDropdownChanged(String? sessionId) {
    setState(() {
      _selectedSessionId = sessionId;
    });
    widget.controller.machineLogSelectedSessionId = sessionId;
  }

  Future<void> _enterSession(String sessionId) async {
    setState(() {
      _selectedSessionId = sessionId;
      _viewMode = _LogViewMode.entries;
    });

    try {
      await widget.controller.loadMachineLogs(
        machineId: _selectedMachineId,
        sessionId: sessionId,
        level: _selectedLevel,
        component: _normalizedText(_componentController),
        eventKey: _normalizedText(_eventKeyController)?.toUpperCase(),
        query: _normalizedText(_queryController),
        from: _normalizedText(_fromController),
        to: _normalizedText(_toController),
      );
      if (!mounted) {
        return;
      }
      setState(() {
        _syncFromController();
      });
    } catch (error) {
      if (!mounted) {
        return;
      }
      ScaffoldMessenger.of(
        context,
      ).showSnackBar(SnackBar(content: Text(describeError(error))));
    }
  }

  Future<void> _handleLevelChanged(String? level) async {
    setState(() {
      _selectedLevel = level;
    });
    await _applyFilters(reloadSessions: false);
  }

  Future<void> _applyFilters({bool reloadSessions = true}) async {
    try {
      if (reloadSessions) {
        await widget.controller.loadMachineLogSessions(
          machineId: _selectedMachineId,
          from: _normalizedText(_fromController),
          to: _normalizedText(_toController),
          loadEntries: false,
        );
        if (_selectedSessionId != null &&
            !widget.controller.machineLogSessions.any(
              (session) => session.sessionId == _selectedSessionId,
            )) {
          _selectedSessionId = widget.controller.machineLogSelectedSessionId;
        }
      }

      await widget.controller.loadMachineLogs(
        machineId: _selectedMachineId,
        sessionId: _selectedSessionId,
        level: _selectedLevel,
        component: _normalizedText(_componentController),
        eventKey: _normalizedText(_eventKeyController)?.toUpperCase(),
        query: _normalizedText(_queryController),
        from: _normalizedText(_fromController),
        to: _normalizedText(_toController),
      );
      if (!mounted) {
        return;
      }
      setState(() {
        _syncFromController();
      });
    } catch (error) {
      if (!mounted) {
        return;
      }
      ScaffoldMessenger.of(
        context,
      ).showSnackBar(SnackBar(content: Text(describeError(error))));
    }
  }

  Future<void> _clearFilters() async {
    setState(() {
      _selectedMachineId = null;
      _selectedSessionId = null;
      _selectedLevel = null;
      _viewMode = _LogViewMode.sessions;
      _componentController.clear();
      _eventKeyController.clear();
      _queryController.clear();
      _fromController.clear();
      _toController.clear();
    });

    widget.controller.clearMachineLogFilters(notify: false);
    await _applyFilters();
  }

  void _backToSessions() {
    setState(() {
      _viewMode = _LogViewMode.sessions;
      _selectedSessionId = widget.controller.machineLogSelectedSessionId;
    });
  }

  Future<void> _exportLogs() async {
    try {
      final exported = await widget.controller.exportMachineLogs();
      if (!mounted) {
        return;
      }
      await showDialog<void>(
        context: context,
        builder: (context) {
          final dialogWidth = min(
            720.0,
            MediaQuery.of(context).size.width - 48,
          );
          return AlertDialog(
            title: const Text('导出日志结果'),
            content: SizedBox(
              width: dialogWidth,
              child: SingleChildScrollView(
                child: SelectableText(
                  exported.isEmpty ? '当前筛选条件下没有日志。' : exported,
                ),
              ),
            ),
            actions: <Widget>[
              TextButton(
                onPressed: () => Navigator.of(context).pop(),
                child: const Text('关闭'),
              ),
            ],
          );
        },
      );
    } catch (error) {
      if (!mounted) {
        return;
      }
      ScaffoldMessenger.of(
        context,
      ).showSnackBar(SnackBar(content: Text(describeError(error))));
    }
  }

  Future<void> _showEntryDetail(MachineLogEntry entry) async {
    final detailText =
        '${entry.rawText}\n\nMetadata:\n${const JsonEncoder.withIndent('  ').convert(entry.metadata)}';
    await showDialog<void>(
      context: context,
      builder: (context) {
        final dialogWidth = min(
          720.0,
          MediaQuery.of(context).size.width - 48,
        );
        return AlertDialog(
          title: const Text('原始日志详情'),
          content: SizedBox(
            width: dialogWidth,
            child: SingleChildScrollView(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                mainAxisSize: MainAxisSize.min,
                children: <Widget>[
                  Wrap(
                    spacing: 8,
                    runSpacing: 8,
                    children: <Widget>[
                      StatusChip(
                        label: entry.level.toUpperCase(),
                        color: entry.level == 'error'
                            ? AppPalette.coral
                            : entry.level == 'warn'
                            ? AppPalette.sun
                            : AppPalette.sky,
                      ),
                      StatusChip(
                        label: 'Seq ${entry.seq}',
                        color: AppPalette.mint,
                      ),
                      StatusChip(
                        label: entry.component,
                        color: AppPalette.sky,
                      ),
                    ],
                  ),
                  const SizedBox(height: 12),
                  SelectableText(detailText),
                ],
              ),
            ),
          ),
          actions: <Widget>[
            TextButton(
              onPressed: () => Navigator.of(context).pop(),
              child: const Text('关闭'),
            ),
          ],
        );
      },
    );
  }

  Widget _buildSessionCard(MachineLogSession session) {
    final selected = session.sessionId == _selectedSessionId;
    return InkWell(
      borderRadius: BorderRadius.circular(20),
      onTap: () => _enterSession(session.sessionId),
      child: Card(
        elevation: selected ? 2 : 0,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(20),
          side: BorderSide(
            color: selected ? AppPalette.coral : AppPalette.border,
          ),
        ),
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: <Widget>[
              LayoutBuilder(
                builder: (context, constraints) {
                  final veryNarrow = constraints.maxWidth < 120;
                  return Row(
                    children: <Widget>[
                      if (!veryNarrow)
                        AccentIconBadge(
                          icon: Icons.folder_open_rounded,
                          color: AppPalette.sky,
                          size: veryNarrow ? 32 : 42,
                        ),
                      if (!veryNarrow) const SizedBox(width: 12),
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: <Widget>[
                            Text(
                              session.sessionId,
                              style: Theme.of(context).textTheme.titleSmall,
                            ),
                            const SizedBox(height: 4),
                            Text(
                              '启动：${session.localizedStartedAt}',
                              style: Theme.of(
                                context,
                              ).textTheme.bodySmall?.copyWith(
                                color: AppPalette.muted,
                              ),
                            ),
                          ],
                        ),
                      ),
                      const Icon(
                        Icons.chevron_right_rounded,
                        color: AppPalette.muted,
                      ),
                    ],
                  );
                },
              ),
              const SizedBox(height: 10),
              Text(
                '最近日志：${session.localizedLastEventAt}',
                style: Theme.of(context).textTheme.bodySmall?.copyWith(
                  color: AppPalette.muted,
                ),
              ),
              const SizedBox(height: 10),
              Wrap(
                spacing: 8,
                runSpacing: 8,
                children: <Widget>[
                  StatusChip(
                    label: '总计 ${session.totalCount}',
                    color: AppPalette.sky,
                  ),
                  StatusChip(
                    label: 'Warn ${session.warnCount}',
                    color: AppPalette.sun,
                  ),
                  StatusChip(
                    label: 'Error ${session.errorCount}',
                    color: AppPalette.coral,
                  ),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }

  Widget _buildEntryCard(MachineLogEntry entry) {
    return InkWell(
      borderRadius: BorderRadius.circular(20),
      onTap: () => _showEntryDetail(entry),
      child: Card(
        elevation: 0,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(20),
          side: const BorderSide(color: AppPalette.border),
        ),
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: <Widget>[
              Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: <Widget>[
                  AccentIconBadge(
                    icon: entry.level == 'error'
                        ? Icons.error_outline_rounded
                        : entry.level == 'warn'
                        ? Icons.warning_amber_rounded
                        : Icons.notes_rounded,
                    color: entry.level == 'error'
                        ? AppPalette.coral
                        : entry.level == 'warn'
                        ? AppPalette.sun
                        : AppPalette.sky,
                    size: 42,
                  ),
                  const SizedBox(width: 12),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: <Widget>[
                        Text(
                          entry.message.isEmpty
                              ? entry.rawText
                              : entry.message,
                          maxLines: 2,
                          overflow: TextOverflow.ellipsis,
                          style: Theme.of(context).textTheme.titleSmall,
                        ),
                        const SizedBox(height: 6),
                        Text(
                          '${entry.localizedOccurredAt} · ${entry.component} · ${entry.eventKey}',
                          style: Theme.of(
                            context,
                          ).textTheme.bodySmall?.copyWith(
                            color: AppPalette.muted,
                          ),
                        ),
                      ],
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 10),
              Wrap(
                spacing: 8,
                runSpacing: 8,
                children: <Widget>[
                  StatusChip(
                    label: entry.level.toUpperCase(),
                    color: entry.level == 'error'
                        ? AppPalette.coral
                        : entry.level == 'warn'
                        ? AppPalette.sun
                        : AppPalette.sky,
                  ),
                  StatusChip(
                    label: 'Seq ${entry.seq}',
                    color: AppPalette.mint,
                  ),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }

  Widget _buildFiltersPanel() {
    final controller = widget.controller;
    final machineOptions = controller.machines
        .map((machine) => machine.machineId)
        .toSet()
        .toList()
      ..sort((left, right) => left.compareTo(right));
    final sessionOptions = controller.machineLogSessions
        .map((session) => session.sessionId)
        .toList();

    return AppPanel(
      padding: const EdgeInsets.all(18),
      child: LayoutBuilder(
        builder: (context, constraints) {
          final available = constraints.maxWidth.isFinite
              ? constraints.maxWidth
              : double.maxFinite;
          double w(double preferred) =>
              preferred < available ? preferred : available;
          return Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: <Widget>[
              Wrap(
                spacing: 12,
                runSpacing: 12,
                children: <Widget>[
                  SizedBox(
                    width: w(260),
                    child: DropdownMenu<String>(
                      width: w(260),
                      key: ValueKey<String>(
                        _selectedMachineId ?? '__all_logs_machine__',
                      ),
                      initialSelection: _selectedMachineId,
                      enableFilter: true,
                      enableSearch: true,
                      label: const Text('机台'),
                      hintText: '全部机台',
                      dropdownMenuEntries: machineOptions
                          .map(
                            (machineId) => DropdownMenuEntry<String>(
                              value: machineId,
                              label: machineId,
                            ),
                          )
                          .toList(),
                      onSelected: _handleMachineChanged,
                    ),
                  ),
                  SizedBox(
                    width: w(320),
                    child: DropdownMenu<String>(
                      width: w(320),
                      key: ValueKey<String>(
                        _selectedSessionId ?? '__all_logs_session__',
                      ),
                      initialSelection: _selectedSessionId,
                      enableFilter: true,
                      enableSearch: true,
                      label: const Text('会话'),
                      hintText: '全部会话',
                      dropdownMenuEntries: sessionOptions
                          .map(
                            (sessionId) => DropdownMenuEntry<String>(
                              value: sessionId,
                              label: sessionId,
                            ),
                          )
                          .toList(),
                      onSelected: _handleSessionDropdownChanged,
                    ),
                  ),
                  SizedBox(
                    width: w(180),
                    child: DropdownMenu<String>(
                      width: w(180),
                      key: ValueKey<String>(
                        _selectedLevel ?? '__all_logs_level__',
                      ),
                      initialSelection: _selectedLevel,
                      label: const Text('级别'),
                      hintText: '全部级别',
                      dropdownMenuEntries: const <DropdownMenuEntry<String>>[
                        DropdownMenuEntry(value: 'debug', label: 'debug'),
                        DropdownMenuEntry(value: 'info', label: 'info'),
                        DropdownMenuEntry(value: 'warn', label: 'warn'),
                        DropdownMenuEntry(value: 'error', label: 'error'),
                      ],
                      onSelected: _handleLevelChanged,
                    ),
                  ),
                  SizedBox(
                    width: w(220),
                    child: TextField(
                      controller: _componentController,
                      decoration: const InputDecoration(labelText: '组件'),
                    ),
                  ),
                  SizedBox(
                    width: w(220),
                    child: TextField(
                      controller: _eventKeyController,
                      decoration: const InputDecoration(labelText: '事件键'),
                    ),
                  ),
                  SizedBox(
                    width: w(260),
                    child: TextField(
                      controller: _queryController,
                      decoration: const InputDecoration(
                        labelText: '关键词',
                        hintText: '搜索 message、rawText、metadata',
                      ),
                    ),
                  ),
                  SizedBox(
                    width: w(260),
                    child: TextField(
                      controller: _fromController,
                      decoration: const InputDecoration(
                        labelText: '开始时间',
                        hintText: '2026-04-19T12:00:00Z',
                      ),
                    ),
                  ),
                  SizedBox(
                    width: w(260),
                    child: TextField(
                      controller: _toController,
                      decoration: const InputDecoration(
                        labelText: '结束时间',
                        hintText: '2026-04-19T13:00:00Z',
                      ),
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 14),
              Wrap(
                spacing: 12,
                runSpacing: 12,
                children: <Widget>[
                  if (available < 320)
                    Tooltip(
                      message: '应用筛选',
                      child: IconButton.filled(
                        onPressed: _applyFilters,
                        icon: const Icon(Icons.filter_alt_rounded),
                      ),
                    )
                  else
                    FilledButton.icon(
                      onPressed: _applyFilters,
                      icon: const Icon(Icons.filter_alt_rounded),
                      label: const Text('应用筛选'),
                    ),
                  if (available < 320)
                    Tooltip(
                      message: '清空筛选',
                      child: IconButton.outlined(
                        onPressed: _clearFilters,
                        icon: const Icon(Icons.filter_alt_off_rounded),
                      ),
                    )
                  else
                    OutlinedButton.icon(
                      onPressed: _clearFilters,
                      icon: const Icon(Icons.filter_alt_off_rounded),
                      label: const Text('清空筛选'),
                    ),
                  if (available < 320)
                    Tooltip(
                      message: '刷新日志',
                      child: IconButton.outlined(
                        onPressed: () => _applyFilters(),
                        icon: const Icon(Icons.refresh_rounded),
                      ),
                    )
                  else
                    OutlinedButton.icon(
                      onPressed: () => _applyFilters(),
                      icon: const Icon(Icons.refresh_rounded),
                      label: const Text('刷新日志'),
                    ),
                  if (available < 320)
                    Tooltip(
                      message: '导出原始文本',
                      child: IconButton.filledTonal(
                        onPressed: _exportLogs,
                        icon: const Icon(Icons.download_rounded),
                      ),
                    )
                  else
                    FilledButton.tonalIcon(
                      onPressed: _exportLogs,
                      icon: const Icon(Icons.download_rounded),
                      label: const Text('导出原始文本'),
                    ),
                ],
              ),
            ],
          );
        },
      ),
    );
  }

  Widget _buildSessionsList() {
    final controller = widget.controller;
    final sessionCards =
        controller.machineLogSessions.map(_buildSessionCard).toList();

    final content = controller.machineLogSessions.isEmpty
        ? const Center(child: Text('当前筛选条件下没有会话。'))
        : widget.embedInParentScroll
        ? Column(children: sessionCards)
        : ListView.separated(
            itemCount: sessionCards.length,
            itemBuilder: (context, index) => sessionCards[index],
            separatorBuilder: (_, _) => const SizedBox(height: 12),
          );

    return AppPanel(
      padding: const EdgeInsets.all(16),
      child: content,
    );
  }

  Widget _buildEntriesList() {
    final controller = widget.controller;
    final entryCards =
        controller.machineLogEntries.map(_buildEntryCard).toList();

    return AppPanel(
      padding: const EdgeInsets.all(16),
      child: controller.machineLogEntries.isEmpty
          ? const Center(child: Text('当前筛选条件下没有日志明细。'))
          : widget.embedInParentScroll
          ? Column(children: entryCards)
          : ListView.separated(
              itemCount: entryCards.length,
              itemBuilder: (context, index) => entryCards[index],
              separatorBuilder: (_, _) => const SizedBox(height: 12),
            ),
    );
  }

  Widget _buildEntriesHeader() {
    final controller = widget.controller;
    final currentSession = controller.machineLogSessions
        .where((s) => s.sessionId == _selectedSessionId)
        .firstOrNull;

    return AppPanel(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          Row(
            children: <Widget>[
              OutlinedButton.icon(
                onPressed: _backToSessions,
                icon: const Icon(Icons.arrow_back_rounded),
                label: const Text('返回会话列表'),
              ),
              const SizedBox(width: 16),
              if (currentSession != null)
                Expanded(
                  child: Wrap(
                    spacing: 8,
                    runSpacing: 8,
                    children: <Widget>[
                      StatusChip(
                        label: '总计 ${currentSession.totalCount}',
                        color: AppPalette.sky,
                      ),
                      StatusChip(
                        label: 'Warn ${currentSession.warnCount}',
                        color: AppPalette.sun,
                      ),
                      StatusChip(
                        label: 'Error ${currentSession.errorCount}',
                        color: AppPalette.coral,
                      ),
                    ],
                  ),
                ),
            ],
          ),
          if (currentSession != null) ...<Widget>[
            const SizedBox(height: 12),
            Text(
              '会话：${currentSession.sessionId}',
              style: Theme.of(context).textTheme.titleMedium,
            ),
            const SizedBox(height: 4),
            Text(
              '启动：${currentSession.localizedStartedAt} · 最近日志：${currentSession.localizedLastEventAt}',
              style: Theme.of(context).textTheme.bodySmall?.copyWith(
                color: AppPalette.muted,
              ),
            ),
          ],
        ],
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final controller = widget.controller;

    if (_viewMode == _LogViewMode.entries) {
      final entriesContent = <Widget>[
        PageHeader(
          eyebrow: 'Session Logs',
          title: '日志条目',
          subtitle: _selectedSessionId ?? '',
          actions: <Widget>[
            StatusChip(
              label: controller.machineLogHasMore ? '还有更多分页' : '已到末页',
              color: controller.machineLogHasMore
                  ? AppPalette.sun
                  : AppPalette.mint,
            ),
          ],
        ),
        const SizedBox(height: 16),
        _buildEntriesHeader(),
        const SizedBox(height: 16),
        if (widget.embedInParentScroll)
          _buildEntriesList()
        else
          Expanded(child: _buildEntriesList()),
        if (controller.machineLogHasMore) ...<Widget>[
          const SizedBox(height: 12),
          Align(
            alignment: Alignment.centerLeft,
            child: OutlinedButton.icon(
              onPressed: widget.controller.loadMoreMachineLogs,
              icon: const Icon(Icons.expand_more_rounded),
              label: const Text('加载更多日志'),
            ),
          ),
        ],
      ];

      return Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: entriesContent,
      );
    }

    final sessionsContent = <Widget>[
      PageHeader(
        eyebrow: 'Machine Telemetry',
        title: '机台日志',
        subtitle: '选择日志会话以查看详细日志条目。',
        actions: <Widget>[
          StatusChip(
            label: controller.machineLogHasMore ? '还有更多分页' : '已到末页',
            color: controller.machineLogHasMore
                ? AppPalette.sun
                : AppPalette.mint,
          ),
        ],
      ),
      const SizedBox(height: 16),
      _buildFiltersPanel(),
      const SizedBox(height: 16),
      if (widget.embedInParentScroll)
        _buildSessionsList()
      else
        Expanded(child: _buildSessionsList()),
    ];

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: sessionsContent,
    );
  }
}
