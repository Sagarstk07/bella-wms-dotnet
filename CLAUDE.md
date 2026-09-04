# Working on this repository

This is a .NET 10 conversion of the `api/` module of Bella WMS — an FDM4 IRMS warehouse
system written in Progress OpenEdge ABL. Read this before changing anything.

## Orientation, in order

1. `README.md` — what is built, what is not, and the deliberate behavioural differences.
2. `docs/ABL_CROSSREF.md` — all 72 ABL files with a disposition and a reason.
3. `docs/SCHEMA_REVIEW.md` — **now verified against the real `.df` dumps**, which are
   committed at `schema/`. Read the two rules above before writing any query, and the
   per-table reference before writing any column name.
4. `docs/RUNNING.md` — how to start the API with no database.
5. `docs/HANDLER_SPECS.md` — exactly what each unconverted Locus handler does, read out
   of the ABL. **Read this before writing any handler** — it saves re-reading 5,292 lines,
   it gives every response string as a literal with its ABL line number, and it flags five
   ABL defects you must reproduce rather than fix. Do not derive a string by analogy from
   a neighbouring handler; the ABL is not internally consistent and four strings were
   written wrong that way.
6. `docs/TRIGGER_RULES.md` — all 40 database triggers, read line by line. **It corrects
   `AuditTransaction`**: `date_time` is a 12-character string, not a `DateTimeOffset`, and
   two fields are missing. It also shows that the two "critical" inventory triggers have
   been compiled out since 1996 and do nothing.

## The ABL source

The original is **not** in this repository. Ask the user for the path to `bella-wms1` and
read it — most stubs name the exact file and line range they convert, and those references
are useless without it.

The module being converted is `api/wms/`. Its `AGENTS.md` is a good summary.

## Ground rules

**Compare characters case-insensitively.** ABL ignores case on character fields, and
exactly 1 of the 2,431 fields in `irms.df` opts out of that. SQL-92 does not ignore case.
The codebase writes both cases of the same status — `pick_status` has `INITIAL "O"` and is
queried as `"o"` in six places. So `WHERE status = 'C'` silently matches nothing where the
ABL matched everything. Use `NOT IN ('C','c')` in SQL (index-friendly, unlike `UPPER()`)
and `StringComparison.OrdinalIgnoreCase` in C#.

**A SQL INSERT applies no schema defaults.** 795 fields across 121 tables carry an
`INITIAL` value that ABL `CREATE` fills in and an `INSERT` does not. Check every new insert
against `docs/SCHEMA_REVIEW.md` before it runs. `transactions.item_type` is MANDATORY with
`INITIAL "S"` — that one alone made every audit write impossible until it was found.

**Never guess a database field — and you no longer have to.** The `.df` dumps arrived on
2026-09-03 and are committed at `schema/irms.df` and `schema/wmscomm.df`. **They are the
source of truth.** Check the column against them before you write it; the four defects
found the day they landed were all columns that did not exist or constraints nobody knew
about. Properties still marked `SCHEMA-INFERRED:` have not been checked yet — verify and
re-label them as you touch them.

**Cite the ABL.** Every converted method carries the file and line range it came from.
Keep doing that. It is what makes the conversion reviewable by someone who knows the
warehouse but not C#.

**A stub is better than a guess.** Where something cannot be converted faithfully yet,
throw `NotImplementedException` with the ABL reference and the reason. A silent wrong answer in a warehouse system is much worse than a loud failure.

## Behaviour that looks wrong and must not be "fixed"

These are preserved on purpose. Locus and the AWS EventBridge middleware have been running
against them for years and their retry logic may depend on them. Changing any of these
needs evidence from captured production traffic, not judgement.

- **Application failures return HTTP 200** with an error body, not a 4xx.
  (`locusAPI.cls:1774-1780`)
- **An unknown event returns exactly** `Invalid Endpoint Action <name>`, with a 200.
  (`wsLocusAPI.p:179-180`)
- **The auth header is `AUTHENTICATION`**, not `Authorization`. (`wsLocusAPI.p:83`)
- **Licence-plate prefix rule**: first character parses as an integer → `replen`, otherwise
  → `putaway`, including the empty-string case. (`wsLocusAPI.p:259-265`)
- **`SingleUnit` is the string `"true"`** while `CaptureSerialNo` is a real boolean, in the
  same payload. (`locusAPI.cls:4806` vs `485`)
- **`OrderJobResultTask` is a single object; `PutawayJobResultTask` is an array.**
  (`locusAPI.cls:2621` vs `3271`) This asymmetry is real and there is a test asserting it.
- **`holdreleasereject` returns `holdcomplete`'s success message.** (`locusAPI.cls:2044`)
  A copy-paste bug that reports success for a rejection. Reproduce it, flag it, raise it.
- **`cancelreject`'s alert email says `"Cnacel"`.** (`locusAPI.cls:2162`) The one place in
  the reject family where the email body differs from the audit comment, and it differs
  only by a typo. Not on the wire; it lands in operators' mailboxes.
- **`holdreject` alone does not call `holdOrderJob`** when the carton is missing.
  (`locusAPI.cls:1912`, commented out — "Removed to prevent hold loops".) Deliberate and
  correct: holding a job because a hold was rejected loops. There is a test asserting it.
- **Four handlers build `"for job id "` and never append the id.**
  (`locusAPI.cls` 1881, 2060, 2076, 2092) They parse `cJobID` and do not use it.

## Deliberate departures from the ABL

Already made, already documented. Do not revert them.

- TLS certificates are validated. The ABL passes `--insecure` on every cURL call.
- No shell invocation. `headerCurl.sh` runs `eval $CMD` with interpolated data.
- An unresolvable API employee is a 401. The ABL logs and continues with a *stale* ambient
  context — a cross-tenant data hazard.
- Authentication never creates an `empmst` row. `wsMiddlewareAPI.p:429` does.
- Archived payloads are redacted. The ABL wrote credentials to disk in clear text.
- `testtotemove` (`locusAPI.cls:3188`) is not registered. It is a test hook that reflection
  dispatch made reachable from production traffic.

## What is deferred, and why it is not just "hard"

Anything touching picking or shipping. `locusAPI.cls:2954` runs `wms_webhost/wspick.p`
(5,890 lines) **inside an open transaction** and passes a **ROWID as a string** — a
physical database address with no meaning outside the ABL session that read it. It cannot
cross any seam, so no amount of cleverness converts it. It waits for the picking module.

Affected: Locus `PICK` and `PICKCOMPLETE`, AutoBagger `ORDCM` and `cartonPacked`.

## Architecture constraints

From the Phase 6 architecture document, which the user has:

- **Two databases, two unit-of-work types.** `IIrmsUnitOfWork` and `IWmsCommUnitOfWork`.
  A class asking for both is asking for a distributed transaction — reject it.
- **Reads run at `READ UNCOMMITTED`.** ABL's default lock is `SHARE-LOCK`, so a .NET read
  taking read locks can block pickers on the warehouse floor. See `IsolationPolicy`.
- **Lock order is declared, not emergent.** `LockOrder.Declare` requires an ABL source
  reference. Both stacks run against one database for the whole parallel run; mismatched
  order means deadlock.
- **Every writer goes through `IAuditService`.** It replaces the `n_trans.t` database
  trigger. A trigger fires for everyone; a service only fires for callers. A direct
  `transactions` insert silently loses audit rows.
- **Everything is scoped.** Scoped lifetimes are what replace `wms_webhost/wmswipeout.p`.
  A singleton holding request state reintroduces exactly the bug that file exists to paper
  over. `CompositionTests` has `ValidateScopes` on to catch it.

## Commands

```
dotnet build          # TreatWarningsAsErrors is currently false — see Directory.Build.props
dotnet test           # 69 tests, all green as of 2026-09-01. Keep it that way.
dotnet run --project src/Hosts/Bella.Wms.Api    # runs with in-memory fakes, no database
```

## Build it as you write it

**Added 2026-09-01, after the first green build.** `Bella.Wms.Platform.Fakes` had been
written, documented in `docs/RUNNING.md`, and referenced by nothing — not the solution,
not the API host, not the tests. It had therefore never been compiled. Adding one line to
the solution surfaced five defects in a row, one of which (`CreateScope` on an
`IAsyncDisposable` unit of work) would have crashed the host on a real database too.
`LocusEndpointTests` and `LocusContractTests` had never compiled either.

So: **when you add a project, add it to `Bella.Wms.sln` and reference it from something in
the same change.** When you write a test file, run it. A file that compiles nowhere is not
work in progress; it is a document that looks like code, and it rots silently against
every interface it claims to implement.

Two structural guards now exist against the specific drift that happened:

- `AuditTransaction.Enrich` is the single enrichment implementation. `AuditService` and
  `FakeAuditService` both call it. Do not reintroduce a hand-written copy in either.
- `LocusJson.SerializerOptions` in Contracts is the single wire-format options instance.

## The best next tasks

In rough order of value:

1. **The tote family** — `toteinduct`, `totemove`, `toteinductcancel`. Unblocked now that
   `pick` is specified (37 columns, `co_wh_carton` is the index they need). Budget a day:
   `toteinduct` is bigger than `docs/HANDLER_SPECS.md` claims — it also touches
   `cartondtl`, carries a single-unit concept, detects duplicate totes, and works a `JT`
   transaction through a third status (`P`) the reject family never sees. It also writes
   `pick.custom_data[4]` and `[5]`, so the extent-field access pattern has to be settled
   first.
2. **The three ERP payload builders** — `IRSHUP`, `IRPMUP`, `IRPIUP`. These need the schema.
3. **The config classes** — `interface/config.cls`, `rest/config.cls`, `rest/awsConfig.cls`.
   About 1,000 lines of JSON parsing, no database. **The only substantial work the missing
   schema does not block.**
4. **`rest/` live-or-dead.** Six files, 1,569 lines, possibly superseded by
   `middleware.cls`. Only `tools/BEL-1018_DataSetup.p` references any of it. Needs a human
   who knows the deployment history — ask, do not guess.

**Twenty of twenty-two Locus handlers are done** (2026-09-04): all six lifecycle, all four
of the reject family, all three tote handlers, and the whole putaway family.

Two remain — `pick` and `pickcomplete` — and both are blocked on the `wspick.p` ROWID seam
described above, not on effort. Nothing else in `api/` can unblock them.

Two worked patterns to copy from: `LocusRejectFamilyHandlerBase` for anything with a
transaction and an audit row, and `LocusReplenPutCompleteHandler` for anything that moves
stock — it is the one that establishes how a multi-table write, a cycle-count seam and a
reproduced ABL defect are all supposed to look.

## Do not

- Add a `.df`-dependent feature and claim it works. Nothing has run against a real database.
- Delete `Bella.Wms.Platform.Fakes` yet — but do delete it once integration tests run
  against a real database, before it grows features people start testing against.
- Soften the guard in `AddWmsFakes`. A warehouse host silently running on fake inventory
  would accept picks and answer Locus with confident nonsense.
- Put a credential in a source file. Six sets are already committed in the ABL and need
  rotating; do not add a seventh here.
