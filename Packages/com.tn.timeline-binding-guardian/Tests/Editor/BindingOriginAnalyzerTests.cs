using System.Collections.Generic;
using NUnit.Framework;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// 変更元判定の純粋ロジックのテスト（Unity Asset 不要）。
    /// </summary>
    public sealed class BindingOriginAnalyzerTests
    {
        [Test]
        public void NoLevelChanged_OriginIsBase()
        {
            TrackBindingObservation observation = Create(
                Level(0, ValueState.Bound, LocalState.Inherited, false),
                Level(1, ValueState.Bound, LocalState.Inherited, false),
                Level(2, ValueState.Bound, LocalState.DefinedHere, false));

            BindingOriginAnalyzer.Analyze(observation);

            Assert.That(observation.OriginKind, Is.EqualTo(OriginKind.Determined));
            Assert.That(observation.OriginLevel, Is.EqualTo(2));
            Assert.That(observation.Levels[2].Role, Is.EqualTo(LevelRole.Origin));
        }

        [Test]
        public void MiddleLevelChanged_OriginIsMiddle()
        {
            TrackBindingObservation observation = Create(
                Level(0, ValueState.Null, LocalState.Inherited, false),
                Level(1, ValueState.Null, LocalState.ExplicitOverride, true),
                Level(2, ValueState.Bound, LocalState.DefinedHere, false));

            BindingOriginAnalyzer.Analyze(observation);

            Assert.That(observation.OriginLevel, Is.EqualTo(1));
            Assert.That(observation.Levels[0].Role, Is.EqualTo(LevelRole.None));
        }

        [Test]
        public void UpperOverride_LowerChangeIsShadowed()
        {
            TrackBindingObservation observation = Create(
                Level(0, ValueState.Bound, LocalState.ExplicitOverride, true),
                Level(1, ValueState.Null, LocalState.Inherited, false),
                Level(2, ValueState.Null, LocalState.ExplicitOverride, true),
                Level(3, ValueState.Bound, LocalState.DefinedHere, false));

            BindingOriginAnalyzer.Analyze(observation);

            Assert.That(observation.OriginLevel, Is.EqualTo(0));
            Assert.That(observation.Levels[2].Role, Is.EqualTo(LevelRole.Shadowed));
            Assert.That(observation.Levels[1].Role, Is.EqualTo(LevelRole.None));
        }

        [Test]
        public void UnknownBeforeOrigin_IsUndetermined()
        {
            TrackBindingObservation observation = Create(
                Level(0, ValueState.Null, LocalState.Inherited, false),
                Level(1, ValueState.Unknown, LocalState.Unknown, true),
                Level(2, ValueState.Bound, LocalState.DefinedHere, false));

            BindingOriginAnalyzer.Analyze(observation);

            Assert.That(observation.OriginKind, Is.EqualTo(OriginKind.Undetermined));
            Assert.That(observation.OriginLevel, Is.EqualTo(-1));
            Assert.That(observation.Origin, Is.Null);
        }

        [Test]
        public void UnknownBelowOrigin_DoesNotAffectOrigin()
        {
            TrackBindingObservation observation = Create(
                Level(0, ValueState.Bound, LocalState.ExplicitOverride, true),
                Level(1, ValueState.Unknown, LocalState.Unknown, false),
                Level(2, ValueState.Bound, LocalState.DefinedHere, false));

            BindingOriginAnalyzer.Analyze(observation);

            Assert.That(observation.OriginKind, Is.EqualTo(OriginKind.Determined));
            Assert.That(observation.OriginLevel, Is.EqualTo(0));
        }

        [Test]
        public void SingleLevel_OriginIsThatLevel()
        {
            TrackBindingObservation observation = Create(Level(0, ValueState.NoEntry, LocalState.DefinedHere, false));

            BindingOriginAnalyzer.Analyze(observation);

            Assert.That(observation.OriginLevel, Is.EqualTo(0));
        }

        [Test]
        public void Reanalyze_ClearsPreviousRoles()
        {
            TrackBindingObservation observation = Create(
                Level(0, ValueState.Bound, LocalState.ExplicitOverride, true),
                Level(1, ValueState.Null, LocalState.ExplicitOverride, true),
                Level(2, ValueState.Bound, LocalState.DefinedHere, false));

            BindingOriginAnalyzer.Analyze(observation);
            BindingOriginAnalyzer.Analyze(observation);

            Assert.That(observation.Levels[0].Role, Is.EqualTo(LevelRole.Origin));
            Assert.That(observation.Levels[1].Role, Is.EqualTo(LevelRole.Shadowed));
        }

        private static TrackBindingObservation Create(params LevelBindingState[] levels)
        {
            TrackLocator track = new TrackLocator("guid", "path", "1", "AnimationTrack", "Track", "Animator");
            return new TrackBindingObservation("key", track, new List<LevelBindingState>(levels));
        }

        private static LevelBindingState Level(int level, ValueState valueState, LocalState localState, bool changed)
        {
            AssetLocator owner = new AssetLocator($"guid{level}", $"Assets/L{level}.prefab", AssetKind.Prefab);
            DirectorLocator director = new DirectorLocator($"gid{level}", "Director", "Director", 0);
            return new LevelBindingState(level, owner, ContextKind.PrefabInstance, director, valueState, localState,
                TargetLocator.None, changed, false, changed, null, null);
        }
    }
}
