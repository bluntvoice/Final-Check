# Project / Template / Version Management v0

## Scope and implementation status

This architecture extends independent Quick Compare without replacing its Engine, frozen Snapshot / Comparison payloads or result / review UI. Implementation progresses in the [task](../tasks/project-template-version-management-v0.md) Phase order. Phases 1–2 implement Template Center and Contract Projects; Version / Round / project comparisons / lifecycle remain designs for Phases 3–5, not claimed implemented yet.

## Template model and persistence

Logical `Template` owns name, contract type, enabled/deleted flags, notes and UTC timestamps. `TemplateVersion` owns a separate Guid, user-editable version label, original `ComparisonFile` metadata/hash, persisted Snapshot Guid, parse status and current marker. Database schema 5 adds Templates and TemplateVersions with Restrict foreign keys and unique (TemplateId, Version), and a filtered unique TemplateId index for IsCurrent=1.

First import initializes one current version. Subsequent imports preserve every existing version and do not replace current automatically; a transaction switches current by clearing the old marker and selecting the requested version. Version labels support existing PRD labels (e.g. v1.0) and three-part suggestions (next patch for known numeric labels); duplicates are rejected with a correction prompt rather than overwriting.

Import reads source metadata/hash and DOCX on a worker, checks parse hash and post-parse source hash, then appends Snapshot/version metadata in a short managed data scope and SQLite transaction. Partial is saved honestly with a warning. Template lists and version lists query only metadata, never all Snapshot JSON. Selected historical Snapshot preview is loaded explicitly and projected on a worker through the same native preview builder; no DOCX reconstruction claim.

Template deletion is logical hiding/disabling after reference warning and confirmation. All historical versions and Snapshots remain for projects/history; no external original file deletion. Phases 2/4 extend reference inspection to project bindings and explicit historical comparison context. Core contracts expose store/service interfaces; Desktop orchestrates existing parser and short DI scopes; App View only adapts picker/drop, VM state and commands.

## Source relink

Original DOCX is never copied into DataRoot. A bounded worker scan checks the original directory and at most 20 non-linked child directories / 500 DOCX files, using size and SHA-256; exact-hash matches are ordered by original name, then path. Candidates only fill a proposed path and require a separate relink action. Scan failure/no match falls back to manual selection, not a guessed replacement. Source disappearance does not invalidate frozen Snapshot viewing.

Relink only updates path/name/size/mtime after exact-hash validation; hash and Snapshot identity remain unchanged. Changed content at the same path is rejected for relink and must be imported as a new version. Historical Comparison source identities are never rewritten when a source path is updated.

## Project / version / round design (Phases 2–5)

Phase 2 implements Projects / ProjectFolders in incremental schema 6. Project tags are a validated list of name + #RRGGBB color, persisted as metadata JSON (not document payload); folders are flat labels without hierarchy or physical file moves. Lists query status/name/template/type/update/folder/exact-tag filters on metadata, sorted UpdatedAt descending, with 20-row pages / 100-row maximum and virtualized UI. Full Snapshot JSON is not selected; invalid Snapshot payloads do not prevent metadata listing. Project creation/edit validates template/version membership, inherits type only when input is blank, never guesses counterparty and never changes baseline. Disabled/deleted templates reject new bindings but permit preserving an existing binding. The editor keeps a stable project identity and template version across list filtering/refresh to prevent accidental new projects or silent unbinding. Template delete warnings now include project names, including preserved lifecycle states.

ContractProject binds a logical Template and a version identity, carries explicit user-entered counterparty/type, one-level Folder / tags, lifecycle status and CurrentBaselineVersionId. Type edits do not switch Template. A ContractVersion references a frozen Snapshot and original metadata, explicit Own / Counterparty role, round and import order/time; rounds allow multiple versions on either side. Role must be confirmed by the user, never inferred from filename, author or path.

Only a version belonging to the same project with Own role can become current baseline. New Own import defaults to not changing it; UI asks separately. A project comparison chooses Template current/historical version or the current Own baseline explicitly, remembers baseline type without auto-executing, and suggests Own baseline for Counterparty versions instead of preceding Counterparty versions. Each execution appends an independent ComparisonRecord and immutable context link to project/current-version/baseline-version/type; Engine and result/review UI are shared. Template switches/current-baseline changes affect future selection only.

Project lists/timelines/history use bounded metadata queries and virtualization; full Snapshot payloads load only for import/comparison/selected preview/history viewing. Archive preserves all data and hides it from active lists; recycle is reversible and permanent deletion requires a separate second confirmation, with reference-aware cleanup only of exclusively owned managed records/assets. Shared Template/Quick Compare Snapshots/results and external DOCX must remain protected. Working-copy cleanup must coordinate storage leases and restore journal recovery; destructive ambiguities require confirmation.

## Review semantics and compatibility

User-visible state is 未处理 / 已审阅 / 忽略. 已审阅 only means a person viewed the difference, not acceptance, agreement or adoption of the contract change. Stable internal `ComparisonReviewState.Confirmed` and persisted payload schema 1 remain unchanged; no terminology-only migration. Existing persisted Confirmed states render as 已审阅, with matching filters/group summaries/action tooltips. Decision/opinion states are outside scope.

## Storage compatibility

Incremental EF migrations must preserve existing Snapshot schema 2, Comparison schema 1, ComparisonRecord schema 1, restore operations and storage bootstrap. New template rows join readonly schema recognition, storage migration row digests and Snapshot/hash validation; original template paths join migration/usage exclusions even if an original sits inside old DataRoot. InstallRoot and DataRoot remain independent; no packaging or installer changes.

## Verification

Phase 1 uses generated nonsensitive DOCX / GUID SQLite fixtures for creation, versions/current uniqueness, disable/edit, duplicate/cancel rollback, missing/moved/changed source relink, Snapshot loading, schema-4 upgrade and full storage migration. VM tests cover single-file picker/drop input, metadata selection, delete warnings/confirmation and persisted Confirmed display compatibility. Actual final-package Template and Version drag/drop, contract version chain, restart/history and archive/recycle are final-HEAD acceptance gates, not substituted by unit tests.
