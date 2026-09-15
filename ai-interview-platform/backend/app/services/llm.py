import json
from collections.abc import Sequence
from typing import Protocol, TypeVar, runtime_checkable

import httpx2 as httpx
from pydantic import BaseModel, ValidationError

from app.config import Settings
from app.schemas import (
    AnswerEvaluation,
    GeneratedQuestion,
    InterviewReport,
    JobAnalysis,
    QuestionSet,
    ScoreBreakdown,
)
from app.services.prompts import (
    answer_evaluation_prompts,
    job_analysis_prompts,
    question_generation_prompts,
    report_prompts,
)
from app.services.retriever import RetrievedChunk


class AIServiceError(RuntimeError):
    """Raised when a compatible LLM service cannot return a valid response."""


@runtime_checkable
class LLMProvider(Protocol):
    async def analyze_job(self, job_description: str) -> JobAnalysis: ...

    async def generate_questions(
        self, job: JobAnalysis, context: Sequence[RetrievedChunk]
    ) -> QuestionSet: ...

    async def evaluate_answer(
        self,
        question: GeneratedQuestion,
        answer: str,
        context: Sequence[RetrievedChunk],
    ) -> AnswerEvaluation: ...

    async def build_report(self, evaluations: Sequence[AnswerEvaluation]) -> InterviewReport: ...


class FakeLLMProvider:
    async def analyze_job(self, job_description: str) -> JobAnalysis:
        return JobAnalysis(
            job_title="Python 后端开发工程师",
            skills=["Python", "FastAPI", "SQL"],
            responsibilities=["设计和开发后端 API", "维护服务稳定性"],
            experience_requirements=["具备后端项目经验", "熟悉 Web API 开发"],
        )

    async def generate_questions(
        self, job: JobAnalysis, context: Sequence[RetrievedChunk]
    ) -> QuestionSet:
        return QuestionSet(
            questions=[
                GeneratedQuestion(
                    question_text="请解释 FastAPI 的依赖注入机制。",
                    question_type="基础知识",
                    difficulty="中等",
                    focus_points=["依赖注入", "请求生命周期"],
                    reference_direction="说明依赖声明、复用方式和使用场景。",
                ),
                GeneratedQuestion(
                    question_text="请介绍一个你负责过的后端项目。",
                    question_type="项目经历",
                    difficulty="中等",
                    focus_points=["个人职责", "技术选型"],
                    reference_direction="说明问题、行动和可衡量结果。",
                ),
                GeneratedQuestion(
                    question_text="接口响应变慢时，你会如何定位问题？",
                    question_type="场景分析",
                    difficulty="较难",
                    focus_points=["监控", "性能分析"],
                    reference_direction="从复现、指标、日志到修复验证说明过程。",
                ),
                GeneratedQuestion(
                    question_text="如何为一个新 API 设计输入校验和错误响应？",
                    question_type="基础知识",
                    difficulty="中等",
                    focus_points=["数据校验", "错误处理"],
                    reference_direction="说明边界校验、错误码和可读提示。",
                ),
                GeneratedQuestion(
                    question_text="面对并发写入冲突，你会如何设计处理方案？",
                    question_type="场景分析",
                    difficulty="较难",
                    focus_points=["事务", "并发控制"],
                    reference_direction="比较事务、锁和幂等设计的取舍。",
                ),
            ]
        )

    async def evaluate_answer(
        self,
        question: GeneratedQuestion,
        answer: str,
        context: Sequence[RetrievedChunk],
    ) -> AnswerEvaluation:
        return AnswerEvaluation(
            score=ScoreBreakdown(accuracy=8, completeness=7, relevance=8, clarity=7),
            strengths=["回答覆盖了问题核心。"],
            problems=["可以补充更多可验证的实践细节。"],
            suggestions=["结合具体项目说明方案的取舍和结果。"],
            answer_structure="结论、实施方法、权衡与结果",
        )

    async def build_report(self, evaluations: Sequence[AnswerEvaluation]) -> InterviewReport:
        total_score = round(
            sum(evaluation.score.total_score for evaluation in evaluations) / len(evaluations), 2
        ) if evaluations else 0.0
        return InterviewReport(
            total_score=total_score,
            summary="候选人具备基础后端开发能力。",
            weaknesses=["回答中的项目细节可以更具体。"],
            recommendations=["练习用可量化结果说明技术决策。"],
        )


ResponseModel = TypeVar("ResponseModel", bound=BaseModel)


class OpenAICompatibleLLM:
    def __init__(
        self,
        base_url: str,
        api_key: str,
        model: str,
        client: httpx.AsyncClient | None = None,
    ) -> None:
        self.base_url = base_url.rstrip("/")
        self.api_key = api_key
        self.model = model
        self._client = client

    @classmethod
    def from_settings(cls, settings: Settings) -> "OpenAICompatibleLLM":
        return cls(settings.llm_base_url, settings.llm_api_key, settings.llm_model)

    async def analyze_job(self, job_description: str) -> JobAnalysis:
        system_prompt, user_prompt_template = job_analysis_prompts()
        return await self._request(
            JobAnalysis, system_prompt, user_prompt_template.format(job_description=job_description)
        )

    async def generate_questions(
        self, job: JobAnalysis, context: Sequence[RetrievedChunk]
    ) -> QuestionSet:
        return await self._request(QuestionSet, *question_generation_prompts(job.job_title, job.skills, context))

    async def evaluate_answer(
        self,
        question: GeneratedQuestion,
        answer: str,
        context: Sequence[RetrievedChunk],
    ) -> AnswerEvaluation:
        return await self._request(
            AnswerEvaluation, *answer_evaluation_prompts(question, answer, context)
        )

    async def build_report(self, evaluations: Sequence[AnswerEvaluation]) -> InterviewReport:
        return await self._request(
            InterviewReport,
            *report_prompts(json.dumps([evaluation.model_dump() for evaluation in evaluations])),
        )

    async def _request(
        self,
        response_model: type[ResponseModel],
        system_prompt: str,
        user_prompt: str,
    ) -> ResponseModel:
        payload = {
            "model": self.model,
            "temperature": 0.2,
            "messages": [
                {"role": "system", "content": system_prompt},
                {"role": "user", "content": user_prompt},
            ],
            "response_format": {"type": "json_object"},
        }
        try:
            if self._client is not None:
                response = await self._client.post(
                    f"{self.base_url}/chat/completions",
                    headers={"Authorization": f"Bearer {self.api_key}"},
                    json=payload,
                )
            else:
                async with httpx.AsyncClient() as client:
                    response = await client.post(
                        f"{self.base_url}/chat/completions",
                        headers={"Authorization": f"Bearer {self.api_key}"},
                        json=payload,
                    )
            response.raise_for_status()
            content = response.json()["choices"][0]["message"]["content"]
            if not isinstance(content, str):
                raise TypeError("response content must be a JSON string")
            return response_model.model_validate(json.loads(content))
        except (httpx.HTTPError, KeyError, IndexError, TypeError, json.JSONDecodeError, ValidationError) as exc:
            raise AIServiceError(f"AI service error: {exc}") from exc
