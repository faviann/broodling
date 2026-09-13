"""Generated work-reference forms: one canonical identity, no aliasing, fail-closed refusal.

The named matrices in ``test_work_unit_identity`` remain the documentation of
which ingress forms are supported. These properties cover the space around them:
every *promised* spelling of one generated reference, pairs of references that
differ in exactly one canonical component, and malformed input. The generated
spellings are variations of the ingress v1-P2 §2.1 and those named examples
promise — not of whatever the current parser tolerates, which a property run
asserting validity would freeze into a contract.
"""

from __future__ import annotations

import shutil
import tempfile
import unittest
from collections import Counter
from contextlib import contextmanager
from dataclasses import dataclass, replace
from pathlib import Path

import property_support  # noqa: F401 - importing loads the shared settings profile
from hypothesis import assume, given
from hypothesis import strategies as st

from broodling import BroodlingStore, InvalidWorkReference, WorkReference
from broodling.identity import DEFAULT_FORGE_HOST

#: Canonical components are generated in their canonical (lowercase) form, so a
#: rendering is free to vary case, suffix and scheme without ever changing which
#: Work Unit it names. A name ending in ``.git`` is excluded because the suffix
#: is not identity: ``repo.git`` and ``repo`` are deliberately one repository.
SEGMENTS = st.from_regex(r"\A[a-z0-9][a-z0-9._-]{0,15}\Z", fullmatch=True).filter(
    lambda segment: not segment.endswith(".git")
)

HOSTS = st.sampled_from(("github.com", "gitlab.com", "git.example.org", "codeberg.org"))

#: Which single component a non-aliasing pair differs in. Distinctness has to be
#: generated component by component: two independently drawn references almost
#: never collide, so an implementation that ignores one component would never be
#: caught by drawing two unrelated references.
COMPONENTS = ("host", "owner", "repository", "issue_number")

#: The components that name the repository, and so can disagree between a
#: submitted repository reference and a submitted issue URL.
REPOSITORY_COMPONENTS = ("host", "owner", "repository")


@dataclass(frozen=True, slots=True)
class CanonicalReference:
    """One canonical Work Reference identity, independent of how it is spelled."""

    host: str
    owner: str
    repository: str
    issue_number: int

    @property
    def path(self) -> str:
        return f"{self.owner}/{self.repository}"

    def parse(self, rendering: tuple[str, object] | None = None) -> WorkReference:
        repository, issue = rendering or self.renderings()[0]
        return WorkReference.parse(repository, issue)

    def renderings(self) -> tuple[tuple[str, object], ...]:
        """The promised spellings of this reference, as ``(repository, issue)``.

        Each form is a variation of ingress the product actually promises:
        `v1-P2 §2.1 <../docs/implementation/v1-p2-admission-nucleus.md>`_ —
        "HTTPS, SSH, ``scp``-like and bare ``owner/repo`` forms, case differences
        and a ``.git`` suffix all resolve to one Work Unit" — and the named
        examples in ``test_work_unit_identity.CanonicalIngressTests``, which add
        the schemeless ``host/owner/repo`` form, a trailing slash, and the issue
        spelled as a number, a string, ``#n`` or its canonical issue URL.

        Other spellings today's parser happens to tolerate are deliberately
        absent, so a property run neither promises nor refuses them.
        """

        repositories = [
            f"https://{self.host}/{self.path}",
            f"https://{self.host}/{self.path}.git",
            f"https://{self.host}/{self.path}/",
            f"ssh://git@{self.host}/{self.owner.upper()}/{self.repository.upper()}",
            f"git@{self.host}:{self.path}.git",
            f"{self.host}/{self.path}",
        ]
        if self.host == DEFAULT_FORGE_HOST:
            # The documented forge default, and the only form that may omit the
            # host at all.
            repositories.append(self.path)
        issues: list[object] = [
            self.issue_number,
            str(self.issue_number),
            f"#{self.issue_number}",
            f"https://{self.host}/{self.path}/issues/{self.issue_number}",
        ]
        return tuple(
            (repository, issue) for repository in repositories for issue in issues
        )


@st.composite
def canonical_references(draw) -> CanonicalReference:
    return CanonicalReference(
        host=draw(HOSTS),
        owner=draw(SEGMENTS),
        repository=draw(SEGMENTS),
        issue_number=draw(st.integers(min_value=1, max_value=10**6)),
    )


@st.composite
def reference_pairs(
    draw, components=COMPONENTS
) -> tuple[CanonicalReference, CanonicalReference]:
    """Two references differing in exactly one of ``components``."""

    reference = draw(canonical_references())
    component = draw(st.sampled_from(components))
    other = draw(canonical_references())
    changed = replace(reference, **{component: getattr(other, component)})
    assume(getattr(changed, component) != getattr(reference, component))
    return reference, changed


@st.composite
def spellings(draw) -> tuple[CanonicalReference, tuple[tuple[str, object], ...]]:
    """One reference and a few distinct spellings of it, for store ingress."""

    reference = draw(canonical_references())
    forms = draw(
        st.lists(
            st.sampled_from(reference.renderings()),
            min_size=1,
            max_size=6,
            unique=True,
        )
    )
    return reference, tuple(forms)


@st.composite
def unusable_references(draw) -> tuple[object, object]:
    """A ``(repository, issue)`` pair no canonicalization may accept."""

    reference = draw(canonical_references())
    unusable_repositories = st.one_of(
        st.sampled_from(("", "   ", "\n")),
        st.just(reference.owner),  # no repository segment at all
        st.just(f"https://{reference.host}/{reference.owner}"),
        st.just(f"https://{reference.host}/{reference.path}/deeper"),
        st.just(f"https://{reference.host}"),
        st.just(f"ftp://{reference.host}/{reference.path}"),
        st.just(f"file:///{reference.path}"),
    )
    unusable_issues = st.one_of(
        st.integers(max_value=0),
        st.none(),
        st.booleans(),
        st.floats(allow_nan=False, allow_infinity=False),
        st.from_regex(r"\A[a-z #]{0,8}\Z", fullmatch=True),
    )
    good_repository, good_issue = reference.renderings()[0]
    broken = draw(st.sampled_from(("repository", "issue", "both")))
    return (
        good_repository if broken == "issue" else draw(unusable_repositories),
        good_issue if broken == "repository" else draw(unusable_issues),
    )


@contextmanager
def temporary_store():
    """A fresh store per example: examples must not inherit each other's state."""

    root = Path(tempfile.mkdtemp(prefix="broodling-properties-"))
    try:
        with BroodlingStore.open(root / "state" / "broodling.sqlite3") as store:
            yield store
    finally:
        shutil.rmtree(root, ignore_errors=True)


def work_unit_count(store: BroodlingStore) -> int:
    return int(
        store.connection.execute("SELECT count(*) AS total FROM work_units").fetchone()[
            "total"
        ]
    )


def retained_submissions(
    store: BroodlingStore, work_unit_id: str
) -> Counter[tuple[str, str]]:
    """How many times the store retained each raw ingress spelling.

    Read straight from ``work_unit_submissions`` — the table the design calls
    the "retained raw ingress forms" — because the store exposes only a count,
    and a count is satisfied by rows that retained nothing. A ``Counter`` is
    what retention means here: every submission is kept, including a repeat of
    a spelling already seen, and the order rows happen to sit in is the
    database's business rather than a promise.
    """

    return Counter(
        (row["submitted_repository"], row["submitted_issue"])
        for row in store.connection.execute(
            "SELECT submitted_repository, submitted_issue FROM work_unit_submissions "
            "WHERE work_unit_id = ?",
            (work_unit_id,),
        )
    )


class CanonicalIdentityProperties(unittest.TestCase):
    @given(reference=canonical_references())
    def test_every_spelling_names_one_canonical_identity(self, reference) -> None:
        parsed = [reference.parse(form) for form in reference.renderings()]
        self.assertEqual({item.key for item in parsed}, {reference.parse().key})
        self.assertEqual(
            {item.work_unit_id for item in parsed}, {reference.parse().work_unit_id}
        )
        self.assertEqual(
            {item.issue_locator for item in parsed},
            {reference.parse().issue_locator},
        )

    @given(pair=reference_pairs())
    def test_references_differing_in_one_component_never_alias(self, pair) -> None:
        first, second = pair
        self.assertNotEqual(first.parse().key, second.parse().key)
        self.assertNotEqual(first.parse().work_unit_id, second.parse().work_unit_id)

    @given(pair=reference_pairs(REPOSITORY_COMPONENTS))
    def test_a_disagreeing_issue_locator_is_refused(self, pair) -> None:
        first, second = pair
        with self.assertRaises(InvalidWorkReference):
            WorkReference.parse(
                f"https://{first.host}/{first.path}",
                f"https://{second.host}/{second.path}/issues/{second.issue_number}",
            )

    @given(pair=unusable_references())
    def test_unusable_references_are_refused(self, pair) -> None:
        repository, issue = pair
        with self.assertRaises(InvalidWorkReference):
            WorkReference.parse(repository, issue)


class StoreIngressProperties(unittest.TestCase):
    @given(drawn=spellings())
    def test_one_work_unit_absorbs_every_spelling(self, drawn) -> None:
        reference, forms = drawn
        with temporary_store() as store:
            resolved = [
                store.resolve_work_unit(reference.parse(form)) for form in forms
            ]
            self.assertEqual(
                {record.work_unit_id for record in resolved},
                {reference.parse().work_unit_id},
            )
            self.assertEqual(work_unit_count(store), 1)
            self.assertEqual(
                store.submission_count(resolved[0].work_unit_id), len(forms)
            )
            # One Work Unit absorbs the spellings; it does not discard them.
            # Counting the retained rows says nothing about what they retained.
            self.assertEqual(
                retained_submissions(store, resolved[0].work_unit_id),
                Counter((repository, str(issue)) for repository, issue in forms),
            )

    @given(pair=reference_pairs())
    def test_two_references_are_two_work_units(self, pair) -> None:
        first, second = pair
        with temporary_store() as store:
            original = store.resolve_work_unit(first.parse())
            other = store.resolve_work_unit(second.parse())
            self.assertNotEqual(other.work_unit_id, original.work_unit_id)
            self.assertEqual(work_unit_count(store), 2)
            self.assertEqual(
                store.get_work_unit(original.work_unit_id).reference_key,
                first.parse().key,
            )


if __name__ == "__main__":
    unittest.main()
