using System.Collections.Concurrent;
using Engine.Graphics;

namespace Engine.Animation {
    /// <summary>
    /// 动画驱动器注册表
    /// 提供驱动器类型的注册和创建功能，避免运行时反射查找
    /// 使用 ConcurrentDictionary 确保线程安全
    /// </summary>
    public static class AnimationDriverManager {
        /// <summary>
        /// 驱动器类型注册表（名称 -> 类型）
        /// 使用 ConcurrentDictionary 确保多线程环境下的安全性
        /// </summary>
        public static readonly ConcurrentDictionary<string, Type> s_drivers = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 已注册的驱动器数量
        /// </summary>
        public static int RegisteredCount => s_drivers.Count;

        /// <summary>
        /// 注册驱动器类型
        /// </summary>
        /// <param name="name">驱动器名称（支持短名称，如 "LookAt"、"Death"）</param>
        /// <param name="type">驱动器类型（必须实现 IAnimationDriver）</param>
        public static void Register(string name, Type type) {
            if (string.IsNullOrEmpty(name)) {
                throw new ArgumentNullException(nameof(name));
            }
            if (type == null) {
                throw new ArgumentNullException(nameof(type));
            }
            if (!typeof(IAnimationDriver).IsAssignableFrom(type)) {
                throw new ArgumentException($"Type {type.FullName} does not implement IAnimationDriver", nameof(type));
            }
            s_drivers[name] = type;
        }

        /// <summary>
        /// 注册驱动器类型（泛型版本）
        /// </summary>
        /// <typeparam name="T">驱动器类型</typeparam>
        /// <param name="name">驱动器名称</param>
        public static void Register<T>(string name) where T : IAnimationDriver, new() {
            Register(name, typeof(T));
        }

        /// <summary>
        /// 批量注册驱动器
        /// </summary>
        /// <param name="drivers">驱动器名称和类型的键值对</param>
        public static void RegisterAll(IEnumerable<KeyValuePair<string, Type>> drivers) {
            if (drivers == null) {
                return;
            }
            foreach (KeyValuePair<string, Type> kvp in drivers) {
                Register(kvp.Key, kvp.Value);
            }
        }

        /// <summary>
        /// 检查驱动器是否已注册
        /// </summary>
        /// <param name="name">驱动器名称</param>
        /// <returns>是否已注册</returns>
        public static bool IsRegistered(string name) => !string.IsNullOrEmpty(name) && s_drivers.ContainsKey(name);

        /// <summary>
        /// 获取已注册的驱动器类型
        /// </summary>
        /// <param name="name">驱动器名称</param>
        /// <returns>驱动器类型，如果未注册则返回 null</returns>
        public static Type GetDriverType(string name) {
            if (string.IsNullOrEmpty(name)) {
                return null;
            }
            return s_drivers.TryGetValue(name, out Type type) ? type : null;
        }

        /// <summary>
        /// 创建驱动器实例
        /// </summary>
        /// <param name="name">驱动器名称</param>
        /// <returns>驱动器实例，如果未注册则返回 null</returns>
        public static IAnimationDriver Create(string name) {
            Type type = GetDriverType(name);
            if (type == null) {
                return null;
            }
            try {
                return Activator.CreateInstance(type) as IAnimationDriver;
            }
            catch (Exception ex) {
                Log.Error($"[AnimationDriverManager] Failed to create driver '{name}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 清除所有注册（主要用于测试）
        /// </summary>
        public static void Clear() {
            s_drivers.Clear();
        }

        /// <summary>
        /// 获取所有已注册的驱动器名称
        /// </summary>
        public static IEnumerable<string> GetRegisteredNames() => s_drivers.Keys;

        // 蒙皮枢轴补偿的采样前快照（动画采样只在主线程，静态暂存免每帧分配）
        static Matrix?[] s_pivotSnapshot;

        /// <summary>
        /// 采样驱动器并对蒙皮模型补回枢轴平移。
        /// 驱动器契约是输出旋转 delta（非蒙皮模型由 ProcessBoneHierarchy 叠加 rest 平移）；
        /// 蒙皮模型走整份替换路径，rest 枢轴会丢，需在此补 value * T(rest)。
        /// 仅 Override（整份替换）语义补：Additive 层输出是乘进底层姿态的 delta，
        /// 底层（关键帧/下层混合结果）已自带枢轴，补 T(rest) 会把平移加算一次（表现为模型漂移）。
        /// 通过采样前后快照只补偿本次实际写入的骨骼：传入缓冲不保证干净
        /// （SampleWithPivot 是公开 API，mod 自备缓冲可能已含其他源写入的值；
        /// TargetBones 也可能为空，如 DeathDriver 作用于根骨）。
        /// 仅主线程（动画采样）调用。
        /// </summary>
        public static void SampleWithPivot(IAnimationDriver driver, Matrix?[] boneTransforms, Model model, AnimationBlendMode blendMode) {
            if (driver == null) {
                return;
            }
            if (model == null
                || !model.HasSkin
                || blendMode != AnimationBlendMode.Override) {
                driver.SampleTransforms(boneTransforms, model);
                return;
            }
            int boneCount = Math.Min(boneTransforms.Length, model.Bones.Count);
            if (s_pivotSnapshot == null || s_pivotSnapshot.Length < boneCount) {
                s_pivotSnapshot = new Matrix?[boneCount];
            }
            for (int i = 0; i < boneCount; i++) {
                s_pivotSnapshot[i] = boneTransforms[i];
            }
            driver.SampleTransforms(boneTransforms, model);
            for (int i = 0; i < boneCount; i++) {
                if (!boneTransforms[i].HasValue || boneTransforms[i].Value == s_pivotSnapshot[i]) {
                    continue;
                }
                boneTransforms[i] = boneTransforms[i].Value
                    * Matrix.CreateTranslation(model.Bones[i].Transform.Translation);
            }
        }
    }
}