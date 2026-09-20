# FieldAssist Portfolio Polish Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 FieldAssist 整理成适合 GitHub 作品集和 AI/FDE 求职展示的项目，补充真实页面截图、架构图、运行说明和可直接使用的简历素材。

**Architecture:** 保留现有 FastAPI、Vue 3、Mock/Dify、Langfuse 和 SQLite 结构，不新增运行时依赖。README 作为作品集入口，使用 GitHub 原生 Mermaid 展示请求链路，并链接到架构、运行手册、演示脚本和简历素材；截图只展示本地 Mock 演示数据。

**Tech Stack:** Markdown、Mermaid、PNG screenshots、现有 Python/FastAPI/Vue 3 项目、pytest、Git。

**Spec:** `docs/superpowers/specs/2026-09-07-fieldassist-design.md`

## Global Constraints

- 不把论文中的预测指标或 Mock 演示数据写成线上生产成果。
- 不把 Dify、Langfuse 或 n8n 的真实服务状态写成已部署状态。
- 不提交 `.env`、密钥、SQLite 数据库、虚拟环境、缓存或日志。
- 截图只使用项目本地 Mock 演示页面，不包含真实个人账号、密码或密钥。
- 保留现有代码和文档结构，只修改作品集展示直接相关的文件。
- 不运行或反编译用户提供的安装包；公开上传前只检查文件大小、扩展名和哈希，并提醒用户确认其中没有公司机密或真实业务数据。

### Task 1: Prepare portfolio screenshots

**Files:**
- Create: `docs/assets/fieldassist-employee-chat.png`
- Create: `docs/assets/fieldassist-admin-dashboard.png`
- Create: `docs/assets/fieldassist-admin-evaluation.png`

**Interfaces:**
- Consumes: local FieldAssist Mock mode at `http://127.0.0.1:8000/`.
- Produces: three readable screenshots that demonstrate employee Q&A, admin operations, and evaluation results.

- [ ] **Step 1: Start the local application in Mock mode**

Run from the repository root:

```powershell
.\.venv\Scripts\python.exe backend\run.py
```

Expected: the application listens on `http://127.0.0.1:8000`.

- [ ] **Step 2: Log in with the seeded employee and admin demo accounts**

Use only the documented local demo accounts:

```text
employee@fieldassist.local / 由本机 DEMO_ADMIN_PASSWORD 配置
admin@fieldassist.local / 由本机 DEMO_ADMIN_PASSWORD 配置
```

- [ ] **Step 3: Capture the three named pages**

Capture the employee Q&A page with an answer and citation, the admin overview page, and the admin quality-evaluation page after running the fixed evaluation set.

- [ ] **Step 4: Inspect each PNG**

Confirm that text is readable, no secret is visible, and the screenshots show the intended pages.

### Task 2: Update portfolio documentation

**Files:**
- Modify: `README.md`
- Modify: `docs/architecture.md`
- Modify: `docs/resume.md`
- Create: `docs/architecture.mmd`

**Interfaces:**
- Consumes: the three screenshots from Task 1 and the existing application contracts.
- Produces: a README that explains the business value, architecture, quick start, screenshots, verification, and resume-ready project facts.

- [ ] **Step 1: Add a README project summary and project navigation**

Keep the existing Mock quick start and add a concise value statement, feature list, architecture link, screenshots section, verification facts, and resume-use notes.

- [ ] **Step 2: Add the Mermaid architecture diagram**

Describe the browser, same-origin API, FastAPI routes/services, provider adapters, tracing adapters, and SQLite boundary. Keep external Dify/Langfuse optional and n8n marked as a future extension.

- [ ] **Step 3: Add screenshot links**

Use relative links under `docs/assets/` and captions that identify each page as a local Mock-mode demonstration.

- [ ] **Step 4: Replace resume placeholders with evidence-backed facts**

Use the verified local test count of 111 passed tests and describe the project without claiming production deployment or real customer usage.

- [ ] **Step 5: Run Markdown and repository hygiene checks**

Run:

```powershell
git diff --check
git status --short
```

Expected: no whitespace errors and no forbidden files staged for the portfolio change.

### Task 3: Verify, commit, and prepare GitHub rename

**Files:**
- Modify: GitHub repository metadata only after user confirmation.
- Modify: local Git remote URL only after the repository rename succeeds.

**Interfaces:**
- Consumes: the documentation and screenshots from Task 2.
- Produces: a verified local portfolio commit ready to push, followed by the GitHub repository rename to `fieldassist-ai`.

- [ ] **Step 1: Run the existing automated test suite**

Run:

```powershell
.\.venv\Scripts\python.exe -m pytest tests -q
```

Expected: 111 tests pass with no failures.

- [ ] **Step 2: Verify frontend and Python syntax**

Run:

```powershell
.\.venv\Scripts\python.exe -m compileall backend tests
node --check frontend/app.js
```

Expected: both commands exit with code 0.

- [ ] **Step 3: Review the final diff**

Confirm that only README/docs/assets and the plan file changed, and that screenshot paths resolve.

- [ ] **Step 4: Ask for confirmation immediately before public GitHub changes**

The rename, commit push, and README publication change the public repository. Do not perform those actions before the user confirms the prepared diff.

- [ ] **Step 5: Rename the GitHub repository and update the local remote**

Rename `curry-30-wang/123` to `curry-30-wang/fieldassist-ai` through the signed-in GitHub page, then update the local remote to:

```text
https://github.com/curry-30-wang/fieldassist-ai.git
```

- [ ] **Step 6: Verify the public result**

Verify that the renamed repository opens, the default branch still contains the portfolio documentation, and the screenshots render from the README.

### Task 4: Add the office digitization project artifact

**Files:**
- Create: `portfolio/README.md`
- Modify: `README.md`
- Modify: `docs/resume.md`

**Interfaces:**
- Consumes: the user-provided installer at `C:/Users/DELL/Documents/Codex/2026-08-07/new-chat-2/outputs/AsphaltPlantManager-Setup-1.0.5.exe`.
- Produces: a clearly labeled portfolio download link and evidence-backed resume wording for the office document/statistics tool; the large installer is uploaded as a GitHub Release asset instead of Git history.

- [ ] **Step 1: Inspect the installer without executing it**

Record its size, extension, and SHA-256 hash. Do not launch, install, or upload it if it contains secrets or real company data.

- [ ] **Step 2: Prepare the portfolio download documentation**

Record the exact filename, size, SHA-256 hash, and a GitHub Releases download path. Do not add the 224.12 MB binary to the Git commit because GitHub blocks regular repository files larger than 100 MiB.

- [ ] **Step 3: Document the project without inventing implementation details**

Describe it as an office data and statistics tool created during the user’s role at 驻马店市公路工程开发有限公司. Do not claim a programming language, database, user count, or measured efficiency unless the artifact or user confirms it.

- [ ] **Step 4: Upload the installer as a GitHub Release asset**

After the repository rename, create release tag `v1.0.5` and upload the original installer without renaming it. Do not publish it as a source-tree file.

- [ ] **Step 5: Verify artifact integrity**

Recompute the SHA-256 hash from the original local file and compare it with the release asset digest when GitHub exposes it; run `git diff --check` and confirm the README link target exists.
