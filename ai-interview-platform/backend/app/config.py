import os
from dataclasses import dataclass, field


@dataclass
class Settings:
    database_url: str = field(default_factory=lambda: os.getenv("DATABASE_URL", "sqlite:///./data/interviews.db"))
    llm_base_url: str = field(default_factory=lambda: os.getenv("LLM_BASE_URL") or "https://api.deepseek.com/v1")
    llm_api_key: str = field(default_factory=lambda: os.getenv("LLM_API_KEY", ""))
    llm_model: str = field(default_factory=lambda: os.getenv("LLM_MODEL") or "deepseek-chat")
    max_upload_mb: int = 10
