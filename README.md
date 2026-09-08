# FieldAssist

FieldAssist 是一个面向企业内部的知识助手和工单工作台。员工可以登录、向知识库提问、查看引用、评价回答并创建工单；管理员可以查看调用指标、集成健康状态、知识文档、工单和固定评测结果。

第一版重点是可交付的业务应用层：FieldAssist 自己负责用户、会话、权限、反馈、工单和本地指标；Dify 负责真实 AI 对话；Langfuse 负责可观测性；没有外部凭据时，Mock 模式提供确定性、离线、可测试的回答。

## 3 分钟启动 Mock 模式

需要 Python 3.10+。在项目根目录执行：

```powershell
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements.txt
.\.venv\Scripts\python.exe backend\seed.py
.\.venv\Scripts\python.exe backend\run.py
```

打开 <http://127.0.0.1:8000/>。Mock 模式不需要 Dify、Langfuse、n8n 或模型密钥；默认端口是 `8000`，SQLite 文件会放在 `database/fieldassist.db`。

演示账号：

- 员工：`employee@fieldassist.local`
- 管理员：`admin@fieldassist.local`
- 默认演示密码：`admin123`，也可以用 `DEMO_ADMIN_PASSWORD` 覆盖

若本机不能访问 Vue CDN，可将 Vue 3 浏览器构建文件放入 `frontend/vendor/`，再把 `frontend/index.html` 中的 CDN 地址替换为本地文件；项目本身不需要 Node.js、Vite 或打包步骤。

## 真实 Dify / Langfuse 模式

复制 `.env.example` 为本地 `.env`，只在本机填写真实值：

```dotenv
APP_ENV=development
SECRET_KEY=replace-with-a-strong-local-secret
DATABASE_URL=sqlite:///./database/fieldassist.db
AI_PROVIDER=dify
OBSERVABILITY_PROVIDER=langfuse
DIFY_BASE_URL=https://your-dify-host
DIFY_API_KEY=your-dify-app-api-key
DIFY_APP_ID=your-dify-app-id
LANGFUSE_PUBLIC_KEY=your-langfuse-public-key
LANGFUSE_SECRET_KEY=your-langfuse-secret-key
LANGFUSE_BASE_URL=https://cloud.langfuse.com
REQUEST_TIMEOUT_SECONDS=15
DEMO_ADMIN_PASSWORD=change-me
```

Dify API Key 和 Langfuse Secret Key 只由后端读取，前端不会返回或保存它们。Langfuse 上报失败不会让成功的 AI 回答失败；Dify 超时、认证失败和不可用会转换为中文提示并记录本地失败调用。第一版不依赖 n8n，工单先保存到 SQLite。

## 常用命令

```powershell
.\.venv\Scripts\python.exe -m pytest tests -q
docker compose config
docker compose up --build
```

停止 Compose 使用 `Ctrl+C`。容器通过 `./database:/app/database` 保留 SQLite 数据，并以非 root 用户运行。不要提交 `.env`、API Key、Langfuse Secret Key、数据库文件、`.venv`、缓存或日志。

## 项目结构

```text
backend/app/       FastAPI、SQLAlchemy、认证、业务服务和外部适配器
frontend/          Vue 3 浏览器构建版静态页面
database/          SQLite 数据目录
tests/             pytest 自动化测试
docs/              架构、接口、演示、运行手册和简历素材
```

进一步阅读：

- [架构说明](docs/architecture.md)
- [接口说明](docs/api.md)
- [3–5 分钟演示脚本](docs/demo-script.md)
- [运行手册](docs/runbook.md)
- [简历素材](docs/resume.md)
- [n8n 后续扩展](docs/n8n-extension.md)
