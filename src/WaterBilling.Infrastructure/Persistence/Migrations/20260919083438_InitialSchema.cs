using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WaterBilling.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_log",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    changes_json = table.Column<string>(type: "jsonb", maxLength: 512, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pricing_plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pricing_plans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    phone_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    billing_address = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    last_login_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pricing_plan_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pricing_plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    slab_mode = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    fixed_charge = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    rate_per_m3 = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    tax_rate_percent = table.Column<decimal>(type: "numeric(6,3)", precision: 6, scale: 3, nullable: false),
                    effective_from_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_to_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pricing_plan_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_pricing_plan_versions_pricing_plans_pricing_plan_id",
                        column: x => x.pricing_plan_id,
                        principalTable: "pricing_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "billing_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_start_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    period_end_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    triggered_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    meters_considered = table.Column<int>(type: "integer", nullable: false),
                    invoices_generated = table.Column<int>(type: "integer", nullable: false),
                    skipped = table.Column<int>(type: "integer", nullable: false),
                    failed = table.Column<int>(type: "integer", nullable: false),
                    total_billed_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    is_dry_run = table.Column<bool>(type: "boolean", nullable: false),
                    failure_reason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_billing_runs", x => x.id);
                    table.ForeignKey(
                        name: "fk_billing_runs_users_triggered_by_user_id",
                        column: x => x.triggered_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "meters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    serial_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    location_description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    installed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pricing_plan_id = table.Column<Guid>(type: "uuid", nullable: true),
                    desired_supply_state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reported_supply_state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    supply_state_changed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    supply_state_reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meters", x => x.id);
                    table.ForeignKey(
                        name: "fk_meters_pricing_plans_pricing_plan_id",
                        column: x => x.pricing_plan_id,
                        principalTable: "pricing_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_meters_users_customer_id",
                        column: x => x.customer_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pricing_slabs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pricing_plan_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    from_m3 = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    to_m3 = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    rate_per_m3 = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pricing_slabs", x => x.id);
                    table.ForeignKey(
                        name: "fk_pricing_slabs_pricing_plan_versions_pricing_plan_version_id",
                        column: x => x.pricing_plan_version_id,
                        principalTable: "pricing_plan_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "invoices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    meter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    period_start_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    period_end_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    opening_total_m3 = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    closing_total_m3 = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    opening_reading_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closing_reading_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    consumption_m3 = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    reading_count = table.Column<int>(type: "integer", nullable: false),
                    consumption_flags = table.Column<int>(type: "integer", nullable: false),
                    pricing_plan_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pricing_plan_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pricing_plan_version_number = table.Column<int>(type: "integer", nullable: false),
                    pricing_plan_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    fixed_charge = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    usage_charge = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tax_rate_percent = table.Column<decimal>(type: "numeric(6,3)", precision: 6, scale: 3, nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    amount_paid = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    issued_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    due_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    paid_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    billing_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoices", x => x.id);
                    table.ForeignKey(
                        name: "fk_invoices_billing_runs_billing_run_id",
                        column: x => x.billing_run_id,
                        principalTable: "billing_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_invoices_meters_meter_id",
                        column: x => x.meter_id,
                        principalTable: "meters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoices_pricing_plan_versions_pricing_plan_version_id",
                        column: x => x.pricing_plan_version_id,
                        principalTable: "pricing_plan_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoices_users_customer_id",
                        column: x => x.customer_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "meter_api_keys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    meter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prefix = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    key_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    label = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_used_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meter_api_keys", x => x.id);
                    table.ForeignKey(
                        name: "fk_meter_api_keys_meters_meter_id",
                        column: x => x.meter_id,
                        principalTable: "meters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "meter_readings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    meter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reading_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    total_m3 = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    flow_m3_per_hour = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: true),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    anomalies = table.Column<int>(type: "integer", nullable: false),
                    anomaly_notes = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meter_readings", x => x.id);
                    table.ForeignKey(
                        name: "fk_meter_readings_meters_meter_id",
                        column: x => x.meter_id,
                        principalTable: "meters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "meter_reset_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    meter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    detected_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    previous_total_m3 = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    new_total_m3 = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    triggering_reading_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    is_acknowledged = table.Column<bool>(type: "boolean", nullable: false),
                    acknowledged_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meter_reset_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_meter_reset_events_meters_meter_id",
                        column: x => x.meter_id,
                        principalTable: "meters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "billing_run_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    billing_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    meter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    consumption_m3 = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    total_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    consumption_flags = table.Column<int>(type: "integer", nullable: false),
                    message = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_billing_run_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_billing_run_items_billing_runs_billing_run_id",
                        column: x => x.billing_run_id,
                        principalTable: "billing_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_billing_run_items_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_billing_run_items_meters_meter_id",
                        column: x => x.meter_id,
                        principalTable: "meters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "invoice_line_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    band_from_m3 = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    band_to_m3 = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    units_m3 = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    rate_per_m3 = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_line_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_invoice_line_items_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    paid_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    provider_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    initiated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                    table.ForeignKey(
                        name: "fk_payments_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payments_users_paid_by_user_id",
                        column: x => x.paid_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", maxLength: 512, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_payment_events_payments_payment_id",
                        column: x => x.payment_id,
                        principalTable: "payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_entity",
                table: "audit_log",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_occurred_at",
                table: "audit_log",
                column: "occurred_at_utc",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_billing_run_items_invoice_id",
                table: "billing_run_items",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_billing_run_items_meter_id",
                table: "billing_run_items",
                column: "meter_id");

            migrationBuilder.CreateIndex(
                name: "ix_billing_run_items_run_id_outcome",
                table: "billing_run_items",
                columns: new[] { "billing_run_id", "outcome" });

            migrationBuilder.CreateIndex(
                name: "ix_billing_runs_period_start_started_at",
                table: "billing_runs",
                columns: new[] { "period_start_utc", "started_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_billing_runs_triggered_by_user_id",
                table: "billing_runs",
                column: "triggered_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_line_items_invoice_id_sort_order",
                table: "invoice_line_items",
                columns: new[] { "invoice_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_invoices_billing_run_id",
                table: "invoices",
                column: "billing_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_customer_id_period_start",
                table: "invoices",
                columns: new[] { "customer_id", "period_start_utc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_invoices_invoice_number_unique",
                table: "invoices",
                column: "invoice_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoices_meter_id_period_start_unique",
                table: "invoices",
                columns: new[] { "meter_id", "period_start_utc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoices_pricing_plan_version_id",
                table: "invoices",
                column: "pricing_plan_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_status",
                table: "invoices",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_meter_api_keys_meter_id",
                table: "meter_api_keys",
                column: "meter_id");

            migrationBuilder.CreateIndex(
                name: "ix_meter_api_keys_prefix",
                table: "meter_api_keys",
                column: "prefix");

            migrationBuilder.CreateIndex(
                name: "ix_meter_readings_meter_id_reading_at_utc_unique",
                table: "meter_readings",
                columns: new[] { "meter_id", "reading_at_utc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_meter_reset_events_meter_id_detected_at_utc",
                table: "meter_reset_events",
                columns: new[] { "meter_id", "detected_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_meters_customer_id",
                table: "meters",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_meters_pricing_plan_id",
                table: "meters",
                column: "pricing_plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_meters_serial_number_unique",
                table: "meters",
                column: "serial_number",
                unique: true,
                filter: "deleted_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_payment_events_payment_id_occurred_at",
                table: "payment_events",
                columns: new[] { "payment_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_idempotency_key_unique",
                table: "payments",
                column: "idempotency_key",
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_payments_invoice_id",
                table: "payments",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_paid_by_user_id",
                table: "payments",
                column: "paid_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_provider_reference_unique",
                table: "payments",
                columns: new[] { "provider", "provider_reference" },
                unique: true,
                filter: "provider_reference IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_pricing_plan_versions_plan_id_effective_from",
                table: "pricing_plan_versions",
                columns: new[] { "pricing_plan_id", "effective_from_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_pricing_plan_versions_plan_id_version_unique",
                table: "pricing_plan_versions",
                columns: new[] { "pricing_plan_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pricing_plans_name_unique",
                table: "pricing_plans",
                column: "name",
                unique: true,
                filter: "deleted_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_pricing_plans_single_default",
                table: "pricing_plans",
                column: "is_default",
                unique: true,
                filter: "is_default = true AND deleted_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_pricing_slabs_version_id_sort_order_unique",
                table: "pricing_slabs",
                columns: new[] { "pricing_plan_version_id", "sort_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_email_unique",
                table: "users",
                column: "email",
                unique: true,
                filter: "deleted_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_users_role",
                table: "users",
                column: "role");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_log");

            migrationBuilder.DropTable(
                name: "billing_run_items");

            migrationBuilder.DropTable(
                name: "invoice_line_items");

            migrationBuilder.DropTable(
                name: "meter_api_keys");

            migrationBuilder.DropTable(
                name: "meter_readings");

            migrationBuilder.DropTable(
                name: "meter_reset_events");

            migrationBuilder.DropTable(
                name: "payment_events");

            migrationBuilder.DropTable(
                name: "pricing_slabs");

            migrationBuilder.DropTable(
                name: "payments");

            migrationBuilder.DropTable(
                name: "invoices");

            migrationBuilder.DropTable(
                name: "billing_runs");

            migrationBuilder.DropTable(
                name: "meters");

            migrationBuilder.DropTable(
                name: "pricing_plan_versions");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "pricing_plans");
        }
    }
}
