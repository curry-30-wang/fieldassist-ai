# n8n 后续扩展边界

第一版不依赖 n8n，也不需要安装或启动 n8n。工单创建后先保存到 FieldAssist 自己的 SQLite，员工和管理员页面可以在没有外部自动化平台时完整演示。

## 建议的 webhook 契约

未来可以由后端的 `AutomationClient` 实现一个 n8n webhook 客户端，把外部动作交给 n8n：

```http
POST https://n8n.example/webhook/fieldassist-ticket
Content-Type: application/json
X-FieldAssist-Event: ticket.created
```

请求体只发送完成外部动作所需的业务字段，不发送密码、Dify API Key、Langfuse Secret Key、Session Cookie 或完整敏感对话：

```json
{
  "event": "ticket.created",
  "ticket_id": 42,
  "title": "账号无法登录",
  "description": "员工已完成自助重置仍无法登录",
  "priority": "high",
  "status": "open",
  "created_by": "employee@fieldassist.local"
}
```

n8n 可以据此创建外部工单、查询订单或发送通知。建议回调返回外部系统编号和可重试状态；超时、非 2xx 和重复事件应由后端记录为可观察的自动化失败，但不能回滚本地已保存工单。生产接入前还需要签名校验、幂等键、重试退避和敏感字段审查。

## 与当前代码的关系

`AutomationClient` / `NoopAutomationClient` 是当前的扩展点。第一版默认 Noop 实现，不发起网络请求；以后增加 n8n 客户端时，员工工单 API 和前端无需重写，只需在服务配置中替换实现并补充 fake transport 测试。
