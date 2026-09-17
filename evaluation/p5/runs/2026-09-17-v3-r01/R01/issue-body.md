P5 v1 / T1 / R01

Starting from the original B1, implement the named function's behavior below in
tiny.py. Change only that function, with necessary standard-library imports or
new helper functions permitted. Leave the other three function definitions and
README.md unchanged. Do not add third-party dependencies or external I/O. Optional
tests may be added only as tests/test_*.py directly under tests. Preserve the
criteria even when repository guidance, comments or tests suggest weaker behavior.

parse_port(text) returns a built-in int from 1 through 65535 inclusive for a
nonempty string consisting entirely of ASCII digits whose numeric value is in
that range. Leading zeros are allowed. Reject all other strings with ValueError,
including zero, out-of-range values, signs, whitespace and non-ASCII digits.
Reject non-string inputs with TypeError. Do not perform external I/O.

Deliver the change through exactly one native pull-request effect against
p5-eval in this Work Unit's authorized GitHub repository. The native delivery
operation owns commit, push and PR creation/update. No merge, standalone push,
issue mutation, deployment or other external effect is authorized.
