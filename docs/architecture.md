# FieldAssist 架构说明

可直接在 GitHub README 中查看渲染后的[架构图](../README.md#架构图)；Mermaid 源文件位于 [`architecture.mmd`](architecture.mmd)。

## 一句话

FieldAssist 是一个同源的 Vue 3 + FastAPI 企业知识助手。浏览器只访问自己的 `/api`，后端负责会话、权限、业务数据和外部服务适配，SQLite 保存本地业务记录。

## 请求流

```text
浏览器
  └─ Vue 3 静态页面
       └─ 同源 fetch /api
            └─ FastAPI 路由与中文错误封装
                 ├─ 登录会话与角色权限
                 ├─ 会话、消息、反馈、工单服务
                 ├─ ChatProvider：Mock 或 Dify
                 ├─ Tracer：Mock 或 Langfuse
                 └─ SQLAlchemy Session → SQLite
```

一次员工提问的主要步骤是：先校验登录身份和会话归属，再保存用户问题；根据配置选择 Mock 或 Dify；把答案、来源和 AI 调用延迟保存为助手消息与 `ai_runs`；最后尽力记录 Langfuse 追踪。外部服务失败时仍保留本地失败调用记录，并返回用户看得懂的中文提示。

## 组件边界

- `backend/app/api/` 只负责 HTTP 参数、权限依赖和统一响应结构。
- `backend/app/services/` 负责会话、聊天、反馈、工单、指标和评测等业务规则。
- `backend/app/integrations/contracts.py` 定义 `ChatProvider`、`Tracer` 和未来自动化边界；Mock、Dify、Langfuse 都通过这些接口接入。
- `backend/app/models.py` 使用 SQLAlchemy 2.x 映射用户、会话、消息、AI 调用、反馈、工单、文档和评测表。
- `frontend/` 使用 Vue 3 浏览器构建版，不引入 Node.js、Vite 或组件库。它只负责呈现状态，后端角色检查才是安全边界。

## 安全与降级

Dify API Key、Langfuse 密钥、会话密钥和连接地址只在后端配置中读取，前端响应不包含这些值。密码使用带随机盐的 PBKDF2-HMAC 保存；Cookie 只保存签名后的用户编号。每个管理员路由都重新检查 `admin` 角色，每个员工会话和工单都检查对象归属。

Mock 模式使用启用的演示文档做确定性关键词匹配，不发起外网请求。没有完整 Langfuse 凭据时使用 Mock tracer；Langfuse 的初始化、上报、评分或 flush 失败只记服务日志，不阻塞本地业务。

## 数据与扩展

SQLite 适合本地演示和单机交付，Compose 用 volume 保留 `database/`。第一版不打包 Dify、Langfuse，也不要求 n8n；真实环境可以把它们部署在外部，通过后端适配器接入。未来的 n8n webhook 契约记录在 [n8n-extension.md](n8n-extension.md)，不会改变员工当前的工单页面。
