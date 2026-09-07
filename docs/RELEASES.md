# X C盘巡检官：Releases 发布规则与历史

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

也可以推送修改 `.github/release-request.json` 的提交到 `main` 触发补发（workflow 会解析该文件中的 `tag` / `target_ref` / `prerelease`）。

工作流会从 `target_ref` 重新构建，而不是直接信任旧二进制文件。

## 已发布版本总览

截至 2026-09-08，Releases 页面共 7 个版本，每个都包含 EXE、ZIP、SHA256 三个资产，全部由 self-hosted Windows Runner（`aix-runner`）或真机手动门禁构建，并通过 `verify-win.ps1` 验证门后才发布：

| Tag | 类型 | Source commit | 说明 |
|---|---|---|---|
| `v1.0.0` | 正式版 | `6c1f3cf60f5a1b46b65719447dead2a1dc359d63` | PR #1 正式封版；45/45 tests PASS，WPF Release 0 warning / 0 error，single-file publish PASS |
| `v1.0.0-pr2-2f39853` | 预发布 | `2f39853b5aae741a482f01f0c3d1525b8d34b510` | Windows 本机 50/50 tests PASS；已验证 EXE 大小 139,895,566 bytes，用户历史 EXE SHA256 `1503D207FBE428767E4B16AF6E5724EEB0348C43857A99C5EC1A6B18DB7455C5` |
| `v1.0.0-pr2-e5c01c` | 预发布 | `e5c01cac9e28030487cc6f67aa282f3ea821f05b` | PR #2 已验证 head 之一，补发构建成功 |
| `v1.0.0-pr2-bd248d7` | 预发布 | `bd248d71cac422867999904fc75ba53d27a66ffb` | PR #2 已验证 head 之一，补发构建成功 |
| `v1.0.0-pr2-f510b05-hotfix1` | 预发布 | `b22e0c394ef6b782953470c238ad22fe493211d2` | f510b05 功能线的修复版（见下文"不发布说明"） |
| `v1.0.0-pr2-f510b05-hotfix2` | 预发布 | `ed4943535b705e40a84c140ad00525dc0b4eb681` | 修复 WPF 启动崩溃（ProgressBar TwoWay 绑定改 OneWay，PR #10）；verify-win.ps1 增加 EXE 启动冒烟测试 |
| `v1.0.0-pr2-f510b05-hotfix3` | 预发布 | `b68049417b72f9c7240c7ccb9346e42e30e14a6b` | 修复 UAC 请求通道（PR #11）：Elevation 存储迁移至 ProgramData + ACL 初始化 + request 落盘校验 + worker 分类错误码 + elevation.log；Issue #3 Windows 真机验收通过（62/62 tests PASS） |

## 原始 v1.0.0-pr2-f510b05：不发布说明

原始 `f510b05aaeca4498915d4eaf9f14e5f0d067d7b3` 的源码因 `ElevatedCleanupBridge.cs` 缺少 `using System.IO;` 导入，导致 `File` / `Directory` / `Path` 编译失败。该失败被发布门（self-hosted Windows 真机构建）正确拦截，未生成伪 Release。

处理方式：

- 不为原始 `f510b05` 补发 Release；
- 修复通过 PR #9 合入 `main`（添加 `using System.IO;`）；
- 替代版本为 `v1.0.0-pr2-f510b05-hotfix1`，target 锁定修复提交 `b22e0c394ef6b782953470c238ad22fe493211d2`。

## 待来源唯一确认的历史二进制

### X-C盘巡检官-v1.0.0-pr2-win-x64.exe

- 文件大小：139,891,470 bytes
- SHA256：`95930CEC80B57255D491A07B540883D37255591F71C0A977C3B67625E7AC1FC4`

该大小在 PR #2 的多个已验证 head 中出现过：

- `e5c01cac9e28030487cc6f67aa282f3ea821f05b`
- `bd248d71cac422867999904fc75ba53d27a66ffb`

仅凭文件名和文件大小无法证明该二进制究竟来自哪一个 commit，因此不把它绑定到具体 Release Tag。避免错误发布来源。

## 当前 main

- 历史版本补发批次已全部完成；hotfix3（UAC 请求通道修复，PR #11）已通过 Windows 真机验收并发布。
- Issue #3（Windows 真机专项验收：管理员清理、删除进度与 10GB+ 大文件响应性）已于 2026-09-08 关闭：UAC 成功路径（成功 9 / 释放 10.09 GB，elevation.log 全链路）、UAC 取消路径（失败保留、无提权、窗口可用）、10GB+ 大文件删除响应性、62/62 tests PASS 全部达成。
- 体验优化项（删除成功后候选列表自动刷新）由 Issue #12 跟踪，不阻塞 v1.0.1。
- 下一版本 `v1.0.1` Stable 发布进行中（Tag + Release 由发布流程生成）。
- 发布自动化基于 self-hosted Runner（Windows 真机计划任务 `GitHubActionsRunner`）执行；其安装与维护见 `docs/SELF_HOSTED_RUNNER.md`。

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
