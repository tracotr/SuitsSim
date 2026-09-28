namespace SuitsSim.Tss
{
    /// <summary>
    /// TSS UDP command numbers, copied from TSS2026 src/data.h (udp_command_mappings).
    /// When TSS2027 is released, diff its data.h against this file first; this is
    /// the only place command numbers should live.
    /// </summary>
    public static class TssCommand
    {
        // GET: response is the raw JSON file, no header.
        public const uint GetRover = 0;
        public const uint GetEva = 1;
        public const uint GetLtv = 2;
        public const uint GetLtvErrors = 3;   // only the Recovery Mode entry until 4800 is resolved

        // UIA (physical panel at the HAB). Bools are sent as float 0.0 / 1.0.
        public const uint UiaEva1Power = 2001;
        public const uint UiaEva1Oxy = 2002;
        public const uint UiaEva1WaterSupply = 2003;
        public const uint UiaEva1WaterWaste = 2004;
        public const uint UiaEva2Power = 2005;
        public const uint UiaEva2Oxy = 2006;
        public const uint UiaEva2WaterSupply = 2007;
        public const uint UiaEva2WaterWaste = 2008;
        public const uint UiaOxyVent = 2009;
        public const uint UiaDepress = 2010;

        // DCU (on the suit). TSS2026 only wires EV1's DCU.
        public const uint DcuEva1BattLu = 2011;  // true = umbilical (UIA) power, false = suit battery
        public const uint DcuEva1Oxy = 2012;     // primary vs secondary tank
        public const uint DcuEva1BattPs = 2013;  // primary vs secondary battery
        public const uint DcuEva1Fan = 2014;     // primary vs secondary fan
        public const uint DcuEva1Pump = 2015;    // coolant pump open vs closed
        public const uint DcuEva1Co2 = 2016;     // scrubber A vs B

        // IMU position, normally written by the TSS location app. The sim takes its place.
        public const uint ImuEva1PosX = 2017;
        public const uint ImuEva1PosY = 2018;
        public const uint ImuEva1Heading = 2019;
        public const uint ImuEva2PosX = 2020;
        public const uint ImuEva2PosY = 2021;
        public const uint ImuEva2Heading = 2022;

        // ltv_errors.error_procedures.{i}.needs_resolved for i = 0..7
        public const uint LtvErrorNeedsResolvedBase = 2023;
        public const int LtvErrorSlots = 8;

        // Registers the sender as the LTV task board so TSS forwards error changes to it.
        public const uint RegisterTaskBoard = 4000;

        public static uint ImuPosX(int eva) => eva == 2 ? ImuEva2PosX : ImuEva1PosX;
        public static uint ImuPosY(int eva) => eva == 2 ? ImuEva2PosY : ImuEva1PosY;
        public static uint ImuHeading(int eva) => eva == 2 ? ImuEva2Heading : ImuEva1Heading;

        public static uint LtvErrorNeedsResolved(int index)
        {
            if (index < 0 || index >= LtvErrorSlots)
                throw new System.ArgumentOutOfRangeException(nameof(index), $"TSS only maps error slots 0..{LtvErrorSlots - 1}");
            return LtvErrorNeedsResolvedBase + (uint)index;
        }

        public static bool IsGet(uint command) => command < 1000;
    }
}
