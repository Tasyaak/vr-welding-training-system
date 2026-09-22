using System;

namespace WeldingTrainer.Domain
{
    public static class NozzleCompatibility
    {
        public const string WeldingNozzleId="welding-nozzle";
        public const string CleaningNozzleId="cleaning-nozzle";
        public static bool IsCompatible(ProcessMode mode,string nozzleId)
        {
            string required=mode switch
            { ProcessMode.Fusion=>WeldingNozzleId,ProcessMode.Wobble=>WeldingNozzleId,
              ProcessMode.Pulsed=>WeldingNozzleId,ProcessMode.PreWeldCleaning=>CleaningNozzleId,
              ProcessMode.PostWeldCleaning=>CleaningNozzleId,_=>null };
            return required!=null&&string.Equals(required,nozzleId,StringComparison.Ordinal);
        }
    }
}
