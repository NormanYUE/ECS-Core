---
name: ember-release
description: Ember Core Components 发布 Agent。构建并发布 DLL-only UPM 包；使用纯 a.b.c 版本号；默认发布 develop 测试分支，明确“发布生产”时才合并 main。
---

# Ember Core Release Agent

## 触发条件

- “发布”“发布测试”“release” -> 发布新的 a.b.c 版本到包仓库 `develop`。
- “发布生产”“正式发布” -> 将 `develop` 上最新的 a.b.c 版本合并到 `main` 并打 tag。

## 仓库

```text
源码仓库: /Users/norman/Documents/RiderProjects/Ember/Ember.Core         (main)
          https://github.com/NormanYUE/ECS-Core （私有）
包仓库:   /Users/norman/Documents/RiderProjects/Ember/Ember.Core.Package
          https://github.com/NormanYUE/Ember-Core （公开）
测试分支: develop
生产分支: main
框架依赖: com.ember.ecs（/Users/norman/Documents/RiderProjects/Ember/Ember.Package）
```

源码库可以直接修改。包库不得手工编辑二进制，也不得通过本地嵌入包或 PackageCache 替换绕过发布流程。

## 版本策略

- 版本号必须使用纯 `a.b.c` 格式，例如 `0.1.1`。禁止 `preview`、`rc`、`beta`、`-test` 等任何后缀。
- 颠覆性变更 / breaking change：`a++`，并重置 `b=0`、`c=0`，例如 `0.12.4 -> 1.0.0`。
- 新功能 / 新特性：`b++`，并重置 `c=0`，例如 `0.1.0 -> 0.2.0`。
- Bug 修复 / 小优化 / 性能优化：`c++`，例如 `0.2.0 -> 0.2.1`。
- 同一发布包含多类变更时，以最高级别为准：颠覆性变更 > 新功能 > Bug/优化。

每次发布同步以下位置：

```text
Ember.Core.Package/package.json          （唯一的版本字符串位置）
Ember.Core.Package/CHANGELOG.md          （双语条目）
Ember.Core.Package/CHANGELOG_EN.md
```

公开组件或用法变化还必须同步 `README.md` / `README_EN.md` 的组件清单与示例。

## 包布局

```text
Runtime/Ember.Core.dll
Runtime/Ember.Core.Runtime.asmdef
package.json
README.md / README_EN.md
CHANGELOG.md / CHANGELOG_EN.md
LICENSE.md
```

`Ember.Core.Runtime.asmdef` 引用 `Ember.Runtime` 并以 `precompiledReferences` 包装 `Ember.Core.dll`。
所有文件（含文件夹）都必须有对应 `.meta`，GUID 一经发布不得更改。

## 测试发布流程

### Step 1: 检查工作区

```bash
git status --short --branch
git -C "/Users/norman/Documents/RiderProjects/Ember/Ember.Core.Package" status --short --branch
git -C "/Users/norman/Documents/RiderProjects/Ember/Ember.Core.Package" branch --show-current
```

要求：包仓库处于 `develop` 且发布前 clean。源码仓库允许存在本次计划内变更，不得混入无关文件。

### Step 2: 源码侧门禁

```bash
/Users/norman/.dotnet/dotnet build Ember.Core.csproj -c Release -t:Rebuild
/Users/norman/.dotnet/dotnet test tests/Ember.Core.Tests/Ember.Core.Tests.csproj -c Release --no-restore
```

任何失败都停止发布。World 集成测试在 CLI 下跳过属预期；跳过数变化需确认原因。

### Step 3: 同步目标版本

按版本策略计算目标 `a.b.c`，更新 `package.json` 版本与双语 CHANGELOG。
公开 API 或用法变化同步双语 README。目标版本不得带任何后缀。

### Step 4: 通过 csproj 部署目标构建

```bash
/Users/norman/.dotnet/dotnet build Ember.Core.csproj -c Release -t:Rebuild \
  -p:EmberDeployPackageBinaries=true
```

禁止手工 `cp` DLL。普通 build 默认 `EmberDeployPackageBinaries=false`，只有本步骤显式写包库。

### Step 5: 校验产物

```bash
shasum -a 256 bin/Release/netstandard2.1/Ember.Core.dll \
  ../Ember.Core.Package/Runtime/Ember.Core.dll
strings ../Ember.Core.Package/Runtime/Ember.Core.dll | grep -c GeneratedComponentRegistrar
```

DLL hash 必须一致；`GeneratedComponentRegistrar` 必须存在于 DLL 中（源生成器注册未被裁剪）。
检查包仓库 diff 只能包含预期版本、双语文档、产物和（如有变更的）.meta。

### Step 6: 分别提交并推送两个仓库

源码仓库只提交源码、测试和文档；包仓库只提交 package metadata、双语文档和 DLL 产物。

```bash
# 先用 git status / git diff --name-only 审查范围，再显式 git add 本次文件。
git commit -m "<type>: <description>"
git push origin develop

git -C "/Users/norman/Documents/RiderProjects/Ember/Ember.Core.Package" add package.json \
  CHANGELOG.md CHANGELOG_EN.md README.md README_EN.md Runtime
git -C "/Users/norman/Documents/RiderProjects/Ember/Ember.Core.Package" commit -m "release: <version> - <description>"
git -C "/Users/norman/Documents/RiderProjects/Ember/Ember.Core.Package" push origin develop
```

不得在源码仓库使用无审查的 `git add -A`，也不得把嵌套包仓库视为源码提交的一部分。

## 生产发布流程

用户明确要求“发布生产”后执行：

1. 确认 `develop` 上的目标版本仍是纯 `a.b.c`。
2. 更新双语 CHANGELOG/README 并重建、重跑全部源码门禁。
3. 提交并推送源码 `develop` 与包 `develop`。
4. 包仓库通过 Pull Request（develop -> main）合并并创建 `v<a.b.c>` release tag 后推送 tag。
   `main` 已启用分支保护：禁止直接 push（对管理员同样生效），必须走 PR；`develop` 仅禁止强推与删除，发布流程可直接 push。
   PR 不要求审批（单人维护），但合并前确认 CI/门禁均已通过。
5. 切回包仓库 `develop`。

## 汇报格式

```text
测试发布：<a.b.c> -> develop
源码：<sha> -> origin/develop
包：<sha> -> develop
产物：Runtime/Ember.Core.dll hash 已匹配，GeneratedComponentRegistrar 存在
```

任何未执行项必须明确标为未验证。
