"""Public run exceptions reproduced from the pinned SDK."""


class ZeroshotError(Exception):
    pass


class TargetError(ZeroshotError):
    pass


class RunNotFoundError(TargetError):
    pass


class SubmissionConflictError(TargetError):
    def __init__(self, message: str, *, existing_run_id: str) -> None:
        super().__init__(message)
        self.existing_run_id = existing_run_id
