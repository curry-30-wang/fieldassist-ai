const { createApp } = Vue;

const labels = {
  open: "待处理",
  in_progress: "处理中",
  resolved: "已解决",
  closed: "已关闭",
  low: "低",
  medium: "中",
  high: "高",
  urgent: "紧急",
};

createApp({
  data() {
    return {
      user: null,
      booting: true,
      loginLoading: false,
      loginError: "",
      sessionExpired: false,
      loginForm: { email: "employee@fieldassist.local", password: "" },
      activeSection: "chat",
      pageError: "",
      actionMessage: "",
      conversations: [],
      activeConversation: null,
      newConversationTitle: "",
      question: "",
      chatLoading: false,
      conversationLoading: false,
      feedbackBusy: {},
      feedbackComment: "",
      ticketDraft: { title: "", description: "", priority: "medium" },
      ticketMessage: null,
      ticketLoading: false,
      tickets: [],
      ticketsLoading: false,
      adminSummary: null,
      adminHealth: null,
      documents: [],
      evaluation: null,
      adminLoading: false,
      evaluationLoading: false,
      adminTicketUpdating: {},
    };
  },
  computed: {
    isAdmin() {
      return Boolean(this.user && this.user.role === "admin");
    },
    currentMessages() {
      return this.activeConversation ? this.activeConversation.messages || [] : [];
    },
    pageTitle() {
      if (this.isAdmin) {
        return { overview: "运营概览", documents: "知识文档", tickets: "工单管理", evaluations: "质量评测" }[this.activeSection] || "管理员工作台";
      }
      return this.activeSection === "tickets" ? "我的工单" : "知识问答";
    },
  },
  async mounted() {
    await this.bootstrap();
  },
  methods: {
    async api(path, options = {}) {
      const requestOptions = {
        credentials: "same-origin",
        ...options,
        headers: { "Content-Type": "application/json", ...(options.headers || {}) },
      };
      const response = await fetch(path, requestOptions);
      let payload = null;
      try {
        payload = await response.json();
      } catch (_error) {
        payload = null;
      }
      if (response.status === 401 && path !== "/api/auth/login") {
        this.user = null;
        this.sessionExpired = true;
        this.activeConversation = null;
      }
      if (!response.ok || !payload || payload.success === false) {
        throw new Error((payload && payload.message) || "请求失败，请稍后重试");
      }
      return payload;
    },
    clearMessages() {
      this.pageError = "";
      this.actionMessage = "";
    },
    showError(error, fallback = "操作失败，请稍后重试") {
      this.pageError = error instanceof Error && error.message ? error.message : fallback;
      this.actionMessage = "";
    },
    showSuccess(message) {
      this.pageError = "";
      this.actionMessage = message;
    },
    async bootstrap() {
      try {
        await this.api("/api/health");
      } catch (_error) {
        // 存活检查失败时仍尝试读取会话，让页面显示真实的登录状态。
      }
      try {
        const payload = await this.api("/api/auth/me");
        this.user = payload.data.user;
        await this.loadWorkspace();
      } catch (_error) {
        this.user = null;
      } finally {
        this.booting = false;
      }
    },
    async login() {
      this.loginLoading = true;
      this.loginError = "";
      this.sessionExpired = false;
      try {
        const payload = await this.api("/api/auth/login", { method: "POST", body: JSON.stringify(this.loginForm) });
        this.user = payload.data.user;
        this.activeSection = this.isAdmin ? "overview" : "chat";
        await this.loadWorkspace();
      } catch (error) {
        this.loginError = error instanceof Error ? error.message : "登录失败，请稍后重试";
      } finally {
        this.loginLoading = false;
      }
    },
    async logout() {
      try {
        await this.api("/api/auth/logout", { method: "POST", body: "{}" });
      } catch (_error) {
        // 无论网络是否短暂失败，都清除浏览器中的本地登录状态。
      }
      this.user = null;
      this.activeConversation = null;
      this.conversations = [];
      this.tickets = [];
      this.sessionExpired = false;
      this.clearMessages();
    },
    async loadWorkspace() {
      this.clearMessages();
      if (this.isAdmin) {
        await this.loadAdminWorkspace();
      } else {
        await Promise.all([this.loadConversations(), this.loadTickets()]);
      }
    },
    async loadConversations() {
      try {
        const payload = await this.api("/api/conversations");
        this.conversations = payload.data.conversations || [];
        if (!this.activeConversation && this.conversations.length) {
          await this.openConversation(this.conversations[0]);
        }
      } catch (error) {
        this.showError(error, "会话列表加载失败");
      }
    },
    async createConversation() {
      const title = this.newConversationTitle.trim();
      if (!title) {
        this.pageError = "请输入会话标题";
        return;
      }
      try {
        const payload = await this.api("/api/conversations", { method: "POST", body: JSON.stringify({ title }) });
        this.newConversationTitle = "";
        this.conversations.unshift(payload.data.conversation);
        await this.openConversation(payload.data.conversation);
        this.showSuccess("会话创建成功");
      } catch (error) {
        this.showError(error, "会话创建失败");
      }
    },
    async openConversation(conversation) {
      this.conversationLoading = true;
      try {
        const payload = await this.api(`/api/conversations/${conversation.id}`);
        this.activeConversation = payload.data.conversation;
      } catch (error) {
        this.showError(error, "会话内容加载失败");
      } finally {
        this.conversationLoading = false;
      }
    },
    async sendQuestion() {
      if (!this.activeConversation || !this.question.trim() || this.chatLoading) {
        return;
      }
      const question = this.question;
      this.chatLoading = true;
      this.clearMessages();
      try {
        await this.api(`/api/conversations/${this.activeConversation.id}/messages`, { method: "POST", body: JSON.stringify({ question }) });
        this.question = "";
        await this.openConversation(this.activeConversation);
        await this.loadConversations();
      } catch (error) {
        this.showError(error, "回答获取失败，问题已保留，可稍后重试");
      } finally {
        this.chatLoading = false;
      }
    },
    async submitFeedback(message, rating) {
      this.feedbackBusy[message.id] = true;
      try {
        await this.api(`/api/messages/${message.id}/feedback`, { method: "POST", body: JSON.stringify({ rating, comment: this.feedbackComment.trim() || null }) });
        this.feedbackComment = "";
        this.showSuccess(rating ? "感谢你的肯定" : "感谢你的反馈，我们会继续改进");
      } catch (error) {
        this.showError(error, "反馈提交失败");
      } finally {
        this.feedbackBusy[message.id] = false;
      }
    },
    openTicket(message) {
      this.ticketMessage = message;
      this.ticketDraft = { title: `关于：${message.content.slice(0, 30)}`, description: "", priority: "medium" };
      this.clearMessages();
    },
    closeTicketForm() {
      this.ticketMessage = null;
      this.ticketLoading = false;
    },
    async createTicket() {
      if (!this.ticketMessage) {
        return;
      }
      this.ticketLoading = true;
      try {
        await this.api(`/api/messages/${this.ticketMessage.id}/ticket`, { method: "POST", body: JSON.stringify(this.ticketDraft) });
        this.closeTicketForm();
        await this.loadTickets();
        this.showSuccess("工单创建成功");
      } catch (error) {
        this.showError(error, "工单创建失败");
      } finally {
        this.ticketLoading = false;
      }
    },
    async loadTickets() {
      this.ticketsLoading = true;
      try {
        const payload = await this.api("/api/tickets");
        this.tickets = payload.data.tickets || [];
      } catch (error) {
        this.showError(error, "工单加载失败");
      } finally {
        this.ticketsLoading = false;
      }
    },
    async loadAdminWorkspace() {
      this.adminLoading = true;
      await Promise.all([this.loadAdminSummary(), this.loadAdminHealth(), this.loadDocuments(), this.loadTickets()]);
      this.adminLoading = false;
    },
    async loadAdminSummary() {
      try { this.adminSummary = (await this.api("/api/admin/summary")).data; } catch (error) { this.showError(error, "运营指标加载失败"); }
    },
    async loadAdminHealth() {
      try { this.adminHealth = (await this.api("/api/admin/health/integrations")).data; } catch (error) { this.showError(error, "服务状态加载失败"); }
    },
    async loadDocuments() {
      try { this.documents = (await this.api("/api/admin/documents")).data.documents || []; } catch (error) { this.showError(error, "知识文档加载失败"); }
    },
    async updateTicket(ticket, status) {
      if (status === ticket.status || this.adminTicketUpdating[ticket.id]) return;
      this.adminTicketUpdating[ticket.id] = true;
      try {
        const payload = await this.api(`/api/tickets/${ticket.id}`, { method: "PATCH", body: JSON.stringify({ status }) });
        const index = this.tickets.findIndex((item) => item.id === ticket.id);
        if (index >= 0) this.tickets.splice(index, 1, payload.data.ticket);
        this.showSuccess("工单状态已更新");
      } catch (error) {
        this.showError(error, "工单状态更新失败");
      } finally {
        this.adminTicketUpdating[ticket.id] = false;
      }
    },
    async runEvaluation() {
      if (this.evaluationLoading) return;
      this.evaluationLoading = true;
      try {
        const started = await this.api("/api/admin/evaluations/run", { method: "POST", body: "{}" });
        this.evaluation = (await this.api(`/api/admin/evaluations/${started.data.run_id}`)).data;
        this.showSuccess("评测运行完成");
      } catch (error) {
        this.showError(error, "评测运行失败");
      } finally {
        this.evaluationLoading = false;
      }
    },
    async refreshCurrentSection() {
      if (this.isAdmin) await this.loadAdminWorkspace();
      else if (this.activeSection === "tickets") await this.loadTickets();
      else await this.loadConversations();
    },
    setSection(section) {
      this.activeSection = section;
      this.clearMessages();
      if (section === "tickets" && !this.isAdmin) this.loadTickets();
    },
    label(value) { return labels[value] || value || "未设置"; },
    formatDate(value) {
      if (!value) return "未记录时间";
      const date = new Date(value);
      return Number.isNaN(date.getTime()) ? value : date.toLocaleString("zh-CN");
    },
    healthClass(component) { return component && component.reachable ? "healthy" : "unhealthy"; },
    healthText(component) { return component ? (component.reachable ? "正常" : component.error_code || "不可用") : "未检查"; },
    statusOptions(ticket) {
      return { open: ["open", "in_progress"], in_progress: ["in_progress", "resolved"], resolved: ["resolved", "closed"], closed: ["closed"] }[ticket.status] || [ticket.status];
    },
  },
  template: `
    <div v-if="booting" class="page-center"><div class="loading-card">加载中，请稍候...</div></div>
    <div v-else-if="!user" class="login-page"><section class="login-card"><div class="brand-mark">F</div><p class="eyebrow">FIELDASSIST · 企业内部知识助手</p><h1>让每个问题都有可靠的下一步</h1><p class="muted">连接企业知识、AI 问答和工单处理，帮助团队更快找到依据。</p><form @submit.prevent="login" class="login-form"><label>邮箱<input v-model="loginForm.email" type="email" autocomplete="username" required></label><label>密码<input v-model="loginForm.password" type="password" autocomplete="current-password" required></label><p v-if="sessionExpired" class="notice warning">会话已失效，请重新登录。</p><p v-if="loginError" class="notice error">{{ loginError }}</p><button class="primary-button full-button" :disabled="loginLoading">{{ loginLoading ? "登录中..." : "登录系统" }}</button></form><div class="demo-hint"><strong>演示账号</strong><span>员工：employee@fieldassist.local</span><span>管理员：admin@fieldassist.local</span><small>密码请以本机 DEMO_ADMIN_PASSWORD 配置为准</small></div></section></div>
    <div v-else class="app-layout"><aside class="sidebar"><div class="sidebar-brand"><span class="brand-mark small">F</span><span>FieldAssist</span></div><div class="user-chip"><span class="avatar">{{ (user.display_name || "U").slice(0, 1) }}</span><span><strong>{{ user.display_name }}</strong><small>{{ isAdmin ? "系统管理员" : "内部员工" }}</small></span></div><nav class="main-nav" aria-label="主导航"><button v-if="!isAdmin" :class="{ active: activeSection === 'chat' }" @click="setSection('chat')"><span>⌂</span>知识问答</button><button v-if="!isAdmin" :class="{ active: activeSection === 'tickets' }" @click="setSection('tickets')"><span>□</span>我的工单</button><template v-if="isAdmin"><p class="nav-heading">运营管理</p><button :class="{ active: activeSection === 'overview' }" @click="setSection('overview')"><span>◈</span>运营概览</button><button :class="{ active: activeSection === 'documents' }" @click="setSection('documents')"><span>▤</span>知识文档</button><button :class="{ active: activeSection === 'tickets' }" @click="setSection('tickets')"><span>□</span>工单管理</button><button :class="{ active: activeSection === 'evaluations' }" @click="setSection('evaluations')"><span>✓</span>质量评测</button></template></nav><button class="logout-button" @click="logout">退出登录</button></aside><main class="main-content"><header class="topbar"><div><p class="eyebrow">{{ isAdmin ? "管理员工作台" : "员工工作台" }}</p><h2>{{ pageTitle }}</h2></div><button class="refresh-button" @click="refreshCurrentSection" title="刷新当前内容">↻ 刷新</button></header><div v-if="pageError" class="notice error page-notice">{{ pageError }}</div><div v-if="actionMessage" class="notice success page-notice">{{ actionMessage }}</div>
      <section v-if="!isAdmin && activeSection === 'chat'" class="workspace-grid"><aside class="conversation-panel panel-card"><div class="panel-heading"><div><p class="eyebrow">CONVERSATIONS</p><h3>我的会话</h3></div><span class="count-badge">{{ conversations.length }}</span></div><form class="inline-form" @submit.prevent="createConversation"><input v-model="newConversationTitle" placeholder="新会话标题" aria-label="新会话标题"><button class="icon-button" type="submit" title="创建会话">＋</button></form><div v-if="!conversations.length" class="empty-state">暂无会话<br><small>创建一个会话开始提问</small></div><div v-else class="conversation-list"><button v-for="conversation in conversations" :key="conversation.id" :class="['conversation-item', { selected: activeConversation && activeConversation.id === conversation.id }]" @click="openConversation(conversation)"><strong>{{ conversation.title }}</strong><small>{{ formatDate(conversation.created_at) }}</small></button></div></aside><section class="chat-panel panel-card"><div v-if="!activeConversation" class="empty-state large-empty"><span class="empty-icon">✦</span><h3>选择或创建一个会话</h3><p>你可以咨询请假、报销、账号访问等内部政策。</p></div><template v-else><div class="chat-heading"><div><p class="eyebrow">ASSISTANT CHAT</p><h3>{{ activeConversation.title }}</h3></div><span class="live-dot">在线</span></div><div class="message-list" aria-live="polite"><div v-if="conversationLoading" class="empty-state">加载中...</div><div v-else-if="!currentMessages.length" class="empty-state">暂无消息<br><small>在下方输入你的第一个问题</small></div><article v-for="message in currentMessages" :key="message.id" :class="['message-row', message.role]"><div class="message-avatar">{{ message.role === 'user' ? '我' : 'F' }}</div><div class="message-bubble"><div class="message-meta">{{ message.role === 'user' ? '你' : 'FieldAssist' }} · {{ formatDate(message.created_at) }}</div><p>{{ message.content }}</p><div v-if="message.role === 'assistant'" class="message-extra"><div v-if="message.sources && message.sources.length" class="sources"><strong>参考来源</strong><span v-for="source in message.sources" :key="source.document_id || source.title">{{ source.title || '知识文档' }}</span></div><div class="message-actions"><button :disabled="feedbackBusy[message.id]" @click="submitFeedback(message, true)">👍 有帮助</button><button :disabled="feedbackBusy[message.id]" @click="submitFeedback(message, false)">👎 需改进</button><button @click="openTicket(message)">转为工单</button></div></div></div></article></div><div class="chat-composer"><input v-model="feedbackComment" class="feedback-input" placeholder="可选：写下反馈后再点击上方评价" aria-label="反馈说明"><form @submit.prevent="sendQuestion"><textarea v-model="question" rows="3" maxlength="2000" placeholder="请输入你的问题，例如：年假需要提前多久申请？" aria-label="问题" :disabled="chatLoading"></textarea><div class="composer-footer"><small>{{ question.length }}/2000 · 回答基于已启用知识文档</small><button class="primary-button" :disabled="chatLoading || !question.trim()">{{ chatLoading ? '回答生成中...' : '发送问题 ↗' }}</button></div></form></div></template></section></section>
      <section v-if="!isAdmin && activeSection === 'tickets'" class="single-panel panel-card"><div class="panel-heading"><div><p class="eyebrow">MY TICKETS</p><h3>我的工单</h3></div><span class="count-badge">{{ tickets.length }}</span></div><div v-if="ticketsLoading" class="empty-state">加载中...</div><div v-else-if="!tickets.length" class="empty-state large-empty"><span class="empty-icon">□</span><h3>暂无工单</h3><p>在问答中点击“转为工单”，即可提交需要跟进的问题。</p></div><div v-else class="ticket-list"><article v-for="ticket in tickets" :key="ticket.id" class="ticket-card"><div class="ticket-top"><span :class="['priority', 'priority-' + ticket.priority]">{{ label(ticket.priority) }}优先级</span><span :class="['status-badge', 'status-' + ticket.status]">{{ label(ticket.status) }}</span></div><h4>{{ ticket.title }}</h4><p>{{ ticket.description }}</p><small>创建于 {{ formatDate(ticket.created_at) }}</small></article></div></section>
      <section v-if="isAdmin && activeSection === 'overview'" class="admin-page"><div v-if="adminLoading && !adminSummary" class="empty-state">加载中...</div><template v-else><div class="metric-grid"><article class="metric-card"><span>累计问题</span><strong>{{ adminSummary ? adminSummary.total_questions : 0 }}</strong><small>员工提交的问题数量</small></article><article class="metric-card accent"><span>平均响应</span><strong>{{ adminSummary ? adminSummary.average_latency_ms : 0 }}<em>ms</em></strong><small>成功 AI 调用平均延迟</small></article><article class="metric-card"><span>正向反馈率</span><strong>{{ adminSummary ? Math.round(adminSummary.positive_feedback_rate * 100) : 0 }}<em>%</em></strong><small>基于全部反馈计算</small></article><article class="metric-card warm"><span>开放工单</span><strong>{{ adminSummary ? adminSummary.open_ticket_count : 0 }}</strong><small>待处理或进行中</small></article></div><div class="admin-two-column"><section class="panel-card"><div class="panel-heading"><div><p class="eyebrow">INTEGRATIONS</p><h3>服务连接状态</h3></div></div><div class="health-list"><div v-for="(component, name) in adminHealth || {}" :key="name" class="health-row"><span class="health-name">{{ { application: '应用服务', ai_provider: 'AI 服务', observability_provider: '可观测性', database: '本地数据库' }[name] || name }}</span><span :class="['health-badge', healthClass(component)]"><i></i>{{ healthText(component) }}</span></div></div></section><section class="panel-card provider-card"><div class="panel-heading"><div><p class="eyebrow">PROVIDERS</p><h3>调用提供方</h3></div></div><div v-if="adminSummary && Object.keys(adminSummary.provider_breakdown).length" class="provider-list"><div v-for="(count, provider) in adminSummary.provider_breakdown" :key="provider"><span>{{ provider }}</span><strong>{{ count }} 次</strong></div></div><div v-else class="empty-state compact">暂无调用记录</div></section></div><section class="panel-card admin-ticket-preview"><div class="panel-heading"><div><p class="eyebrow">OPERATIONS</p><h3>近期工单</h3></div><button class="text-button" @click="setSection('tickets')">查看全部 →</button></div><div v-if="!tickets.length" class="empty-state compact">暂无工单</div><div v-else class="mini-ticket-list"><div v-for="ticket in tickets.slice(0, 5)" :key="ticket.id" class="mini-ticket"><span :class="['status-dot', 'status-dot-' + ticket.status]"></span><strong>{{ ticket.title }}</strong><span>{{ label(ticket.status) }}</span></div></div></section></template></section>
      <section v-if="isAdmin && activeSection === 'documents'" class="single-panel panel-card"><div class="panel-heading"><div><p class="eyebrow">KNOWLEDGE BASE</p><h3>已启用知识文档</h3></div><span class="count-badge">{{ documents.length }}</span></div><div v-if="!documents.length" class="empty-state large-empty">暂无数据</div><div v-else class="table-wrap"><table><thead><tr><th>文档标题</th><th>分类</th><th>来源</th><th>状态</th><th>更新时间</th></tr></thead><tbody><tr v-for="document in documents" :key="document.id"><td><strong>{{ document.title }}</strong></td><td>{{ document.category }}</td><td>{{ document.source_url || '内部知识库' }}</td><td><span class="health-badge healthy"><i></i>已启用</span></td><td>{{ formatDate(document.created_at) }}</td></tr></tbody></table></div></section>
      <section v-if="isAdmin && activeSection === 'tickets'" class="single-panel panel-card"><div class="panel-heading"><div><p class="eyebrow">ALL TICKETS</p><h3>工单管理</h3></div><span class="count-badge">{{ tickets.length }}</span></div><div v-if="!tickets.length" class="empty-state large-empty">暂无工单</div><div v-else class="table-wrap"><table><thead><tr><th>标题</th><th>优先级</th><th>当前状态</th><th>创建时间</th><th>操作</th></tr></thead><tbody><tr v-for="ticket in tickets" :key="ticket.id"><td><strong>{{ ticket.title }}</strong><small class="table-subtitle">{{ ticket.description }}</small></td><td><span :class="['priority', 'priority-' + ticket.priority]">{{ label(ticket.priority) }}</span></td><td><span :class="['status-badge', 'status-' + ticket.status]">{{ label(ticket.status) }}</span></td><td>{{ formatDate(ticket.created_at) }}</td><td><select :value="ticket.status" :disabled="adminTicketUpdating[ticket.id]" @change="updateTicket(ticket, $event.target.value)"><option v-for="status in statusOptions(ticket)" :key="status" :value="status">{{ label(status) }}</option></select></td></tr></tbody></table></div></section>
      <section v-if="isAdmin && activeSection === 'evaluations'" class="admin-two-column evaluation-page"><section class="panel-card"><div class="panel-heading"><div><p class="eyebrow">QUALITY CHECK</p><h3>固定问题集评测</h3></div><button class="primary-button" :disabled="evaluationLoading" @click="runEvaluation">{{ evaluationLoading ? '评测中...' : '运行评测' }}</button></div><p class="muted">使用当前配置的 AI 提供方逐题检查知识库回答，所有关键词都命中才算通过。</p><div v-if="!evaluation" class="empty-state">暂无评测记录<br><small>点击右上角开始一次评测</small></div><div v-else class="evaluation-summary"><div class="score-ring"><strong>{{ Math.round(evaluation.run.score * 100) }}<em>%</em></strong><span>总体得分</span></div><div class="evaluation-stats"><span>提供方<strong>{{ evaluation.run.provider }}</strong></span><span>通过题数<strong>{{ evaluation.run.passed_cases }} / {{ evaluation.run.total_cases }}</strong></span><span>完成时间<strong>{{ formatDate(evaluation.run.completed_at) }}</strong></span></div></div></section><section class="panel-card"><div class="panel-heading"><div><p class="eyebrow">RESULTS</p><h3>逐题结果</h3></div></div><div v-if="!evaluation || !evaluation.results.length" class="empty-state compact">暂无结果</div><div v-else class="evaluation-results"><article v-for="result in evaluation.results" :key="result.id" :class="['evaluation-row', result.matched ? 'passed' : 'failed']"><span class="result-icon">{{ result.matched ? '✓' : '!' }}</span><div><strong>题目 #{{ result.case_id }}</strong><p v-if="result.error_message" class="error-text">{{ result.error_message }}</p><p v-else>{{ result.answer }}</p></div><span>{{ result.matched ? '通过' : '未通过' }}</span></article></div></section></section>
    </main></div>
    <div v-if="ticketMessage" class="modal-backdrop" @click.self="closeTicketForm"><section class="modal-card"><div class="panel-heading"><div><p class="eyebrow">CREATE TICKET</p><h3>把回答转为工单</h3></div><button class="close-button" @click="closeTicketForm">×</button></div><p class="quote">“{{ ticketMessage.content }}”</p><form class="ticket-form" @submit.prevent="createTicket"><label>工单标题<input v-model="ticketDraft.title" maxlength="255" required></label><label>问题描述<textarea v-model="ticketDraft.description" maxlength="2000" rows="4" placeholder="请补充需要跟进的内容" required></textarea></label><label>优先级<select v-model="ticketDraft.priority"><option value="low">低</option><option value="medium">中</option><option value="high">高</option><option value="urgent">紧急</option></select></label><div class="modal-actions"><button type="button" class="secondary-button" @click="closeTicketForm">取消</button><button class="primary-button" :disabled="ticketLoading">{{ ticketLoading ? '提交中...' : '提交工单' }}</button></div></form></section></div>
  `,
}).mount("#app");
