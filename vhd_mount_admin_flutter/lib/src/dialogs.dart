part of '../app.dart';

Future<String?> showSingleInputDialog(
  BuildContext context, {
  required String title,
  required String label,
  String initialValue = '',
  bool obscureText = false,
}) async {
  return showDialog<String>(
    context: context,
    builder: (context) => _SingleInputDialog(
      title: title,
      label: label,
      initialValue: initialValue,
      obscureText: obscureText,
    ),
  );
}

Future<List<String>?> showTwoFieldDialog(
  BuildContext context, {
  required String title,
  required String firstLabel,
  required String secondLabel,
  int secondMinLines = 1,
}) async {
  return showDialog<List<String>>(
    context: context,
    builder: (context) => _TwoFieldDialog(
      title: title,
      firstLabel: firstLabel,
      secondLabel: secondLabel,
      secondMinLines: secondMinLines,
    ),
  );
}

Future<MachineDraft?> showAddMachineDialog(
  BuildContext context, {
  required String defaultVhdKeyword,
}) async {
  return showDialog<MachineDraft>(
    context: context,
    builder: (context) => _AddMachineDialog(
      defaultVhdKeyword: defaultVhdKeyword,
    ),
  );
}

class _SingleInputDialog extends StatefulWidget {
  const _SingleInputDialog({
    required this.title,
    required this.label,
    required this.initialValue,
    required this.obscureText,
  });

  final String title;
  final String label;
  final String initialValue;
  final bool obscureText;

  @override
  State<_SingleInputDialog> createState() => _SingleInputDialogState();
}

class _SingleInputDialogState extends State<_SingleInputDialog> {
  late final TextEditingController _controller;

  @override
  void initState() {
    super.initState();
    _controller = TextEditingController(text: widget.initialValue);
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  void _close([String? result]) {
    FocusManager.instance.primaryFocus?.unfocus();
    Navigator.of(context).pop(result);
  }

  @override
  Widget build(BuildContext context) {
    return AlertDialog(
      title: Text(widget.title),
      content: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 560),
        child: widget.obscureText
            ? buildSecureTextField(
                controller: _controller,
                autofillHints: const <String>[AutofillHints.password],
                decoration: InputDecoration(labelText: widget.label),
              )
            : TextField(
                controller: _controller,
                decoration: InputDecoration(labelText: widget.label),
              ),
      ),
      actions: <Widget>[
        TextButton(onPressed: _close, child: const Text('取消')),
        FilledButton(
          onPressed: () => _close(_controller.text),
          child: const Text('确定'),
        ),
      ],
    );
  }
}

class _TwoFieldDialog extends StatefulWidget {
  const _TwoFieldDialog({
    required this.title,
    required this.firstLabel,
    required this.secondLabel,
    required this.secondMinLines,
  });

  final String title;
  final String firstLabel;
  final String secondLabel;
  final int secondMinLines;

  @override
  State<_TwoFieldDialog> createState() => _TwoFieldDialogState();
}

class _TwoFieldDialogState extends State<_TwoFieldDialog> {
  late final TextEditingController _firstController;
  late final TextEditingController _secondController;

  @override
  void initState() {
    super.initState();
    _firstController = TextEditingController();
    _secondController = TextEditingController();
  }

  @override
  void dispose() {
    _firstController.dispose();
    _secondController.dispose();
    super.dispose();
  }

  void _close([List<String>? result]) {
    FocusManager.instance.primaryFocus?.unfocus();
    Navigator.of(context).pop(result);
  }

  @override
  Widget build(BuildContext context) {
    return AlertDialog(
      title: Text(widget.title),
      content: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 560),
        child: SingleChildScrollView(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: <Widget>[
              TextField(
                controller: _firstController,
                decoration: InputDecoration(labelText: widget.firstLabel),
              ),
              const SizedBox(height: 12),
              TextField(
                controller: _secondController,
                minLines: widget.secondMinLines,
                maxLines: widget.secondMinLines + 4,
                decoration: InputDecoration(
                  labelText: widget.secondLabel,
                  alignLabelWithHint: true,
                ),
              ),
            ],
          ),
        ),
      ),
      actions: <Widget>[
        TextButton(onPressed: _close, child: const Text('取消')),
        FilledButton(
          onPressed: () =>
              _close(<String>[_firstController.text, _secondController.text]),
          child: const Text('确定'),
        ),
      ],
    );
  }
}

class _AddMachineDialog extends StatefulWidget {
  const _AddMachineDialog({required this.defaultVhdKeyword});

  final String defaultVhdKeyword;

  @override
  State<_AddMachineDialog> createState() => _AddMachineDialogState();
}

class _AddMachineDialogState extends State<_AddMachineDialog> {
  late final TextEditingController _machineIdController;
  late final TextEditingController _vhdController;
  late final TextEditingController _evhdPasswordController;
  bool _protectedState = false;

  @override
  void initState() {
    super.initState();
    _machineIdController = TextEditingController();
    _vhdController = TextEditingController(text: widget.defaultVhdKeyword);
    _evhdPasswordController = TextEditingController();
  }

  @override
  void dispose() {
    _machineIdController.dispose();
    _vhdController.dispose();
    _evhdPasswordController.dispose();
    super.dispose();
  }

  void _close([MachineDraft? result]) {
    FocusManager.instance.primaryFocus?.unfocus();
    Navigator.of(context).pop(result);
  }

  @override
  Widget build(BuildContext context) {
    return AlertDialog(
      title: const Text('添加机台'),
      content: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 560),
        child: SingleChildScrollView(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: <Widget>[
              TextField(
                controller: _machineIdController,
                decoration: const InputDecoration(labelText: '机台 ID'),
              ),
              const SizedBox(height: 12),
              TextField(
                controller: _vhdController,
                decoration: const InputDecoration(labelText: '初始启动关键词'),
              ),
              const SizedBox(height: 12),
              buildSecureTextField(
                controller: _evhdPasswordController,
                decoration: const InputDecoration(labelText: '初始 EVHD 密码（可选）'),
              ),
              const SizedBox(height: 12),
              SwitchListTile.adaptive(
                contentPadding: EdgeInsets.zero,
                title: const Text('创建后立即开启保护'),
                value: _protectedState,
                onChanged: (value) {
                  setState(() {
                    _protectedState = value;
                  });
                },
              ),
            ],
          ),
        ),
      ),
      actions: <Widget>[
        TextButton(onPressed: _close, child: const Text('取消')),
        FilledButton(
          onPressed: () {
            _close(
              MachineDraft(
                machineId: _machineIdController.text.trim(),
                vhdKeyword: _vhdController.text.trim().toUpperCase(),
                protectedState: _protectedState,
                evhdPassword: _evhdPasswordController.text.isEmpty
                    ? null
                    : _evhdPasswordController.text,
              ),
            );
          },
          child: const Text('创建'),
        ),
      ],
    );
  }
}

Future<bool?> showConfirmDialog(
  BuildContext context, {
  required String title,
  required String message,
  String confirmLabel = '确定',
}) {
  return showDialog<bool>(
    context: context,
    builder: (context) => AlertDialog(
      title: Text(title),
      content: Text(message),
      actions: <Widget>[
        TextButton(
          onPressed: () => Navigator.of(context).pop(false),
          child: const Text('取消'),
        ),
        FilledButton(
          onPressed: () => Navigator.of(context).pop(true),
          child: Text(confirmLabel),
        ),
      ],
    ),
  );
}