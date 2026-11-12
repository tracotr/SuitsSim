using System;
using TMPro;
using UnityEngine;
using static TssSwitch;

// Panel with all of EV1's telemetry. The vital resources sit at the top as rings (O2 tanks, batteries), the suit atmosphere and vitals
// below as bars, and the support systems (scrubbers, coolant, fans, gas rates) smaller at the bottom.
// Whatever the DCU has selected is tagged ACTIVE (UMB for a battery on umbilical power), and the fan that's switched off goes grey.
public class EvaDisplay : WristDisplayPanel
{
    [Header("References")]
    [Tooltip("EVA elapsed time, bottom right.")]
    [SerializeField] private TMP_Text elapsed;

    [Header("Resources")]
    [SerializeField] private TelemetryGauge primaryOxygen;
    [SerializeField] private TelemetryGauge secondaryOxygen;
    [SerializeField] private TelemetryGauge primaryOxygenPressure;
    [SerializeField] private TelemetryGauge secondaryOxygenPressure;
    [SerializeField] private TelemetryGauge primaryBattery;
    [SerializeField] private TelemetryGauge secondaryBattery;

    [Header("Suit Pressure")]
    [SerializeField] private TelemetryGauge suitOxygen;
    [SerializeField] private TelemetryGauge suitCo2;
    [SerializeField] private TelemetryGauge suitOther;
    [SerializeField] private TelemetryGauge suitTotal;

    [Header("Vitals")]
    [SerializeField] private TelemetryGauge heartRate;
    [SerializeField] private TelemetryGauge temperature;
    [SerializeField] private TelemetryGauge helmetCo2;

    [Header("Systems")]
    [SerializeField] private TelemetryGauge scrubberA;
    [SerializeField] private TelemetryGauge scrubberB;
    [SerializeField] private TelemetryGauge coolant;
    [SerializeField] private TelemetryGauge primaryFan;
    [SerializeField] private TelemetryGauge secondaryFan;
    [SerializeField] private TelemetryGauge coolantLiquid;
    [SerializeField] private TelemetryGauge coolantGas;
    [SerializeField] private TelemetryGauge oxygenConsumption;
    [SerializeField] private TelemetryGauge co2Production;

    protected override void Refresh()
    {
        var sim = TssSim.Instance;
        var eva = sim.Eva1;

        bool primaryOxy = sim.GetSwitch(DcuEva1Oxy);
        primaryOxygen.Show(eva.primaryOxygenStorage.Value, primaryOxy ? "ACTIVE" : "");
        secondaryOxygen.Show(eva.secondaryOxygenStorage.Value, primaryOxy ? "" : "ACTIVE");
        primaryOxygenPressure.Show(eva.primaryOxygenPressure.Value);
        secondaryOxygenPressure.Show(eva.secondaryOxygenPressure.Value);

        bool primaryBatt = sim.GetSwitch(DcuEva1BattSource);
        string battTag = sim.GetSwitch(DcuEva1Batt) ? "UMB" : "ACTIVE";
        primaryBattery.Show(eva.primaryBatteryLevel.Value, primaryBatt ? battTag : "");
        secondaryBattery.Show(eva.secondaryBatteryLevel.Value, primaryBatt ? "" : battTag);

        suitOxygen.Show(eva.suitPressureOxygen.Value);
        suitCo2.Show(eva.suitPressureCo2.Value);
        suitOther.Show(eva.suitPressureOther.Value);
        suitTotal.Show(eva.suitPressureTotal.Value);

        heartRate.Show(eva.heartRate.Value);
        temperature.Show(eva.temperature.Value);
        helmetCo2.Show(eva.helmetPressureCo2.Value);

        bool scrubbingA = sim.GetSwitch(DcuEva1Co2);
        scrubberA.Show(eva.scrubberACo2Storage.Value, scrubbingA ? "ACTIVE" : "");
        scrubberB.Show(eva.scrubberBCo2Storage.Value, scrubbingA ? "" : "ACTIVE");
        coolant.Show(eva.coolantStorage.Value);

        bool primaryFanOn = sim.GetSwitch(DcuEva1Fan);
        primaryFan.Show(eva.primaryFanRpm.Value, idle: !primaryFanOn);
        secondaryFan.Show(eva.secondaryFanRpm.Value, idle: primaryFanOn);
        coolantLiquid.Show(eva.coolantLiquidPressure.Value);
        coolantGas.Show(eva.coolantGasPressure.Value);
        oxygenConsumption.Show(eva.oxygenConsumption.Value);
        co2Production.Show(eva.co2Production.Value);

        elapsed.text = $"{TimeSpan.FromSeconds(eva.elapsedTime.Value):hh\\:mm\\:ss}";
    }
}
