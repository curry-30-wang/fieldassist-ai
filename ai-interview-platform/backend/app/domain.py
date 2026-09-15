from enum import Enum


class InterviewStatus(str, Enum):
    CREATED = "created"
    IN_PROGRESS = "in_progress"
    COMPLETED = "completed"
    FAILED = "failed"


ALLOWED_TRANSITIONS = {
    InterviewStatus.CREATED: {InterviewStatus.IN_PROGRESS, InterviewStatus.FAILED},
    InterviewStatus.IN_PROGRESS: {InterviewStatus.COMPLETED, InterviewStatus.FAILED},
    InterviewStatus.COMPLETED: set(),
    InterviewStatus.FAILED: set(),
}


def can_transition(current: InterviewStatus, target: InterviewStatus) -> bool:
    return target in ALLOWED_TRANSITIONS[current]
