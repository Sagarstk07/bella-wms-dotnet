# Database trigger rules

All 40 triggers in `trigger/`, read line by line. Phase 6 §5 requires every one to become
explicit application logic, because SQL Server triggers are the wrong destination and
because a trigger's behaviour is invisible to anyone reading the calling code.

> **Headline: the scope is far smaller than every earlier phase assumed.** Phase 2 called
> these "forty database triggers holding real business rules" and Phase 6 built its
> argument around `w_invent.t` maintaining item totals. Read directly, **three triggers
> hold real logic**, twenty-nine are one repeated pattern, and eight are empty or disabled.

---

## Summary

| Category | Count | Effort |
|---|---:|---|
| Real business logic | 3 | Needs careful conversion |
| Sequence-assignment pattern (identical) | 25 | One shared implementation + a lookup table |
| Field-format validators | 2 | Two small validators |
| **Empty or compiled out** | **8** | **Nothing to convert** |
| Debug logging only | 1 | Decide whether to keep |
| **Total** | **40** | |

Of 1,840 lines, **330 are compiled out** and roughly 700 more are the same 12-line
sequence pattern repeated.

---

## ⚠ The correction: the inventory triggers do nothing

Every earlier phase names `w_invent.t` (323 lines) and `d_invent.t` (134 lines) as the
biggest and most critical triggers. `trigger/AGENTS.md` describes them as maintaining item
totals. **Both were disabled in August 1996** by the same developer on the same day.

### `d_invent.t` — DELETE OF inventory — **complete no-op**

Line 51 opens `&If 0 &Then` and there is **no `&Endif`**, so the preprocessor discards
everything to end of file. The trigger declares a variable and returns. It has no effect
whatsoever.

`AGENTS.md` says it "recalculates item totals on removal." It does not.

### `w_invent.t` — WRITE OF inventory — **one line of live behaviour**

Line 77 opens `&If false &Then`, again with no `&Endif`. 246 of 323 lines are discarded.
The complete live behaviour is:

```abl
do transaction:
    newinv.bin_num = upper(newinv.bin_num).
end.
```

**It uppercases the bin number. That is all.** The item-total maintenance sits inside the
dead block, above a comment reading *"28aug96 14:43 glauber — This code is not needed
anymore."*

### Why this matters

Phase 6 §5 argues: *"`w_invent.t` updating item totals whenever inventory changes is real
logic that no reader of the calling code can see."* That premise is false, and the
conclusion drawn from it — that trigger replacement is a large, high-risk workstream —
should be revised.

**The open question for the warehouse team:** if the triggers stopped maintaining
`item.alloc_qty`, `item.un_alloc_qty` and `item.reserved_qty` in 1996, what maintains them
now? Either application code took it over, or those totals have been drifting for thirty
years. Worth asking before anyone relies on them.

---

## The three that hold real logic

### 1. `w_pick.t` — WRITE OF pick (284 lines, all live)

The only genuinely complex trigger. A carton-status state machine.

**Guard conditions — it exits early unless all hold:**

1. An `ordhdr` exists matching the pick's company, warehouse, order and suffix.
2. A `carrier` exists for that order's carrier **and** `carrier.pm_irms = "CARRIER"`.

So this only fires for carrier-managed orders. Anything else returns immediately.

**Mode switch:** business rule **1104** (`syspar_value.parameter_id = 1104`). Non-`"0"`
means per-carton status; otherwise status is set when the whole order is picked. The two
branches are near-identical apart from scoping by `carton_id` versus `order` +
`order_suffix`.

**The state machine**, writing `ship_info.carton_status`:

| Pick status | Condition | New carton status |
|---|---|---|
| `P`, or `S` with qty 0 | no sibling pick is `O` or `I`, and this pick is `S`, and no sibling is non-`S` | `C` (complete) |
| `P`, or `S` with qty 0 | as above but this pick is not `S`, or a sibling is non-`S` | `P` (partial) |
| `O` | the previous status was `P` | `W` |
| `O` | a sibling with same item, qty 0, status `S` exists | `W` |

Takes `ship_info` **EXCLUSIVE-LOCK** inside `DO TRANSACTION`.

**Converting this:** it belongs to the picking module, not `api/`. It fires on every pick
write, so whichever service owns pick updates must call it. Note it reads `syspar_value`
on every invocation — cache within the request, never across.

### 2. `n_trans.t` — CREATE OF transactions (56 lines)

**This unblocks `AuditService` — schema review item 9 is now answered.**

```abl
tryloop: do while true:
    try = NEXT-VALUE( transactions_trans_num ).
    IF NOT CAN-FIND( transactions WHERE transactions.trans_num EQ try )
        THEN LEAVE tryloop.
end.
```

A database sequence with an unbounded collision-retry loop — the comment says *"if by any
chance the sequence gets out of sync, retry until we get a valid (unique) value."*

It also assigns three fields the .NET side currently gets wrong:

| Field | Value | Status in our code |
|---|---|---|
| `trans_num` | `NEXT-VALUE(transactions_trans_num)`, collision-checked | `AppendAsync` returns 0 — **now implementable** |
| `trans_sec_time` | `TIME` — raw seconds since midnight, to order transactions within the same minute | **not modelled at all** |
| `date_time` | **12-character string** `"YYYYMMDDHHMM"` | ⚠ **modelled as `DateTimeOffset` — wrong** |
| `proc_created` | `program-name(2)` — the calling program | **not modelled** |

**`date_time` is not a datetime.** It is built by slicing formatted strings:

```abl
transactions.date_time =
    SUBSTRING(sdate,7,4) + SUBSTRING(sdate,1,2) + SUBSTRING(sdate,4,2) +
    SUBSTRING(stime,1,2) + SUBSTRING(stime,4,2)     /* "199412131415" */
```

Minute precision only — which is why `trans_sec_time` exists separately, "to arbitrate
which transaction happened first."

**Three fixes needed in `AuditTransaction`:** change `DateTime` to a string in this format,
add `TransSecTime`, add `ProcCreated`.

### 3. `w_shipinfo.t` — WRITE OF ship_info (59 lines) — diagnostics, not logic

Despite Phase 2 listing it as 60 lines of business rules, it writes a tab-separated audit
line to `<program dir>/monitor/ship_info_trig.log` whenever `carton_status` changes —
capturing old and new status plus three levels of `program-name()` call stack.

**It is self-disabling:** `if search(v-log-path) eq ? then leave`. No log file, no logging.
Someone left a debugging aid in place.

Worth keeping as structured logging on the carton-status transition — that call-stack
capture is genuinely useful for tracing who changed a status — but it is not a business
rule and must not be treated as one.

---

## The 25 sequence-assignment triggers — one pattern

Every CREATE trigger except `n_param.t` is the same twelve lines:

```abl
tryloop: do while true:
    try = NEXT-VALUE( <sequence> ).
    IF NOT CAN-FIND( <table> WHERE <table>.<idfield> EQ try ) THEN LEAVE tryloop.
end.
ASSIGN <table>.<idfield> = try.
```

One `ISequenceAllocator` with this table covers all of them:

| Trigger | Table | Sequence | ID field | Also assigns |
|---|---|---|---|---|
| `n_access.t` | `assessorial` | `assessorial_id` | `id` | |
| `n_alloc.t` | `item_alloc` | `item_alloc_id` | `id` | `date_time` (string) |
| `n_carton.t` | `cartonmst` | `cartonmst_carton_num` | `carton_num` | |
| `n_comm_in.t` | `comm_in` | `comm_id_seq` | `comm_id` | `create_date`, `create_time` |
| `n_comm_out.t` | `comm_out` | `comm_id_seq` | `comm_id` | `create_date`, `create_time` |
| `n_commen.t` | `comment` | `comment_comment_num` | `comment_num` | |
| `n_containc.t` | `container` | `container_id` | `container_id` | |
| `n_custad.t` | `custaddr` | `custaddr_code` | `custaddr_code` | |
| `n_cycle.t` | `cycle_cnt` | `cycle_cnt_id` | `id` | |
| `n_invent.t` | `inventory` | `inventory_id` | `id` | |
| `n_invprb.t` | `inv_prob` | `inv_prob_id` | `inv_prob_id` | |
| `n_itemhi.t` | `item_history` | `item_history_id` | `rec_id` | |
| `n_itmldg.t` | `wms_item_ledger` | `wms_item_ledger_id` | `id` | `trans_date`, `trans_time`, `proc_created` |
| `n_kit.t` | `kitmst` | `kit_id` | `id` | |
| `n_maphdr.t` | `maphdr` | `maphdr_id` | `id` | |
| `n_move.t` | `movemst` | `movemst_id` | `id` | |
| `n_ordhdr.t` | `ordhdr` | `ordhdr_id` | `id` | |
| `n_pallet.t` | `palletmst` | `palletmst_pallet_num` | `pallet_num` | |
| `n_pick.t` | `pick` | `pick_id` | `id` | `date_time` (string) |
| `n_pomst.t` | `pomst` | `pomst_po_id` | — | |
| `n_pstage.t` | `prod_stg_mst` | `prod_stg_id` | `id` | |
| `n_rtmst.t` | `rtmst` | `rtmst_rt_id` | `rt_id` | |
| `n_rtnctn.t` | `rtn_ctn_mst` | `rtn_ctn_num` | `carton_num` | |
| `n_shpmst.t` | `shpmst` | `shpmst_manifest` | `manifest_id` | |
| `n_task.t` | `task` | `task_id` | `task_id` | `requested` (string) |
| `n_trans.t` | `transactions` | `transactions_trans_num` | `trans_num` | see above |
| `n_wave.t` | `wave` | `pick_batch` | `batch` | `drop_date_time` (string) |

**`n_comm_in.t` and `n_comm_out.t` share one sequence** (`comm_id_seq`), so comm ids are
unique across both tables. Anything assuming per-table uniqueness is wrong.

**Five triggers write the same `"YYYYMMDDHHMM"` string format** as `n_trans.t`:
`item_alloc.date_time`, `pick.date_time`, `wave.drop_date_time`, `task.requested`, and
`wms_item_ledger.trans_date`/`trans_time`. Build one formatter and reuse it.

**On the retry loop:** it exists because the sequence can drift out of step with the data.
Keep an equivalent guard — a unique-constraint violation with retry — rather than trusting
the sequence blindly. And keep it bounded; the ABL loop has no maximum iteration count.

---

## The two field validators

Both are ABL `ASSIGN` triggers — they fire on field assignment, not on record write, and
they **reject** bad values with `RETURN ERROR`.

**`datetim.t`** validates the 12-character `"YYYYMMDDHHMM"` format: parses the date part,
requires hour `00`-`23` and minute `00`-`59`. Empty and unknown pass through untouched.

**`time.t`** validates a 4-character `"HHMM"` string: exactly 4 characters, hour `00`-`23`,
minute `00`-`59`.

These become validation attributes or a value type on the string-formatted date fields.
They are the reason those fields can be trusted to parse.

---

## The eight that do nothing

Declare a trigger, define an unused `SCCS_ID` variable, return.

| Trigger | Table | Event | `AGENTS.md` claim |
|---|---|---|---|
| `d_invent.t` | `inventory` | DELETE | "Recalculates item totals" — **false** |
| `w_binmst.t` | `binmst` | WRITE | listed as "trigger-audited" — **false** |
| `w_trans.t` | `transactions` | WRITE | — |
| `w_lpn.t` | `lpn` | WRITE | — |
| `w_param.t` | `parameters` | WRITE | — |
| `d_param.t` | `parameters` | DELETE | — |
| `n_param.t` | `parameters` | CREATE | — |
| `w_invent.t` | `inventory` | WRITE | "Updates item totals" — **only uppercases `bin_num`** |

Nothing to convert. Record the finding so nobody budgets for them.

---

## What to do

1. **Fix `AuditTransaction` now.** `date_time` is the wrong type and two fields are
   missing. This affects code already written. Schema review item 9 is answered.
2. **Build `ISequenceAllocator`** with the table above. One implementation, 25 triggers.
3. **Build the `"YYYYMMDDHHMM"` formatter and its validator.** Six triggers depend on it.
4. **Leave `w_pick.t` to the picking module.** Specified above; convert it there.
5. **Ask about item totals.** If nothing has maintained them since 1996, that is a data
   question for the warehouse team, not a conversion task.
6. **Revise the Phase 6 §5 estimate.** The premise it rests on is false and the workstream
   is much smaller than planned.

---

## Running total of ABL findings

| # | Where | What |
|---|---|---|
| 1 | `tofdm4.cls:229` | `Accept` header never sent — missing comma |
| 2 | `tofdm4.cls:247` | Half the configured retries happen — double-incremented counter |
| 3 | `locusAPI.cls:2045` | `holdreleasereject` returns `holdcomplete`'s success message |
| 4 | `locusAPI.cls` 1881, 2060, 2076, 2092 | Four handlers build `"for job id "` and never append the id |
| 5 | `w_invent.t:77` | 246 of 323 lines compiled out since 1996; only uppercases `bin_num` |
| 6 | `d_invent.t:51` | Entirely compiled out — a complete no-op |
| 7 | 6 further triggers | Empty stubs, documented as doing work they do not do |
| 8 | `trigger/AGENTS.md` | Describes behaviour for triggers that have none |
