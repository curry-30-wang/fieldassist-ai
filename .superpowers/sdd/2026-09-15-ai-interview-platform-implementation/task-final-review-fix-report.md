# 最终整体验收修复报告

## 修复轮次（2026-09-16）

### 内容

- 为报告建立 `session_id` 数据库唯一约束和已有 SQLite 表补索引逻辑；报告首次生成原子保存，后续读取优先返回已保存内容。
- 报告响应新增从答案表恢复的逐题问题、回答、四项评分和反馈；前端首页加载历史记录，支持历史报告进入和 localStorage 刷新恢复。
- 出题、答案评价、报告生成发生 AIServiceError 时，在独立事务中把会话标记为 `failed` 并释放 operation claim；后续请求按终态返回 409。
- 报告总分限制为有限的 0–10 数值，并由服务根据已验证答案评分重新计算；QuestionPanel 新增四项即时评分。

### 问题

- 原实现每次读取报告都会再次调用模型并更新数据库，且并发下缺少报告级唯一性和操作保护。
- 报告页面依赖前端内存，刷新或从历史记录进入时无法恢复逐题结果。
- AI 失败只回滚当前事务，没有持久化 `failed` 状态。
- 模型提供的报告总分缺少有限值和范围校验，也没有与答案评分保持一致。

### 解决方案

- 新增报告读取仓储、报告生成 claim、数据库唯一约束和冲突回归；历史读取不再依赖模型。
- 新增稳定报告响应结构，逐题评价从持久化答案恢复；React 使用 `listInterviews()` 和 `lastInterviewSessionId` 恢复页面。
- 新增独立失败事务，覆盖出题、答题、报告三条路径，并保持创建阶段岗位分析失败不产生会话。
- 使用 Pydantic `FiniteFloat` 与区间约束，并以答案总分平均值覆盖模型总分。

### 验证结果

- 后端完整 pytest：52 passed。
- 前端完整 Vitest：15 passed。
- 前端生产构建：成功，31 modules transformed。
- compileall：成功。
- git diff --check：成功。
- FakeLLM TestClient：创建 201；生成题目 200，共 5 题；提交答案 201，总分 7.5；两次报告读取均为 200、响应完全相同、逐题结果 1 条、报告总分 7.5。

### 下一步计划

- 本轮只提交相关 tracked 代码、测试和 README；该报告位于仓库忽略的 `.superpowers` 目录，仅保留在工作区，不强制加入提交。
- 提交：`d86768e273b320de2ae812a8ee9b3af9a0dc0dc6`（`fix: complete report persistence and history flow`）。

## 修复轮次 2（2026-09-16）

### 内容

- 为 SQLite 旧版 `reports` 表增加唯一索引前的重复行迁移：按 `rowid` 保留每个 `session_id` 最早的一条记录。
- 新增旧表重复报告行的升级回归测试，覆盖合并、唯一约束和其他会话数据保留。

### 问题

- 旧 SQLite 数据库可能已存在同一 `session_id` 的多条报告记录，直接创建唯一索引会失败。

### 解决方案

- 仅当当前数据库方言为 SQLite 时，在报告唯一索引创建前删除同一会话中除最小 `rowid` 外的旧记录；不改变 questions/answers 迁移逻辑，也不删除其他会话数据。

### 验证结果

- 后端完整 pytest：53 passed（128 warnings）。
- compileall：成功。
- git diff --check：成功。
