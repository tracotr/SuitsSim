using System;
using TMPro;
using UnityEngine;

// Panel with EV1's vital telemetry as gauges. Both O2 tanks and both batteries are shown, and the battery in use reads UMB while on
// umbilical power. The scrubber shows whichever one is filling.
public class EvaDisplay : WristDisplayPanel
{
    [Header("References")]
    [Tooltip("EVA elapsed time, bottom right.")]
    [SerializeField] private TMP_Text elapsed;

    [SerializeField] private TelemetryGauge primaryOxygen;
    [SerializeField] private TelemetryGauge secondaryOxygen;
    [SerializeField] private TelemetryGauge primaryBattery;
    [SerializeField] private TelemetryGauge secondaryBattery;
    [SerializeField] private TelemetryGauge suitOxygen;
    [SerializeField] private TelemetryGauge suitPressure;
    [SerializeField] private TelemetryGauge helmetCo2;
    [SerializeField] private TelemetryGauge heartRate;
    [SerializeField] private TelemetryGauge scrubber;

    protected override void Refresh()
    {
        var sim = TssSim.Instance;
        var eva = sim.Eva1;

        primaryOxygen.Show(eva.primaryOxygenStorage.Value);
        secondaryOxygen.Show(eva.secondaryOxygenStorage.Value);

        bool umbilical = sim.GetSwitch(TssSwitch.DcuEva1Batt), onPrimaryBattery = sim.GetSwitch(TssSwitch.DcuEva1BattSource);
        primaryBattery.Show(eva.primaryBatteryLevel.Value, umbilical && onPrimaryBattery ? "UMB" : "");
        secondaryBattery.Show(eva.secondaryBatteryLevel.Value, umbilical && !onPrimaryBattery ? "UMB" : "");

        suitOxygen.Show(eva.suitPressureOxygen.Value);
        suitPressure.Show(eva.suitPressureTotal.Value);
        helmetCo2.Show(eva.helmetPressureCo2.Value);
        heartRate.Show(eva.heartRate.Value);

        bool scrubberA = sim.GetSwitch(TssSwitch.DcuEva1Co2);
        scrubber.Show((scrubberA ? eva.scrubberACo2Storage : eva.scrubberBCo2Storage).Value, scrubberA ? "A" : "B");

        elapsed.text = $"{TimeSpan.FromSeconds(eva.elapsedTime.Value):hh\\:mm\\:ss}";
    }
}
