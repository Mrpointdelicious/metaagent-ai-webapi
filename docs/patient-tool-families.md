# Patient tool families

2026-10-14. Routes use `/api/ai/patients/tools/` and the existing service Bearer / trusted `system_context` contract. `GET catalog` returns object capabilities and implementation status, with no patient data. OpenAPI tags group tools by object.

Each implemented object has overview, history, single analysis, multiple analysis, single report, multiple report:

| Object | Overview | Single | Multiple | Reports |
| --- | --- | --- | --- | --- |
| irego | get_irego_patient_context | get_irego_session_analysis | get_irego_longitudinal_analysis | generate_irego_single_session_report / generate_irego_longitudinal_report |
| iretour | get_iretour_patient_context | get_iretour_session_analysis | get_iretour_longitudinal_analysis | generate_iretour_single_session_report / generate_iretour_longitudinal_report |
| irecog | get_irecog_patient_context | get_irecog_session_analysis | get_irecog_longitudinal_analysis | generate_irecog_single_session_report / generate_irecog_longitudinal_report |
| assessment | get_assessment_patient_context | get_assessment_result_analysis | get_assessment_longitudinal_analysis | generate_assessment_single_report / generate_assessment_longitudinal_report |

History endpoints are `get_{object}_patient_history`. IREMO is listed as blocked and has no tools. Existing route names remain compatible.

The new assessment family has contract `assessment-1.0.0`, independent `assessment_ref`, and `AssessmentTools:ReferenceSigningKey` (at least 32 random characters). Report rendering/storage reuse `IReGoTools:ReportImage`; template is `assessment-records-v1`.

The SELECT-only repository reads active patients and same-institution online plans (`DeviceId=-1`, `TemplateId=0/-1`), with explicit read limits. Known online games are excluded. Report links do not determine the object. Identity requires product catalog and source report agreement before exposing a reported total. Unknown structures, missing results and conflicts remain explicit. The parser supports bounded, duplicate-key-free object summaries using `Score/TableTitle/Title` or `score/tableTitle/title`; historical variants outside those shapes fail closed.

Requests support optional `scale_type` (1–13), paginated history, single selectors `latest_record/latest_usable/assessment_ref`, and multiple windows `latest_count` (2–20) / `date_range` (up to 180 days, at most 20 records). Records are returned in `data.records`, counts in `data.counts`; overview additionally returns catalog coverage. Upload vs scheduled times are labeled separately. Answers, raw JSON and source free-text explanations are excluded.

Scoring versions/direction/ranges are not verified: multiple analysis summarizes records and exposes `blocked_unverified_scoring_rules`, with no clinical score delta or cross-scale average. Reports require the analysis `source_snapshot` in `expected_source_snapshot`, and an explicit `assessment_ref` selector for single reports. Changed source content returns `ASSESSMENT_SOURCE_CHANGED` before rendering.

Build passed with 0 errors and 26 existing warnings; two focused synthetic family smoke tests passed. No full regression or production clinical queries/writes were performed.

2026-10-15: local AI_WebApi and doctor Sandbox API/BFF/Web deployed as `tools-f545f61b9bfd`. Authenticated service catalog smoke found 24 tools; doctor login/catalog smoke confirmed 4 families enabled and IREMO blocked. Actual clinical source coverage remains unverified. Existing data/artifact volumes were retained.

Docker Desktop recovered by launching outside the affected Codex MSIX process environment. A separate Debian mirror 404 was avoided by explicitly reusing local `ai_webapi:runtime-base-33a67b530010` (Chromium/fonts verified), while recompiling the application. Compose supports optional `AI_WEBAPI_RUNTIME_IMAGE` and `AI_WEBAPI_INSTALL_RUNTIME_DEPENDENCIES`; defaults still install runtime dependencies.

Windows reserves TCP 5041–5140 on this machine. `AI_WEBAPI_PORT` now supports a local override (default 5043); current API host port is 15044 and local report public URLs were adjusted. Doctor UI remains at `http://127.0.0.1:18200`, using internal `ai_webapi:8080`. No Windows reservation policy was changed.
