using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// 差分エンジンの純粋ロジックのテスト（仕様 7.1〜7.4、13章 T01〜T04, T06, T08, T10, T12）。
    /// 文脈は「Scene(L0) → Middle(L1) → Child(L2)」の3階層を合成して使う。
    /// </summary>
    public sealed class BindingDiffEngineTests
    {
        private const string SceneA = "sceneA";
        private const string SceneB = "sceneB";
        private const string Timeline = "timeline1";
        private const string MiddleLevel = "middle|director";
        private const string ChildLevel = "child|director";

        private int _observationCounter;

        [Test]
        public void T01_InitialObservation_RecordsOnlyInitial_EvenWithNull()
        {
            HistoryState state = new HistoryState("project");

            DiffResult result = Apply(state, SceneA, Context(SceneA, "d1", "AnimA", Bound("actorA")), Context(SceneA, "d1", "AnimB", NullValue()));

            Assert.That(result.NewEvents.Select(e => e.ChangeType), Is.EqualTo(new[] { ChangeType.Initial }));
            Assert.That(state.Contexts.Count, Is.EqualTo(2));
        }

        [Test]
        public void T02_BoundToNull_OneLost_NullContinued_NoNewEvent()
        {
            HistoryState state = new HistoryState("project");
            Apply(state, SceneA, Context(SceneA, "d1", "AnimA", Bound("actorA")));

            DiffResult lost = Apply(state, SceneA, Context(SceneA, "d1", "AnimA", NullValue()));
            DiffResult again = Apply(state, SceneA, Context(SceneA, "d1", "AnimA", NullValue()));

            ChangeEvent changeEvent = lost.NewEvents.Single();
            Assert.That(changeEvent.ChangeType, Is.EqualTo(ChangeType.BindingLost));
            Assert.That(changeEvent.OwnerLevelKey, Is.EqualTo(LevelKey(ChildLevel, "AnimA")));
            Assert.That(changeEvent.AffectedContexts.Count, Is.EqualTo(1));
            Assert.That(changeEvent.AffectsEffective, Is.True);
            Assert.That(again.NewEvents, Is.Empty);
        }

        [Test]
        public void T04_LostAgainAfterRecovery_CreatesNewEventId()
        {
            HistoryState state = new HistoryState("project");
            Apply(state, SceneA, Context(SceneA, "d1", "AnimA", Bound("actorA")));

            ChangeEvent first = Apply(state, SceneA, Context(SceneA, "d1", "AnimA", NullValue())).NewEvents.Single();
            ChangeEvent assigned = Apply(state, SceneA, Context(SceneA, "d1", "AnimA", Bound("actorA"))).NewEvents.Single();
            ChangeEvent second = Apply(state, SceneA, Context(SceneA, "d1", "AnimA", NullValue())).NewEvents.Single();

            Assert.That(assigned.ChangeType, Is.EqualTo(ChangeType.BindingAssigned));
            Assert.That(second.ChangeType, Is.EqualTo(ChangeType.BindingLost));
            Assert.That(second.EventId, Is.Not.EqualTo(first.EventId));
        }

        [Test]
        public void T12_Unknown_KeepsLastCertainValue_AndRecordsOnce()
        {
            HistoryState state = new HistoryState("project");
            Apply(state, SceneA, Context(SceneA, "d1", "AnimA", Bound("actorA")));

            DiffResult unknown1 = Apply(state, SceneA, Uncertain(Context(SceneA, "d1", "AnimA", NullValue())));
            DiffResult unknown2 = Apply(state, SceneA, Uncertain(Context(SceneA, "d1", "AnimA", NullValue())));

            Assert.That(unknown1.NewEvents.Select(e => e.ChangeType), Is.EqualTo(new[] { ChangeType.Unresolved }));
            Assert.That(unknown2.NewEvents, Is.Empty);
            ContextSnapshot kept = state.Contexts.Single();
            Assert.That(kept.Effective.ValueState, Is.EqualTo(ValueState.Bound));

            DiffResult resolved = Apply(state, SceneA, Context(SceneA, "d1", "AnimA", NullValue()));
            ChangeEvent lost = resolved.NewEvents.Single();
            Assert.That(lost.ChangeType, Is.EqualTo(ChangeType.BindingLost));
            Assert.That(Lang.RenderNote(lost.AffectedContexts.Single().Note), Does.Contain("unknown"));
        }

        [Test]
        public void T12_UnknownThenSameValue_NoEvent()
        {
            HistoryState state = new HistoryState("project");
            Apply(state, SceneA, Context(SceneA, "d1", "AnimA", Bound("actorA")));
            Apply(state, SceneA, Uncertain(Context(SceneA, "d1", "AnimA", NullValue())));

            DiffResult result = Apply(state, SceneA, Context(SceneA, "d1", "AnimA", Bound("actorA")));

            Assert.That(result.NewEvents, Is.Empty);
            Assert.That(state.Contexts.Single().UnknownSinceUtc, Is.Empty);
        }

        [Test]
        public void T06_MiddleChangePropagatedToTenContexts_OneEventTenAffected()
        {
            HistoryState state = new HistoryState("project");
            Apply(state, SceneA, Enumerable.Range(0, 10).Select(i => Context(SceneA, $"d{i}", "Activation", Bound("actTarget"))).ToArray());

            ContextSnapshot[] changed = Enumerable.Range(0, 10)
                .Select(i => Context(SceneA, $"d{i}", "Activation", NullValue(), middle: Defined(NullValue()), origin: 1))
                .ToArray();
            DiffResult result = Apply(state, SceneA, changed);

            ChangeEvent changeEvent = result.NewEvents.Single();
            Assert.That(changeEvent.ChangeType, Is.EqualTo(ChangeType.BindingLost));
            Assert.That(changeEvent.OwnerLevelKey, Is.EqualTo(LevelKey(MiddleLevel, "Activation")));
            Assert.That(changeEvent.AffectedContexts.Count, Is.EqualTo(10));
        }

        [Test]
        public void ChildChange_IsAttributedToChild_NotToInheritingMiddle()
        {
            HistoryState state = new HistoryState("project");
            Apply(state, SceneA, Context(SceneA, "d1", "AnimA", Bound("actorA")));

            DiffResult result = Apply(state, SceneA, Context(SceneA, "d1", "AnimA", Bound("actorB")));

            ChangeEvent changeEvent = result.NewEvents.Single();
            Assert.That(changeEvent.ChangeType, Is.EqualTo(ChangeType.TargetChanged));
            Assert.That(changeEvent.OwnerLevelKey, Is.EqualTo(LevelKey(ChildLevel, "AnimA")));
        }

        [Test]
        public void SimultaneousUpperOverride_IsSeparateEvent()
        {
            HistoryState state = new HistoryState("project");
            Apply(state, SceneA,
                Context(SceneA, "d1", "Activation", Bound("actTarget")),
                Context(SceneA, "d2", "Activation", Bound("actTarget")));

            // d1: Middle の NULL Override が伝播。d2: 同時に Scene でも別対象へ Override
            DiffResult result = Apply(state, SceneA,
                Context(SceneA, "d1", "Activation", NullValue(), middle: Defined(NullValue()), origin: 1),
                Context(SceneA, "d2", "Activation", Bound("sceneTarget"), scene: Defined(Bound("sceneTarget")), middle: Defined(NullValue()), origin: 0));

            ChangeEvent middleEvent = result.NewEvents.Single(e => e.OwnerLevelKey == LevelKey(MiddleLevel, "Activation"));
            ChangeEvent sceneEvent = result.NewEvents.Single(e => e.OwnerLevelKey == LevelKey(SceneLevel("d2"), "Activation"));
            Assert.That(middleEvent.AffectedContexts.Select(c => c.ContextBindingKey), Is.EqualTo(new[] { Key(SceneA, "d1", "Activation") }));
            Assert.That(sceneEvent.ChangeType, Is.EqualTo(ChangeType.TargetChanged));
            Assert.That(sceneEvent.AffectedContexts.Select(c => c.ContextBindingKey), Is.EqualTo(new[] { Key(SceneA, "d2", "Activation") }));
        }

        [Test]
        public void T08_LowerChangeShadowedByUpperOverride_DoesNotAffectEffective()
        {
            HistoryState state = new HistoryState("project");
            Apply(state, SceneA, Context(SceneA, "d1", "Activation", Bound("sceneTarget"), scene: Defined(Bound("sceneTarget")), origin: 0));

            DiffResult result = Apply(state, SceneA,
                Context(SceneA, "d1", "Activation", Bound("sceneTarget"), scene: Defined(Bound("sceneTarget")), middle: Defined(NullValue()), origin: 0));

            ChangeEvent middleEvent = result.NewEvents.Single();
            Assert.That(middleEvent.OwnerLevelKey, Is.EqualTo(LevelKey(MiddleLevel, "Activation")));
            Assert.That(middleEvent.ChangeType, Is.EqualTo(ChangeType.BindingLost));
            Assert.That(middleEvent.AffectsEffective, Is.False);
            Assert.That(middleEvent.AffectedContexts, Is.Empty);
        }

        [Test]
        public void RemovingUpperOverride_IsAttributedToThatLevel()
        {
            HistoryState state = new HistoryState("project");
            Apply(state, SceneA, Context(SceneA, "d1", "Activation", NullValue(), middle: Defined(NullValue()), origin: 1));

            DiffResult result = Apply(state, SceneA, Context(SceneA, "d1", "Activation", Bound("actTarget")));

            ChangeEvent changeEvent = result.NewEvents.Single();
            Assert.That(changeEvent.OwnerLevelKey, Is.EqualTo(LevelKey(MiddleLevel, "Activation")));
            Assert.That(changeEvent.ChangeType, Is.EqualTo(ChangeType.BindingAssigned));
            Assert.That(changeEvent.AffectedContexts.Count, Is.EqualTo(1));
        }

        [Test]
        public void TwoLevelsChangedAboveOrigin_IsUndetermined()
        {
            HistoryState state = new HistoryState("project");
            Apply(state, SceneA, Context(SceneA, "d1", "AnimA", NullValue(), middle: Defined(NullValue()), origin: 1));

            // Middle の Override 解除と Child の変更が同時（どちらも今回の Origin 以上）
            DiffResult result = Apply(state, SceneA, Context(SceneA, "d1", "AnimA", Bound("actorB"), child: Bound("actorB")));

            ChangeEvent undetermined = result.NewEvents.Single(e => e.OriginUndetermined);
            Assert.That(undetermined.ChangeType, Is.EqualTo(ChangeType.BindingAssigned));
            Assert.That(undetermined.AffectedContexts.Count, Is.EqualTo(1));
        }

        [Test]
        public void T10_TrackRemoved_IsNotBindingLost()
        {
            HistoryState state = new HistoryState("project");
            Apply(state, SceneA, Context(SceneA, "d1", "AnimA", Bound("actorA")), Context(SceneA, "d1", "AnimB", Bound("actorB")));

            DiffResult result = Apply(state, SceneA, Context(SceneA, "d1", "AnimA", Bound("actorA")));

            Assert.That(result.NewEvents.Select(e => e.ChangeType), Is.EqualTo(new[] { ChangeType.TrackRemoved }));
        }

        [Test]
        public void TrackAdded_IsRecorded()
        {
            HistoryState state = new HistoryState("project");
            Apply(state, SceneA, Context(SceneA, "d1", "AnimA", Bound("actorA")));

            DiffResult result = Apply(state, SceneA, Context(SceneA, "d1", "AnimA", Bound("actorA")), Context(SceneA, "d1", "AnimB", NullValue()));

            Assert.That(result.NewEvents.Select(e => e.ChangeType), Is.EqualTo(new[] { ChangeType.TrackAdded }));
        }

        [Test]
        public void DirectorRemoved_OneEventPerDirector()
        {
            HistoryState state = new HistoryState("project");
            Apply(state, SceneA,
                Context(SceneA, "d1", "AnimA", Bound("actorA")),
                Context(SceneA, "d1", "AnimB", Bound("actorB")),
                Context(SceneA, "d2", "AnimA", Bound("actorA")));

            DiffResult result = Apply(state, SceneA, Context(SceneA, "d2", "AnimA", Bound("actorA")));

            Assert.That(result.NewEvents.Select(e => e.ChangeType), Is.EqualTo(new[] { ChangeType.DirectorRemoved }));
            Assert.That(state.Contexts.Count, Is.EqualTo(1));
        }

        [Test]
        public void T10_TimelineReplaced_OneTimelineChangedEvent()
        {
            HistoryState state = new HistoryState("project");
            Apply(state, SceneA, Context(SceneA, "d1", "AnimA", Bound("actorA")), Context(SceneA, "d1", "AnimB", Bound("actorB")));

            DiffResult result = Apply(state, SceneA,
                Context(SceneA, "d1", "AnimA", Bound("actorA"), timeline: "timeline2"),
                Context(SceneA, "d1", "AnimB", NullValue(), timeline: "timeline2"));

            Assert.That(result.NewEvents.Select(e => e.ChangeType), Is.EqualTo(new[] { ChangeType.TimelineChanged }));
        }

        [Test]
        public void DisplayHintsOnly_NoEvent()
        {
            HistoryState state = new HistoryState("project");
            Apply(state, SceneA, Context(SceneA, "d1", "AnimA", Bound("actorA")));

            DiffResult result = Apply(state, SceneA, Context(SceneA, "d1", "AnimA", Bound("actorA", hint: "Renamed/ActorA"), directorHint: "Renamed/Director"));

            Assert.That(result.NewEvents, Is.Empty);
        }

        [Test]
        public void SameOwnerChangeSeenFromAnotherScene_ReusesExistingEvent()
        {
            HistoryState state = new HistoryState("project");
            Apply(state, SceneA, Context(SceneA, "d1", "Activation", Bound("actTarget")));
            Apply(state, SceneB, Context(SceneB, "d9", "Activation", Bound("actTarget")));

            DiffResult inA = Apply(state, SceneA, Context(SceneA, "d1", "Activation", NullValue(), middle: Defined(NullValue()), origin: 1));
            DiffResult inB = Apply(state, SceneB, Context(SceneB, "d9", "Activation", NullValue(), middle: Defined(NullValue()), origin: 1));

            ChangeEvent changeEvent = inA.NewEvents.Single();
            Assert.That(inB.NewEvents, Is.Empty);
            Assert.That(inB.UpdatedEvents.Single().EventId, Is.EqualTo(changeEvent.EventId));
            Assert.That(changeEvent.AffectedContexts.Count, Is.EqualTo(2));
        }

        [Test]
        public void StateRoundTripThroughJson_KeepsBehavior()
        {
            HistoryState state = new HistoryState("project");
            Apply(state, SceneA, Context(SceneA, "d1", "AnimA", Bound("actorA")));

            HistoryState reloaded = state.Clone();
            DiffResult sameAgain = Apply(reloaded, SceneA, Context(SceneA, "d1", "AnimA", Bound("actorA")));
            DiffResult lost = Apply(reloaded, SceneA, Context(SceneA, "d1", "AnimA", NullValue()));

            Assert.That(sameAgain.NewEvents, Is.Empty);
            Assert.That(lost.NewEvents.Single().ChangeType, Is.EqualTo(ChangeType.BindingLost));
            Assert.That(reloaded.Events.First().Before.ValueState, Is.EqualTo(ValueState.NotApplicable));
        }

        private DiffResult Apply(HistoryState state, string scope, params ContextSnapshot[] contexts)
        {
            _observationCounter++;
            ScopeObservation observation = new ScopeObservation($"obs{_observationCounter}", $"2026-01-01T00:00:{_observationCounter:00}Z",
                "test", scope, $"Assets/{scope}.unity", contexts.ToList());
            return BindingDiffEngine.Apply(state, observation);
        }

        /// <summary>
        /// Scene(L0) → Middle(L1) → Child(L2) の文脈。指定しない階層は継承（Child は child 値を定義）。
        /// </summary>
        private static ContextSnapshot Context(
            string scope,
            string director,
            string track,
            LevelValue effective,
            LevelSnapshotSpec scene = null,
            LevelSnapshotSpec middle = null,
            LevelValue child = null,
            int origin = 2,
            string timeline = Timeline,
            string directorHint = "Parent/Child/Director")
        {
            LevelValue childValue = child ?? (origin == 2 ? effective : Bound("actTarget"));
            List<LevelSnapshot> levels = new List<LevelSnapshot>
            {
                Level(LevelKey(SceneLevel(director), track, timeline), scene, effective),
                Level(LevelKey(MiddleLevel, track, timeline), middle, effective),
                new LevelSnapshot(LevelKey(ChildLevel, track, timeline), new AssetLocator("child", "Assets/Child.prefab", AssetKind.Prefab),
                    new DirectorLocator(ChildLevel, "Child/Director", "Child#0/Director#0", 0), childValue, ContributionKind.Defined),
            };

            DirectorLocator directorLocator = new DirectorLocator(SceneLevel(director), directorHint, directorHint, 0);
            TrackLocator trackLocator = new TrackLocator(timeline, $"Assets/{timeline}.playable", track, "AnimationTrack", track, "Animator");
            return new ContextSnapshot(Key(scope, director, track, timeline), scope, $"Assets/{scope}.unity", directorLocator, trackLocator,
                effective, origin, false, levels);
        }

        private static LevelSnapshot Level(string key, LevelSnapshotSpec spec, LevelValue inheritedValue)
        {
            bool defined = spec != null;
            LevelValue value = defined ? spec.Value : inheritedValue;
            return new LevelSnapshot(key, new AssetLocator(key, key, AssetKind.Prefab), new DirectorLocator(key, key, key, 0), value,
                defined ? ContributionKind.Defined : ContributionKind.Inherited);
        }

        private static ContextSnapshot Uncertain(ContextSnapshot context)
        {
            return new ContextSnapshot(context.BindingKey, context.ScopeGuid, context.ScopePathHint, context.Director, context.Track,
                context.Effective, -1, true, context.Levels.ToList());
        }

        private static string Key(string scope, string director, string track, string timeline = Timeline)
        {
            return $"{SceneLevel(director)}@{scope}|{timeline}|{track}";
        }

        // 製品の OwnerLevelKey と同じく Owner 階層 | Timeline | Track の形にする
        private static string LevelKey(string level, string track, string timeline = Timeline)
        {
            return $"{level}|{timeline}|{track}";
        }

        private static string SceneLevel(string director)
        {
            return "scene|" + director;
        }

        private static LevelValue Bound(string target, string hint = null)
        {
            return new LevelValue(ValueState.Bound, LocalState.Inherited, target, hint ?? target, "Animator");
        }

        private static LevelValue NullValue()
        {
            return new LevelValue(ValueState.Null, LocalState.Inherited, string.Empty, string.Empty, string.Empty);
        }

        private static LevelSnapshotSpec Defined(LevelValue value)
        {
            return new LevelSnapshotSpec(value);
        }

        private sealed class LevelSnapshotSpec
        {
            public LevelSnapshotSpec(LevelValue value)
            {
                Value = value;
            }

            public LevelValue Value { get; }
        }
    }
}
