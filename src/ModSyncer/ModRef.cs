using System;

namespace ModSyncer
{
    /// <summary>
    /// Identifies one exact mod build the way Thunderstore does: Namespace-Name-Version,
    /// for example "ValheimModding-Jotunn-2.20.0". Namespace is the author/team.
    /// Thunderstore forbids '-' inside namespaces and names, which is what makes the string
    /// unambiguous to split.
    /// </summary>
    public sealed class ModRef
    {
        public string Namespace { get; }
        public string Name { get; }
        public string Version { get; }

        public ModRef(string ns, string name, string version)
        {
            Namespace = ns;
            Name = name;
            Version = version;
        }

        /// <summary>"Namespace-Name" - the identity of a mod regardless of version. Also the install folder name.</summary>
        public string FullName => Namespace + "-" + Name;

        /// <summary>"Namespace-Name-Version" - the exact build.</summary>
        public string DependencyString => Namespace + "-" + Name + "-" + Version;

        /// <summary>Thunderstore's public download URL for exactly this version (a zip file).</summary>
        public string DownloadUrl => "https://thunderstore.io/package/download/" + Namespace + "/" + Name + "/" + Version + "/";

        public static bool TryParse(string text, out ModRef result)
        {
            result = null;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string[] parts = text.Trim().Split('-');
            if (parts.Length < 3) return false;

            string ns = parts[0];
            string version = parts[parts.Length - 1];
            // Be forgiving about a stray '-' in the middle: everything between first and last is the name.
            string name = string.Join("-", parts, 1, parts.Length - 2);

            if (ns.Length == 0 || name.Length == 0 || version.Length == 0) return false;
            if (!char.IsDigit(version[0])) return false; // versions look like 1.2.3

            result = new ModRef(ns, name, version);
            return true;
        }

        public override string ToString() => DependencyString;
    }

    /// <summary>Which side of the connection a mod must be installed on.</summary>
    public enum ModSide
    {
        /// <summary>Required on the server and every client (the common case).</summary>
        Both = 0,
        /// <summary>Required on clients only, e.g. a UI mod the host wants everyone to have.</summary>
        Client = 1,
        /// <summary>Runs on the server only; clients are never asked for it.</summary>
        Server = 2,
    }

    public static class ModSideExtensions
    {
        public static bool TryParseSide(string text, out ModSide side)
        {
            switch ((text ?? "").Trim().ToLowerInvariant())
            {
                case "": case "both": side = ModSide.Both; return true;
                case "client": side = ModSide.Client; return true;
                case "server": side = ModSide.Server; return true;
                default: side = ModSide.Both; return false;
            }
        }
    }
}
