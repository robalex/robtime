using NodaTime;
using TimeCalculation.Model;
using TimeCalculation.PunchPairing;
using Xunit;

namespace TimeCalculationTests
{
    public class PunchPairerTests
    {
        private readonly Employee testEmployee = new Employee { Id = 1 };
        private readonly PayRule _payRule = new PayRule { MaxShiftLengthHours = 15 };
        private readonly PunchPairer _pairer = new PunchPairer();

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

        [Fact]
        public void PairPunches_WhenInThenOut_ReturnsCorrectPair()
        {
            var inTime = Instant.FromDateTimeUtc(DateTime.UtcNow);
            var outTime = inTime.Plus(Duration.FromHours(8));

            var punch1 = CreateTestPunch(inTime, PunchType.In);
            var punch2 = CreateTestPunch(outTime, PunchType.Out);

            var result = _pairer.PairPunches([punch1, punch2], _payRule);

            Assert.Single(result);
            Assert.Equal(8, result[0].TotalHours);
            Assert.Same(punch1, result[0].InPunch);
            Assert.Same(punch2, result[0].OutPunch);
        }

        [Fact]
        public void PairPunches_WhenLongShift_ReturnsTwoPairs()
        {
            var inTime = Instant.FromDateTimeUtc(DateTime.UtcNow);
            var outTime = inTime.Plus(Duration.FromHours(20)); // Exceeds max shift

            var punch1 = CreateTestPunch(inTime, PunchType.In);
            var punch2 = CreateTestPunch(outTime, PunchType.Out);

            var result = _pairer.PairPunches([punch1, punch2], _payRule);

            Assert.Equal(2, result.Count);
            Assert.Null(result[0].OutPunch); // First pair is incomplete (no OutPunch)
            Assert.Null(result[1].OutPunch); // Second pair is also incomplete (no OutPunch)
        }

        [Fact]
        public void PairPunches_WhenNoPunches_ReturnsEmptyList()
        {
            var result = _pairer.PairPunches([], _payRule);

            Assert.Empty(result);
        }

        [Fact]
        public void PairPunches_WhenNullPunches_ReturnsEmptyList()
        {
            var result = _pairer.PairPunches(null, _payRule);

            Assert.Empty(result);
        }

        [Fact]
        public void PairPunches_WhenOnlyInPunch_ReturnsIncompletePair()
        {
            var inTime = Instant.FromDateTimeUtc(DateTime.UtcNow);

            var punch1 = CreateTestPunch(inTime, PunchType.In);

            var result = _pairer.PairPunches([punch1], _payRule);

            Assert.Single(result);
            Assert.Null(result[0].OutPunch);
            Assert.Equal(0, result[0].TotalHours);
        }

        [Fact]
        public void PairPunches_WhenOnlyOutPunch_ReturnsEmptyList()
        {
            var outTime = Instant.FromDateTimeUtc(DateTime.UtcNow);

            var punch1 = CreateTestPunch(outTime, PunchType.Out);

            var result = _pairer.PairPunches([punch1], _payRule);

            Assert.Empty(result);
        }

        [Fact]
        public void PairPunches_WhenMultipleInAndOut_ReturnsCorrectPairs()
        {
            var inTime1 = Instant.FromDateTimeUtc(DateTime.UtcNow);
            var outTime1 = inTime1.Plus(Duration.FromHours(8));
            var inTime2 = outTime1.Plus(Duration.FromHours(1)); // 1 hour break
            var outTime2 = inTime2.Plus(Duration.FromHours(6));

            var punch1 = CreateTestPunch(inTime1, PunchType.In);
            var punch2 = CreateTestPunch(outTime1, PunchType.Out);
            var punch3 = CreateTestPunch(inTime2, PunchType.In);
            var punch4 = CreateTestPunch(outTime2, PunchType.Out);

            var result = _pairer.PairPunches([punch1, punch2, punch3, punch4], _payRule);

            Assert.Equal(2, result.Count);
            Assert.Equal(8, result[0].TotalHours);
            Assert.Equal(6, result[1].TotalHours);
        }

        [Fact]
        public void PairPunches_WhenOutOfOrderPunches_OrdersChronologically()
        {
            var inTime = Instant.FromDateTimeUtc(DateTime.UtcNow);
            var outTime = inTime.Plus(Duration.FromHours(8));
            var inTime2 = outTime.Plus(Duration.FromHours(1)); // Later in time
            var outTime2 = inTime2.Plus(Duration.FromHours(4));

            // Create punches out of order
            var punch1 = CreateTestPunch(outTime2, PunchType.Out);
            var punch2 = CreateTestPunch(inTime, PunchType.In);
            var punch3 = CreateTestPunch(outTime, PunchType.Out);
            var punch4 = CreateTestPunch(inTime2, PunchType.In);

            var result = _pairer.PairPunches([punch1, punch2, punch3, punch4], _payRule);

            Assert.Equal(2, result.Count);
            Assert.Equal(8, result[0].TotalHours);
            Assert.Equal(4, result[1].TotalHours);
        }

        [Fact]
        public void PairPunches_WhenShiftExceedsMaxButNotByMuch_ReturnsOneCompletePair()
        {
            var inTime = Instant.FromDateTimeUtc(DateTime.UtcNow);
            var outTime = inTime.Plus(Duration.FromHours(14.5)); // Just under max

            var punch1 = CreateTestPunch(inTime, PunchType.In);
            var punch2 = CreateTestPunch(outTime, PunchType.Out);

            var result = _pairer.PairPunches([punch1, punch2], _payRule);

            Assert.Single(result);
            Assert.Equal(14.5, result[0].TotalHours);
        }
    }

}