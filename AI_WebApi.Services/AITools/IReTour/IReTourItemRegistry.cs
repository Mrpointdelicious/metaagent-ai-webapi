namespace AI_WebApi.Services.AITools.IReTour;

/// <summary>
/// IReTour item registry v1 draft. Canonical codes are neutral identifiers;
/// display names are draft-level and must be re-signed by the device vendor
/// before patient-facing publication. Unknown item ids fail closed.
/// </summary>
public sealed class IReTourItemDefinition
{
    public int ItemId { get; init; }
    public string CanonicalCode { get; init; }
    public string DisplayName { get; init; }
    public string SchemaFamily { get; init; }
    public string RegistryVersion { get; init; } = "iretour-item-registry-1.0.0";
}

public sealed class IReTourItemRegistry
{
    public const string UnknownCode = "unknown_item";

    private static readonly IReadOnlyDictionary<int, IReTourItemDefinition> Items =
        new Dictionary<int, IReTourItemDefinition>
        {
            [0] = new()
            {
                ItemId = 0,
                CanonicalCode = "straight_primary",
                DisplayName = "直行训练",
                SchemaFamily = "straight_summary_v1"
            },
            [1] = new()
            {
                ItemId = 1,
                CanonicalCode = "straight_secondary",
                DisplayName = "直行训练（次级）",
                SchemaFamily = "straight_summary_v1"
            },
            [2] = new()
            {
                ItemId = 2,
                CanonicalCode = "reaction_primary",
                DisplayName = "反应训练",
                SchemaFamily = "reaction_summary_v1"
            },
            [3] = new()
            {
                ItemId = 3,
                CanonicalCode = "reaction_secondary",
                DisplayName = "反应训练（次级）",
                SchemaFamily = "reaction_summary_v1"
            },
            [4] = new()
            {
                ItemId = 4,
                CanonicalCode = "balance_bridge",
                DisplayName = "平衡训练",
                SchemaFamily = "balance_summary_v1"
            },
            [5] = new()
            {
                ItemId = 5,
                CanonicalCode = "vibration",
                DisplayName = "振动训练",
                SchemaFamily = "vibration_summary_v1"
            },
            [6] = new()
            {
                ItemId = 6,
                CanonicalCode = "transverse",
                DisplayName = "横向训练",
                SchemaFamily = "transverse_summary_v1"
            },
            [7] = new()
            {
                ItemId = 7,
                CanonicalCode = "stride",
                DisplayName = "跨步训练",
                SchemaFamily = "stride_summary_v1"
            },
            [8] = new()
            {
                ItemId = 8,
                CanonicalCode = "sideway",
                DisplayName = "侧向训练",
                SchemaFamily = "sideway_summary_v1"
            },
            [9] = new()
            {
                ItemId = 9,
                CanonicalCode = "gait_assessment",
                DisplayName = "步态评估",
                SchemaFamily = "gait_assessment_summary_v1"
            }
        };

    public static IReTourItemDefinition? Resolve(int itemId) =>
        Items.TryGetValue(itemId, out var item) ? item : null;

    public static IReadOnlyList<IReTourItemDefinition> All() =>
        Items.Values.OrderBy(x => x.ItemId).ToList();
}
