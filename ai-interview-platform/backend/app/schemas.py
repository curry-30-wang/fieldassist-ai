from pydantic import BaseModel, ConfigDict, Field, FiniteFloat, computed_field


class ResponseSchema(BaseModel):
    model_config = ConfigDict(extra="forbid")


class JobAnalysis(ResponseSchema):
    job_title: str
    skills: list[str]
    responsibilities: list[str]
    experience_requirements: list[str]


class GeneratedQuestion(ResponseSchema):
    question_text: str
    question_type: str
    difficulty: str
    focus_points: list[str]
    reference_direction: str


class QuestionSet(ResponseSchema):
    questions: list[GeneratedQuestion]


class ScoreBreakdown(ResponseSchema):
    accuracy: float = Field(ge=0, le=10)
    completeness: float = Field(ge=0, le=10)
    relevance: float = Field(ge=0, le=10)
    clarity: float = Field(ge=0, le=10)

    @computed_field
    @property
    def total_score(self) -> float:
        return round(
            (self.accuracy + self.completeness + self.relevance + self.clarity) / 4,
            2,
        )


class AnswerEvaluation(ResponseSchema):
    score: ScoreBreakdown
    strengths: list[str]
    problems: list[str]
    suggestions: list[str]
    answer_structure: str


class InterviewReport(ResponseSchema):
    total_score: FiniteFloat = Field(ge=0, le=10)
    summary: str
    weaknesses: list[str]
    recommendations: list[str]


class ReportQuestion(ResponseSchema):
    id: str
    question_text: str
    question_type: str
    difficulty: str
    focus_points: list[str]
    reference_direction: str
    order_index: int


class ReportResult(ResponseSchema):
    question: ReportQuestion
    answer_text: str
    evaluation: AnswerEvaluation


class InterviewReportResponse(InterviewReport):
    results: list[ReportResult]
