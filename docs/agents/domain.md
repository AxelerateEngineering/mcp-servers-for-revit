# Domain docs

## Layout

This repo uses a single-context layout:

- `GLOSSARY.md` at the repo root for domain vocabulary.
- `docs/adr/` for architecture decision records.

## Before exploring

Read `GLOSSARY.md` and any ADRs relevant to the work.

If either is absent, proceed silently. Do not suggest creating
them upfront. The domain-modeling skill creates them as terms
and decisions are established.

## Use the glossary's vocabulary

Use defined terms in issue titles, proposals, hypotheses,
and test names. Avoid synonyms the glossary explicitly rejects.

When a needed concept is missing, reconsider whether it belongs
in the domain and note genuine gaps for domain-modeling.

## Flag ADR conflicts

If a proposal contradicts an existing ADR, identify that ADR
and explain why the decision should be reconsidered.
