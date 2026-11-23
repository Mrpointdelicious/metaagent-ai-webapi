-- 患者报告归档业务库，MySQL 8.0.16+ / InnoDB。选定目标 schema 后执行。
-- 会话时区使用 UTC，保证 DATETIME 默认值与业务写入一致。
SET SESSION time_zone = '+00:00';

-- 脱敏归档，每行对应一份报告的一个版本。
CREATE TABLE patient_report (
    id                    BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    report_ref            CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    revision              INT UNSIGNED NOT NULL DEFAULT 1,
    archive_state         ENUM('active','superseded','withdrawn') NOT NULL DEFAULT 'active',
    active_ref            CHAR(36) CHARACTER SET ascii COLLATE ascii_bin
                          GENERATED ALWAYS AS
                          (CASE WHEN archive_state = 'active' THEN report_ref ELSE NULL END) STORED,
    owner_user_id         BIGINT UNSIGNED NOT NULL,
    upload_business_id    BIGINT UNSIGNED NULL,
    upload_request_key    CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    request_mac           BINARY(32) NOT NULL,
    mac_key_version       SMALLINT UNSIGNED NOT NULL,
    report_type           VARCHAR(32) NOT NULL DEFAULT 'unknown',
    report_date           DATE NULL,
    input_media_type      VARCHAR(64) CHARACTER SET ascii NOT NULL,
    archived_pdf          LONGBLOB NOT NULL,
    archived_bytes        INT UNSIGNED NOT NULL,
    archived_sha256       BINARY(32) NOT NULL,
    page_count            SMALLINT UNSIGNED NOT NULL,
    sanitizer_version     VARCHAR(48) NOT NULL,
    extractor_version     VARCHAR(48) NOT NULL,
    evidence_json         JSON NOT NULL,
    source_snapshot       BINARY(32) NOT NULL,
    archived_at           DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (id),
    UNIQUE KEY uq_report_revision (report_ref, revision),
    UNIQUE KEY uq_report_active (active_ref),
    UNIQUE KEY uq_owner_upload (owner_user_id, upload_request_key),
    KEY ix_owner_date (owner_user_id, archive_state, report_date, id),
    KEY ix_owner_tenant_date (owner_user_id, upload_business_id, archive_state, report_date, id),
    CONSTRAINT ck_report_size CHECK (archived_bytes BETWEEN 1 AND 20971520
                                     AND archived_bytes = OCTET_LENGTH(archived_pdf)),
    CONSTRAINT ck_report_pages CHECK (page_count BETWEEN 1 AND 30),
    CONSTRAINT ck_report_revision CHECK (revision >= 1)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- 首次解读，每行绑定一个归档版本。
CREATE TABLE patient_report_interpretation (
    id                    BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    job_ref               CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    report_id             BIGINT UNSIGNED NOT NULL,
    source_snapshot       BINARY(32) NOT NULL,
    state                 ENUM('queued','running','succeeded','failed','cancelled') NOT NULL,
    attempt_count         SMALLINT UNSIGNED NOT NULL DEFAULT 0,
    available_at          DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    lease_epoch           BIGINT UNSIGNED NOT NULL DEFAULT 0,
    lease_digest          BINARY(32) NULL,
    lease_until           DATETIME(6) NULL,
    error_code            VARCHAR(64) NULL,
    retryable             BOOLEAN NOT NULL DEFAULT FALSE,
    result_json           JSON NULL,
    result_sha256         BINARY(32) NULL,
    receipt_digest        BINARY(32) NULL,
    interpreter_version   VARCHAR(64) NULL,
    created_at            DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    updated_at            DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6)
                          ON UPDATE CURRENT_TIMESTAMP(6),
    finished_at           DATETIME(6) NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_job_ref (job_ref),
    UNIQUE KEY uq_first_reading (report_id),
    KEY ix_claim (state, available_at, id),
    KEY ix_expired_lease (state, lease_until, id),
    CONSTRAINT fk_reading_report FOREIGN KEY (report_id) REFERENCES patient_report(id),
    CONSTRAINT ck_reading_lease CHECK (
        (state = 'running' AND lease_until IS NOT NULL AND lease_digest IS NOT NULL)
        OR (state <> 'running' AND lease_until IS NULL AND lease_digest IS NULL)),
    CONSTRAINT ck_reading_result CHECK (
        (state = 'succeeded' AND result_json IS NOT NULL AND result_sha256 IS NOT NULL
         AND interpreter_version IS NOT NULL AND receipt_digest IS NOT NULL)
        OR (state <> 'succeeded' AND result_json IS NULL AND result_sha256 IS NULL
            AND receipt_digest IS NULL)),
    CONSTRAINT ck_reading_finished CHECK (
        (state IN ('succeeded','failed','cancelled') AND finished_at IS NOT NULL)
        OR (state IN ('queued','running') AND finished_at IS NULL))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
