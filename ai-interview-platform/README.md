# AI 智能面试辅助平台

这是一个可在 Windows 本地运行的求职面试练习项目。用户上传简历并填写目标岗位描述后，系统会解析资料、检索相关片段、生成面试题、评价答案并汇总报告。自动化测试使用 `FakeLLMProvider`，不需要网络或真实模型密钥。

## 环境要求

- Windows PowerShell
- Python 3.11 或更高版本
- Node.js 18 或更高版本及 npm

以下命令均从项目根目录 `ai-interview-platform` 开始执行。

## 后端安装、测试与启动

首次安装：

```powershell
cd backend
py -3.11 -m venv .venv
.\.venv\Scripts\python.exe -m pip install -e .
cd ..
```

运行后端完整测试：

```powershell
backend\.venv\Scripts\python.exe -m pytest backend -q
```

启动后端：

```powershell
cd backend
.\.venv\Scripts\python.exe -m uvicorn app.main:app --reload --port 8000
# 等价命令（已激活虚拟环境时）：uvicorn app.main:app --reload --port 8000
```

浏览器访问 `http://127.0.0.1:8000/api/health`，应得到服务状态 JSON。

## 真实兼容模型配置

后端支持 OpenAI 接口格式的兼容模型。先复制配置示例，真实密钥只保存在被 Git 忽略的 `backend/.env`：

```powershell
Copy-Item backend\.env.example backend\.env
```

在 `backend/.env` 中填写模型服务实际提供的 `LLM_BASE_URL`、`LLM_API_KEY`、`LLM_MODEL`。示例文件只保留空值和说明，不包含任何可用密钥。

当前后端从进程环境读取配置。启动前，可将 `.env` 中的非注释配置载入当前 PowerShell，再启动服务：

```powershell
Get-Content backend\.env | Where-Object { $_ -and -not $_.StartsWith('#') } | ForEach-Object {
    $name, $value = $_ -split '=', 2
    Set-Item -Path "Env:$name" -Value $value
}
```

未配置密钥时不要假装完成真实模型验收。后端测试和可编程本地闭环会注入 `FakeLLMProvider`，它返回固定且符合数据结构的内容，用于验证上传、出题、评分和报告流程。

## 前端安装、开发、测试与构建

```powershell
cd frontend
npm install
npm run dev
npm run test -- --run
npm run build
cd ..
```

开发页面默认为 `http://localhost:5173`。后端 CORS 白名单仅允许该地址和 `http://127.0.0.1:5173`。

## 本地演示

1. 按前述命令启动后端和前端。
2. 打开前端页面，上传 `sample_data/resume.txt`。
3. 将 `sample_data/job_description.txt` 的内容粘贴到岗位描述。
4. 创建面试并生成题目，提交至少一道答案后查看评分与报告。
5. 真实模型不可用时，以后端测试中的 `FakeLLMProvider` 闭环为准，并明确标记为本地模拟结果。

两份样例只包含虚构姓名、虚构公司、虚构项目和 `.test` 域名联系方式，不应替换为真实个人资料后提交到仓库。

## 架构

```text
React/Vite 页面
    ↓ HTTP JSON / 文件上传
FastAPI 路由
    ↓
InterviewService 业务流程
    ├─ DocumentParser：PDF、DOCX、TXT 文本解析
    ├─ TfidfRetriever：分块与相关片段检索
    ├─ LLMProvider：真实兼容模型或 FakeLLMProvider
    └─ Repository / SQLAlchemy / SQLite：会话、题目、答案和报告
```

路由层负责参数和响应，业务层管理面试状态及调用顺序，模型适配层负责结构化 AI 输出，数据层负责持久化。模型结果在写入数据库前经过数据模型校验，外部服务、业务冲突和数据库故障返回不同的错误状态。

## 主要接口

| 方法 | 路径 | 用途 |
| --- | --- | --- |
| GET | `/api/health` | 健康检查 |
| POST | `/api/interviews` | 上传简历并创建面试 |
| GET | `/api/interviews` | 获取历史面试列表 |
| GET | `/api/interviews/{session_id}` | 获取面试详情和题目 |
| POST | `/api/interviews/{session_id}/questions` | 生成五道面试题 |
| POST | `/api/questions/{question_id}/answers` | 提交答案并获取结构化评分 |
| GET | `/api/interviews/{session_id}/report` | 汇总并获取面试报告 |

## 简历项目亮点

可按真实完成内容描述为：

> 基于 FastAPI、React 和 OpenAI 兼容模型接口实现智能面试辅助平台，完成简历与岗位资料解析、TF-IDF 检索增强出题、结构化答案评分和面试报告生成；通过 LLMProvider 抽象与 FakeLLMProvider 分离真实模型调用和离线自动化测试，并使用 SQLAlchemy 持久化面试流程数据。

不要填写未经实际评测的准确率、性能提升比例或“自主训练大模型”等无法验证的内容。实际评测结果可按 `evals/README.md` 模板补充。

## 面试准备清单

- 能说明 FastAPI 路由、业务服务、数据访问和模型适配为什么分层。
- 能解释 TF-IDF 如何把查询与文档片段转成可比较的权重，以及轻量方案的适用边界。
- 能说明提示词如何固定任务、输入资料和 JSON 输出约束，并防止简历中的文字覆盖系统规则。
- 能说明模型返回内容为何必须经过数据模型校验，非法 JSON、字段缺失或分数越界如何处理。
- 能解释 `FakeLLMProvider` 如何让测试不依赖网络和真实密钥，以及它不能证明真实模型效果。
- 能区分输入错误、资源不存在、状态冲突、模型服务失败和数据库失败对应的处理方式。
- 能现场演示健康检查、上传资料、生成题目、提交答案和查看报告的完整流程。
