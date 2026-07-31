part of '../../app.dart';

/// PEM 格式编解码异常。
class PemFormatException implements Exception {
  PemFormatException(this.message);

  final String message;

  @override
  String toString() => 'PemFormatException: $message';
}

/// 可复用的 PEM 编解码器，支持编码、解码、多块解码。
///
/// 支持的类型标签：'PRIVATE KEY'、'PUBLIC KEY'、'CERTIFICATE'。
class PemCodec {
  /// 将 DER 字节编码为 PEM 格式文本。
  ///
  /// [derBytes] 为待编码的 DER 字节序列。
  /// [type] 为类型标签，如 'PRIVATE KEY'、'PUBLIC KEY'、'CERTIFICATE'。
  /// 输出包含标准头尾行和 64 字符换行的 base64 正文。
  String encode(Uint8List derBytes, String type) {
    final base64Str = base64Encode(derBytes);
    final buffer = StringBuffer();
    buffer.writeln('-----BEGIN $type-----');
    for (var i = 0; i < base64Str.length; i += 64) {
      final end = (i + 64).clamp(0, base64Str.length);
      buffer.writeln(base64Str.substring(i, end));
    }
    buffer.write('-----END $type-----');
    return buffer.toString();
  }

  /// 将 PEM 文本解码为 DER 字节。
  ///
  /// 剥离头尾行并移除 base64 正文中的所有空白字符后解码。
  /// 如果 PEM 文本不包含预期的类型标签，抛出 [PemFormatException]。
  /// 如果 base64 正文无效，抛出 [PemFormatException]。
  Uint8List decode(String pemText, String expectedType) {
    final beginMarker = '-----BEGIN $expectedType-----';
    final endMarker = '-----END $expectedType-----';

    final startIndex = pemText.indexOf(beginMarker);
    if (startIndex < 0) {
      throw PemFormatException(
        '未找到预期的类型标签: $expectedType (缺少 BEGIN 标记)',
      );
    }

    final endIndex = pemText.indexOf(endMarker, startIndex);
    if (endIndex < 0) {
      throw PemFormatException(
        '未找到预期的类型标签: $expectedType (缺少 END 标记)',
      );
    }

    final base64Body = pemText
        .substring(startIndex + beginMarker.length, endIndex)
        .replaceAll(RegExp(r'[\s]'), '');

    if (base64Body.isEmpty) {
      throw PemFormatException('PEM 正文为空');
    }

    try {
      return base64Decode(base64Body);
    } on FormatException catch (e) {
      throw PemFormatException('无效的 base64 内容: ${e.message}');
    }
  }

  /// 解码包含多个 PEM 块的文本，按顺序返回所有 DER 字节列表。
  ///
  /// 如果文本中不包含任何预期类型的 PEM 块，抛出 [PemFormatException]。
  List<Uint8List> decodeAll(String pemText, String expectedType) {
    final beginMarker = '-----BEGIN $expectedType-----';
    final endMarker = '-----END $expectedType-----';

    final results = <Uint8List>[];
    var searchFrom = 0;

    while (true) {
      final startIndex = pemText.indexOf(beginMarker, searchFrom);
      if (startIndex < 0) break;

      final endIndex = pemText.indexOf(endMarker, startIndex);
      if (endIndex < 0) {
        throw PemFormatException(
          '未找到预期的类型标签: $expectedType (缺少 END 标记)',
        );
      }

      final base64Body = pemText
          .substring(startIndex + beginMarker.length, endIndex)
          .replaceAll(RegExp(r'[\s]'), '');

      if (base64Body.isEmpty) {
        throw PemFormatException('PEM 正文为空');
      }

      try {
        results.add(base64Decode(base64Body));
      } on FormatException catch (e) {
        throw PemFormatException('无效的 base64 内容: ${e.message}');
      }

      searchFrom = endIndex + endMarker.length;
    }

    if (results.isEmpty) {
      throw PemFormatException(
        '未找到预期的类型标签: $expectedType (未找到任何 PEM 块)',
      );
    }

    return results;
  }
}
