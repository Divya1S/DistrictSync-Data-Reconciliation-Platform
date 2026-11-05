# Sample data

Two snapshots of the same eight employees as seen by HR and by Payroll, with every discrepancy type planted:

| Employee | HR vs Payroll | Expected outcome (seeded rule set v1) |
|---|---|---|
| Ava Lee | identical data, different formatting (case, `61,500.00`, `(555) 010-1001`) | **Matched** — normalizers absorb formatting |
| Liam Patel | salary 72 000 vs 75 000 | **FieldConflict** → rule "Salary or status conflicts need payroll approval" → **High** case |
| Noah Garcia | title + phone differ, payroll is newer | **FieldConflict** → "Contact details: newest wins" → **auto-resolved**, write-back to HR |
| Emma Nguyen | terminated in HR, absent from payroll | **MissingInRight** → "Ignore terminated staff" → **Ignored** |
| Olivia Smith | hired 15 days ago, absent from payroll | **MissingInRight** → "New hires missing from payroll are urgent" → **Urgent** case |
| Mateo Okafor / Aiden Rossi | identical | **Matched** |
| Sofia Kim | appears twice in payroll | **Duplicate** → "Duplicates need a human" → **Normal** case |
| Ghost User | only in payroll | **MissingInLeft** → no rule → default → **Normal** case |

Larger data sets with the same planted anomaly mix can be generated:

```bash
dotnet run --project tools/DistrictSync.DataGen -- --rows 1000000 --out samples/generated --seed 42
```

The generator prints the expected counts so you can check a run's statistics against them.
