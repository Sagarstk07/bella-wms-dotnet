# ABL → .NET cross-reference

Every file in `api/wms` and where it went. Generated from the source tree, not from memory.

| Status | Files | ABL lines | Meaning |
|---|---:|---:|---|
| **CONVERTED** | 16 | 2,759 | Fully converted; behaviour preserved or difference documented |
| **PARTIAL** | 5 | 8,436 | Partly converted; the rest is scaffolded with ABL references |
| **REPLACED** | 11 | 1,444 | Capability provided by a platform component rather than a translation |
| **DELETED** | 12 | 975 | No .NET equivalent needed — the runtime or framework supplies it |
| **TODO** | 21 | 8,708 | Scaffolded, not written. Blocked on schema, captured traffic, or effort |
| **DEFERRED** | 1 | 1,305 | Blocked on another module converting first |
| **QUESTION** | 6 | 1,279 | Needs a decision from the team before it can be dispositioned |
| | **72** | **24,906** | |

---

## File by file

| ABL file | Lines | Status | .NET target | Note |
|---|---:|---|---|---|
| `accutechAutoBagger.i` | 39 | CONVERTED | `Partners.Domain/PartnerEventTypes.cs` | Status constants. TWO EXCEED STATED x(8) WIDTH - see schema review. |
| `interface/IWMSCommInProcessor.cls` | 33 | CONVERTED | `Erp.Contracts/ICommInProcessor.cs` |  |
| `interface/IWMSCommOutProcessor.cls` | 25 | CONVERTED | `Erp.Contracts/ICommOutProcessor.cs` |  |
| `interface/WMSCommInProcess.cls` | 82 | CONVERTED | `Erp.Application/CommProcessorRegistry.cs` |  |
| `interface/WMSCommOutProcess.cls` | 102 | CONVERTED | `Erp.Application/CommProcessorRegistry.cs` | CASE -> DI registration. Duplicate registration now throws at startup. |
| `interface/WMSCommOutProcessorBase.cls` | 205 | CONVERTED | `Erp.Application/CommOutProcessorBase.cs` | Template method; cURL setup removed. |
| `interface/base.cls` | 216 | CONVERTED | `Erp.Application/Fdm4Sender.cs + Platform.Config` | Config/logging/cURL split across three .NET concerns. |
| `interface/comm_in_result.cls` | 181 | CONVERTED | `Erp.Contracts/CommInResult` |  |
| `interface/config.cls` | 354 | CONVERTED | `Erp.Application/Fdm4Options` | JSON file -> IOptions. Secrets no longer in source. |
| `interface/oms_comm_out_route.p` | 386 | CONVERTED | `Erp.Application/CommOutRouter.cs + Jobs/CommOutWorker.cs` | 3-transaction claim/process/record preserved exactly. |
| `interface/oms_ircodt.cls` | 91 | CONVERTED | `Erp.Application/Processors/OrderDownloadAckProcessor` | Pass-through in ABL; genuinely complete. |
| `interface/oms_irorup.cls` | 174 | CONVERTED | `Erp.Application/Processors/OrderDropUpdateProcessor` | Pass-through in ABL; genuinely complete. |
| `interface/tofdm4.cls` | 510 | CONVERTED | `Erp.Application/Fdm4Sender.cs` | TWO ABL DEFECTS FIXED - see notes below. |
| `middleware.i` | 17 | CONVERTED | `Partners.Domain/PartnerEventTypes.cs` | INBOUND_API_KEY64 removed from source. |
| `rest/clobber.i` | 28 | CONVERTED | `Erp.Domain/CommStatus.cs` | Status vocabulary. |
| `wsLocusAPI.p` | 316 | CONVERTED | `Partners.Application/Locus/LocusEventRouter.cs + Api/Endpoints` | dynamic-invoke -> explicit registry. Hardcoded key removed. |
| `accutechAutoBaggerOut.cls` | 1977 | PARTIAL | `Partners.Contracts/IAutoBaggerClient` | 1,977 lines. Eligibility/grouping convertible; cartonPacked deferred (wsship.p). |
| `interface/oms_comm_in_prep.p` | 398 | PARTIAL | `Erp.Infrastructure/PromoteNewToOpenAsync` |  |
| `interface/oms_comm_out_prep.p` | 313 | PARTIAL | `Erp.Infrastructure/PromoteNewToOpenAsync` | Status promotion done; payload prep not. |
| `locusAPI.cls` | 5292 | PARTIAL | `Partners.Application/Locus/*` | 5,292 lines, 41 public methods. ACCEPT converted as reference; 22 inbound + 9 outbound remain. |
| `wsMiddlewareAPI.p` | 456 | PARTIAL | `Api/Middleware + Partners.Application/` | Auth + context done; router not. ensureEmployee deliberately NOT carried over. |
| `apiHelpers.i` | 642 | REPLACED | `Platform.Http/WmsHttpClient.cs` | initCurlInstance/apiRequest/command building. BR 59/60/61 become obsolete. |
| `httpRequestCURL.cls` | 529 | REPLACED | `Platform.Http/WmsHttpClient.cs` | cURL shell-out -> HttpClient. Loses OS-COMMAND, temp files, runAsBackGroundTaskNextTime. |
| `interface/config.json` | 17 | REPLACED | `Fdm4Options` | Contains apiKey OJsLL7IuTp. Rotate. |
| `interface/config_tt.i` | 46 | REPLACED | `Fdm4Options` |  |
| `interface/cron_globals.i` | 55 | REPLACED | `Jobs/CommWorkerOptions` | SESSION:PARAM parsing -> IOptions. |
| `interface/interfaceAPI.i` | 29 | REPLACED | `(constants)` |  |
| `interface/wsInterfaceAPI.p` | 58 | REPLACED | `(Jobs host)` | Persistent PASOE proc -> BackgroundService. |
| `locusAPI.i` | 24 | REPLACED | `Partners.Domain/` | Config temp-table. |
| `locusConfig.json` | 19 | REPLACED | `appsettings + secret store` | CONTAINS LIVE LOCUS CREDENTIALS. Rotate. |
| `rest/http.i` | 11 | REPLACED | `Platform.Http` |  |
| `rest/rest.i` | 14 | REPLACED | `Platform.Http` |  |
| `globaleCurl.sh` | 18 | DELETED | `Platform.Http/WmsHttpClient.cs` | --insecure. Globale adapter not yet scaffolded - no ABL caller found in api/. |
| `headerCurl.sh` | 65 | DELETED | `Platform.Http/WmsHttpClient.cs` | --insecure and eval $CMD both gone. TLS now validated. |
| `interface/comm_log.i` | 88 | DELETED | `(ILogger)` | writeToLog. |
| `interface/cron_check.i` | 160 | DELETED | `(CancellationToken)` | Stop-file polling. |
| `interface/cron_connect.i` | 144 | DELETED | `(BackgroundService)` | PID file + path resolution. |
| `interface/cron_disconnect.i` | 57 | DELETED | `(BackgroundService)` | Cleanup. |
| `interface/static_comm.cls` | 47 | DELETED | `(ILogger scope)` | Static PID holder for cron logging. Replaced by structured logging. |
| `jsonHelpers.i` | 269 | DELETED | `(System.Text.Json)` | Hand-rolled JSON helpers. No .NET equivalent needed. |
| `locusCurl.sh` | 40 | DELETED | `Platform.Http/WmsHttpClient.cs` | Credentials moved to configuration. |
| `locusTestCurl.sh` | 21 | DELETED | `(none)` | Test script. |
| `pyramidCurl.sh` | 26 | DELETED | `Platform.Http/WmsHttpClient.cs` |  |
| `test.p` | 40 | DELETED | `(none)` | HTML connectivity test page. |
| `accutechAutoBagger.cls` | 637 | TODO | `Partners.Application/AutoBagger/` | 637 lines base class. Config + box validation. |
| `accutechAutoBagger_tt.i` | 73 | TODO | `Partners.Domain/` | Config dataset. |
| `interface/WMSCommInProcessorBase.cls` | 383 | TODO | `Erp.Application/` | Inbound base. 383 lines, 2 queries. |
| `interface/ircodn_dcp.i` | 60 | TODO | `Erp.Domain/` | 60 lines, detail change processing. |
| `interface/ircodn_hcp.i` | 172 | TODO | `Erp.Domain/` | 172 lines, header change processing. |
| `interface/ircodn_tt.i` | 190 | TODO | `Erp.Domain/` | 190 lines, 3 temp-tables for order download. |
| `interface/oms_comm_in_route.p` | 381 | TODO | `Erp.Application/` | Inbound router. Same claim shape as outbound. |
| `interface/oms_comm_purge.p` | 405 | TODO | `Erp.Infrastructure/PurgeCompletedAsync` | Exports to BR 7000 dir before delete. Export format not characterised. |
| `interface/oms_ircodn.cls` | 1523 | TODO | `Erp.Application/` | 1,523 lines. Inbound order download + changenc.i change tracking. |
| `interface/oms_irpiup.cls` | 218 | TODO | `Erp.Application/Processors/PickingUpdateProcessor` | IRSTUP status=picking. Near-duplicate of irpmup - collapse once characterised. |
| `interface/oms_irpmup.cls` | 217 | TODO | `Erp.Application/Processors/PackedUpdateProcessor` | IRSTUP status=packed. |
| `interface/oms_irshup.cls` | 441 | TODO | `Erp.Application/Processors/ShipmentUpdateProcessor` | create_payload lines 93-400. Needs shpmst/shpdtl/cartonmst/ordhdr .df. |
| `interface/oms_order_import.cls` | 55 | TODO | `Erp.Application/` | 55 lines. |
| `locusResultTest.cls` | 1473 | TODO | `tests/` | 1,473 lines of mock payload generation. Useful as characterization fixtures. |
| `middleware.cls` | 473 | TODO | `Partners.Application/AutoBagger/` | 473 lines. EventBridge gateway. |
| `middleware_tt.i` | 50 | TODO | `Partners.Domain/` |  |
| `pyramidAPI.cls` | 673 | TODO | `Partners.Contracts/IPyramidClient` | 673 lines. 3 outbound + 1 inbound (SHIPOLPN). |
| `rest/inboundTest.p` | 237 | TODO | `tests/` |  |
| `upsAPI.cls` | 799 | TODO | `Partners.Contracts/IUpsClient` | 799 lines. OAuth2. Calls ms/dhl.p on a remote AppServer. |
| `wsLocusSimServer.p` | 65 | TODO | `tests/` | Mock Locus server. Becomes a WireMock stub or TestServer. |
| `wsPyramidAPI.p` | 183 | TODO | `Api/Endpoints/` | Router. Hardcoded key j5gBhMJ3fJ to remove. |
| `accutechAutoBaggerIn.cls` | 1305 | DEFERRED | `Partners.Application/AutoBagger/` | 1,305 lines. ORDCM runs wsship.p persistently for 6 procedures. |
| `rest/awsConfig.cls` | 254 | QUESTION | `(TBD)` | 254 lines. Read by tools/BEL-1018_DataSetup.p. |
| `rest/awsConfig_tt.i` | 53 | QUESTION | `(TBD)` |  |
| `rest/clobberIn.cls` | 298 | QUESTION | `(TBD)` | 298 lines. |
| `rest/config.cls` | 308 | QUESTION | `(TBD)` | 308 lines. |
| `rest/config_tt.i` | 59 | QUESTION | `(TBD)` |  |
| `rest/inbound.p` | 307 | QUESTION | `(TBD)` | 307 lines. Relationship to middleware.cls unclear - is this live? |
