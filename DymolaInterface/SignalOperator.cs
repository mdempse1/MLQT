namespace DymolaInterface;

/// <summary>
/// Dymola enumeration used by interface functions.
/// </summary>
public enum SignalOperator
{
    Min = 1,
    Max = 2,
    ArithmeticMean = 3,
    RectifiedMean = 4,
    RMS = 5,
    ACCoupledRMS = 6,
    SlewRate = 7,
    THD = 8,
    FirstHarmonic = 9
}
