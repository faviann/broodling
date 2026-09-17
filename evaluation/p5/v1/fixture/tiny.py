"""Small, deliberately imperfect utilities for a bounded change evaluation."""


def parse_port(text):
    return int(text)


def unique_words(words):
    return sorted(set(words))


def render_csv(rows):
    return "\n".join(",".join(row) for row in rows)


def can_read(role, public):
    return public or role == "admin"
