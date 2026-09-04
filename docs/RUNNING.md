# Running it

> **Verified 2026-09-01.** Everything on this page has now actually been executed. Until
> that date it described a mode that did not exist: `Bella.Wms.Platform.Fakes` was written
> but referenced by nothing, the host never called `AddWmsFakes`, and `/health` was never
> mapped. See the "What the first real build found" section of `README.md`.

The API host runs with no database, using in-memory fake data. This proves the pipeline —
HTTP in, authentication, ambient context, routing, handler, audit write, response out —
without needing the schema.

**Nothing it returns is real.** The warehouse is three employees, one carton and two
business rules, held in memory and thrown away when the process stops.

---

## Run the tests

From the solution root:

```
dotnet test
```

The suite that matters is `LocusEndpointTests` — it hosts the real API in-process and
posts real Locus payloads through it. If those pass, the plumbing works.

## Start the API

```
dotnet run --project src/Hosts/Bella.Wms.Api
```

You should see, near the top of the log:

```
warn: Startup
      RUNNING WITH IN-MEMORY FAKE DATA. No database is connected and no result from this
      host is real.
info: Startup
      Inbound partner routes (22): locus:ACCEPT, locus:CANCELCOMPLETE, ...
info: Startup
      ERP outbound routes (5): OMS:IRCODT, OMS:IRORUP, OMS:IRPIUP, OMS:IRPMUP, OMS:IRSHUP
```

That route listing is worth pausing on. **The ABL had no route table at all** — the
effective API surface was every public method on `locusAPI.cls`, discoverable only by
reading the file. This is the first time the accepted event list has been written down
anywhere.

Note the port in the startup output — Kestrel picks it from `launchSettings.json` or
defaults to 5000/5001.

## Send it a request

Health first:

```powershell
curl http://localhost:5000/health
```

```json
{"status":"ok","fakeData":true,"environment":"Development"}
```

Then a real Locus ACCEPT for the seeded carton:

```powershell
curl -X POST http://localhost:5000/api/locus `
  -H "AUTHENTICATION: dev-locus-key-not-a-real-secret" `
  -H "HTTP_COMPANY: ALO" `
  -H "HTTP_WAREHOUSE: AV" `
  -H "Content-Type: application/json" `
  -d '{\"OrderJobResult\":{\"EventType\":\"ACCEPT\",\"JobId\":\"C0001234\",\"JobStatus\":\"COMPLETED\"}}'
```

Expected response, byte-identical to what the ABL returns at `locusAPI.cls:1799`:

```
OrderJobResult accept successful for job id C0001234
```

That single call went through authentication, resolved the `LOCUS01` employee from the
fake `empmst`, established the ambient context that replaces `static_connect`, detected
the `OrderJobResult` envelope, resolved `ACCEPT` in the event registry, opened a unit of
work, wrote a `JA` audit row, and committed.

## Things worth trying

**An unknown carton** — returns **HTTP 200** with an error body, not a 4xx:

```powershell
-d '{\"OrderJobResult\":{\"EventType\":\"ACCEPT\",\"JobId\":\"NOSUCHCARTON\"}}'
```

```
Unable to find carton matching job id NOSUCHCARTON
```

That is deliberate. `locusAPI.cls:1774-1780` does the same, and Locus has been running
against it for years — its retry logic may depend on it. Do not "fix" this without
evidence from captured traffic.

**An unknown event** — also 200, with the ABL's exact wording:

```
Invalid Endpoint Action NOTAREALEVENT
```

**A bad key** — 401, `Invalid Authentication`. The header is `AUTHENTICATION`, not
`Authorization`; that is what Locus sends, so it is preserved.

**An event we haven't built yet** — `TOTEMOVE`, say — throws `NotImplementedException`
carrying the ABL line range to convert. That is intentional: a silent 200 would look to
Locus like the event was handled, rather than not yet built. `REJECT` and the rest of the
reject family are implemented now; the ten that still throw are the tote handlers, the
putaway/replen writers and the two picking events.

## The seeded data

| What | Value |
|---|---|
| Company / warehouse | `ALO` / `AV` |
| Carton | `C0001234`, order `SO12345-01`, bin `A-01-02-03` |
| API users | `LOCUS01`, `PYRAMID01`, `AWSAV` |
| Business rules | 7510 (interface API) = `yes`, 2013 (base directory) = `/tmp/bella` |

Change it in `InMemoryWarehouse.CreateSeeded()`.

## Why it cannot start on fake data by accident

`AddWmsFakes` throws unless **both** conditions hold: the host is in the Development
environment, *and* configuration sets `UseFakeData`. `appsettings.Development.json` sets
the flag; `appsettings.json` does not.

The guard is deliberately unforgiving. This is a warehouse system — a host that quietly
started against fake inventory would accept picks, write audit rows and answer Locus with
confident nonsense. Failing loudly at startup is much better than that, so it does.

## What this does not prove

- **No SQL runs.** Not one column name, type or index has been validated. That waits for
  the `.df` dumps.
- **The wire contracts are read from the ABL, not from Locus.** They are the best
  available specification, and they need checking against a week of captured traffic.
- **Twelve of twenty-two Locus handlers are implemented.** The other ten throw with their
  ABL reference attached. None of the ten can be finished without the `.df` dumps: they
  all write stock, or reach the picking engine.

Delete `Bella.Wms.Platform.Fakes` once integration tests run against a real database. A
fake that outlives its purpose accumulates features, and then people start writing tests
against the fake's behaviour instead of the database's.
