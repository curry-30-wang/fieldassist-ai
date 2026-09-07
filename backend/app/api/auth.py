from typing import Annotated, Literal

from fastapi import APIRouter, Depends, Request, status
from pydantic import BaseModel
from sqlalchemy import select
from sqlalchemy.orm import Session

from backend.app.database import get_db
from backend.app.dependencies import SafeHTTPException, get_current_user
from backend.app.models import User, UserProfile
from backend.app.security import verify_password


router = APIRouter(prefix="/api/auth", tags=["authentication"])
DUMMY_PASSWORD_HASH = (
    "pbkdf2_sha256$600000$AAECAwQFBgcICQoLDA0ODw$"
    "WMe-pudYgaB71PfxiMH1_wMkuB5fl536_ndRtyys8BY"
)


class LoginRequest(BaseModel):
    email: str
    password: str


class UserData(BaseModel):
    user: UserProfile


class UserResponse(BaseModel):
    success: Literal[True] = True
    data: UserData
    message: str


class EmptyResponse(BaseModel):
    success: Literal[True] = True
    data: None = None
    message: str


def _user_response(user: User, message: str) -> UserResponse:
    return UserResponse(data=UserData(user=UserProfile.model_validate(user)), message=message)


@router.post("/login", response_model=UserResponse)
def login(credentials: LoginRequest, request: Request, db: Annotated[Session, Depends(get_db)]) -> UserResponse:
    email = credentials.email.strip().lower()
    user = db.scalar(select(User).where(User.email == email))
    password_hash = user.password_hash if user is not None else DUMMY_PASSWORD_HASH
    password_valid = verify_password(credentials.password, password_hash)
    if user is None or not password_valid or not user.is_active:
        raise SafeHTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="邮箱或密码错误")

    request.session.clear()
    request.session["user_id"] = user.id
    return _user_response(user, "登录成功")


@router.post("/logout", response_model=EmptyResponse)
def logout(request: Request) -> EmptyResponse:
    request.session.clear()
    return EmptyResponse(message="退出登录成功")


@router.get("/me", response_model=UserResponse)
def me(user: Annotated[User, Depends(get_current_user)]) -> UserResponse:
    return _user_response(user, "获取当前用户成功")
