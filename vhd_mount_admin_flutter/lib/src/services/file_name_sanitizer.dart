part of '../../app.dart';

/// 文件名清理工具，确保输出文件名在所有目标平台有效。
///
/// 将无效字符替换为下划线，空结果回退到 `_output`，
/// 基本名称（不含扩展名）截断为最大 200 字符。
class FileNameSanitizer {
  /// 无效字符集：< > : " / \ | ? * 以及 U+0000–U+001F 控制字符。
  static final RegExp invalidChars = RegExp(r'[<>:"/\\|?*\x00-\x1F]');

  /// 将文件名中每个无效字符替换为单个下划线。
  ///
  /// - 如果清理后结果为空，返回 `_output`。
  /// - 清理后的基本名称（不含文件扩展名）截断为最大 200 个字符。
  /// - 扩展名在截断后保留。
  String sanitize(String fileName) {
    // 替换所有无效字符为下划线
    var sanitized = fileName.replaceAll(invalidChars, '_');

    // 清理后为空则回退
    if (sanitized.isEmpty) {
      return '_output';
    }

    // 分离基本名称和扩展名
    final dotIndex = sanitized.lastIndexOf('.');
    String baseName;
    String extension;

    if (dotIndex > 0) {
      baseName = sanitized.substring(0, dotIndex);
      extension = sanitized.substring(dotIndex);
    } else {
      baseName = sanitized;
      extension = '';
    }

    // 截断基本名称到最大 200 字符
    if (baseName.length > 200) {
      baseName = baseName.substring(0, 200);
    }

    // 截断后基本名称为空则回退
    if (baseName.isEmpty) {
      return '_output$extension';
    }

    return '$baseName$extension';
  }
}
