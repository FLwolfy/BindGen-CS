# Diagnostic explanations

`bindgen-cs explain` lists the stable BGCS catalog. `bindgen-cs explain CODE` prints
the condition and corrective action; add `--json` for structured fields. The complete
[generated reference](generated/diagnostic-reference.md) comes from the same reviewed
catalog as the executable command.

An individual diagnostic also carries the failing request's source location, target,
phase or detailed compiler message. Use that context with the catalog explanation.
Clang messages are forwarded as `BGCS-FRONTEND-CLANG`; the explanation does not replace
the original parser message. Omitted friendly wrappers and unsupported object macros
are reported explicitly, while valid raw/native facts remain available as described
by the individual result.

Contributors update `eng/diagnostics/catalog.json` with English and Chinese text, then
run `python scripts/generate-diagnostic-docs.py`. The `--check` mode checks production
diagnostic IDs and all generated references. Tests additionally compare the compiled
runtime catalog and actual CLI JSON with the reviewed source, so adding a production
diagnostic without an executable explanation fails acceptance.
