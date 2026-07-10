# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Build
dotnet build

# Run all tests
dotnet test

# Run a single test class
dotnet test --filter "FullyQualifiedName~WeightedOvertimeCalculatorTests"

# Run a single test method
dotnet test --filter "FullyQualifiedName~WeightedOvertimeCalculatorTests.CalculateOvertime_WhenNoHours_ReturnsZero"
```

## Architecture

RobTime is a payroll time-calculation library. The data flows through three pipeline stages:

1. **PunchPairing** (`TimeCalculation/PunchPairing/`) — raw `Punch` records (In/Out events) are matched into `PunchPair` objects by `PunchPairer`. Pairs exceeding `PayRule.MaxShiftLengthHours` are split.

2. **Shift Building** (`TimeCalculation/PunchPairing/ShiftBuilder.cs`) — `PunchPair` lists are grouped into `Shift` objects. A new `Shift` starts when the gap between consecutive pairs exceeds `PayRule.DistanceBetweenShiftsHours`.

3. **Pay Calculation** (`TimeCalculation/Calculation/`) — `PayCalculator` orchestrates the pipeline end-to-end. `WeightedOvertimeCalculator` computes weekly overtime (hours > 40 at 1.5× `Employee.MinimumWage`).

### Key types

- `Punch` — a single clock-in or clock-out event; `PunchTime` is a NodaTime `Instant`
- `PunchPair` — matched In/Out pair; `TotalHours` is computed from the two instants
- `Shift` — one contiguous work period composed of one or more `PunchPair`s
- `Week` — a collection of `Shift`s used as the unit for overtime calculation
- `PayRule` — per-client configuration (`MaxShiftLengthHours`, `DistanceBetweenShiftsHours`)
- `Employee` — carries `MinimumWage` used in pay calculations

### Testing conventions

Tests use xunit.v3. `TestEntityCreator` (in the test project) is the shared factory for `Punch` and `PunchPair` test objects. NodaTime `Instant.FromUtc(...)` is used throughout tests to construct punch times.
