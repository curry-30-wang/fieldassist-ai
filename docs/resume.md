# FieldAssist 简历素材

以下内容按本地 Mock 演示和自动化测试结果整理，适合放入简历项目经历。面试时应按本人真实参与范围进行调整，并能够说明关键设计取舍。

## 中文项目描述

**FieldAssist 企业知识助手与工单工作台**｜Python、FastAPI、Vue 3、SQLAlchemy、SQLite、Dify、Langfuse、pytest、Docker Compose

- 完成企业内部知识问答闭环，接入 Mock 与 Dify Chat API，覆盖登录、角色权限、会话、来源展示、反馈和工单流程。
- 建立 Langfuse 可观测性适配层和本地 `ai_runs` 调用记录，记录提供方、延迟、模型和错误状态；外部追踪失败时，仍保留本地业务结果并向用户返回可理解的提示。
- 实现管理员指标、集成健康检查和固定问题集评测；本地 Mock 验证中固定评测集 5/5 通过，能够逐题查看回答和通过状态。
- 采用同源 Vue 3 静态前端和 Docker Compose 配置，Mock 模式无需第三方密钥即可复现；`pytest` 自动化测试 111 项通过。

## English project description

**FieldAssist — Enterprise Knowledge Assistant and Ticket Workbench** | Python, FastAPI, Vue 3, SQLAlchemy, SQLite, Dify, Langfuse, pytest, Docker Compose

- Built an end-to-end internal knowledge workflow with role-based sessions, conversation history, citations, feedback, and ticket creation, using a deterministic Mock provider and a Dify Chat API adapter.
- Added a Langfuse observability adapter plus local AI-run records for provider, latency, model, and safe error status; tracing degradation is isolated from the user request.
- Delivered admin metrics, integration health checks, and a deterministic five-question evaluation set; the local Mock verification passed all five questions.
- Packaged a same-origin Vue browser build with Docker Compose and an offline Mock path; 111 pytest tests passed in the local verification run.

## 面试时可以展开

重点讲三个工程取舍：第一版不打包 Dify/Langfuse，降低本地交付复杂度；把外部系统放进可替换适配器，测试用 Mock/fake transport；业务数据先本地提交，再尽力上报 Langfuse，保证外部服务波动不会破坏员工工作流。

## FDE 表达重点

- 从员工“查制度、找依据、提交问题”的实际流程出发，设计问答、引用、反馈和工单闭环。
- 用 Mock 模式降低首次演示和联调门槛，再通过 Dify 适配器切换到真实 AI 服务。
- 用管理员指标、健康检查和固定评测集支持交付后的问题定位和持续迭代。

## 实习经历写法

**驻马店市公路工程开发有限公司｜办公室文职**

- 负责日常办公文档、表格制作和信息整理，熟练使用 Word、Excel、PowerPoint 等办公软件。
- 针对公司表格制作和信息统计需求，完成一套办公数据管理与统计工具，推动重复性信息整理工作集中化处理。
- 在工作中与上级领导及同事保持良好沟通，能够快速理解业务需求并将需求转化为可使用的工具。

**南宁盛邦商务有限公司｜销售业务员**

- 负责公司产品销售、客户沟通和合作推进，持续为公司创造销售业绩。
- 与同事协作推进并达成两项合作，积累了客户需求理解、商务沟通和团队协作经验。
- 工作期间获得上级和同事认可，具备较强的沟通表达、关系维护和执行能力。

## 第二项目简历写法

**办公室数据与统计工具**｜办公数字化项目

- 根据办公室日常表格制作和信息统计需求，完成一套面向实际工作场景的办公辅助软件。
- 将重复的表格整理和信息汇总流程集中到工具中，体现需求梳理、工具设计和落地交付能力。
- 项目安装包以 GitHub Release 资产形式提供，具体文件信息见 [`portfolio/README.md`](../portfolio/README.md)。
