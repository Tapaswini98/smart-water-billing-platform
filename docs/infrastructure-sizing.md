# Infrastructure sizing

> **Status:** the arithmetic and the reasoning are settled; the per-tier bills of
> materials are being finalised. _(pending)_

The point of this document is not to list three server sizes. It is to show the
arithmetic that makes the choice obvious, and then to decline to over-engineer.

## The workload, in numbers

Assume the design cadence of **one reading per meter every 15 minutes** (96/day).

| | Small (≤ 50 meters) | Medium (≤ 300) | Large (300+, modelled at 2,000) |
|---|---|---|---|
| Readings / day | 4,800 | 28,800 | 192,000 |
| Readings / year | 1.75 M | 10.5 M | 70 M |
| Average write rate | 0.06 /s | **0.33 /s** | 2.2 /s |
| Burst (all meters at once) | 50 | 300 | 2,000 |
| Row size incl. indexes | ~150 B | ~150 B | ~150 B |
| Data growth / year | ~0.3 GB | **~1.6 GB** | ~10 GB |
| 5-year retention | ~1.5 GB | ~8 GB | ~50 GB |

Invoices are negligible by comparison: 300 meters × 12 months = 3,600 rows/year,
plus roughly four line items each.

## What that arithmetic actually says

**0.33 writes per second.** A single PostgreSQL instance on modest hardware handles
thousands. The entire five-year dataset for the medium tier fits in RAM on a 16 GB
box, which means the working set is never on disk and read latency is a non-issue.

This is why the architecture has **no message queue, no stream processor, no
sharding and no read replica**. Adding them would not make the system faster; it
would add components that must be understood and maintained by whoever is on site
when something breaks at 2 a.m.

The thresholds at which that changes are stated in
[ADR-0008](adr/0008-postgresql-single-node.md) rather than left implied.

## Recommended configurations

_(pending — hardware bill of materials, OS and software stack, network and power
resilience, and the monitoring baseline for each of the three tiers.)_

### Small — up to 50 meters
### Medium — up to 300 meters
### Large — 300+ meters

## Sizing the network and the edge

_(pending — meter-to-gateway protocol assumptions, LAN segmentation, and what
happens during a WAN outage.)_
