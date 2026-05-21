import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:vhd_mount_admin_flutter/app.dart';

import '../support/fake_admin_api.dart';
import '../support/fake_client_config_store.dart';

// Feature: admin-tools-flutter-migration, Property 14: OTP 守卫在未验证时触发对话框
// Feature: admin-tools-flutter-migration, Property 15: OTP 守卫验证成功后自动重试

const ServerStatus _readyServerStatus = ServerStatus(
  initialized: true,
  pendingInitialization: false,
  databaseReady: true,
  defaultVhdKeyword: 'SAFEBOOT',
  trustedRegistrationCertificateCount: 1,
);

const AuthStatus _otpNotVerified = AuthStatus(
  initialized: true,
  isAuthenticated: true,
  otpVerified: false,
);

const AuthStatus _otpVerified = AuthStatus(
  initialized: true,
  isAuthenticated: true,
  otpVerified: true,
);

/// Minimal app wrapper that provides a MaterialApp context for dialog testing.
class _TestApp extends StatelessWidget {
  const _TestApp({required this.controller, required this.child});

  final AppController controller;
  final Widget child;

  @override
  Widget build(BuildContext context) {
    return MaterialApp(home: child);
  }
}

void main() {
  group('OtpGuard', () {
    // **Property 14: OTP 守卫在未验证时触发对话框**
    // **Validates: Requirements 8.1, 8.2**
    testWidgets(
      'shows OTP dialog when action throws OtpRequiredException',
      (tester) async {
        final api = FakeAdminApi(
          serverStatus: _readyServerStatus,
          authStatus: _otpNotVerified,
        );
        final controller = AppController(
          api: api,
          clientConfigStore: FakeClientConfigStore(),
        );

        late OtpGuard guard;
        late BuildContext capturedContext;

        await tester.pumpWidget(
          _TestApp(
            controller: controller,
            child: Builder(
              builder: (context) {
                capturedContext = context;
                guard = OtpGuard(controller: controller);
                return const SizedBox.shrink();
              },
            ),
          ),
        );

        int callCount = 0;
        // Start the guard call — it will show a dialog because action throws
        final future = guard.guard<String>(capturedContext, () async {
          callCount++;
          if (callCount == 1) {
            throw const OtpRequiredException();
          }
          return 'success';
        });

        // Let the dialog appear
        await tester.pumpAndSettle();

        // Verify the OTP dialog is shown
        expect(find.text('OTP 验证'), findsOneWidget);
        expect(find.text('此操作需要 OTP 二次验证，请输入验证码。'), findsOneWidget);
        expect(find.widgetWithText(TextField, '验证码'), findsOneWidget);

        // Cancel the dialog to complete the future
        await tester.tap(find.text('取消'));
        await tester.pumpAndSettle();

        final result = await future;
        expect(result, isNull);
      },
    );

    // **Property 15: OTP 守卫验证成功后自动重试**
    // **Validates: Requirements 8.3, 8.8**
    testWidgets(
      'retries action after successful OTP verification',
      (tester) async {
        final api = FakeAdminApi(
          serverStatus: _readyServerStatus,
          authStatus: _otpNotVerified,
        );
        final controller = AppController(
          api: api,
          clientConfigStore: FakeClientConfigStore(),
        );

        late OtpGuard guard;
        late BuildContext capturedContext;

        await tester.pumpWidget(
          _TestApp(
            controller: controller,
            child: Builder(
              builder: (context) {
                capturedContext = context;
                guard = OtpGuard(controller: controller);
                return const SizedBox.shrink();
              },
            ),
          ),
        );

        int callCount = 0;
        final future = guard.guard<String>(capturedContext, () async {
          callCount++;
          if (callCount == 1) {
            throw const OtpRequiredException();
          }
          return 'retry-result';
        });

        await tester.pumpAndSettle();

        // Dialog should be visible
        expect(find.text('OTP 验证'), findsOneWidget);

        // Enter OTP code and verify
        await tester.enterText(
          find.widgetWithText(TextField, '验证码'),
          '123456',
        );
        await tester.tap(find.text('验证'));
        await tester.pumpAndSettle();

        // After successful verification, the action is retried
        final result = await future;
        expect(result, 'retry-result');
        expect(callCount, 2); // Called once (threw), then retried
        expect(controller.otpVerified, isTrue);
      },
    );

    // Cancel behavior: guard returns null without error
    testWidgets(
      'returns null when user cancels OTP dialog',
      (tester) async {
        final api = FakeAdminApi(
          serverStatus: _readyServerStatus,
          authStatus: _otpNotVerified,
        );
        final controller = AppController(
          api: api,
          clientConfigStore: FakeClientConfigStore(),
        );

        late OtpGuard guard;
        late BuildContext capturedContext;

        await tester.pumpWidget(
          _TestApp(
            controller: controller,
            child: Builder(
              builder: (context) {
                capturedContext = context;
                guard = OtpGuard(controller: controller);
                return const SizedBox.shrink();
              },
            ),
          ),
        );

        final future = guard.guard<String>(capturedContext, () async {
          throw const OtpRequiredException();
        });

        await tester.pumpAndSettle();

        // Dialog is shown
        expect(find.text('OTP 验证'), findsOneWidget);

        // User cancels
        await tester.tap(find.text('取消'));
        await tester.pumpAndSettle();

        final result = await future;
        expect(result, isNull);
        // OTP remains unverified
        expect(controller.otpVerified, isFalse);
      },
    );

    // Already verified: action executes directly without showing dialog
    testWidgets(
      'executes action directly when OTP is already verified (no dialog)',
      (tester) async {
        final api = FakeAdminApi(
          serverStatus: _readyServerStatus,
          authStatus: _otpVerified,
        );
        final controller = AppController(
          api: api,
          clientConfigStore: FakeClientConfigStore(),
        );
        // Bootstrap to apply the otpVerified state
        await controller.bootstrap();

        late OtpGuard guard;
        late BuildContext capturedContext;

        await tester.pumpWidget(
          _TestApp(
            controller: controller,
            child: Builder(
              builder: (context) {
                capturedContext = context;
                guard = OtpGuard(controller: controller);
                return const SizedBox.shrink();
              },
            ),
          ),
        );

        // Action does NOT throw — OTP is already verified, so it runs directly
        final result = await guard.guard<String>(capturedContext, () async {
          return 'direct-result';
        });

        await tester.pumpAndSettle();

        // No dialog should appear
        expect(find.text('OTP 验证'), findsNothing);
        expect(result, 'direct-result');
      },
    );

    // AdminApiException with requireOtp: guard triggers dialog
    testWidgets(
      'shows OTP dialog when action throws AdminApiException with requireOtp',
      (tester) async {
        final api = FakeAdminApi(
          serverStatus: _readyServerStatus,
          authStatus: _otpNotVerified,
        );
        final controller = AppController(
          api: api,
          clientConfigStore: FakeClientConfigStore(),
        );

        late OtpGuard guard;
        late BuildContext capturedContext;

        await tester.pumpWidget(
          _TestApp(
            controller: controller,
            child: Builder(
              builder: (context) {
                capturedContext = context;
                guard = OtpGuard(controller: controller);
                return const SizedBox.shrink();
              },
            ),
          ),
        );

        int callCount = 0;
        final future = guard.guard<String>(capturedContext, () async {
          callCount++;
          if (callCount == 1) {
            throw AdminApiException(
              '需要 OTP 验证',
              requireOtp: true,
              statusCode: 403,
            );
          }
          return 'after-otp';
        });

        await tester.pumpAndSettle();

        // Dialog should be shown (triggered by AdminApiException.requireOtp)
        expect(find.text('OTP 验证'), findsOneWidget);

        // Verify and retry
        await tester.enterText(
          find.widgetWithText(TextField, '验证码'),
          '654321',
        );
        await tester.tap(find.text('验证'));
        await tester.pumpAndSettle();

        final result = await future;
        expect(result, 'after-otp');
        expect(callCount, 2);
      },
    );

    // AdminApiException without requireOtp should rethrow
    testWidgets(
      'rethrows AdminApiException when requireOtp is false',
      (tester) async {
        final api = FakeAdminApi(
          serverStatus: _readyServerStatus,
          authStatus: _otpNotVerified,
        );
        final controller = AppController(
          api: api,
          clientConfigStore: FakeClientConfigStore(),
        );

        late OtpGuard guard;
        late BuildContext capturedContext;

        await tester.pumpWidget(
          _TestApp(
            controller: controller,
            child: Builder(
              builder: (context) {
                capturedContext = context;
                guard = OtpGuard(controller: controller);
                return const SizedBox.shrink();
              },
            ),
          ),
        );

        expect(
          () => guard.guard<String>(capturedContext, () async {
            throw AdminApiException('服务器错误', requireOtp: false, statusCode: 500);
          }),
          throwsA(
            isA<AdminApiException>().having(
              (e) => e.message,
              'message',
              '服务器错误',
            ),
          ),
        );
      },
    );
  });
}
