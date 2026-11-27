using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// 変化のない再検査の高速化（Phase 5）。
    /// - 文脈の内容が同じなら置き換えず、文脈の区画を書き直さない。
    /// - 「内容が同じか」の比較が、保存される全項目を見ていること（項目の追加漏れを検出する）。
    /// </summary>
    public sealed class UnchangedRescanTests
    {
        private static readonly HashSet<string> TimestampFields = new HashSet<string> { "observationId", "observedAtUtc", "unknownSinceUtc" };

        [Test]
        public void SameContent_IsDetected_AndEverySavedFieldIsCompared()
        {
            ContextSnapshot context = CreateContext();
            ContextSnapshot stored = context.CreateStorageCopy();
            stored.SetObservation("old-observation", "2026-01-01T00:00:00.0000000Z");

            Assert.That(context.HasSameStoredContent(Clone(stored)), Is.True, "only the timestamps differ");

            List<string> paths = new List<string>();
            CollectFieldPaths(typeof(ContextSnapshot), stored, string.Empty, paths);
            Assert.That(paths.Count, Is.GreaterThan(20));
            foreach (string path in paths)
            {
                ContextSnapshot mutated = Clone(stored);
                Mutate(mutated, path);
                Assert.That(context.HasSameStoredContent(mutated), Is.False, $"a change in '{path}' must be detected");
            }
        }

        [Test]
        public void UnchangedRescan_DoesNotRewriteContexts_ButAChangeDoes()
        {
            string directory = Path.Combine(Path.GetFullPath("Temp"), "TBGUnchangedRescan", Guid.NewGuid().ToString("N"));
            HistoryStore store = new HistoryStore(directory, "project");
            store.Load();

            Commit(store, "first", Bound("target-a"));
            string contextsPath = Path.Combine(directory, "contexts", "scope.json.gz");
            byte[] written = File.ReadAllBytes(contextsPath);

            DiffResult unchanged = Commit(store, "second", Bound("target-a"));
            Assert.That(unchanged.ChangedScopeGuids, Is.Empty);
            Assert.That(unchanged.NewEvents, Is.Empty);
            Assert.That(File.ReadAllBytes(contextsPath), Is.EqualTo(written));
            Assert.That(store.State.Scopes.Single().LastObservationId, Is.EqualTo("second"), "the scope record still shows the latest observation");

            DiffResult changed = Commit(store, "third", Bound("target-b"));
            Assert.That(changed.ChangedScopeGuids, Does.Contain("scope"));
            Assert.That(File.ReadAllBytes(contextsPath), Is.Not.EqualTo(written));

            HistoryStore reloaded = new HistoryStore(directory, "project");
            reloaded.Load();
            Assert.That(reloaded.EnsureAllLoaded(), Is.True);
            Assert.That(reloaded.State.Contexts.Single().Effective.TargetGlobalObjectId, Is.EqualTo("target-b"));
        }

        private static DiffResult Commit(HistoryStore store, string observationId, LevelValue value)
        {
            Assert.That(store.EnsureScopeLoaded("scope") && store.EnsureOwnersLoaded(new[] { "owner" }), Is.True, store.ErrorMessage);
            ContextSnapshot context = CreateContext(value);
            ScopeObservation observation = new ScopeObservation(observationId, DateTime.UtcNow.ToString("o"), "test", "scope", "Assets/Scope.unity",
                new List<ContextSnapshot> { context }, AssetKind.Scene);
            DiffResult result = BindingDiffEngine.Apply(store.State, observation);
            HistoryChangeSet changes = new HistoryChangeSet();
            changes.Add(result);
            Assert.That(store.TryCommit(store.State, changes, out string error), Is.True, error);
            return result;
        }

        private static LevelValue Bound(string target)
        {
            return new LevelValue(ValueState.Bound, LocalState.DefinedHere, target, target + "-hint", "GameObject");
        }

        private static ContextSnapshot CreateContext(LevelValue value = null)
        {
            LevelValue effective = value ?? Bound("target-a");
            AssetLocator owner = new AssetLocator("owner", "Assets/Owner.prefab", AssetKind.Prefab);
            DirectorLocator director = new DirectorLocator("director-id", "Root/Director", "Root#0/Director#0", 0);
            LevelSnapshot level = new LevelSnapshot("owner|director-id|timeline|1", owner, director, effective, ContributionKind.Defined);
            TrackLocator track = new TrackLocator("timeline", "Assets/Timeline.playable", "1", "ActivationTrack", "Track", "GameObject");
            return new ContextSnapshot("binding-key", "scope", "Assets/Scope.unity", director, track, effective, 0, false,
                new List<LevelSnapshot> { level });
        }

        private static ContextSnapshot Clone(ContextSnapshot source)
        {
            return JsonUtility.FromJson<ContextSnapshot>(JsonUtility.ToJson(source));
        }

        /// <summary>
        /// 保存される項目（[SerializeField]）を、入れ子・リスト要素も含めて「a.b[0].c」の形で集める（観測時刻は除く）。
        /// </summary>
        private static void CollectFieldPaths(Type type, object value, string prefix, List<string> paths)
        {
            foreach (FieldInfo field in SerializedFields(type))
            {
                if (TimestampFields.Contains(field.Name))
                {
                    continue;
                }

                string path = prefix + field.Name;
                object fieldValue = field.GetValue(value);
                if (IsLeaf(field.FieldType))
                {
                    paths.Add(path);
                }
                else if (fieldValue is IList list)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        CollectFieldPaths(list[i].GetType(), list[i], $"{path}[{i}].", paths);
                    }
                }
                else if (fieldValue != null)
                {
                    CollectFieldPaths(field.FieldType, fieldValue, path + ".", paths);
                }
            }
        }

        private static void Mutate(object root, string path)
        {
            object target = root;
            string[] parts = path.Split('.');
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                int bracket = part.IndexOf('[');
                string name = bracket >= 0 ? part.Substring(0, bracket) : part;
                FieldInfo field = SerializedFields(target.GetType()).Single(f => f.Name == name);
                bool isLast = i == parts.Length - 1;
                if (isLast)
                {
                    field.SetValue(target, MutatedValue(field.FieldType, field.GetValue(target)));
                    return;
                }

                object next = field.GetValue(target);
                if (bracket >= 0)
                {
                    int index = int.Parse(part.Substring(bracket + 1, part.IndexOf(']') - bracket - 1));
                    next = ((IList)next)[index];
                }

                target = next;
            }
        }

        private static object MutatedValue(Type type, object value)
        {
            if (type == typeof(string))
            {
                return (string)value + "-changed";
            }

            if (type == typeof(int))
            {
                return (int)value + 1;
            }

            if (type == typeof(bool))
            {
                return !(bool)value;
            }

            if (type.IsEnum)
            {
                Array values = Enum.GetValues(type);
                int index = Array.IndexOf(values, value);
                return values.GetValue((index + 1) % values.Length);
            }

            throw new NotSupportedException($"Add a mutation for {type.Name} so that the field is covered by the test.");
        }

        private static bool IsLeaf(Type type)
        {
            return type.IsPrimitive || type.IsEnum || type == typeof(string);
        }

        private static IEnumerable<FieldInfo> SerializedFields(Type type)
        {
            return type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(f => !f.IsNotSerialized && (f.IsPublic || f.GetCustomAttribute<SerializeField>() != null));
        }
    }
}
