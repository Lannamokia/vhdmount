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

/// OTP 验证守卫，拦截需要 OTP 的操作并自动弹出验证对话框。
/// 验证成功后自动重试原始操作。
class OtpGuard {
  OtpGuard({required this.controller});

  final AppController controller;

  /// 包装需要 OTP 验证的操作。
  ///
  /// 执行流程：
  /// 1. 直接尝试执行 [action]
  /// 2. 如果抛出 [OtpRequiredException] 或 [AdminApiException] 且 requireOtp == true，
  ///    自动弹出 OTP 验证对话框
  /// 3. 验证成功后透明地重试 [action]
  /// 4. 用户取消验证时返回 null，不显示错误消息
  Future<T?> guard<T>(BuildContext context, Future<T> Function() action) async {
    try {
      return await action();
    } on OtpRequiredException {
      // OTP 未验证，弹出对话框
    } on AdminApiException catch (e) {
      if (!e.requireOtp) rethrow;
      // requireOtp == true，弹出对话框
    }

    // 需要 OTP 验证，显示对话框
    final verified = await showOtpDialog(context);
    if (!verified) {
      // 用户取消，静默返回 null
      return null;
    }

    // 验证成功，重试原始操作
    return await action();
  }

  /// 显示 OTP 验证对话框。
  /// 返回 true 表示验证成功，false 表示用户取消。
  /// 验证失败时在对话框内显示错误，不关闭对话框。
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

  BiometricOtpService? _biometricService;
  bool _biometricAvailable = false;

  @override
  void initState() {
    super.initState();
    _initBiometric();
  }

  Future<void> _initBiometric() async {
    final service = createBiometricOtpService();
    if (service == null) return;

    final available = await service.isAvailable();
    if (!available) return;

    final bound = await service.isBound();
    if (!bound) return;

    if (mounted) {
      setState(() {
        _biometricService = service;
        _biometricAvailable = true;
      });
    }
  }

  @override
  void dispose() {
    _otpController.dispose();
    super.dispose();
  }

  Future<void> _authenticateWithBiometric() async {
    setState(() {
      _isVerifying = true;
      _errorText = null;
    });

    try {
      final code = await _biometricService!.authenticate();
      if (code == null) {
        // 用户取消或认证失败，保持对话框打开
        if (mounted) {
          setState(() {
            _isVerifying = false;
          });
        }
        return;
      }

      // 自动提交验证码到服务端
      await widget.controller.verifyOtp(code);
      if (widget.controller.otpVerified) {
        if (mounted) {
          Navigator.of(context).pop(true);
        }
      } else {
        // 服务端拒绝（密钥已注销），清除本地绑定
        await _biometricService!.unbind();
        if (mounted) {
          setState(() {
            _biometricAvailable = false;
            _errorText = '生物识别绑定已失效，请重新绑定';
            _isVerifying = false;
          });
        }
      }
    } on AdminApiException catch (_) {
      // 服务端验证失败（密钥已被注销）
      await _biometricService!.unbind();
      if (mounted) {
        setState(() {
          _biometricAvailable = false;
          _errorText = '生物识别绑定已失效，请重新绑定';
          _isVerifying = false;
        });
      }
    } catch (e) {
      if (mounted) {
        setState(() {
          _errorText = describeError(e);
          _isVerifying = false;
        });
      }
    }
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
        constraints: const BoxConstraints(maxWidth: 360),
        child: SingleChildScrollView(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: <Widget>[
              if (_biometricAvailable) ...[
                FilledButton.icon(
                  onPressed: _isVerifying ? null : _authenticateWithBiometric,
                  icon: Icon(_biometricService!.icon),
                  label: Text('${_biometricService!.displayName} 验证'),
                ),
                const SizedBox(height: 16),
                const Divider(),
                const SizedBox(height: 8),
                Text(
                  '或手动输入验证码',
                  style: Theme.of(context).textTheme.bodySmall,
                ),
                const SizedBox(height: 12),
              ] else ...[
                const Text('此操作需要 OTP 二次验证，请输入验证码。'),
                const SizedBox(height: 16),
              ],
              TextField(
                controller: _otpController,
                decoration: InputDecoration(
                  labelText: '验证码',
                  errorText: _errorText,
                ),
                keyboardType: TextInputType.number,
                autofocus: !_biometricAvailable,
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
