// Pure acceptance contract, not a food-selection policy.
public sealed record NaturalFoodMeal(Guid RequestId,long? ReceiptSequence,Guid? RunId,int Value,int InitialCount,int AcquiredCount,int InventoryBefore,int InventoryAfter,int EventCount,bool MatchingEvents,float Nutrition,float FoodBefore,float FoodAfter,long StatsBefore,long StatsAfter,string? NativeStatus);
public static class NaturalFoodEvidence {
 public static bool Verified(NaturalFoodMeal m)=>m.RequestId!=Guid.Empty&&m.ReceiptSequence>0&&m.RunId is Guid run&&run!=Guid.Empty&&m.Value>=0&&m.InitialCount==0&&m.AcquiredCount>0&&m.InventoryBefore>0&&m.InventoryAfter>=0&&m.EventCount>0&&m.EventCount<=m.AcquiredCount&&m.InventoryBefore-m.InventoryAfter==m.EventCount&&m.MatchingEvents&&float.IsFinite(m.Nutrition)&&m.Nutrition>0&&float.IsFinite(m.FoodBefore)&&float.IsFinite(m.FoodAfter)&&m.FoodAfter>m.FoodBefore&&m.StatsAfter-m.StatsBefore==m.EventCount&&m.NativeStatus=="succeeded";
}
