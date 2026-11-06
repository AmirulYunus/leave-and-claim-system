using System;

namespace Leave.Plugins
{
    // The company document treats Medical and Hospitalisation as one 60-day
    // reporting figure. Keep the separate statutory Hospitalisation floor so
    // the company figure can never shorten a protected hospitalisation claim.
    public sealed class MedicalHospitalBalance
    {
        public int MedicalUsedHours { get; private set; }
        public int HospitalisationUsedHours { get; private set; }
        public int CompanyCombinedRemainingHours { get; private set; }
        public int StatutoryHospitalisationRemainingHours { get; private set; }

        public static MedicalHospitalBalance Calculate(int medicalEntitlementHours,
            int medicalUsedHours, int hospitalisationUsedHours)
        {
            const int hospitalisationHours = 60 * 8;
            if (medicalEntitlementHours < 0 || medicalEntitlementHours % 4 != 0 ||
                medicalUsedHours < 0 || medicalUsedHours % 4 != 0 ||
                medicalUsedHours > medicalEntitlementHours ||
                hospitalisationUsedHours < 0 || hospitalisationUsedHours % 4 != 0 ||
                hospitalisationUsedHours > hospitalisationHours)
                throw new ArgumentException("Verified Medical and Hospitalisation usage in half-day hours is required.");
            var combinedUsed = checked(medicalUsedHours + hospitalisationUsedHours);
            return new MedicalHospitalBalance {
                MedicalUsedHours = medicalUsedHours,
                HospitalisationUsedHours = hospitalisationUsedHours,
                CompanyCombinedRemainingHours = Math.Max(0, hospitalisationHours - combinedUsed),
                StatutoryHospitalisationRemainingHours = hospitalisationHours - hospitalisationUsedHours
            };
        }
    }
}
