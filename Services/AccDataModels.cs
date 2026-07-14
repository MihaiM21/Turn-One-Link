using System.Runtime.InteropServices;

namespace Turn_One_Link.Services.AccTelemetry;

[StructLayout(LayoutKind.Sequential)]
public struct Coordinates
{
    public float X;
    public float Y;
    public float Z;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Vec4
{
    public float Fl;
    public float Fr;
    public float Rl;
    public float Rr;
}

[StructLayout(LayoutKind.Sequential, Pack = 4, CharSet = CharSet.Unicode)]
public struct SPageFilePhysics
{
    public int PacketId;
    public float Gas;
    public float Brake;
    public float Fuel;
    public int Gear;
    public int Rpms;
    public float SteerAngle;
    public float SpeedKmh;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
    public float[] Velocity;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
    public float[] AccG;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] WheelSlip;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] WheelLoad;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] WheelsPressure;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] WheelAngularSpeed;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] TyreWear;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] TyreDirtyLevel;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] TyreCoreTemperature;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] CamberRad;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] SuspensionTravel;

    public float Drs;
    public float TC;
    public float Heading;
    public float Pitch;
    public float Roll;
    public float CgHeight;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
    public float[] CarDamage;

    public int NumberOfTyresOut;
    public int PitLimiterOn;
    public float Abs;

    public float KersCharge;
    public float KersInput;
    public int AutoShifterOn;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
    public float[] RideHeight;

    public float TurboBoost;
    public float Ballast;
    public float AirDensity;

    public float AirTemp;
    public float RoadTemp;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
    public float[] LocalAngularVelocity;
    public float FinalFF;

    public float PerformanceMeter;
    public int EngineBrake;
    public int ErsRecoveryLevel;
    public int ErsPowerLevel;
    public int ErsHeatCharging;
    public int ErsisCharging;
    public float KersCurrentKJ;
    public int DrsAvailable;
    public int DrsEnabled;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] BrakeTemp;

    public float Clutch;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] TyreTempI;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] TyreTempM;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] TyreTempO;

    public int IsAIControlled;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public Coordinates[] TyreContactPoint;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public Coordinates[] TyreContactNormal;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public Coordinates[] TyreContactHeading;
    public float BrakeBias;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
    public float[] LocalVelocity;

    // --- ACC Specific Additions ---
    public int P2PActivation;
    public int P2PStatus;
    public float CurrentMaxRpm;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] Mz;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] Fx;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] Fy;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] SlipRatio;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] SlipAngle;
    
    public int TcInAction;
    public int AbsInAction;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] SuspensionDamage;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] TyreTemp;
    
    public float WaterTemp;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] BrakePressure;

    public int FrontBrakeCompound;
    public int RearBrakeCompound;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] PadLife;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] DiscLife;

    public int IgnitionOn;
    public int StarterEngineOn;
    public int IsEngineRunning;
    public float KerbVibration;
    public float SlipVibrations;
    public float GVibrations;
    public float AbsVibrations;
}

public enum AC_FLAG_TYPE
{
    AC_NO_FLAG = 0,
    AC_BLUE_FLAG = 1,
    AC_YELLOW_FLAG = 2,
    AC_BLACK_FLAG = 3,
    AC_WHITE_FLAG = 4,
    AC_CHECKERED_FLAG = 5,
    AC_PENALTY_FLAG = 6
}

public enum AC_STATUS
{
    AC_OFF = 0,
    AC_REPLAY = 1,
    AC_LIVE = 2,
    AC_PAUSE = 3
}

public enum AC_SESSION_TYPE
{
    AC_UNKNOWN = -1,
    AC_PRACTICE = 0,
    AC_QUALIFY = 1,
    AC_RACE = 2,
    AC_HOTLAP = 3,
    AC_TIME_ATTACK = 4,
    AC_DRIFT = 5,
    AC_DRAG = 6
}

public enum PenaltyType
{
    None = 0,
    DriveThrough_Cutting = 1,
    StopAndGo_10_Cutting = 2,
    StopAndGo_20_Cutting = 3,
    StopAndGo_30_Cutting = 4,
    Disqualified_Cutting = 5,
    RemoveBestLaptime_Cutting = 6,
    DriveThrough_PitSpeeding = 7,
    StopAndGo_10_PitSpeeding = 8,
    StopAndGo_20_PitSpeeding = 9,
    StopAndGo_30_PitSpeeding = 10,
    Disqualified_PitSpeeding = 11,
    RemoveBestLaptime_PitSpeeding = 12,
    Disqualified_IgnoredMandatoryPit = 13,
    PostRaceTime = 14,
    Disqualified_Trolling = 15,
    Disqualified_PitEntry = 16,
    Disqualified_PitExit = 17,
    Disqualified_WrongWay = 18,
    DriveThrough_IgnoredDriverStint = 19,
    Disqualified_IgnoredDriverStint = 20,
    Disqualified_ExceededDriverStintLimit = 21,
}

public enum TrackGripStatus
{
    Green = 0,
    Fast = 1,
    Optimum = 2,
    Greasy = 3,
    Damp = 4,
    Wet = 5,
    Flooded = 6
}

public enum RainIntensity
{
    NoRain = 0,
    Drizzle = 1,
    LightRain = 2,
    MediumRain = 3,
    HeavyRain = 4,
    Thunderstorm = 5
}

[StructLayout(LayoutKind.Sequential, Pack = 4, CharSet = CharSet.Unicode)]
public struct SPageFileGraphic
{
    public int PacketId;
    public AC_STATUS Status;
    public AC_SESSION_TYPE Session;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 15)]
    public string CurrentTime;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 15)]
    public string LastTime;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 15)]
    public string BestTime;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 15)]
    public string Split;
    public int CompletedLaps;
    public int Position;
    public int iCurrentTime;
    public int iLastTime;
    public int iBestTime;
    public float SessionTimeLeft;
    public float DistanceTraveled;
    public int IsInPit;
    public int CurrentSectorIndex;
    public int LastSectorTime;
    public int NumberOfLaps;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)]
    public string TyreCompound;

    public float ReplayTimeMultiplier;
    public float NormalizedCarPosition;
    public int ActiveCars;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 60)]
    public Coordinates[] CarCoordinates;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 60)]
    public int[] CarIDs;

    public int PlayerCarID;
    public float PenaltyTime;
    public AC_FLAG_TYPE Flag;
    public PenaltyType Penalty;
    public int IdealLineOn;
    public int IsInPitLane;
    public float SurfaceGrip;
    public int MandatoryPitDone;
    
    // --- ACC Specific Additions ---
    public float WindSpeed;
    public float WindDirection;
    public int IsSetupMenuVisible;
    public int MainDisplayIndex;
    public int SecondaryDisplayIndex;
    public int Tc;
    public int TcCut;
    public int EngineMap;
    public int Abs;
    public float FuelXLap;
    public int RainLights;
    public int FlashingLights;
    public int LightsStage;
    public float ExhaustTemperature;
    public int WiperLV;
    public int DriverStintTotalTimeLeft;
    public int DriverStintTimeLeft;
    public int RainTyres;
    public int SessionIndex;
    public float UsedFuel;
    
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 15)]
    public string DeltaLapTime;
    public int IDeltaLapTime;
    
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 15)]
    public string EstimatedLapTime;
    public int IEstimatedLapTime;
    public int IsDeltaPositive;
    public int ISplit;
    public int IsValidLap;
    public float FuelEstimatedLaps;
    
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)]
    public string TrackStatus;
    
    public int MissingMandatoryPits;
    public float Clock;
    public int DirectionLightsLeft;
    public int DirectionLightsRight;
    public int GlobalYellow;
    public int GlobalYellow1;
    public int GlobalYellow2;
    public int GlobalYellow3;
    public int GlobalWhite;
    public int GlobalGreen;
    public int GlobalChequered;
    public int GlobalRed;
    public int MfdTyreSet;
    public float MfdFuelToAdd;
    
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] MfdTyrePressure;

    public TrackGripStatus trackGripStatus;
    public RainIntensity RainIntensity;
    public RainIntensity RainIntensityIn10min;
    public RainIntensity RainIntensityIn30min;
    public int CurrentTyreSet;
    public int StrategyTyreSet;
    public int GapAhead;
    public int GapBehind;
}

[StructLayout(LayoutKind.Sequential, Pack = 4, CharSet = CharSet.Unicode)]
public struct SPageFileStatic
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 15)]
    public string SMVersion;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 15)]
    public string ACVersion;

    public int NumberOfSessions;
    public int NumCars;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)]
    public string CarModel;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)]
    public string Track;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)]
    public string PlayerName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)]
    public string PlayerSurname;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)]
    public string PlayerNick;

    public int SectorCount;
    public float MaxTorque;
    public float MaxPower;
    public int MaxRpm;
    public float MaxFuel;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] SuspensionMaxTravel;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] TyreRadius;

    public float MaxTurboBoost;
    public float Deprecated1;
    public float Deprecated2;
    public int PenaltiesEnabled;
    public float AidFuelRate;
    public float AidTireRate;
    public float AidMechanicalDamage;
    public int AidAllowTyreBlankets;
    public float AidStability;
    public int AidAutoClutch;
    public int AidAutoBlip;

    public int HasDRS;
    public int HasERS;
    public int HasKERS;
    public float KersMaxJoules;
    public int EngineBrakeSettingsCount;
    public int ErsPowerControllerCount;
    public float TrackSPlineLength;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 15)]
    public string TrackConfiguration;

    public float ErsMaxJ;
    public int IsTimedRace;
    public int HasExtraLap;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)]
    public string CarSkin;
    public int ReversedGridPositions;
    public int PitWindowStart;
    public int PitWindowEnd;
    
    // --- ACC Specific Additions ---
    public int IsOnline;
    
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)]
    public string dryTyresName;
    
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)]
    public string wetTyresName;
}
