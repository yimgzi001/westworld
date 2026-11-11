#nullable enable

using Game;

internal static class PlaySurvivalKnowledge
{
    public static object[] ForSituation(ComponentPlayer player, bool hasCamp, bool hasFurnace)
    {
        var notes = new List<object>();
        void Add(string topic, string advice) => notes.Add(new { topic, advice });
        Add("延续目标", "把长期意图保留在 goal，当前一至四步只是其中一段。已收集材料、放好的工具和剩余前置不要忘；只有新危险、前提失效或收益明显下降才改目标。");
        Add("食物账本", "比较当天消耗和次日启动成本。饱食高但没有可吃库存也不算供食稳定；一次南瓜或一次猎获只解决眼前，不等于建立了食物来源。");
        Add("时间取舍", "做任务要计入往返、制作、烤制和日落。先确保能活到下一次收获，再投入工具或建筑；不要为了补满目标库存无限追猎或绕路。");
        var vitals = player.ComponentVitalStats;
        var carry = player.ComponentMiner.Inventory;
        bool Has(Func<int, bool> predicate) => Enumerable.Range(0, carry.SlotsCount)
            .Any(slot => carry.GetSlotCount(slot) > 0 && predicate(carry.GetSlotValue(slot)));
        bool rawMeat = Has(PlaySurvivalFood.IsRawMeat);
        if (rawMeat)
            Add("已有生肉", hasFurnace
                ? "已有炉具和生肉时，优先比较返回烤制与继续追猎的收益。确认有燃料，取回熟食；饥饿时可把吃饭排在烹饪之后。"
                : "生肉尚不能算可靠口粮。若白天与饱食允许，可选采石、石斧、八圆石、工作台、熔炉及燃料的分段计划；快饿死时先取附近现成食物，别半途造炉断粮。");
        else if (PlayFoodBudget.NeedsSupply(player))
            Add("寻找主食", "先比较已见成熟南瓜和有肉鸟的可达性。木矛投猎鸭、乌鸦、海鸥；不要为麻雀鸽子白耗时间。附近资源用尽后选新方向巡猎，避开反复受阻与猎食者区域。");
        else
            Add("巩固供食", "有一天余粮可投入营地与续种。保留一部分南瓜应急，另一部分制种；肉按近期消耗烤制，避免熟食腐败。幼苗尚未成熟时仍需狩猎与采集。");
        if (vitals.Temperature < 8f || vitals.Wetness > 0.2f || player.ComponentFlu.HasFlu)
            Add("湿冷恢复", "先离开水体并到干地，结合体温变化决定生火、遮雨或取食。回暖没有改善就检查热源和距离，不能只反复等待；流感时预留恢复期间的食物。");
        if (player.Entity.FindComponent<ComponentOnFire>() is { IsOnFire: true } or { TouchesFire: true })
            Add("着火处理", "自身正在着火或接触火焰，先脱离火源，停止制作和等待。原生水浸过半可灭火，但只能走已确认可进出的浅水；附近无安全水体时在远离火源的安全处让火自然熄灭，观察生命值。");
        else
            Add("用火距离", "营火放在屋外不易蔓延的位置，与身体、进出路径、木墙和树叶保持距离；在旁边取暖即可，不要踩火。回暖过程中掉血不是成功，要检查火焰并离开危险位置。");
        Add(hasCamp ? "固定家园" : "选址与过夜", hasCamp
            ? "白天从营地外出并保留返程时间。安全睡眠只度过夜晚，醒来要重新检查门外、库存和天气；门外有狼且睡不着时不能靠反复睡眠解决。"
            : "早期临时土坑能过夜；有余粮和白天后可选四十块土或木板的二乘二小屋。选干燥平地，靠木石水源但不贴窄岸；房子要有屋顶、可封门口和安全出口。");
        return notes.ToArray();
    }
}
