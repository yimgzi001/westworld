using Engine.Graphics;

namespace Game {
    /// <summary>
    /// 一个方块值在独立绘制场景的自报容量需求（上界）：一组「批次键 → 顶点/索引数」条目。
    /// 返回 null 表示不参与预扩容记账（走原生逐块扩容行为）。
    /// </summary>
    public sealed class BlockDrawDemand {
        public struct Entry {
            public TexturedBatchKey BatchKey;
            public int VertexCount;
            public int IndexCount;
        }

        readonly Entry[] m_entries;

        /// <summary>普通六面体：6 面 = 24 顶点/36 索引。</summary>
        public static readonly BlockDrawDemand Cube = new((TexturedBatchKey.Cube, 24, 36));

        /// <summary>交叉面片：billboard 单 quad，上界 2 quad（无 BillboardDirection 时画两面）。</summary>
        public static readonly BlockDrawDemand Flat = new((TexturedBatchKey.Flat, 8, 12));

        public BlockDrawDemand(params (TexturedBatchKey BatchKey, int VertexCount, int IndexCount)[] entries) {
            m_entries = new Entry[entries.Length];
            for (int i = 0; i < entries.Length; i++) {
                m_entries[i] = new Entry { BatchKey = entries[i].BatchKey, VertexCount = entries[i].VertexCount, IndexCount = entries[i].IndexCount };
            }
        }

        public IReadOnlyList<Entry> Entries => m_entries;

        public static BlockDrawDemand FromBlockMesh(TexturedBatchKey batchKey, BlockMesh blockMesh) =>
            blockMesh == null ? null : new BlockDrawDemand((batchKey, blockMesh.Vertices.Count, blockMesh.Indices.Count));

        /// <summary>按键合并多份需求（同键顶点/索引累加），供家具等多子集几何求总。</summary>
        public static BlockDrawDemand Sum(params BlockDrawDemand[] parts) {
            Dictionary<TexturedBatchKey, (int Verts, int Idxs)> totals = [];
            foreach (BlockDrawDemand part in parts) {
                if (part == null) {
                    continue;
                }
                foreach (Entry entry in part.Entries) {
                    (int verts, int idxs) = totals.TryGetValue(entry.BatchKey, out (int, int) value) ? value : (0, 0);
                    totals[entry.BatchKey] = (verts + entry.VertexCount, idxs + entry.IndexCount);
                }
            }
            if (totals.Count == 0) {
                return null;
            }
            (TexturedBatchKey, int, int)[] tuples = new (TexturedBatchKey, int, int)[totals.Count];
            int index = 0;
            foreach (KeyValuePair<TexturedBatchKey, (int Verts, int Idxs)> pair in totals) {
                tuples[index++] = (pair.Key, pair.Value.Verts, pair.Value.Idxs);
            }
            return new BlockDrawDemand(tuples);
        }
    }

    /// <summary>
    /// 方块绘制容量需求账本：随实体（掉落物/抛射物）增删按值累加/扣减，
    /// 绘制前按键一次性 <see cref="Engine.Graphics.BaseTexturedBatch.EnsureCapacity"/>。
    /// 与粒子系统的 m_batchDemand 账本同构。
    /// </summary>
    public class BlockDrawDemandLedger {
        readonly Dictionary<TexturedBatchKey, (int Verts, int Idxs)> m_demand = [];

        public IReadOnlyDictionary<TexturedBatchKey, (int Verts, int Idxs)> Demand => m_demand;

        public void Book(BlockDrawDemand demand, int sign) {
            if (demand == null) {
                return;
            }
            foreach (BlockDrawDemand.Entry entry in demand.Entries) {
                (int verts, int idxs) = m_demand.TryGetValue(entry.BatchKey, out (int, int) value) ? value : (0, 0);
                verts += sign * entry.VertexCount;
                idxs += sign * entry.IndexCount;
                if (verts <= 0
                    && idxs <= 0) {
                    m_demand.Remove(entry.BatchKey);
                }
                else {
                    m_demand[entry.BatchKey] = (verts, idxs);
                }
            }
        }
    }
}
