from functools import lru_cache

from pydantic import Field
from pydantic_settings import BaseSettings, SettingsConfigDict


PUBLIC_DEFAULT_SECRET_KEY = "replace-with-a-local-secret"


class Settings(BaseSettings):
    app_name: str = "FieldAssist"
    environment: str = Field(default="development", validation_alias="APP_ENV")
    secret_key: str = PUBLIC_DEFAULT_SECRET_KEY
    database_url: str = "sqlite:///./database/fieldassist.db"
    ai_provider: str = "mock"
    observability_provider: str = "mock"
    dify_base_url: str = "http://localhost"
    dify_api_key: str = ""
    dify_app_id: str = ""
    langfuse_public_key: str = ""
    langfuse_secret_key: str = ""
    langfuse_base_url: str = "https://cloud.langfuse.com"
    request_timeout_seconds: int = 15
    demo_admin_password: str = "replace-with-a-local-demo-password"

    model_config = SettingsConfigDict(env_file=".env", extra="ignore")


@lru_cache
def get_settings() -> Settings:
    return Settings()
