using System;

namespace Leave.Plugins
{
    // The written exception is captured during the closing year. It cannot be
    // invented during January's credit confirmation.
    public static class CarryForwardExceptionPolicy
    {
        public static void Validate(int sourceYear, DateTime recordedMalaysiaDate,
            int approvedLimitHours, string documentReference, Guid superiorEntraId)
        {
            BalanceLedger.CalendarDate(recordedMalaysiaDate);
            if (recordedMalaysiaDate < new DateTime(sourceYear, 10, 1) ||
                recordedMalaysiaDate > new DateTime(sourceYear, 12, 15))
                throw new InvalidOperationException("Written carry-forward exceptions must reach HR from 1 October through 15 December of the closing year.");
            if (approvedLimitHours <= 40 || approvedLimitHours % 4 != 0 || approvedLimitHours > 1000)
                throw new ArgumentException("The Superior-approved limit must exceed five days in half-day increments.");
            if (superiorEntraId == Guid.Empty)
                throw new ArgumentException("The current immediate Superior must be verified.");
            if (string.IsNullOrWhiteSpace(documentReference) || documentReference.Trim().Length > 200)
                throw new ArgumentException("A written Superior approval document reference is required.");
        }
    }
}
