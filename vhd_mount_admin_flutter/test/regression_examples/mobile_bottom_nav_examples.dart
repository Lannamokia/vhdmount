// Regression examples for the `mobile-bottom-nav-redesign` PBT suite.
//
// **Purpose:** 把 PBT（Glados / Property-Based Tests）失败时收敛出的 shrunk
// counterexample 永久固化为 example test，与 property test 一起跑。这样未来
// 即便随机种子换掉、或 PBT 范围调整，老反例也不会回归。
//
// **How to use:** 当
//   * `test/dashboard/*.dart`（Property 1 / 3 / 5 / 8 / 14）或
//   * `test/widgets/mobile_bottom_nav_*.dart`（Property 2 / 4 / 6 / 7 / 10–13）或
//   * `test/widgets/dashboard_screen_*.dart`（Property 9 / 15 / 16）
// 中任一 PBT 用例失败，Glados 会在 console 打印形如
// `Tested 17 inputs, shrunk 14 times. Failing for input: ...` 的日志。把
// 那个最小化反例直接拷贝到本文件中作为一条新的 `test(...)`，命名约定：
//
//     `[mobile-bottom-nav-redesign] regression: <Property X> <一句话场景>`
//
// 每条 regression test 应：
//   1. 引用对应 Property 编号与 `Validates: Requirements ...` 注释；
//   2. 直接执行硬编码反例（不再随机），断言 expect 通过。
//
// **空占位说明：** 首次创建本文件时尚无任何 PBT 失败被收敛过，因此
// `void main()` 内当前为空。这并非未实现——空 main 在 `flutter test` 下
// 同样合法，会被报告为「No tests ran」并以 exit code 0 结束（与
// regression_examples 目录其它子模块的占位约定一致）。后续 PBT 失败时
// 直接在此 main 内追加 `test(...)` 即可。

void main() {
  // 占位：尚无 shrunk counterexample 被回写。后续 PBT 失败时在此追加 test。
}
