using BrewYou.ApiService.Data.Entities;

namespace BrewYou.ApiService.Services;

public enum BatchMetricSourceKind
{
    None,
    Sensor,
    Fermenter,
    StageVessel
}

public sealed record BatchMetricSourceResolution(
    BatchMetricSourceKind SourceKind,
    IReadOnlyList<Equipment> Sensors,
    Equipment? PrimaryVessel,
    Equipment? PrimarySensor);

/// <summary>
/// Shared telemetry metric source and sensor-precedence resolver used across
/// TelemetryService and BatchService DTO projection.
/// </summary>
public static class BatchTelemetryResolver
{
    public static bool IsCellarStage(BrewStage stage) =>
        stage is BrewStage.Ferment or BrewStage.Condition;

    public static IReadOnlyList<BatchSensorAssignment> GetStageScopedSensorAssignments(Batch batch, BrewStage stage)
    {
        if (batch.SensorAssignments == null || batch.SensorAssignments.Count == 0)
        {
            return [];
        }

        return batch.SensorAssignments
            .Where(sa => sa.Stage == stage || sa.Stage == null)
            .OrderBy(sa => sa.Stage == stage ? 0 : 1)
            .ThenBy(sa => sa.CreatedAt)
            .ThenBy(sa => sa.Id)
            .ToList();
    }

    public static BatchMetricSourceResolution ResolveMetricSource(Batch batch, BrewStage stage)
    {
        var stageAssignments = GetStageScopedSensorAssignments(batch, stage);
        var stageSensors = stageAssignments
            .Where(sa => sa.Equipment != null)
            .Select(sa => sa.Equipment!)
            .DistinctBy(e => e.Id)
            .ToList();

        if (IsCellarStage(stage))
        {
            if (stageAssignments.Count > 0)
            {
                return new BatchMetricSourceResolution(
                    BatchMetricSourceKind.Sensor,
                    stageSensors,
                    null,
                    stageSensors.FirstOrDefault());
            }

            if (batch.Fermenter != null)
            {
                return new BatchMetricSourceResolution(
                    BatchMetricSourceKind.Fermenter,
                    [],
                    batch.Fermenter,
                    null);
            }

            return new BatchMetricSourceResolution(BatchMetricSourceKind.None, [], null, null);
        }

        var primaryVessel = stage switch
        {
            BrewStage.Mash => (batch.MashTun?.CurrentTemperatureC.HasValue == true ? batch.MashTun : null)
                              ?? (batch.Boiler?.CurrentTemperatureC.HasValue == true ? batch.Boiler : null)
                              ?? batch.MashTun
                              ?? batch.Boiler,
            BrewStage.Boil => batch.Boiler,
            BrewStage.Package => batch.PackagingVessel,
            _ => null
        };

        var sourceKind = primaryVessel != null
            ? BatchMetricSourceKind.StageVessel
            : (stageAssignments.Count > 0 ? BatchMetricSourceKind.Sensor : BatchMetricSourceKind.None);

        return new BatchMetricSourceResolution(
            sourceKind,
            stageSensors,
            primaryVessel,
            stageSensors.FirstOrDefault());
    }

    public static bool IsEquipmentSupersededBySensor(Batch batch, BrewStage stage, Guid equipmentId)
    {
        if (!IsCellarStage(stage))
        {
            return false;
        }

        var stageAssignments = GetStageScopedSensorAssignments(batch, stage);
        return stageAssignments.Count > 0 && !stageAssignments.Any(sa => sa.EquipmentId == equipmentId);
    }

    public static Equipment? ResolvePrimaryCellarSensor(Batch batch)
    {
        var preferredStage = batch.CurrentStage == BrewStage.Condition
            ? BrewStage.Condition
            : BrewStage.Ferment;

        return GetStageScopedSensorAssignments(batch, preferredStage)
            .Where(sa => sa.Equipment != null)
            .Select(sa => sa.Equipment!)
            .FirstOrDefault();
    }
}