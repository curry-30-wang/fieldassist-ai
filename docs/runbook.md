# FieldAssist 运行手册

## 标准启动

本地 Mock：

```powershell
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements.txt
.\.venv\Scripts\python.exe backend\seed.py
.\.venv\Scripts\python.exe backend\run.py
```

容器：

```powershell
docker compose config
docker compose up --build
```

浏览器访问 `http://127.0.0.1:8000/`，存活检查访问 `http://127.0.0.1:8000/api/health`。

## 配置检查

- 不填 Dify 和 Langfuse 配置时，确认 `AI_PROVIDER=mock`、`OBSERVABILITY_PROVIDER=mock`，应用应该可以离线登录和问答。
- 真实 Dify 模式需要 `DIFY_BASE_URL`、`DIFY_API_KEY`，并设置 `AI_PROVIDER=dify`；API Key 必须是后端环境变量，不要复制到浏览器或截图。
- 真实 Langfuse 模式需要 `LANGFUSE_PUBLIC_KEY`、`LANGFUSE_SECRET_KEY`、可选的 `LANGFUSE_BASE_URL`，并设置 `OBSERVABILITY_PROVIDER=langfuse`。
- 用管理员登录后查看 `/api/admin/health/integrations`，只根据 `configured`、`reachable` 和 `error_code` 判断状态；不要在日志或前端展示密钥、完整 URL 或原始响应。

## 常见问题

### 页面打不开或接口 404

确认 `backend\run.py` 正在运行、端口是 `8000`，然后分别访问 `/api/health` 和 `/`。如果只改了容器或环境变量，重启当前服务；不要把前端改成跨域地址。

### 登录失败

确认已经执行 `backend\seed.py`，账号是 `employee@fieldassist.local` 或 `admin@fieldassist.local`，密码与 `DEMO_ADMIN_PASSWORD` 当前配置一致。401 表示没有有效会话或凭据错误；页面会清除失效状态并提示重新登录。

### Dify 超时、认证失败或不可用

先检查 `DIFY_BASE_URL` 是否包含正确的协议和主机、API Key 是否属于目标应用，再看管理员健康状态。连接超时、401/403 和 5xx 会被转换为中文安全消息；本地 `ai_runs` 会保存失败状态。Mock 模式可用于先验证业务闭环。

### Langfuse 没有追踪

确认公钥、密钥、地址和 `OBSERVABILITY_PROVIDER` 配置。即使 Langfuse 暂时不可用，聊天成功也不应失败；检查服务日志中的降级记录和本地 AI 调用数据。不要为了追踪失败而重试用户问答。

### 数据库问题

检查 `database/` 是否可写，执行 `backend\seed.py` 初始化表和演示数据，并访问管理员健康接口。SQLite 数据文件只适合本地或单机演示；并发生产环境应规划 PostgreSQL 和迁移方案。

### Docker 问题

先运行 `docker compose config` 检查 YAML，再运行 `docker compose build`。Compose 容器监听 `0.0.0.0`，本地直接运行仍默认监听 `127.0.0.1`。若本机未安装 Docker，记录命令和原始环境错误，不要声称构建成功。

## 安全清单

`.env`、Dify API Key、Langfuse Secret Key、密码、数据库文件、`.venv`、缓存和日志均不得提交。对外分享截图或日志前，确认没有 Cookie、Authorization、手机号、邮箱或业务全文。
