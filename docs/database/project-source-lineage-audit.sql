-- Optional migration provenance audit; requires SELECT on project and robotdb.
-- robotdb is used ONLY as an audit source, never as the proposed runtime dependency.
-- No DDL/DML, user names, phone numbers, or report contents are returned.
SET SESSION TRANSACTION READ ONLY;
START TRANSACTION WITH CONSISTENT SNAPSHOT;
SELECT 'B01_snapshot' section,NOW() observed_at;
SELECT 'B02_report_content' section,COUNT(*) common_reports,
 SUM(NOT (r.Details <=> o.Details)) details_different
FROM project.report r JOIN robotdb.report o ON o.Id=r.Id;
SELECT 'B03_plan_identity' section,COUNT(*) common_plan_ids,
 SUM(m.TargetId=p.PatientId) patient_consistent,SUM(m.TargetId IS NULL) mapping_missing,
 SUM(m.TargetId IS NOT NULL AND m.TargetId<>p.PatientId) patient_inconsistent,
 SUM(NOT (p.details <=> o.details)) details_different,
 SUM(NOT (p.StartTime <=> o.StartTime)) start_different
FROM project.rehabplan p JOIN robotdb.rehabplan o ON o.Id=p.Id
LEFT JOIN project.dbsyncidmapping m ON m.TableName='robotuser' AND m.SourceId=o.PatientId;
SELECT 'B04_report_patient_identity' section,COUNT(*) joined_report_plan_ids,
 SUM(m.TargetId=p.PatientId) patient_consistent,SUM(m.TargetId IS NULL) mapping_missing,
 SUM(m.TargetId IS NOT NULL AND m.TargetId<>p.PatientId) patient_inconsistent
FROM project.report r JOIN project.rehabplan p ON p.Id=r.Id
JOIN robotdb.rehabplan o ON o.Id=r.Id
LEFT JOIN project.dbsyncidmapping m ON m.TableName='robotuser' AND m.SourceId=o.PatientId;
SELECT 'B05_template_content' section,COUNT(*) common_templates,
 SUM(NOT (t.Details <=> o.Details)) details_different,SUM(t.Modelid<>o.Modelid) model_different
FROM project.templates t JOIN robotdb.templates o ON o.Id=t.Id;
SELECT 'B06_eligible_report_identity' section,COUNT(*) reports,
 SUM(m.TargetId=p.PatientId) patient_consistent,SUM(m.TargetId IS NULL) mapping_unknown,
 SUM(m.TargetId IS NOT NULL AND m.TargetId<>p.PatientId) patient_inconsistent
FROM project.rehabplan p JOIN project.dbuser u ON u.Id=p.PatientId
JOIN project.dbuser d ON d.Id=p.doctorid JOIN project.dbbuniess b ON b.Id=u.BusinessId
JOIN project.templates t ON t.Id=p.templateid JOIN project.report r ON r.Id=p.Id
LEFT JOIN robotdb.rehabplan o ON o.Id=r.Id
LEFT JOIN project.dbsyncidmapping m ON m.TableName='robotuser' AND m.SourceId=o.PatientId
WHERE p.devicetype=1 AND t.Modelid=1 AND d.BusinessId=u.BusinessId
AND COALESCE(u.IsDeleted+0,0)=0 AND COALESCE(d.IsDeleted+0,0)=0
AND EXISTS(SELECT 1 FROM project.dbroleuser ru WHERE ru.UserId=u.Id AND ru.RoleId=0)
AND EXISTS(SELECT 1 FROM project.dbroleuser rd WHERE rd.UserId=d.Id AND rd.RoleId=1);
SELECT 'B07_old_eligibility_baseline' section,COUNT(*) plans,
 COUNT(DISTINCT p.PatientId) patients,SUM(r.Id IS NOT NULL) reports
FROM robotdb.rehabplan p JOIN robotdb.users u ON u.Id=p.PatientId
JOIN robotdb.users d ON d.Id=p.doctorid JOIN robotdb.vendor v ON v.Id=u.VendorId
JOIN robotdb.templates t ON t.Id=p.templateid LEFT JOIN robotdb.report r ON r.Id=p.Id
WHERE p.devicetype=1 AND u.UserType=2 AND d.UserType=1 AND t.Modelid=1 AND d.VendorId=u.VendorId;
SELECT 'B08_changed_business_ids' section,COUNT(*) users_n
FROM project.dbsyncidmapping m JOIN project.dbuser u ON u.Id=m.TargetId
JOIN robotdb.users o ON o.Id=m.SourceId
WHERE m.TableName='robotuser' AND NOT (u.BusinessId <=> o.VendorId);
COMMIT;
