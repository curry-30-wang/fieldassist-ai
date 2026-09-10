from typing import Annotated

from fastapi import Depends, HTTPException, Request, status
from sqlalchemy.orm import Session

from backend.app.database import get_db
from backend.app.models import User


class SafeHTTPException(HTTPException):
    pass


def get_current_user(
    request: Request,
    db: Annotated[Session, Depends(get_db)],
) -> User:
    user_id = request.session.get("user_id")
    if not isinstance(user_id, int) or isinstance(user_id, bool) or user_id <= 0:
        raise SafeHTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="请先登录")

    user = db.get(User, user_id)
    if user is None or not user.is_active:
        request.session.clear()
        raise SafeHTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="登录状态已失效")
    return user


def require_admin(
    user: Annotated[User, Depends(get_current_user)],
) -> User:
    if user.role != "admin":
        raise SafeHTTPException(status_code=status.HTTP_403_FORBIDDEN, detail="需要管理员权限")
    return user
