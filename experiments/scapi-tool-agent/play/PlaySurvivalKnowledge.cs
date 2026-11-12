#nullable enable

using Game;

internal static class PlaySurvivalKnowledge
{
    public static object[] ForSituation(ComponentPlayer player, bool hasCamp, bool hasFurnace, bool distantWorkbench = false,
        PlaySurvivalPanorama.Sweep? environment = null)
    {
        var notes = new List<object>();
        void Add(string topic, string advice) => notes.Add(new { topic, advice });
        Add("延续目标", "把长期意图保留在 goal，当前一至四步只是其中一段。已收集材料、放好的工具和剩余前置不要忘；只有新危险、前提失效或收益明显下降才改目标。");
        Add("食物账本", "比较当天消耗和次日启动成本。饱食高但没有可吃库存也不算供食稳定；一次南瓜或一次猎获只解决眼前，不等于建立了食物来源。");
        Add("时间取舍", "做任务要计入往返、制作、烤制和日落。先确保能活到下一次收获，再投入工具或建筑；不要为了补满目标库存无限追猎或绕路。");
        Add("楼层与交互", "工作站摘要的 heightDifference 正数在上层、负数在下层；sameLevel 和 interactable 要分别核对。平面距离很近但不同层时，先走有落脚和净空证据的台阶或入口。localLayers 只列当前已证明的一格台阶，未知处不能直接跳；反复失败时比较三维返程与就地建台的时间。");
        var vitals = player.ComponentVitalStats;
        var carry = player.ComponentMiner.Inventory;
        bool Has(Func<int, bool> predicate) => Enumerable.Range(0, carry.SlotsCount)
            .Any(slot => carry.GetSlotCount(slot) > 0 && predicate(carry.GetSlotValue(slot)));
        bool rawMeat = Has(PlaySurvivalFood.IsRawMeat);
        int logs = PlaySurvivalProduction.Stock(player, PlaySurvivalProduction.Wood);
        int? visibleWood = environment?.Panorama.Blocks.Count(block => PlaySurvivalProduction.IsMaterial(block.Value, PlaySurvivalProduction.Wood));
        bool dusk = PlaySurvivalRest.NightCoverDue(player.Project.FindSubsystem<SubsystemTimeOfDay>(true));
        Add("目标冲突", "当前目标默认继续；新攻击、着火、落水先避险，来不及撑到任务结束的饥饿或湿冷先补救。普通机会先记住，比较收益、时间、已完成投入与返程成本后再决定是否换目标；切换时在 goal 写明原因并保留已有材料。");
        if (logs >= 8)
            Add("木材已备", "原木库存已达到八块储备目标，另有木板木棒可继续使用。优先比较把现有材料转成食物、工具或住处的收益，别把重复采木当作进展。");
        else if (visibleWood >= 8 && vitals.Food >= 0.35f && !dusk)
            Add("批量备料", "当前已见木材较多，饱食与天色允许时，可比较 stock_eight_wood 一次把原木库存补到八块，兼顾工作台、工具、炉火燃料和建设备用，减少反复往返。按下一趟任务留够余量，工具与燃料够用时继续主目标。已有石样时也可先制斧。附近危险或口粮迫近时先处理其成本。");
        else if (visibleWood is < 4)
            Add("资源稀疏", "当前已见木材少，按眼前用途留足工具与燃料；比较去新林地方向、把工位迁近资源或先取附近食物的成本，不要把反复短途空跑当成补给。");
        if (dusk)
            Add("临近过夜", "日落前把返程、封门、烤制或遮雨时间算进去，少量紧急补给与大批备料分开评估；新发现的远处资源可以记住留到白天。");
        if (environment is { FrozenGround.Length: > 0 } && !PlaySurvivalCold.CanEnterSnow(player))
            Add("当前地形", "实际观察范围里有雪或冰，当前防寒不足；比较已有干暖路线上的资源，避免为了树木或猎物主动深入雪地。");
        if (distantWorkbench)
            Add("就近工位", "记忆中的工作台已在四十格外，当前不能直接操作它。已有石样或生肉时，比较就近补木、做新工作台，再制石斧与炉具；不必为了旧台放弃当天口粮。新台应靠近资源与安全住处，留足原料和燃料。");
        if (rawMeat)
            Add("已有生肉", hasFurnace
                ? "已有炉具和生肉时，优先比较返回烤制与继续追猎的收益。确认有燃料，取回熟食；饥饿时可把吃饭排在烹饪之后。"
                : "生肉是现有加工原料：熟鸟肉营养0.5、烤肉0.8，先比较建立炉具的收益，别永远先追下一只猎物。看不到露天石料可选 quarry，最多六格，先核对回跳再取样；已见岩石但没圆石时可选 collect_one_cobblestone，先取一块再比较制斧与批量采八块。已有石样则比较补木与就近工作台、石斧、八圆石和炉具。饱食不超过4%、尚未患病且无更安全食物时才冒原生风险吃生肉救急。");
        else if (PlayFoodBudget.NeedsSupply(player))
            Add("寻找主食", "先比较已见成熟南瓜和有肉鸟的可达性。木矛投猎鸭、乌鸦、海鸥；不要为麻雀鸽子白耗时间。附近资源用尽后选新方向巡猎，避开反复受阻与猎食者区域。");
        else
            Add("巩固供食", "有一天余粮可投入营地与续种。保留一部分南瓜应急，另一部分制种；肉按近期消耗烤制，避免熟食腐败。幼苗尚未成熟时仍需狩猎与采集。");
        if (vitals.Temperature < 8f || vitals.Wetness > 0.2f || player.ComponentFlu.HasFlu)
            Add("湿冷恢复", "先离水，结合实际变化决定遮雨、回营、取食或生火。持续淋雨时露天火不能保证晾干，体温已安全却仍湿透时先找屋顶，别反复添柴等干。已有燃烧营火时可返回、按余量添柴，再选 warm_at_own_campfire 连续恢复。初放营火三档，雨中每五秒减一档，干燥约三十秒减一档；熄灭后添柴不能复燃。");
        if (player.Entity.FindComponent<ComponentOnFire>() is { IsOnFire: true } or { TouchesFire: true })
            Add("着火处理", "自身正在着火或接触火焰，先脱离火源，停止制作和等待。原生水浸过半可灭火，但只能走已确认可进出的浅水；附近无安全水体时在远离火源的安全处让火自然熄灭，观察生命值。");
        else
            Add("用火距离", "营火放在屋外不易蔓延的位置，与身体、进出路径、木墙和树叶保持距离；在旁边取暖即可，不要踩火。回暖过程中掉血不是成功，要检查火焰并离开危险位置。");
        Add(hasCamp ? "固定家园" : "选址与过夜", hasCamp
            ? "白天从营地外出并保留返程时间。安全睡眠只度过夜晚，醒来要重新检查门外、库存和天气；门外有狼且睡不着时不能靠反复睡眠解决。"
            : "早期临时土坑能过夜；有余粮和白天后可选四十块土或木板的二乘二小屋。选干燥平地，靠木石水源但不贴窄岸；房子要有屋顶、可封门口和安全出口。");
        return notes.ToArray();
    }

    public static object ResourcePlanning(ComponentPlayer player, PlaySurvivalPanorama.Sweep? environment)
    {
        var carry = player.ComponentMiner.Inventory;
        int Ingredient(string id) => Enumerable.Range(0, carry.SlotsCount).Where(slot => carry.GetSlotCount(slot) > 0
            && BlocksManager.Blocks[Terrain.ExtractContents(carry.GetSlotValue(slot))].GetCraftingId(carry.GetSlotValue(slot)) == id)
            .Sum(carry.GetSlotCount);
        return new
        {
            rawLogs = PlaySurvivalProduction.Stock(player, PlaySurvivalProduction.Wood), planks = Ingredient("planks"), sticks = Ingredient("stick"),
            smallWoodTrip = "连续取得三原木，适合眼前工具与燃料", reserveWoodTrip = "把现有原木库存补至八块，已有原木计入，不是再采八块",
            observedWoodBlocks = environment?.Panorama.Blocks.Count(block => PlaySurvivalProduction.IsMaterial(block.Value, PlaySurvivalProduction.Wood)),
            observedStoneBlocks = environment?.Panorama.Blocks.Count(block => PlaySurvivalProduction.IsMaterial(block.Value, PlaySurvivalProduction.Stone)),
            observedFoodTargets = environment?.FoodTargets.Length, observedFrozenGround = environment?.FrozenGround.Length,
            note = "只统计本人已观察资源，不保证可达或足够。结合饱食、危险、天色、工位和返程比较短途采集、批量备料、就地发展或迁移。"
        };
    }
}
