# FieldAssist 设计说明

## 1. 项目目标

FieldAssist 是一个面向企业内部的知识库与工单辅助助手。员工可以登录系统，向企业知识库提问、查看历史会话、评价回答，并把问题转成工单；管理员可以查看服务连接状态、知识文档、调用指标、用户反馈和评测结果。

项目的重点不是复制 Dify 或 Langfuse，而是构建一个可交付的业务应用层：

- 用 Dify 负责真实 AI 对话和知识库问答；
- 用 Langfuse 记录 AI 调用、延迟、质量评分和评测数据；
- 用 FieldAssist 自己负责用户、会话、反馈、工单、权限和业务指标；
- 没有外部服务凭据时，用 Mock 实现完成本地演示和自动化测试。

## 2. 目标用户和角色

### 普通员工

- 登录并查看自己的会话；
- 发送知识库问题；
- 查看回答和可用的引用信息；
- 对回答点赞或点踩并填写反馈；
- 从一条回答创建工单；
- 查看自己创建的工单及处理状态。

### 管理员

- 查看系统概览和 AI 调用指标；
- 查看 Dify、Langfuse 和本地 Mock 模式的连接状态；
- 查看演示知识文档；
- 查看所有反馈和工单；
- 更新工单状态；
- 运行固定评测集并查看结果；
- 导出评测摘要或复制交付报告中的关键指标。

## 3. 范围边界

### 第一版包含

- 同源的 Vue 3 前端和 FastAPI 后端；
- 基于会话的登录认证和员工/管理员权限；
- SQLite 数据库及演示数据；
- Dify Chat API 适配器；
- Langfuse Python SDK 适配器；
- Mock AI 和 Mock 追踪模式；
- 会话、消息、反馈、工单、知识文档元数据和评测记录；
- 健康检查、超时、错误转换和本地调用指标；
- Docker Compose 启动文件；
- pytest 自动化测试；
- README、架构说明、接口说明、演示脚本、运行手册和简历描述。

### 第一版不包含

- 自己实现向量数据库或模型训练；
- 真实企业微信、飞书或短信登录；
- 真实多租户隔离；
- 支付、复杂审批和实时协作；
- 完整打包 Dify 和 Langfuse 的大型部署栈；
- n8n 的强依赖集成。

n8n 作为后续扩展点记录在文档中。以后可以让 n8n 负责查询订单、创建外部工单和发送通知，但第一版的工单先保存在 FieldAssist 自己的数据库里，以控制项目复杂度。

## 4. 技术架构

~~~text
Browser
  └─ Vue 3 static page
       │ same-origin JSON requests
       ▼
FastAPI application
  ├─ Auth and role checks
  ├─ Conversation and ticket services
  ├─ AI orchestration service
  │    ├─ Dify adapter      -> Dify Chat API
  │    └─ Mock adapter       -> local deterministic answers
  ├─ Observability service
  │    ├─ Langfuse adapter   -> Langfuse Python SDK
  │    └─ Mock trace         -> local AI run record
  └─ SQLAlchemy session      -> SQLite file
~~~

### 4.1 前端

前端使用 Vue 3 的浏览器构建版本，以静态 HTML、CSS 和 JavaScript 文件提供页面，不引入 Node.js、Vite 或打包步骤。页面通过同源 /api 路径访问后端，避免额外的跨域配置。

页面包含：

- 登录页；
- 员工工作台：会话列表、对话区、反馈和创建工单；
- 管理员工作台：指标卡片、连接状态、反馈、工单、知识文档和评测；
- 通用加载、空数据、失败和登录失效提示。

Vue CDN 是第一版的运行前提。README 会同时说明网络不可用时可以把 Vue 浏览器文件放入 frontend/vendor/，后续实现不依赖开发服务器。

### 4.2 后端

FastAPI 负责 HTTP 路由、参数校验、认证会话和异常响应。业务规则放在服务层，不直接散落在路由函数中。

建议目录：

~~~text
fieldassist/
├─ backend/
│  ├─ app/
│  │  ├─ api/
│  │  ├─ integrations/
│  │  ├─ models/
│  │  ├─ schemas/
│  │  ├─ services/
│  │  ├─ config.py
│  │  ├─ database.py
│  │  ├─ security.py
│  │  └─ main.py
│  ├─ seed.py
│  └─ run.py
├─ frontend/
│  ├─ index.html
│  ├─ assets/
│  └─ app.js
├─ database/
├─ tests/
├─ docs/
├─ Dockerfile
├─ compose.yaml
├─ requirements.txt
├─ .env.example
└─ README.md
~~~

### 4.3 数据库

SQLAlchemy 2.x 负责模型映射和会话管理，SQLite 负责本地持久化。数据库文件位于 database/fieldassist.db，通过 Docker volume 保留。

核心表：

- users：用户、密码哈希、角色和启用状态；
- conversations：会话标题、所属用户和 Dify 会话编号；
- messages：用户问题、助手回答、角色、引用元数据和创建时间；
- ai_runs：提供方、状态、延迟、追踪编号、估算 token 和错误信息；
- feedback：回答评价和文字意见；
- tickets：工单标题、描述、优先级、状态和来源消息；
- knowledge_documents：Mock 模式使用的演示文档和真实知识库映射信息；
- evaluation_cases：评测问题、分类和期望关键词；
- evaluation_runs：一次评测的模式、总数、通过数和总体得分；
- evaluation_results：单题回答、关键词匹配、延迟和错误信息。

所有关联数据使用外键。用户只能读取自己的会话、消息、反馈和工单；管理员接口必须在后端再次校验角色，不能只依赖前端隐藏菜单。

## 5. 外部服务适配

### 5.1 Dify Chat API

后端通过 DifyClient 发送：

- 用户问题；
- FieldAssist 用户标识；
- Dify 会话编号；
- 应用输入参数；
- blocking 响应模式。

适配器只向上层返回统一结构：

~~~python
ChatResult(
    answer: str,
    conversation_id: str | None,
    sources: list[dict],
    provider: str,
    raw_metadata: dict,
)
~~~

API Key 只从后端环境变量读取，绝不发送到 Vue 页面。Dify 的不同版本可能返回不同的引用字段，因此适配器保留原始元数据，同时从常见字段中提取可展示的来源。

### 5.2 Langfuse Python SDK

后端通过 LangfuseTracer 包住一次完整的 AI 请求，记录：

- 用户问题的脱敏版本；
- Dify 或 Mock 提供方；
- 模型和应用标识；
- 请求开始和结束时间；
- 回答结果的脱敏版本；
- 错误信息；
- 用户反馈分数和评测分数。

Langfuse 未配置时，系统使用 MockTracer，只写入本地 ai_runs，不阻断聊天功能。Langfuse 上报失败不能让用户的回答失败，但需要写入服务日志和本地状态。

### 5.3 n8n 扩展点

第一版不依赖 n8n。TicketService 预留一个清晰的自动化边界，未来可以增加 N8nWebhookClient，将“创建外部工单、查询订单、发送通知”等动作交给 n8n；本地工单接口和页面不需要因此重写。

## 6. Mock 模式

当 DIFY_API_KEY 为空，应用默认使用 Mock AI；当 Langfuse 凭据不完整时，应用默认使用 Mock 追踪。也可以通过 AI_PROVIDER=mock|dify 和 OBSERVABILITY_PROVIDER=mock|langfuse 显式配置。

Mock AI 使用演示知识文档做确定性关键词匹配：

1. 按问题匹配标题、分类和正文关键词；
2. 命中时返回对应答案和文档来源；
3. 未命中时返回“当前知识库没有足够依据”的可解释提示；
4. 生成本地 AI 调用记录，包含提供方、延迟和成功状态。

Mock 输出必须稳定，这样评测和 pytest 不会依赖网络、模型随机性或第三方费用。

## 7. 主要接口

| 方法 | 路径 | 权限 | 用途 |
|---|---|---|---|
| POST | /api/auth/login | 公开 | 登录并建立会话 |
| POST | /api/auth/logout | 登录 | 注销 |
| GET | /api/auth/me | 登录 | 获取当前用户 |
| GET | /api/conversations | 登录 | 获取当前用户会话 |
| POST | /api/conversations | 登录 | 创建会话 |
| GET | /api/conversations/{id} | 所属用户/管理员 | 获取消息历史 |
| POST | /api/conversations/{id}/messages | 所属用户/管理员 | 发送问题并获得回答 |
| POST | /api/messages/{id}/feedback | 所属用户/管理员 | 保存回答评价 |
| POST | /api/messages/{id}/ticket | 所属用户/管理员 | 从回答创建工单 |
| GET | /api/tickets | 登录 | 获取自己的或全局工单 |
| PATCH | /api/tickets/{id} | 管理员/工单创建人 | 更新工单状态 |
| GET | /api/admin/summary | 管理员 | 获取运营指标 |
| GET | /api/admin/health/integrations | 管理员 | 检查外部服务 |
| GET | /api/admin/documents | 管理员 | 获取演示文档 |
| POST | /api/admin/evaluations/run | 管理员 | 执行评测集 |
| GET | /api/admin/evaluations/{id} | 管理员 | 获取评测结果 |
| GET | /api/health | 公开 | 应用存活检查 |

统一返回结构为：

~~~json
{
  "success": true,
  "message": "操作成功",
  "data": {}
}
~~~

错误响应使用用户能理解的中文消息，详细堆栈只写服务端日志，不返回给浏览器。

## 8. 错误处理和安全边界

- Dify 请求设置连接和读取超时；
- Dify 的网络错误、鉴权错误和服务错误转换成统一的集成错误；
- 外部服务失败时保存失败的 ai_runs，并向用户返回可重试提示；
- Langfuse 上报采用尽力而为策略，不影响回答返回；
- 密码使用带随机盐的 PBKDF2 哈希保存；
- 会话密钥和外部 API Key 从环境变量读取；
- 日志和 Langfuse 记录不保存密码、API Key 或完整敏感业务数据；
- 工单和会话接口执行对象级权限检查；
- 对问题长度、反馈内容、工单标题和描述设置长度限制；
- CORS 默认关闭，因为前端由同一 FastAPI 服务提供；
- Docker 容器以非 root 用户运行，数据库目录使用专用 volume。

## 9. 测试策略

pytest 覆盖以下内容：

- 应用工厂和数据库初始化；
- 演示账号登录、错误密码、注销和会话失效；
- 普通用户访问管理员接口时返回 403；
- 用户不能读取或修改其他用户的会话和工单；
- Mock 命中和未命中问题；
- 会话、消息、反馈和工单完整链路；
- Dify 超时、错误响应和无凭据降级；
- Langfuse 上报失败不影响回答；
- 管理指标计算；
- 评测集通过、失败和无命中情况；
- /api/health 返回正确状态。

外部网络调用全部通过适配器隔离，测试使用假客户端或 HTTP transport，不调用真实 Dify、Langfuse 和模型服务。

## 10. 本地和容器运行

### Mock 模式

~~~powershell
python -m venv .venv
.\\.venv\\Scripts\\python.exe -m pip install -r requirements.txt
.\\.venv\\Scripts\\python.exe backend\\seed.py
.\\.venv\\Scripts\\python.exe backend\\run.py
~~~

访问 http://127.0.0.1:8000/，不需要 Dify、Langfuse 或 n8n。

### Docker Compose

~~~powershell
docker compose up --build
~~~

Compose 第一版启动 FieldAssist 应用并挂载 SQLite 数据目录。真实模式通过 .env 注入 Dify 和 Langfuse 地址及凭据；凭据文件不得提交到 Git。

## 11. 验收标准

项目完成必须满足：

1. 新环境按 README 可以启动 Mock 模式；
2. 普通用户可以登录、提问、查看历史、评价回答和创建工单；
3. 管理员可以查看指标、连接状态、文档、反馈、工单和评测结果；
4. 没有外部凭据时主要业务流程仍可演示；
5. 配置 Dify 后可以真实调用 Chat API；
6. 配置 Langfuse 后可以看到 AI 调用追踪，Langfuse 失败不阻断业务；
7. pytest -q 全部通过；
8. Docker Compose 可以构建并启动应用；
9. 文档包含架构、接口、演示流程、故障处理和真实简历表述；
10. 不把 .env、密钥、虚拟环境、缓存和运行日志提交到仓库。

## 12. 后续扩展

- 增加 n8n Webhook，把工单动作接入真实业务系统；
- 增加 PostgreSQL 配置和数据库迁移；
- 增加企业微信或飞书通知；
- 增加真正的 Dify 知识库文档同步；
- 增加多租户和组织级权限；
- 增加更严格的人工评测和离线评测集。
