import pytest
from pydantic import ValidationError

from app.schemas import (
    AnswerEvaluation,
    GeneratedQuestion,
    InterviewReport,
    JobAnalysis,
    QuestionSet,
    ScoreBreakdown,
)


def test_score_breakdown_calculates_average_total():
    score = ScoreBreakdown(
        accuracy=8,
        completeness=6,
        relevance=9,
        clarity=7,
    )

    assert score.total_score == 7.5


def test_score_breakdown_rejects_score_outside_zero_to_ten():
    with pytest.raises(ValidationError):
        ScoreBreakdown(accuracy=11, completeness=6, relevance=7, clarity=8)


@pytest.mark.parametrize("invalid_score", [-1, 11, float("nan"), float("inf"), float("-inf")])
def test_interview_report_rejects_non_finite_or_out_of_range_total(invalid_score):
    with pytest.raises(ValidationError):
        InterviewReport(
            total_score=invalid_score,
            summary="Invalid total",
            weaknesses=[],
            recommendations=[],
        )


def test_response_schemas_validate_nested_interview_output():
    question = GeneratedQuestion(
        question_text="How would you design an API?",
        question_type="technical",
        difficulty="medium",
        focus_points=["API design"],
        reference_direction="Discuss trade-offs.",
    )

    assert JobAnalysis(
        job_title="Backend Engineer",
        skills=["Python"],
        responsibilities=["Build APIs"],
        experience_requirements=["3 years"],
    ).job_title == "Backend Engineer"
    assert QuestionSet(questions=[question]).questions == [question]
    assert AnswerEvaluation(
        score=ScoreBreakdown(accuracy=8, completeness=6, relevance=9, clarity=7),
        strengths=["Clear structure"],
        problems=["Missing monitoring details"],
        suggestions=["Discuss observability"],
        answer_structure="Situation, approach, trade-offs",
    ).score.total_score == 7.5
    assert InterviewReport(
        total_score=7.5,
        summary="Strong technical foundation.",
        weaknesses=["Observability"],
        recommendations=["Practice system design"],
    ).total_score == 7.5


def test_question_set_rejects_unexpected_fields():
    with pytest.raises(ValidationError):
        QuestionSet(questions=[], extra="not allowed")
