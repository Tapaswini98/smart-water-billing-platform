CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919083438_InitialSchema') THEN
    CREATE TABLE audit_log (
        id uuid NOT NULL,
        actor_user_id uuid,
        actor_email character varying(256),
        action character varying(64) NOT NULL,
        entity_type character varying(64) NOT NULL,
        entity_id uuid,
        occurred_at_utc timestamp with time zone NOT NULL,
        ip_address character varying(64),
        changes_json jsonb,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        deleted_at_utc timestamp with time zone,
        CONSTRAINT pk_audit_log PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919083438_InitialSchema') THEN
    CREATE TABLE pricing_plans (
        id uuid NOT NULL,
        name character varying(128) NOT NULL,
        description character varying(1024),
        currency character varying(3) NOT NULL,
        is_default boolean NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        deleted_at_utc timestamp with time zone,
        CONSTRAINT pk_pricing_plans PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919083438_InitialSchema') THEN
    CREATE TABLE users (
        id uuid NOT NULL,
        email character varying(256) NOT NULL,
        password_hash character varying(512) NOT NULL,
        full_name character varying(200) NOT NULL,
        role character varying(32) NOT NULL,
        phone_number character varying(32),
        billing_address character varying(1024),
        is_active boolean NOT NULL,
        last_login_at_utc timestamp with time zone,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        deleted_at_utc timestamp with time zone,
        CONSTRAINT pk_users PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919083438_InitialSchema') THEN
    CREATE TABLE pricing_plan_versions (
        id uuid NOT NULL,
        pricing_plan_id uuid NOT NULL,
        version_number integer NOT NULL,
        mode character varying(32) NOT NULL,
        slab_mode character varying(48) NOT NULL,
        fixed_charge numeric(18,2) NOT NULL,
        rate_per_m3 numeric(18,4) NOT NULL,
        tax_rate_percent numeric(6,3) NOT NULL,
        effective_from_utc timestamp with time zone NOT NULL,
        effective_to_utc timestamp with time zone,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        deleted_at_utc timestamp with time zone,
        CONSTRAINT pk_pricing_plan_versions PRIMARY KEY (id),
        CONSTRAINT fk_pricing_plan_versions_pricing_plans_pricing_plan_id FOREIGN KEY (pricing_plan_id) REFERENCES pricing_plans (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919083438_InitialSchema') THEN
    CREATE TABLE billing_runs (
        id uuid NOT NULL,
        period_start_utc timestamp with time zone NOT NULL,
        period_end_utc timestamp with time zone NOT NULL,
        triggered_by_user_id uuid,
        started_at_utc timestamp with time zone NOT NULL,
        completed_at_utc timestamp with time zone,
        status character varying(32) NOT NULL,
        meters_considered integer NOT NULL,
        invoices_generated integer NOT NULL,
        skipped integer NOT NULL,
        failed integer NOT NULL,
        total_billed_amount numeric(18,2) NOT NULL,
        is_dry_run boolean NOT NULL,
        failure_reason character varying(1024),
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        deleted_at_utc timestamp with time zone,
        CONSTRAINT pk_billing_runs PRIMARY KEY (id),
        CONSTRAINT fk_billing_runs_users_triggered_by_user_id FOREIGN KEY (triggered_by_user_id) REFERENCES users (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919083438_InitialSchema') THEN
    CREATE TABLE meters (
        id uuid NOT NULL,
        serial_number character varying(64) NOT NULL,
        model character varying(128),
        location_description character varying(512),
        installed_at_utc timestamp with time zone NOT NULL,
        status character varying(32) NOT NULL,
        customer_id uuid,
        pricing_plan_id uuid,
        desired_supply_state character varying(16) NOT NULL,
        reported_supply_state character varying(16),
        supply_state_changed_at_utc timestamp with time zone,
        supply_state_reason character varying(512),
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        deleted_at_utc timestamp with time zone,
        CONSTRAINT pk_meters PRIMARY KEY (id),
        CONSTRAINT fk_meters_pricing_plans_pricing_plan_id FOREIGN KEY (pricing_plan_id) REFERENCES pricing_plans (id) ON DELETE RESTRICT,
        CONSTRAINT fk_meters_users_customer_id FOREIGN KEY (customer_id) REFERENCES users (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919083438_InitialSchema') THEN
    CREATE TABLE pricing_slabs (
        id uuid NOT NULL,
        pricing_plan_version_id uuid NOT NULL,
        sort_order integer NOT NULL,
        from_m3 numeric(18,3) NOT NULL,
        to_m3 numeric(18,3),
        rate_per_m3 numeric(18,4) NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        deleted_at_utc timestamp with time zone,
        CONSTRAINT pk_pricing_slabs PRIMARY KEY (id),
        CONSTRAINT fk_pricing_slabs_pricing_plan_versions_pricing_plan_version_id FOREIGN KEY (pricing_plan_version_id) REFERENCES pricing_plan_versions (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919083438_InitialSchema') THEN
    CREATE TABLE invoices (
        id uuid NOT NULL,
        invoice_number character varying(32) NOT NULL,
        meter_id uuid NOT NULL,
        customer_id uuid,
        period_start_utc timestamp with time zone NOT NULL,
        period_end_utc timestamp with time zone NOT NULL,
        opening_total_m3 numeric(18,3),
        closing_total_m3 numeric(18,3),
        opening_reading_at_utc timestamp with time zone,
        closing_reading_at_utc timestamp with time zone,
        consumption_m3 numeric(18,3) NOT NULL,
        reading_count integer NOT NULL,
        consumption_flags integer NOT NULL,
        pricing_plan_id uuid,
        pricing_plan_version_id uuid,
        pricing_plan_version_number integer NOT NULL,
        pricing_plan_name character varying(128) NOT NULL,
        fixed_charge numeric(18,2) NOT NULL,
        usage_charge numeric(18,2) NOT NULL,
        tax_rate_percent numeric(6,3) NOT NULL,
        tax_amount numeric(18,2) NOT NULL,
        total_amount numeric(18,2) NOT NULL,
        amount_paid numeric(18,2) NOT NULL,
        currency character varying(3) NOT NULL,
        status character varying(16) NOT NULL,
        issued_at_utc timestamp with time zone,
        due_at_utc timestamp with time zone,
        paid_at_utc timestamp with time zone,
        billing_run_id uuid,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        deleted_at_utc timestamp with time zone,
        CONSTRAINT pk_invoices PRIMARY KEY (id),
        CONSTRAINT fk_invoices_billing_runs_billing_run_id FOREIGN KEY (billing_run_id) REFERENCES billing_runs (id) ON DELETE SET NULL,
        CONSTRAINT fk_invoices_meters_meter_id FOREIGN KEY (meter_id) REFERENCES meters (id) ON DELETE RESTRICT,
        CONSTRAINT fk_invoices_pricing_plan_versions_pricing_plan_version_id FOREIGN KEY (pricing_plan_version_id) REFERENCES pricing_plan_versions (id) ON DELETE RESTRICT,
        CONSTRAINT fk_invoices_users_customer_id FOREIGN KEY (customer_id) REFERENCES users (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919083438_InitialSchema') THEN
    CREATE TABLE meter_api_keys (
        id uuid NOT NULL,
        meter_id uuid NOT NULL,
        prefix character varying(16) NOT NULL,
        key_hash character varying(128) NOT NULL,
        label character varying(128),
        expires_at_utc timestamp with time zone,
        revoked_at_utc timestamp with time zone,
        last_used_at_utc timestamp with time zone,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        deleted_at_utc timestamp with time zone,
        CONSTRAINT pk_meter_api_keys PRIMARY KEY (id),
        CONSTRAINT fk_meter_api_keys_meters_meter_id FOREIGN KEY (meter_id) REFERENCES meters (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919083438_InitialSchema') THEN
    CREATE TABLE meter_readings (
        id uuid NOT NULL,
        meter_id uuid NOT NULL,
        reading_at_utc timestamp with time zone NOT NULL,
        received_at_utc timestamp with time zone NOT NULL,
        total_m3 numeric(18,3) NOT NULL,
        flow_m3_per_hour numeric(12,3),
        source character varying(32) NOT NULL,
        anomalies integer NOT NULL,
        anomaly_notes character varying(512),
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        deleted_at_utc timestamp with time zone,
        CONSTRAINT pk_meter_readings PRIMARY KEY (id),
        CONSTRAINT fk_meter_readings_meters_meter_id FOREIGN KEY (meter_id) REFERENCES meters (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919083438_InitialSchema') THEN
    CREATE TABLE meter_reset_events (
        id uuid NOT NULL,
        meter_id uuid NOT NULL,
        detected_at_utc timestamp with time zone NOT NULL,
        previous_total_m3 numeric(18,3) NOT NULL,
        new_total_m3 numeric(18,3) NOT NULL,
        triggering_reading_id uuid,
        notes character varying(512),
        is_acknowledged boolean NOT NULL,
        acknowledged_by_user_id uuid,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        deleted_at_utc timestamp with time zone,
        CONSTRAINT pk_meter_reset_events PRIMARY KEY (id),
        CONSTRAINT fk_meter_reset_events_meters_meter_id FOREIGN KEY (meter_id) REFERENCES meters (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919083438_InitialSchema') THEN
    CREATE TABLE billing_run_items (
        id uuid NOT NULL,
        billing_run_id uuid NOT NULL,
        meter_id uuid NOT NULL,
        outcome character varying(32) NOT NULL,
        invoice_id uuid,
        consumption_m3 numeric(18,3),
        total_amount numeric(18,2),
        consumption_flags integer NOT NULL,
        message character varying(1024),
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        deleted_at_utc timestamp with time zone,
        CONSTRAINT pk_billing_run_items PRIMARY KEY (id),
        CONSTRAINT fk_billing_run_items_billing_runs_billing_run_id FOREIGN KEY (billing_run_id) REFERENCES billing_runs (id) ON DELETE CASCADE,
        CONSTRAINT fk_billing_run_items_invoices_invoice_id FOREIGN KEY (invoice_id) REFERENCES invoices (id) ON DELETE SET NULL,
        CONSTRAINT fk_billing_run_items_meters_meter_id FOREIGN KEY (meter_id) REFERENCES meters (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919083438_InitialSchema') THEN
    CREATE TABLE invoice_line_items (
        id uuid NOT NULL,
        invoice_id uuid NOT NULL,
        sort_order integer NOT NULL,
        kind character varying(24) NOT NULL,
        description character varying(256) NOT NULL,
        band_from_m3 numeric(18,3),
        band_to_m3 numeric(18,3),
        units_m3 numeric(18,3) NOT NULL,
        rate_per_m3 numeric(18,4) NOT NULL,
        amount numeric(18,2) NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        deleted_at_utc timestamp with time zone,
        CONSTRAINT pk_invoice_line_items PRIMARY KEY (id),
        CONSTRAINT fk_invoice_line_items_invoices_invoice_id FOREIGN KEY (invoice_id) REFERENCES invoices (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919083438_InitialSchema') THEN
    CREATE TABLE payments (
        id uuid NOT NULL,
        invoice_id uuid NOT NULL,
        paid_by_user_id uuid,
        amount numeric(18,2) NOT NULL,
        currency character varying(3) NOT NULL,
        status character varying(16) NOT NULL,
        provider character varying(64) NOT NULL,
        provider_reference character varying(128),
        idempotency_key character varying(128),
        initiated_at_utc timestamp with time zone NOT NULL,
        completed_at_utc timestamp with time zone,
        failure_reason character varying(512),
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        deleted_at_utc timestamp with time zone,
        CONSTRAINT pk_payments PRIMARY KEY (id),
        CONSTRAINT fk_payments_invoices_invoice_id FOREIGN KEY (invoice_id) REFERENCES invoices (id) ON DELETE RESTRICT,
        CONSTRAINT fk_payments_users_paid_by_user_id FOREIGN KEY (paid_by_user_id) REFERENCES users (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919083438_InitialSchema') THEN
    CREATE TABLE payment_events (
        id uuid NOT NULL,
        payment_id uuid NOT NULL,
        event_type character varying(64) NOT NULL,
        occurred_at_utc timestamp with time zone NOT NULL,
        payload_json jsonb,
        created_at_utc timestamp with time zone NOT NULL,
        updated_at_utc timestamp with time zone,
        deleted_at_utc timestamp with time zone,
        CONSTRAINT pk_payment_events PRIMARY KEY (id),
        CONSTRAINT fk_payment_events_payments_payment_id FOREIGN KEY (payment_id) REFERENCES payments (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919083438_InitialSchema') THEN
    CREATE INDEX ix_audit_log_entity ON audit_log (entity_type, entity_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919083438_InitialSchema') THEN
    CREATE INDEX ix_audit_log_occurred_at ON audit_log (occurred_at_utc DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919083438_InitialSchema') THEN
    CREATE INDEX ix_billing_run_items_invoice_id ON billing_run_items (invoice_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919083438_InitialSchema') THEN
    CREATE INDEX ix_billing_run_items_meter_id ON billing_run_items (meter_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919083438_InitialSchema') THEN
    CREATE INDEX ix_billing_run_items_run_id_outcome ON billing_run_items (billing_run_id, outcome);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919083438_InitialSchema') THEN
    CREATE INDEX ix_billing_runs_period_start_started_at ON billing_runs (period_start_utc, started_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919083438_InitialSchema') THEN
    CREATE INDEX ix_billing_runs_triggered_by_user_id ON billing_runs (triggered_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919083438_InitialSchema') THEN
    CREATE INDEX ix_invoice_line_items_invoice_id_sort_order ON invoice_line_items (invoice_id, sort_order);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919083438_InitialSchema') THEN
    CREATE INDEX ix_invoices_billing_run_id ON invoices (billing_run_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919083438_InitialSchema') THEN
    CREATE INDEX ix_invoices_customer_id_period_start ON invoices (customer_id, period_start_utc DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919083438_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_invoices_invoice_number_unique ON invoices (invoice_number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919083438_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_invoices_meter_id_period_start_unique ON invoices (meter_id, period_start_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919083438_InitialSchema') THEN
    CREATE INDEX ix_invoices_pricing_plan_version_id ON invoices (pricing_plan_version_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919083438_InitialSchema') THEN
    CREATE INDEX ix_invoices_status ON invoices (status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919083438_InitialSchema') THEN
    CREATE INDEX ix_meter_api_keys_meter_id ON meter_api_keys (meter_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919083438_InitialSchema') THEN
    CREATE INDEX ix_meter_api_keys_prefix ON meter_api_keys (prefix);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919083438_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_meter_readings_meter_id_reading_at_utc_unique ON meter_readings (meter_id, reading_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919083438_InitialSchema') THEN
    CREATE INDEX ix_meter_reset_events_meter_id_detected_at_utc ON meter_reset_events (meter_id, detected_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919083438_InitialSchema') THEN
    CREATE INDEX ix_meters_customer_id ON meters (customer_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919083438_InitialSchema') THEN
    CREATE INDEX ix_meters_pricing_plan_id ON meters (pricing_plan_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919083438_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_meters_serial_number_unique ON meters (serial_number) WHERE deleted_at_utc IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919083438_InitialSchema') THEN
    CREATE INDEX ix_payment_events_payment_id_occurred_at ON payment_events (payment_id, occurred_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919083438_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_payments_idempotency_key_unique ON payments (idempotency_key) WHERE idempotency_key IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919083438_InitialSchema') THEN
    CREATE INDEX ix_payments_invoice_id ON payments (invoice_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919083438_InitialSchema') THEN
    CREATE INDEX ix_payments_paid_by_user_id ON payments (paid_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919083438_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_payments_provider_reference_unique ON payments (provider, provider_reference) WHERE provider_reference IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919083438_InitialSchema') THEN
    CREATE INDEX ix_pricing_plan_versions_plan_id_effective_from ON pricing_plan_versions (pricing_plan_id, effective_from_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919083438_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_pricing_plan_versions_plan_id_version_unique ON pricing_plan_versions (pricing_plan_id, version_number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919083438_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_pricing_plans_name_unique ON pricing_plans (name) WHERE deleted_at_utc IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919083438_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_pricing_plans_single_default ON pricing_plans (is_default) WHERE is_default = true AND deleted_at_utc IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919083438_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_pricing_slabs_version_id_sort_order_unique ON pricing_slabs (pricing_plan_version_id, sort_order);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919083438_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_users_email_unique ON users (email) WHERE deleted_at_utc IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919083438_InitialSchema') THEN
    CREATE INDEX ix_users_role ON users (role);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260919083438_InitialSchema') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260919083438_InitialSchema', '10.0.12');
    END IF;
END $EF$;
COMMIT;

