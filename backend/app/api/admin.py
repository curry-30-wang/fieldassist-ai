from __future__ import annotations

from typing import Annotated, Any

from fastapi import APIRouter, Depends
from sqlalchemy.orm import Session

from backend.app.config import get_settings
from backend.app.database import get_db
from backend.app.dependencies import SafeHTTPException, require_admin
from backend.app.models import User
from backend.app.services.evaluations import get_evaluation_detail, run_evaluation
from backend.app.services.metrics import (
    get_integration_health,
    get_summary,
    list_enabled_documents,
)


router = APIRouter(prefix="/api/admin", tags=["admin"])


@router.get("/summary")
def summary(
    _admin: Annotated[User, Depends(require_admin)],
    db: Annotated[Session, Depends(get_db)],
) -> dict[str, Any]:
    return {"success": True, "data": get_summary(db), "message": "获取管理指标成功"}


@router.get("/health/integrations")
def integration_health(
    _admin: Annotated[User, Depends(require_admin)],
    db: Annotated[Session, Depends(get_db)],
) -> dict[str, Any]:
    return {
        "success": True,
        "data": get_integration_health(db, get_settings()),
        "message": "获取集成健康状态成功",
    }


@router.get("/documents")
def documents(
    _admin: Annotated[User, Depends(require_admin)],
    db: Annotated[Session, Depends(get_db)],
) -> dict[str, Any]:
    return {
        "success": True,
        "data": {"documents": list_enabled_documents(db)},
        "message": "获取知识文档成功",
    }


@router.post("/evaluations/run")
def start_evaluation(
    _admin: Annotated[User, Depends(require_admin)],
    db: Annotated[Session, Depends(get_db)],
) -> dict[str, Any]:
    run = run_evaluation(db, get_settings().ai_provider)
    return {
        "success": True,
        "data": {"run_id": run.id},
        "message": "评测运行完成",
    }


@router.get("/evaluations/{run_id}")
def evaluation_detail(
    run_id: int,
    _admin: Annotated[User, Depends(require_admin)],
    db: Annotated[Session, Depends(get_db)],
) -> dict[str, Any]:
    detail = get_evaluation_detail(db, run_id)
    if detail is None:
        raise SafeHTTPException(status_code=404, detail="评测记录不存在")
    return {"success": True, "data": detail, "message": "获取评测结果成功"}
