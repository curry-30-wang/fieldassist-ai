# FieldAssist 接口说明

所有 JSON 响应使用统一外壳：

```json
{
  "success": true,
  "data": {},
  "message": "操作成功"
}
```

错误响应的 `success` 为 `false`，`data` 为 `null`，`message` 为中文安全提示。需要登录的接口通过同源 Session Cookie 识别用户。

## 认证

| 方法 | 路径 | 权限 | 请求体 / 用途 |
|---|---|---|---|
| POST | `/api/auth/login` | 公开 | `{ "email": "...", "password": "..." }`；登录并建立会话 |
| POST | `/api/auth/logout` | 登录 | 无；清除会话 |
| GET | `/api/auth/me` | 登录 | 获取当前用户的 id、邮箱、显示名、角色和启用状态 |

## 员工会话与问答

| 方法 | 路径 | 权限 | 请求体 / 用途 |
|---|---|---|---|
| GET | `/api/conversations` | 登录 | 获取当前用户自己的会话列表 |
| POST | `/api/conversations` | 登录 | `{ "title": "报销咨询" }`；创建会话 |
| GET | `/api/conversations/{conversation_id}` | 会话所属用户 | 获取消息历史、来源和 AI 调用记录 |
| POST | `/api/conversations/{conversation_id}/messages` | 会话所属用户 | `{ "question": "年假需要提前多久申请？" }`；发送问题 |
| POST | `/api/messages/{message_id}/feedback` | 助手消息所属用户或管理员 | `{ "rating": true, "comment": "回答很清楚" }`；重复提交会更新同一条反馈 |
| POST | `/api/messages/{message_id}/ticket` | 助手消息所属用户或管理员 | `{ "title": "账号问题", "description": "...", "priority": "medium" }`；创建工单 |

问答成功时 `data` 包含 `user_message`、`assistant_message`、`sources`、`provider` 和 `ai_run`。失败时仍保存失败的 AI 调用，并返回例如“AI 服务响应超时，请稍后重试”。

## 工单

| 方法 | 路径 | 权限 | 请求体 / 用途 |
|---|---|---|---|
| GET | `/api/tickets` | 登录 | 员工只看自己创建的工单，管理员看全局工单 |
| PATCH | `/api/tickets/{ticket_id}` | 工单创建人或管理员 | 可更新 `title`、`description`、`priority`、`status`；状态按既定流转规则检查 |

工单状态为 `open`、`in_progress`、`resolved` 或 `closed`；优先级为 `low`、`medium`、`high` 或 `urgent`。

## 管理员

以下接口都要求 `role=admin`，员工访问返回 403：

| 方法 | 路径 | 用途 |
|---|---|---|
| GET | `/api/admin/summary` | 返回问题总数、成功调用平均延迟、正向反馈率、开放/处理中工单数、提供方调用次数 |
| GET | `/api/admin/health/integrations` | 返回应用、AI 提供方、可观测性和数据库的 `configured`、`reachable`、`error_code` 安全字段 |
| GET | `/api/admin/documents` | 返回启用文档的 id、标题、分类、来源、启用状态和创建时间，不返回全文 |
| POST | `/api/admin/evaluations/run` | 使用当前 ChatProvider 执行全部评测用例，返回 `{ "run_id": 1 }` |
| GET | `/api/admin/evaluations/{id}` | 返回评测摘要和按用例顺序排列的回答、通过状态、延迟及安全错误 |

## 公开健康检查

`GET /api/health` 不需要登录，返回当前应用、AI 提供方名称和可观测性提供方名称。它用于容器 healthcheck；详细的连接状态需要管理员访问 `/api/admin/health/integrations`。
