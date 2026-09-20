# FieldAssist Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (\`- [ ]\`) syntax for tracking.

**Goal:** Build a locally runnable FieldAssist enterprise knowledge assistant with Mock mode, Dify Chat API integration, Langfuse tracing, role-based workflows, evaluation metrics, and Docker delivery artifacts.

**Architecture:** A same-origin Vue 3 static page calls a FastAPI backend. The backend owns authentication, conversations, feedback, tickets, evaluations, and local metrics; provider interfaces isolate Dify, Langfuse, and deterministic Mock implementations. SQLite is the local persistence layer, while Docker Compose packages the FieldAssist service without bundling the large Dify or Langfuse platforms.

**Tech Stack:** Python 3.10+, FastAPI, SQLAlchemy 2.x, SQLite, Pydantic Settings, httpx, Langfuse Python SDK v4, Vue 3 browser build, Docker Compose, pytest.

**Spec:** docs/superpowers/specs/2026-09-07-fieldassist-design.md

## Global Constraints

- Mock mode must work without Dify, Langfuse, n8n, model credentials, or external network calls except loading the Vue browser build.
- Dify and Langfuse credentials must remain server-side and must never appear in frontend responses.
- The first version stores local business data in SQLite and does not require PostgreSQL.
- The first version does not depend on n8n; the ticket service exposes an extension boundary for a future webhook client.
- Backend role checks and object ownership checks are authoritative; hidden frontend menus are not security controls.
- External integration errors must be converted into user-readable Chinese messages and must not expose stack traces.
- Langfuse reporting is best effort and must not turn a successful AI answer into a failed user request.
- All external calls must be isolated behind testable adapters and replaced with fake transports in pytest.
- Do not add .env, database files, virtual environments, caches, logs, or generated build output to Git.
- Run verification from the repository root D:/文档/ChatGPT/2.

---

### Task 1: Create the runnable project skeleton

**Files:**
- Create: requirements.txt
- Create: .env.example
- Create: .gitignore
- Create: Dockerfile
- Create: compose.yaml
- Create: backend/__init__.py
- Create: backend/run.py
- Create: backend/app/__init__.py
- Create: backend/app/config.py
- Create: backend/app/database.py
- Create: backend/app/main.py
- Create: frontend/index.html
- Create: frontend/app.js
- Create: frontend/styles.css
- Create: database/.gitkeep
- Test: tests/test_app_boot.py
- Test: tests/conftest.py

**Interfaces:**
- config.get_settings() returns a cached Settings object containing app_name, environment, secret_key, database_url, ai_provider, observability_provider, dify_base_url, dify_api_key, dify_app_id, langfuse_public_key, langfuse_secret_key, langfuse_base_url, request_timeout_seconds, and demo_admin_password.
- database.get_db() yields a SQLAlchemy Session.
- main.create_app() returns the FastAPI application.
- GET /api/health returns success=true, status=ok, and the active AI and observability provider names.
- GET / returns the Vue page from frontend/index.html.

- [ ] **Step 1: Write the failing boot tests**

Create tests/test_app_boot.py with tests for create_app(), GET /api/health, and GET /. Use a temporary database URL from tests/conftest.py and assert that the response has the fields success, data, and message.

- [ ] **Step 2: Run the boot tests and verify the expected failure**

Run:

~~~powershell
python -m pytest tests/test_app_boot.py -q
~~~

Expected: FAIL because the application modules and routes do not exist yet.

- [ ] **Step 3: Add the dependency and configuration files**

requirements.txt must contain compatible ranges for fastapi, uvicorn[standard], sqlalchemy, pydantic-settings, httpx, itsdangerous, langfuse, pytest, and pytest-cov.

.env.example must contain safe placeholders:

~~~text
APP_ENV=development
SECRET_KEY=replace-with-a-local-secret
DATABASE_URL=sqlite:///./database/fieldassist.db
AI_PROVIDER=mock
OBSERVABILITY_PROVIDER=mock
DIFY_BASE_URL=http://localhost
DIFY_API_KEY=
DIFY_APP_ID=
LANGFUSE_PUBLIC_KEY=
LANGFUSE_SECRET_KEY=
LANGFUSE_BASE_URL=https://cloud.langfuse.com
REQUEST_TIMEOUT_SECONDS=15
DEMO_ADMIN_PASSWORD=replace-with-a-local-demo-password
~~~

- [ ] **Step 4: Implement the application factory and static serving**

Create Settings with Pydantic Settings, create the SQLAlchemy engine and session dependency, add GET /api/health, mount the frontend directory, and make backend/run.py call uvicorn with host 127.0.0.1 and port 8000.

- [ ] **Step 5: Add Docker delivery files**

The Dockerfile must install requirements, copy backend, frontend, database, and docs, create a non-root app user, expose port 8000, and run backend/run.py.

compose.yaml must define one fieldassist service, mount ./database to /app/database, load .env when present, expose 8000:8000, and include a healthcheck calling /api/health.

- [ ] **Step 6: Run the boot tests and verify they pass**

Run:

~~~powershell
python -m pytest tests/test_app_boot.py -q
~~~

Expected: PASS.

- [ ] **Step 7: Commit the skeleton**

~~~powershell
git add requirements.txt .env.example .gitignore Dockerfile compose.yaml backend frontend database tests
git commit -m "feat: add FieldAssist application skeleton"
~~~

---

### Task 2: Add database models and deterministic seed data

**Files:**
- Modify: backend/app/database.py
- Create: backend/app/models.py
- Create: backend/seed.py
- Create: database/README.md
- Test: tests/test_database_seed.py

**Interfaces:**
- database.init_db() creates all SQLAlchemy tables.
- seed.seed_database() creates the demo users, knowledge documents, and evaluation cases without duplicating records.
- models.User has id, email, display_name, password_hash, role, is_active, and created_at.
- models.Conversation has id, user_id, title, dify_conversation_id, and created_at.
- models.Message has id, conversation_id, role, content, provider, sources_json, created_at.
- models.AiRun has id, message_id, provider, status, trace_id, model, latency_ms, input_tokens, output_tokens, total_cost, error_code, and created_at.
- models.Feedback has id, message_id, user_id, rating, comment, and created_at.
- models.Ticket has id, source_message_id, created_by, title, description, priority, status, created_at, and updated_at.
- models.KnowledgeDocument has id, title, category, content, source_url, enabled, and created_at.
- models.EvaluationCase has id, question, category, expected_keywords_json, and created_at.
- models.EvaluationRun has id, provider, total_cases, passed_cases, score, started_at, and completed_at.
- models.EvaluationResult has id, run_id, case_id, answer, matched, latency_ms, and error_message.

- [ ] **Step 1: Write failing model and seed tests**

Test that init_db creates every required table, seed_database creates one admin and one employee, creates at least five knowledge documents and five evaluation cases, and is idempotent when called twice.

- [ ] **Step 2: Run the tests and verify the expected failure**

Run:

~~~powershell
python -m pytest tests/test_database_seed.py -q
~~~

Expected: FAIL because models and seed functions are missing.

- [ ] **Step 3: Implement the models and relationships**

Use SQLAlchemy 2 declarative mappings. Add foreign keys from conversations to users, messages to conversations, AI runs to messages, feedback to messages and users, tickets to messages and users, and evaluation results to evaluation runs and cases. Use JSON text columns for provider metadata to keep SQLite setup simple.

- [ ] **Step 4: Implement idempotent seed data**

Create a demo admin with email admin@fieldassist.local, role admin, and the configured demo password. Create a demo employee with email employee@fieldassist.local, role employee, and the same configured demo password. Add knowledge documents covering leave policy, expense reimbursement, IT account access, customer support escalation, and data security. Add evaluation questions that map deterministically to those documents.

- [ ] **Step 5: Run the model and seed tests**

Run:

~~~powershell
python -m pytest tests/test_database_seed.py -q
~~~

Expected: PASS.

- [ ] **Step 6: Commit the database layer**

~~~powershell
git add backend/app/database.py backend/app/models.py backend/seed.py database/README.md tests/test_database_seed.py
git commit -m "feat: add FieldAssist database models and seed data"
~~~

---

### Task 3: Implement session authentication and role permissions

**Files:**
- Create: backend/app/security.py
- Create: backend/app/dependencies.py
- Create: backend/app/api/auth.py
- Modify: backend/app/main.py
- Modify: backend/app/models.py
- Test: tests/test_auth_permissions.py

**Interfaces:**
- security.hash_password(password: str) returns a salted password hash string.
- security.verify_password(password: str, password_hash: str) returns bool.
- dependencies.get_current_user(request, db) returns User or raises HTTP 401.
- dependencies.require_admin(user) returns User or raises HTTP 403.
- POST /api/auth/login accepts email and password and creates a signed session.
- POST /api/auth/logout clears the session.
- GET /api/auth/me returns the current user profile without password data.

- [ ] **Step 1: Write failing authentication tests**

Cover successful admin login, successful employee login, wrong password returning 401, unauthenticated /me returning 401, logout clearing the session, employee access to an admin-only dependency returning 403, and password hashes not containing the clear password.

- [ ] **Step 2: Run the tests and verify the expected failure**

Run:

~~~powershell
python -m pytest tests/test_auth_permissions.py -q
~~~

Expected: FAIL because authentication routes and dependencies are missing.

- [ ] **Step 3: Implement password hashing and signed sessions**

Use Python standard-library PBKDF2-HMAC with a random salt. Add Starlette SessionMiddleware with SECRET_KEY. Store only user_id in the signed session cookie.

- [ ] **Step 4: Add auth routes and role dependencies**

Add Pydantic request and response schemas, authenticate against the database, reject inactive users, and return a consistent Chinese response envelope. Do not return password_hash or configuration secrets.

- [ ] **Step 5: Run the authentication tests**

Run:

~~~powershell
python -m pytest tests/test_auth_permissions.py -q
~~~

Expected: PASS.

- [ ] **Step 6: Commit the authentication layer**

~~~powershell
git add backend/app/security.py backend/app/dependencies.py backend/app/api/auth.py backend/app/main.py backend/app/models.py tests/test_auth_permissions.py
git commit -m "feat: add session authentication and role permissions"
~~~

---

### Task 4: Define AI and observability contracts with deterministic Mock implementations

**Files:**
- Create: backend/app/integrations/contracts.py
- Create: backend/app/integrations/mock_ai.py
- Create: backend/app/integrations/mock_trace.py
- Create: backend/app/integrations/factory.py
- Create: backend/app/services/ai_runs.py
- Test: tests/test_mock_integrations.py

**Interfaces:**
- integrations.contracts.ChatResult has answer, conversation_id, sources, provider, model, and raw_metadata.
- integrations.contracts.ChatProvider.chat(question: str, user_id: str, conversation_id: str | None) returns ChatResult.
- integrations.contracts.ChatProvider.health_check() returns HealthResult.
- integrations.contracts.TraceContext has trace_id and provider.
- integrations.contracts.Tracer.start_trace(name: str, user_id: str, input_text: str) returns a context manager.
- integrations.contracts.Tracer.record_generation(context, model, input_text, output_text, latency_ms, metadata) returns None.
- integrations.contracts.Tracer.record_score(trace_id, name, value, comment) returns None.
- services.ai_runs.record_ai_run(db, message_id, result) returns AiRun.
- services.ai_runs.record_ai_failure(db, message_id, provider, error_code, latency_ms) returns AiRun.

- [ ] **Step 1: Write failing Mock tests**

Test that a leave-policy question returns a stable answer and source, an unknown question returns a transparent no-evidence answer, a Mock trace produces a mock trace_id, and an AI run record is saved with provider mock and status success.

- [ ] **Step 2: Run the tests and verify the expected failure**

Run:

~~~powershell
python -m pytest tests/test_mock_integrations.py -q
~~~

Expected: FAIL because provider contracts and Mock implementations are missing.

- [ ] **Step 3: Implement provider contracts and the Mock AI**

Load enabled KnowledgeDocument rows, normalize Chinese text, score matches by title/category/content keyword hits, return the highest match, and use the no-evidence response when the best score is zero. Do not use random output.

- [ ] **Step 4: Implement the Mock tracer and provider factory**

The factory chooses Mock when AI_PROVIDER=mock or when no DIFY_API_KEY is configured. The observability factory chooses Mock when OBSERVABILITY_PROVIDER=mock or credentials are incomplete.

- [ ] **Step 5: Implement local AI run persistence**

Save successful and failed calls with latency and provider information. Keep source metadata in JSON and leave trace_id populated for Mock calls.

- [ ] **Step 6: Run the Mock tests**

Run:

~~~powershell
python -m pytest tests/test_mock_integrations.py -q
~~~

Expected: PASS.

- [ ] **Step 7: Commit the integration contracts**

~~~powershell
git add backend/app/integrations backend/app/services/ai_runs.py tests/test_mock_integrations.py
git commit -m "feat: add AI provider and observability contracts"
~~~

---

### Task 5: Implement the Dify Chat API adapter

**Files:**
- Create: backend/app/integrations/dify.py
- Modify: backend/app/integrations/factory.py
- Test: tests/test_dify_adapter.py

**Interfaces:**
- DifyClient.chat(question: str, user_id: str, conversation_id: str | None) returns ChatResult.
- DifyClient.health_check() returns HealthResult.
- DifyClient maps HTTP 401 and 403 to IntegrationAuthError, HTTP 408 and timeout to IntegrationTimeoutError, HTTP 5xx to IntegrationUnavailableError, and malformed JSON to IntegrationResponseError.

- [ ] **Step 1: Write failing adapter tests using httpx.MockTransport**

Test the blocking chat request body contains query, user, response_mode=blocking, and conversation_id when present. Test Bearer authentication, successful answer extraction, conversation_id extraction, common citation metadata extraction, and each error mapping.

- [ ] **Step 2: Run the adapter tests and verify the expected failure**

Run:

~~~powershell
python -m pytest tests/test_dify_adapter.py -q
~~~

Expected: FAIL because DifyClient is missing.

- [ ] **Step 3: Implement the adapter**

Use httpx.AsyncClient with the configured base URL and timeout. Send the Dify chat request through the server-side API key. Accept a response with answer and conversation_id, preserve the complete metadata, and extract sources from retriever_resources when present.

- [ ] **Step 4: Implement health checking**

Use a lightweight configured endpoint that does not expose the API key in the response. Return configured=false when required settings are absent and reachable=false with a safe error code when the request fails.

- [ ] **Step 5: Run the adapter tests**

Run:

~~~powershell
python -m pytest tests/test_dify_adapter.py -q
~~~

Expected: PASS.

- [ ] **Step 6: Commit the Dify adapter**

~~~powershell
git add backend/app/integrations/dify.py backend/app/integrations/factory.py tests/test_dify_adapter.py
git commit -m "feat: integrate Dify Chat API"
~~~

---

### Task 6: Implement Langfuse tracing with safe degradation

**Files:**
- Create: backend/app/integrations/langfuse.py
- Modify: backend/app/integrations/factory.py
- Test: tests/test_langfuse_adapter.py

**Interfaces:**
- LangfuseTracer.start_trace(name, user_id, input_text) returns a trace context.
- LangfuseTracer.record_generation(context, model, input_text, output_text, latency_ms, metadata) returns None.
- LangfuseTracer.record_score(trace_id, name, value, comment) returns None.
- LangfuseTracer.health_check() returns HealthResult.
- Any Langfuse SDK exception is logged and swallowed by the adapter after local AI run persistence has completed.

- [ ] **Step 1: Write failing tracer tests with a fake Langfuse client**

Test configured and unconfigured behavior, trace creation, generation recording, score recording, and SDK failure not escaping to the chat service.

- [ ] **Step 2: Run the tracer tests and verify the expected failure**

Run:

~~~powershell
python -m pytest tests/test_langfuse_adapter.py -q
~~~

Expected: FAIL because the adapter is missing.

- [ ] **Step 3: Implement the SDK adapter**

Use the current Langfuse Python SDK v4 API behind a small wrapper. Read public key, secret key, and base URL only from Settings. Record a trace for the FieldAssist request and a generation observation for the Dify or Mock result.

- [ ] **Step 4: Implement score recording**

Map thumbs-up to 1 and thumbs-down to 0. Store user feedback locally first, then attempt to send the score to Langfuse. Record comments only after removing control characters and truncating to the configured safe length.

- [ ] **Step 5: Add the safe fallback**

If SDK import, client construction, network delivery, or flush fails, return a Mock-like local context and log a short error code. Do not return the exception text or credentials to the browser.

- [ ] **Step 6: Run the tracer tests**

Run:

~~~powershell
python -m pytest tests/test_langfuse_adapter.py -q
~~~

Expected: PASS.

- [ ] **Step 7: Commit the Langfuse adapter**

~~~powershell
git add backend/app/integrations/langfuse.py backend/app/integrations/factory.py tests/test_langfuse_adapter.py
git commit -m "feat: add Langfuse tracing with fallback"
~~~

---

### Task 7: Implement conversations and the end-to-end chat flow

**Files:**
- Create: backend/app/schemas/chat.py
- Create: backend/app/services/chat.py
- Create: backend/app/api/chat.py
- Modify: backend/app/main.py
- Test: tests/test_chat_flow.py

**Interfaces:**
- services.chat.create_conversation(db, user, title) returns Conversation.
- services.chat.send_message(db, user, conversation_id, question) returns Message.
- POST /api/conversations creates a conversation.
- GET /api/conversations lists only the current user’s conversations, ordered by newest activity.
- GET /api/conversations/{id} returns the conversation and ordered messages after an ownership check.
- POST /api/conversations/{id}/messages accepts question and returns the saved user message, assistant message, sources, provider, and ai_run summary.

- [ ] **Step 1: Write failing end-to-end Mock tests**

Log in as employee, create a conversation, ask a known knowledge question, assert both user and assistant messages are saved, assert the answer contains the seeded policy, assert the AI run is successful, then ask an unknown question and assert the no-evidence response.

- [ ] **Step 2: Write failing ownership tests**

Assert that an employee cannot read another employee’s conversation and that an employee cannot post to another user’s conversation.

- [ ] **Step 3: Run the chat tests and verify the expected failure**

Run:

~~~powershell
python -m pytest tests/test_chat_flow.py -q
~~~

Expected: FAIL because chat schemas, services, and routes are missing.

- [ ] **Step 4: Implement the chat service**

Create a user message before the provider call. Start a trace, call the configured ChatProvider, measure latency, save the assistant message and AI run, record the generation, and return a stable response. On integration errors, save a failed AI run and return a safe retry message with an error code.

- [ ] **Step 5: Implement conversation routes and ownership checks**

Use the current session user for all queries. Allow administrators to inspect conversations only through an explicit admin route, not by weakening the employee ownership check.

- [ ] **Step 6: Run the chat tests**

Run:

~~~powershell
python -m pytest tests/test_chat_flow.py -q
~~~

Expected: PASS.

- [ ] **Step 7: Commit the chat flow**

~~~powershell
git add backend/app/schemas/chat.py backend/app/services/chat.py backend/app/api/chat.py backend/app/main.py tests/test_chat_flow.py
git commit -m "feat: add conversation and chat workflow"
~~~

---

### Task 8: Add feedback and ticket workflows

**Files:**
- Create: backend/app/schemas/tickets.py
- Create: backend/app/services/tickets.py
- Create: backend/app/api/tickets.py
- Modify: backend/app/integrations/contracts.py
- Test: tests/test_feedback_tickets.py

**Interfaces:**
- POST /api/messages/{id}/feedback accepts rating=true|false and an optional comment.
- POST /api/messages/{id}/ticket accepts title, description, and priority.
- GET /api/tickets returns the current user’s tickets; admin receives all tickets.
- PATCH /api/tickets/{id} accepts status=open|in_progress|resolved|closed and applies ownership/admin rules.
- services.tickets.create_feedback(db, user, message_id, rating, comment) returns Feedback.
- services.tickets.create_ticket(db, user, message_id, title, description, priority) returns Ticket.
- services.tickets.update_ticket(db, user, ticket_id, status) returns Ticket.

- [ ] **Step 1: Write failing workflow tests**

Test feedback creation, duplicate feedback update, ticket creation from an assistant message, employee visibility limited to owned tickets, employee status update on owned tickets, employee rejection of another user’s ticket, and admin visibility of all tickets.

- [ ] **Step 2: Run the workflow tests and verify the expected failure**

Run:

~~~powershell
python -m pytest tests/test_feedback_tickets.py -q
~~~

Expected: FAIL because feedback and ticket routes are missing.

- [ ] **Step 3: Implement validation and services**

Require feedback to reference an assistant message. Enforce maximum lengths for comments, titles, and descriptions. Use an explicit status transition map and update updated_at on every successful transition.

- [ ] **Step 4: Preserve the n8n extension boundary**

Define an AutomationClient protocol with create_external_ticket(ticket) and notify_ticket(ticket). Use a NoopAutomationClient in the first version. Do not call n8n and do not add n8n credentials to the required configuration.

- [ ] **Step 5: Run the workflow tests**

Run:

~~~powershell
python -m pytest tests/test_feedback_tickets.py -q
~~~

Expected: PASS.

- [ ] **Step 6: Commit feedback and tickets**

~~~powershell
git add backend/app/schemas/tickets.py backend/app/services/tickets.py backend/app/api/tickets.py backend/app/integrations/contracts.py tests/test_feedback_tickets.py
git commit -m "feat: add feedback and ticket workflows"
~~~

---

### Task 9: Add administrator metrics, health details, and evaluations

**Files:**
- Create: backend/app/schemas/admin.py
- Create: backend/app/services/metrics.py
- Create: backend/app/services/evaluations.py
- Create: backend/app/api/admin.py
- Test: tests/test_admin_evaluations.py

**Interfaces:**
- GET /api/admin/summary returns total_questions, average_latency_ms, positive_feedback_rate, open_ticket_count, and provider_breakdown.
- GET /api/admin/health/integrations returns application, AI provider, observability provider, and database health.
- GET /api/admin/documents returns enabled knowledge document metadata without exposing full sensitive document content.
- POST /api/admin/evaluations/run executes all active evaluation cases and returns the created run id.
- GET /api/admin/evaluations/{id} returns run summary and ordered result rows.
- services.evaluations.run_evaluation(db, provider_name) returns EvaluationRun.

- [ ] **Step 1: Write failing admin tests**

Test employee rejection from every admin endpoint, summary calculations from seeded chat/feedback/ticket records, health details in Mock mode, evaluation execution, keyword matching, and evaluation result retrieval.

- [ ] **Step 2: Run the admin tests and verify the expected failure**

Run:

~~~powershell
python -m pytest tests/test_admin_evaluations.py -q
~~~

Expected: FAIL because admin services and routes are missing.

- [ ] **Step 3: Implement metric queries**

Use SQLAlchemy aggregate queries for question count, average successful AI latency, feedback rate, open/in-progress ticket count, and provider counts. Return zero-safe values when no records exist.

- [ ] **Step 4: Implement health and document endpoints**

Check database access, provider configuration, and provider health checks. Return configured, reachable, and safe error_code fields. Do not expose base URL credentials or raw integration responses.

- [ ] **Step 5: Implement deterministic evaluation**

For each EvaluationCase, call the same ChatProvider used by chat, save an EvaluationResult, mark matched when all expected keywords are present, and compute score as passed_cases divided by total_cases. Continue remaining cases after one case fails.

- [ ] **Step 6: Run the admin tests**

Run:

~~~powershell
python -m pytest tests/test_admin_evaluations.py -q
~~~

Expected: PASS.

- [ ] **Step 7: Commit the admin and evaluation layer**

~~~powershell
git add backend/app/schemas/admin.py backend/app/services/metrics.py backend/app/services/evaluations.py backend/app/api/admin.py tests/test_admin_evaluations.py
git commit -m "feat: add admin metrics and AI evaluations"
~~~

---

### Task 10: Build the Vue 3 operator and employee interfaces

**Files:**
- Modify: frontend/index.html
- Modify: frontend/app.js
- Modify: frontend/styles.css
- Test: tests/test_frontend_delivery.py

**Interfaces:**
- The page loads Vue 3 from a browser script and mounts one application at #app.
- The frontend uses fetch with credentials=same-origin for all /api calls.
- The frontend never contains DIFY_API_KEY, LANGFUSE_SECRET_KEY, or any provider secret.
- The frontend displays employee navigation for chat and tickets.
- The frontend displays admin navigation for dashboard, health, documents, feedback, tickets, and evaluations.
- The frontend shows loading, empty, failure, and session-expired states in Chinese.

- [ ] **Step 1: Write failing static delivery tests**

Request / and assert the page contains the Vue 3 script, #app, /api references, and no secret variable names or secret values. Request /api/health and assert the page is served by the same application.

- [ ] **Step 2: Run the static tests and verify the expected failure**

Run:

~~~powershell
python -m pytest tests/test_frontend_delivery.py -q
~~~

Expected: FAIL because the page is still a placeholder.

- [ ] **Step 3: Implement the login and session shell**

Create a responsive layout with a login card, role-aware top bar, logout button, and a left navigation area. Use one small request helper that converts non-2xx responses into Chinese messages.

- [ ] **Step 4: Implement the employee workbench**

Add conversation creation, conversation list, message history, question submission, source display, feedback controls, and ticket creation. Disable the submit button during the request and preserve the user’s question when the provider fails.

- [ ] **Step 5: Implement the administrator workbench**

Add summary cards, provider health badges, document metadata table, feedback list, ticket status controls, and evaluation run/result panels. Only render the admin area after /api/auth/me confirms role=admin.

- [ ] **Step 6: Add responsive styles and CDN fallback documentation**

Use accessible labels, keyboard-focus styles, readable contrast, mobile layout rules, and an empty state for each list. Keep the Vue CDN URL in one place so it can be replaced by a local vendor file later.

- [ ] **Step 7: Run the frontend delivery tests**

Run:

~~~powershell
python -m pytest tests/test_frontend_delivery.py -q
~~~

Expected: PASS.

- [ ] **Step 8: Commit the frontend**

~~~powershell
git add frontend tests/test_frontend_delivery.py
git commit -m "feat: add Vue employee and admin workbenches"
~~~

---

### Task 11: Complete documentation and run the delivery verification

**Files:**
- Modify: README.md
- Create: docs/architecture.md
- Create: docs/api.md
- Create: docs/demo-script.md
- Create: docs/runbook.md
- Create: docs/resume.md
- Create: docs/n8n-extension.md
- Create: tests/test_delivery_contract.py

**Interfaces:**
- README documents Mock setup, real Dify/Langfuse setup, demo accounts, ports, environment variables, test command, and Docker Compose command.
- docs/architecture.md explains the request flow and provider adapters.
- docs/api.md lists the implemented routes, auth requirements, request bodies, and response examples.
- docs/demo-script.md gives a 3–5 minute employee/admin demonstration.
- docs/runbook.md gives checks for provider configuration, database health, timeout, auth failure, and Langfuse degradation.
- docs/resume.md contains truthful Chinese and English project bullets with placeholders represented as bracketed fields to be replaced by measured results.
- docs/n8n-extension.md describes the future webhook contract without making n8n a first-version dependency.

- [ ] **Step 1: Write the failing documentation contract test**

Assert README contains the Mock startup command, real-mode environment variables, demo accounts, pytest command, and Docker Compose command. Assert the architecture and runbook files mention Dify, Langfuse, Mock, and the safe-secret rule.

- [ ] **Step 2: Run the contract test and verify the expected failure**

Run:

~~~powershell
python -m pytest tests/test_delivery_contract.py -q
~~~

Expected: FAIL because the handoff documents are missing.

- [ ] **Step 3: Write the operational documentation**

Document these exact flows:

~~~powershell
python -m venv .venv
.\\.venv\\Scripts\\python.exe -m pip install -r requirements.txt
.\\.venv\\Scripts\\python.exe backend\\seed.py
.\\.venv\\Scripts\\python.exe backend\\run.py
~~~

and:

~~~powershell
docker compose up --build
~~~

Document employee@fieldassist.local and admin@fieldassist.local with the local demo password configured by DEMO_ADMIN_PASSWORD. Explain that real mode requires a Dify app API key and Langfuse credentials, while Mock mode does not.

- [ ] **Step 4: Write the resume evidence section**

Include a project summary template that records the actual scenario, number of integrated systems, request volume, latency, feedback score, evaluation score, rollout steps, and incident handling. State clearly that all metrics must be replaced with measured values before publication.

- [ ] **Step 5: Run all tests**

Run:

~~~powershell
python -m pytest tests -q
~~~

Expected: PASS with no test failures.

- [ ] **Step 6: Verify the container definition**

Run:

~~~powershell
docker compose config
docker compose build
~~~

Expected: Compose configuration validates and the FieldAssist image builds successfully. If Docker is unavailable, record the exact environment error in the final handoff and still complete the local pytest verification.

- [ ] **Step 7: Run a Mock-mode smoke test**

Start the application, then run:

~~~powershell
Invoke-WebRequest http://127.0.0.1:8000/api/health
Invoke-WebRequest http://127.0.0.1:8000/
~~~

Log in with the demo employee, ask a seeded policy question, submit feedback, create a ticket, log in as admin, run evaluations, and inspect the summary.

- [ ] **Step 8: Check the final Git diff and repository hygiene**

Run:

~~~powershell
git status --short
git diff --check
git ls-files | rg '(^|/)(\\.env|fieldassist\\.db|\\.venv|__pycache__|\\.pytest_cache|logs?/)'
~~~

Expected: only intended source and documentation files are tracked, diff check is clean, and no local secrets or generated files are tracked.

- [ ] **Step 9: Commit the handoff documentation**

~~~powershell
git add README.md docs tests/test_delivery_contract.py
git commit -m "docs: complete FieldAssist delivery handoff"
~~~

---

## Verification Summary

At the end of the plan, the following commands must succeed from D:/文档/ChatGPT/2:

~~~powershell
python -m pytest tests -q
docker compose config
docker compose build
~~~

The final manual acceptance must show:

1. Mock mode starts without external credentials.
2. Employee login, chat, feedback, and ticket creation work.
3. Admin login, metrics, provider health, documents, tickets, and evaluations work.
4. Dify adapter tests use fake HTTP responses and cover safe error mapping.
5. Langfuse failure does not break a successful answer.
6. README and runbook are sufficient for another person to reproduce the demo.
7. Git contains no credentials, database files, virtual environments, caches, or logs.
