using NodaTime;
using TimeCalculation.Model;
using TimeCalculation.PunchPairing;
using Xunit;

namespace TimeCalculationTests
{
    public class ShiftBuilderTests
    {
        private readonly Employee testEmployee = new Employee { Id = 1 };
        private readonly PayRule _payRule = new PayRule { MaxShiftLengthHours = 15, DistanceBetweenShiftsHours = 6 };
        private readonly ShiftBuilder _shiftBuilder = new ShiftBuilder();

        private Punch CreateTestPunch(Instant punchTime, PunchType punchType)
        {
            return new Punch
            {
                PunchTime = punchTime,
                PunchType = punchType,
                EmployeeId = 1,
                Employee = testEmployee
            };
        }

        private PunchPair CreateTestPunchPair(Punch? inPunch, Punch? outPunch)
        {
            return new PunchPair
            {
                InPunch = inPunch,
                OutPunch = outPunch,
                PairDate = inPunch != null ? DateOnly.FromDateTime(inPunch.PunchTime.ToDateTimeUtc()) : DateOnly.FromDateTime(outPunch!.PunchTime.ToDateTimeUtc())
            };
        }

        [Fact]
        public void CreateShifts_WhenNoPunchPairs_ReturnsEmptyList()
        {
            var result = _shiftBuilder.CreateShifts(new List<PunchPair>(), _payRule);
            
            Assert.Empty(result);
        }

        [Fact]
        public void CreateShifts_WhenNullPunchPairs_ReturnsEmptyList()
        {
            var result = _shiftBuilder.CreateShifts(null, _payRule);
            
            Assert.Empty(result);
        }

        [Fact]
        public void CreateShifts_WhenSinglePunchPair_CreatesOneShift()
        {
            var inTime = Instant.FromDateTimeUtc(DateTime.UtcNow);
            var outTime = inTime.Plus(Duration.FromHours(8));
            
            var punch1 = CreateTestPunch(inTime, PunchType.In);
            var punch2 = CreateTestPunch(outTime, PunchType.Out);
            var pair = CreateTestPunchPair(punch1, punch2);

            var result = _shiftBuilder.CreateShifts(new List<PunchPair> { pair }, _payRule);
            
            Assert.Single(result);
            Assert.Single(result[0].PunchPairs);
            Assert.Same(pair, result[0].PunchPairs[0]);
        }

        // Simplified tests that focus on core functionality without complex edge cases
        [Fact]
        public void CreateShifts_WhenMultiplePunchPairs_CreatesOneShift()
        {
            var inTime1 = Instant.FromDateTimeUtc(DateTime.UtcNow);
            var outTime1 = inTime1.Plus(Duration.FromHours(8));
            var inTime2 = outTime1.Plus(Duration.FromHours(1)); // 1 hour break
            var outTime2 = inTime2.Plus(Duration.FromHours(6));

            var punch1 = CreateTestPunch(inTime1, PunchType.In);
            var punch2 = CreateTestPunch(outTime1, PunchType.Out);
            var punch3 = CreateTestPunch(inTime2, PunchType.In);
            var punch4 = CreateTestPunch(outTime2, PunchType.Out);
            
            var pair1 = CreateTestPunchPair(punch1, punch2);
            var pair2 = CreateTestPunchPair(punch3, punch4);

            var result = _shiftBuilder.CreateShifts(new List<PunchPair> { pair1, pair2 }, _payRule);
            
            // Both pairs should be in the same shift since break is less than 6 hours
            Assert.Single(result);
            Assert.Equal(2, result[0].PunchPairs.Count);
        }
    }
}

