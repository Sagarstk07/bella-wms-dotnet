# Locus handler specifications

Exactly what each unconverted handler does, read from `api/wms/locusAPI.cls`. Written so a
handler can be implemented without re-reading 5,292 lines of ABL.

`LocusAcceptHandler` is the worked reference. Everything below follows its shape.

> **Response strings are contract.** Locus has been receiving these exact strings for
> years and may log or match on them. Reproduce them byte-for-byte, including the defects
> flagged below. Do not "improve" the wording.

---

## Group 1 — Trivial. Message only, no database.

Five handlers that parse the payload and return a string. Nothing else. All are ~8-16
lines of ABL and convert in minutes.

| Handler | ABL lines | Response |
|---|---|---|
| `holdcomplete` | 1878-1883 | `OrderJobResult hold complete successful for job id ` |
| `holdreleasecomplete` | 1958-1973 | `OrderJobResult hold release complete successful for job id ` + JobId |
| `updatecomplete` | 2048-2063 | `OrderJobResult update complete successful for job id ` |
| `updatereject` | 2064-2079 | `OrderJobResult update reject successful for job id ` |
| `cancelcomplete` | 2080-2095 | `OrderJobResult cancel complete successful for job id ` |

### ⚠ Defect: four of these drop the job id

Look carefully at the table. Only `holdreleasecomplete` appends `cJobID`. The other four
end with a **trailing space and no id** — they parse `cJobID` into a variable and then
never use it:

```abl
cJobID = parseJSOProperty(jsoOrderJobResult,"JobId")   /* parsed... */
...
lcResponse = "OrderJobResult update complete successful for job id ".   /* ...never used */
```

`holdcomplete` (1878-1883) does not even parse the payload — it is four lines long.

**Reproduce this exactly.** It is wire-visible and Locus may be logging these responses.
Fixing it is a contract change that needs evidence from captured traffic first. Add a
`⚠ ABL DEFECT` comment at each site so the next reader knows it is deliberate.

---

## Group 2 — The reject family. One shape, four handlers.

`reject` (1803-1877) is the template. `holdreject`, `holdreleasereject` and `cancelreject`
are the same 74 lines with different strings.

### The shape

1. Parse `EventInfo`, `JobId`, `JobStatus`, `JobDate`.
2. `find first cartonmst` on company + warehouse + `carton_id = JobId`, **NO-LOCK**.
3. **Not found** → HTTP 200, body `Unable to find carton matching job id <JobId>`, call
   `holdOrderJob(JobId, body)`, return. **`holdreject` is the exception** — see divergence 1.
4. **Idempotency guard** — if `cartonmst.row_status` is `C` or `E`, return 200 with the
   handler's idempotency string (table below) and do nothing else.
   The ABL is `if lookup(cartonmst.row_status,"C,E":U) gt 0`.
5. Open a transaction:
   - Re-find the carton **EXCLUSIVE-LOCK** by rowid (a second buffer, `bcartonmst`).
   - Write a `JE` audit row: company, warehouse, `trans_type = "JE"`, carton id,
     `po_number` = `cartonmst.order`, `po_suffix` = `cartonmst.order_suffix`,
     `comments` = the message below.
   - If the carton's `row_status` is not already `C`, set it to `E`.
6. Send an alert email — `sendMail(cSubject, cBody)`. `cBody` is normally the same string
   as the audit `comments`; **`cancelreject` is the exception** — see divergence 2.
7. HTTP 200 with the success string.

### Per-handler strings — literal, read from the ABL 2026-09-01

Every cell below is the exact ABL literal with its line number. An earlier revision of this
document paraphrased four of them as "hold-reject wording" / "cancel wording" and omitted
the idempotency prefixes entirely; four strings were written wrong downstream as a result.
**Do not derive these by analogy from `reject` — the ABL is not internally consistent.**

| Handler | ABL | Audit `comments` | Email subject | Email body | Success response | Idempotency response |
|---|---|---|---|---|---|---|
| `reject` | 1803-1877 | `OrderJobResult rejected by Locus: ` +EventInfo (1858) | `Error sending carton <order> - <carton> to locus` (1869) | = comments (1870) | `OrderJobResult reject successful for job id ` +JobId (1874) | `OrderJobResult reject already processed for job id ` +JobId (1840) |
| `holdreject` | 1884-1957 | `Hold rejected by Locus: ` +EventInfo (1938) | `Error putting carton <order> - <carton> on hold in locus` (1949) | = comments (1950) | `OrderJobResult hold reject successful for job id ` +JobId (1954) | `OrderJobResult hold reject already processed for job id ` +JobId (1920) |
| `holdreleasereject` | 1974-2047 | `Hold release rejected by Locus: ` +EventInfo (2028) | `Error releasing carton <order> - <carton> from hold in locus` (2039) | = comments (2040) | ⚠ defect — see below (2044) | `OrderJobResult hold release reject already processed for job id ` +JobId (2010) |
| `cancelreject` | 2096-2169 | `Cancel order rejected by Locus: ` +EventInfo (2150) | `Error cancelling carton <order> - <carton> in locus` (2161) | ⚠ `Cnacel rejected by Locus: ` +EventInfo (2162) | `OrderJobResult cancel order successful for job id ` +JobId (2166) | `OrderJobResult cancel reject already processed for job id ` +JobId (2132) |

Three traps in that table worth naming, because each one is exactly what analogy gets wrong:

- Only `reject` prefixes its audit comment with `OrderJobResult`. The other three start
  `Hold rejected` / `Hold release rejected` / `Cancel order rejected`.
- `cancelreject`'s success string says **"cancel order successful"** — no "reject" in it at
  all — while its idempotency string says **"cancel reject already processed"**. Two
  different nouns for the same event, in the same method.
- `holdreject`'s email subject is "putting … **on hold**", not "holding".

### ⚠ Divergence 1: `holdreject` does not hold the job

`reject` (1832), `holdreleasereject` (2002) and `cancelreject` (2124) all call
`holdOrderJob` on the not-found path. `holdreject` does not — line 1912:

```abl
//holdOrderJob(cJobID, string(lcResponse)). Removed to prevent hold loops
```

This one is deliberate and correct: holding a job because a *hold* was rejected asks Locus
to hold it again, which fails, which holds it again. Preserve the asymmetry.

### ⚠ Divergence 2: `cancelreject`'s email body is misspelt

Line 2162 reads `"Cnacel rejected by Locus: "`. It is the only handler in the family whose
`cBody` differs from its `transactions.comments`, and it differs only by a typo. Reproduce
it — it lands in operators' alert mailboxes and a mail rule may match on it. Fixing it is a
one-line change once someone confirms nothing filters on the misspelling.

### ⚠ Defect: `holdreleasereject` returns the wrong message

Line 2044 returns:

```
OrderJobResult hold complete successful for job id <JobId>
```

That is `holdcomplete`'s message, not a hold-release-reject message. A copy-paste bug —
and note it reports **success** for a **rejection**. Anything downstream parsing these
strings sees a hold-release rejection as a hold completion.

**Reproduce it**, flag it with a `⚠ ABL DEFECT` comment, and raise it as a question for the
captured-traffic review. It is the most consequential of the four string defects because
it inverts the meaning.

### Note on `sendMail`

The reject family sends alert email. `sendMail` is a private method on `locusAPI.cls`;
`putawayreject` reaches `rf/send_mail2.p`. **Do not convert this inline** — introduce an
`INotificationService` in Contracts with a stub implementation, and register it the way
`NotImplementedLocusClient` is registered. Email is a platform concern, not a handler one.

---

## Group 3 — Tote handling. Convertible, with one dependency.

| Handler | ABL lines | Tables | Audit | Notes |
|---|---|---|---|---|
| `toteinduct` | 2170-2346 | `cartonmst`, `pick`, `transactions` | `JT` | 2 transactions, 2 exclusive locks |
| `totemove` | 2347-2468 | `cartonmst`, `pick`, `transactions` | — | 2 exclusive locks, no explicit transaction block |
| `toteinductcancel` | 2469-2543 | `cartonmst`, `pick`, `transactions` | — | 2 exclusive locks |

Responses:

- `toteinduct` → `OrderJobResult tote induct successful for job id ` + JobId;
  failure paths `Unable to find carton matching job id <id>` and `Unable to induct tote <id>`
- `totemove` → `OrderJobResult tote move successful for job id ` + JobId
- `toteinductcancel` → `OrderJobResult tote induct cancel successful for job id ` + JobId

**Before converting these**, note that `totemove` and `toteinductcancel` take exclusive
locks with **no `DO TRANSACTION` block**. That means ABL's implicit outermost-updating-block
rule is setting the transaction boundary. Phase 2 §3 flags exactly this: most ABL
transaction boundaries are implicit and each must be re-derived deliberately rather than
inherited. Read the surrounding block structure before choosing where `BeginAsync` goes —
do not assume it wraps the whole method.

`toteinduct` also touches `pick`, which is not yet modelled. You will need a `Pick` domain
record — 14 fields, listed in `docs/SCHEMA_REVIEW.md`.

---

## Group 4 — Deferred. Do not attempt.

These reach out of the module into the warehouse core. See `CLAUDE.md` for why this is a
hard boundary and not a difficulty to work around.

| Handler | ABL lines | Reaches |
|---|---|---|
| `pick` | 2544-3077 (534) | `wms_webhost/wspick.p`, `rf/wsset_cyc.p` |
| `pickcomplete` | 3078-3187 | same persistent handle |
| `replenputcomplete` | 3242-3588 (347) | `rf/wsset_cyc.p`; writes `lpn`, `movemst`; **6 exclusive locks** |
| `putawayput` | 3589-3894 (306) | `rf/wsset_cyc.p`; writes `inventory` |
| `putawayreject` | 3895-4486 (592) | `comm/wms_common.p` (remote AppServer), `rf/send_mail2.p`, `rf/wsset_cyc.p` |
| `putawayputcomplete` | 4487-4494 | delegates straight to `replenputcomplete` |

`replenputcomplete` and `putawayput` also write `inventory` and `movemst` — tables owned by
B03 Inventory & Locations. That is one of the eight largest seams in the Phase 6 register
and needs an ownership decision before conversion, separate from the `wsset_cyc.p` problem.

---

## Group 5 — Special cases

### `putawayrequest` (3210-3241) — convertible, but it calls outbound

Parses `LicensePlate`, `RequestDate`, `RequestRobot`, `RequestUser`, then branches on the
same first-character rule as the router:

- First character **is not** an integer → tote → `locusCreatePutawayJob(plate, robot)`
- First character **is** an integer → SSCC-18 → `createPutawayJob(plate, robot)`

Both are outbound Locus calls on `ILocusClient`, currently `NotImplementedLocusClient`. So
this handler can be written now and will return its response correctly, but the outbound
call fails until Slice 3. That is acceptable and visible — the stub logs an error.

Response: `PutawayJobRequest for SSCC18 <plate> ` — note the **trailing space**, and that
it says "SSCC18" even on the tote branch.

### `putawayaccept` (4495-4502) — trivial

Response: `PutawayJobResult accept successful`. No job id, no payload parsing. Four lines.

### `putawayputinduct` (4503+) — read it first

The extraction could not cleanly bound this method. Read it directly before starting.

### `testtotemove` (3188-3209) — **do not implement**

A test hook that reflection dispatch made reachable from production traffic. Deliberately
unregistered; `CompositionTests` asserts it stays that way.

---

## Suggested order

1. **Group 1** — five handlers, near-free, gets the pattern established.
2. **`reject`** — then the other three reject-family handlers, which are copies.
3. **`putawayrequest`** and **`putawayaccept`** — self-contained.
4. **Group 3** — needs a `Pick` model and careful transaction-boundary work.
5. **Group 4** — not until picking and shipping convert.

After Group 1 and 2 you will have **13 of 22** handlers done, which is every event that
does not touch inventory or picking.

---

## Running total of ABL defects found

Five so far, none fixed without evidence:

1. `tofdm4.cls:229` — `Accept` header never sent (missing comma). Wire-visible to FDM4.
2. `tofdm4.cls:247` — half the configured retries happen (double-incremented loop counter).
3. `locusAPI.cls:2044` — `holdreleasereject` returns `holdcomplete`'s success message.
   Wire-visible to Locus, and it inverts the meaning.
4. `locusAPI.cls` 1881, 2060, 2076, 2092 — four handlers build `"for job id "` and never
   append the id. Wire-visible to Locus.
5. `locusAPI.cls:2162` — `cancelreject`'s alert email body reads `"Cnacel rejected by
   Locus: "`. Not on the wire; visible to whoever reads the alert mailbox.

Also recorded, but **not** a defect — a deliberate asymmetry that must be preserved:
`holdreject` alone does not call `holdOrderJob` on the not-found path (`locusAPI.cls:1912`,
commented out to prevent hold loops).

Add to this list as you find more. It is the most useful artefact this conversion produces
for the people still running the ABL system.
