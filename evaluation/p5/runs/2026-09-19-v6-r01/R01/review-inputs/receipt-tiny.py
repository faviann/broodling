"""Small, deliberately imperfect utilities for a bounded change evaluation."""


def parse_port(text):
    if not isinstance(text, str):
        raise TypeError("port must be a string")
    if not text or not str.isascii(text) or not str.isdigit(text):
        raise ValueError("port must contain only ASCII digits")

    significant_digits = str.lstrip(text, "0")
    port = int(significant_digits or "0")
    if not 1 <= port <= 65535:
        raise ValueError("port must be between 1 and 65535")
    return port


def unique_words(words):
    return sorted(set(words))


def render_csv(rows):
    return "\n".join(",".join(row) for row in rows)


def can_read(role, public):
    return public or role == "admin"
