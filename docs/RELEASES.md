# X C盘巡检官：Releases 发布规则与历史补发表

## 自动发布规则

仓库已启用 `.github/workflows/release-win-x64.yml`。

### 未来版本

推送任意 `v*` Tag 后，GitHub Actions 会自动：

1. Checkout 对应 Tag；
2. 安装 .NET 10；
3. 运行 `scripts/verify-win.ps1`；
4. 生成 Windows x64 self-contained single-file EXE；
5. 生成 ZIP；
6. 生成 SHA256 文件；
7. 创建/更新 GitHub Release；
8. 上传 EXE、ZIP、SHA256；
9. 额外保留 Actions artifact 30 天。

发布前验证失败时，不创建 Release。

### 历史版本补发

在 GitHub Actions 中手动运行 `Release Windows x64`，填写：

- `tag`：Release Tag；
- `target_ref`：要构建的历史 commit / tag / branch；
- `prerelease`：是否预发布。

工作流会从 `target_ref` 重新构建，而不是直接信任旧二进制文件。

## 已确认历史版本

### v1.0.0-pr2-2f39853

- 建议 Tag：`v1.0.0-pr2-2f39853`
- target_ref：`2f39853b5aae741a482f01f0c3d1525b8d34b510`
- Windows 验证：50/50 tests PASS
- WPF Release：0 warning / 0 error
- 已验证 EXE 大小：139,895,566 bytes
- 用户提供历史 EXE：`X-C盘巡检官-v1.0.0-pr2-2f39853-win-x64.exe`
- 用户提供 EXE SHA256：`1503D207FBE428767E4B16AF6E5724EEB0348C43857A99C5EC1A6B18DB7455C5`

该版本的 commit 与已验证 EXE 大小唯一对应，可直接按上述 target_ref 补发。

## 待来源唯一确认的历史二进制

### X-C盘巡检官-v1.0.0-pr2-win-x64.exe

- 文件大小：139,891,470 bytes
- SHA256：`95930CEC80B57255D491A07B540883D37255591F71C0A977C3B67625E7AC1FC4`

该大小在 PR #2 的多个已验证 head 中出现过，包括：

- `e5c01cac9e28030487cc6f67aa282f3ea821f05b`
- `bd248d71cac422867999904fc75ba53d27a66ffb`

仅凭文件名和文件大小无法证明该二进制究竟来自哪一个 commit，因此暂不把它绑定到具体 Release Tag。避免错误发布来源。

## 当前 main

PR #2 merge commit：

`fbad729cc8541d233d178dff9934aaeb9dc5050f`

Release 自动化 merge commit：

`a60340e1b47fbe6cbaae320fef06efbc20cbe3d6`

当前管理员清理 / 删除进度 / 10GB+ 大文件响应性仍由 Issue #3 作为专项 Windows 真机验收门。

## 版本发布原则

每个 Release 至少包含：

- Tag
- Source commit
- Windows x64 EXE
- ZIP
- SHA256 文件
- 验证状态
- Release Notes

不要仅凭文件名或文件大小推断 Source commit。历史二进制只有在来源可唯一确认时才与 Tag 绑定。
