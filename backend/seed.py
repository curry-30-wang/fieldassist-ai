import base64
import hashlib
import json
import secrets

from sqlalchemy import select

from backend.app.config import get_settings
from backend.app.database import SessionLocal, init_db
from backend.app.models import EvaluationCase, KnowledgeDocument, User


PBKDF2_ITERATIONS = 600_000


KNOWLEDGE_DOCUMENTS = (
    {
        "title": "员工请假政策",
        "category": "leave_policy",
        "content": "员工申请年假应至少提前三个工作日提交，并经直属主管审批；紧急病假可先通知主管后补交材料。",
    },
    {
        "title": "费用报销流程",
        "category": "expense_reimbursement",
        "content": "业务费用应在发生后三十日内提交报销，附有效发票和费用说明，并由部门负责人审批。",
    },
    {
        "title": "IT 账号访问指南",
        "category": "it_account_access",
        "content": "账号无法登录时先通过自助服务重置密码；多因素认证设备遗失应联系 IT 服务台核验身份。",
    },
    {
        "title": "客户支持升级规范",
        "category": "customer_support_escalation",
        "content": "影响客户核心业务的 P1 事件应在十五分钟内升级给值班经理，并持续记录处置进展。",
    },
    {
        "title": "数据安全要求",
        "category": "data_security",
        "content": "敏感数据不得通过个人邮箱或未授权网盘传输；发现疑似泄露应立即停止传播并报告安全团队。",
    },
)


EVALUATION_CASES = (
    {
        "question": "年假需要提前多久申请？",
        "category": "leave_policy",
        "expected_keywords": ["三个工作日", "主管审批"],
    },
    {
        "question": "业务费用报销需要准备什么？",
        "category": "expense_reimbursement",
        "expected_keywords": ["三十日", "有效发票", "费用说明"],
    },
    {
        "question": "多因素认证设备遗失后应该怎么办？",
        "category": "it_account_access",
        "expected_keywords": ["IT 服务台", "核验身份"],
    },
    {
        "question": "影响客户核心业务的事件如何升级？",
        "category": "customer_support_escalation",
        "expected_keywords": ["P1", "十五分钟", "值班经理"],
    },
    {
        "question": "发现敏感数据疑似泄露时如何处理？",
        "category": "data_security",
        "expected_keywords": ["停止传播", "安全团队"],
    },
)


def _base64url_without_padding(value: bytes) -> str:
    return base64.urlsafe_b64encode(value).rstrip(b"=").decode("ascii")


def _hash_demo_password(password: str) -> str:
    salt = secrets.token_bytes(16)
    digest = hashlib.pbkdf2_hmac(
        "sha256",
        password.encode("utf-8"),
        salt,
        PBKDF2_ITERATIONS,
    )
    return "$".join(
        (
            "pbkdf2_sha256",
            str(PBKDF2_ITERATIONS),
            _base64url_without_padding(salt),
            _base64url_without_padding(digest),
        )
    )


def seed_database() -> None:
    init_db()
    settings = get_settings()
    demo_users = (
        ("admin@fieldassist.local", "Demo Admin", "admin"),
        ("employee@fieldassist.local", "Demo Employee", "employee"),
    )

    with SessionLocal.begin() as session:
        for email, display_name, role in demo_users:
            existing_user = session.scalar(select(User).where(User.email == email))
            if existing_user is None:
                session.add(
                    User(
                        email=email,
                        display_name=display_name,
                        password_hash=_hash_demo_password(settings.demo_admin_password),
                        role=role,
                        is_active=True,
                    )
                )

        for document_data in KNOWLEDGE_DOCUMENTS:
            existing_document = session.scalar(
                select(KnowledgeDocument).where(KnowledgeDocument.title == document_data["title"])
            )
            if existing_document is None:
                session.add(KnowledgeDocument(**document_data))

        for case_data in EVALUATION_CASES:
            existing_case = session.scalar(
                select(EvaluationCase).where(EvaluationCase.question == case_data["question"])
            )
            if existing_case is None:
                session.add(
                    EvaluationCase(
                        question=case_data["question"],
                        category=case_data["category"],
                        expected_keywords_json=json.dumps(
                            case_data["expected_keywords"],
                            ensure_ascii=False,
                        ),
                    )
                )


if __name__ == "__main__":
    seed_database()
