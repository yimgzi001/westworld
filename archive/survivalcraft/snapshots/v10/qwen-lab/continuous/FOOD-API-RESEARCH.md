# 真实进食 API 静态核查

只读研究，没有注入食物、发送动作、重启或读取 Game.log。依据当前 `survivalcraft-lab/bot-runtime/Survivalcraft.dll` ILSpy反编译；参考源码版本有签名差异，不能直接照搬。

## 关键事实
1. 当前 DLL 的 `ComponentVitalStats.Eat(IInventory inventory, int slotIndex, int value)` 调用 `NetEat(...)`。它检查营养/疾病/饱腹，增加Food、饱食累积、可能触发疾病、增加FoodItemsEaten，但**并不减少库存**。直接调用会制造“吃了但物品没消耗”的假闭环。
2. `ComponentMiner.Use(Ray3)` 只按当前手持物品调用block behavior.OnUse，不能据此假定普通食物会被吃掉。通用食用入口实际属于库存拖到角色服装区域的处理逻辑。
3. 可复用原生完整事务：
   `InventorySlotWidget.HandleDragDrop(sourceInventory, sourceSlotIndex, DragMode.SingleItem, clothingInventory, 0, processingOnly: true, componentPlayer: player)`
   这是 public static，可在游戏更新线程直接调用，无需图形化拖拽。
4. target为该玩家 `ComponentClothing`（实现IInventory），营养物品`GetSlotProcessCapacity`返回1。原生HandleDragDrop先RemoveSlotItems(1)，调用ProcessSlotItems，再AddSlotItems归还处理结果。进食拒绝返还原物，桶食物返还空桶并保留data。
5. HandleDragDrop的true只表示进入处理分支，不等于吃成功！验收必须看食物值增长和真实库存变化。

## 建议接口（尚未部署）
`eat {slot, expectedValue, expectedCount, commandId}`：仅允许当前玩家真实库存中营养>0的物品；校验slot/count/value未变化、非死亡、无并发库存操作。只消费一次，不像移动命令每tick重复调用。commandId去重，过期重试不能再消费。

观察增加：Food、Stamina、Sleep、IsSick，以及每个非空槽的完整value、count、GetNutritionalValue(value)、GetSicknessProbability(value)、可读物品名称。保持health/air。不要仅按block id判断可食性，物品data包含成熟度/腐坏等状态。

调用前记下Food、FoodItemsEaten与源槽value/count；调用后记录同项和processed结果。普通食物成功条件为Food上升、FoodItemsEaten+1、对应真实物品净减1。桶类需检验原物变空桶，失败应无净损失。拒绝返回错误原因，不直接写Food/Health。

首版可只支持非桶、低疾病概率、库存已有的普通食物，避免容器和叠加边界。若调用过程中异常，不能盲目回滚Food或补物品；先检查实际结果、停止该角色并报告。

## 食物/真实资源缺口
- 检查时50角色真实非空背包合计只有衣物block203一件及沙block7三份，没有可食物。
- 当前观察到的2,257个去重附近方块中没有PumpkinBlock(131)。这仅覆盖已观察邻域，不代表整个世界没有食物。
- 当前DLL确认PumpkinBlock.Index=131；BasePumpkinBlock.GetNutritionalValue仅成熟size==7时>0；成熟南瓜通过正常dig有真实物品掉落，是优先考虑的无战斗采食路线。仍需在隔离世界真实寻找，不可生成南瓜冒充获取。
- 参考源码BlocksData（非当前运行时数据实测）示例：南瓜营养0.15、疾病概率0；生肉营养0.3、疾病概率0.25；熟肉0.8、面包0.5。这些数值不能写死，应从运行时block.GetNutritionalValue/GetSicknessProbability读取。
- 肉类需要狩猎/尸体掉落，熟食还需真实燃料、烹饪设施与配方，当前未验证。不要把生肉或种子直接当安全应急食物。

## 最小真实生存实验建议
保护50角色基线存档，单独克隆隔离实验世界。在正常游戏规则中寻找成熟南瓜→走到可挖范围→普通dig→拾取→背包+1→原生单物品食用事务→背包-1与Food上涨→继续观察饥饿下降、health/air。记录实际获取来源、时间与每步证据。若没有现成食物，阶段结论就是“接口已核实，真实食物获取尚未完成”，不注入物品填补。

饥饿已导致伤害的角色不能仅靠当前health<1急停策略完成进食恢复；后续应区分紧急生存技能与危险移动，只允许受伤时执行受控食用/停止，不允许绕过健康守护继续探索。

### 补充已核实类型ID
运行DLL另确认 Game.SeaUrchinBlock.Index=226，继承BottomSuckerBlock；参考数据有营养但疾病风险0.25，水下获取还需要air防护，不推荐作为首个生存食物。天然直接采食首选成熟南瓜131；不能保证未审阅模组没有其他食物。Rye等作物种子不是直接可食物，应以完整value调用运行时营养API为准。肉/蛋/奶涉及动物来源，不能当作地面天然可挖资源。

精确原生签名：
`public static bool Game.InventorySlotWidget.HandleDragDrop(Game.IInventory sourceInventory, int sourceSlotIndex, Game.DragMode dragMode, Game.IInventory targetInventory, int targetSlotIndex, bool processingOnly, Game.ComponentPlayer componentPlayer = null)`
枚举为 `Game.DragMode.SingleItem`，不是嵌套在widget里的枚举。
