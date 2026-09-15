-- AI_WebApi project schema/data audit, 2026-09-15.
-- Run with project as the default database. No DDL/DML or personal data output.
SET SESSION TRANSACTION READ ONLY;
START TRANSACTION WITH CONSISTENT SNAPSHOT;

SELECT 'A01_snapshot' section, DATABASE() db, VERSION() mysql_version, NOW() observed_at;
SELECT 'A02_tables' section, COUNT(*) table_count
FROM information_schema.tables WHERE table_schema=DATABASE();
SELECT 'A03_schema' section, table_name, column_name, column_type, is_nullable, column_key
FROM information_schema.columns WHERE table_schema=DATABASE()
AND table_name IN ('dbuser','dbroleuser','dbbuniess','dbsyncidmapping','rehabplan',
 'report','templates','reportdata_new','reportdata','dbrehaplan','dbreport',
 'dbtemplates','dbdevice','dbdevice1') ORDER BY table_name,ordinal_position;

SELECT 'A04_counts' section,'dbuser' t,COUNT(*) n FROM dbuser
UNION ALL SELECT 'A04_counts','dbroleuser',COUNT(*) FROM dbroleuser
UNION ALL SELECT 'A04_counts','dbbuniess',COUNT(*) FROM dbbuniess
UNION ALL SELECT 'A04_counts','rehabplan',COUNT(*) FROM rehabplan
UNION ALL SELECT 'A04_counts','report',COUNT(*) FROM report
UNION ALL SELECT 'A04_counts','templates',COUNT(*) FROM templates
UNION ALL SELECT 'A04_counts','reportdata_new',COUNT(*) FROM reportdata_new
UNION ALL SELECT 'A04_counts','reportdata',COUNT(*) FROM reportdata
UNION ALL SELECT 'A04_counts','dbrehaplan',COUNT(*) FROM dbrehaplan
UNION ALL SELECT 'A04_counts','dbreport',COUNT(*) FROM dbreport
UNION ALL SELECT 'A04_counts','dbtemplates',COUNT(*) FROM dbtemplates;

SELECT 'A05_mapping' section,TableName,COUNT(*) n,MIN(SourceId) min_source,
 MAX(SourceId) max_source,MIN(TargetId) min_target,MAX(TargetId) max_target,
 MIN(CreateTime) first_at,MAX(CreateTime) last_at
FROM dbsyncidmapping GROUP BY TableName;
SELECT 'A06_mapping_targets' section,m.TableName,COUNT(*) n,
 SUM(u.Id IS NULL) missing_target,
 SUM(EXISTS(SELECT 1 FROM dbroleuser r WHERE r.UserId=u.Id AND r.RoleId=0)) patient_targets
FROM dbsyncidmapping m LEFT JOIN dbuser u ON u.Id=m.TargetId
WHERE m.TableName IN ('user','robotuser') GROUP BY m.TableName;
SELECT 'A07_id_collisions' section,m.TableName,COUNT(*) conflicts
FROM dbsyncidmapping m JOIN dbuser u ON u.Id=m.SourceId
WHERE m.TableName IN ('user','robotuser') AND m.TargetId<>u.Id GROUP BY m.TableName;
SELECT 'A08_ambiguous_mapping' section,TableName,COUNT(*) ambiguous_source_groups
FROM (SELECT TableName,SourceId FROM dbsyncidmapping
 GROUP BY TableName,SourceId HAVING COUNT(DISTINCT TargetId)>1) q GROUP BY TableName;

SELECT 'A09_role_definition' section,Id,Name FROM dbrole WHERE Id IN (0,1);
SELECT 'A10_roles' section,RoleId,COUNT(*) role_rows,COUNT(DISTINCT UserId) users_n
FROM dbroleuser GROUP BY RoleId;
SELECT 'A11_role_flags' section,RoleId,Enable,ActiveRole+0 active_role,COUNT(*) n
FROM dbroleuser WHERE RoleId IN (0,1) GROUP BY RoleId,Enable,ActiveRole;
SELECT 'A12_business' section,COUNT(*) users_n,SUM(u.BusinessId IS NULL) null_business,
 SUM(b.Id IS NULL) missing_business,SUM(COALESCE(u.IsDeleted+0,0)=1) deleted
FROM dbuser u LEFT JOIN dbbuniess b ON b.Id=u.BusinessId;
SELECT 'A13_missing_patient_role' section,COUNT(DISTINCT p.PatientId) patients_n
FROM rehabplan p WHERE NOT EXISTS
 (SELECT 1 FROM dbroleuser r WHERE r.UserId=p.PatientId AND r.RoleId=0);
SELECT 'A14_dual_roles' section,COUNT(*) users_n FROM dbuser u
WHERE EXISTS(SELECT 1 FROM dbroleuser r WHERE r.UserId=u.Id AND r.RoleId=0)
AND EXISTS(SELECT 1 FROM dbroleuser r WHERE r.UserId=u.Id AND r.RoleId=1);
SELECT 'A15_duplicate_phones' section,COUNT(*) duplicate_patient_phone_groups
FROM (SELECT Phone FROM dbuser u WHERE NULLIF(TRIM(Phone),'') IS NOT NULL
 AND COALESCE(IsDeleted+0,0)=0
 AND EXISTS(SELECT 1 FROM dbroleuser r WHERE r.UserId=u.Id AND r.RoleId=0)
 GROUP BY Phone HAVING COUNT(*)>1) q;

SELECT 'A16_plan_links' section,COUNT(*) plans,COUNT(DISTINCT p.PatientId) patients,
 SUM(u.Id IS NULL) missing_patient,SUM(d.Id IS NULL) missing_doctor,
 SUM(p.createtime IS NULL) null_create_time
FROM rehabplan p LEFT JOIN dbuser u ON u.Id=p.PatientId
LEFT JOIN dbuser d ON d.Id=p.doctorid;
SELECT 'A17_template_orphans' section,COUNT(*) plans,
 COUNT(DISTINCT p.templateid) template_ids,COUNT(DISTINCT p.PatientId) patients
FROM rehabplan p LEFT JOIN templates t ON t.Id=p.templateid WHERE t.Id IS NULL;
SELECT 'A18_report_orphans' section,COUNT(*) reports,SUM(p.Id IS NULL) missing_plan
FROM report r LEFT JOIN rehabplan p ON p.Id=r.Id;
SELECT 'A19_irego_exclusions' section,COUNT(*) plans,COUNT(DISTINCT p.PatientId) patients,
 SUM(t.Id IS NULL) no_template,SUM(t.Id IS NOT NULL AND t.Modelid<>1) wrong_template_model,
 SUM(b.Id IS NULL) no_business,
 SUM(NOT EXISTS(SELECT 1 FROM dbroleuser r WHERE r.UserId=u.Id AND r.RoleId=0)) no_patient_role,
 SUM(NOT EXISTS(SELECT 1 FROM dbroleuser r WHERE r.UserId=d.Id AND r.RoleId=1)) no_doctor_role,
 SUM(d.BusinessId IS NULL OR u.BusinessId IS NULL OR d.BusinessId<>u.BusinessId) wrong_doctor_business
FROM rehabplan p JOIN dbuser u ON u.Id=p.PatientId JOIN dbuser d ON d.Id=p.doctorid
LEFT JOIN dbbuniess b ON b.Id=u.BusinessId LEFT JOIN templates t ON t.Id=p.templateid
WHERE p.devicetype=1;
-- Counts below describe SQL eligibility only, not clinically/semantically verified sessions.
SELECT 'A20_structurally_eligible' section,COUNT(*) plans,
 COUNT(DISTINCT p.PatientId) patients,SUM(r.Id IS NOT NULL) reports
FROM rehabplan p JOIN dbuser u ON u.Id=p.PatientId JOIN dbuser d ON d.Id=p.doctorid
JOIN dbbuniess b ON b.Id=u.BusinessId JOIN templates t ON t.Id=p.templateid
LEFT JOIN report r ON r.Id=p.Id
WHERE p.devicetype=1 AND t.Modelid=1 AND d.BusinessId=u.BusinessId
AND COALESCE(u.IsDeleted+0,0)=0 AND COALESCE(d.IsDeleted+0,0)=0
AND EXISTS(SELECT 1 FROM dbroleuser ru WHERE ru.UserId=u.Id AND ru.RoleId=0)
AND EXISTS(SELECT 1 FROM dbroleuser rd WHERE rd.UserId=d.Id AND rd.RoleId=1);

SELECT 'A21_overview_plan_links' section,COUNT(*) plans,SUM(u.Id IS NULL) missing_user,
 SUM(t.Id IS NULL) missing_template,SUM(b.Id IS NULL) missing_business
FROM dbrehaplan p LEFT JOIN dbuser u ON u.Id=p.UserId
LEFT JOIN dbtemplates t ON t.Id=p.TemplateId LEFT JOIN dbbuniess b ON b.Id=p.BusinessId;
SELECT 'A22_overview_report_links' section,COUNT(*) reports,SUM(p.Id IS NULL) missing_plan
FROM dbreport r LEFT JOIN dbrehaplan p ON p.Id=r.planId;
SELECT 'A23_indexes' section,table_name,index_name,non_unique,seq_in_index,column_name
FROM information_schema.statistics WHERE table_schema=DATABASE()
AND table_name IN ('dbuser','dbroleuser','dbsyncidmapping','rehabplan','report',
 'reportdata_new','dbrehaplan','dbreport') ORDER BY table_name,index_name,seq_in_index;
COMMIT;
