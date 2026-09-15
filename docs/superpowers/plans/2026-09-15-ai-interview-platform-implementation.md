# 智能面试辅助平台 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 Windows 本地完成一个可运行、可测试的 AI 智能面试辅助平台，实现“上传资料 → 生成题目 → 提交答案 → 获取评分 → 查看报告”的完整闭环。

**Architecture:** 后端采用 FastAPI 分层结构，路由只负责 HTTP 参数和响应，InterviewService 负责业务流程，LLMProvider 和 Retriever 使用接口隔离外部模型及检索实现。前端采用 React + Vite，通过 REST API 操作面试会话；SQLite 保存业务数据，TF-IDF 保存小规模本地资料检索能力。

**Tech Stack:** Python 3.11+、FastAPI、Pydantic、SQLAlchemy、SQLite、PyMuPDF、python-docx、scikit-learn、httpx、pytest、pytest-asyncio、React、Vite、Vitest。

**Spec:** `docs/superpowers/specs/2026-09-15-ai-interview-platform-design.md`

## Global Constraints

- 后端：Python 3.11+、FastAPI、Pydantic、SQLAlchemy。
- 前端：React、Vite、基础 CSS。
- 数据库：SQLite，避免本地部署依赖 MySQL 服务。
- 文档解析：PyMuPDF 解析 PDF，python-docx 解析 DOCX。
- 检索：文档分块后使用轻量 TF-IDF 相似度检索，封装成独立 Retriever 接口；后续可以替换为向量模型而不改业务层。
- 大模型：OpenAI 兼容接口，默认支持 DeepSeek，也支持通过环境变量切换到 OpenAI 兼容服务或本地 Ollama。
- 测试：pytest、FastAPI TestClient；测试使用 FakeLLMProvider，不依赖真实 API 和网络。
- 第一版只面向单个求职者，不做注册、登录和多人协作。
- 默认生成 5 道题。
- 评分维度为准确性、完整性、岗位相关性和表达清晰度，每项 0 到 10 分。
- 第一版不实现语音识别、视频面试、在线搜索、复杂权限、支付、模型训练、Docker、云服务器和 Kubernetes。
- 不在代码中保存真实 API 密钥。
- 每个任务都必须先写失败测试，再写让测试通过的最小实现。

## File Map

项目根目录为 `ai-interview-platform/`，相对于当前工作目录 `D:\文档\ChatGPT\1 2`。

- `backend/app/main.py`：创建 FastAPI 应用、注册路由和健康检查。
- `backend/app/config.py`：环境变量和运行配置。
- `backend/app/db.py`：SQLAlchemy 引擎、会话工厂和建表入口。
- `backend/app/models.py`：六张业务表对应的 ORM 模型。
- `backend/app/schemas.py`：岗位分析、题目、评分和报告的数据校验模型。
- `backend/app/domain.py`：会话状态和状态转换规则。
- `backend/app/repositories.py`：会话、文档、题目、答案和报告的数据库读写。
- `backend/app/services/document_parser.py`：PDF、DOCX、TXT 文本解析和分块。
- `backend/app/services/retriever.py`：Retriever 协议和 TF-IDF 实现。
- `backend/app/services/llm.py`：FakeLLMProvider 和 OpenAI 兼容模型适配器。
- `backend/app/services/prompts.py`：岗位分析、出题、评分和报告提示词。
- `backend/app/services/interview_service.py`：面试业务流程编排。
- `backend/app/api/interviews.py`：面试会话、资料和报告接口。
- `backend/app/api/questions.py`：题目和答案接口。
- `backend/tests/`：后端单元测试和 API 集成测试。
- `frontend/src/App.jsx`：页面路由和整体页面状态。
- `frontend/src/api.js`：统一的前端 API 请求封装。
- `frontend/src/components/`：资料上传、题目答题、评分卡片和报告组件。
- `frontend/src/App.test.jsx`：前端基础渲染测试。
- `sample_data/`：不含真实隐私信息的演示简历和岗位描述。
- `README.md`：安装、配置、启动、测试和演示说明。
- `.env.example`：模型服务配置示例，不包含真实密钥。
- `.gitignore`：忽略虚拟环境、构建产物、数据库文件和本地密钥。

---

### Task 1: 创建可测试的项目骨架和健康检查

**Files:**
- Create: `ai-interview-platform/backend/pyproject.toml`
- Create: `ai-interview-platform/backend/app/__init__.py`
- Create: `ai-interview-platform/backend/app/config.py`
- Create: `ai-interview-platform/backend/app/main.py`
- Create: `ai-interview-platform/backend/tests/test_health.py`
- Create: `ai-interview-platform/.gitignore`

**Interfaces:**
- Produces `create_app() -> FastAPI` and module-level `app: FastAPI` for later routers and TestClient 使用。
- Produces `Settings` with `database_url`, `llm_base_url`, `llm_api_key`, `llm_model`, `max_upload_mb`。

- [ ] **Step 1: Write the failing test**

```python
from fastapi.testclient import TestClient

from app.main import app


def test_health_endpoint_returns_service_status():
    response = TestClient(app).get("/api/health")

    assert response.status_code == 200
    assert response.json() == {
        "status": "ok",
        "service": "ai-interview-platform",
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run from `ai-interview-platform/backend/`:

```powershell
python -m pytest tests/test_health.py::test_health_endpoint_returns_service_status -q
```

Expected: FAIL because `app.main` and the `/api/health` route do not exist。

- [ ] **Step 3: Write the minimal implementation**

Create `config.py` with a `Settings` dataclass whose defaults are:

```python
database_url = "sqlite:///./data/interviews.db"
llm_base_url = "https://api.deepseek.com/v1"
llm_api_key = ""
llm_model = "deepseek-chat"
max_upload_mb = 10
```

Create `main.py` with this application factory shape:

```python
from fastapi import FastAPI


def create_app() -> FastAPI:
    application = FastAPI(title="AI Interview Platform")

    @application.get("/api/health")
    def health() -> dict[str, str]:
        return {"status": "ok", "service": "ai-interview-platform"}

    return application


app = create_app()
```

Add FastAPI, Uvicorn and pytest dependencies to `pyproject.toml`，并设置 `pythonpath = ["."]`。

- [ ] **Step 4: Run the test to verify it passes**

```powershell
python -m pytest tests/test_health.py::test_health_endpoint_returns_service_status -q
```

Expected: PASS。

- [ ] **Step 5: Commit the task**

```powershell
git add ai-interview-platform
git commit -m "chore: scaffold ai interview platform"
```

### Task 2: 建立数据库模型、业务数据校验和状态规则

**Files:**
- Create: `ai-interview-platform/backend/app/db.py`
- Create: `ai-interview-platform/backend/app/models.py`
- Create: `ai-interview-platform/backend/app/schemas.py`
- Create: `ai-interview-platform/backend/app/domain.py`
- Create: `ai-interview-platform/backend/tests/test_schemas.py`
- Create: `ai-interview-platform/backend/tests/test_domain.py`

**Interfaces:**
- Produces `create_engine_and_session(database_url: str)` and `init_db(engine) -> None`。
- Produces ORM models `InterviewSession`, `Document`, `DocumentChunk`, `Question`, `Answer`, `Report`。
- Produces Pydantic models `JobAnalysis`, `GeneratedQuestion`, `QuestionSet`, `ScoreBreakdown`, `AnswerEvaluation`, `InterviewReport`。
- Produces `InterviewStatus` and `can_transition(current, target) -> bool`。

- [ ] **Step 1: Write the failing tests**

`tests/test_schemas.py`：

```python
import pytest
from pydantic import ValidationError

from app.schemas import ScoreBreakdown


def test_score_breakdown_calculates_average_total():
    score = ScoreBreakdown(
        accuracy=8,
        completeness=6,
        relevance=9,
        clarity=7,
    )

    assert score.total_score == 7.5


def test_score_breakdown_rejects_score_outside_zero_to_ten():
    with pytest.raises(ValidationError):
        ScoreBreakdown(accuracy=11, completeness=6, relevance=7, clarity=8)
```

`tests/test_domain.py`：

```python
from app.domain import InterviewStatus, can_transition


def test_created_session_can_start():
    assert can_transition(InterviewStatus.CREATED, InterviewStatus.IN_PROGRESS)


def test_completed_session_cannot_return_to_progress():
    assert not can_transition(InterviewStatus.COMPLETED, InterviewStatus.IN_PROGRESS)
```

- [ ] **Step 2: Run the tests to verify they fail**

```powershell
python -m pytest tests/test_schemas.py tests/test_domain.py -q
```

Expected: FAIL because the schemas and domain rules have not been implemented。

- [ ] **Step 3: Write the minimal implementation**

Implement `ScoreBreakdown` with four `float` fields using `Field(ge=0, le=10)` and a read-only `total_score` property returning the rounded arithmetic average。

Implement these exact statuses and transitions:

```python
class InterviewStatus(str, Enum):
    CREATED = "created"
    IN_PROGRESS = "in_progress"
    COMPLETED = "completed"
    FAILED = "failed"


ALLOWED_TRANSITIONS = {
    InterviewStatus.CREATED: {InterviewStatus.IN_PROGRESS, InterviewStatus.FAILED},
    InterviewStatus.IN_PROGRESS: {InterviewStatus.COMPLETED, InterviewStatus.FAILED},
    InterviewStatus.COMPLETED: set(),
    InterviewStatus.FAILED: set(),
}
```

Use SQLAlchemy 2 declarative models with string UUID primary keys and JSON text fields where the specification names `*_json`。`init_db()` must create all six tables and must not delete existing tables。

Define the Pydantic response models so invalid model output cannot enter the service layer：`JobAnalysis` has `job_title`, `skills`, `responsibilities`, `experience_requirements`；`GeneratedQuestion` has `question_text`, `question_type`, `difficulty`, `focus_points`, `reference_direction`；`QuestionSet` has exactly `questions: list[GeneratedQuestion]`；`AnswerEvaluation` has `score: ScoreBreakdown`, `strengths`, `problems`, `suggestions`, `answer_structure`；`InterviewReport` has `total_score`, `summary`, `weaknesses`, `recommendations`。

- [ ] **Step 4: Run the tests to verify they pass**

```powershell
python -m pytest tests/test_schemas.py tests/test_domain.py -q
```

Expected: PASS。

- [ ] **Step 5: Commit the task**

```powershell
git add backend/app/db.py backend/app/models.py backend/app/schemas.py backend/app/domain.py backend/tests
git commit -m "feat: add interview domain models and validation"
```

### Task 3: 实现文档解析、文本分块和本地检索

**Files:**
- Create: `ai-interview-platform/backend/app/services/__init__.py`
- Create: `ai-interview-platform/backend/app/services/document_parser.py`
- Create: `ai-interview-platform/backend/app/services/retriever.py`
- Modify: `ai-interview-platform/backend/pyproject.toml`
- Create: `ai-interview-platform/backend/tests/test_document_parser.py`
- Create: `ai-interview-platform/backend/tests/test_retriever.py`

**Interfaces:**
- Produces `DocumentParser.parse(filename: str, content_type: str, data: bytes) -> str`。
- Produces `split_text(text: str, chunk_size: int = 800, overlap: int = 100) -> list[str]`。
- Produces `ChunkRecord` and `RetrievedChunk` dataclasses。
- Produces `Retriever` protocol with `index(records: Sequence[ChunkRecord]) -> None` and `search(query: str, top_k: int = 4) -> list[RetrievedChunk]`。
- Produces `TfidfRetriever` implementing `Retriever`，使用字符 n-gram 以兼容中文文本。

- [ ] **Step 1: Write the failing tests**

```python
import fitz
import pytest

from app.services.document_parser import DocumentParser, split_text


def test_parser_reads_plain_text_and_rejects_empty_content():
    parser = DocumentParser()

    assert parser.parse("job.txt", "text/plain", "Python FastAPI".encode()) == "Python FastAPI"
    with pytest.raises(ValueError, match="empty"):
        parser.parse("empty.txt", "text/plain", b"   ")


def test_parser_reads_pdf_text():
    document = fitz.open()
    page = document.new_page()
    page.insert_text((72, 72), "FastAPI backend interview")
    pdf_bytes = document.tobytes()
    document.close()

    text = DocumentParser().parse("resume.pdf", "application/pdf", pdf_bytes)

    assert "FastAPI backend interview" in text


def test_split_text_keeps_order_and_overlap():
    chunks = split_text("abcdefghij", chunk_size=6, overlap=2)

    assert chunks == ["abcdef", "efghij"]
```

`tests/test_retriever.py`：

```python
from app.services.retriever import ChunkRecord, TfidfRetriever


def test_retriever_returns_most_relevant_chunk_first():
    retriever = TfidfRetriever()
    retriever.index([
        ChunkRecord("resume", 0, "熟悉 Python 和 FastAPI 接口开发"),
        ChunkRecord("resume", 1, "负责网页视觉样式设计"),
    ])

    results = retriever.search("Python FastAPI", top_k=1)

    assert len(results) == 1
    assert results[0].chunk_index == 0
    assert results[0].score > 0
```

- [ ] **Step 2: Run the tests to verify they fail**

```powershell
python -m pytest tests/test_document_parser.py tests/test_retriever.py -q
```

Expected: FAIL because the parser and retriever modules do not exist。

- [ ] **Step 3: Write the minimal implementation**

`DocumentParser.parse()` 根据后缀和 MIME 类型分派到 TXT、PDF 或 DOCX 解析器；只接受 `.txt`、`.pdf`、`.docx`，其他类型抛出 `ValueError("unsupported file type")`。解析后统一去除首尾空白，空结果抛出 `ValueError("empty document")`。PDF 使用 `fitz.open(stream=data, filetype="pdf")`，DOCX 使用 `Document(io.BytesIO(data))`。

`split_text()` 必须保证 `0 <= overlap < chunk_size`，按字符切片并逐块前移 `chunk_size - overlap`；对空文本返回空列表。

`TfidfRetriever` 使用 `TfidfVectorizer(analyzer="char", ngram_range=(2, 4))`。`search()` 对查询向量和文档矩阵计算余弦相似度，过滤 0 分结果，按分数降序返回最多 `top_k` 条 `RetrievedChunk`。

- [ ] **Step 4: Run the tests to verify they pass**

```powershell
python -m pytest tests/test_document_parser.py tests/test_retriever.py -q
```

Expected: PASS。

- [ ] **Step 5: Commit the task**

```powershell
git add backend/app/services backend/pyproject.toml backend/tests
git commit -m "feat: add document parsing and local retrieval"
```

### Task 4: 建立模型适配器、提示词和 FakeLLMProvider

**Files:**
- Create: `ai-interview-platform/backend/app/services/llm.py`
- Create: `ai-interview-platform/backend/app/services/prompts.py`
- Create: `ai-interview-platform/backend/.env.example`
- Create: `ai-interview-platform/backend/tests/test_llm_provider.py`
- Modify: `ai-interview-platform/backend/pyproject.toml`

**Interfaces:**
- Produces async protocol `LLMProvider` with `analyze_job(job_description) -> JobAnalysis`、`generate_questions(job, context) -> QuestionSet`、`evaluate_answer(question, answer, context) -> AnswerEvaluation`、`build_report(evaluations) -> InterviewReport`。
- Produces `FakeLLMProvider`，每个方法返回合法且固定的 Pydantic 数据。
- Produces `OpenAICompatibleLLM`，通过 `httpx.AsyncClient` 调用 `/chat/completions`，从环境变量读取地址、模型和密钥。
- `OpenAICompatibleLLM.from_settings() -> OpenAICompatibleLLM` 负责从 `Settings` 创建真实模型适配器。

- [ ] **Step 1: Write the failing tests**

```python
import pytest

from app.services.llm import FakeLLMProvider


@pytest.mark.anyio
async def test_fake_provider_generates_five_interview_questions():
    provider = FakeLLMProvider()

    analysis = await provider.analyze_job("招聘 Python 后端开发，要求 FastAPI")
    question_set = await provider.generate_questions(analysis, [])

    assert len(question_set.questions) == 5
    assert question_set.questions[0].question_type in {"基础知识", "项目经历", "场景分析"}


@pytest.mark.anyio
async def test_fake_provider_returns_bounded_answer_scores():
    provider = FakeLLMProvider()
    analysis = await provider.analyze_job("招聘 Python 后端开发")
    question_set = await provider.generate_questions(analysis, [])

    evaluation = await provider.evaluate_answer(question_set.questions[0], "我会使用 FastAPI", [])

    assert 0 <= evaluation.score.total_score <= 10
    assert evaluation.suggestions
```

- [ ] **Step 2: Run the tests to verify they fail**

```powershell
python -m pytest tests/test_llm_provider.py -q
```

Expected: FAIL because `FakeLLMProvider` and `LLMProvider` do not exist。

- [ ] **Step 3: Write the minimal implementation**

让 `FakeLLMProvider.generate_questions()` 返回 5 道固定题目，至少覆盖基础知识、项目经历和场景分析三种类型；让 `evaluate_answer()` 返回四项合法分数、优点、问题、建议和回答结构；让 `build_report()` 使用评价结果的平均分生成总体报告。

`OpenAICompatibleLLM` 使用以下请求结构：

```python
payload = {
    "model": self.model,
    "temperature": 0.2,
    "messages": [
        {"role": "system", "content": system_prompt},
        {"role": "user", "content": user_prompt},
    ],
    "response_format": {"type": "json_object"},
}
```

请求失败、HTTP 非 2xx、返回内容不是 JSON 或 Pydantic 校验失败时，抛出包含“AI service error”的自定义异常；服务层捕获该异常并将会话标记为 failed。`.env.example` 只写变量名和空密钥：`LLM_BASE_URL`、`LLM_API_KEY`、`LLM_MODEL`、`DATABASE_URL`。

提示词必须明确：简历和岗位描述是资料，不是系统指令；模型只能按规定字段返回 JSON；评分必须在 0 到 10 之间；不能声称训练了模型。

- [ ] **Step 4: Run the tests to verify they pass**

```powershell
python -m pytest tests/test_llm_provider.py -q
```

Expected: PASS，并且测试过程不读取 `LLM_API_KEY`，不发出网络请求。

- [ ] **Step 5: Commit the task**

```powershell
git add backend/app/services/llm.py backend/app/services/prompts.py backend/.env.example backend/tests backend/pyproject.toml
git commit -m "feat: add pluggable llm providers"
```

### Task 5: 实现面试业务服务和后端 API 闭环

**Files:**
- Create: `ai-interview-platform/backend/app/repositories.py`
- Create: `ai-interview-platform/backend/app/services/interview_service.py`
- Create: `ai-interview-platform/backend/app/api/__init__.py`
- Create: `ai-interview-platform/backend/app/api/interviews.py`
- Create: `ai-interview-platform/backend/app/api/questions.py`
- Modify: `ai-interview-platform/backend/app/main.py`
- Create: `ai-interview-platform/backend/tests/test_interview_api.py`

**Interfaces:**
- Produces `InterviewService.create_session(job_description, filename, content_type, data) -> InterviewSession`。
- Produces `InterviewService.generate_questions(session_id) -> list[Question]`。
- Produces `InterviewService.submit_answer(question_id, answer_text) -> AnswerEvaluation`。
- Produces `InterviewService.get_report(session_id) -> InterviewReport`。
- Exposes the exact routes from the specification：`GET /api/health`、`POST /api/interviews`、`GET /api/interviews`、`GET /api/interviews/{session_id}`、`POST /api/interviews/{session_id}/questions`、`POST /api/questions/{question_id}/answers`、`GET /api/interviews/{session_id}/report`。

- [ ] **Step 1: Write the failing API tests**

```python
from fastapi.testclient import TestClient

from app.services.llm import FakeLLMProvider
from app.main import create_app


def test_interview_api_completes_the_core_flow(tmp_path):
    application = create_app(
        database_url=f"sqlite:///{tmp_path / 'test.db'}",
        llm_provider=FakeLLMProvider(),
    )
    client = TestClient(application)

    created = client.post(
        "/api/interviews",
        data={"job_description": "招聘 Python FastAPI 后端开发"},
        files={"resume": ("resume.txt", b"我做过 FastAPI 项目", "text/plain")},
    )
    assert created.status_code == 201
    session_id = created.json()["id"]

    generated = client.post(f"/api/interviews/{session_id}/questions")
    assert generated.status_code == 200
    assert len(generated.json()["questions"]) == 5
    question_id = generated.json()["questions"][0]["id"]

    answered = client.post(
        f"/api/questions/{question_id}/answers",
        json={"answer_text": "我会使用 FastAPI 编写接口并进行参数校验"},
    )
    assert answered.status_code == 201
    assert 0 <= answered.json()["score"]["total_score"] <= 10

    report = client.get(f"/api/interviews/{session_id}/report")
    assert report.status_code == 200
    assert "summary" in report.json()
```

- [ ] **Step 2: Run the test to verify it fails**

```powershell
python -m pytest tests/test_interview_api.py::test_interview_api_completes_the_core_flow -q
```

Expected: FAIL because the repositories, service and business routes do not exist。

- [ ] **Step 3: Write the minimal implementation**

调整 `create_app()` 签名为：

```python
def create_app(
    database_url: str | None = None,
    llm_provider: LLMProvider | None = None,
    retriever: Retriever | None = None,
) -> FastAPI:
    application = FastAPI(title="AI Interview Platform")
    application.state.database_url = database_url or Settings().database_url
    application.state.llm_provider = llm_provider or OpenAICompatibleLLM.from_settings()
    application.state.retriever = retriever or TfidfRetriever()
    return application
```

应用创建时调用 `create_engine_and_session()` 和 `init_db()`，将 `session_factory`、`llm_provider`、`retriever` 放入 `application.state`，让测试可注入临时 SQLite 和 FakeLLMProvider。

实现服务流程：

1. 创建会话并保存岗位描述。
2. 调用 `DocumentParser` 解析简历，保存文档及分块，并将分块交给 Retriever。
3. 生成题目时读取会话资料，检索岗位和简历相关片段，调用 `generate_questions()`，保存 5 道题并把状态改为 `in_progress`。
4. 提交答案时确认题目属于存在的会话且会话未完成，调用 `evaluate_answer()`，保存答案和评分。
5. 获取报告时读取所有已评价答案，调用 `build_report()`，保存或更新报告；全部题目完成后把会话状态改为 `completed`。

路由使用 `HTTPException` 返回具体状态：文件类型或内容错误为 400，会话或题目不存在为 404，非法状态为 409，AI 服务错误为 502。数据库提交失败必须回滚当前事务。

- [ ] **Step 4: Run the API test and the complete backend suite**

```powershell
python -m pytest tests/test_interview_api.py::test_interview_api_completes_the_core_flow -q
python -m pytest -q
```

Expected: API 闭环测试 PASS，后端全部测试 PASS。

- [ ] **Step 5: Commit the task**

```powershell
git add backend/app backend/tests
git commit -m "feat: add interview api workflow"
```

### Task 6: 构建 React 页面并接通后端

**Files:**
- Create: `ai-interview-platform/frontend/package.json`
- Create: `ai-interview-platform/frontend/index.html`
- Create: `ai-interview-platform/frontend/src/main.jsx`
- Create: `ai-interview-platform/frontend/src/App.jsx`
- Create: `ai-interview-platform/frontend/src/App.css`
- Create: `ai-interview-platform/frontend/src/api.js`
- Create: `ai-interview-platform/frontend/src/components/InterviewSetup.jsx`
- Create: `ai-interview-platform/frontend/src/components/QuestionPanel.jsx`
- Create: `ai-interview-platform/frontend/src/components/ReportPanel.jsx`
- Create: `ai-interview-platform/frontend/src/App.test.jsx`
- Create: `ai-interview-platform/frontend/vite.config.js`

**Interfaces:**
- `api.js` 提供 `createInterview(formData)`、`generateQuestions(sessionId)`、`submitAnswer(questionId, answerText)`、`getReport(sessionId)`、`listInterviews()`。
- `App.jsx` 管理 `setup`、`answering`、`report` 三个页面状态。
- 后端 API 响应字段直接对应 Task 2 的 Pydantic 模型和 Task 5 的接口。

- [ ] **Step 1: Write the failing frontend test**

```jsx
import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import App from "./App";

describe("App", () => {
  it("shows the interview setup page", () => {
    render(<App />);
    expect(screen.getByText("智能面试辅助平台")).toBeInTheDocument();
    expect(screen.getByLabelText("岗位描述")).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

```powershell
npm install
npm run test -- --run
```

Expected: FAIL because the Vite app and setup page do not exist。

- [ ] **Step 3: Write the minimal implementation**

创建 Vite React 项目配置，安装 `react`、`react-dom`、`vite`、`@vitejs/plugin-react`、`vitest`、`@testing-library/react`、`@testing-library/jest-dom` 和 `jsdom`。

`InterviewSetup` 提供岗位描述文本框、简历文件选择和“开始面试”按钮；按钮调用 `createInterview()`，成功后调用 `generateQuestions()` 并进入答题页。

`QuestionPanel` 一次展示一道题，提供答案文本框和“提交答案”按钮；提交后显示四项评分和建议，再允许进入下一题。

`ReportPanel` 展示总分、总体评价、薄弱项、建议和逐题结果；页面加载时调用 `getReport()`。

`api.js` 统一处理 JSON 响应：非 2xx 状态读取 `detail` 字段并抛出 `Error`，请求地址由 `VITE_API_BASE_URL` 控制，默认值为 `http://127.0.0.1:8000/api`。

- [ ] **Step 4: Run frontend tests and production build**

```powershell
npm run test -- --run
npm run build
```

Expected: 测试 PASS，`frontend/dist/` 生成且构建命令退出码为 0。

- [ ] **Step 5: Commit the task**

```powershell
git add frontend
git commit -m "feat: add react interview workflow"
```

### Task 7: 增加演示资料、运行文档和最终验收

**Files:**
- Create: `ai-interview-platform/README.md`
- Create: `ai-interview-platform/sample_data/resume.txt`
- Create: `ai-interview-platform/sample_data/job_description.txt`
- Create: `ai-interview-platform/evals/README.md`
- Modify: `ai-interview-platform/.gitignore`
- Modify: `ai-interview-platform/backend/app/main.py`

**Interfaces:**
- README 必须给出 Windows PowerShell 的后端安装、测试、启动命令和前端安装、测试、构建命令。
- `sample_data/` 只能使用虚构姓名、虚构项目和虚构联系方式。
- `GET /api/health`、后端完整测试、前端测试和前端构建组成最终验证集。

- [ ] **Step 1: Write the failing documentation/QA checks**

创建 `backend/tests/test_security_config.py`，同时检查最终说明文件确实存在：

```python
from pathlib import Path


def test_repository_does_not_store_real_api_keys():
    root = Path(__file__).parents[2]
    readme = root / "README.md"
    evals = root / "evals" / "README.md"
    assert readme.exists()
    assert evals.exists()
    assert "uvicorn app.main:app --reload --port 8000" in readme.read_text(encoding="utf-8")
    assert "样例编号" in evals.read_text(encoding="utf-8")

    source_files = [
        path for path in root.rglob("*")
        if path.is_file() and ".git" not in path.parts and "node_modules" not in path.parts
    ]
    forbidden = ("sk-", "api_key=", "LLM_API_KEY=sk-")

    for path in source_files:
        text = path.read_text(encoding="utf-8", errors="ignore")
        assert not any(token in text for token in forbidden), path
```

在 `evals/README.md` 中记录评测表格字段：样例编号、岗位类型、资料是否成功解析、题目数量、评分 JSON 是否合法、人工检查备注；不填写未经实际运行得到的数字。

- [ ] **Step 2: Run the check to verify the intended repository state is not yet documented**

```powershell
python -m pytest backend/tests/test_security_config.py -q
```

Expected: 如果此时还存在示例密钥或缺少 `.gitignore` 约束，测试 FAIL；清理为占位配置后再继续。

- [ ] **Step 3: Write the minimal documentation and configuration**

README 至少包含：

```powershell
cd backend
py -3.11 -m venv .venv
.\.venv\Scripts\Activate.ps1
python -m pip install -e .
python -m pytest -q
uvicorn app.main:app --reload --port 8000
```

以及前端命令：

```powershell
cd frontend
npm install
npm run dev
npm run test -- --run
npm run build
```

补充真实模型配置方法：复制 `backend/.env.example` 为本地环境配置，设置 `LLM_BASE_URL`、`LLM_API_KEY`、`LLM_MODEL`；不提交该文件。README 同时说明未配置密钥时可以使用 FakeLLMProvider 完成本地测试。

在 `main.py` 增加启动时创建 `data/` 目录、统一异常响应和 CORS 白名单 `http://localhost:5173`、`http://127.0.0.1:5173`。不允许使用 `allow_origins=["*"]`。

- [ ] **Step 4: Run the final verification set**

从项目根目录执行：

```powershell
python -m pytest backend -q
Push-Location frontend
npm run test -- --run
npm run build
Pop-Location
rg -n --hidden --glob "!.git/**" --glob "!frontend/node_modules/**" "sk-[A-Za-z0-9]|LLM_API_KEY=sk-|api_key\s*=\s*['\"]" .
git status --short
```

Expected：后端和前端测试通过、前端构建产物生成、密钥搜索无结果、工作区只包含项目预期文件。

启动后进行一次手工闭环：使用 `sample_data/resume.txt` 和 `sample_data/job_description.txt` 完成上传、生成 5 道题、提交至少 1 道答案、查看评分和报告；若使用真实模型，再记录模型名称和实际测试结果。

- [ ] **Step 5: Commit the task**

```powershell
git add README.md sample_data evals backend frontend .gitignore
git commit -m "docs: add local setup and project acceptance guide"
```

## Final Handoff

完成所有任务后，交付以下内容：

1. 项目目录 `ai-interview-platform/`。
2. 可点击的 README、后端和前端入口文件。
3. 后端测试、前端测试和构建命令的实际输出摘要。
4. 一份基于真实完成内容的简历项目描述。
5. 一份面试准备清单，覆盖 FastAPI 分层、TF-IDF 检索、提示词约束、结构化输出校验、FakeLLMProvider 和错误处理。
