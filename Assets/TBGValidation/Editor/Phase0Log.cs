using System.IO;
using System.Text;
using UnityEngine;

namespace TimelineBindingGuardian.Validation
{
    /// <summary>
    /// 検証結果をテキストファイルに出力するための簡易ログ。
    /// </summary>
    public sealed class Phase0Log
    {
        private readonly StringBuilder _builder = new StringBuilder();
        private int _indent;
        private int _passCount;
        private int _failCount;

        public int FailCount => _failCount;

        public void Line(string text)
        {
            _builder.Append(' ', _indent * 2);
            _builder.AppendLine(text);
        }

        public void Section(string title)
        {
            _builder.AppendLine();
            _builder.AppendLine("==== " + title + " ====");
        }

        public void Indent()
        {
            _indent++;
        }

        public void Unindent()
        {
            if (_indent > 0)
            {
                _indent--;
            }
        }

        public void Check(bool condition, string description)
        {
            if (condition)
            {
                _passCount++;
            }
            else
            {
                _failCount++;
            }

            Line((condition ? "[PASS] " : "[FAIL] ") + description);
        }

        public string Save(string fileName)
        {
            Directory.CreateDirectory(Phase0Paths.ResultsDirectory);
            string path = Path.Combine(Phase0Paths.ResultsDirectory, fileName);
            _builder.AppendLine();
            _builder.AppendLine($"CHECKS: pass={_passCount} fail={_failCount}");
            File.WriteAllText(path, _builder.ToString(), new UTF8Encoding(false));
            Debug.Log($"[TBGValidation] Result saved: {path} (pass={_passCount} fail={_failCount})");
            return path;
        }
    }
}
