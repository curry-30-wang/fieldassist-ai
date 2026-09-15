from dataclasses import dataclass


@dataclass
class Settings:
    database_url: str = "sqlite:///./data/interviews.db"
    llm_base_url: str = "https://api.deepseek.com/v1"
    llm_api_key: str = ""
    llm_model: str = "deepseek-chat"
    max_upload_mb: int = 10
