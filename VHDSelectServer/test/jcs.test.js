'use strict';

const assert = require('node:assert/strict');
const fs = require('fs');
const path = require('path');
const test = require('node:test');

const { canonicalize, canonicalizeString } = require('../jcs');

/**
 * 跨语言夹具测试（任务 18.1 / 14.6 / Requirement 15.5）：与 C# JcsCanonicalizer.cs
 * 共用同一份 test-fixtures/jcs-vectors.json，断言两端输出字节相等。
 *
 * 本文件只校验 Node.js 端的 jcs.js 与夹具 expected 字段一致；C# 端的对应测试在
 * VHDMounter.Tests/RustDeskBridge/Properties/JcsCanonicalizerVectorTests.cs 中。
 *
 * 加 Property 17 的 trait 等价 description（按 tasks.md "Trait 等价 node test annotation" 约定）。
 */

const FIXTURE_PATH = path.join(__dirname, '..', '..', 'test-fixtures', 'jcs-vectors.json');

function loadFixture() {
    const raw = fs.readFileSync(FIXTURE_PATH, 'utf8');
    return JSON.parse(raw);
}

test('rustdesk-bridge-host > Property 5 (cross-lang JCS): fixture file exists', () => {
    assert.ok(fs.existsSync(FIXTURE_PATH), `JCS 跨语言夹具不存在: ${FIXTURE_PATH}`);
});

test('rustdesk-bridge-host > Property 5 (cross-lang JCS): every fixture case matches Node jcs.js byte-for-byte', () => {
    const fixture = loadFixture();
    assert.ok(Array.isArray(fixture.cases) && fixture.cases.length > 0, '夹具至少有一条 case');

    for (const caseEntry of fixture.cases) {
        const actualString = canonicalizeString(caseEntry.input);
        assert.equal(
            actualString,
            caseEntry.expected,
            `JCS case "${caseEntry.name}" Node 输出不匹配: actual=${JSON.stringify(actualString)} expected=${JSON.stringify(caseEntry.expected)}`,
        );

        const actualBytes = canonicalize(caseEntry.input);
        const expectedBytes = Buffer.from(caseEntry.expected, 'utf8');
        assert.deepEqual(
            Array.from(actualBytes),
            Array.from(expectedBytes),
            `JCS case "${caseEntry.name}" Node 字节输出与 UTF-8(expected) 不一致`,
        );
    }
});

test('rustdesk-bridge-host > Property 5 (cross-lang JCS): canonicalize(canonicalize.parse) is idempotent', () => {
    const fixture = loadFixture();
    for (const caseEntry of fixture.cases) {
        const once = canonicalizeString(caseEntry.input);
        // 把规范化后的 JSON 再 parse → canonicalize 一次必须输出相同字节
        const parsed = JSON.parse(once);
        const twice = canonicalizeString(parsed);
        assert.equal(twice, once, `JCS case "${caseEntry.name}" 第二次规范化结果不等`);
    }
});

test('rustdesk-bridge-host > Property 5 (cross-lang JCS): rejects NaN / Infinity numbers', () => {
    assert.throws(() => canonicalizeString(NaN), /NaN/);
    assert.throws(() => canonicalizeString(Infinity), /Infinity/);
    assert.throws(() => canonicalizeString(-Infinity), /Infinity/);
});

test('rustdesk-bridge-host > Property 5 (cross-lang JCS): negative zero canonicalizes to "0"', () => {
    assert.equal(canonicalizeString(-0), '0');
});
