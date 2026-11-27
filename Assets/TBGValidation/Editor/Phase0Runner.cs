using System;
using UnityEditor;
using UnityEngine;

namespace TimelineBindingGuardian.Validation
{
    /// <summary>
    /// Phase 0 検証のエントリポイント。
    /// batchmode: Unity.exe -batchmode -nographics -projectPath &lt;path&gt;
    ///   -executeMethod TimelineBindingGuardian.Validation.Phase0Runner.RunBuild -logFile &lt;log&gt;
    /// ※ -quit は付けない（終了コードを返すため EditorApplication.Exit を使う）
    /// 引数: -tbgTag &lt;name&gt;（RunRead の結果ファイル名に付与。再起動後の ID 比較に使う）
    /// </summary>
    public static class Phase0Runner
    {
        public const string BaseSet = "Base";
        public const string MutationSet = "Mutation";
        public const string RestoreSet = "Restore";

        public static void RunBuild()
        {
            Execute("build.txt", log =>
            {
                Phase0AssetBuilder.Build(BaseSet, log);
                Phase0AssetBuilder.Build(MutationSet, log);
                Phase0AssetBuilder.Build(RestoreSet, log);
            });
        }

        public static void RunRead()
        {
            string tag = GetArgument("-tbgTag", "run1");
            Execute($"read_{tag}.txt", log => Phase0ReadProbe.Run(BaseSet, tag, log));
        }

        public static void RunMutation()
        {
            Execute("mutation.txt", log => Phase0MutationProbe.Run(MutationSet, log));
        }

        public static void RunRestore()
        {
            Execute("restore.txt", log => Phase0RestoreProbe.Run(RestoreSet, log));
        }

        public static void RunIdentity()
        {
            Execute("identity.txt", Phase0IdentityProbe.Run);
        }

        [MenuItem("Tools/TBG Validation/1. Build Assets")]
        private static void InspectorBuild()
        {
            RunInEditor("build.txt", log =>
            {
                Phase0AssetBuilder.Build(BaseSet, log);
                Phase0AssetBuilder.Build(MutationSet, log);
                Phase0AssetBuilder.Build(RestoreSet, log);
            });
        }

        [MenuItem("Tools/TBG Validation/2. Read Probe")]
        private static void InspectorRead()
        {
            RunInEditor("read_menu.txt", log => Phase0ReadProbe.Run(BaseSet, "menu", log));
        }

        private static void Execute(string resultFileName, Action<Phase0Log> action)
        {
            int exitCode = RunCore(resultFileName, action) ? 0 : 1;
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
            }
        }

        private static void RunInEditor(string resultFileName, Action<Phase0Log> action)
        {
            bool confirmed = EditorUtility.DisplayDialog("TBG Validation",
                "This replaces the open scene with an empty scene. Unsaved scene changes will be lost. Continue?", "Run", "Cancel");
            if (confirmed)
            {
                RunCore(resultFileName, action);
            }
        }

        private static bool RunCore(string resultFileName, Action<Phase0Log> action)
        {
            Phase0Paths.RunName = GetArgument("-tbgRun", Phase0Paths.RunName);
            resultFileName = $"{Phase0Paths.RunName}_{resultFileName}";
            Phase0Log log = new Phase0Log();
            bool succeeded = true;
            try
            {
                action(log);
            }
            catch (Exception exception)
            {
                succeeded = false;
                log.Line("EXCEPTION: " + exception);
                Debug.LogException(exception);
            }
            finally
            {
                log.Save(resultFileName);
            }

            return succeeded;
        }

        private static string GetArgument(string name, string defaultValue)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i < arguments.Length - 1; i++)
            {
                if (arguments[i] == name)
                {
                    return arguments[i + 1];
                }
            }

            return defaultValue;
        }
    }
}
