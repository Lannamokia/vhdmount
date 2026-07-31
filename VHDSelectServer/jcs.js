'use strict';

/**
 * RFC 8785 JSON Canonicalization Scheme (JCS) Node.js 实现。
 * 与 src/VHDMounter/RustDeskBridge/Json/JcsCanonicalizer.cs 共享同一份
 * test-fixtures/jcs-vectors.json 跨语言夹具，保证两端输出逐字节相等。
 *
 * 关键约束：
 *  - 对象键按 UTF-16 code unit 字典序排序（§3.2.3）
 *  - 字符串短转义：\b \t \n \f \r \" \\ 为字面量；其它 0x00–0x1F 用 \u00xx；
 *    非 ASCII 字符按原文 UTF-8 输出（§3.2.2.2）
 *  - 数字按 ECMAScript Number.prototype.toString() 输出（§3.2.2.3，
 *    Node.js 默认行为已经匹配，无需后处理）
 */

function canonicalize(value) {
    return Buffer.from(canonicalizeString(value), 'utf8');
}

function canonicalizeString(value) {
    if (value === null) {
        return 'null';
    }

    if (value === true) {
        return 'true';
    }

    if (value === false) {
        return 'false';
    }

    if (typeof value === 'number') {
        return formatNumber(value);
    }

    if (typeof value === 'string') {
        return formatString(value);
    }

    if (Array.isArray(value)) {
        return '[' + value.map(canonicalizeString).join(',') + ']';
    }

    if (typeof value === 'object') {
        const keys = Object.keys(value);
        // RFC 8785 §3.2.3：按 UTF-16 code unit 字典序排序
        keys.sort();
        const parts = keys.map((key) => `${formatString(key)}:${canonicalizeString(value[key])}`);
        return '{' + parts.join(',') + '}';
    }

    throw new TypeError(`JCS 不支持的值类型: ${typeof value}`);
}

function formatNumber(value) {
    if (!Number.isFinite(value)) {
        throw new TypeError('JCS 不允许 NaN / Infinity 数字');
    }

    if (Object.is(value, -0)) {
        return '0';
    }

    return String(value);
}

function formatString(value) {
    let result = '"';
    for (let i = 0; i < value.length; i += 1) {
        const ch = value.charCodeAt(i);
        switch (ch) {
            case 0x5c:
                result += '\\\\';
                break;
            case 0x22:
                result += '\\"';
                break;
            case 0x08:
                result += '\\b';
                break;
            case 0x09:
                result += '\\t';
                break;
            case 0x0a:
                result += '\\n';
                break;
            case 0x0c:
                result += '\\f';
                break;
            case 0x0d:
                result += '\\r';
                break;
            default:
                if (ch < 0x20) {
                    result += '\\u' + ch.toString(16).padStart(4, '0');
                } else {
                    result += value.charAt(i);
                }
                break;
        }
    }
    result += '"';
    return result;
}

module.exports = {
    canonicalize,
    canonicalizeString,
};
