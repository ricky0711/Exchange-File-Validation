# Sample data for Exchange File Validator

Minimal smoke-test workbooks. Replace with your real Nissan/FACE files for production validation.

| File | Load bar field |
|------|----------------|
| `ExchangeFile_Sample.xlsm` | Exchange File |
| `MsgSet_Sample.xlsx` | Msg Set / PDU |
| `ISR_Applied_Sample.xlsx` | ISR Applied |

## Regenerate

```bash
dotnet run --project tools/SampleDataGenerator
```

## Contents

- **MsgSet_Sample.xlsx** — Message List all PDU, Dico, Network Path, Construction of Container frame
- **ISR_Applied_Sample.xlsx** — one applied ISR (VehicleSpeed, BCM→PCM)
- **ExchangeFile_Sample.xlsm** — two demands: L2 re-use (VehicleSpeed) and L3 new signal (BrandNewSignal)

These are **not** representative of the full ~27k-row Message List. Use your project’s real `.xlsx` / `.xlsm` files on Windows for full validation.
