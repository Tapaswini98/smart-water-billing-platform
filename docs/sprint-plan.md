# Project plan — waterfall, three developers

The brief asks for a plan to build this system in its entirety with a team of three
using waterfall. Waterfall is specified, so this plan commits to it properly — phases
with entry and exit criteria and a gate review at each boundary — rather than
describing an agile plan with phase labels attached.

**Duration:** 12 weeks to commissioning, plus ongoing maintenance.

## Team

| Role | Owns | Primary skills |
|---|---|---|
| **D1 — Backend & Domain** | Consumption calculation, pricing engine, billing, API, data model | C#, EF Core, domain modelling. Tech lead; final say on design. |
| **D2 — Platform & DevOps** | Containers, CI/CD, infrastructure, backup/restore, deployment, monitoring, site commissioning | Docker, Linux, PostgreSQL administration, AWS, networking |
| **D3 — QA & Integration** | Test strategy, integration and acceptance tests, meter/gateway integration, web client, documentation | Test automation, TypeScript/React, protocol integration |

Three people cannot specialise completely. D3 writes the web client because it is the
smallest coherent slice that does not block D1's domain work, and because the person
writing the acceptance tests benefits from having built the surface they test.

## Phases

### Phase 1 — Requirements (weeks 1–2)

**Entry:** signed statement of work; customer stakeholder identified.

| Deliverable | Owner |
|---|---|
| Functional requirements specification, numbered and traceable | D1 |
| Non-functional requirements: meter count, retention, RPO/RTO, availability | D2 |
| Meter and gateway inventory: makes, models, protocols, firmware | D3 |
| Tariff specification signed off by the customer's billing team | D1 |
| Test strategy and acceptance criteria | D3 |
| Risk register, first pass | All |

**Exit:** customer sign-off on the requirements specification and the tariff rules.

**Gate review — customer stakeholder + tech lead.** The specific question: *is the
tariff specification unambiguous enough to implement without further interpretation?*
This is the gate that most often should fail and does not. "Slab based" alone is not
a specification — the progressive-versus-whole-volume ambiguity in ADR-0004 is
exactly the kind of thing that must be resolved here, not discovered in week 8.

### Phase 2 — Design (weeks 3–4)

**Entry:** requirements signed off.

| Deliverable | Owner |
|---|---|
| Architecture decision records | D1 |
| Database schema and migration strategy | D1 |
| API specification (OpenAPI, written before the implementation) | D1 |
| Infrastructure sizing for the customer's meter count | D2 |
| Backup, recovery and deployment design | D2 |
| **Hardware-in-the-loop spike against one real meter** | D3 |
| UI wireframes for the admin and customer journeys | D3 |

**Exit:** design documents reviewed; the spike has posted a real reading from a real
meter into a throwaway endpoint.

**Gate review — tech lead + customer's IT representative.**

The spike is the most important item in this phase and is deliberately pulled
forward. See the risk discussion below.

### Phase 3 — Implementation (weeks 5–9)

**Entry:** design signed off; hardware ordered.

| Week | D1 — Backend | D2 — Platform | D3 — QA & Client |
|---|---|---|---|
| 5 | Schema, migrations, auth | Compose stack, CI pipeline | Test harness, web scaffold |
| 6 | Users, meters, ingestion | Backup/restore scripts | Client generation, auth screens |
| 7 | **Consumption + pricing engine** | Monitoring, log shipping | Unit tests alongside D1; meter list |
| 8 | Invoicing, billing runs | Deployment scripts, staging | Invoice screens, integration tests |
| 9 | Payments, supply control, audit | Hardware build and burn-in | Admin tariff editor; test pass |

**Exit:** all requirements implemented; unit tests green; code review complete.

**Gate review — tech lead.** Every numbered requirement maps to a test.

Week 7 is the critical path. Consumption and pricing are the only components whose
defects cost money rather than time, and everything downstream depends on them, so
they are scheduled early enough that a week of overrun is absorbable.

### Phase 4 — Integration & Test (weeks 10–11)

**Entry:** implementation complete and frozen. **Only defect fixes from here.**

| Deliverable | Owner |
|---|---|
| End-to-end integration on real hardware | D3 |
| Performance test at 2× expected meter count | D2 |
| **Restore drill, timed, by someone who did not write the procedure** | D2 |
| Security review: authorization matrix, credential handling, dependency audit | D1 |
| Customer UAT, scripted against the acceptance criteria | D3 |
| Operations runbook | D2 |

**Exit:** UAT signed off; zero open critical or high defects; the restore drill met
its RTO.

**Gate review — customer stakeholder + tech lead. This is the go/no-go.**

The restore drill is performed by D1 or D3, never D2, because a procedure is only
proven when someone who did not write it can follow it.

### Phase 5 — Deployment (week 12)

**Entry:** UAT signed off.

| Deliverable | Owner |
|---|---|
| Hardware installed and commissioned on site | D2 |
| Production secrets generated; seeding disabled; environment set to Production | D2 |
| Meters registered, ingestion keys issued and handed over securely | D3 |
| Backup schedule enabled and **verified on site** | D2 |
| Administrator and operator training | D3 |
| Parallel run: one full billing cycle alongside the existing process | All |

**Exit:** a full month billed correctly in parallel with the incumbent process, and
reconciled.

**Gate review — customer stakeholder. Acceptance and handover.**

The parallel run is not optional. The first real invoice run is the first time the
system meets a full month of genuine meter behaviour, and the only safe way to
discover a discrepancy is with the old process still running beside it.

### Phase 6 — Maintenance (ongoing)

Warranty support, quarterly restore rehearsals, dependency and security patching,
and an annual review of the sizing against actual growth.

## Schedule, dependencies and slack

```
Week    1  2  3  4  5  6  7  8  9 10 11 12
Req.   [=====]
Design       [=====]
Impl.              [==============]
I&T                               [=====]
Deploy                                  [==]

Critical path: Requirements -> Tariff sign-off -> Design -> Consumption &
               pricing (wk 7) -> Invoicing (wk 8) -> UAT -> Parallel run

Hardware:    order end of wk 4 ---------> delivery wk 8 (4-week lead time)
```

| Item | Slack | Consequence of overrun |
|---|---|---|
| Requirements | 0 | Every phase slips; this is why the tariff gate is strict |
| Design | 3 days | Absorbed by implementation |
| Consumption & pricing (wk 7) | 0 | Critical path |
| Web client | 5 days | Can ship with fewer screens; the API satisfies the requirements |
| Hardware delivery | 2 weeks | Ordered in week 4 for week 8, against a 4-week quoted lead time |
| Integration & test | 2 days | Eats into deployment week |
| Parallel run | 0 | A billing cycle cannot be compressed |

## Risk register

| # | Risk | P | I | Owner | Mitigation | Trigger |
|---|---|---|---|---|---|---|
| R1 | Meter protocol differs from documentation | **High** | **High** | D3 | Hardware-in-the-loop spike in Design, not Integration | Spike fails to read a real meter by end of week 4 |
| R2 | Tariff rules ambiguous or change mid-project | Med | **High** | D1 | Versioned, immutable tariffs; formal sign-off at the Phase 1 gate | Any tariff change request after week 4 |
| R3 | Hardware delivery slips | Med | Med | D2 | Order in week 4; develop against containers throughout | No dispatch confirmation by week 6 |
| R4 | Meter clock drift corrupts billing periods | **High** | Med | D1 | Server-side skew tolerance; device timestamps validated on ingestion | Any reading rejected for clock skew during the parallel run |
| R5 | Site network unreliable | Med | Med | D2 | Store-and-forward gateways; batch ingestion; on-prem billing path | Any gateway buffer exceeding 24 h |
| R6 | Parallel run finds a discrepancy | Med | **High** | D1 | Invoices store their full breakdown, so any figure is traceable to readings and a tariff version | Any unexplained variance vs. the incumbent process |
| R7 | Key person unavailable | Low | **High** | All | ADRs and runbooks written as work happens, not at the end | Any absence over 3 days |
| R8 | Customer UAT availability slips | Med | Med | D3 | UAT dates agreed at the Phase 1 gate | No confirmed UAT participants by week 9 |

## Where waterfall fits this project, and where it does not

**It genuinely fits.** The scope is fixed and externally specified. Hardware
procurement has a four-week lead time and must be committed before implementation
finishes, which requires the sizing to be settled early — exactly what a design phase
produces. Billing is a regulated, auditable function where the customer reasonably
wants formal UAT and sign-off before a single real invoice is issued. And the
customer's own staff need training on a stable system, not a moving one.

**Where it is risky.** The assumptions that cannot be validated by analysis are all
about hardware: what the meters actually send, how reliably, and with what clock
accuracy. In a strict waterfall these surface in Integration, in week 10, which is the
phase with the least slack and the highest cost of change.

**The mitigation, and it is a real one:** a hardware-in-the-loop spike in the Design
phase (R1), where a real meter posts a real reading through a throwaway endpoint. This
is a deliberate, bounded deviation from pure waterfall — building a throwaway piece of
software during a design phase — and it is worth stating plainly rather than hiding.
It converts the project's single largest unknown from a week-10 crisis into a week-4
finding, when the design can still absorb it.

**A second deviation worth naming:** the API specification is written in Phase 2 and
the web client is generated from it in Phase 3. That lets D3 build the client against
a contract before D1 has implemented it, removing a dependency that would otherwise
serialise two of the three developers for three weeks.

Both deviations exist for the same reason: waterfall's weakness is late discovery, and
the correct response is to pull the discoverable forward without abandoning the phase
structure the customer is relying on.
