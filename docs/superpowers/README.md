# Superpowers 工作流（本仓库）

本目录用于存放按照 [superpowers](https://github.com/obra/superpowers) 标准流程产出的设计文档（spec）与实施计划（plan）。

## 安装情况

| 项目 | 值 |
| --- | --- |
| superpowers 版本 | 6.3.0（来自 `obra/superpowers` / `anthropics/claude-plugins-official` 缓存） |
| 安装位置 | `C:\Users\GPP\.claude\skills\superpowers\`（用户级，14 个技能） |
| 技能清单 | `brainstorming`、`writing-plans`、`executing-plans`、`subagent-driven-development`、`test-driven-development`、`systematic-debugging`、`verification-before-completion`、`requesting-code-review`、`receiving-code-review`、`using-git-worktrees`、`finishing-a-development-branch`、`dispatching-parallel-agents`、`writing-skills`、`using-superpowers` |
| 校验 | `Get-ChildItem $env:USERPROFILE\.claude\skills\superpowers` 应列出上述 14 个目录，共 39 个 Markdown 文件 |

> 说明：DSH 会话的技能目录在会话启动时装载，本会话无法通过 `skill` 工具直接加载 superpowers；因此本仓库的代理通过**显式读取技能文件路径**来遵循流程（见下方“执行方式”）。

## 目录约定

```
docs/superpowers/
├── README.md                 # 本文件
├── specs/                    # 设计文档（brainstorming 输出）
│   └── YYYY-MM-DD-<topic>-design.md
└── plans/                    # 实施计划（writing-plans 输出）
    └── YYYY-MM-DD-<topic>.md
```

## 执行方式（无人值守变体）

superpowers 的默认流程包含若干“等待人类确认”的门禁。本仓库的自动化流水线在**用户一次性授权**下运行，因此做如下等价替换，且每处替换都在对应文档中留痕：

| superpowers 原始步骤 | 本仓库的等价做法 |
| --- | --- |
| brainstorming 向用户提问 | 子代理自行调研代码库，把所有原本要问的问题写成「假设 + 默认选择 + 理由 + 风险」 |
| 用户批准设计 | 由独立评审子代理对照仓库现状与 Issue 验收，评审结论写入文档末尾 |
| 用户批准计划 | 由独立评审子代理做「计划可执行性校验」（文件路径存在性、接口一致性、测试可运行性） |
| executing-plans 的分批检查点 | 每个任务完成后由实现子代理自测（`dotnet build` + 相关单元测试），主代理复核 |
| verification-before-completion | 统一跑 Release 构建 + 全量单元测试 + CLI 冒烟测试，证据附在 PR/Release 说明中 |

## 流程要求（对所有参与代理）

1. 先读 `using-superpowers`，再读与当前阶段对应的技能文件（设计阶段 `brainstorming`，计划阶段 `writing-plans`，实现阶段 `test-driven-development` + `subagent-driven-development`，收尾阶段 `verification-before-completion`）。
2. 每个设计/计划文档必须包含：目标、非目标、假设、方案、测试策略、验收标准、风险。
3. 禁止出现 `TBD` / `TODO` / “适当处理错误”这类占位描述；每一步都要能照着执行。
4. 计划中的文件路径、类名、方法签名必须与仓库现状一致；新文件给出确切路径。
5. 实现阶段先写失败测试，再写最小实现，测试通过后再提交。
