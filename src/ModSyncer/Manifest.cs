using System;
using System.Collections.Generic;
using System.Text;

namespace ModSyncer
{
    /// <summary>One line of the server's rule book: this mod, at this version, on this side.</summary>
    public sealed class ManifestEntry
    {
        public ModRef Mod { get; }
        public ModSide Side { get; }

        public ManifestEntry(ModRef mod, ModSide side)
        {
            Mod = mod;
            Side = side;
        }
    }

    /// <summary>
    /// The list of mods one side has (client) or demands (server), plus enough metadata to
    /// stay compatible if the wire format ever changes. It is sent inside a ZPackage, which is
    /// Valheim's own byte-buffer class for network messages.
    /// </summary>
    public sealed class Manifest
    {
        /// <summary>Bump this whenever WriteTo/ReadFrom change shape. Both sides refuse to parse a newer protocol.</summary>
        public const int ProtocolVersion = 1;

        /// <summary>The BepInEx pack is installed by the mod manager, not into the plugins folder, so we never manage it.</summary>
        public const string BepInExPackName = "BepInExPack_Valheim";

        public string SyncerVersion { get; set; } = PluginVersion.Value;
        public List<ManifestEntry> Entries { get; } = new List<ManifestEntry>();

        public void WriteTo(ZPackage pkg)
        {
            pkg.Write(ProtocolVersion);
            pkg.Write(SyncerVersion ?? "");
            pkg.Write(Entries.Count);
            foreach (ManifestEntry e in Entries)
            {
                pkg.Write(e.Mod.DependencyString);
                pkg.Write((int)e.Side);
            }
        }

        /// <summary>Returns null (and logs) if the package is not something we understand.</summary>
        public static Manifest ReadFrom(ZPackage pkg)
        {
            try
            {
                int protocol = pkg.ReadInt();
                if (protocol > ProtocolVersion)
                {
                    Plugin.Log.LogWarning($"Received manifest with protocol {protocol}, but this build only understands {ProtocolVersion}. Update Mod Syncer.");
                    return null;
                }

                var m = new Manifest { SyncerVersion = pkg.ReadString() };
                int count = pkg.ReadInt();
                if (count < 0 || count > 10000) return null;

                for (int i = 0; i < count; i++)
                {
                    string dep = pkg.ReadString();
                    int side = pkg.ReadInt();
                    if (!ModRef.TryParse(dep, out ModRef mod))
                    {
                        Plugin.Log.LogWarning($"Ignoring unparseable mod reference in manifest: '{dep}'");
                        continue;
                    }
                    m.Entries.Add(new ManifestEntry(mod, Enum.IsDefined(typeof(ModSide), side) ? (ModSide)side : ModSide.Both));
                }
                return m;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("Failed to read manifest package: " + ex.Message);
                return null;
            }
        }

        public string Describe()
        {
            if (Entries.Count == 0) return "  (no mods)";
            var sb = new StringBuilder();
            foreach (ManifestEntry e in Entries)
                sb.Append("  ").Append(e.Mod.DependencyString).Append(" [").Append(e.Side).AppendLine("]");
            return sb.ToString().TrimEnd();
        }
    }

    /// <summary>The difference between what the server demands and what a client has.</summary>
    public sealed class SyncPlan
    {
        public List<ModRef> Missing { get; } = new List<ModRef>();
        /// <summary>Pairs of (installed, wanted) where the versions differ.</summary>
        public List<KeyValuePair<ModRef, ModRef>> WrongVersion { get; } = new List<KeyValuePair<ModRef, ModRef>>();

        public bool InSync => Missing.Count == 0 && WrongVersion.Count == 0;

        /// <summary>Everything the client needs to download: missing mods plus the wanted version of mismatched ones.</summary>
        public List<ModRef> ToInstall
        {
            get
            {
                var list = new List<ModRef>(Missing);
                foreach (var pair in WrongVersion) list.Add(pair.Value);
                return list;
            }
        }

        /// <summary>
        /// Compare a server manifest against a list of installed mods. Runs identically on the
        /// server (with the client's reported list) and on the client (with its own scan), so
        /// both sides always reach the same verdict.
        /// </summary>
        public static SyncPlan Compare(Manifest server, IEnumerable<ModRef> installed)
        {
            var plan = new SyncPlan();
            var byFullName = new Dictionary<string, ModRef>(StringComparer.OrdinalIgnoreCase);
            foreach (ModRef m in installed)
                if (!byFullName.ContainsKey(m.FullName)) byFullName.Add(m.FullName, m);

            foreach (ManifestEntry entry in server.Entries)
            {
                if (entry.Side == ModSide.Server) continue;                 // clients never need server-only mods
                if (entry.Mod.Name == Manifest.BepInExPackName) continue;    // the loader itself is out of scope

                if (!byFullName.TryGetValue(entry.Mod.FullName, out ModRef have))
                    plan.Missing.Add(entry.Mod);
                else if (!string.Equals(have.Version, entry.Mod.Version, StringComparison.OrdinalIgnoreCase))
                    plan.WrongVersion.Add(new KeyValuePair<ModRef, ModRef>(have, entry.Mod));
            }
            return plan;
        }

        public string Describe()
        {
            if (InSync) return "All required mods match.";
            var sb = new StringBuilder();
            foreach (ModRef m in Missing)
                sb.Append("  missing: ").AppendLine(m.DependencyString);
            foreach (var pair in WrongVersion)
                sb.Append("  wrong version: ").Append(pair.Key.FullName).Append(" have ").Append(pair.Key.Version).Append(", need ").AppendLine(pair.Value.Version);
            return sb.ToString().TrimEnd();
        }

        /// <summary>Short, player-facing summary for popups.</summary>
        public string DescribeForPlayer()
        {
            var lines = new List<string>();
            foreach (ModRef m in Missing) lines.Add(m.FullName + " " + m.Version + " (not installed)");
            foreach (var pair in WrongVersion) lines.Add(pair.Key.FullName + " " + pair.Key.Version + " -> " + pair.Value.Version);
            return string.Join("\n", lines);
        }
    }
}
