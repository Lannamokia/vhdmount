import 'dart:async';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';

import 'package:vhd_mount_admin_flutter/app.dart';

void main() {
  test('encodePathSegment escapes reserved path characters', () {
    expect(encodePathSegment('MACHINE/01'), 'MACHINE%2F01');
    expect(encodePathSegment('ABC 123'), 'ABC%20123');
  });

  test('HttpAdminApi times out slow requests with diagnostic message', () async {
    final server = await HttpServer.bind(InternetAddress.loopbackIPv4, 0);
    addTearDown(() => server.close(force: true));

    unawaited(
      server.forEach((request) async {
        await Future<void>.delayed(const Duration(milliseconds: 200));
        request.response
          ..statusCode = HttpStatus.ok
          ..write('{"initialized":true,"pendingInitialization":false,"databaseReady":true,"defaultVhdKeyword":"SAFEBOOT","trustedRegistrationCertificateCount":1}');
        await request.response.close();
      }),
    );

    final api = HttpAdminApi(
      baseUrl: 'http://${server.address.address}:${server.port}',
      requestTimeout: const Duration(milliseconds: 50),
    );

    await expectLater(
      api.getServerStatus(),
      throwsA(
        isA<AdminApiException>().having(
          (error) => error.message,
          'message',
          contains('请求超时'),
        ),
      ),
    );
  });
}