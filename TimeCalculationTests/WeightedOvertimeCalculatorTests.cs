namespace TimeCalculationTests
{
    public class WeightedOvertimeCalculatorTests
    {
        private readonly WeightedOvertimeCalculator _calculator = new();
        private readonly Employee _employee = new()
        {
            MinimumWage = 15.00,
            Name = "Test Employee"
        };

        private Week CreateTestWeek(List<Shift> shifts)
        {
            return new Week(shifts);
        }

        [Fact]
        public void CalculateOvertime_WhenNoHours_ReturnsZero()
        {
            // Arrange
            var shifts = new List<Shift>();
            var week = CreateTestWeek(shifts);

            // Act
            var result = _calculator.CalculateOvertime(week, _employee);

            // Assert
            Assert.Equal(0.0, result);
        }

        [Fact]
        public void CalculateOvertime_WhenExactly40Hours_ReturnsZero()
        {
            // Arrange
            var shifts = new List<Shift>
            {
                new Shift(new PunchPair(
                    CreateTestPunch(Instant.FromUtc(2023, 1, 1, 9, 0), PunchType.In),
                    CreateTestPunch(Instant.FromUtc(2023, 1, 1, 17, 0), PunchType.Out)
                ))
            };
            var week = CreateTestWeek(shifts);

            // Act
            var result = _calculator.CalculateOvertime(week, _employee);

            // Assert
            Assert.Equal(0.0, result);
        }

        [Fact]
        public void CalculateOvertime_When41Hours_ReturnsCorrectOvertime()
        {
            // Arrange
            var shifts = new List<Shift>
            {
                new Shift(new PunchPair(
                    CreateTestPunch(Instant.FromUtc(2023, 1, 1, 9, 0), PunchType.In),
                    CreateTestPunch(Instant.FromUtc(2023, 1, 1, 18, 0), PunchType.Out)
                ))
            };
            var week = CreateTestWeek(shifts);

            // Act
            var result = _calculator.CalculateOvertime(week, _employee);

            // Assert - 1 hour overtime at 1.5x rate = $22.50
            Assert.Equal(22.50, result);
        }

        [Fact]
        public void CalculateOvertime_When45Hours_ReturnsCorrectOvertime()
        {
            // Arrange
            var shifts = new List<Shift>
            {
                new Shift(new PunchPair(
                    CreateTestPunch(Instant.FromUtc(2023, 1, 1, 9, 0), PunchType.In),
                    CreateTestPunch(Instant.FromUtc(2023, 1, 1, 22, 0), PunchType.Out)
                ))
            };
            var week = CreateTestWeek(shifts);

            // Act
            var result = _calculator.CalculateOvertime(week, _employee);

            // Assert - 5 hours overtime at 1.5x rate = $112.50
            Assert.Equal(112.50, result);
        }

        [Fact]
        public void CalculateOvertime_WhenMultipleShifts_ReturnsTotalOvertime()
        {
            // Arrange
            var shifts = new List<Shift>
            {
                new Shift(new PunchPair(
                    CreateTestPunch(Instant.FromUtc(2023, 1, 1, 9, 0), PunchType.In),
                    CreateTestPunch(Instant.FromUtc(2023, 1, 1, 17, 0), PunchType.Out)
                )), // 8 hours
                new Shift(new PunchPair(
                    CreateTestPunch(Instant.FromUtc(2023, 1, 2, 9, 0), PunchType.In),
                    CreateTestPunch(Instant.FromUtc(2023, 1, 2, 18, 0), PunchType.Out)
                )) // 9 hours (1 hour overtime)
            };
            var week = CreateTestWeek(shifts);

            // Act
            var result = _calculator.CalculateOvertime(week, _employee);

            // Assert - 1 hour overtime at 1.5x rate = $22.50
            Assert.Equal(22.50, result);
        }

        [Fact]
        public void CalculateOvertime_WhenEmployeeHasHigherMinimumWage_ReturnsCorrectAmount()
        {
            // Arrange
            var employee = new Employee
            {
                MinimumWage = 20.00,
                Name = "Test Employee"
            };

            var shifts = new List<Shift>
            {
                new Shift(new PunchPair(
                    CreateTestPunch(Instant.FromUtc(2023, 1, 1, 9, 0), PunchType.In),
                    CreateTestPunch(Instant.FromUtc(2023, 1, 1, 18, 0), PunchType.Out)
                ))
            };
            var week = CreateTestWeek(shifts);

            // Act
            var result = _calculator.CalculateOvertime(week, employee);

            // Assert - 1 hour overtime at 1.5x rate ($30/hour) = $45.00
            Assert.Equal(45.00, result);
        }

        [Fact]
        public void CalculateOvertime_WhenShiftExceedsMaxButNotByMuch_ReturnsOneCompletePair()
        {
            // Arrange
            var shifts = new List<Shift>
            {
                new Shift(new PunchPair(
                    CreateTestPunch(Instant.FromUtc(2023, 1, 1, 9, 0), PunchType.In),
                    CreateTestPunch(Instant.FromUtc(2023, 1, 1, 20, 0), PunchType.Out)
                ))
            };
            var week = CreateTestWeek(shifts);

            // Act
            var result = _calculator.CalculateOvertime(week, _employee);

            // Assert - 11 hours total, 7 hours overtime at 1.5x rate = $105.00
            Assert.Equal(105.00, result);
        }
    }
}
