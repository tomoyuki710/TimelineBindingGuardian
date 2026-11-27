using System.Globalization;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// 永続キーの生成。Hierarchy パス・名前・配列インデックス・InstanceID は使わない。
    /// </summary>
    public static class BindingIdentity
    {
        private const char Separator = '|';

        /// <summary>
        /// Director（文脈込みの GlobalObjectId）+ Timeline GUID + Track fileId で BindingKey を作る。
        /// </summary>
        public static string CreateBindingKey(string directorGlobalObjectId, string timelineGuid, string trackFileId)
        {
            return string.Concat(directorGlobalObjectId, Separator, timelineGuid, Separator, trackFileId);
        }

        /// <summary>
        /// fileId を符号付き 64bit の文字列にする。GlobalObjectId は unsigned 表示のため、保存時はこちらに統一する。
        /// </summary>
        public static string FormatFileId(long fileId)
        {
            return fileId.ToString(CultureInfo.InvariantCulture);
        }
    }
}
