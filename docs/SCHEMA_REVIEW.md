# Schema review — VERIFIED

> **Status changed 2026-09-03.** The `.df` dumps for `irms` and `wmscomm` arrived. Every
> field in this solution has now been checked against them. This document used to list ten
> open inferences; it now records what the schema actually says, what was wrong, and what
> remains genuinely open.
>
> The dumps are committed at `schema/irms.df` and `schema/wmscomm.df`. **They are the
> source of truth.** If this document and the `.df` disagree, the `.df` wins.

## What arrived

| | `irms` | `wmscomm` |
|---|---|---|
| Tables | 158 | 2 |
| Fields | 2,431 | 41 |
| Indexes | 444 | 15 |
| Sequences | 55 | 2 |

**158 tables, not ~874.** The Phase 0 estimate counted every distinct identifier that
looked like a table reference — temp-tables, buffer names and aliases included. The real
relational surface is a fifth of that, which materially shrinks the schema work.

---

## Five defects the schema found

Every one of these would have failed on the first write against a real database. All are
now fixed.

| # | Where | Defect |
|---|---|---|
| 1 | `AuditService.InsertSql` | Wrote a column `comment`. **No such column** — the table has `comments` and `ns_comment`. `comment` is a separate *table* in `irms`. The confusion came from `locusAPI.cls:3021` assigning `xtrans.comment`, where `xtrans` is a buffer on something else. |
| 2 | `AuditService.InsertSql` | Omitted `item_type`, which is **MANDATORY** with `INITIAL "S"`. ABL `CREATE` applies schema defaults; a SQL `INSERT` does not. Every audit row would have violated the constraint. |
| 3 | `AuditService.InsertSql` | Bound `custom_data` as a scalar. It is **`EXTENT 5`** — a Progress array, surfacing over SQL-92 as `custom_data##1`..`##5`. Cannot be bound as one value. Dropped; nothing in `api/` sets it. |
| 4 | `CartonRepository.SelectColumns` | Selected `carrier`. **No such column on `cartonmst`** — only `carrier_id`. `carrier` is a separate table. Every carton read would have failed. |
| 5 | `AuditTransaction` | `TransactionNumber` modelled as `long`; `trans_num` is **`integer`** (32-bit). `CaseQuantity` modelled as `decimal`; `case_qty` is **`integer`**. |

---

## The systemic finding: 795 defaults a SQL INSERT will not apply

**121 tables carry 795 fields with a non-blank `INITIAL` value.** ABL `CREATE` applies
those defaults automatically. A SQL `INSERT` does not — the column simply comes out blank
or null, and the ABL side then reads a row it considers malformed.

On the three tables `api/` writes:

| Table | Fields with defaults | The ones that matter |
|---|---|---|
| `transactions` | 7 | `row_status` = `O`, `item_type` = `S`, `void` = `no`, `trans_sec_time` = `0` |
| `cartonmst` | 9 | `row_status` = `O`, `full` = `no`, `carton_num` = `0` |
| `pick` | 14 | `pick_status` = `O`, `wh_zone` = `P`, `line` = `1`, `aisle` = `1`, `allow_pick` = `yes` |

`row_status` defaulting to `O` also confirms the carton lifecycle the reject family
depends on: **`O` open → `C` complete / `E` error.** The idempotency guard tests for
`C` or `E`; `O` is where a carton starts.

**Every `INSERT` in this solution must be checked against this list before it runs.**

---

## The discovery: 12 field-level triggers that exist only in the schema

`docs/TRIGGER_RULES.md` documents 40 *table* triggers. The `.df` reveals a second layer:
**12 field-level triggers**, attached to individual columns. They appear in no `.p` file
and nothing in the code tells you they exist.

| Table | Column | Trigger |
|---|---|---|
| `transactions` | `date_time` | `datetim.t` |
| `inventory` | `date_time` | `datetim.t` |
| `pick` | `date_time` | `datetim.t` |
| `palletdet` | `date_time` | `datetim.t` |
| `shpmst` | `date_time` | `datetim.t` |
| `cycle_cnt` | `started`, `requested` | `datetim.t` |
| `file_retent` | `timestamp` | `datetim.t` |
| `stntbl` | `last_activation` | `datetim.t` |
| `parameters` | `pic_release_time` | `time.t` |
| `shfmst` | `time_start`, `time_end` | `time.t` |

### `datetim.t` turns an inference into an enforced contract

It validates the string positionally — year 1–4, month 5–6, day 7–8, hour 9–10 (`00`–`23`),
minute 11–12 (`00`–`59`) — and does `RETURN ERROR` on anything else. Empty and unknown pass.

So the `"yyyyMMddHHmm"` format in `AuditTransaction` is not a lucky guess that happens to
match `n_trans.t`. **It is the only form the database will accept.** Write anything else
and the row is rejected, not silently stored.

---

## Open items — what actually remains

| # | Item | Status |
|---|---|---|
| 1 | Column names and types | **CLOSED.** Verified against the dumps. |
| 2 | Field widths and precision | **CLOSED.** Note that `FORMAT` is display-only; `MAX-WIDTH` is the real limit (`transactions.comments` shows `x(50)` but stores 2,854). |
| 3 | Index structure and real keys | **CLOSED.** See the per-table reference below. |
| 4 | ODBC named vs positional parameter binding | **STILL OPEN.** Needs one query against a real connection. Half a day of DBA time. Nothing here has run against a database yet. |
| 5 | Extent (array) field handling | **OPEN, now scoped.** Every table checked carries a five-slot `custom_data` array. Nothing in `api/` needs it, but the access pattern must be settled before any module that does. |
| 6 | CLOB handling | **NEW.** `comm_out.event_data` and `comm_out.process_response` are `clob`. ODBC CLOB binding needs its own decision — this is the ERP module's payload column. |
| 7 | `origin_stack` source marker | **CONFIRMED ABSENT.** The Phase 6 §9 column does not exist on `transactions`. It must be added, or an existing free-text column agreed for the purpose. |
| 8 | `empmst.row_status` type clash | **NEW.** `row_status` is `character X` on `transactions` and `cartonmst`, but **`logical`** on `empmst`. The same column name is not the same type across this schema. |
| 9 | `trans_num` assignment | **STILL OPEN, now unavoidable.** `integer`, `MANDATORY`, `INITIAL ?`, UNIQUE index. There is no default, so an INSERT that omits it fails. `n_trans.t` takes the next sequence value and retries on collision; the `ISequenceAllocator` work must be done before the first real write. |
| 11 | **Character comparison is case-insensitive in ABL and case-sensitive in SQL** | **NEW, and it affects every query in the solution.** Progress compares character fields ignoring case; **exactly 1 of the 2,431 fields in `irms.df` is declared `CASE-SENSITIVE`.** SQL-92 does not ignore case. This is not theoretical: `pick_status` has `INITIAL "O"` and is queried as `"o"` in six places across four modules, `locusAPI.cls:2300` among them. A `WHERE status = 'o'` over ODBC would match nothing while the ABL matches everything. Fixed in `CartonRepository` and the reject family; **every future query needs the same treatment.** |
| 12 | **The tote handlers write to an extent field** | `locusAPI.cls:2309-2311` assigns `pick.custom_data[4]` and `[5]` — the robot id and tote id. Open item 5 stops being theoretical the moment `toteinduct` is converted: it needs `custom_data##4` and `custom_data##5` addressed individually. |
| 13 | **`virtual_lock` is keyed on a ROWID string** | **NEW, 2026-09-04.** `replenputcomplete` releases a movemst lock by looking up `virtual_lock.record_id EQ STRING(ROWID(bmovemst))` (`locusAPI.cls:3324`). SQL-92 exposes a `ROWID` pseudo-column, so the value *can* be read — but whether it renders to the same string ABL produces is unverified. **If the forms differ the lookup finds nothing, and "no lock row" is indistinguishable from "not locked",** so .NET would delete a movemst row an RF operator is holding. Test this before anything else on the putaway path. |
| 14 | **SQL width vs ABL field width** | **NEW, 2026-09-04, and possibly the largest unknown left.** ABL character fields have no storage limit — `FORMAT` is a display mask. OpenEdge SQL-92 gives each character column a *SQL width* derived from that format and errors rather than truncating when a stored value exceeds it. `virtual_lock.record_id` is `x(8)` and holds ROWID strings; `virtual_lock.locked_by` is `x(8)` and holds ABL file names like `wms_webhost/wsprd_sgde.i`; `transactions.comments` is `x(50)` and stores 2,854 characters (open item 2). If the widths were never adjusted, **whole tables are unreadable over ODBC regardless of how correct the C# is,** and the fix is `dbtool`, not code. One `SELECT` against a real database settles it for every table at once. |
| 15 | **`SELECT … FOR UPDATE` and lock waits** | **NEW, 2026-09-04.** ABL's `EXCLUSIVE-LOCK` becomes `FOR UPDATE`, and `FIND … NO-WAIT` + `LOCKED(buffer)` has no SQL equivalent at all — `VirtualLockRepository` approximates it by treating a lock-wait failure as "held". Needs three answers: does the driver accept `FOR UPDATE`, is the lock wait settable per statement, and what SQLSTATE does a timeout report so the `catch` can stop being over-broad. |
| 10 | Employee lookup is unindexed | **NEW.** `empmst`'s only unique index is on `emp_num` alone — not scoped by company or warehouse. The ABL's `find first empmst where co_num = … and wh_num = … and emp_num begins "LOCUS"` is a table scan. It also means employee numbers are globally unique, so the company/warehouse filter is a safety check rather than part of the key. |

---

## Per-table reference

The tables `api/` reads or writes. Generated from the dumps; regenerate rather than edit
by hand.


### `transactions` — 64 columns

Written by `AuditService`. The `n_trans.t` CREATE trigger supplies `trans_num`, `date_time`, `trans_sec_time` and `proc_created` — all four become application logic.

| column | type | format | notes |
|---|---|---|---|
| `abs_num` | character | `x(24)` |  |
| `action_code` | character | `x(8)` |  |
| `adj_code` | character | `x(6)` |  |
| `batch` | integer | `>,>>>,>>9` |  |
| `bin_from` | character | `x(10)` |  |
| `bin_num` | character | `x(10)` |  |
| `bin_to` | character | `x(10)` |  |
| `cancelled` | logical | `yes/no` | default `no` |
| `cancelled_at` | character | `9999-99-99 99:99` |  |
| `cancelled_by` | character | `x(6)` |  |
| `cargo_control` | character | `x(24)` |  |
| `carton_id` | character | `x(17)` |  |
| `case_qty` | integer | `>>,>>9` |  |
| `cc_string` | character | `x(24)` |  |
| `cc_type` | character | `X(8)` |  |
| `co_num` | character | `x(4)` |  |
| `comm_link_id` | int64 | `>,>>>,>>>,>>>,>>>,>>>,>>9` | default `0` |
| `comments` | character | `x(50)` |  |
| `custom_data` | character | `X(50)` | **EXTENT 5 — array** |
| `date_time` | character | `9999-99-99 99:99` |  |
| `dept_num` | integer | `>>>>>9` |  |
| `doc_id` | character | `X(10)` |  |
| `emp_num` | character | `x(6)` |  |
| `exp_abs` | character | `x(24)` |  |
| `ifaces_file` | character | `X(20)` |  |
| `item_num` | character | `x(24)` |  |
| `item_qty` | decimal | `>,>>9.99` | 2 dp |
| `item_type` | character | `X` | **MANDATORY**, default `S` |
| `line_sequence` | integer | `>>>9` |  |
| `link_id` | character | `x(30)` |  |
| `lot` | character | `x(24)` |  |
| `lpn_scanid` | character | `x(24)` |  |
| `mach_type` | character | `X` |  |
| `ns_comment` | character | `X(30)` |  |
| `old_stock_stat` | character | `X` |  |
| `packer` | character | `X(14)` |  |
| `pallet_id` | character | `x(17)` |  |
| `pallet_id_from` | character | `x(17)` |  |
| `po_line` | integer | `>>>9` |  |
| `po_number` | character | `X(10)` |  |
| `po_suffix` | character | `X(2)` |  |
| `proc_created` | character | `x(50)` |  |
| `qa_release_id` | integer | `>>>,>>>,>>9` | default `0` |
| `rec_carton_id` | character | `x(32)` |  |
| `record_type` | character | `X(6)` |  |
| `release_id` | character | `x(24)` |  |
| `result_code` | character | `x(4)` |  |
| `result_msg` | character | `x(70)` |  |
| `row_status` | character | `X` | default `O` |
| `rt_num` | character | `x(18)` |  |
| `serial_num` | character | `x(20)` |  |
| `shf_num` | integer | `>>>>>9` |  |
| `stock_stat` | character | `X` |  |
| `sugg_qty` | decimal | `>,>>9.99` | 2 dp |
| `task_id` | integer | `>>>,>>>,>>9` |  |
| `trans_link` | integer | `>>>>>>>>9` |  |
| `trans_num` | integer | `>>>>>>>>9` | **MANDATORY** |
| `trans_sec_time` | integer | `>,>>>,>>9` | **MANDATORY**, default `0` |
| `trans_type` | character | `x(2)` | **MANDATORY** |
| `transmission` | integer | `>,>>>,>>9` |  |
| `truck_id` | character | `x(20)` |  |
| `uom` | character | `X(4)` |  |
| `void` | logical | `yes/no` | default `no` |
| `wh_num` | character | `x(4)` |  |

**Indexes**

- `co_wh_time` — **PRIMARY** · `co_num`, `wh_num`, `date_time`
- `cargocontrol` · `cargo_control`
- `co_wh_abs_time` · `co_num`, `wh_num`, `abs_num`, `date_time`
- `co_wh_bin_time` · `co_num`, `wh_num`, `bin_num`, `date_time`
- `co_wh_dept_type_time` · `co_num`, `wh_num`, `dept_num`, `trans_type`, `date_time`
- `co_wh_docid` · `co_num`, `wh_num`, `doc_id`
- `co_wh_emp_time` · `co_num`, `wh_num`, `emp_num`, `date_time`
- `co_wh_pallet_time` · `co_num`, `wh_num`, `pallet_id`, `date_time`
- `co_wh_po_suf_time` · `co_num`, `wh_num`, `po_number`, `po_suffix`, `date_time`
- `co_wh_rt_time` · `co_num`, `wh_num`, `rt_num`, `date_time`
- `co_wh_shf_type_time` · `co_num`, `wh_num`, `shf_num`, `trans_type`, `date_time`
- `co_wh_task_datetime` · `co_num`, `wh_num`, `task_id`, `date_time`
- `co_wh_trans_link_id` · `co_num`, `wh_num`, `trans_type`, `link_id`, `row_status`
- `co_wh_type_time` · `co_num`, `wh_num`, `trans_type`, `date_time`
- `lpn_search` · `lpn_scanid`
- `qarelease` · `qa_release_id`
- `releaseid` · `release_id`
- `status_chrono` · `row_status`, `date_time`, `trans_sec_time`
- `status_type_time` · `row_status`, `trans_type`, `date_time`
- `tlink` · `trans_link`
- `trans_num` — **UNIQUE** · `trans_num`
- `type_transmission` · `trans_type`, `transmission`

### `cartonmst` — 35 columns

Read and updated by `CartonRepository`. Note the primary key is `carton_num`, but the lookup `api/` uses is the `co_wh_id` unique index.

| column | type | format | notes |
|---|---|---|---|
| `batch` | integer | `>,>>>,>>9` |  |
| `bin_num` | character | `x(10)` |  |
| `box_id` | character | `X(8)` |  |
| `calculated_freight` | decimal | `->>,>>9.99` | default `0`, 2 dp |
| `carrier_id` | character | `x(6)` |  |
| `carton_id` | character | `x(18)` |  |
| `carton_num` | integer | `ZZZZZZZZZ9` | **MANDATORY**, default `0` |
| `co_num` | character | `x(4)` |  |
| `cod_amt` | decimal | `->>>,>>>,>>9.99` | default `0`, 2 dp |
| `cust_code` | character | `x(12)` |  |
| `custom_data` | character | `X(50)` | **EXTENT 5 — array** |
| `except_carrier_id` | character | `x(6)` |  |
| `except_service` | character | `x(10)` |  |
| `expected_delivery_date` | date | `99/99/9999` |  |
| `external_id` | character | `x(10)` |  |
| `external_message` | character | `x(78)` |  |
| `external_status` | character | `x(8)` |  |
| `full` | logical | `yes/no` | **MANDATORY**, default `no` |
| `height` | decimal | `>>9.99` | default `0`, 2 dp |
| `length` | decimal | `>>9.99` | default `0`, 2 dp |
| `order` | character | `x(12)` | **MANDATORY** |
| `order_suffix` | character | `X(2)` |  |
| `package_code` | character | `X` |  |
| `picker_emp_num` | character | `x(6)` |  |
| `pickup_status` | character | `x` |  |
| `print_form` | character | `x(10)` |  |
| `reference` | character | `x(10)` |  |
| `return_reason` | character | `X(4)` |  |
| `row_status` | character | `X` | **MANDATORY**, default `O` |
| `sequence` | integer | `>,>>>,>>9` |  |
| `tracking_id` | character | `X(30)` |  |
| `weight` | decimal | `>>>,>>>.99` | default `0`, 2 dp |
| `wh_num` | character | `x(4)` | **MANDATORY** |
| `width` | decimal | `>>9.99` | default `0`, 2 dp |
| `x_of_y` | character | `x(15)` |  |

**Indexes**

- `num` — **UNIQUE PRIMARY** · `carton_num`
- `co_wh_batch_order_suf_seq` · `co_num`, `wh_num`, `batch`, `order`, `order_suffix`, `sequence`
- `co_wh_batch_sequence` · `co_num`, `wh_num`, `batch`, `sequence`
- `co_wh_carrier` · `co_num`, `wh_num`, `carrier_id`
- `co_wh_cust_carrier` · `co_num`, `wh_num`, `cust_code`, `carrier_id`
- `co_wh_extstat` · `co_num`, `wh_num`, `external_status`
- `co_wh_id` — **UNIQUE** · `co_num`, `wh_num`, `carton_id`
- `co_wh_order_suffix` · `co_num`, `wh_num`, `order`, `order_suffix`
- `co_wh_reference` · `co_num`, `wh_num`, `reference`
- `co_wh_status` · `co_num`, `wh_num`, `row_status`
- `co_wh_tracking` · `co_num`, `wh_num`, `tracking_id`

### `syspar_value` — 5 columns

Business-rule values, read by `BusinessRuleService`.

| column | type | format | notes |
|---|---|---|---|
| `co_num` | character | `x(4)` |  |
| `custom_data` | character | `X(50)` | **EXTENT 5 — array** |
| `parameter_id` | integer | `>,>>>,>>9` | **MANDATORY**, default `0` |
| `parameter_value` | character | `X(80)` |  |
| `wh_num` | character | `x(4)` |  |

**Indexes**

- `id_co_wh` — **UNIQUE PRIMARY** · `parameter_id`, `co_num`, `wh_num`

### `comm_out` — 21 columns

The outbound ERP queue. Two CLOB columns — see open item 6.

| column | type | format | notes |
|---|---|---|---|
| `co_num` | character | `x(4)` |  |
| `comm_endpoint` | character | `x(10)` |  |
| `comm_id` | int64 | `>>>>>>>>>>>>>>>>>>9` | default `0` |
| `create_date` | date | `99/99/99` |  |
| `create_time` | integer | `>>>>>>>>>9` |  |
| `event_data` | clob | `x(8)` |  |
| `event_id` | character | `x(24)` |  |
| `event_type` | character | `x(15)` |  |
| `process_date` | date | `99/99/99` |  |
| `process_duration` | integer | `>>>>>>>>>9` | default `0` |
| `process_response` | clob | `x(8)` |  |
| `process_response_status` | character | `x(10)` |  |
| `process_status` | character | `x(1)` |  |
| `process_time` | integer | `>>>>>>>>>9` |  |
| `ref_comm_id` | int64 | `>>>>>>>>>>>>>>>>>>9` | default `0` |
| `ref_id` | character | `x(24)` |  |
| `ref_id_type` | character | `x(10)` |  |
| `ref_trans_num` | integer | `>>>>>>>>9` | default `0` |
| `retry_count` | integer | `>,>>>,>>9` | default `0` |
| `transmission_id` | int64 | `>,>>>,>>>,>>>,>>>,>>>,>>9` | default `0` |
| `wh_num` | character | `x(4)` |  |

**Indexes**

- `id` — **UNIQUE PRIMARY** · `comm_id`
- `co-wh-date-time` · `co_num`, `wh_num`, `create_date`, `create_time`
- `co-wh-end-type-id` · `co_num`, `wh_num`, `comm_endpoint`, `event_type`, `event_id`
- `co-wh-end-type-stat-pdate-ptime` · `co_num`, `wh_num`, `comm_endpoint`, `event_type`, `process_status`, `process_date`, `process_time`
- `co-wh-ref-id` · `co_num`, `wh_num`, `ref_id_type`, `ref_id`
- `co-wh-stat-end-type-date-time` · `co_num`, `wh_num`, `process_status`, `comm_endpoint`, `event_type`, `create_date`, `create_time`
- `stat-end-id` · `process_status`, `comm_endpoint`, `comm_id`
- `trans-id` · `transmission_id`
