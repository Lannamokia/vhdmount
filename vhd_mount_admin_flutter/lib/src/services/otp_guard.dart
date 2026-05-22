part of '../../app.dart';

/// 需要 OTP 验证时抛出的异常。
/// 当 API 返回 requireOtp == true 时，可将 AdminApiException 转换为此异常，
/// 由 OtpGuard 捕获并自动触发验证流程。
class OtpRequiredException implements Exception {
  const OtpRequiredException([this.message = '此操作需要 OTP 验证']);

  final String message;

  @override
  String toString() => message;
}

/// 全局 OTP 验证主机接口。
/// [_OtpHostOverlay] 在 widget tree 中实现该接口，并通过
/// [AppController.attachOtpHost] 注册给 controller。
/// 当 controller 在 [_runAction] 中遇到 requireOtp 错误时，会调用
/// [requestVerification] 弹出对话框，验证成功后由 controller 透明重试原始操作。
abstract class OtpHostHandler {
  /// 弹出 OTP 验证对话框。
  /// 返回 true 表示验证成功，false 表示用户取消。
  Future<bool> requestVerification();
}

/// OTP 验证守卫，拦截需要 OTP 的操作并自动弹出验证对话框。
/// 验证成功后自动重试原始操作。
///
/// **使用建议：**
/// - 大部分需要 OTP 的操作已通过 [AppController._runAction] 自动接管，
///   普通调用直接 await controller 方法即可，错误会自动触发对话框。
/// - 进入证书页面等需要"主动验证"的场景仍可使用本守卫的 [showOtpDialog] 方法。
class OtpGuard {
  OtpGuard({required this.controller});

  final AppController controller;

  /// 包装需要 OTP 验证的操作（兼容老调用点）。
  Future<T?> guard<T>(BuildContext context, Future<T> Function() action) async {
    try {
      return await action();
    } on OtpRequiredException {
      // OTP 未验证，弹出对话框
    } on AdminApiException catch (e) {
      if (!e.requireOtp) rethrow;
      // requireOtp == true，弹出对话框
    }

    final verified = await showOtpDialog(context);
    if (!verified) {
      return null;
    }
    return await action();
  }

  /// 显示 OTP 验证对话框。
  Future<bool> showOtpDialog(BuildContext context) async {
    final result = await showDialog<bool>(
      context: context,
      barrierDismissible: false,
      builder: (context) => _OtpVerificationDialog(controller: controller),
    );
    return result == true;
  }
}

class _OtpVerificationDialog extends StatefulWidget {
  const _OtpVerificationDialog({required this.controller});

  final AppController controller;

  @override
  State<_OtpVerificationDialog> createState() =>
      _OtpVerificationDialogState();
}

class _OtpVerificationDialogState extends State<_OtpVerificationDialog> {
  final TextEditingController _otpController = TextEditingController();
  String? _errorText;
  bool _isVerifying = false;

  @override
  void dispose() {
    _otpController.dispose();
    super.dispose();
  }

  Future<void> _verify() async {
    final code = _otpController.text.trim();
    if (code.isEmpty) {
      setState(() {
        _errorText = '请输入验证码';
      });
      return;
    }

    setState(() {
      _isVerifying = true;
      _errorText = null;
    });

    try {
      await widget.controller.verifyOtp(code);
      if (widget.controller.otpVerified) {
        if (mounted) {
          Navigator.of(context).pop(true);
        }
      } else {
        setState(() {
          _errorText = '验证码错误，请重新输入';
          _isVerifying = false;
        });
      }
    } on AdminApiException catch (e) {
      setState(() {
        _errorText = e.message;
        _isVerifying = false;
      });
    } catch (e) {
      setState(() {
        _errorText = describeError(e);
        _isVerifying = false;
      });
    }
  }

  void _cancel() {
    Navigator.of(context).pop(false);
  }

  @override
  Widget build(BuildContext context) {
    return AlertDialog(
      title: const Text('OTP 验证'),
      content: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 380),
        child: SingleChildScrollView(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: <Widget>[
              const Text('此操作需要 OTP 二次验证，请输入验证码。'),
              const SizedBox(height: 16),
              TextField(
                controller: _otpController,
                decoration: InputDecoration(
                  labelText: '验证码',
                  errorText: _errorText,
                ),
                keyboardType: TextInputType.number,
                autofocus: true,
                enabled: !_isVerifying,
                onSubmitted: (_) => _verify(),
              ),
              if (_isVerifying) ...[
                const SizedBox(height: 12),
                const LinearProgressIndicator(),
              ],
            ],
          ),
        ),
      ),
      actions: <Widget>[
        TextButton(
          onPressed: _isVerifying ? null : _cancel,
          child: const Text('取消'),
        ),
        FilledButton(
          onPressed: _isVerifying ? null : _verify,
          child: const Text('验证'),
        ),
      ],
    );
  }
}


/// 全局 OTP 验证主机：在 widget tree 中包裹根页面，将自身注册到
/// [AppController]，由 controller 在 [_runAction] 检测到 requireOtp 错误时
/// 通过此 overlay 弹出验证对话框。
///
/// 这个组件解决了"在没有 BuildContext 的 controller 内部弹出对话框"
/// 的问题：UI 持有 context，controller 通过抽象接口反向调用。
class OtpHostOverlay extends StatefulWidget {
  const OtpHostOverlay({
    super.key,
    required this.controller,
    required this.child,
  });

  final AppController controller;
  final Widget child;

  @override
  State<OtpHostOverlay> createState() => _OtpHostOverlayState();
}

class _OtpHostOverlayState extends State<OtpHostOverlay>
    implements OtpHostHandler {
  /// 防止并发触发多个 OTP 对话框（多个 API 同时拿到 requireOtp 时）。
  Future<bool>? _pendingDialog;

  @override
  void initState() {
    super.initState();
    widget.controller.attachOtpHost(this);
  }

  @override
  void dispose() {
    widget.controller.detachOtpHost(this);
    super.dispose();
  }

  @override
  Future<bool> requestVerification() {
    // 已经有一个对话框在排队 / 显示，复用它的结果，避免叠出多个
    final pending = _pendingDialog;
    if (pending != null) return pending;

    final completer = _showDialogOnce();
    _pendingDialog = completer;
    completer.whenComplete(() {
      if (identical(_pendingDialog, completer)) {
        _pendingDialog = null;
      }
    });
    return completer;
  }

  Future<bool> _showDialogOnce() async {
    if (!mounted) return false;
    final navigator = Navigator.of(context, rootNavigator: true);
    final result = await navigator.push<bool>(
      DialogRoute<bool>(
        context: context,
        barrierDismissible: false,
        builder: (context) =>
            _OtpVerificationDialog(controller: widget.controller),
      ),
    );
    return result == true;
  }

  @override
  Widget build(BuildContext context) => widget.child;
}
