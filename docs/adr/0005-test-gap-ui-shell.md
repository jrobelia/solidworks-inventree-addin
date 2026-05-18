# UI shell spec items need explicit tests before production code

During the image push feature build, two spec items had no covering tests in Stage 5, so they survived to manual verify broken: (1) the "Include image" checkbox in the Push Revision confirmation dialog, (2) SwAddin wiring the real SwViewportCaptureService into the TaskPaneControl constructor.

Rule: any time the spec says "dialog shows X" or "SwAddin wires Y", write a failing test before any production code. If a dialog-level behaviour is hard to unit-test, call it out explicitly in the manual-verify checklist rather than leaving it implicit.
