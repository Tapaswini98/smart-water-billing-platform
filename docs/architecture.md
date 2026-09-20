# Architecture

Four diagrams, each answering a different question. They render natively on GitHub.

- [System context](#system-context) — who talks to what
- [AWS deployment](#aws-deployment) — the services and how traffic flows
- [Ingestion and billing](#how-a-reading-becomes-an-invoice) — the mechanism that decides what a customer pays
- [Authentication](#two-authentication-schemes) — why a meter key cannot read an invoice
- [Data model](#data-model) — the schema

---

## System context

```mermaid
graph TB
    subgraph site["Customer site (LAN)"]
        meter["Water meters<br/>TOTAL m³ · FLOW m³/hr"]
        gw["Site gateway<br/>protocol translation<br/>store &amp; forward"]
        valve["Relay valves<br/>supply cut-off"]
    end

    subgraph aws["AWS"]
        api["Billing API<br/>ECS Fargate"]
        db[("PostgreSQL<br/>Amazon RDS")]
        spa["Web client<br/>S3 + CloudFront"]
    end

    admin["Admin<br/>meters · tariffs · billing runs"]
    cust["Customer<br/>consumption · invoices · payment"]

    meter -->|"field protocol"| gw
    gw -->|"HTTPS POST /api/v1/ingest<br/>X-Meter-Key"| api
    api -.->|"desired valve state"| gw
    gw -->|"actuate"| valve

    admin -->|HTTPS| spa
    cust -->|HTTPS| spa
    spa -->|"/api/* · JWT"| api
    api <--> db

    classDef edge fill:#e8f1f8,stroke:#1c5580,color:#17212b
    classDef cloud fill:#e7f5ec,stroke:#1c6b3e,color:#17212b
    classDef person fill:#fdf4e0,stroke:#8a5a00,color:#17212b
    class meter,gw,valve edge
    class api,db,spa cloud
    class admin,cust person
```

The gateway exists because meters rarely speak HTTP. It translates Modbus/M-Bus/
LoRaWAN into the REST contract, and buffers readings when the link drops — which is
why ingestion accepts out-of-order arrival and batch upload.

---

## AWS deployment

```mermaid
graph TB
    users(("Admins &amp;<br/>customers"))
    gateways(("Site gateways<br/>meter readings"))

    subgraph edge["Edge"]
        r53["Route 53<br/>DNS"]
        cf["CloudFront<br/>CDN + origin routing"]
        waf["AWS WAF<br/>rate limit · IP allowlist"]
        acm["ACM<br/>TLS certificates"]
    end

    subgraph vpc["VPC — 2 Availability Zones"]
        subgraph pub["Public subnets"]
            alb["Application<br/>Load Balancer"]
            nat["NAT Gateway"]
        end

        subgraph priv["Private subnets — no inbound from internet"]
            ecs["ECS Fargate<br/>Billing API<br/>2–4 tasks"]
            task["Scheduled task<br/>pg_dump"]
        end

        subgraph data["Isolated subnets"]
            rds[("RDS PostgreSQL 17<br/>Multi-AZ")]
            replica[("Read replica<br/>large scale only")]
        end
    end

    subgraph storage["Storage &amp; supporting services"]
        s3web["S3<br/>SPA assets"]
        s3bak["S3 + Glacier<br/>backups"]
        ecr["ECR<br/>container images"]
        sm["Secrets Manager<br/>DB creds · JWT key"]
        cw["CloudWatch<br/>logs · metrics · alarms"]
        sched["EventBridge Scheduler<br/>nightly backup"]
    end

    gha["GitHub Actions<br/>build · test · verify restore"]

    users --> r53 --> cf
    gateways --> r53
    acm -.-> cf
    waf -.-> cf
    cf -->|"static assets"| s3web
    cf -->|"/api/*"| alb
    alb --> ecs
    ecs --> rds
    rds -.->|"streaming"| replica
    ecs -.-> nat
    ecs --> sm
    ecs --> cw
    sched --> task --> rds
    task --> s3bak
    gha -->|"push image"| ecr
    ecr -.->|"pull"| ecs
    gha -->|"deploy"| ecs

    classDef edgeC fill:#e8f1f8,stroke:#1c5580,color:#17212b
    classDef computeC fill:#e7f5ec,stroke:#1c6b3e,color:#17212b
    classDef dataC fill:#fdeceb,stroke:#b4231e,color:#17212b
    classDef supportC fill:#fdf4e0,stroke:#8a5a00,color:#17212b
    class r53,cf,waf,acm,alb edgeC
    class ecs,task computeC
    class rds,replica dataC
    class s3web,s3bak,ecr,sm,cw,sched,gha supportC
```

**What changes versus running locally:** the `web` container disappears. On a laptop
nginx serves the SPA and proxies `/api`; on AWS CloudFront does both jobs — static
assets from S3, `/api/*` to the ALB. Same single origin, so still no CORS anywhere,
but one fewer container to run and pay for.

Service choices and the reasoning behind each are in
[`deployment.md`](deployment.md); costs are in
[`infrastructure-sizing.md`](infrastructure-sizing.md).

---

## How a reading becomes an invoice

The mechanism that decides what a customer pays. Consumption is **derived**, never
accumulated — see [ADR-0003](adr/0003-immutable-readings-and-consumption.md).

```mermaid
flowchart TB
    post["POST /api/v1/ingest/readings<br/>X-Meter-Key"]
    auth{"Key valid,<br/>meter active?"}
    dup{"Reading already<br/>at this timestamp?"}
    clock{"Timestamp within<br/>skew and backdating<br/>limits?"}
    classify["Classify against neighbours<br/>· TOTAL decreased → reset event<br/>· later reading exists → out-of-order<br/>· ΔTOTAL vs FLOW×hours → mismatch"]
    store[("INSERT meter_readings<br/>UNIQUE (meter_id, reading_at)")]

    post --> auth
    auth -->|no| r401["401"]
    auth -->|yes| clock
    clock -->|no| r422["422 rejected<br/>with what to check"]
    clock -->|yes| dup
    dup -->|yes| r200["200 duplicate_ignored<br/>retry was safe"]
    dup -->|no| classify --> store

    store -.-> calc

    subgraph billing["Billing run — previous month"]
        calc["ConsumptionCalculator<br/>TOTAL at last reading ≤ end<br/>− TOTAL at last reading ≤ start<br/>walked pairwise across resets"]
        tariff["TariffResolver<br/>version in force at period END"]
        price["PricingEngine<br/>progressive slabs or flat rate"]
        already{"Invoice exists for<br/>(meter, period)?"}
        invoice[("INSERT invoice + line items<br/>UNIQUE (meter_id, period_start)")]
        skip["Report AlreadyBilled<br/>nothing changed"]
    end

    already -->|yes| skip
    already -->|no| calc --> tariff --> price --> invoice

    classDef ok fill:#e7f5ec,stroke:#1c6b3e,color:#17212b
    classDef warn fill:#fdf4e0,stroke:#8a5a00,color:#17212b
    classDef bad fill:#fdeceb,stroke:#b4231e,color:#17212b
    classDef store fill:#e8f1f8,stroke:#1c5580,color:#17212b
    class store,invoice store
    class r200,skip warn
    class r401,r422 bad
    class calc,tariff,price ok
```

Three properties fall out of this shape:

- **Retry is safe.** A duplicate returns `200 duplicate_ignored`, not an error, so a
  meter that retries after a timeout cannot corrupt anything.
- **Arrival order is irrelevant.** Consumption reads the time-ordered series, so a
  late reading changes nothing about which invoice it belongs to.
- **Re-running a billing period is safe.** The unique index makes double-billing
  impossible at the storage layer rather than unlikely in application code.

---

## Two authentication schemes

```mermaid
sequenceDiagram
    autonumber
    participant M as Meter gateway
    participant C as Customer browser
    participant A as API
    participant D as PostgreSQL

    Note over M,A: Device path — no login, no token
    M->>A: POST /ingest/readings<br/>X-Meter-Key: wmk_...
    A->>D: lookup by indexed 12-char prefix
    D-->>A: candidate key hashes
    A->>A: SHA-256 + constant-time compare
    A-->>M: 201 accepted

    Note over C,A: Human path — JWT with a role claim
    C->>A: POST /auth/login
    A->>D: user by email
    A->>A: PBKDF2-HMAC-SHA512 verify
    A-->>C: JWT (role: Customer)
    C->>A: GET /invoices + Bearer
    A->>D: WHERE customer_id = caller
    A-->>C: only their own invoices

    Note over M,A: The schemes never overlap
    C->>A: POST /ingest/readings + Bearer
    A-->>C: 401 — policy requires the MeterKey scheme
    M->>A: GET /invoices + X-Meter-Key
    A-->>M: 401 — policy requires a JWT
```

The last two exchanges are the point: "a device credential cannot read invoices, and
a customer token cannot post readings" is enforced by the authorization policy, not
by remembering to check in each handler. Both are covered in the
[API walkthrough](api-walkthrough.http).

---

## Data model

```mermaid
erDiagram
    users ||--o{ meters : "is billed for"
    users ||--o{ invoices : "owes"
    meters ||--o{ meter_api_keys : "authenticates with"
    meters ||--o{ meter_readings : "reports"
    meters ||--o{ meter_reset_events : "raises"
    meters ||--o{ invoices : "is billed on"
    pricing_plans ||--o{ pricing_plan_versions : "versioned as"
    pricing_plan_versions ||--o{ pricing_slabs : "banded by"
    pricing_plans ||--o{ meters : "prices"
    pricing_plan_versions ||--o{ invoices : "cited by"
    invoices ||--o{ invoice_line_items : "itemised as"
    invoices ||--o{ payments : "settled by"
    payments ||--o{ payment_events : "logged as"
    billing_runs ||--o{ billing_run_items : "reports"
    billing_runs ||--o{ invoices : "produced"

    meter_readings {
        uuid id PK
        uuid meter_id FK
        timestamptz reading_at_utc "UNIQUE with meter_id"
        numeric total_m3 "billing authority"
        numeric flow_m3_per_hour "diagnostic only"
        int anomalies "flags"
    }

    invoices {
        uuid id PK
        uuid meter_id FK "UNIQUE with period_start"
        timestamptz period_start_utc
        numeric opening_total_m3 "evidence"
        numeric closing_total_m3 "evidence"
        numeric consumption_m3
        uuid pricing_plan_version_id FK "the rates it was priced at"
        numeric total_amount
    }

    pricing_plan_versions {
        uuid id PK
        int version_number
        timestamptz effective_from_utc "immutable window"
        timestamptz effective_to_utc
        numeric fixed_charge
    }
```

Two tables are never deleted from and never updated: `meter_readings` is the ledger
every invoice is reproducible from, and `pricing_plan_versions` must stay resolvable
for the life of any invoice citing it. Full policy in
[ADR-0010](adr/0010-soft-delete-policy.md).
