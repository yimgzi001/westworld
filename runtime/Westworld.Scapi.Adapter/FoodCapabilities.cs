using Game;
using Westworld.Runtime.Core;
namespace Westworld.Scapi.Adapter;

public enum FoodRiskPolicy { ZeroReportedSickness, AllowReportedSickness }
public sealed record FoodCapability(int Contents,string Name,string Acquisition,bool ContainerReturn,bool TerrainHarvest);
public sealed record FoodAssessment(bool Eligible,string Reason,FoodCapability? Capability);

// Original SCAPI food catalogue, not evidence that a resource exists or was acquired.
// No terrain/global scans and no execution. The host must bind observed values to its
// observation ID and revalidate nutrition, sickness, inventory and reach at execution.
public static class FoodCapabilities {
    public static IReadOnlyList<FoodCapability> Catalog {get;}=Array.AsReadOnly(new[]{
        new FoodCapability(131,"Mature pumpkin","Visible mature plant -> native dig -> natural pickup",false,true),
        new FoodCapability(78,"Cooked bird","Observed inventory/drop; cooking or native loot required",false,false),
        new FoodCapability(89,"Cooked meat","Observed inventory/drop; cooking or native loot required",false,false),
        new FoodCapability(162,"Cooked fish","Observed inventory/drop; cooking or native loot required",false,false),
        new FoodCapability(177,"Bread","Observed inventory/drop; ingredients and cooking required",false,false),
        new FoodCapability(77,"Raw bird","Observed corpse loot; hunting is a separate unverified route",false,false),
        new FoodCapability(88,"Raw meat","Observed corpse loot; hunting is a separate unverified route",false,false),
        new FoodCapability(161,"Raw fish","Observed corpse loot; hunting is a separate unverified route",false,false),
        new FoodCapability(118,"Laid egg","Observed natural laid drop; species determines nutrition",false,false),
        new FoodCapability(226,"Sea urchin","Visible underwater block; collision injury and sickness risks",false,true),
        new FoodCapability(110,"Milk bucket","Milking requires a bucket; native return-container accounting",true,false),
        new FoodCapability(251,"Pumpkin soup bucket","Prepared food; native return-container accounting",true,false)
    });
    public static FoodCapability? Describe(int value)=>value<0?null:Catalog.FirstOrDefault(f=>f.Contents==Terrain.ExtractContents(value));

    // Nutrition must be the actual observation-time native GetNutritionalValue;
    // sickness must be GetSicknessProbability on that same full encoded value.
    // SlotObservation.CanEat is deliberately not reused: earlier hosts used a
    // pumpkin-only allowlist there. This result never authorizes execution alone.
    public static FoodAssessment AssessObservedItem(SlotObservation item,float sickness,
        FoodRiskPolicy policy=FoodRiskPolicy.ZeroReportedSickness) {
        ArgumentNullException.ThrowIfNull(item);
        if(!Enum.IsDefined(policy))throw new ArgumentOutOfRangeException(nameof(policy));
        var food=Describe(item.Value);
        if(item.Slot<0||item.Count<=0||!float.IsFinite(item.Nutrition)||item.Nutrition<=0||!float.IsFinite(sickness)||sickness<0||sickness>1)
            return new(false,"invalid_or_non_nutritious_observation",food);
        if(food is null)return new(false,"outside_reviewed_catalogue",null);
        if(food.ContainerReturn)return new(false,"container_accounting_not_enabled",food);
        if(food.Contents==131&&BasePumpkinBlock.GetSize(Terrain.ExtractData(item.Value))!=7)return new(false,"immature_pumpkin",food);
        if(food.Contents==118&&!EggBlock.GetIsLaid(Terrain.ExtractData(item.Value)))return new(false,"egg_not_marked_naturally_laid",food);
        if(sickness>0&&policy==FoodRiskPolicy.ZeroReportedSickness)return new(false,"reported_sickness_risk",food);
        return new(true,"observed_edible_item_requires_native_revalidation",food);
    }

    // This is solely a native block-harvest classification. The default route is
    // still pumpkin. Urchin needs an explicit aquatic hazard-capable executor;
    // enabling food sickness risk does not enable underwater movement or injury.
    public static FoodAssessment AssessObservedHarvest(CellObservation cell,bool aquaticHazardsSupported=false) {
        ArgumentNullException.ThrowIfNull(cell);
        if(cell.Visibility!="known"||cell.Value is not int value||cell.X < -10_000_000||cell.X>10_000_000
            ||cell.Z < -10_000_000||cell.Z>10_000_000||cell.Y<0||cell.Y>=256)
            return new(false,"not_a_valid_visible_cell",null);
        var food=Describe(value);
        if(food is null||!food.TerrainHarvest)return new(false,"not_a_reviewed_food_plant",food);
        if(food.Contents==131)return BasePumpkinBlock.GetSize(Terrain.ExtractData(value))==7
            ?new(true,"mature_pumpkin_native_drop_changes_dead_flag",food):new(false,"immature_pumpkin",food);
        return aquaticHazardsSupported?new(true,"urchin_harvest_classification_only_native_hazards_require_revalidation",food)
            :new(false,"aquatic_collision_hazards_not_enabled",food);
    }
}
