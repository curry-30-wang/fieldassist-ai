# FieldAssist 简历素材

下面的数字是占位符，必须在实际演示、压测或部署后替换为实测结果，再放入简历；不要把示例数字当成项目成果。

## 中文项目描述

**FieldAssist 企业知识助手与工单工作台**｜Python、FastAPI、Vue 3、SQLAlchemy、SQLite、Dify、Langfuse、Docker Compose

- 设计并实现企业内部知识问答闭环，统一接入 Mock 与 Dify Chat API，覆盖登录、角色权限、会话、来源展示、反馈和工单流程；在实测 `[待替换：问题数量]` 个问题中保持 `[待替换：成功率]` 的业务成功率。
- 建立 Langfuse 可观测性适配层和本地 `ai_runs` 调用记录，记录提供方、延迟、模型和错误状态；实测成功调用平均延迟为 `[待替换：平均延迟 ms]`，外部追踪降级不阻断回答。
- 实现管理员指标、集成健康检查和固定评测集，使用“全部关键词命中”规则计算质量得分；评测集实测得分为 `[待替换：评测得分]`，发现失败题后可定位到具体回答和错误。
- 采用同源静态前端和 Docker Compose 交付，Mock 模式无需第三方密钥即可复现；自动化测试实测通过 `[待替换：测试数量]` 项，部署演示耗时 `[待替换：部署分钟数]` 分钟。

## English project description

**FieldAssist — Enterprise Knowledge Assistant and Ticket Workbench** | Python, FastAPI, Vue 3, SQLAlchemy, SQLite, Dify, Langfuse, Docker Compose

- Built an end-to-end internal knowledge workflow with role-based sessions, conversation history, citations, feedback, and ticket creation, using a deterministic Mock provider and a Dify Chat API adapter; replace `[待替换：实测问题数量]` with measured usage before publishing.
- Added a Langfuse observability adapter plus local AI-run records for provider, latency, model, and safe error status; measured successful-call latency was `[待替换：实测平均延迟 ms]`, with tracing degradation isolated from the user request.
- Delivered admin metrics, integration health checks, and a deterministic evaluation set whose score requires all expected keywords; measured evaluation score was `[待替换：实测评测得分]` across `[待替换：实测评测题数]` cases.
- Packaged a same-origin Vue browser build with Docker Compose and an offline Mock path; replace `[待替换：实测测试数量]` with the verified pytest count and keep all claims evidence-backed.

## 面试时可以展开

重点讲三个工程取舍：第一版不打包 Dify/Langfuse，降低本地交付复杂度；把外部系统放进可替换适配器，测试用 Mock/fake transport；业务数据先本地提交，再尽力上报 Langfuse，保证外部服务波动不会破坏员工工作流。
