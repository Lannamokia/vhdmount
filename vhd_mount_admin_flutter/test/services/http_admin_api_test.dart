import 'dart:async';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';

import 'package:vhd_mount_admin_flutter/app.dart';

class _MemoryCookieStore implements CookieStore {
  Map<String, Map<String, Cookie>> data = <String, Map<String, Cookie>>{};

  @override
  Future<void> clear() async {
    data = <String, Map<String, Cookie>>{};
  }

  @override
  Future<Map<String, Map<String, Cookie>>> load() async => data;

  @override
  Future<void> save(Map<String, Map<String, Cookie>> cookiesByOrigin) async {
    data = <String, Map<String, Cookie>>{};
    for (final entry in cookiesByOrigin.entries) {
      data[entry.key] = <String, Cookie>{...entry.value};
    }
  }
}

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

  test('HttpAdminApi scopes cookies by server origin', () async {
    final cookieStore = _MemoryCookieStore();
    final firstServer = await HttpServer.bind(InternetAddress.loopbackIPv4, 0);
    final secondServer = await HttpServer.bind(InternetAddress.loopbackIPv4, 0);
    addTearDown(() => firstServer.close(force: true));
    addTearDown(() => secondServer.close(force: true));

    String? firstServerCookieHeader;
    String? secondServerCookieHeader;

    unawaited(
      firstServer.forEach((request) async {
        firstServerCookieHeader = request.headers.value(HttpHeaders.cookieHeader);
        request.response.headers.add(
          HttpHeaders.setCookieHeader,
          'sid=first-session; Path=/',
        );
        request.response
          ..statusCode = HttpStatus.ok
          ..write('{"initialized":true,"pendingInitialization":false,"databaseReady":true,"defaultVhdKeyword":"SAFEBOOT","trustedRegistrationCertificateCount":1}');
        await request.response.close();
      }),
    );

    unawaited(
      secondServer.forEach((request) async {
        secondServerCookieHeader = request.headers.value(HttpHeaders.cookieHeader);
        request.response
          ..statusCode = HttpStatus.ok
          ..write('{"initialized":true,"pendingInitialization":false,"databaseReady":true,"defaultVhdKeyword":"SAFEBOOT","trustedRegistrationCertificateCount":1}');
        await request.response.close();
      }),
    );

    final api = HttpAdminApi(
      baseUrl: 'http://${firstServer.address.address}:${firstServer.port}',
      cookieStore: cookieStore,
    );

    await api.getServerStatus();
    expect(firstServerCookieHeader, isNull);

    api.updateBaseUrl('http://${secondServer.address.address}:${secondServer.port}');
    await api.getServerStatus();

    expect(secondServerCookieHeader, anyOf(isNull, isEmpty));
    expect(cookieStore.data.keys, contains('http://${firstServer.address.address}:${firstServer.port}'));
  });
}
